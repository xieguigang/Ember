Imports System
Imports System.Globalization
Imports System.IO
Imports Microsoft.VisualBasic.ComponentModel.Settings.Inf
Imports Ollama

''' <summary>
''' Ember 情感陪伴智能体配置模型。
''' 通过 settings.ini（复用 GCModeller Core 的 <see cref="IniFile"/> 模块读写）
''' 管理 LLM 连接信息与智能体运行参数；首次运行自动生成带注释的默认配置文件，
''' 之后程序启动时从该文件恢复配置。
''' </summary>
Public Class EmberConfig

    ''' <summary>LLM 后端类型常量：本地 Ollama 服务</summary>
    Public Const PROVIDER_OLLAMA As String = "ollama"
    ''' <summary>LLM 后端类型常量：OpenAI 兼容 API 服务</summary>
    Public Const PROVIDER_OPENAI As String = "openai"

    Private Const SECTION_LLM As String = "llm"
    Private Const SECTION_AGENT As String = "agent"

    ' ==================== [llm] LLM 连接配置 ====================

    ''' <summary>LLM 后端类型：ollama 或 openai（读取时不区分大小写，其余值按 ollama 处理）</summary>
    Public Property provider As String = PROVIDER_OLLAMA

    ''' <summary>Ollama 服务器地址（host:port）</summary>
    Public Property server As String = "127.0.0.1:11434"

    ''' <summary>对话所用模型名称</summary>
    Public Property model As String = "qwen3:8b"

    ''' <summary>OpenAI 兼容服务的 API 基础地址（provider = openai 时生效）</summary>
    Public Property api_base As String = "https://api.openai.com"

    ''' <summary>OpenAI 兼容服务的 API Key（provider = openai 时生效）</summary>
    Public Property api_key As String = ""

    ''' <summary>对话温度（0~1，情感陪伴对话建议 0.5~0.8，越高回复越有创造性）</summary>
    Public Property temperature As Double = 0.7

    ' ==================== [agent] 智能体运行参数 ====================

    ''' <summary>每 N 轮用户输入后触发一次用户性格画像总结</summary>
    Public Property profile_interval As Integer = 5

    ''' <summary>画像总结时参考的最近对话消息条数上限</summary>
    Public Property recent_window As Integer = 20

    ''' <summary>对话上下文最大 token 估算上限，超过后自动从最旧端裁剪（被裁剪内容仍保留在持久化文件与检索索引中）</summary>
    Public Property max_context_tokens As Integer = 1000000

    ''' <summary>运行时数据目录（相对路径时相对于配置文件所在目录解析）</summary>
    Public Property data_dir As String = "data"

    ''' <summary>是否每轮对话后自动保存对话历史（False 时仅在退出或手动 /save 命令时保存）</summary>
    Public Property autosave As Boolean = True

    ' ==================== 派生路径（运行时数据落盘位置） ====================

    ''' <summary>配置文件绝对路径</summary>
    Public ReadOnly Property IniFilePath As String

    Dim _dataDirectory As String = ""

    ''' <summary>运行时数据目录（绝对路径，已确保创建）</summary>
    Public ReadOnly Property DataDirectory As String
        Get
            Return _dataDirectory
        End Get
    End Property

    ''' <summary>Agent 人设持久化文件（JSON）</summary>
    Public ReadOnly Property PersonaFilePath As String
        Get
            Return Path.Combine(DataDirectory, "agent_persona.json")
        End Get
    End Property

    ''' <summary>用户性格画像持久化文件（JSON）</summary>
    Public ReadOnly Property ProfileFilePath As String
        Get
            Return Path.Combine(DataDirectory, "user_profile.json")
        End Get
    End Property

    ''' <summary>对话历史持久化文件（JSON，由 MemoryPersistsStorage 维护）</summary>
    Public ReadOnly Property ChatHistoryFilePath As String
        Get
            Return Path.Combine(DataDirectory, "chat_history.json")
        End Get
    End Property

    ''' <summary>主对话上下文日志文件（JSONL，由 ChatContextMemory 维护）</summary>
    Public ReadOnly Property ContextLogFilePath As String
        Get
            Return Path.Combine(DataDirectory, "context_log.jsonl")
        End Get
    End Property

    ''' <summary>画像总结客户端上下文日志文件（JSONL）</summary>
    Public ReadOnly Property SummaryLogFilePath As String
        Get
            Return Path.Combine(DataDirectory, "summary_log.jsonl")
        End Get
    End Property

    Private Sub New(iniFilePath As String)
        Me.IniFilePath = iniFilePath
    End Sub

    ''' <summary>
    ''' 加载配置文件；若文件不存在则先生成带注释的默认配置再加载。
    ''' </summary>
    ''' <param name="iniPath">
    ''' 配置文件路径，默认为程序可执行文件所在目录下的 settings.ini。
    ''' </param>
    ''' <returns>解析完成的配置对象（所有键缺失/非法时均安全回退默认值）</returns>
    Public Shared Function LoadOrCreate(Optional iniPath As String = Nothing) As EmberConfig
        If String.IsNullOrWhiteSpace(iniPath) Then
            iniPath = Path.Combine(App.ProductProgramData, "settings.ini")
        End If

        iniPath = Path.GetFullPath(iniPath)

        If Not File.Exists(iniPath) Then
            Call WriteDefaultIni(iniPath)
            Call Console.WriteLine($"[Config] 未找到配置文件，已生成默认配置: {iniPath}")
        End If

        Dim cfg As New EmberConfig(iniPath)
        Call cfg.ReadFromIni()
        Return cfg
    End Function

    ''' <summary>
    ''' 依据 provider 字段构造对应的 LLM 后端提供者实例。
    ''' </summary>
    Public Function CreateProvider() As ILLMProvider
        If String.Equals(provider, PROVIDER_OPENAI, StringComparison.OrdinalIgnoreCase) Then
            Return New OpenAIProvider(api_base, api_key)
        Else
            Return New OllamaProvider(server)
        End If
    End Function

    ''' <summary>
    ''' 后端连接信息描述（用于启动时展示，不泄露 api_key 明文）。
    ''' </summary>
    Public Function DescribeEndpoint() As String
        If String.Equals(provider, PROVIDER_OPENAI, StringComparison.OrdinalIgnoreCase) Then
            Return $"openai 兼容后端 {api_base} / 模型 {model}"
        Else
            Return $"ollama 后端 http://{server} / 模型 {model}"
        End If
    End Function

    ' ==================== ini 读写内部实现 ====================

    ''' <summary>
    ''' 从 ini 文件读取全部配置项；单个键解析失败时保留默认值，不中断启动。
    ''' </summary>
    Private Sub ReadFromIni()
        Using ini As New IniFile(IniFilePath)
            provider = NormalizeProvider(ini.ReadValue(SECTION_LLM, NameOf(provider), provider))
            server = ini.ReadString(SECTION_LLM, NameOf(server), server)
            model = ini.ReadString(SECTION_LLM, NameOf(model), model)
            api_base = ini.ReadString(SECTION_LLM, NameOf(api_base), api_base)
            api_key = ini.ReadString(SECTION_LLM, NameOf(api_key), api_key)
            temperature = ini.ReadDouble(SECTION_LLM, NameOf(temperature), temperature)

            profile_interval = ini.ReadInt32(SECTION_AGENT, NameOf(profile_interval), profile_interval)
            recent_window = ini.ReadInt32(SECTION_AGENT, NameOf(recent_window), recent_window)
            max_context_tokens = ini.ReadInt32(SECTION_AGENT, NameOf(max_context_tokens), max_context_tokens)
            data_dir = ini.ReadString(SECTION_AGENT, NameOf(data_dir), data_dir)
            autosave = ini.ReadBoolean(SECTION_AGENT, NameOf(autosave), autosave)
        End Using

        ' 数值参数合法性保护
        If profile_interval < 1 Then profile_interval = 1
        If recent_window < 2 Then recent_window = 2
        If max_context_tokens < 4096 Then max_context_tokens = 4096
        If temperature < 0 Then temperature = 0
        If temperature > 2 Then temperature = 2
        If String.IsNullOrWhiteSpace(model) Then model = "qwen3:8b"

        ' 解析数据目录（相对路径相对于配置文件所在目录），并确保目录存在
        Dim dir As String = data_dir
        If String.IsNullOrWhiteSpace(dir) Then dir = "data"
        If Not Path.IsPathRooted(dir) Then
            dir = Path.Combine(Path.GetDirectoryName(IniFilePath), dir)
        End If

        _dataDirectory = Path.GetFullPath(dir)
        Call Directory.CreateDirectory(_dataDirectory)
    End Sub

    ''' <summary>生成带注释的默认配置文件（目录不存在时自动创建）。</summary>
    Private Shared Sub WriteDefaultIni(iniPath As String)
        Call Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(iniPath)))

        Using ini As New IniFile(iniPath)
            Call ini.comments.Add("Ember 情感陪伴智能体配置文件")
            Call ini.comments.Add("首次启动自动生成；手动修改后重启程序生效")

            Call ini.WriteValue(SECTION_LLM, NameOf(provider), PROVIDER_OLLAMA, "LLM 后端类型: ollama 或 openai")
            Call ini.WriteValue(SECTION_LLM, NameOf(server), "127.0.0.1:11434", "Ollama 服务器地址 (host:port)，provider 为 ollama 时生效")
            Call ini.WriteValue(SECTION_LLM, NameOf(model), "qwen3:8b", "对话所用模型名称，可执行: ollama pull <model> 拉取模型")
            Call ini.WriteValue(SECTION_LLM, NameOf(api_base), "https://api.openai.com", "OpenAI 兼容服务地址，provider 为 openai 时生效")
            Call ini.WriteValue(SECTION_LLM, NameOf(api_key), "", "OpenAI 兼容服务的 API Key，provider 为 openai 时生效")
            Call ini.WriteValue(SECTION_LLM, NameOf(temperature), "0.7", "对话温度 0~1，情感陪伴建议 0.5~0.8")

            Call ini.WriteValue(SECTION_AGENT, NameOf(profile_interval), "5", "每 N 轮用户输入触发一次用户性格画像总结")
            Call ini.WriteValue(SECTION_AGENT, NameOf(recent_window), "20", "画像总结时参考的最近对话消息条数上限")
            Call ini.WriteValue(SECTION_AGENT, NameOf(max_context_tokens), "1000000", "对话上下文最大 token 估算上限，超过后自动裁剪最旧消息")
            Call ini.WriteValue(SECTION_AGENT, NameOf(data_dir), "data", "运行时数据目录（人设/画像/对话历史），相对路径基于本配置文件所在目录")
            Call ini.WriteValue(SECTION_AGENT, NameOf(autosave), "true", "true 时每轮对话后自动保存；false 仅在退出或 /save 时保存")

            Call ini.Flush()
        End Using
    End Sub

    Private Shared Function NormalizeProvider(value As String) As String
        If String.Equals(value, PROVIDER_OPENAI, StringComparison.OrdinalIgnoreCase) Then
            Return PROVIDER_OPENAI
        Else
            Return PROVIDER_OLLAMA
        End If
    End Function
End Class
