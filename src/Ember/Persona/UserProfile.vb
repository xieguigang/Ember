Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports Microsoft.VisualBasic.Serialization.JSON

''' <summary>
''' 用户性格画像模型：由总结客户端根据对话记忆持续提炼的用户特征快照。
''' 画像字段将注入 system prompt 供主对话客户端据此调整与用户对话的语气；
''' 画像本身也会作为 JSON 文件持久化，重启后自动恢复。
''' </summary>
Public Class UserProfile

    ''' <summary>用户性格总体概要（一段自然语言描述）</summary>
    Public Property Summary As String = ""

    ''' <summary>用户性格特征要点列表</summary>
    Public Property Traits As New List(Of String)

    ''' <summary>用户兴趣爱好列表</summary>
    Public Property Interests As New List(Of String)

    ''' <summary>用户近期情绪状态描述</summary>
    Public Property EmotionalState As String = ""

    ''' <summary>用户偏好的沟通方式（影响语气适配）</summary>
    Public Property CommunicationStyle As String = ""

    ''' <summary>最近一次画像更新时间（ISO 8601 字符串）</summary>
    Public Property UpdatedAt As String = ""

    ''' <summary>画像是否为空（尚无任何总结结果）</summary>
    Public ReadOnly Property IsEmpty As Boolean
        Get
            Return String.IsNullOrWhiteSpace(Summary) _
                AndAlso (Traits Is Nothing OrElse Traits.Count = 0) _
                AndAlso (Interests Is Nothing OrElse Interests.Count = 0) _
                AndAlso String.IsNullOrWhiteSpace(EmotionalState) _
                AndAlso String.IsNullOrWhiteSpace(CommunicationStyle)
        End Get
    End Property

    ''' <summary>
    ''' 从 JSON 文件加载画像；文件不存在或损坏时返回空画像（记录错误但不中断启动）。
    ''' </summary>
    Public Shared Function Load(filePath As String) As UserProfile
        If Not File.Exists(filePath) Then
            Return New UserProfile()
        End If

        Try
            Dim profile As UserProfile = LoadJsonFile(Of UserProfile)(file:=filePath, simpleDict:=True)
            Return If(profile, New UserProfile())
        Catch ex As Exception
            Call Console.Error.WriteLine($"[Profile] 用户画像文件加载失败，已回退空画像: {ex.Message}")
            Return New UserProfile()
        End Try
    End Function

    ''' <summary>
    ''' 将当前画像保存为 JSON 文件；目录不存在时自动创建。
    ''' </summary>
    Public Function Save(filePath As String) As Boolean
        Try
            UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            Call Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath)))
            Call File.WriteAllText(filePath, Me.GetJson(indent:=True, simpleDict:=True), Text.Encoding.UTF8)
            Return True
        Catch ex As Exception
            Call Console.Error.WriteLine($"[Profile] 用户画像保存失败: {ex.Message}")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' LLM 常见中文键名 → 英文属性名映射（供 <see cref="FromLlmJson"/> 归一化兜底使用）。
    ''' </summary>
    Private Shared ReadOnly FieldNameMap As New Dictionary(Of String, String) From {
        {"总体印象", NameOf(Summary)},
        {"总体概要", NameOf(Summary)},
        {"性格概要", NameOf(Summary)},
        {"性格特征", NameOf(Traits)},
        {"性格特点", NameOf(Traits)},
        {"特征", NameOf(Traits)},
        {"兴趣爱好", NameOf(Interests)},
        {"兴趣偏好", NameOf(Interests)},
        {"兴趣话题", NameOf(Interests)},
        {"兴趣", NameOf(Interests)},
        {"近期情绪", NameOf(EmotionalState)},
        {"情绪状态", NameOf(EmotionalState)},
        {"近期情绪状态", NameOf(EmotionalState)},
        {"沟通偏好", NameOf(CommunicationStyle)},
        {"沟通方式", NameOf(CommunicationStyle)},
        {"偏好的沟通方式", NameOf(CommunicationStyle)}
    }

    ''' <summary>
    ''' 从 LLM 总结输出容错解析画像：优先按 JSON 解析（含中文键名归一化与嵌套解包），
    ''' 失败后回退按"键: 值"行文本格式解析（小模型对行格式的遵循度远高于 JSON）。
    ''' </summary>
    ''' <param name="llmOutput">LLM 原始输出文本</param>
    ''' <returns>解析成功且非空画像返回新画像对象；失败返回 Nothing（调用方保留旧画像）</returns>
    Public Shared Function FromLlmOutput(llmOutput As String) As UserProfile
        If String.IsNullOrWhiteSpace(llmOutput) Then
            Return Nothing
        End If

        ' 剥离 markdown 代码块包裹（```json ... ```）
        Dim text As String = llmOutput.Trim()
        If text.StartsWith("```") Then
            Dim firstLineEnd As Integer = text.IndexOf(vbLf)
            If firstLineEnd > 0 Then text = text.Substring(firstLineEnd + 1)
            Dim fenceEnd As Integer = text.LastIndexOf("```", StringComparison.Ordinal)
            If fenceEnd >= 0 Then text = text.Substring(0, fenceEnd)
            text = text.Trim()
        End If

        ' 含 "{" 时优先按 JSON 解析；失败再回退行文本（容忍混杂输出）
        If text.Contains("{"c) Then
            Dim byJson As UserProfile = TryParseJson(text)
            If byJson IsNot Nothing Then
                Return byJson
            End If
        End If

        Return TryParseLines(text)
    End Function

    ''' <summary>
    ''' 尝试按 JSON 对象解析画像：提取 { ... } 片段 → 嵌套单键包装解包 →
    ''' 中文键名与大小写变体归一化 → DataContractJsonSerializer 反序列化。
    ''' </summary>
    Private Shared Function TryParseJson(text As String) As UserProfile
        ' 提取第一个 { 与最后一个 } 之间的 JSON 片段，容忍 LLM 前后输出的说明文字
        Dim start As Integer = text.IndexOf("{"c)
        Dim [end] As Integer = text.LastIndexOf("}"c)
        If start < 0 OrElse [end] <= start Then
            Return Nothing
        End If
        text = text.Substring(start, [end] - start + 1)

        ' 嵌套单键包装解包：{"user_profile": {...}} → {...}
        Dim wrapped As System.Text.RegularExpressions.Match = System.Text.RegularExpressions.Regex.Match(
            text, "^\s*\{\s*""[^""]+""\s*:\s*(\{.*\})\s*\}\s*$",
            System.Text.RegularExpressions.RegexOptions.Singleline)
        If wrapped.Success Then
            text = wrapped.Groups(1).Value
        End If

        ' 中文键名 → 英文属性名归一化（文本层替换，无法识别的键会被反序列化安全忽略）
        For Each mapping In FieldNameMap
            text = text.Replace($"""{mapping.Key}""", $"""{mapping.Value}""")
        Next

        ' 英文键名大小写变体归一化（DataContractJsonSerializer 严格区分大小写）
        For Each eng As String In {"Summary", "Traits", "Interests", "EmotionalState", "CommunicationStyle"}
            text = text.Replace($"""{eng.ToLower()}""", $"""{eng}""")
            text = text.Replace($"""{eng.ToUpper()}""", $"""{eng}""")
            text = text.Replace($"""{Char.ToLower(eng(0)) & eng.Substring(1)}""", $"""{eng}""")
        Next

        Try
            Dim profile As UserProfile = text.LoadJSON(Of UserProfile)(simpleDict:=True, throwEx:=False)
            Return CheckProfile(profile)
        Catch ex As Exception
            Call Console.Error.WriteLine($"[Profile] 画像 JSON 解析失败: {ex.Message}")
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' 尝试按"键: 值"行文本格式解析画像（每行一个字段，列表值以逗号/顿号/分号分隔）。
    ''' </summary>
    Private Shared Function TryParseLines(text As String) As UserProfile
        Dim profile As New UserProfile()
        Dim matched As Integer = 0

        ' 模型可能将多个字段挤在同一行输出（如 "Traits: a,bInterests: x,y"），
        ' 先在行内嵌入的字段标记前补换行再做行解析
        text = BreakInlineFields(text)

        For Each line As String In text.Split({vbCrLf, vbLf, vbCr}, StringSplitOptions.RemoveEmptyEntries)
            Dim idx As Integer = line.IndexOf(":"c)
            Dim idxCn As Integer = line.IndexOf("："c)
            If idxCn >= 0 AndAlso (idx < 0 OrElse idxCn < idx) Then idx = idxCn
            If idx <= 0 Then Continue For

            Dim key As String = line.Substring(0, idx).Trim().Trim(""""c, "*"c, "#"c, "-"c).Trim()
            Dim value As String = line.Substring(idx + 1).Trim()

            If value.Length = 0 Then Continue For

            Select Case NormalizeKey(key)
                Case NameOf(Summary)
                    profile.Summary = CleanValue(value) : matched += 1
                Case NameOf(Traits)
                    profile.Traits = SplitList(value) : matched += 1
                Case NameOf(Interests)
                    profile.Interests = SplitList(value) : matched += 1
                Case NameOf(EmotionalState)
                    profile.EmotionalState = CleanValue(value) : matched += 1
                Case NameOf(CommunicationStyle)
                    profile.CommunicationStyle = CleanValue(value) : matched += 1
            End Select
        Next

        ' 至少识别出两个字段才认为是有效画像，防止单条噪音行误判
        If matched < 2 Then
            Return Nothing
        End If

        Return CheckProfile(profile)
    End Function

    ''' <summary>
    ''' 将同一行内嵌入的多个字段标记拆分为独立行：
    ''' 利用零宽正向先行断言在每个字段标记（英文键或中文映射键 + 冒号）前切分。
    ''' </summary>
    Private Shared Function BreakInlineFields(text As String) As String
        ' 收集全部字段标记：英文属性名 + 中文映射键名，构造正则交替分支（需转义）
        Dim markers As String() = {"Summary", "Traits", "Interests", "EmotionalState", "CommunicationStyle"} _
            .Concat(FieldNameMap.Keys) _
            .Select(AddressOf System.Text.RegularExpressions.Regex.Escape) _
            .ToArray()
        Dim pattern As String = "(?=(" & String.Join("|", markers) & ")\s*[:：])"

        Dim sb As New System.Text.StringBuilder()

        For Each line As String In text.Split({vbCrLf, vbLf, vbCr}, StringSplitOptions.RemoveEmptyEntries)
            For Each seg As String In System.Text.RegularExpressions.Regex.Split(line.Trim(), pattern)
                If Not String.IsNullOrWhiteSpace(seg) Then
                    Call sb.AppendLine(seg.Trim())
                End If
            Next
        Next

        Return sb.ToString()
    End Function

    ''' <summary>
    ''' 键名归一化：英文键大小写不敏感匹配 + 中文键名映射。
    ''' </summary>
    Private Shared Function NormalizeKey(key As String) As String
        If String.IsNullOrWhiteSpace(key) Then Return Nothing

        For Each eng As String In {"Summary", "Traits", "Interests", "EmotionalState", "CommunicationStyle"}
            If String.Equals(key, eng, StringComparison.OrdinalIgnoreCase) Then
                Return eng
            End If
        Next

        Dim mapped As String = Nothing
        If FieldNameMap.TryGetValue(key, mapped) Then
            Return mapped
        End If

        Return Nothing
    End Function

    ''' <summary>清理值文本中的引号、句末标点与前后空白。</summary>
    Private Shared Function CleanValue(value As String) As String
        Return value.Trim().Trim(""""c, "'"c, ","c, "，"c, "."c, "。"c).Trim()
    End Function

    ''' <summary>将逗号/顿号/分号分隔的列表值拆分为字符串列表。</summary>
    Private Shared Function SplitList(value As String) As List(Of String)
        Dim items As New List(Of String)

        For Each item As String In value.Split({",", "，", "、", ";", "；"}, StringSplitOptions.RemoveEmptyEntries)
            Dim clean As String = CleanValue(item)
            If clean.Length > 0 Then
                Call items.Add(clean)
            End If
        Next

        Return items
    End Function

    ''' <summary>校验解析结果非空并做列表字段空值保护。</summary>
    Private Shared Function CheckProfile(profile As UserProfile) As UserProfile
        If profile Is Nothing OrElse profile.IsEmpty Then
            Return Nothing
        End If

        If profile.Traits Is Nothing Then profile.Traits = New List(Of String)
        If profile.Interests Is Nothing Then profile.Interests = New List(Of String)

        Return profile
    End Function

    ''' <summary>
    ''' 生成用于注入 system prompt 的画像描述文本（空画像返回空字符串）。
    ''' </summary>
    Public Function ToPromptText() As String
        If IsEmpty Then
            Return ""
        End If

        Dim parts As New List(Of String)

        If Not String.IsNullOrWhiteSpace(Summary) Then
            parts.Add("总体印象：" & Summary.Trim())
        End If
        If Traits IsNot Nothing AndAlso Traits.Count > 0 Then
            parts.Add("性格特征：" & String.Join("；", Traits))
        End If
        If Interests IsNot Nothing AndAlso Interests.Count > 0 Then
            parts.Add("兴趣话题：" & String.Join("；", Interests))
        End If
        If Not String.IsNullOrWhiteSpace(EmotionalState) Then
            parts.Add("近期情绪：" & EmotionalState.Trim())
        End If
        If Not String.IsNullOrWhiteSpace(CommunicationStyle) Then
            parts.Add("沟通偏好：" & CommunicationStyle.Trim())
        End If

        Return String.Join(vbCrLf, parts)
    End Function

    ''' <summary>
    ''' 提取画像中的关键词（性格特征 + 兴趣话题条目），供 MemoryPersistsStorage 长期记忆模糊检索使用。
    ''' </summary>
    Public Function GetKeywords() As String()
        Dim words As New List(Of String)

        If Traits IsNot Nothing Then words.AddRange(Traits)
        If Interests IsNot Nothing Then words.AddRange(Interests)
        If Not String.IsNullOrWhiteSpace(EmotionalState) Then words.Add(EmotionalState.Trim())

        Return words.ToArray()
    End Function
End Class
