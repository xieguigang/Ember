Imports System.Linq
Imports System.Net.Http
Imports Flute.Http.AppEngine
Imports Flute.Http.Core
Imports Flute.Http.Core.Message
Imports Flute.Http.Core.Message.HttpHeader
Imports Microsoft.VisualBasic.Net.Http
Imports Ember.AgentRuntime

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
        ReadOnly _config As EmberConfig

        ' 进程内会话令牌字典：解锁成功后写入 token → true；校验时查表。
        ' 单实例 Agent 服务使用内存字典即可，重启即失效，避免密码落盘风险。
        ReadOnly _tokens As New HashSet(Of String)

        Sub New(agent As CompanionAgent, config As EmberConfig)
            _agent = agent
            _config = config
        End Sub

        ' ==================== 状态 ====================

        <HttpGet("/api/status")>
        Public Sub GetStatus(request As HttpRequest, response As HttpResponse)
            ' 受密码锁保护：未解锁一律 401
            If Not RequireValidToken(request, response) Then Return
            Try
                Call AllowCors(response)
                Dim snapshot As AgentStatusSnapshot = _agent.GetStatusSnapshotAsync().GetAwaiter().GetResult()
                Call response.WriteJSON(Envelope(snapshot))
            Catch ex As Exception
                Call Fail(response, ex)
            End Try
        End Sub

        ' ==================== 密码锁 ====================

        ''' <summary>
        ''' 前端启动信息（免 token 校验）：返回是否启用密码锁、模型、后端类型与智能体名称。
        ''' 注意：绝不返回密码或密码哈希，避免明文/哈希泄露。
        ''' </summary>
        <HttpGet("/api/info")>
        Public Sub GetInfo(request As HttpRequest, response As HttpResponse)
            Try
                Call AllowCors(response)

                Dim persona As PersonaSnapshot = _agent.GetPersonaSnapshotAsync().GetAwaiter().GetResult()
                Dim status As AgentStatusSnapshot = _agent.GetStatusSnapshotAsync().GetAwaiter().GetResult()

                Dim info As New InfoResult With {
                    .passwordEnabled = _config.enable_password,
                    .model = status.model,
                    .provider = _config.provider,
                    .agentName = persona.name
                }
                Call response.WriteJSON(Envelope(info))
            Catch ex As Exception
                Call Fail(response, ex)
            End Try
        End Sub

        ''' <summary>
        ''' 解锁端点（免 token 校验）：校验密码，成功返回一次性会话令牌。
        ''' 前端凭此令牌在后续 /api/* 请求中通过 X-Access-Token 头携带。
        ''' </summary>
        <HttpPost("/api/unlock")>
        Public Sub Unlock(request As HttpRequest, response As HttpResponse)
            Try
                Call AllowCors(response)

                If Not _config.enable_password Then
                    ' 未启用密码锁时直接放行（返回空令牌，前端也可忽略）
                    Call response.WriteJSON(Envelope(New UnlockResult With {.token = ""}))
                    Return
                End If

                Dim password As String = ReadPostField(request, "password")

                If password = _config.web_password Then
                    Dim token As String = Guid.NewGuid().ToString("N")
                    SyncLock _tokens
                        Call _tokens.Add(token)
                    End SyncLock
                    Call response.WriteJSON(Envelope(New UnlockResult With {.token = token}))
                Else
                    ' 密码错误：返回 401，前端退回锁屏并提示
                    Call response.WriteJSON(Envelope("密码不正确", 401))
                End If
            Catch ex As Exception
                Call Fail(response, ex)
            End Try
        End Sub

        ''' <summary>
        ''' 统一会话令牌校验：读取 X-Access-Token 头，校验是否已在解锁字典中。
        ''' 仅当启用密码锁（_config.enable_password = true）时才强制校验；
        ''' 未启用时直接放行，保持向后兼容。校验失败写 401 并返回 False。
        ''' </summary>
        Private Function RequireValidToken(request As HttpRequest, response As HttpResponse) As Boolean
            Call AllowCors(response)

            ' 未启用密码锁：不强制鉴权
            If Not _config.enable_password Then Return True

            Dim token As String = ""
            If request.HttpHeaders IsNot Nothing AndAlso request.HttpHeaders.ContainsKey("X-Access-Token") Then
                token = request.HttpHeaders("X-Access-Token")
            End If

            If String.IsNullOrWhiteSpace(token) Then
                Call response.WriteJSON(Envelope("需要密码解锁", 401))
                Return False
            End If

            Dim valid As Boolean
            SyncLock _tokens
                valid = _tokens.Contains(token)
            End SyncLock

            If Not valid Then
                Call response.WriteJSON(Envelope("会话已失效，请重新解锁", 401))
                Return False
            End If

            Return True
        End Function

        ' ==================== 人设 ====================

        <HttpGet("/api/persona")>
        Public Sub GetPersona(request As HttpRequest, response As HttpResponse)
            Try
                Call AllowCors(response)
                If Not RequireValidToken(request, response) Then Return
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
                If Not RequireValidToken(request, response) Then Return

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
                If Not RequireValidToken(request, response) Then Return
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
                If Not RequireValidToken(request, response) Then Return
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
                If Not RequireValidToken(request, response) Then Return
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
                If Not RequireValidToken(request, response) Then Return

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
                If Not RequireValidToken(request, response) Then Return

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
                If Not RequireValidToken(request, response) Then Return
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
                If Not RequireValidToken(request, response) Then Return

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

        ' ==================== TTS 语音合成代理 ====================

        ''' <summary>TTS 缓存容量上限：2GB（单文件平均数百 KB 下可缓存数千条语音）。</summary>
        Private Const TTS_CACHE_LIMIT_BYTES As Long = 2L * 1024 * 1024 * 1024

        ''' <summary>单段 TTS 文本字符数兜底上限（超出按句边界截断，防止合成服务超时/失败）。</summary>
        Private Const TTS_MAX_CHARS As Integer = 500

        ''' <summary>线程安全的共享 HttpClient（避免每请求建连，Timeout 适配长文本合成）。</summary>
        Private Shared ReadOnly _httpClient As New HttpClient() With {.Timeout = TimeSpan.FromSeconds(120)}

        ''' <summary>
        ''' TTS 语音合成代理端点：前端经本同源端点请求，规避浏览器直连 TTS 服务的跨域限制。
        ''' 查询参数：
        '''   text           待合成文本（必填，为空返回 400）
        '''   text_language  语言参数（可选，缺省用 ini [web] tts_language）
        '''
        ''' 行为：以 SHA256(text|language) 为缓存键查询本地 cache 目录，命中直接秒回 wav；
        ''' 未命中则转发到 ini 配置的 tts_url，成功后将 wav 原子落盘（先 .tmp 再 Move），
        ''' 并触发 2GB LRU 淘汰。TTS 服务不可用/超时/非 2xx 返回 502，失败结果不缓存。
        ''' 单段超 500 字符按句号/感叹号/问号/换行边界截断兜底。
        ''' </summary>
        <HttpGet("/api/tts")>
        Public Sub Tts(request As HttpRequest, response As HttpResponse)
            Try
                Call AllowCors(response)
                If Not RequireValidToken(request, response) Then Return

                Dim text As String = ""
                If request.URL.query.ContainsKey("text") Then
                    text = request.URL.query("text").ElementAtOrNull(Scan0)
                End If

                If String.IsNullOrWhiteSpace(text) Then
                    Call response.WriteJSON(Envelope("text 参数不能为空", 400))
                    Return
                End If

                Dim language As String = _agent.Config.tts_language
                If request.URL.query.ContainsKey("text_language") Then
                    Dim l As String = request.URL.query("text_language").ElementAtOrNull(Scan0)
                    If Not String.IsNullOrWhiteSpace(l) Then language = l
                End If

                ' 单段过长兜底截断（句边界优先），避免单次合成失败
                If text.Length > TTS_MAX_CHARS Then
                    text = TruncateBySentence(text, TTS_MAX_CHARS)
                End If

                ' 缓存键 = SHA256(text|language)
                Dim cacheDir As String = _agent.Config.CacheDirectory
                IO.Directory.CreateDirectory(cacheDir)
                Dim key As String = ComputeSha256($"{text}|{language}")
                Dim cacheFile As String = IO.Path.Combine(cacheDir, key & ".wav")

                Dim wav As Byte()

                If IO.File.Exists(cacheFile) Then
                    ' 命中缓存：更新访问时间（LRU 依据 LastWriteTime，命中后刷新以便判定）
                    wav = IO.File.ReadAllBytes(cacheFile)
                    Try
                        IO.File.SetLastWriteTimeUtc(cacheFile, Date.UtcNow)
                    Catch
                    End Try
                Else
                    ' 未命中：转发到 TTS 服务
                    Dim ttsUrl As String = _agent.Config.tts_url
                    If Not ttsUrl.EndsWith("/") Then ttsUrl &= "/"
                    Dim reqUrl As String = $"{ttsUrl}?text={Uri.EscapeDataString(text)}&text_language={Uri.EscapeDataString(language)}"

                    Dim ttsResp As Net.Http.HttpResponseMessage
                    Try
                        ttsResp = _httpClient.GetAsync(reqUrl).GetAwaiter().GetResult()
                    Catch ex As Exception
                        Call Console.Error.WriteLine($"[TTS] 调用 TTS 服务失败: {ex.Message}")
                        Call response.WriteError(HTTP_RFC.RFC_BAD_GATEWAY, "TTS 服务不可用，请确认本地语音合成服务已启动")
                        Return
                    End Try

                    Using ttsResp
                        If Not ttsResp.IsSuccessStatusCode Then
                            Call Console.Error.WriteLine($"[TTS] TTS 服务返回非成功状态: {(CInt(ttsResp.StatusCode))}")
                            Call response.WriteError(HTTP_RFC.RFC_BAD_GATEWAY, "TTS 服务返回错误，语音合成失败")
                            Return
                        End If

                        wav = ttsResp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
                    End Using

                    If wav Is Nothing OrElse wav.Length = 0 Then
                        Call response.WriteError(HTTP_RFC.RFC_BAD_GATEWAY, "TTS 服务返回空音频")
                        Return
                    End If

                    ' 原子落盘：先写 .tmp 再 Move，避免半截文件被后续请求当作命中
                    Dim tmpFile As String = cacheFile & ".tmp"
                    Try
                        IO.File.WriteAllBytes(tmpFile, wav)
                        If IO.File.Exists(cacheFile) Then IO.File.Delete(cacheFile)
                        IO.File.Move(tmpFile, cacheFile)
                    Catch ex As Exception
                        Call Console.Error.WriteLine($"[TTS] 缓存写入失败（已回退响应）: {ex.Message}")
                        If IO.File.Exists(tmpFile) Then
                            Try : IO.File.Delete(tmpFile) : Catch : End Try
                        End If
                    End Try

                    ' 写入后执行容量淘汰（异常静默，不影响本次响应）
                    Try
                        Call EvictCacheIfNeeded(cacheDir, TTS_CACHE_LIMIT_BYTES)
                    Catch ex As Exception
                        Call Console.Error.WriteLine($"[TTS] 缓存淘汰异常（已忽略）: {ex.Message}")
                    End Try
                End If

                ' 回传 wav 二进制
                Call response.WriteHttp("audio/wav", wav.Length)
                Call response.SendData(wav)
            Catch ex As Exception
                Call Fail(response, ex)
            End Try
        End Sub

        ''' <summary>按句号/感叹号/问号/换行边界将文本截断到 maxChars 以内（保留完整句子）。</summary>
        Private Shared Function TruncateBySentence(text As String, maxChars As Integer) As String
            If text.Length <= maxChars Then Return text
            Dim cut As Integer = maxChars
            ' 向前寻找最近的句边界（。！？！？\n），找不到则硬截断
            For i As Integer = maxChars To Math.Max(0, maxChars - 60) Step -1
                Dim c As Char = text(i)
                If c = "。"c OrElse c = "！"c OrElse c = "？"c OrElse c = "?"c OrElse c = "!"c OrElse c = vbLf OrElse c = vbCr Then
                    cut = i + 1
                    Exit For
                End If
            Next
            Return text.Substring(0, cut).Trim()
        End Function

        ''' <summary>计算字符串的 SHA256 十六进制（UTF-8）。</summary>
        Private Shared Function ComputeSha256(input As String) As String
            Using sha As Security.Cryptography.SHA256 = Security.Cryptography.SHA256.Create()
                Dim bytes As Byte() = System.Text.Encoding.UTF8.GetBytes(input)
                Dim hash As Byte() = sha.ComputeHash(bytes)
                Dim sb As New System.Text.StringBuilder(hash.Length * 2)
                For Each b As Byte In hash
                    sb.Append(b.ToString("x2"))
                Next
                Return sb.ToString()
            End Using
        End Function

        ''' <summary>
        ''' 缓存目录容量淘汰：统计全部 *.wav 总大小，超过上限时按 LastWriteTime 从最旧文件
        ''' 开始删除，直至低于上限或无可删文件。仅作用于本目录缓存文件，异常由调用方静默。
        ''' </summary>
        Private Shared Sub EvictCacheIfNeeded(cacheDir As String, limitBytes As Long)
            Dim files As String() = IO.Directory.GetFiles(cacheDir, "*.wav")
            If files.Length = 0 Then Return

            Dim total As Long = 0
            For Each f In files
                Try
                    total += New IO.FileInfo(f).Length
                Catch
                End Try
            Next

            If total <= limitBytes Then Return

            ' 按最旧优先排序
            Dim ordered = files _
                .OrderBy(Function(f)
                             Try : Return IO.File.GetLastWriteTimeUtc(f)
                             Catch : Return Date.MinValue
                             End Try
                         End Function) _
                .ToArray()

            For Each f In ordered
                If total <= limitBytes Then Exit For
                Try
                    Dim sz As Long = New IO.FileInfo(f).Length
                    IO.File.Delete(f)
                    total -= sz
                Catch
                End Try
            Next
        End Sub

        ' ==================== 每日日记 ====================

        ''' <summary>
        ''' 日记列表（按日期倒序，仅摘要不含正文）。
        ''' </summary>
        <HttpGet("/api/diary/list")>
        Public Sub ListDiaries(request As HttpRequest, response As HttpResponse)
            Try
                Call AllowCors(response)
                If Not RequireValidToken(request, response) Then Return
                Dim diaries As List(Of DiaryEntry) = _agent.ListDiariesAsync().GetAwaiter().GetResult()
                Dim result As New DiaryListResult With {.diaries = diaries}
                Call response.WriteJSON(Envelope(result))
            Catch ex As Exception
                Call Fail(response, ex)
            End Try
        End Sub

        ''' <summary>
        ''' 读取指定日期的日记（query 参数 date=yyyy-MM-dd，缺省今日）。
        ''' 返回该日全部日记（entries，多篇按生成时间倒序）；同时以最新一篇填充 title/content/generatedAt，兼容旧前端。
        ''' </summary>
        <HttpGet("/api/diary")>
        Public Sub GetDiary(request As HttpRequest, response As HttpResponse)
            Try
                Call AllowCors(response)
                If Not RequireValidToken(request, response) Then Return

                Dim [date] As String = ""
                If request.URL.query.ContainsKey("date") Then
                    [date] = request.URL.query("date").ElementAtOrNull(Scan0)
                End If

                Dim entries As List(Of DiaryEntry) = _agent.GetDiaryAllAsync([date]).GetAwaiter().GetResult()
                Dim latest As DiaryEntry = If(entries IsNot Nothing AndAlso entries.Count > 0,
                                              entries.OrderByDescending(Function(e) e.generatedAt).First(),
                                              Nothing)

                Dim result As New DiaryResult With {
                    .exists = latest IsNot Nothing,
                    .[date] = If(latest?.[date], If([date], "")),
                    .title = If(latest?.title, ""),
                    .content = If(latest?.content, ""),
                    .generatedAt = If(latest?.generatedAt, "")
                }
                If entries IsNot Nothing Then result.entries = entries
                Call response.WriteJSON(Envelope(result))
            Catch ex As Exception
                Call Fail(response, ex)
            End Try
        End Sub

        ''' <summary>
        ''' 手动生成/重写今日日记（同步等待完成）。
        ''' </summary>
        <HttpPost("/api/diary/generate")>
        Public Sub GenerateDiary(request As HttpRequest, response As HttpResponse)
            Try
                Call AllowCors(response)
                If Not RequireValidToken(request, response) Then Return

                Dim entry As DiaryEntry = _agent.WriteDiaryAsync().GetAwaiter().GetResult()
                If entry Is Nothing Then
                    Call response.WriteJSON(Envelope(New DiaryGenerateResult With {
                        .ok = False, .[date] = Date.Today.ToString("yyyy-MM-dd")}, 400))
                Else
                    Call response.WriteJSON(Envelope(New DiaryGenerateResult With {
                        .ok = True, .[date] = entry.[date], .id = entry.id}))
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
                If Not RequireValidToken(request, response) Then Return
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