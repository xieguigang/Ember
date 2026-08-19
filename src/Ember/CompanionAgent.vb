Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports System.Text
Imports System.Threading.Tasks
Imports Ollama

''' <summary>
''' 情感陪伴智能体核心编排器。
''' 采用双客户端架构：
''' 1. 主对话客户端（preserveMemory:=True）：维护完整多轮对话上下文，其上下文由
'''    <see cref="MemoryPersistsStorage"/> 挂载实现对话历史的落盘与重载；
''' 2. 画像总结客户端（preserveMemory:=False）：以一次性 prompt 方式调用 LLM，
'''    定期从对话记忆中总结用户性格画像，不会污染主对话上下文。
''' 系统提示词 = Agent 人设 + 用户画像 + 语气适配指令，画像更新后下一轮对话即生效。
''' </summary>
Public Class CompanionAgent : Implements IDisposable

    ''' <summary>总结客户端所用的固定系统提示词</summary>
    Private Const SUMMARY_SYSTEM_PROMPT As String =
        "你是一个用户画像分析引擎。你的唯一任务是根据给定的对话记录，" &
        "客观地总结用户的性格画像，并严格按照要求的 JSON 格式输出，不输出任何其他内容。"

    ''' <summary>画像总结时每条消息截断的最大字符数（防止超长消息撑爆总结 prompt）</summary>
    Private Const MAX_MESSAGE_CHARS As Integer = 500

    ''' <summary>画像总结时从长期记忆中最多召回的消息条数</summary>
    Private Const MAX_RECALL As Integer = 5

    ''' <summary>画像总结时用于长期记忆检索的最近用户消息条数</summary>
    Private Const RECALL_KEYWORD_MESSAGES As Integer = 3

    ReadOnly _config As EmberConfig
    ReadOnly _mainClient As LLMClient
    ReadOnly _sumClient As LLMClient
    ReadOnly _storage As MemoryPersistsStorage

    Dim _persona As AgentPersona
    Dim _profile As UserProfile
    Dim _personaDirty As Boolean
    Dim _profileDirty As Boolean
    Dim _userTurnCount As Integer

    ''' <summary>主对话客户端（暴露用于状态展示等只读用途）</summary>
    Public ReadOnly Property MainClient As LLMClient
        Get
            Return _mainClient
        End Get
    End Property

    ''' <summary>当前 Agent 人设</summary>
    Public ReadOnly Property Persona As AgentPersona
        Get
            Return _persona
        End Get
    End Property

    ''' <summary>当前用户性格画像</summary>
    Public ReadOnly Property Profile As UserProfile
        Get
            Return _profile
        End Get
    End Property

    ''' <summary>累计用户对话轮次</summary>
    Public ReadOnly Property UserTurnCount As Integer
        Get
            Return _userTurnCount
        End Get
    End Property

    Private Sub New(config As EmberConfig, mainClient As LLMClient, sumClient As LLMClient)
        _config = config
        _mainClient = mainClient
        _sumClient = sumClient

        ' 加载持久化的个性化数据（缺失/损坏均安全回退默认值）
        Dim personaIsDefault As Boolean
        _persona = AgentPersona.Load(config.PersonaFilePath, personaIsDefault)
        _personaDirty = Not personaIsDefault   ' 用户自定义人设立即回写落盘，防止异常退出丢失
        _profile = UserProfile.Load(config.ProfileFilePath)
        _profileDirty = False
        _userTurnCount = 0

        ' 将持久化门面挂载到主对话客户端的上下文，并从文件恢复历史对话
        _storage = New MemoryPersistsStorage(mainClient.Context, config.ChatHistoryFilePath)
        Call _storage.Load()

        Call RefreshSystemPrompt()
    End Sub

    ''' <summary>
    ''' 创建智能体实例：构造主/总结双客户端，加载人设、画像与对话历史。
    ''' </summary>
    ''' <param name="config">已加载的配置对象</param>
    ''' <param name="restoredMessages">输出：本次启动从持久化文件恢复的历史消息条数</param>
    Public Shared Function Create(config As EmberConfig, Optional ByRef restoredMessages As Integer = 0) As CompanionAgent
        ' 主对话客户端：保留完整对话记忆，温度取自配置
        Dim mainClient As New LLMClient(
            provider:=config.CreateProvider(),
            model:=config.model,
            logfile:=config.ContextLogFilePath,
            preserveMemory:=True)
        mainClient.temperature = config.temperature
        mainClient.max_context_tokens = config.max_context_tokens

        ' 画像总结客户端：不保留记忆（一次性请求），低温度保证输出稳定
        Dim sumClient As New LLMClient(
            provider:=config.CreateProvider(),
            model:=config.model,
            logfile:=config.SummaryLogFilePath,
            preserveMemory:=False)
        sumClient.temperature = 0.2
        sumClient.system_message = SUMMARY_SYSTEM_PROMPT

        Dim agent As New CompanionAgent(config, mainClient, sumClient)
        restoredMessages = mainClient.Context.Count
        Return agent
    End Function

    ''' <summary>
    ''' 重组系统提示词（人设 + 用户画像 + 语气适配指令）并应用到主对话客户端。
    ''' 画像或人设更新后调用，下一轮对话立即生效。
    ''' </summary>
    Public Sub RefreshSystemPrompt()
        _mainClient.system_message = BuildSystemPrompt()
    End Sub

    ''' <summary>
    ''' 组装主对话系统提示词：Agent 人设 + 用户画像 + 语气适配指令。
    ''' </summary>
    Private Function BuildSystemPrompt() As String
        Dim sb As New StringBuilder()

        ' 1. Agent 人设
        Call sb.AppendLine(_persona.Description.Trim())
        Call sb.AppendLine()

        ' 2. 用户画像（若已有总结结果）
        Dim profileText As String = _profile.ToPromptText()
        If Not String.IsNullOrEmpty(profileText) Then
            Call sb.AppendLine("【你长期陪伴的这位用户的画像（根据历史对话总结）】")
            Call sb.AppendLine(profileText)
            Call sb.AppendLine()
            Call sb.AppendLine("【语气适配要求】")
            Call sb.AppendLine("请根据以上用户画像调整你的对话语气与方式：")
            Call sb.AppendLine("- 优先按照用户的沟通偏好选择表达方式（细腻/简洁/幽默等）；")
            Call sb.AppendLine("- 关注用户的近期情绪状态，若偏消极则给予更多共情与支持，若积极则真诚地为 TA 开心；")
            Call sb.AppendLine("- 围绕用户的兴趣话题自然地展开与延伸对话；")
            Call sb.AppendLine("- 若画像与用户当下表达出现矛盾，以用户当下的表达为准。")
        Else
            Call sb.AppendLine("【语气要求】")
            Call sb.AppendLine("你还不了解这位用户，请保持自然、温和、真诚的语气，在陪伴中慢慢了解 TA。")
        End If

        Return sb.ToString().Trim()
    End Function

    ''' <summary>
    ''' 与用户进行一轮对话：发送消息、流式输出回复、计数轮次、按配置自动保存，
    ''' 每达到 profile_interval 轮时自动触发一次用户画像总结并刷新系统提示词。
    ''' </summary>
    ''' <param name="userInput">用户输入的文本</param>
    ''' <returns>LLM 回复正文；网络持续失败等异常时返回空字符串并给出提示（不中断会话）</returns>
    Public Async Function ChatAsync(userInput As String) As Task(Of String)
        Dim response As LLMsResponse = Nothing

        Try
            ' LLMClient 内部已将 think/output 增量直接 Console.Write 流式输出，
            ' 此处只需在回复结束后补一个换行分隔
            response = Await _mainClient.Chat(userInput)
            Call Console.WriteLine()
            Call Console.WriteLine()
        Catch ex As Exception
            Call Console.Error.WriteLine($"[对话失败] {ex.Message}")
            Call Console.Error.WriteLine("可以稍后重试，本轮对话未计入画像总结轮次。")
            Return ""
        End Try

        _userTurnCount += 1

        ' 每轮自动保存对话历史（情感对话数据珍贵，避免异常退出丢失）
        If _config.autosave Then
            Call SaveAll()
        End If

        ' 周期性总结用户画像并动态调整后续语气
        If _userTurnCount Mod _config.profile_interval = 0 Then
            Try
                Call Await UpdateProfileAsync()
            Catch ex As Exception
                Call Console.Error.WriteLine($"[画像总结失败] {ex.Message}（不影响对话，下个周期重试）")
            End Try
        End If

        Return If(response, New LLMsResponse).output
    End Function

    ''' <summary>
    ''' 触发一次用户画像总结：输入 = 当前画像 + 最近对话窗口 + 长期记忆召回，
    ''' 由独立总结客户端输出结构化 JSON 画像；解析成功则更新画像、刷新系统提示词并落盘。
    ''' </summary>
    Public Async Function UpdateProfileAsync() As Task(Of Boolean)
        ' 1. 取最近对话窗口
        Dim recent As List(Of ChatMessage) = TakeRecentMessages(_config.recent_window)
        If recent.Count = 0 Then
            Return False
        End If

        Call Console.WriteLine("[系统] 正在根据对话记忆总结你的性格画像…")

        ' 2. 基于最近用户消息 + 既有画像关键词，从长期记忆中模糊召回相关片段
        Dim recalled As ChatMessage() = RecallLongTermMemory(recent)

        ' 3. 构造总结 prompt 并请求总结客户端
        Dim prompt As String = BuildSummaryPrompt(recent, recalled)
        Dim response As LLMsResponse = Await _sumClient.Chat(prompt)

        ' 4. 容错解析 JSON 画像；失败则保留旧画像
        Dim newProfile As UserProfile = UserProfile.FromLlmJson(response.output)

        If newProfile Is Nothing Then
            Call Console.WriteLine("[系统] 本轮画像总结结果无法解析，已保留原画像。")
            Return False
        End If

        _profile = newProfile
        _profileDirty = True
        Call RefreshSystemPrompt()
        Call _profile.Save(_config.ProfileFilePath)
        _profileDirty = False

        Call Console.WriteLine("[系统] 画像已更新，从下一轮对话开始我会用更适合你的方式和你聊天。")
        Call Console.WriteLine()
        Return True
    End Function

    ''' <summary>
    ''' 从主对话上下文导出最近 N 条有效对话消息（仅 user/assistant，逐条截断防超长）。
    ''' </summary>
    Private Function TakeRecentMessages(count As Integer) As List(Of ChatMessage)
        Dim messages As List(Of ChatMessage) = _mainClient.Context.ExportMessages()
        Dim recent As New List(Of ChatMessage)

        For i As Integer = messages.Count - 1 To 0 Step -1
            If recent.Count >= count Then Exit For

            Dim msg As ChatMessage = messages(i)
            If msg Is Nothing Then Continue For
            If msg.Role <> "user" AndAlso msg.Role <> "assistant" Then Continue For
            If String.IsNullOrWhiteSpace(msg.Content) Then Continue For

            recent.Insert(0, msg)
        Next

        Return recent
    End Function

    ''' <summary>
    ''' 基于最近用户消息与画像关键词，从持久化全文索引中召回相关长期记忆。
    ''' </summary>
    Private Function RecallLongTermMemory(recent As List(Of ChatMessage)) As ChatMessage()
        Try
            Dim keywords As New List(Of String)

            ' 最近几条用户消息原文作为查询（Search 内部会统一 Tokenize 切词）
            For Each msg In recent.Where(Function(m) m.Role = "user").Take(RECALL_KEYWORD_MESSAGES)
                Call keywords.Add(msg.Content)
            Next
            Call keywords.AddRange(_profile.GetKeywords())

            Return _storage.RecallMessages(keywords, top:=MAX_RECALL).ToArray()
        Catch ex As Exception
            ' 长期记忆召回是增强项，失败不应影响画像总结主流程
            Call Console.Error.WriteLine($"[记忆召回失败] {ex.Message}")
            Return New ChatMessage() {}
        End Try
    End Function

    ''' <summary>
    ''' 构造画像总结 prompt：当前画像 + 最近对话 + 召回的长期记忆 + 严格 JSON 输出格式要求。
    ''' </summary>
    Private Function BuildSummaryPrompt(recent As List(Of ChatMessage), recalled As ChatMessage()) As String
        Dim sb As New StringBuilder()

        Call sb.AppendLine("请根据以下对话信息，总结并更新这位用户的性格画像。")

        ' 当前已有画像
        Call sb.AppendLine()
        Call sb.AppendLine("【当前已知画像】")
        Dim current As String = _profile.ToPromptText()
        Call sb.AppendLine(If(String.IsNullOrEmpty(current), "（暂无，这是首次总结）", current))

        ' 最近对话
        Call sb.AppendLine()
        Call sb.AppendLine("【最近对话记录】")
        For Each msg In recent
            Dim content As String = msg.Content.Trim().Replace(vbCrLf, " ")
            If content.Length > MAX_MESSAGE_CHARS Then
                content = content.Substring(0, MAX_MESSAGE_CHARS) & "…"
            End If
            Call sb.AppendLine($"{msg.Role}: {content}")
        Next

        ' 长期记忆召回（与最近对话去重展示）
        If recalled IsNot Nothing AndAlso recalled.Length > 0 Then
            Call sb.AppendLine()
            Call sb.AppendLine("【相关的更早期记忆】")
            For Each msg In recalled
                Dim content As String = msg.Content.Trim().Replace(vbCrLf, " ")
                If content.Length > MAX_MESSAGE_CHARS Then
                    content = content.Substring(0, MAX_MESSAGE_CHARS) & "…"
                End If
                Call sb.AppendLine($"{msg.Role}: {content}")
            Next
        End If

        ' 输出格式要求（字段名与 UserProfile 属性严格一致，DataContractJsonSerializer 区分大小写）
        Call sb.AppendLine()
        Call sb.AppendLine("请只输出如下格式的 JSON 对象，不要输出任何解释文字或 markdown 代码块标记：")
        Call sb.AppendLine("{")
        Call sb.AppendLine("  ""Summary"": ""用户性格的总体概要，一两句话"",")
        Call sb.AppendLine("  ""Traits"": [""性格特征关键词"", ""可多个""],")
        Call sb.AppendLine("  ""Interests"": [""感兴趣的话题"", ""可多个""],")
        Call sb.AppendLine("  ""EmotionalState"": ""用户近期的主要情绪状态"",")
        Call sb.AppendLine("  ""CommunicationStyle"": ""用户偏好的沟通方式，例如：喜欢温暖细腻的关怀 / 喜欢简洁直接 / 喜欢幽默轻松""")
        Call sb.AppendLine("}")

        Return sb.ToString()
    End Function

    ''' <summary>
    ''' 生成开场问候语：临时借用无记忆的总结客户端（不污染主对话上下文与持久化记忆），
    ''' 请求前后切换其系统提示词，生成结果仅打印给用户。
    ''' </summary>
    Public Async Function GreetAsync() As Task(Of String)
        Dim savedSystem As String = _sumClient.system_message

        _sumClient.system_message =
            _persona.Description.Trim() & vbCrLf &
            "请始终以这个人设的身份说话。"

        Try
            Dim response As LLMsResponse = Await _sumClient.Chat(
                "这是陪伴会话的开始。请以你的人设身份，用一两句自然、温暖的话向用户打招呼，" &
                "开启今天的陪伴。只输出打招呼的内容本身，不要任何解释。")
            Return If(response, New LLMsResponse).output
        Catch ex As Exception
            Call Console.Error.WriteLine($"[问候生成失败] {ex.Message}")
            Return ""
        Finally
            _sumClient.system_message = savedSystem
        End Try
    End Function

    ' ==================== 用户命令支持 ====================

    ''' <summary>
    ''' 设置/覆盖 Agent 人设（/persona set 命令后端）：即时生效并持久化。
    ''' </summary>
    Public Sub SetPersona(description As String)
        If _persona Is Nothing Then _persona = AgentPersona.CreateDefault()
        _persona.Description = description.Trim()
        _personaDirty = True

        Call RefreshSystemPrompt()
        Call _persona.Save(_config.PersonaFilePath)
        _personaDirty = False
    End Sub

    ''' <summary>
    ''' 重置为内置默认人设（/persona reset 命令后端）。
    ''' </summary>
    Public Sub ResetPersona()
        _persona = AgentPersona.CreateDefault()
        _personaDirty = True

        Call RefreshSystemPrompt()
        Call _persona.Save(_config.PersonaFilePath)
        _personaDirty = False
    End Sub

    ''' <summary>
    ''' 获取运行状态摘要（/status 命令后端）。
    ''' </summary>
    Public Function GetStatusText() As String
        Dim sb As New StringBuilder()

        Call sb.AppendLine($"后端: {_config.DescribeEndpoint()}")
        Call sb.AppendLine($"温度: {_mainClient.temperature}")
        Call sb.AppendLine($"对话轮次: {_userTurnCount}")
        Call sb.AppendLine($"上下文消息数: {_mainClient.Context.Count}")
        Call sb.AppendLine($"上下文 token 估算: {_mainClient.Context.EstimatedTokens} / {_config.max_context_tokens}")
        Call sb.AppendLine($"人设: {_persona.Name}{If(_persona.IsDefault, "（默认）", "（自定义）")}")
        Call sb.AppendLine($"用户画像: {If(_profile.IsEmpty, "（尚未总结）", $"已更新于 {_profile.UpdatedAt}")}")
        Call sb.AppendLine($"自动保存: {If(_config.autosave, "每轮", "仅退出/手动")}")
        Call sb.AppendLine($"数据目录: {_config.DataDirectory}")

        Return sb.ToString().Trim()
    End Function

    ' ==================== 持久化 ====================

    ''' <summary>
    ''' 保存全部持久化数据：对话历史（含全文索引重建）、人设与用户画像。
    ''' </summary>
    Public Sub SaveAll()
        Call _storage.Save()
        If _personaDirty Then
            If _persona.Save(_config.PersonaFilePath) Then _personaDirty = False
        End If
        If _profileDirty Then
            If _profile.Save(_config.ProfileFilePath) Then _profileDirty = False
        End If
    End Sub

    ' ==================== IDisposable ====================

    Private disposedValue As Boolean

    Protected Overridable Sub Dispose(disposing As Boolean)
        If Not disposedValue Then
            If disposing Then
                Try
                    Call SaveAll()
                Catch ex As Exception
                    Call Console.Error.WriteLine($"[退出保存失败] {ex.Message}")
                Finally
                    Call _mainClient.Dispose()
                    Call _sumClient.Dispose()
                    Call _storage.Dispose()
                End Try
            End If

            disposedValue = True
        End If
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Call Dispose(True)
        GC.SuppressFinalize(Me)
    End Sub
End Class
