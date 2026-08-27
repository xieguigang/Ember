
' ==================== HTTP API 用的 DTO 类型（公共属性，属性名即 JSON 字段名） ====================

''' <summary>一轮对话的结构化结果（POST /api/chat 响应体）。</summary>
Public Class ChatResult
    ''' <summary>LLM 回复正文</summary>
    Public Property reply As String = ""
    ''' <summary>LLM 思考过程文本（可能为空）</summary>
    Public Property think As String = ""
    ''' <summary>本轮完成后的累计对话轮次</summary>
    Public Property turn As Integer
    ''' <summary>本轮对话是否成功</summary>
    Public Property success As Boolean
    ''' <summary>失败时的错误信息</summary>
    Public Property errorMessage As String = ""
End Class

''' <summary>运行状态快照（GET /api/status 响应体）。</summary>
Public Class AgentStatusSnapshot
    Public Property backend As String = ""
    Public Property model As String = ""
    Public Property turns As Integer
    Public Property tokens As Integer
    Public Property maxTokens As Integer
    Public Property personaName As String = ""
    Public Property personaIsDefault As Boolean
    Public Property profileUpdated As String = ""
    Public Property autosave As Boolean
    Public Property dataDir As String = ""
    ''' <summary>是否已配置远程关闭 token（不泄露 token 明文，前端据此决定是否显示关闭按钮）</summary>
    Public Property remoteShutdownEnabled As Boolean
End Class

''' <summary>Agent 人设快照（GET /api/persona 响应体）。</summary>
Public Class PersonaSnapshot
    Public Property name As String = ""
    Public Property description As String = ""
    Public Property isDefault As Boolean
    Public Property updatedAt As String = ""
End Class

''' <summary>用户画像快照（GET /api/profile 响应体）。</summary>
Public Class ProfileSnapshot
    Public Property summary As String = ""
    Public Property traits As New List(Of String)
    Public Property interests As New List(Of String)
    Public Property emotionalState As String = ""
    Public Property communicationStyle As String = ""
    Public Property updatedAt As String = ""
    Public Property isEmpty As Boolean
End Class

''' <summary>历史消息集合（GET /api/history 响应体）。</summary>
Public Class HistorySnapshot
    Public Property messages As New List(Of HistoryMessage)
End Class

''' <summary>单条历史消息。</summary>
Public Class HistoryMessage
    Public Property role As String = ""
    Public Property content As String = ""
End Class

''' <summary>
''' 进行中对话的实时流快照（GET /api/chat/live 响应体）。
''' phase: idle=无对话；thinking=模型思考中；replying=回复生成中。
''' </summary>
Public Class LiveChatSnapshot
    Public Property active As Boolean
    Public Property phase As String = "idle"
    Public Property think As String = ""
    Public Property output As String = ""
    Public Property turn As Integer
End Class
