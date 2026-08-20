Namespace Web

    ' ==================== API 响应用 DTO（公共类型，可被 DataContractJsonSerializer 序列化） ====================

    ''' <summary>简单操作结果（{ok}）。</summary>
    Public Class OkResult
        Public Property ok As Boolean
    End Class

    ''' <summary>画像手动总结结果（{updated}）。</summary>
    Public Class ProfileRefreshResult
        Public Property updated As Boolean
    End Class

    ''' <summary>头像列表（GET /api/avatars 响应体）。</summary>
    Public Class AvatarListResult
        ''' <summary>头像静态资源 URL 前缀（/resource/images/avatars/）</summary>
        Public Property urlPrefix As String = "/resource/images/avatars/"
        ''' <summary>可用头像文件名列表（默认头像置首；目录不存在时为空数组）</summary>
        Public Property avatars As New List(Of String)
    End Class

    ''' <summary>日记列表（GET /api/diary/list 响应体；日记摘要不含正文）。</summary>
    Public Class DiaryListResult
        Public Property diaries As New List(Of DiaryEntry)
    End Class

    ''' <summary>日记读取结果（GET /api/diary 响应体）。</summary>
    Public Class DiaryResult
        ''' <summary>指定日期的日记（当日多篇）是否存在</summary>
        Public Property exists As Boolean
        ''' <summary>指定日期（yyyy-MM-dd）</summary>
        Public Property [date] As String = ""
        ''' <summary>该日最新一篇的标题（兼容旧前端/REPL）</summary>
        Public Property title As String = ""
        ''' <summary>该日最新一篇的正文（兼容旧前端/REPL）</summary>
        Public Property content As String = ""
        ''' <summary>该日最新一篇的生成时间（兼容旧前端/REPL）</summary>
        Public Property generatedAt As String = ""
        ''' <summary>该日全部日记（多篇，按生成时间倒序）；前端阅读器顺序渲染</summary>
        Public Property entries As New List(Of DiaryEntry)
    End Class

    ''' <summary>日记生成结果（POST /api/diary/generate 响应体）。</summary>
    Public Class DiaryGenerateResult
        Public Property ok As Boolean
        ''' <summary>生成日记的日期（yyyy-MM-dd）</summary>
        Public Property [date] As String = ""
        ''' <summary>新生成日记的唯一 id（便于前端定位/高亮）</summary>
        Public Property id As String = ""
    End Class

    ''' <summary>Web 前端启动信息（GET /api/info 响应体）。</summary>
    Public Class InfoResult
        ''' <summary>是否启用密码锁；true 时前端必须先 /api/unlock 换取令牌再访问其他接口</summary>
        Public Property passwordEnabled As Boolean
        ''' <summary>当前对话模型名称（用于前端展示）</summary>
        Public Property model As String = ""
        ''' <summary>LLM 后端类型描述（ollama / openai）</summary>
        Public Property provider As String = ""
        ''' <summary>智能体名称（人设标题），空时前端回退默认</summary>
        Public Property agentName As String = ""
    End Class

    ''' <summary>解锁响应（POST /api/unlock 响应体）；成功时携带一次性会话令牌。</summary>
    Public Class UnlockResult
        ''' <summary>会话令牌（GUID 字符串）；后续请求通过 X-Access-Token 头携带</summary>
        Public Property token As String = ""
    End Class
End Namespace