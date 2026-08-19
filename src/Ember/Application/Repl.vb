Imports Ember.AgentRuntime

Namespace Application

    Module Repl

        ''' <summary>Ctrl+C 退出标志（CLI 模式：主循环检测；HTTP 模式：触发服务器关闭）</summary>
        Friend _cancelled As Boolean = False

        ''' <summary>需要从输入中清理的零宽字符（管道/重定向输入可能携带的 BOM 等）</summary>
        ReadOnly _zeroWidthChars As Char() = {ChrW(&HFEFF), ChrW(&H200B), ChrW(&H200C), ChrW(&H200D)}

        ' ==================== CLI 交互模式（默认） ====================

        ''' <summary>CLI 模式入口：创建智能体并进入交互命令循环。</summary>
        Friend Function RunCliMode(config As EmberConfig) As Integer
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
End Namespace