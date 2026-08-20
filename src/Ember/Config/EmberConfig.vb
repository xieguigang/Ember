Imports System.IO
Imports Microsoft.VisualBasic.ComponentModel.Ranges.Unit
Imports Microsoft.VisualBasic.ComponentModel.Settings.Inf
Imports Ollama

Namespace AgentRuntime

    ''' <summary>
    ''' Ember 情感陪伴智能体配置模型。
    ''' 通过 settings.ini（复用 GCModeller Core 的 <see cref="IniFile"/> 模块读写）
    ''' 管理 LLM 连接信息与智能体运行参数；首次运行自动生成带注释的默认配置文件，
    ''' 之后程序启动时从该文件恢复配置。
    ''' </summary>
    Public Class EmberConfig

        ''' <summary>LLM 后端类型常量：本地 Ollama 服务</summary>
        Public Const PROVIDER_OLLAMA As String = "ollama"
        ''' <summary>LLM 后端类型常量：OpenAI 兼容 API 服务</summary>
        Public Const PROVIDER_OPENAI As String = "openai"

        Private Const SECTION_LLM As String = "llm"
        Private Const SECTION_AGENT As String = "agent"
        Private Const SECTION_WEB As String = "web"

        ' ==================== [llm] LLM 连接配置 ====================

        ''' <summary>LLM 后端类型：ollama 或 openai（读取时不区分大小写，其余值按 ollama 处理）</summary>
        Public Property provider As String = PROVIDER_OLLAMA

        ''' <summary>Ollama 服务器地址（host:port）</summary>
        Public Property server As String = "127.0.0.1:11434"

        ''' <summary>对话所用模型名称</summary>
        Public Property model As String = "qwen3:8b"

        ''' <summary>OpenAI 兼容服务的 API 基础地址（provider = openai 时生效）</summary>
        Public Property api_base As String = "https://api.openai.com"

        ''' <summary>OpenAI 兼容服务的 API Key（provider = openai 时生效）</summary>
        Public Property api_key As String = ""

        ''' <summary>对话温度（0~1，情感陪伴对话建议 0.5~0.8，越高回复越有创造性）</summary>
        Public Property temperature As Double = 0.7

        ' ==================== [agent] 智能体运行参数 ====================

        ''' <summary>每 N 轮用户输入后触发一次用户性格画像总结</summary>
        Public Property profile_interval As Integer = 5

        ''' <summary>画像总结时参考的最近对话消息条数上限</summary>
        Public Property recent_window As Integer = 20

        ''' <summary>对话上下文最大 token 估算上限，超过后自动从最旧端裁剪（被裁剪内容仍保留在持久化文件与检索索引中）</summary>
        Public Property max_context_tokens As Integer = 128 * ByteSize.KB

        ''' <summary>运行时数据目录（相对路径时相对于配置文件所在目录解析）</summary>
        Public Property data_dir As String = "data"

        ''' <summary>是否每轮对话后自动保存对话历史（False 时仅在退出或手动 /save 命令时保存）</summary>
        Public Property autosave As Boolean = True

        ' ==================== [web] HTTP 服务模式配置 ====================

        ''' <summary>HTTP 服务模式监听端口（--http 启动时生效，命令行 --port 可覆盖）</summary>
        Public Property http_port As Integer = 8080

        ''' <summary>Web 静态文件根目录（空=自动探测：exe 同级 web → 向上逐级查找；命令行 --wwwroot 可覆盖）</summary>
        Public Property wwwroot As String = ""

        ''' <summary>
        ''' 远程关闭令牌：非空时启用 Flute 内置的 OPTIONS /ctrl/kill 远程关闭端点，
        ''' Web 端携带 X-Shutdown-Token 请求头匹配此值即可远程安全关闭服务（关闭前自动保存全部记忆数据）；
        ''' 留空则禁用远程关闭（默认）。注意：请勿使用弱口令，任何知道此 token 的访问者都可以关闭服务。
        ''' </summary>
        Public Property shutdown_token As String = ""

        ''' <summary>
        ''' 头像图片目录：Web 界面头像选择列表的扫描来源，其中的文件经
        ''' /resource/images/avatars/&lt;文件名&gt; 静态路由对外提供。
        ''' 留空=自动探测（从 exe 目录向上逐级查找 agent\web\resource\images\avatars，
        ''' 开发环境命中 G:\Ember\agent\web\resource\images\avatars）。
        ''' </summary>
        Public Property avatar_dir As String = ""

        ''' <summary>
        ''' 本地 TTS 语音合成服务地址（返回 wav 音频流），Web 端经后端 /api/tts 代理访问以规避跨域。
        ''' 服务地址应带结尾斜杠，例如 http://127.0.0.1:9880/
        ''' </summary>
        Public Property tts_url As String = "http://127.0.0.1:9880/"

        ''' <summary>
        ''' 转发给 TTS 服务的语言参数（与 tts_url 的 text_language 查询参数对应）。
        ''' </summary>
        Public Property tts_language As String = "zh"

        ''' <summary>
        ''' Web 前端密码锁开关：true 时启用，用户在进入 Web 聊天界面前必须输入正确密码（web_password）换取会话令牌，
        ''' 之后所有 /api/* 请求携带该令牌由后端统一鉴权；false（默认）时不启用，前端行为与旧版完全一致（向后兼容）。
        ''' 配合反向代理 HTTPS 可安全将服务暴露到公网。
        ''' </summary>
        Public Property enable_password As Boolean = False

        ''' <summary>
        ''' Web 前端密码锁密码：enable_password 为 true 时生效，用户输入需与此值一致方可解锁。
        ''' 注意：明文存储在 settings.ini 中，请使用强口令并妥善保管配置文件；建议配合 HTTPS 避免传输嗅探。
        ''' </summary>
        Public Property web_password As String = ""

        ' ==================== 派生路径（运行时数据落盘位置） ====================

        ''' <summary>配置文件绝对路径</summary>
        Public ReadOnly Property IniFilePath As String

        Dim _dataDirectory As String = ""

        ''' <summary>运行时数据目录（绝对路径，已确保创建）</summary>
        Public ReadOnly Property DataDirectory As String
            Get
                Return _dataDirectory
            End Get
        End Property

        ''' <summary>Agent 人设持久化文件（JSON）</summary>
        Public ReadOnly Property PersonaFilePath As String
            Get
                Return Path.Combine(DataDirectory, "agent_persona.json")
            End Get
        End Property

        ''' <summary>用户性格画像持久化文件（JSON）</summary>
        Public ReadOnly Property ProfileFilePath As String
            Get
                Return Path.Combine(DataDirectory, "user_profile.json")
            End Get
        End Property

        ''' <summary>对话历史持久化文件（JSON，由 MemoryPersistsStorage 维护）</summary>
        Public ReadOnly Property ChatHistoryFilePath As String
            Get
                Return Path.Combine(DataDirectory, "chat_history.json")
            End Get
        End Property

        ''' <summary>
        ''' 长期记忆归档文件路径（JSONL）。所有被上下文裁剪/压缩丢弃的对话会归档到此文件并建立全文索引，
        ''' 使 LLM 能够通过 recall_longterm_memory 工具召回被遗忘的记忆。仅保存被丢弃的历史，活跃窗口仍存于 chat_history.json。
        ''' </summary>
        Public ReadOnly Property MemoryArchiveFilePath As String
            Get
                Return Path.Combine(DataDirectory, "memory_archive.jsonl")
            End Get
        End Property


        ''' <summary>主对话上下文日志文件（JSONL，由 ChatContextMemory 维护）</summary>
        Public ReadOnly Property ContextLogFilePath As String
            Get
                Return Path.Combine(DataDirectory, "context_log.jsonl")
            End Get
        End Property

        ''' <summary>画像总结客户端上下文日志文件（JSONL）</summary>
        Public ReadOnly Property SummaryLogFilePath As String
            Get
                Return Path.Combine(DataDirectory, "summary_log.jsonl")
            End Get
        End Property

        Dim _avatarDir As String = ""

        ''' <summary>头像图片目录（绝对路径，自动探测或 ini [web] avatar_dir 指定；目录可能不存在，由调用方容错）</summary>
        Public ReadOnly Property AvatarDir As String
            Get
                Return _avatarDir
            End Get
        End Property

        ''' <summary>每日日记持久化目录（data\diary，绝对路径）</summary>
        Public ReadOnly Property DiaryDir As String
            Get
                Return Path.Combine(DataDirectory, "diary")
            End Get
        End Property

        ''' <summary>
        ''' TTS 合成音频缓存目录（与 data 目录平级，即 &lt;data_dir 父级&gt;\cache）。
        ''' 单段语音以 SHA256(文本|语言) 命名落盘，容量超上限时按最旧优先淘汰。
        ''' </summary>
        Public ReadOnly Property CacheDirectory As String
            Get
                ' data_dir 默认相对配置文件所在目录解析，故 cache 与 data 同父级
                Dim parent As String = Path.GetDirectoryName(DataDirectory)
                If String.IsNullOrEmpty(parent) Then parent = DataDirectory
                Return Path.Combine(parent, "cache")
            End Get
        End Property

        ''' <summary>
        ''' 头像目录的父级 Web 根（agent\web）：MountFs 静态服务第二根，
        ''' 使 /resource/images/avatars/* 命中 avatarDir 中的物理文件。
        ''' 目录不存在时返回空字符串（调用方退化为单根挂载）。
        ''' </summary>
        Public ReadOnly Property AgentWebRoot As String
            Get
                ' avatarDir 形如 ...\agent\web\resource\images\avatars → 上溯三级到 agent\web
                If String.IsNullOrWhiteSpace(AvatarDir) Then Return ""

                Dim dir As New DirectoryInfo(AvatarDir)
                If dir.Parent Is Nothing Then Return ""                    ' images
                If dir.Parent.Parent Is Nothing Then Return ""            ' resource
                Dim webRoot As DirectoryInfo = dir.Parent.Parent.Parent    ' web
                If webRoot Is Nothing Then Return ""

                If String.Equals(webRoot.Name, "web", StringComparison.OrdinalIgnoreCase) AndAlso webRoot.Exists Then
                    Return webRoot.FullName
                Else
                    Return ""
                End If
            End Get
        End Property

        Private Sub New(iniFilePath As String)
            Me.IniFilePath = iniFilePath
        End Sub

        ''' <summary>
        ''' 加载配置文件；若文件不存在则先生成带注释的默认配置再加载。
        ''' </summary>
        ''' <param name="iniPath">
        ''' 配置文件路径，默认为程序可执行文件所在目录下的 settings.ini。
        ''' </param>
        ''' <returns>解析完成的配置对象（所有键缺失/非法时均安全回退默认值）</returns>
        Public Shared Function LoadOrCreate(Optional iniPath As String = Nothing) As EmberConfig
            If String.IsNullOrWhiteSpace(iniPath) Then
                iniPath = Path.Combine(App.ProductProgramData, "settings.ini")
            End If

            iniPath = Path.GetFullPath(iniPath)

            If Not File.Exists(iniPath) Then
                Call WriteDefaultIni(iniPath)
                Call Console.WriteLine($"[Config] 未找到配置文件，已生成默认配置: {iniPath}")
            End If

            Dim cfg As New EmberConfig(iniPath)
            Call cfg.ReadFromIni()
            Return cfg
        End Function

        ''' <summary>
        ''' 依据 provider 字段构造对应的 LLM 后端提供者实例。
        ''' </summary>
        Public Function CreateProvider() As ILLMProvider
            If String.Equals(provider, PROVIDER_OPENAI, StringComparison.OrdinalIgnoreCase) Then
                Return New OpenAIProvider(api_base, api_key)
            Else
                Return New OllamaProvider(server)
            End If
        End Function

        ''' <summary>
        ''' 后端连接信息描述（用于启动时展示，不泄露 api_key 明文）。
        ''' </summary>
        Public Function DescribeEndpoint() As String
            If String.Equals(provider, PROVIDER_OPENAI, StringComparison.OrdinalIgnoreCase) Then
                Return $"openai 兼容后端 {api_base} / 模型 {model}"
            Else
                Return $"ollama 后端 http://{server} / 模型 {model}"
            End If
        End Function

        ' ==================== ini 读写内部实现 ====================

        ''' <summary>
        ''' 从 ini 文件读取全部配置项；单个键解析失败时保留默认值，不中断启动。
        ''' </summary>
        Private Sub ReadFromIni()
            Using ini As New IniFile(IniFilePath)
                provider = NormalizeProvider(ini.ReadValue(SECTION_LLM, NameOf(provider), provider))
                server = ini.ReadString(SECTION_LLM, NameOf(server), server)
                model = ini.ReadString(SECTION_LLM, NameOf(model), model)
                api_base = ini.ReadString(SECTION_LLM, NameOf(api_base), api_base)
                api_key = ini.ReadString(SECTION_LLM, NameOf(api_key), api_key)
                temperature = ini.ReadDouble(SECTION_LLM, NameOf(temperature), temperature)

                profile_interval = ini.ReadInt32(SECTION_AGENT, NameOf(profile_interval), profile_interval)
                recent_window = ini.ReadInt32(SECTION_AGENT, NameOf(recent_window), recent_window)
                max_context_tokens = ini.ReadInt32(SECTION_AGENT, NameOf(max_context_tokens), max_context_tokens)
                data_dir = ini.ReadString(SECTION_AGENT, NameOf(data_dir), data_dir)
                autosave = ini.ReadBoolean(SECTION_AGENT, NameOf(autosave), autosave)

                http_port = ini.ReadInt32(SECTION_WEB, NameOf(http_port), http_port)
                wwwroot = ini.ReadString(SECTION_WEB, NameOf(wwwroot), wwwroot)
                shutdown_token = ini.ReadString(SECTION_WEB, NameOf(shutdown_token), shutdown_token)
                avatar_dir = ini.ReadString(SECTION_WEB, NameOf(avatar_dir), avatar_dir)
                tts_url = ini.ReadString(SECTION_WEB, NameOf(tts_url), tts_url)
                tts_language = ini.ReadString(SECTION_WEB, NameOf(tts_language), tts_language)
                enable_password = ini.ReadBoolean(SECTION_WEB, NameOf(enable_password), enable_password)
                web_password = ini.ReadString(SECTION_WEB, NameOf(web_password), web_password)
            End Using

            ' 数值参数合法性保护
            If profile_interval < 1 Then profile_interval = 1
            If recent_window < 2 Then recent_window = 2
            If max_context_tokens < 4096 Then max_context_tokens = 4096
            If temperature < 0 Then temperature = 0
            If temperature > 2 Then temperature = 2
            If String.IsNullOrWhiteSpace(model) Then model = "qwen3:8b"
            If http_port < 1 OrElse http_port > 65535 Then http_port = 8080
            shutdown_token = If(shutdown_token, "").Trim()

            ' 解析数据目录（相对路径相对于配置文件所在目录），并确保目录存在
            Dim dir As String = data_dir
            If String.IsNullOrWhiteSpace(dir) Then dir = "data"
            If Not Path.IsPathRooted(dir) Then
                dir = Path.Combine(Path.GetDirectoryName(IniFilePath), dir)
            End If

            _dataDirectory = Path.GetFullPath(dir)
            Call Directory.CreateDirectory(_dataDirectory)

            ' 解析头像目录：ini 显式配置优先（相对路径基于 exe 目录）；空则自动探测
            _avatarDir = ResolveAvatarDir()
        End Sub

        ''' <summary>
        ''' 解析头像图片目录：ini [web] avatar_dir 显式配置（相对路径基于 exe 目录）
        ''' → 自动探测（从 exe 目录向上逐级查找 agent\web\resource\images\avatars，最多 8 级）
        ''' → 回退 exe 同级 avatars。
        ''' </summary>
        Private Function ResolveAvatarDir() As String
            If Not String.IsNullOrWhiteSpace(avatar_dir) Then
                Return Path.GetFullPath(ResolveRelative(avatar_dir.Trim()))
            End If

            Dim exeDir As String = App.HOME
            Dim dir As New DirectoryInfo(exeDir)

            For i As Integer = 0 To 8
                Dim candidate As String = Path.Combine(dir.FullName, "agent", "web", "resource", "images", "avatars")
                If Directory.Exists(candidate) Then
                    Return candidate
                End If
                If dir.Parent Is Nothing Then Exit For
                dir = dir.Parent
            Next

            Return Path.Combine(exeDir, "avatars")
        End Function

        ''' <summary>生成带注释的默认配置文件（目录不存在时自动创建）。</summary>
        Private Shared Sub WriteDefaultIni(iniPath As String)
            Call Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(iniPath)))

            Using ini As New IniFile(iniPath)
                Call ini.comments.Add("Ember 情感陪伴智能体配置文件")
                Call ini.comments.Add("首次启动自动生成；手动修改后重启程序生效")

                Call ini.WriteValue(SECTION_LLM, NameOf(provider), PROVIDER_OLLAMA, "LLM 后端类型: ollama 或 openai")
                Call ini.WriteValue(SECTION_LLM, NameOf(server), "127.0.0.1:11434", "Ollama 服务器地址 (host:port)，provider 为 ollama 时生效")
                Call ini.WriteValue(SECTION_LLM, NameOf(model), "qwen3:8b", "对话所用模型名称，可执行: ollama pull <model> 拉取模型")
                Call ini.WriteValue(SECTION_LLM, NameOf(api_base), "https://api.openai.com", "OpenAI 兼容服务地址，provider 为 openai 时生效")
                Call ini.WriteValue(SECTION_LLM, NameOf(api_key), "", "OpenAI 兼容服务的 API Key，provider 为 openai 时生效")
                Call ini.WriteValue(SECTION_LLM, NameOf(temperature), "0.7", "对话温度 0~1，情感陪伴建议 0.5~0.8")

                Call ini.WriteValue(SECTION_AGENT, NameOf(profile_interval), "5", "每 N 轮用户输入触发一次用户性格画像总结")
                Call ini.WriteValue(SECTION_AGENT, NameOf(recent_window), "20", "画像总结时参考的最近对话消息条数上限")
                Call ini.WriteValue(SECTION_AGENT, NameOf(max_context_tokens), "1000000", "对话上下文最大 token 估算上限，超过后自动裁剪最旧消息")
                Call ini.WriteValue(SECTION_AGENT, NameOf(data_dir), "data", "运行时数据目录（人设/画像/对话历史），相对路径基于本配置文件所在目录")
                Call ini.WriteValue(SECTION_AGENT, NameOf(autosave), "true", "true 时每轮对话后自动保存；false 仅在退出或 /save 时保存")

                Call ini.WriteValue(SECTION_WEB, NameOf(http_port), "8080", "--http 模式监听端口，命令行 --port 可覆盖")
                Call ini.WriteValue(SECTION_WEB, NameOf(wwwroot), "", "Web 静态文件根目录；留空则自动探测（exe 同级 web 目录，或向上逐级查找名为 web 的目录）")
                Call ini.WriteValue(SECTION_WEB, NameOf(shutdown_token), "", "远程关闭令牌：设置后可在 Web 界面输入该令牌远程安全关闭服务（自动保存数据后退出）；留空禁用远程关闭")
                Call ini.WriteValue(SECTION_WEB, NameOf(avatar_dir), "", "头像图片目录：Web 头像选择列表的扫描来源；留空则自动探测（向上查找 agent\\web\\resource\\images\\avatars）")
                Call ini.WriteValue(SECTION_WEB, NameOf(tts_url), "http://127.0.0.1:9880/", "本地 TTS 语音合成服务地址（返回 wav 音频流）；Web 经后端 /api/tts 同代理访问以规避跨域，地址需带结尾斜杠")
                Call ini.WriteValue(SECTION_WEB, NameOf(tts_language), "zh", "转发给 TTS 服务的语言参数（对应 text_language 查询参数）")
                Call ini.WriteValue(SECTION_WEB, NameOf(enable_password), "false", "Web 前端密码锁开关：true 时进入聊天界面前需输入密码（web_password）换取令牌，全接口鉴权；false 关闭（默认）")
                Call ini.WriteValue(SECTION_WEB, NameOf(web_password), "", "Web 前端密码锁密码；enable_password 为 true 时生效。明文保存，请使用强口令并配合 HTTPS 部署")

                Call ini.Flush()
            End Using
        End Sub

        ' ==================== Web 静态目录解析 ====================

        ''' <summary>
        ''' 判断候选目录是否为有效的前端根目录：目录存在且包含 index.html。
        ''' （仅按目录名探测会误命中项目内同名的服务端源码目录 Web\，故以 index.html 为标志文件）
        ''' </summary>
        Private Shared Function IsWebRoot(candidate As String) As Boolean
            Return Directory.Exists(candidate) AndAlso File.Exists(System.IO.Path.Combine(candidate, "index.html"))
        End Function

        ''' <summary>
        ''' 解析 Web 静态文件根目录（三级优先：命令行 --wwwroot 覆盖 → ini [web] wwwroot 显式配置 → 自动探测）。
        ''' 自动探测：exe 同级 web 目录 → 从 exe 目录向上逐级查找名为 web 的目录（最多 6 级，
        ''' 开发环境可自动命中仓库根下的 web 文件夹）；均失败时回退 exe 同级 web 并提示。
        ''' 候选目录必须包含 index.html 才会被采纳（<see cref="IsWebRoot"/>）。
        ''' </summary>
        ''' <param name="cliOverride">命令行 --wwwroot 参数值（空表示未指定）</param>
        ''' <returns>解析出的 wwwroot 绝对路径（不校验存在性，由调用方提示）</returns>
        Public Function ResolveWwwroot(Optional cliOverride As String = Nothing) As String
            ' 1. 命令行覆盖优先
            If Not String.IsNullOrWhiteSpace(cliOverride) Then
                Return Path.GetFullPath(ResolveRelative(cliOverride.Trim()))
            End If

            ' 2. ini 显式配置
            If Not String.IsNullOrWhiteSpace(wwwroot) Then
                Return Path.GetFullPath(ResolveRelative(wwwroot.Trim()))
            End If

            ' 3. 自动探测
            Dim exeDir As String = App.HOME

            ' 3a. exe 同级 web
            Dim candidate As String = System.IO.Path.Combine(exeDir, "web")
            If IsWebRoot(candidate) Then
                Return candidate
            End If

            ' 3b. 从 exe 目录向上逐级查找名为 web 的目录（最多 6 级）
            Dim dir As DirectoryInfo = New DirectoryInfo(exeDir)
            For i As Integer = 1 To 6
                If dir.Parent Is Nothing Then Exit For
                dir = dir.Parent

                candidate = System.IO.Path.Combine(dir.FullName, "web")
                If IsWebRoot(candidate) Then
                    Return candidate
                End If
            Next

            ' 4. 均失败：回退 exe 同级 web（调用方负责提示目录不存在）
            Return System.IO.Path.Combine(exeDir, "web")
        End Function

        ''' <summary>相对路径基于 exe 所在目录解析为绝对路径。</summary>
        Private Shared Function ResolveRelative(rawPath As String) As String
            If System.IO.Path.IsPathRooted(rawPath) Then
                Return rawPath
            ElseIf rawPath.DirectoryExists Then
                Return rawPath
            Else
                Return System.IO.Path.Combine(App.HOME, rawPath)
            End If
        End Function

        Private Shared Function NormalizeProvider(value As String) As String
            If String.Equals(value, PROVIDER_OPENAI, StringComparison.OrdinalIgnoreCase) Then
                Return PROVIDER_OPENAI
            Else
                Return PROVIDER_OLLAMA
            End If
        End Function
    End Class
End Namespace