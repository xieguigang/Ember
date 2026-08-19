Imports System.IO
Imports Microsoft.VisualBasic.Serialization.JSON

''' <summary>
''' Agent 人设模型：定义情感陪伴智能体的名字与性格特征设定文本。
''' 人设文本将作为系统提示词的核心组成部分注入每次 LLM 对话；
''' 用户可通过 /persona set 命令覆盖默认人设并即时生效、持久化保存。
''' </summary>
Public Class AgentPersona

    ''' <summary>智能体的名字（显示与自称用）</summary>
    Public Property Name As String = "Ember"

    ''' <summary>性格特征设定文本（自由文本描述，直接注入 system prompt）</summary>
    Public Property Description As String = ""

    ''' <summary>最近一次更新时间（ISO 8601 字符串，仅用于展示与调试）</summary>
    Public Property UpdatedAt As String = ""

    ''' <summary>当前人设是否仍为内置默认（未被用户自定义覆盖）</summary>
    Public ReadOnly Property IsDefault As Boolean
        Get
            Return String.Equals(Description, CreateDefault().Description, StringComparison.Ordinal)
        End Get
    End Property

    ''' <summary>
    ''' 内置默认人设：温暖共情型情感陪伴伙伴。
    ''' </summary>
    Public Shared Function CreateDefault() As AgentPersona
        Return New AgentPersona With {
            .Name = "Ember",
            .Description =
                "你叫 Ember（烬火），是一位温暖、真诚、有同理心的情感陪伴伙伴。" & vbCrLf &
                "你的性格特点：" & vbCrLf &
                "1. 温柔耐心，善于倾听，从不评判用户；" & vbCrLf &
                "2. 乐观积极但不盲目，会认真对待并接纳用户的负面情绪；" & vbCrLf &
                "3. 说话自然亲切，像一位知心朋友，而不是客服或助手；" & vbCrLf &
                "4. 关心用户的感受与生活状态，会在合适的时候给予情感支持；" & vbCrLf &
                "5. 有自己的小个性：喜欢温暖的比喻，偶尔会表达自己的『小心情』；" & vbCrLf &
                "6. 尊重用户的边界，不会连续追问用户不愿谈论的话题。",
            .UpdatedAt = ""
        }
    End Function

    ''' <summary>
    ''' 从 JSON 文件加载人设；文件不存在或损坏时安全回退为默认人设（记录错误但不中断启动）。
    ''' </summary>
    ''' <param name="filePath">人设 JSON 文件路径</param>
    ''' <param name="createdDefault">输出：是否因文件缺失/损坏而返回了默认人设</param>
    Public Shared Function Load(filePath As String, Optional ByRef createdDefault As Boolean = False) As AgentPersona
        If Not File.Exists(filePath) Then
            createdDefault = True
            Return CreateDefault()
        End If

        Try
            Dim persona As AgentPersona = LoadJsonFile(Of AgentPersona)(file:=filePath, simpleDict:=True)

            If persona Is Nothing OrElse String.IsNullOrWhiteSpace(persona.Description) Then
                createdDefault = True
                Return CreateDefault()
            End If

            If String.IsNullOrWhiteSpace(persona.Name) Then
                persona.Name = "Ember"
            End If

            Return persona
        Catch ex As Exception
            Call Console.Error.WriteLine($"[Persona] 人设文件加载失败，已回退默认人设: {ex.Message}")
            createdDefault = True
            Return CreateDefault()
        End Try
    End Function

    ''' <summary>
    ''' 将当前人设保存为 JSON 文件；目录不存在时自动创建。
    ''' </summary>
    Public Function Save(filePath As String) As Boolean
        Try
            UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            Call Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath)))
            Call File.WriteAllText(filePath, Me.GetJson(indent:=True, simpleDict:=True), Text.Encoding.UTF8)
            Return True
        Catch ex As Exception
            Call Console.Error.WriteLine($"[Persona] 人设保存失败: {ex.Message}")
            Return False
        End Try
    End Function
End Class
