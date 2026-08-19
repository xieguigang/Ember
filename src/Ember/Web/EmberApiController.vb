Imports System.Linq
Imports Flute.Http.AppEngine
Imports Flute.Http.Core
Imports Flute.Http.Core.Message
Imports Flute.Http.Core.Message.HttpHeader

Namespace Web

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

        ''' <summary>默认头像文件名（缺失时前端回退 emoji）</summary>
        Private Const DEFAULT_AVATAR As String = "ember_default.jpg"

        ''' <summary>可作为头像的图片扩展名</summary>
        Private Shared ReadOnly AVATAR_EXTENSIONS As String() = {".jpg", ".jpeg", ".png", ".webp", ".gif"}

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

        ''' <summary>
        ''' 进行中对话的实时流快照：前端在 POST /api/chat 期间高频轮询本端点，
        ''' 实时渲染思考过程与增量回复。只读轻量锁快照，不进入互斥门，永不阻塞对话。
        ''' </summary>
        <HttpGet("/api/chat/live")>
        Public Sub GetLiveChat(request As HttpRequest, response As HttpResponse)
            Try
                Call AllowCors(response)
                Dim snapshot As LiveChatSnapshot = _agent.GetLiveChatSnapshot()
                Call response.WriteJSON(Envelope(snapshot))
            Catch ex As Exception
                Call Fail(response, ex)
            End Try
        End Sub

        ''' <summary>
        ''' 头像列表：扫描头像图片目录（*.jpg/*.png/*.webp/*.gif，字母排序，默认图置首），
        ''' 供前端头像选择面板使用。目录不存在时返回空列表（前端回退 emoji）。
        ''' </summary>
        <HttpGet("/api/avatars")>
        Public Sub GetAvatars(request As HttpRequest, response As HttpResponse)
            Try
                Call AllowCors(response)

                Dim list As New AvatarListResult With {
                    .urlPrefix = "/resource/images/avatars/",
                    .avatars = New List(Of String)
                }

                Dim dir As String = _agent.GetAvatarDirectory()
                If IO.Directory.Exists(dir) Then
                    Dim files As String() = IO.Directory.GetFiles(dir, "*.*") _
                        .Where(Function(f) AVATAR_EXTENSIONS.Contains(IO.Path.GetExtension(f).ToLowerInvariant())) _
                        .Select(Function(f) IO.Path.GetFileName(f)) _
                        .OrderBy(Function(f) f, StringComparer.OrdinalIgnoreCase) _
                        .ToArray()

                    ' 默认头像置首
                    Dim defaultFile As String = files.FirstOrDefault(
                        Function(f) String.Equals(f, DEFAULT_AVATAR, StringComparison.OrdinalIgnoreCase))
                    If defaultFile IsNot Nothing Then
                        list.avatars.Add(defaultFile)
                    End If
                    For Each f In files
                        If Not String.Equals(f, defaultFile, StringComparison.OrdinalIgnoreCase) Then
                            list.avatars.Add(f)
                        End If
                    Next
                End If

                Call response.WriteJSON(Envelope(list))
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


        ''' <summary>设置 CORS 响应头（同源部署非必需，为本地调试端口分离留余地）。</summary>
        Private Shared Sub AllowCors(response As HttpResponse)
            response.AccessControlAllowOrigin = "*"
        End Sub
    End Class
End Namespace