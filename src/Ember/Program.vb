Imports System.IO
Imports System.Text
Imports Ember.AgentRuntime
Imports Ember.Web

''' <summary>
''' Ember 情感陪伴智能体程序入口：两种互斥运行模式。
'''
''' 1. CLI 模式（默认，无参数）：交互命令循环，支持 /help、/exit、/persona、
'''    /profile、/status、/save 命令与 Ctrl+C 安全退出落盘；
''' 2. HTTP 模式（--http）：无界面后台服务常驻运行——加载配置与记忆数据后启动
'''    Web 服务器（REST API + 静态页面），主线程阻塞在服务循环，不接受 CLI 交互；
'''    关闭途径：本地 Ctrl+C 或 Web 端 OPTIONS /ctrl/kill（需令牌匹配），
'''    两者均触发优雅关闭，服务循环退出后统一保存全部数据落盘。
''' </summary>
Module Program

    ''' <summary>Ctrl+C 退出标志（CLI 模式：主循环检测；HTTP 模式：触发服务器关闭）</summary>
    Dim _cancelled As Boolean = False

    ''' <summary>HTTP 模式下的 Web 服务器实例（Ctrl+C 时触发其优雅关闭）</summary>
    Dim _webServer As EmberWebServer = Nothing

    ''' <summary>需要从输入中清理的零宽字符（管道/重定向输入可能携带的 BOM 等）</summary>
    ReadOnly _zeroWidthChars As Char() = {ChrW(&HFEFF), ChrW(&H200B), ChrW(&H200C), ChrW(&H200D)}

    Function Main(args As String()) As Integer
        ' Ctrl+C 安全退出：阻止进程被直接终止，改由统一流程落盘后正常退出
        AddHandler Console.CancelKeyPress,
            Sub(sender, e)
                e.Cancel = True
                _cancelled = True
                Call Console.WriteLine()
                Call Console.WriteLine("(检测到 Ctrl+C，正在准备安全退出…再次按下将强制终止)")

                If _webServer IsNot Nothing Then
                    ' HTTP 模式：触发服务器优雅关闭（后台执行避免阻塞事件回调线程，
                    ' Shutdown 会停止监听使 Run() 的 accept 循环退出）
                    Call Task.Run(Sub() Call _webServer.Shutdown())
                End If
            End Sub

        ' Windows 控制台中文输出保护（输出被重定向等场景下可能抛异常，安全忽略）
        Try
            Console.OutputEncoding = Encoding.UTF8
        Catch
        End Try

        ' 解析命令行参数
        Dim portOverride As Integer = 0
        Dim wwwrootOverride As String = Nothing
        Dim opts As Opts = Opts.Build(args)

        If opts.help Then
            Call PrintHelp()
        Else
            portOverride = opts.port
            wwwrootOverride = opts.wwwroot
        End If

        ' 加载（或首次生成）ini 配置
        Dim config As EmberConfig = EmberConfig.LoadOrCreate()

        If opts.http_mode Then
            Return RunHttpService(config, portOverride, wwwrootOverride)
        Else
            Return RunCliMode(config)
        End If
    End Function

    ' ==================== HTTP 后台服务模式 ====================

    ''' <summary>
    ''' 无界面 HTTP 服务模式：初始化智能体 → 启动 Web 服务器 → 主线程阻塞常驻。
    ''' 本地 Ctrl+C 与远程 OPTIONS /ctrl/kill（令牌匹配）均触发 <see cref="EmberWebServer.Shutdown"/>，
    ''' 服务循环返回后在此统一落盘退出（两条关闭路径的汇合点）。
    ''' </summary>
    Private Function RunHttpService(config As EmberConfig, portOverride As Integer, wwwrootOverride As String) As Integer
        Dim restoredMessages As Integer

        Using agent As CompanionAgent = CompanionAgent.Create(config, restoredMessages)
            ' 解析监听端口与静态目录（命令行覆盖 → ini 配置 → 自动探测）
            Dim port As Integer = If(portOverride > 0, portOverride, config.http_port)
            Dim wwwroot As String = config.ResolveWwwroot(wwwrootOverride)

            Call Console.WriteLine("==================================================")
            Call Console.WriteLine("   Ember · 情感陪伴服务（HTTP 模式）")
            Call Console.WriteLine("==================================================")
            Call Console.WriteLine($"后端: {config.DescribeEndpoint()}")
            Call Console.WriteLine($"数据: {config.DataDirectory}（已恢复 {restoredMessages} 条历史对话）")

            If Not Directory.Exists(wwwroot) Then
                Call Console.WriteLine($"[警告] Web 静态目录不存在: {wwwroot}")
                Call Console.WriteLine("        页面将无法访问，但 API 仍可用；可在 settings.ini [web] wwwroot 或 --wwwroot 指定。")
            Else
                Call Console.WriteLine($"页面: {wwwroot}")
            End If

            If String.IsNullOrWhiteSpace(config.shutdown_token) Then
                Call Console.WriteLine("远程关闭: 未启用（settings.ini [web] shutdown_token 配置令牌后可用）")
            Else
                Call Console.WriteLine("远程关闭: 已启用（Web 界面可通过令牌安全关闭服务）")
            End If

            ' 组装并启动服务器（构造时预检端口占用，失败抛 InvalidOperationException）
            Dim server As EmberWebServer
            Try
                server = New EmberWebServer(agent, port, wwwroot, config.shutdown_token)
                Call server.Start()
            Catch ex As InvalidOperationException
                Call Console.Error.WriteLine($"[错误] {ex.Message}")
                Return 1
            End Try

            _webServer = server

            Call Console.WriteLine($"服务: http://127.0.0.1:{port} （Ctrl+C 或 Web 界面远程关闭）")
            Call Console.WriteLine("--------------------------------------------------")

            ' 主线程阻塞常驻：接受请求直到 Shutdown（Ctrl+C / 远程 kill 汇合点）
            Dim exitCode As Integer = server.Run()

            _webServer = Nothing

            ' 统一落盘（Using Dispose 中的保存为二次兜底，幂等）
            Call Console.WriteLine()
            Call Console.WriteLine("[服务] 正在保存对话记忆与人设画像…")
            Call agent.SaveAllAsync().GetAwaiter().GetResult()
            Call Console.WriteLine("[服务] 已全部保存。服务已安全退出，再见！")

            Return exitCode
        End Using
    End Function

    ' ==================== CLI 交互模式（默认） ====================

    ''' <summary>CLI 模式入口：创建智能体并进入交互命令循环。</summary>
    Private Function RunCliMode(config As EmberConfig) As Integer
        Dim restoredMessages As Integer

        Using agent As CompanionAgent = CompanionAgent.Create(config, restoredMessages)
            Call RunCliAsync(agent, restoredMessages).GetAwaiter().GetResult()
        End Using

        Return 0
    End Function

    ''' <summary>
    ''' 交互主循环：欢迎信息 → 开场问候 → 命令分发与多轮对话 → 退出落盘。
    ''' </summary>
    Private Async Function RunCliAsync(agent As CompanionAgent, restoredMessages As Integer) As Task
        Call PrintBanner(agent, restoredMessages)

        ' 开场问候：老用户简短欢迎，新用户由 LLM 以人设身份主动打招呼
        '（问候正文在请求过程中已由 LLMClient 流式输出，此处只打前缀与收尾换行，避免重复打印）
        If restoredMessages > 0 Then
            Call Console.WriteLine("欢迎回来！我们接着上次的聊吧～")
            Call Console.WriteLine()
        Else
            Call Console.Write($"{agent.Persona.Name}> ")
            Dim greeting As String = Await agent.GreetAsync()
            If String.IsNullOrWhiteSpace(greeting) Then
                Call Console.WriteLine("（问候生成失败，不过没关系，你可以直接开始输入）")
                Call Console.WriteLine()
            Else
                Call Console.WriteLine()
                Call Console.WriteLine()
            End If
        End If

        ' 命令与对话主循环
        While Not _cancelled
            Call Console.Write("你> ")
            Dim input As String = Console.ReadLine()

            If _cancelled Then Exit While
            If input Is Nothing Then Exit While   ' 输入流被关闭（重定向结束等）

            ' 清理 BOM 等零宽字符（管道/重定向输入场景），再去除首尾空白
            input = input.Trim().Trim(_zeroWidthChars).Trim()
            If input.Length = 0 Then Continue While

            If input.StartsWith("/") Then
                ' 命令：返回 True 表示请求退出
                If Await HandleCommandAsync(agent, input) Then
                    Exit While
                End If
            Else
                ' 普通对话：交由智能体处理（内部含流式输出、轮次计数、
                ' 自动保存与周期性画像总结）
                Await agent.ChatCoreAsync(input)
            End If
        End While

        ' 退出前统一落盘（Using Dispose 中的保存为二次兜底，幂等）
        Call Console.WriteLine()
        Call Console.WriteLine("[系统] 正在保存对话记忆与人设画像…")
        Await agent.SaveAllAsync()
        Call Console.WriteLine("[系统] 已全部保存。期待下次见面，再见！")
    End Function

    ''' <summary>
    ''' 处理斜杠命令；返回 True 表示用户请求退出。
    ''' </summary>
    Private Async Function HandleCommandAsync(agent As CompanionAgent, input As String) As Task(Of Boolean)
        Dim parts As String() = input.Split(" "c, 2)
        Dim cmd As String = parts(0).ToLowerInvariant()
        Dim arg As String = If(parts.Length > 1, parts(1).Trim(), "")

        Select Case cmd
            Case "/help", "/?"
                Call PrintHelp()

            Case "/exit", "/quit"
                Return True

            Case "/save"
                Await agent.SaveAllAsync()
                Call Console.WriteLine("[系统] 对话历史、人设与用户画像已全部保存。")

            Case "/status"
                Call Console.WriteLine(agent.GetStatusText())
                Call Console.WriteLine()

            Case "/profile"
                Call PrintProfile(agent)
                Call Console.WriteLine()

            Case "/persona"
                Await HandlePersonaCommandAsync(agent, arg)

            Case Else
                Call Console.WriteLine($"未知命令 {cmd}，输入 /help 查看可用命令。")
                Call Console.WriteLine()
        End Select

        Return False
    End Function

    ''' <summary>
    ''' 处理 /persona 子命令：set/show/reset。
    ''' </summary>
    Private Async Function HandlePersonaCommandAsync(agent As CompanionAgent, arg As String) As Task
        Dim parts As String() = arg.Split(" "c, 2)
        Dim [sub] As String = If(parts.Length > 0, parts(0).ToLowerInvariant(), "")
        Dim subArg As String = If(parts.Length > 1, parts(1).Trim(), "")

        Select Case [sub]
            Case "set"
                If subArg.Length = 0 Then
                    Call Console.WriteLine("用法：/persona set <人设描述>")
                    Call Console.WriteLine("例如：/persona set 你是一只温柔傲娇的猫娘助手")
                Else
                    Await agent.SetPersonaAsync(subArg)
                    Call Console.WriteLine("[系统] 人设已更新并保存，从下一句话开始生效。")
                End If

            Case "show"
                Call Console.WriteLine("当前人设：")
                Call Console.WriteLine(agent.Persona.Description)
                Call Console.WriteLine($"（人设名：{agent.Persona.Name}，更新于 {agent.Persona.UpdatedAt}）")

            Case "reset"
                Await agent.ResetPersonaAsync()
                Call Console.WriteLine("[系统] 已恢复内置默认人设。")

            Case Else
                Call Console.WriteLine("用法：/persona set <描述> | /persona show | /persona reset")
        End Select

        Call Console.WriteLine()
    End Function

    ' ==================== 界面文本 ====================

    ''' <summary>
    ''' 打印程序用法。
    ''' </summary>
    Private Sub PrintUsage()
        Call Console.WriteLine("Ember · 情感陪伴智能体")
        Call Console.WriteLine()
        Call Console.WriteLine("用法: Ember [--http] [--port N] [--wwwroot 目录]")
        Call Console.WriteLine()
        Call Console.WriteLine("  （无参数）    CLI 交互模式：命令行对话（/help 查看命令）")
        Call Console.WriteLine("  --http       HTTP 服务模式：无界面后台服务，通过 Web 页面访问")
        Call Console.WriteLine("  --port N     HTTP 模式监听端口（默认取 settings.ini [web] http_port，即 8080）")
        Call Console.WriteLine("  --wwwroot D  Web 静态文件目录（默认自动探测，如 G:\Ember\web）")
        Call Console.WriteLine()
        Call Console.WriteLine("HTTP 模式关闭方式: Ctrl+C，或 Web 界面远程关闭（需在 settings.ini [web] 配置 shutdown_token）")
    End Sub

    ''' <summary>
    ''' 打印当前用户性格画像（/profile 命令）。
    ''' </summary>
    Private Sub PrintProfile(agent As CompanionAgent)
        Dim profile As UserProfile = agent.Profile

        If profile.IsEmpty Then
            Call Console.WriteLine("[系统] 还没有总结出你的性格画像，多聊几轮之后我会慢慢更懂你。")
            Return
        End If

        Call Console.WriteLine("我眼中的你（根据对话记忆总结，会持续更新）：")
        Call Console.WriteLine(profile.ToPromptText())
        Call Console.WriteLine($"（画像更新于 {profile.UpdatedAt}）")
    End Sub

    ''' <summary>
    ''' 打印启动横幅与运行环境摘要。
    ''' </summary>
    Private Sub PrintBanner(agent As CompanionAgent, restoredMessages As Integer)
        Call Console.WriteLine("==================================================")
        Call Console.WriteLine("   Ember · 你的情感陪伴伙伴")
        Call Console.WriteLine("==================================================")
        Call Console.WriteLine(agent.GetStatusText())
        Call Console.WriteLine()
        If restoredMessages > 0 Then
            Call Console.WriteLine($"[系统] 已恢复 {restoredMessages} 条历史对话记忆。")
        Else
            Call Console.WriteLine("[系统] 检测到这是一段新的陪伴旅程。")
        End If
        Call Console.WriteLine("[系统] 输入 /help 查看命令；/exit 退出（自动保存）。")
        Call Console.WriteLine()
    End Sub

    ''' <summary>
    ''' 打印命令帮助。
    ''' </summary>
    Private Sub PrintHelp()
        Call Console.WriteLine("可用命令：")
        Call Console.WriteLine("  /help                 显示本帮助")
        Call Console.WriteLine("  /exit, /quit          退出程序（自动保存全部记忆）")
        Call Console.WriteLine("  /persona set <描述>   设置我的性格人设")
        Call Console.WriteLine("                        例如：/persona set 你是一只温柔傲娇的猫娘")
        Call Console.WriteLine("  /persona show         查看当前人设")
        Call Console.WriteLine("  /persona reset        恢复默认人设")
        Call Console.WriteLine("  /profile              查看我对你的性格画像总结")
        Call Console.WriteLine("  /status               查看运行状态")
        Call Console.WriteLine("  /save                 立即保存全部记忆")
        Call Console.WriteLine()
        Call Console.WriteLine("其他任何输入都会作为对话内容发送给我，直接开始聊天吧～")
        Call Console.WriteLine()
    End Sub
End Module
