Imports Flute.Http.AppEngine
Imports Flute.Http.Core
Imports Flute.Http.Core.Message
Imports Flute.Http.Core.Message.HttpHeader

''' <summary>
''' Ember Web API 控制器：由 <see cref="HttpRouter"/> 通过反射注册，
''' 为 Web 前端提供 REST API（对话/历史/人设/画像/状态/保存）。
'''
''' 路由要求：方法必须为 Public Sub(request As HttpRequest, response As HttpResponse)，
''' 并以 &lt;HttpGet("/url")&gt;/&lt;HttpPost("/url")&gt; 特性标注。
'''
''' 并发与异步：Flute 使用 ThreadPool 多线程分发请求；本控制器内部通过
''' CompanionAgent 的互斥信号量串行化对共享状态的访问。worker 线程无
''' SynchronizationContext，故用 GetAwaiter().GetResult() 同步等待异步方法无死锁风险。
''' 所有 handler 均有全局异常捕获，异常返回 code=500 的 JSON，绝不让 worker 线程崩溃。
''' </summary>
Public Class EmberApiController

    ReadOnly _agent As CompanionAgent

    Sub New(agent As CompanionAgent)
        _agent = agent
    End Sub

    ' ==================== 状态 ====================

    <HttpGet("/api/status")>
    Public Sub GetStatus(request As HttpRequest, response As HttpResponse)
        Try
            Call AllowCors(response)
            Dim snapshot As AgentStatusSnapshot = _agent.GetStatusSnapshotAsync().GetAwaiter().GetResult()
            Call response.WriteJSON(Envelope(snapshot))
        Catch ex As Exception
            Call Fail(response, ex)
        End Try
    End Sub

    ' ==================== 人设 ====================

    <HttpGet("/api/persona")>
    Public Sub GetPersona(request As HttpRequest, response As HttpResponse)
        Try
            Call AllowCors(response)
            Dim snapshot As PersonaSnapshot = _agent.GetPersonaSnapshotAsync().GetAwaiter().GetResult()
            Call response.WriteJSON(Envelope(snapshot))
        Catch ex As Exception
            Call Fail(response, ex)
        End Try
    End Sub

    <HttpPost("/api/persona")>
    Public Sub SetPersona(request As HttpRequest, response As HttpResponse)
        Try
            Call AllowCors(response)

            Dim description As String = ReadPostField(request, "description")
            If String.IsNullOrWhiteSpace(description) Then
                Call response.WriteJSON(Envelope("description 字段不能为空", 400))
                Return
            End If

            Call _agent.SetPersonaAsync(description).GetAwaiter().GetResult()
            Call response.WriteJSON(Envelope(New OkResult With {.ok = True}))
        Catch ex As Exception
            Call Fail(response, ex)
        End Try
    End Sub

    <HttpPost("/api/persona/reset")>
    Public Sub ResetPersona(request As HttpRequest, response As HttpResponse)
        Try
            Call AllowCors(response)
            Call _agent.ResetPersonaAsync().GetAwaiter().GetResult()
            Call response.WriteJSON(Envelope(New OkResult With {.ok = True}))
        Catch ex As Exception
            Call Fail(response, ex)
        End Try
    End Sub

    ' ==================== 用户画像 ====================

    <HttpGet("/api/profile")>
    Public Sub GetProfile(request As HttpRequest, response As HttpResponse)
        Try
            Call AllowCors(response)
            Dim snapshot As ProfileSnapshot = _agent.GetProfileSnapshotAsync().GetAwaiter().GetResult()
            Call response.WriteJSON(Envelope(snapshot))
        Catch ex As Exception
            Call Fail(response, ex)
        End Try
    End Sub

    <HttpPost("/api/profile/refresh")>
    Public Sub RefreshProfile(request As HttpRequest, response As HttpResponse)
        Try
            Call AllowCors(response)
            Dim updated As Boolean = _agent.UpdateProfileAsync().GetAwaiter().GetResult()
            Call response.WriteJSON(Envelope(New ProfileRefreshResult With {.updated = updated}))
        Catch ex As Exception
            Call Fail(response, ex)
        End Try
    End Sub

    ' ==================== 对话 ====================

    <HttpGet("/api/history")>
    Public Sub GetHistory(request As HttpRequest, response As HttpResponse)
        Try
            Call AllowCors(response)

            Dim limit As Integer = 50
            If request.URL.query.ContainsKey("limit") Then
                Dim s As String = request.URL.query("limit").ElementAtOrNull(Scan0)
                If Not Integer.TryParse(s, limit) Then limit = 50
            End If

            Dim snapshot As HistorySnapshot = _agent.GetRecentHistoryAsync(limit).GetAwaiter().GetResult()
            Call response.WriteJSON(Envelope(snapshot))
        Catch ex As Exception
            Call Fail(response, ex)
        End Try
    End Sub

    <HttpPost("/api/chat")>
    Public Sub Chat(request As HttpRequest, response As HttpResponse)
        Try
            Call AllowCors(response)

            Dim message As String = ReadPostField(request, "message")
            If String.IsNullOrWhiteSpace(message) Then
                Call response.WriteJSON(Envelope("message 字段不能为空", 400))
                Return
            End If

            ' 同步等待对话完成（含轮次计数/自动保存/周期画像总结）；
            ' LLM 流式增量由 LLMClient 直接输出到服务器控制台日志
            Dim result As ChatResult = _agent.ChatCoreAsync(message).GetAwaiter().GetResult()

            If Not result.success Then
                Call response.WriteJSON(Envelope(New ChatResult With {
                    .success = False,
                    .errorMessage = result.errorMessage,
                    .turn = result.turn
                }, 500))
            Else
                Call response.WriteJSON(Envelope(result))
            End If
        Catch ex As Exception
            Call Fail(response, ex)
        End Try
    End Sub

    ' ==================== 持久化 ====================

    <HttpPost("/api/save")>
    Public Sub Save(request As HttpRequest, response As HttpResponse)
        Try
            Call AllowCors(response)
            Call _agent.SaveAllAsync().GetAwaiter().GetResult()
            Call response.WriteJSON(Envelope(New OkResult With {.ok = True}))
        Catch ex As Exception
            Call Fail(response, ex)
        End Try
    End Sub

    ' ==================== 内部辅助 ====================

    ''' <summary>读取 POST JSON body 字段（自动回退 query 参数）；请求非 POST 时读 query。</summary>
    Private Shared Function ReadPostField(request As HttpRequest, name As String) As String
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

    ''' <summary>设置 CORS 响应头（同源部署非必需，为本地调试端口分离留余地）。</summary>
    Private Shared Sub AllowCors(response As HttpResponse)
        response.AccessControlAllowOrigin = "*"
    End Sub

    ''' <summary>成功响应信封 {code:0, info:...}。</summary>
    Private Shared Function Envelope(Of T)(payload As T) As JsonResponse(Of T)
        Return New JsonResponse(Of T) With {.code = 0, .info = payload}
    End Function

    ''' <summary>错误响应信封 {code:..., info:错误信息}。</summary>
    Private Shared Function Envelope(Of T)(payload As T, code As Integer) As JsonResponse(Of T)
        Return New JsonResponse(Of T) With {.code = code, .info = payload}
    End Function

    ''' <summary>全局异常兜底：返回 code=500，记录服务器日志。</summary>
    Private Shared Sub Fail(response As HttpResponse, ex As Exception)
        Call Console.Error.WriteLine($"[API] 处理请求失败: {ex.Message}")
        Try
            response.AccessControlAllowOrigin = "*"
            Call response.WriteJSON(New JsonResponse(Of String) With {.code = 500, .info = ex.Message})
        Catch inner As Exception
            Call Console.Error.WriteLine($"[API] 写出错误响应失败: {inner.Message}")
        End Try
    End Sub
End Class

' ==================== API 响应用 DTO（公共类型，可被 DataContractJsonSerializer 序列化） ====================

''' <summary>简单操作结果（{ok}）。</summary>
Public Class OkResult
    Public Property ok As Boolean
End Class

''' <summary>画像手动总结结果（{updated}）。</summary>
Public Class ProfileRefreshResult
    Public Property updated As Boolean
End Class
