Imports System.Runtime.CompilerServices
Imports Flute.Http.AppEngine
Imports Flute.Http.Core.Message

Namespace Web

    Module Message

        ' ==================== 内部辅助 ====================

        ''' <summary>读取 POST JSON body 字段（自动回退 query 参数）；请求非 POST 时读 query。</summary>
        Friend Function ReadPostField(request As HttpRequest, name As String) As String
            Dim post = TryCast(request, HttpPOSTRequest)

            If post IsNot Nothing Then
                Dim value As String = post(name).DefaultValue
                If Not String.IsNullOrWhiteSpace(value) Then
                    Return value.Trim()
                End If
            End If

            If request.URL.query.ContainsKey(name) Then
                Return request.URL.query(name).ElementAtOrNull(Scan0)
            End If

            Return ""
        End Function


        ''' <summary>成功响应信封 {code:0, info:...}。</summary>
        Friend Function Envelope(Of T)(payload As T) As JsonResponse(Of T)
            Return New JsonResponse(Of T) With {.code = 0, .info = payload}
        End Function

        ''' <summary>错误响应信封 {code:..., info:错误信息}。</summary>
        Friend Function Envelope(Of T)(payload As T, code As Integer) As JsonResponse(Of T)
            Return New JsonResponse(Of T) With {.code = code, .info = payload}
        End Function

        ''' <summary>全局异常兜底：返回 code=500，记录服务器日志。</summary>
        ''' 
        <Extension>
        Friend Sub Fail(response As HttpResponse, ex As Exception)
            Call Console.Error.WriteLine($"[API] 处理请求失败: {ex.Message}")
            Try
                response.AccessControlAllowOrigin = "*"
                Call response.WriteJSON(New JsonResponse(Of String) With {.code = 500, .info = ex.Message})
            Catch inner As Exception
                Call Console.Error.WriteLine($"[API] 写出错误响应失败: {inner.Message}")
            End Try
        End Sub
    End Module
End Namespace