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
End Namespace