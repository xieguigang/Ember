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
End Namespace