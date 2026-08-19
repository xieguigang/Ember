Imports System.Text
Imports Ember.AgentRuntime
Imports Ember.Application

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

    Function Main(args As String()) As Integer
        ' Ctrl+C 安全退出：阻止进程被直接终止，改由统一流程落盘后正常退出
        AddHandler Console.CancelKeyPress,
            Sub(sender, e)
                e.Cancel = True
                Repl._cancelled = True
                Call Console.WriteLine()
                Call Console.WriteLine("(检测到 Ctrl+C，正在准备安全退出…再次按下将强制终止)")

                If Http._webServer IsNot Nothing Then
                    ' HTTP 模式：触发服务器优雅关闭（后台执行避免阻塞事件回调线程，
                    ' Shutdown 会停止监听使 Run() 的 accept 循环退出）
                    Call Task.Run(Sub() Call Http._webServer.Shutdown())
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
            Call PrintUsage()
        Else
            portOverride = opts.port
            wwwrootOverride = opts.wwwroot
        End If

        ' 加载（或首次生成）ini 配置
        Dim config As EmberConfig = EmberConfig.LoadOrCreate()

        If opts.http_mode Then
            Return Http.RunHttpService(config, portOverride, wwwrootOverride)
        Else
            Return Repl.RunCliMode(config)
        End If
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
End Module
