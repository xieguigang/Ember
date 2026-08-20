Imports System.IO
Imports Ember.AgentRuntime
Imports Ember.Web

Namespace Application

    Module Http

        ''' <summary>HTTP 模式下的 Web 服务器实例（Ctrl+C 时触发其优雅关闭）</summary>
        Friend _webServer As EmberWebServer = Nothing

        ' ==================== HTTP 后台服务模式 ====================

        ''' <summary>
        ''' 无界面 HTTP 服务模式：初始化智能体 → 启动 Web 服务器 → 主线程阻塞常驻。
        ''' 本地 Ctrl+C 与远程 OPTIONS /ctrl/kill（令牌匹配）均触发 <see cref="EmberWebServer.Shutdown"/>，
        ''' 服务循环返回后在此统一落盘退出（两条关闭路径的汇合点）。
        ''' </summary>
        Public Function RunHttpService(config As EmberConfig, portOverride As Integer, wwwrootOverride As String) As Integer
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

                If config.enable_password Then
                    Call Console.WriteLine("Web 密码锁: 已启用（进入聊天界面前需输入密码，全接口已受令牌保护）")
                Else
                    Call Console.WriteLine("Web 密码锁: 未启用（settings.ini [web] enable_password=true 可开启）")
                End If

                ' 组装并启动服务器（构造时预检端口占用，失败抛 InvalidOperationException）
                Dim server As EmberWebServer
                Try
                    server = New EmberWebServer(agent, port, wwwroot, config.shutdown_token, config, config.AgentWebRoot)
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
    End Module
End Namespace