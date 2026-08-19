---
name: Ember 体验优化：实时思考流/多主题/头像系统/每日日记
overview: 四项体验优化：1) Web 界面实时流式展示 LLM 思考过程与回复生成（HookResponseStream + 轮询端点）；2) 前端多套预设 light 主题 + localStorage 记忆；3) Ember 头像系统（后台扫描 G:\Ember\agent\web\resource\images\avatars 供 Web 选择、默认 ember_default.jpg、localStorage 记忆、丢失回退链）；4) Agent 每日日记（当日对话总结成日记，CLI 与 Web 均可查看与生成）。
design:
  architecture:
    framework: html
  styleKeywords:
    - Light主题
    - 清新活泼
    - 五套渐变主题
    - 珊瑚橙
    - 薄荷绿
    - 天空蓝
    - 樱花粉
    - 柠檬黄
    - 圆角卡片
    - 柔彩阴影
    - 微动效
    - 实时流式渲染
  fontSystem:
    fontFamily: PingFang SC
    heading:
      size: 22px
      weight: 700
    subheading:
      size: 16px
      weight: 600
    body:
      size: 15px
      weight: 400
  colorSystem:
    primary:
      - "#FF8A65"
      - "#FFB88C"
      - "#FF6F61"
      - "#4ECDA4"
      - "#5BB8F5"
      - "#FF9EB5"
      - "#F5C518"
    background:
      - "#FFF9F0"
      - "#F2FBF7"
      - "#F0F8FF"
      - "#FFF5F8"
      - "#FFFBEB"
      - "#FFFFFF"
    text:
      - "#4A3B32"
      - "#8D7B6E"
      - "#FFFFFF"
    functional:
      - "#56C596"
      - "#FF5A5F"
      - "#FFD93D"
      - "#6EC6FF"
todos:
  - id: live-thinking-backend
    content: CompanionAgent 增加流式缓冲与实时快照（HookResponseStream），新增 GET /api/chat/live 轮询端点
    status: completed
  - id: avatar-backend
    content: EmberConfig 增加 avatar_dir 配置，EmberWebServer 双目录挂载，GET /api/avatars 扫描，生成默认头像图片
    status: completed
  - id: diary-backend
    content: 新建 DiaryStore 持久化模块，CompanionAgent 日记生成与每日自动触发，三个日记 API 与 CLI /diary 命令组
    status: completed
  - id: theme-frontend
    content: style.css 编写五套主题变量组与主题切换弹层，localStorage 记忆用户偏好
    status: completed
  - id: frontend-integration
    content: app.js 整合：实时思考轮询渲染、头像系统与三级回退、日记卡片与弹层
    status: completed
    dependencies:
      - live-thinking-backend
      - avatar-backend
      - diary-backend
      - theme-frontend
  - id: build-verify
    content: 构建并端到端验证全部新 API 与 CLI 命令，用 [skill:agent-browser] 检验主题/头像/日记/实时思考界面
    status: completed
    dependencies:
      - frontend-integration
---

## 产品概述

对 Ember 情感陪伴智能体进行四项使用体验升级，覆盖 Web 界面与智能体能力两个层面。

## 核心功能

- **实时思考展示**：对话等待期间，网页上实时滚动显示模型的思考过程与正在逐段生成的回复；回复完成后思考过程收起为可折叠区块供回看
- **多主题切换**：提供珊瑚橙、薄荷绿、天空蓝、樱花粉、柠檬黄五套清新亮丽的浅色主题；用户在主题面板中一键切换全站配色，偏好自动本地记忆，下次打开自动应用
- **头像系统**：智能体默认显示指定默认头像图片；后台扫描头像资源文件夹生成可选头像列表，用户在头像选择面板（缩略图网格）中挑选，偏好本地记忆；所选图片缺失时自动回退默认图，默认图也缺失时回退表情符号
- **每日日记**：智能体每天首次完成对话后自动总结当日交流内容，以第一人称写一篇陪伴日记（标题+正文）并持久保存；用户可通过命令行命令组与网页日记面板（日期列表、正文阅读、手动重写）查看每天的日记

## 技术方案

### 需求1：实时思考过程（流式轮询方案）

- **CompanionAgent.vb** 新增实时状态缓冲：私有 `_liveSync As Object` 锁 + `LiveChatState`（active/phase/thinkSoFar/outputSoFar/turn）；`ChatCoreLockedAsync` 调用 `_mainClient.Chat` 前通过已验证的 `LLMClient.HookResponseStream(getOutputToken, getThinkToken)` 注册回调（LLMClient.vb 124 行），回调在轻量锁内追加 StringBuilder 并刷新快照；phase 随首个 output token 从 thinking 切换到 replying，结束/异常置 idle
- 新增 `GetLiveChatSnapshot()`：仅取 `_liveSync` 短锁拷贝字符串快照，**不进入 agent 互斥门**（关键：否则轮询请求会被对话锁阻塞而失效）
- **EmberApiController.vb** 新增 `GET /api/chat/live` 返回 `{active, phase, think, output, turn}` 快照信封
- **app.js**：发送消息后 300ms 轮询该端点——等待期实时渲染思考气泡（限高自动滚动），output 出现后直接增量渲染回复正文；POST /api/chat 返回后停止轮询并用完整文本定格，思考区收起为现有折叠样式；打字机效果仅作为无实时数据时的兜底

### 需求2：多主题

- **style.css**：现有 CSS 变量令牌（--coral-*/--coral-grad/--shadow-coral/--bg-*/--mint-soft 等）保持名称不变，按 `body[data-theme="coral|mint|sky|sakura|lemon"]` 五组重定义值（含渐变与柔彩阴影联动变色）
- **index.html** 顶栏加主题按钮与主题选择弹层（色板圆点+名称）；**app.js** 主题管理器：读 `localStorage["ember-theme"]`（默认 coral）→ 设置 `document.body.dataset.theme`，选择即存即生效

### 需求3：头像系统

- **EmberConfig.vb**：新增 `[web] avatar_dir`（默认 `G:\Ember\agent\web\resource\images\avatars`，ini 可覆盖，相对路径基于 exe 目录解析）+ `AvatarDir` 只读属性 + `DiaryDir`（`data\diary`）；`WriteDefaultIni` 同步新增键与注释
- **EmberWebServer.vb**：`MountFs` 改为双根挂载 `New WebFileSystemListener(New FileSystem(wwwroot), New FileSystem(agentWebRoot))`（已验证 ParamArray 构造与多根逐个 FileExists 查找逻辑）；agentWebRoot 由 avatarDir 上溯三级推导（avatars→images→resource→web），目录不存在时退化为单根；前端经 `/resource/images/avatars/<文件名>` 直接命中物理图片，无需改 Flute
- **EmberApiController.vb** 新增 `GET /api/avatars`：扫描 avatarDir（*.jpg/*.png/*.webp/*.gif，排序，默认图置首）返回 `{default:"ember_default.jpg", urlPrefix:"/resource/images/avatars/", avatars:[...]}`
- **默认头像生成**：执行阶段用 PowerShell System.Drawing 绘制 256x256 珊瑚橙渐变+字母标识的 `ember_default.jpg` 写入目标目录（目录不存在则创建），后续用户可自行替换同名文件
- **前端**：Ember 全部头像位（侧栏人设卡/聊天气泡/思考动画）改 `<img>`，`onerror` 三级回退链（所选图→ember_default.jpg→隐藏 img 显示 emoji）；头像选择弹层为缩略图网格，选择存 `localStorage["ember-avatar"]`

### 需求4：每日日记

- **新建 `Diary\DiaryStore.vb`**：`DiaryEntry`（date/title/content/generatedAt/turnCount）DTO + 按 `data\diary\yyyy-MM-dd.json` 的 Load/Save/ListDates（倒序）；
- **CompanionAgent.vb** 新增 `WriteDiaryAsync()`（互斥门内核心 `WriteDiaryCoreAsync`）：筛当日 00:00 后 user/assistant 消息→复用总结客户端（临时切换 system_message 为日记写作者角色，请求后恢复，GreetAsync 同款模式）→第一行为标题（剥离“标题：”前缀，缺省“X月X日的陪伴日记”）、其余为正文→落盘
- **自动触发**：`ChatCoreLockedAsync` 完成一轮成功对话后，若 `_lastAutoDiaryDate <> 今日` 则置位标志并 `Task.Run` 后台执行（WriteDiaryAsync 自行获取互斥门，不阻塞 POST /api/chat 返回；每天仅自动尝试一次，失败可手动触发）
- **CLI（Program.vb）**：`/diary`（看今日）、`/diary gen`（生成/重写今日）、`/diary list`（全部日期+标题）、`/diary show yyyy-MM-dd`
- **Web API**：`GET /api/diary/list` → `{diaries:[{date,title}]}`；`GET /api/diary?date=`（缺省今日）→ `{exists,date,title,content,generatedAt}`；`POST /api/diary/generate` → `{ok,date}`（互斥同步等待，画像 refresh 同款）
- **前端**：侧栏新增日记卡（今日标题/摘要+查看+重写按钮）与日记弹层（左列日期列表+右侧正文排版）

### 数据流

```mermaid
graph TD
    A[POST /api/chat 开始] --> B[HookResponseStream 回调]
    B -->|轻量锁| C[LiveChatState 快照]
    D[前端 300ms 轮询 /api/chat/live] --> C
    C -->|实时渲染| E[思考气泡+增量回复]
    A -->|返回| F[停止轮询 定格完整文本]
    G[对话完成] -->|每日首次| H[Task.Run 后台写日记]
    H --> I[data/diary/yyyy-MM-dd.json]
    J[CLI /diary 与 /api/diary] --> I
    K[GET /api/avatars] --> L[扫描 avatars 目录]
    M[/resource/images/avatars/*] --> N[双根 MountFs 静态服务]
```

### 执行要点

- `/api/chat/live` 与 `/api/avatars`、`/api/diary/list`、`/api/diary`（读）均不进入互斥门，避免阻塞；`/api/diary/generate` 与 chat/profile 一样走门
- HookResponseStream 钩子在 CLI 模式同样生效但无人轮询，无害；LLMClient 原有 Console.Write 流式输出保持（服务日志）
- 主题/头像偏好均为纯前端 localStorage，不新增后端配置
- 多标签页同时打开时 live 端点反映全局唯一进行中的对话（v1 已知行为，文档化）
- 不修改 Ollama/Flute 库；所有后端改动限于 Ember 项目

## 设计方案

在现有 light 清新活泼四区块布局（顶栏+侧栏三卡+聊天区+输入区）基础上扩展四组界面元素，视觉语言延续圆角卡片、柔彩阴影、微动效：

- **实时思考面板**：等待回复时，Ember 气泡上方出现浅色虚线边框的“正在思考”面板，思考文字实时滚动（限高、内滚动条），顶部小圆点跳动动画；回复开始生成后思考面板自动收起为灰字折叠条，回复正文在珊瑚橙白字气泡内逐段浮现
- **主题系统**：顶栏新增调色盘图标按钮，弹出主题选择面板——五枚大色板圆点（每枚为对应主题主渐变）+主题名，当前主题带描边选中态与勾选角标；切换瞬时生效（CSS 变量过渡动画 0.3s），顶部滑入薄荷绿 toast 确认
- **头像系统**：人设卡圆形头像由 emoji 改为真实图片（带柔彩描边阴影）；点击头像打开选择弹层：缩略图圆角方格网（悬停放大+选中珊瑚橙描边勾选角标），首格为默认头像；聊天气泡头像同步跟随，加载失败自动回退 emoji 保证永不空白
- **日记面板**：侧栏新增日记卡片（纸质感浅色卡+书本图标，显示今日日记标题与首行摘要，未生成时显示引导文案与“写日记”按钮）；点击打开日记弹层——左侧窄列日期列表（选中项主题色胶囊高亮），右侧日记正文（标题+生成时间+首人称正文排版，纸面滚动区）；“重新写一篇”按钮带确认
- 所有新弹层复用现有 modal 弹出动画（弹性缩放+背景模糊），移动端窄屏下全宽适配

## Agent Extensions

### Skill

- **agent-browser**
- Purpose: 在最终验证阶段打开 Ember Web 页面，实际操作主题切换、头像选择、日记面板与实时思考展示，截图确认四项体验优化的视觉效果
- Expected outcome: 获得五套主题、头像选择弹层、日记弹层与实时思考渲染的可视化验证截图，确认 localStorage 偏好在刷新后保持