Imports System
Imports System.Collections.Generic
Imports System.IO
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
    ''' 从 LLM 总结输出的 JSON 文本容错解析画像。
    ''' </summary>
    ''' <param name="json">LLM 输出的 JSON 文本（可包含 markdown 代码块包裹）</param>
    ''' <returns>解析成功且非空画像返回新画像对象；解析失败或结果为空时返回 Nothing（调用方保留旧画像）</returns>
    Public Shared Function FromLlmJson(json As String) As UserProfile
        If String.IsNullOrWhiteSpace(json) Then
            Return Nothing
        End If

        ' 剥离 markdown 代码块包裹（```json ... ```）
        Dim text As String = json.Trim()
        If text.StartsWith("```") Then
            Dim firstLineEnd As Integer = text.IndexOf(vbLf)
            If firstLineEnd > 0 Then text = text.Substring(firstLineEnd + 1)
            Dim fenceEnd As Integer = text.LastIndexOf("```", StringComparison.Ordinal)
            If fenceEnd >= 0 Then text = text.Substring(0, fenceEnd)
            text = text.Trim()
        End If

        ' 提取第一个 { ... } JSON 对象片段，容忍 LLM 在 JSON 前后输出的说明文字
        Dim start As Integer = text.IndexOf("{"c)
        Dim [end] As Integer = text.LastIndexOf("}"c)
        If start < 0 OrElse [end] <= start Then
            Return Nothing
        End If
        text = text.Substring(start, [end] - start + 1)

        Try
            Dim profile As UserProfile = text.LoadJSON(Of UserProfile)(simpleDict:=True, throwEx:=False)

            If profile Is Nothing OrElse profile.IsEmpty Then
                Return Nothing
            End If

            ' 列表字段空值保护
            If profile.Traits Is Nothing Then profile.Traits = New List(Of String)
            If profile.Interests Is Nothing Then profile.Interests = New List(Of String)

            Return profile
        Catch ex As Exception
            Call Console.Error.WriteLine($"[Profile] 画像 JSON 解析失败，保留旧画像: {ex.Message}")
            Return Nothing
        End Try
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
