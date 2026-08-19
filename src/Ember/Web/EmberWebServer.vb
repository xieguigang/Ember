Imports System
Imports Flute.Http.Configurations
Imports Flute.Http.Core
Imports Flute.Http.FileSystem
Imports Microsoft.VisualBasic.Net

''' <summary>
''' Ember HTTP 服务模式封装：组装 Flute 的 HttpRouter（反射注册 API 控制器）+
''' MountFs 静态文件服务（Web 前端），以 HttpSocket 形式对外提供 Web 服务。
'''
''' 生命周期约定：
''' 1. <see cref="Start"/> 组装路由与静态文件系统（不监听端口）；
''' 2. <see cref="Run"/> 由主线程阻塞调用（后台服务常驻模型，accept 循环）；
''' 3. <see cref="Shutdown"/> 优雅关闭（本地 Ctrl+C 或远程 OPTIONS /ctrl/kill 均汇合到此），
'''    Run() 返回后由调用方统一执行 agent 数据落盘。
'''
''' 远程关闭：Flute 的 HttpSocket 内置 OPTIONS /ctrl/kill 端点，
''' 通过 <see cref="Configuration.shutdown_token"/>（对应请求头 X-Shutdown-Token 严格匹配）
''' 控制启用；令牌为空则远程关闭整体禁用。
''' </summary>
Public Class EmberWebServer

    ReadOnly _agent As CompanionAgent
    ReadOnly _port As Integer
    ReadOnly _wwwroot As String
    ReadOnly _configs As Configuration
    Dim _server As HttpSocket

    ''' <summary>监听端口</summary>
    Public ReadOnly Property Port As Integer
        Get
            Return _port
        End Get
    End Property

    ''' <summary>静态文件根目录（Web 前端）</summary>
    Public ReadOnly Property Wwwroot As String
        Get
            Return _wwwroot
        End Get
    End Property

    ''' <summary>
    ''' 创建 HTTP 服务封装；构造时预检端口占用情况。
    ''' </summary>
    ''' <param name="agent">共享的情感陪伴智能体实例</param>
    ''' <param name="port">监听端口</param>
    ''' <param name="wwwroot">Web 静态文件根目录</param>
    ''' <param name="shutdownToken">远程关闭令牌（空字符串=禁用远程关闭）</param>
    Public Sub New(agent As CompanionAgent, port As Integer, wwwroot As String, shutdownToken As String)
        If Not Tcp.PortIsAvailable(port) Then
            Throw New InvalidOperationException($"端口 {port} 已被其他程序占用，无法启动 HTTP 服务模式。" &
                                                $"请关闭占用程序，或通过命令行 --port / settings.ini [web] http_port 更换端口。")
        End If

        _agent = agent
        _port = port
        _wwwroot = wwwroot
        _configs = New Configuration With {
            .shutdown_token = If(shutdownToken, "").Trim(),
            .silent = True
        }
    End Sub

    ''' <summary>
    ''' 组装路由（反射注册 API 控制器）并挂载静态文件系统。本方法不监听端口，
    ''' 实际开始接受请求需调用 <see cref="Run"/>。
    ''' </summary>
    Public Sub Start()
        ' 1. 反射注册 /api/* 端点
        Dim router As New HttpRouter(New EmberApiController(_agent))

        ' 2. 挂载静态文件服务（Web 前端），静态命中优先于 API 路由
        Call router.MountFs(New WebFileSystemListener(_wwwroot))

        ' 3. 组装 HttpSocket（携带 shutdown_token 配置，启用 Flute 内置远程关闭端点）
        _server = New HttpSocket(router, _port, configs:=_configs)
    End Sub

    ''' <summary>
    ''' 阻塞式运行 HTTP 服务（主线程调用，后台服务常驻）。
    ''' 本地 Ctrl+C 或远程 OPTIONS /ctrl/kill（令牌匹配）触发 <see cref="Shutdown"/> 后，
    ''' 本方法返回，调用方继续执行统一的落盘退出流程。
    ''' </summary>
    ''' <returns>服务运行状态码（0=正常关闭）</returns>
    Public Function Run() As Integer
        If _server IsNot Nothing Then
            Return _server.Run()
        Else
            Return -1
        End If
    End Function

    ''' <summary>
    ''' 优雅关闭：停止接受新连接，等待在途请求处理完成（最多约 10 秒）。
    ''' 调用后 <see cref="Run"/> 将返回。可安全重复调用。
    ''' </summary>
    Public Sub Shutdown()
        If _server IsNot Nothing Then
            Call _server.Shutdown()
        End If
    End Sub
End Class
