# OBS帮助助手 V3.4.0 开发指引（全量执行排序版）

> **版本**：V3.4.0（minor，功能增强版）
> **基线**：V3.3.0（`master` 提交 `c22c37f`），`net10.0-windows` 主构建 + `net6.0-windows` Win7 兼容构建
> **文档日期**：2026-10-30
> **文档状态**：执行稿（全量 3.4.0，**不含工期估算**）。本文只做功能排序、技术路线、代码落点与验收设计，**不修改任何源码、配置、安装脚本或发布物**。
> **执行对象**：AI 开发代理 / 开发人员。**按 §3.6 的 1~46 顺序逐项实现，完成一项验收一项**；不要跳项、不要并行跨依赖。
> **版本主题**：**手机联动（Phone Link）——把手机变成第二块屏幕、第二台摄像机、第二支麦克风。**
> **V3.4.0 范围**：§3 的 **46 项功能全部交付**；唯一执行顺序见 **§3.6**。不再区分 P0/P1/P2，不再按“可选/顺延”裁剪。
>
> **建议一起阅读**
>
> - [`ARCHITECTURE.md`](ARCHITECTURE.md) —— 现有分层、组合根、线程模型与安全清单
> - [`CODEBASE.md`](CODEBASE.md) —— 源码文件清单与快速定位
> - [`OPEN_SOURCE_ATTRIBUTION.md`](OPEN_SOURCE_ATTRIBUTION.md) —— 现有开源署名与许可证边界
> - [`PLAN_V3.3.0.md`](PLAN_V3.3.0.md) —— 虚拟直播工作台、Live2D、插件广场的上一版设计
> - [`V3.3.0_IMPLEMENTATION_STATUS.md`](V3.3.0_IMPLEMENTATION_STATUS.md) —— 3.3.0 已验证/待人工测试项

---

## 0. 结论速览

### 0.1 一句话

V3.4.0 不再只是“再加一个排障页面”，而是把手机接进直播工作流：
**扫码配对 → 手机遥控 / 监看 / 提词 / 拍摄 / 收音 → 回到 OBS 一键成场景**；
在这个底座上，再把弹幕与实时字幕接进来，形成“一个人也能开一场专业直播”的最小闭环。

### 0.2 能做 / 不能做

| 能力 | V3.4.0 能做到 | 明确不承诺 |
|---|---|---|
| **手机遥控** | 同一局域网扫码配对；控制场景、元素显隐、静音/音量、录制、推流、虚拟摄像头、回放、演播室模式、提词器；支持只读/操作员/管理员三种角色 | 不把 OBS 密码交给手机；不做公网远程、内网穿透、UPnP 自动开端口 |
| **手机监看** | 1~5 fps 的节目画面截图预览 + Tally 灯 + 录制/推流计时 + 静音状态 | 不是零延迟视频回传；高帧率监看走后续 WebRTC/WHIP，不计入 46 项 |
| **手机当摄像头** | 手机用 SRT/RTMP 推到**本机**媒体服务器，应用自动在 OBS 建 `ffmpeg_source` 并套布局；Android 可用 USB + scrcpy 兜底 | 不自带手机 App；不保证 iOS 浏览器直接调用摄像头；不自动改路由器/防火墙；不承诺 4K/60 |
| **手机当麦克风** | 随 SRT/RTMP 音频一起进 OBS，复用现有音频同步偏移能力 | 不承诺专业级 <100 ms 低延迟；不做无损音频 |
| **手机提词器** | 手机控制滚动/暂停/速度/字号/镜像；可同步显示稿件 | 3.4.0 不做手机端编辑稿件（不在 46 项内，后续增强） |
| **弹幕聚合** | B 站、Twitch 的公开只读弹幕；统一消息流、上屏 overlay、可选 TTS | 抖音/快手没有公开弹幕 API，不破解、不抓登录态；不自动发弹幕、不存账号 |
| **实时字幕** | 本地 Whisper/Vosk 转写，中/英字幕推到 OBS 文本源或 overlay；可选云端翻译 | 不承诺 100% 准确；默认不上传音频；Win7 走降级方案 |
| **多平台/切片/AI 搭档** | 3.4.0 **全量交付**（按 §3.6 顺序执行） | 不承诺“零配置/全平台自动成功”；高风险项提供独立开关、回退与人工确认 |

### 0.3 交付范围与执行顺序

**§3 的 46 项功能全部进 3.4.0**，不按 P0/P1/P2 裁剪，也不按“可选/顺延”处理。
唯一权威顺序见 **§3.6**；下面的批次只用于控制合入节奏，不改变顺序。

| 批次 | 序号 | 主题 | 本批完成标志 |
|---|---|---|---|
| **0** | 前置 | 基础设施：LAN/权限、sidecar、错误码、i18n、HTTP/WS、FFmpeg、AI 工具、测试骨架 | 编译、单测、安全清单全过；后续功能可直接接入 |
| **1** | 1~5 | 手机第二屏核心：遥控、预览/Tally、提词器、补光、文件快传 | 扫码可用；权限/撤销/限流/预览/上传测试过 |
| **2** | 6~9 | 手机音视频输入：USB 兜底、Wi-Fi 摄像头、麦克风、多手机监看 | USB 引导可用；720p30 稳定；音频同步；多会话 |
| **3** | 10~12 | 多机位、远程嘉宾、手机通知/剪贴上屏 | 2~4 路切换；嘉宾进房；隐私开关与审核 |
| **4** | 13~16 | 弹幕核心：聚合、overlay、敏感词、TTS | B 站/Twitch 稳定；overlay 不崩；过滤生效 |
| **5** | 17~21 | 互动玩法：点歌/投票/抽奖、礼物特效、投稿、日历、AI 回复建议 | 本地状态机、队列、审核、人工确认 |
| **6** | 22~25 | 字幕/翻译/语音控制 | 中英字幕；翻译；语音命令白名单 |
| **7** | 26~31 | 独立智能与诊断：复盘、封面、推流健康、构图、预案、事故回放 | 报告/建议可导出；不依赖未完成功能 |
| **8** | 32~39 | 切片、竖屏、录制检索、AI 导播、安全哨兵、音频链、多平台、归档 | 切片/转推/归档可回滚；安全动作需确认 |
| **9** | 40~46 | 硬件、灯光、看板、插件市场、装修市场、协作、数字人 | 权限/审计/供应链/内容合规验收 |

> **执行原则**：先底座、后功能；先本地、后云端；先只读、后写入；先单机、后多端；先人工确认、后自动化。
> 后项不得在前项验收通过前开工；每项必须有独立开关与回退路径。

### 0.4 六条关键决策

1. **手机不直连 OBS。** 手机只连 PC 上的 `MobileLinkServer`，由 PC 侧 `ObsConnectionService` 执行动作；OBS 密码、推流码、API Key 永不出 PC。
2. **摄像头优先“本地媒体服务器 + SRT/RTMP”，不自研 WebRTC。** 3.4.0 用 mediamtx（MIT）作为可选 sidecar；浏览器 WHIP、纯 C# WebRTC 只做技术储备，不作为手机主链路的前置依赖。
3. **所有局域网能力默认关闭。** 用户显式开启；只绑私有地址、校验来源 IP、随机路径/一次性 token、限流、可撤销；不做 UPnP，不自动加防火墙规则。
4. **零 NuGet 不破例。** QR 生成内嵌 MIT 源码；音频采集用 P/Invoke；聊天协议手写；重能力走外部进程。若确实必须引包，先写 ADR 并同步修改 README 的“零第三方依赖”表述。
5. **GPL 只做外部进程/插件。** 不复制、不静态链接、不捆绑；sidecar 由用户确认后下载，哈希校验，许可证与来源写入 `OPEN_SOURCE_ATTRIBUTION.md`。
6. **Win7 明确降级，不假支持。** 遥控/预览/提词器可用（SSE/长轮询）；Wi-Fi 摄像头、Whisper、媒体服务器在 Win7 上关闭并解释原因，走 USB/外部应用。

---

## 1. 现状审计（有代码依据）

### 1.1 可以直接复用的底座

| 能力 | 现有落点 | 3.4.0 怎么用 | 注意事项 |
|---|---|---|---|
| OBS 连接与控制 | `Services/Obs/ObsConnectionService.cs`、`ObsWebSocketClient.cs`、`RawRequestAsync` | 手机遥控、状态同步、截图、场景/音频/录制/推流 | **不要把 `RawRequestAsync` 直接暴露给手机**；只暴露白名单动作 |
| OBS 事件 | `ObsConnectionService.OnObsEvent`：`CurrentProgramSceneChanged`、`RecordStateChanged`、`StreamStateChanged`、`VirtualcamStateChanged`、`ReplayBufferStateChanged` | Tally、计时、按钮状态 | `InputVolumeMeters` 是高频事件，默认订阅**不含**它（见 `Models/Obs/ObsProtocol.cs` 注释），按需单独开启 |
| 节目截图 | `RawRequestAsync("GetSourceScreenshot", ...)` | 手机预览（JPEG，低帧率） | 限制 480p/1~5 fps；页面不可见立即停；避免全屏原图 |
| 本地 HTTP / overlay | `Services/Live/RoomOverlayServer.cs`、`RoomOverlayAssets.cs`、`RoomOverlayCore.cs` | 手机页面、弹幕/字幕 overlay、静态资源 | 现在**只绑 127.0.0.1**、只服务内置页面；LAN 页面要另起服务，不复用回环 token 策略 |
| 提词器 | `Services/Shell/TeleprompterService.cs`、`TeleprompterCore.cs` | 手机遥控滚动/暂停/速度/字号/镜像 | 现有 `TogglePlayPause`、`SaveSettings`、`ApplyScene` 可直接复用 |
| 虚拟直播与场景 | `Services/Live/RoomService.cs`、`SceneTemplateService.cs`、`SceneTemplateCaptureCore.cs` | 手机机位场景、布局预设 | 默认新建场景集合，不覆盖用户现有配置 |
| 开播/推流 | `Services/Live/GoLiveService.cs`、`Services/Shell/SimpleStreamingService.cs` | 手机一键开播/停止 | 破坏性动作必须二次确认 |
| 录制与文件 | `SimpleRecordingService.cs`、`RecordingNamingCore.cs`、`RecordingArchiveService.cs` | 手机录制/停止/文件列表 | 复用既有错误码与文件命名规则 |
| 插件供应链 | `Services/Plugins/PluginInstallCore.cs`、`PluginStateStore.cs`、`GitHubSearchCore.cs` | sidecar 下载、哈希、回滚的现成模式 | 不要直接把插件安装器用于 sidecar；抽出公共 `SafeArchive`/`HashVerifier` |
| 存储与机密 | `HostBridge.AppDataDirectory`、`LocalStore`、`SecretCodec` | 设备 token、推流码、API Key | token 只存哈希；推流码只进 `secrets.dat`；日志脱敏 |
| 多语言 | `Localization/Strings.cs`、`StringTableZhHans.cs`、`StringTableEnUs.cs` | 手机页面、错误提示、overlay | 手机页面也要中英双语；不要在前端硬编码中文 |
| 无障碍/老年模式 | `Services/Appearance/UiProfileCore.cs`、`Themes/Palette.xaml` | 手机页面大按钮/高对比/减少动画 | 手机端触控目标至少 44×44 px；跟随应用设置 |
| 系统监控 | `Services/Shell/SystemMonitorService.cs` | 手机显示 CPU/内存/磁盘/网络摘要 | 只推摘要，不推全量采样 |
| 自检与测试 | `OBS_SELFTEST=1`、`tools/PluginSmoke`、1508 项单测（V3.3.0 基线） | 新路由/纯逻辑/事务链路 | 新路由必须进自检 `cases`；`check_docs.py` 的数字要同步 |

### 1.2 现有边界

- **零第三方 NuGet**：主工程没有任何 `PackageReference`，只依赖 BCL；这是产品卖点，也是一条硬约束。
- **双 TFM**：`net10.0-windows`（Win10/11）与 `net6.0-windows`（Win7 SP1+，`WIN7_COMPAT`）。
- **离线优先**：知识库、排障、模板、插件目录都可离线；联网只发生在用户主动诊断/更新/插件下载。
- **无 WebView2**：界面是原生 WPF；overlay 由 OBS 的 browser source 渲染，主程序不内嵌浏览器。
- **无 UI Automation 承诺**：不自动点第三方直播软件按钮、不自动登录、不绕过验证码。
- **主程序 MIT**；GPL 代码不进主程序，不复制、不静态链接。
- **`RoomOverlayServer` 的边界**：只绑回环、随机端口、一次性 token、只读内置页面、CSP `default-src 'none'`。它**不能**直接承载局域网手机页面。

### 1.3 3.4.0 要补的缺口

1. **局域网服务**：绑定私网地址、二维码配对、设备注册、会话 token、角色权限、撤销、审计。
2. **手机 Web UI**：无框架、无远程 CDN、CSP、白名单命令、中英双语、大触控目标。
3. **媒体服务器与 sidecar 管理**：下载/哈希/解压/启动/停止/端口/许可证/进程树回收。
4. **OBS 媒体源自动化**：建场景、建 `ffmpeg_source`、音频同步、布局预设、断流重连。
5. **弹幕适配器**：B 站/Twitch 协议、重连、限流、去重、过滤、overlay、TTS。
6. **ASR 管线**：WASAPI 回环/麦克风采集、VAD、分段、Whisper/Vosk、字幕发布。
7. **视频/音频工具链**：FFmpeg 参数白名单、临时目录、输出命名、失败回滚。
8. **安全与合规**：局域网暴露面、token 生命周期、平台 ToS、模型/许可证、隐私说明。

---

## 2. 版本目标、原则与非目标

### 2.1 版本目标

1. **让手机成为 OBS 的外设**，而不是“第二个 OBS 客户端”。
2. **把“开播前/开播中”的高频操作搬到手机上**：切场景、调音量、看状态、提词、控录制。
3. **用手机补足摄像头/麦克风**，不要求用户买采集卡。
4. **给弹幕与字幕留出统一底座**：消息流、overlay、字幕管线、权限与隐私边界。
5. **保持产品可信**：默认关闭、可撤销、无静默安装、无公网暴露、零 NuGet 主程序。
6. **46 项全部交付**：按 §3.6 的依赖顺序执行，不跳步、不裁剪、不以“后续版本”替代。

### 2.2 设计原则

1. 手机是“外设”：只发高层动作，不碰 OBS 原始协议。
2. 默认关闭、最小暴露、显式授权、随时撤销。
3. 本地优先、离线可用；云能力必须显式同意。
4. 能 P/Invoke 就不引包；能 sidecar 就不静态链接；能协议复刻就不复制代码。
5. 破坏性操作二次确认；AI 不得静默执行。
6. 所有 URL、路径、参数过白名单；拒绝目录穿越、命令注入、任意文件读写。
7. 失败可解释：稳定错误码 + 用户能照做的下一步。
8. 双 TFM 同步；Win7 明确降级，不伪造能力。
9. 中英双语、无障碍、大触控目标从第一行代码开始。
10. 文档、许可证、测试与代码同批交付。
11. **顺序即依赖**：严格按 §3.6 的序号推进；后项不得在前项验收通过前开工。
12. **每项独立开关与回退**；遇到平台/许可证硬阻断时记录 `BLOCKED` 与替代方案，不静默删除功能。

### 2.3 非目标（V3.4.0 明确不做）

- 公网远程控制 OBS、内网穿透、UPnP 自动端口映射（M10 远程嘉宾的媒体走用户自建/自选 SFU，不属于“远程控制 OBS”）。
- 自研手机原生 App、上架应用商店；3.4.0 用手机浏览器 + 现有推流 App；M11 仅做 Android 可选 helper/ADB，iOS 明确不支持。
- 自研 WebRTC/SFU/编解码器。
- 抖音/快手弹幕协议逆向、自动登录、自动发弹幕、绕过验证码。
- 静默安装 sidecar、驱动、虚拟摄像头；静默提权；静默修改防火墙/杀软。
- 专业级 <100 ms 无线图传；4K/60 无线摄像头。
- 在 Win7 上运行媒体服务器/Whisper。
- 自动修改路由器、hosts、系统代理。

---

## 3. 功能池（46 项，全部 3.4.0 交付）

> 这一节是 3.4.0 的完整交付池：**46 项全部实现**，不再区分 P0/P1/P2。
> §3.1-3.5 按能力分类，便于阅读；**§3.6 是唯一权威执行顺序**。
> 每项的技术落点 / 复用见分类表，验收要点见 §3.6。

### 3.1 手机与多设备

| ID | 功能 | 用户价值 | 技术落点 / 复用 | 执行序 |
|---|---|---|---|---|
| M01 | **手机遥控台** | 躺在床上切场景、调音量、开录/停录；给助理一台只读或可控手机 | `MobileLinkServer` + `MobileCommandRouter` + `ObsConnectionService` | 1 |
| M02 | **手机监视器 + Tally** | 手机看节目画面、红/绿 Tally、录制/推流计时；低成本返送 | `GetSourceScreenshot` + OBS 事件 + `TallyService` | 2 |
| M03 | **手机当摄像头（Wi-Fi）** | 旧手机变第二机位；俯拍、特写、户外 | `MediaServerManager` + mediamtx + `ffmpeg_source` | 7 |
| M04 | **手机当麦克风** | 手机麦进 OBS；减少额外声卡 | 随 M03 的 SRT/RTMP 音频 + 音频同步偏移 | 8 |
| M05 | **手机提词器遥控** | 手机控制滚动/速度/字号/镜像；不必切回 PC | `TeleprompterService` + `TeleprompterCore` | 3 |
| M06 | **手机文件快传** | 手机截图/片段/封面一键到 PC，直接进 OBS | `POST /api/upload` + 类型/大小/路径校验 | 5 |
| M07 | **手机补光灯/色卡/倒计时** | 全屏纯色补光、白平衡卡、开播倒计时 | 手机页面全屏 CSS + 现有倒计时页 | 4 |
| M08 | **USB 零网络手机摄像头** | Wi-Fi 不稳/隔离时，Android 用 USB | scrcpy（外部工具）检测 + 引导；iOS 引导 OBS Camera/Iriun | 6 |
| M09 | **多机位轻量导播** | 2~4 台手机自动发现、切换、画中画/分屏 | 多路 mediamtx + 场景预设；远程嘉宾的 SFU 见 M10 | 10 |
| M10 | **远程嘉宾连线** | 嘉宾浏览器进房，不用装软件；混流进 OBS | LiveKit/mediasoup sidecar；WebRTC | 11 |
| M11 | **手机通知/剪贴板上屏** | 手机通知、验证码、消息一键上屏（谨慎） | Android 可选 helper/ADB；iOS 不支持；隐私开关 | 12 |
| M12 | **手机当第二预览/多手机监看** | 导演/助理各看一台，互不干扰 | 多会话 + 角色 `viewer` | 9 |

### 3.2 互动与内容

| ID | 功能 | 用户价值 | 技术落点 / 复用 | 执行序 |
|---|---|---|---|---|
| C01 | **弹幕聚合** | B 站/Twitch 弹幕进一个面板，不漏消息 | `ChatHub` + `BilibiliChatAdapter` + `TwitchChatAdapter` | 13 |
| C02 | **弹幕 overlay / 聊天框** | 直播画面内滚动弹幕、聊天框、主题皮肤 | 现有 `RoomOverlayServer` + `CommentCoreLibrary`（MIT） | 14 |
| C03 | **弹幕 TTS 朗读** | 边玩游戏边“听”弹幕；可过滤/限速 | Windows SAPI / piper sidecar；TTS 队列 | 16 |
| C04 | **弹幕点歌/投票/抽奖/问答** | 观众参与感；直播间互动玩法 | 本地命令解析 + overlay + 队列；不自动回平台 | 17 |
| C05 | **礼物/关注/进房特效** | 礼物触发 OBS 场景/滤镜/音效 | 事件 → `SceneItem`/滤镜/音频；白名单 | 18 |
| C06 | **观众投稿/来信队列** | 观众图片/文字审核后上屏 | 上传 + 审核队列 + overlay | 19 |
| C07 | **AI 弹幕回复建议** | 给主播提词，不自动发 | 本地/云端 LLM；人工确认 | 21 |
| C08 | **弹幕实时翻译** | 海外观众/外语直播 | 翻译引擎 + 双语 overlay | 24 |
| C09 | **敏感词/节奏预警** | 提前发现违规/刷屏风险 | 本地词表 + 频率检测；只提醒 | 15 |
| C10 | **直播日历/预告/倒计时** | 开播前预热、跨平台预告 | 本地日程 + overlay + 手机提醒 | 20 |

### 3.3 智能内容

| ID | 功能 | 用户价值 | 技术落点 / 复用 | 执行序 |
|---|---|---|---|---|
| S01 | **实时字幕（本地）** | 静音观看/无障碍/剪辑素材 | WASAPI + whisper.cpp/Vosk + OBS 文本源 | 22 |
| S02 | **双语字幕/实时翻译** | 海外观众、跨语言直播 | 翻译引擎 + 双语字幕行 | 23 |
| S03 | **语音控制 OBS** | 手上忙时“切场景/开录/静音” | ASR + 命令语法 + 白名单 + 唤醒词 | 25 |
| S04 | **AI 直播搭档/数字人** | 无人时也能互动；TTS + Live2D/VRM 口型 | VTS API + TTS + 参数注入；明确标注 AI | 46 |
| S05 | **高光切片** | 自动从长录像中找精彩片段 | 弹幕速率 + 音频能量 + 场景变化；FFmpeg | 32 |
| S06 | **竖屏自动重构** | 横屏录像一键变 9:16 短视频 | 人脸/主体检测 + 安全区 + FFmpeg | 33 |
| S07 | **智能封面/标题/话题** | 开播前少想文案 | LLM + 模板 + 本地图 | 27 |
| S08 | **直播复盘教练** | 用数据告诉主播下次改什么 | 现有 `SessionReviewService` + AI 报告 | 26 |
| S09 | **AI 导播/自动场景** | 按游戏/窗口/语音/时间自动切场景 | 现有 `SceneAutoSwitcher` + 规则/LLM | 35 |
| S10 | **录制文件 AI 检索** | “上次说的那句话在哪一段” | Whisper 转写 + 本地索引 + 搜索 | 34 |

### 3.4 画质 / 音频 / 网络 / 安全

| ID | 功能 | 用户价值 | 技术落点 / 复用 | 执行序 |
|---|---|---|---|---|
| Q01 | **一键多平台转推** | 一次推流到 B 站/YouTube/Twitch | mediamtx/SRS 本地转推；或外部 obs-multi-rtmp | 38 |
| Q02 | **推流健康医生** | 掉帧/断流/带宽不足时给出可执行建议 | 现有统计 + 带宽探测 + 码率建议 | 28 |
| Q03 | **AI 音频链** | 降噪、去混响、响度、齿音、变声 | RNNoise/FFmpeg/RVC；本地优先 | 37 |
| Q04 | **构图/灯光/色温检查 2.0** | 开播前提示“脸太暗/偏色/构图偏” | 现有 `ColorCheck` + 摄像头取样 | 29 |
| Q05 | **录制自动归档** | 录完自动传 NAS/网盘，防丢 | rclone（MIT）sidecar + 校验 | 39 |
| Q06 | **直播安全哨兵** | 黑屏/静音/掉帧/敏感词，一键切 BRB | 截图采样 + 音量事件 + 词表 + 恐慌按钮 | 36 |
| Q07 | **事故回放/环形缓冲** | 事故前后 30 秒一键留证 | OBS 回放缓存 + 导出 | 31 |
| Q08 | **一键开播预案/回滚** | 开播前自动检查，失败回退 | 现有预检/备份 + 运行手册 | 30 |

### 3.5 生态 / 硬件 / 协作

| ID | 功能 | 用户价值 | 技术落点 / 复用 | 执行序 |
|---|---|---|---|---|
| E01 | **Stream Deck / MIDI / 脚踏板** | 硬件控台，盲操作 | Companion 外部；WinMM MIDI P/Invoke | 40 |
| E02 | **灯光联动 WLED/OpenRGB** | 开播红灯、礼物闪灯 | WLED HTTP JSON；OpenRGB SDK 外部 | 41 |
| E03 | **直播间装修市场** | overlay 主题、边框、字体、音效一键换 | overlay 资产包 + 哈希 + 预览 + 回滚 | 44 |
| E04 | **多操作员/团队协作** | 主播 + 助理 + 导播分工 | 角色权限 + 审计 + 多会话 | 45 |
| E05 | **插件市场 2.0** | 签名、评分、依赖、沙箱 | 现有插件供应链扩展 | 43 |
| E06 | **观众数据本地看板** | 不依赖平台后台的留存/互动趋势 | 本地统计 + 图表；隐私优先 | 42 |

### 3.6 3.4.0 执行顺序总表（唯一权威排序）

> **执行时只看这张表**：从 1 到 46 顺序推进；后项不得在前项验收通过前开工。
> “批次”只用于控制合入节奏；批次 0 是前置基础设施，不计入 46 项。

| 序 | ID | 功能 | 批次 | 依赖 | 验收要点 |
|---|---|---|---|---|---|
| 1 | M01 | 手机遥控台 | 1 | 基础设施 | 扫码配对；角色权限；命令白名单；撤销/限流；状态同步 |
| 2 | M02 | 手机监视器 + Tally | 1 | M01 | 1~5 fps 预览；Tally/计时误差 <1 s；页面隐藏即停 |
| 3 | M05 | 手机提词器遥控 | 1 | M01 | 播放/暂停/速度/字号/镜像；与 PC 漂移 <1 行 |
| 4 | M07 | 手机补光灯/色卡/倒计时 | 1 | M01 | 全屏色卡/色温/倒计时；无网络请求 |
| 5 | M06 | 手机文件快传 | 1 | M01 | 50 MB；JPEG/PNG/MP4；魔数校验；用户确认后才进 OBS |
| 6 | M08 | USB 零网络手机摄像头 | 2 | 无（可与 M03 并行） | scrcpy/OBS Camera 检测与步骤；不自动装驱动 |
| 7 | M03 | 手机当摄像头（Wi-Fi） | 2 | 基础设施 | SRT/RTMP→mediamtx→OBS；720p30 连续 30 min；断流重连 |
| 8 | M04 | 手机当麦克风 | 2 | M03 | 音频随流；同步偏移 ±200 ms；无双重音频 |
| 9 | M12 | 手机第二预览/多手机监看 | 2 | M01/M02 | 3 并发会话；viewer 只读；互不干扰 |
| 10 | M09 | 多机位轻量导播 | 3 | M03/M04/M12 | 2~4 路；切换/画中画/分屏；场景预设 |
| 11 | M10 | 远程嘉宾连线 | 3 | M09 | 浏览器进房；SFU/TURN；混流进 OBS；权限/踢人 |
| 12 | M11 | 手机通知/剪贴板上屏 | 3 | M01/M06 | Android only（helper/ADB）；显式授权；只上屏确认内容；一键关闭 |
| 13 | C01 | 弹幕聚合 | 4 | 基础设施 | B 站/Twitch；2 h 稳定；断线重连；100 msg/s 不卡 |
| 14 | C02 | 弹幕 overlay / 聊天框 | 4 | C01 | 文本转义；CSP；主题；50 条上限；不崩 |
| 15 | C09 | 敏感词/节奏预警 | 4 | C01/C02 | 本地词表；频率检测；只提醒；可导出 |
| 16 | C03 | 弹幕 TTS 朗读 | 4 | C01/C02 | 队列/限速/过滤；可静音；不读 URL/表情 |
| 17 | C04 | 弹幕点歌/投票/抽奖/问答 | 5 | C01/C02/C09 | 本地状态机；冷却；overlay；不自动回平台 |
| 18 | C05 | 礼物/关注/进房特效 | 5 | C01/C02 | 事件→场景/滤镜/音效；白名单；可关 |
| 19 | C06 | 观众投稿/来信队列 | 5 | C02/M06 | 上传/审核/队列；类型/大小/路径校验 |
| 20 | C10 | 直播日历/预告/倒计时 | 5 | M01/C02 | 本地日程；overlay；手机提醒；时区正确 |
| 21 | C07 | AI 弹幕回复建议 | 5 | C01/AI | 只给建议；人工确认；不自动发；限频 |
| 22 | S01 | 实时字幕（本地） | 6 | 基础设施 | 中英；延迟 <2 s；本地；无音频上传 |
| 23 | S02 | 双语字幕/实时翻译 | 6 | S01/翻译 | 双语行；可关；云端需显式同意 |
| 24 | C08 | 弹幕实时翻译 | 6 | C01/S02 | 双语 overlay；限频；可关 |
| 25 | S03 | 语音控制 OBS | 6 | S01 | 唤醒词；命令白名单；破坏性动作确认 |
| 26 | S08 | 直播复盘教练 | 7 | 现有 SessionReview/AI | 报告/行动清单；可导出；隐私 |
| 27 | S07 | 智能封面/标题/话题 | 7 | AI | 3 套候选；可编辑；不自动发布 |
| 28 | Q02 | 推流健康医生 | 7 | 现有统计 | 带宽/掉帧/断流建议；不自动改码率 |
| 29 | Q04 | 构图/灯光/色温检查 2.0 | 7 | ColorCheck | 人脸/亮度/色温/构图评分；可关 |
| 30 | Q08 | 一键开播预案/回滚 | 7 | 预检/备份 | 检查清单；失败回滚；不静默提权 |
| 31 | Q07 | 事故回放/环形缓冲 | 7 | OBS 回放 | 前后 30 s；导出；磁盘上限 |
| 32 | S05 | 高光切片 | 8 | S01/C01/FFmpeg | 评分/窗口/去重；9:16 可选；可回滚 |
| 33 | S06 | 竖屏自动重构 | 8 | S05 | 人脸/主体；安全区；预览后导出 |
| 34 | S10 | 录制文件 AI 检索 | 8 | S01 | 转写索引；本地搜索；可删索引 |
| 35 | S09 | AI 导播/自动场景 | 8 | S03/S05/SceneSwitcher | 规则/建议；人工确认；可回退 |
| 36 | Q06 | 直播安全哨兵 | 8 | Q07/Q02/S01 | 黑屏/静音/掉帧/敏感词；一键 BRB；需确认 |
| 37 | Q03 | AI 音频链 | 8 | RNNoise/FFmpeg | 降噪/响度/变声；旁路对比；可关 |
| 38 | Q01 | 一键多平台转推 | 8 | mediamtx/SRS | 2~3 目标；码率/健康；密钥不落日志 |
| 39 | Q05 | 录制自动归档 | 8 | rclone/录制 | 计划/校验/重试；带宽限制；可撤销 |
| 40 | E01 | Stream Deck / MIDI / 脚踏板 | 9 | 基础设施 | 映射/冲突/默认配置；可导入导出 |
| 41 | E02 | 灯光联动 WLED/OpenRGB | 9 | 基础设施 | 本地 API；场景/礼物触发；超时降级 |
| 42 | E06 | 观众数据本地看板 | 9 | C01/S08 | 本地统计；无上传；可导出/清空 |
| 43 | E05 | 插件市场 2.0 | 9 | 现有插件供应链 | 签名/评分/依赖/沙箱；回滚；不静默装 |
| 44 | E03 | 直播间装修市场 | 9 | C02/C05 | 资产包/哈希/预览/回滚；CSP；不远程代码 |
| 45 | E04 | 多操作员/团队协作 | 9 | M01/M12 | 角色/邀请/审计/踢人；最小权限 |
| 46 | S04 | AI 直播搭档/数字人 | 9 | S01/S02/C03/Live2D | TTS+口型；明确 AI 标注；可打断；不冒充真人 |

### 3.7 排序原则与批次规则

1. **先底座、后功能**：批次 0 的 LAN/权限、sidecar、错误码、i18n、HTTP/WS、FFmpeg、AI 工具、测试骨架必须先落地。
2. **先手机本地、后网络/多端**：先做不需要 sidecar 的遥控/预览/提词器/补光/快传，再做媒体服务器、多机位、远程嘉宾。
3. **先只读、后写入**：弹幕先接只看，再做互动/特效；AI 先给建议，再做自动动作。
4. **先人工确认、后自动化**：停流、切场景、发消息、装插件、改配置等破坏性动作必须二次确认。
5. **批次内严格串行**：同批次也按序号推进；前项验收未过，不开始后项。
6. **批次门禁**：每批结束跑一次全量门禁（§9.3）；失败先修复，不带着失败进入下一批。
7. **不删减**：遇到平台/许可证硬阻断，记录 `BLOCKED`、影响范围与替代方案；不静默跳过、不把功能改成占位按钮。

### 3.8 创意池（非 3.4.0 交付项，10 项）

> 这些是**非交付项**：不计入 46 项验收，也不要求 3.4.0 实现；仅记录，避免好点子丢失。

| ID | 脑洞 | 玩法 | 技术落点 |
|---|---|---|---|
| I01 | **虚拟观众席** | 观众头像/Live2D 小人坐进虚拟房间，发言时亮起 | overlay + ChatHub + Live2D/VRM |
| I02 | **弹幕天气** | 弹幕情绪变成直播间天气、灯光、背景粒子 | ChatHub 情绪分析 + overlay + WLED |
| I03 | **AI 吐槽搭子** | 看画面/弹幕给主播递梗，人工确认后才上屏 | 多模态 LLM + 人工确认 |
| I04 | **体感导播** | 手机摇一摇/陀螺仪切场景（可关，防误触） | 手机页面 DeviceOrientation + 命令白名单 |
| I05 | **直播成就墙** | 连续开播、互动里程碑，本地徽章与结束页 | 本地统计 + overlay |
| I06 | **观众合影** | 弹幕头像/昵称拼成本场结束画面 | ChatHub + 图像合成 + overlay |
| I07 | **声音分身** | 录 1 分钟生成 TTS 音色，用于待机播报 | 本地 TTS/声音克隆；必须授权与标注 |
| I08 | **3D 直播间** | 把 2D 模板升级成 3D 房间/虚拟舞台 | three-vrm/three.js overlay |
| I09 | **节日皮肤** | 按节日/活动自动换 overlay、音效、灯光 | 装修市场 + 定时规则 |
| I10 | **AI 赛后采访** | 下播后自动生成“本场战报”短视频/图文 | 复盘 + 切片 + TTS/字幕 |

---

## 4. 关键功能详细设计（按执行序）

### 4.1 总体架构

```text
┌──────────────────────────── 手机（同一局域网）────────────────────────────┐
│  浏览器页面（遥控/监看/提词/补光）        手机推流 App（Larix/OBS Camera） │
│        │ HTTP/WS                                  │ SRT / RTMP              │
└────────┼──────────────────────────────────────────┼─────────────────────────┘
         │                                          │
┌────────▼──────────────────────────┐   ┌───────────▼──────────────────────────┐
│ PC: MobileLinkServer              │   │ PC: MediaServerManager               │
│  ├ PairingCore / SessionStore     │   │  └ mediamtx sidecar（可选下载）      │
│  ├ MobileCommandRouter            │   │       ├ SRT :随机                   │
│  │    └─> ObsConnectionService    │   │       ├ RTMP :随机                  │
│  ├ MobilePreviewService           │   │       └ RTSP :随机（只绑回环）      │
│  ├ TallyService                   │   └───────────┬──────────────────────────┘
│  ├ TeleprompterBridge             │               │ RTSP
│  └ UploadService                │               ▼
└────────┬──────────────────────────┘   ┌──────────────────────────────────────┐
         │ obs-websocket                │ OBS: 场景「手机机位」                │
         ▼                              │  └ ffmpeg_source（媒体源）           │
┌──────────────────────────────────────┐│  └ 音频轨 + 同步偏移                  │
│ OBS Studio 32.x                      │└──────────────────────────────────────┘
└──────────────────────────────────────┘
```

**边界**：手机永远不接触 obs-websocket；手机只发高层动作，PC 侧做权限、参数校验、错误映射与审计。

### 4.2 手机第二屏（执行序 1~3：配对 / 遥控 / 预览 / Tally / 提词器）

#### 用户故事

- 主播在 OBS 前坐下，打开应用「手机联动」，屏幕出现二维码。
- 手机扫码，浏览器打开遥控页；确认设备名与权限。
- 手机上看到当前场景、节目预览、录制/推流状态；点场景卡片切换，点麦克风静音，点「开始录制」。
- 主播走到远处，用手机控制提词器滚动；手机同时显示 Tally。
- 助理用另一台手机以 `viewer` 角色只看预览与 Tally，不能操作。
- 主播在 PC 上撤销助理设备，助理手机立即失效。

#### 配对流程

1. 用户点「生成配对码」。
2. `MobileLinkServer` 启动（随机端口，绑定私网地址；默认关闭，首次需用户确认）。
3. 生成 `pairId`（128-bit 随机）与 `pairSecret`（256-bit 随机），TTL 120 秒，一次性。
4. 页面显示二维码：`http://<私网IP>:<port>/m/<pairId>#<pairSecret>`。
   - **secret 放 URL fragment**，不上服务器日志、不进 Referer。
   - 同时显示 6 位短码，供无法扫码时手动输入（短码只用于定位配对会话，仍需 secret）。
5. 手机 `GET /m/<pairId>` 加载页面；JS 读取 `location.hash`，`POST /api/pair` 提交 secret、设备名、能力列表。
6. 服务端校验 TTL、一次性、常量时间比较 secret；通过后创建：
   - `deviceId`（随机）；
   - `sessionToken`（256-bit 随机，只返回给手机，服务端只存 SHA-256）；
   - `role`（默认 `operator`，可由 PC 改为 `viewer`/`admin`）。
7. 手机用 `sessionToken` 建立 WebSocket；**token 放在首条认证消息里，不放 query**（避免日志）。
8. 服务端返回快照：场景列表、当前节目场景、录音/推流/虚拟摄像头/回放状态、提词器状态、设备权限。
9. 心跳 15 秒；30 秒无心跳断开；会话默认 12 小时，空闲 30 分钟过期。
10. PC 可随时撤销设备；撤销后关闭该设备所有 WS，token 立即失效。

#### 角色与命令白名单

| 命令 | 权限 | OBS / 服务调用 | 说明 |
|---|---|---|---|
| `state.get` | viewer | 快照 | 场景、输出状态、计时 |
| `preview.get` | viewer | `GetSourceScreenshot` | 480p JPEG，1~5 fps |
| `scene.set` | operator | `SetSceneAsync` | 校验场景存在 |
| `source.toggle` | operator | `SetSceneItemEnabledAsync` | 校验 sceneItemId 属于当前场景 |
| `audio.mute` | operator | `SetMuteAsync` | 校验输入存在 |
| `audio.volume` | operator | `SetVolumeDbAsync` | 范围钳制 |
| `record.start/stop/toggle` | operator | `StartRecordAsync` 等 | `stop` 在 PC 端可配置二次确认 |
| `replay.save` | operator | `SaveReplayBufferAsync` | 回放缓存未开则提示 |
| `virtualcam.toggle` | operator | `ToggleVirtualCamAsync` | — |
| `studio.toggle` | operator | `ToggleStudioModeAsync` | — |
| `stream.start` | admin | `StartStreamAsync` | **必须** PC 确认或管理员 PIN |
| `stream.stop` | admin | `StopStreamAsync` | **必须** PC 确认或管理员 PIN |
| `teleprompter.*` | operator | `TeleprompterService` | 播放/暂停/速度/字号/镜像 |
| `device.revoke` | admin | `SessionStore` | 只能撤销自己之外？管理员可撤销全部 |
| `app.panic` | admin | 静音 + 切 BRB + 可选停流 | 可配置；默认只静音+切 BRB |
| ~~`obs.request`~~ | — | — | **不提供**；禁止手机透传任意 OBS 请求 |

**参数校验**：场景名必须在当前场景列表；音量钳制到 OBS 允许范围；文字/设备名去控制字符、限长 64；所有命令带 `requestId`，响应带同一 ID；重复 `requestId` 幂等。

#### 预览与 Tally

- **预览**：`GetSourceScreenshot`，`imageFormat=jpg`，`imageWidth=480`，`imageCompressionQuality=60`；默认 1 fps，最高 5 fps；页面不可见/锁屏立即停；连续失败 3 次降级为静态占位并提示。
- **Tally**：订阅 `CurrentProgramSceneChanged`、`RecordStateChanged`、`StreamStateChanged`、`VirtualcamStateChanged`、`ReplayBufferStateChanged`；手机显示红/绿、计时、静音状态。
- **音频表**：`InputVolumeMeters` 是高频事件，默认订阅不含。3.4.0 先显示静音状态；音量表在手机页可见时临时开启该订阅（若 obs-websocket 不支持动态改订阅，则单独重连或直接不做，避免全局高频开销）。
- **演播室模式**：显示 program/preview 两个场景；`studio.toggle` 可切。

#### 提词器遥控

- 手机控制：播放/暂停、速度、字号、镜像、回到开头。
- 手机可同步显示稿件（只读）；滚动位置由 PC 推送，避免两端漂移。
- 复用 `TeleprompterCore` 的 clamp/滚动逻辑；手机端不重复实现一套算法。
- 手机端编辑稿件不在 46 项内；本版保持只读，避免误改。

#### 错误码（`MOB_*`）

`MOB_DISABLED`、`MOB_NO_LAN`、`MOB_PAIR_EXPIRED`、`MOB_PAIR_BAD_SECRET`、`MOB_TOKEN_INVALID`、`MOB_RATE_LIMITED`、`MOB_ROLE_DENIED`、`MOB_OBS_OFFLINE`、`MOB_COMMAND_UNKNOWN`、`MOB_COMMAND_INVALID`、`MOB_PREVIEW_FAILED`、`MOB_UPLOAD_TOO_LARGE`、`MOB_UPLOAD_BAD_TYPE`。

#### 验收标准

1. 扫码后 10 秒内完成配对；过期/错误 secret 被拒绝，且不泄露“secret 错还是过期”。
2. 手机端无 OBS 密码、无推流码、无 API Key；抓包只能看到场景名/状态。
3. 撤销设备后 1 秒内 WS 断开、token 失效。
4. 场景切换、静音、录制开始/停止在局域网内 200 ms 内返回（不含 OBS 自身耗时）。
5. 预览 1 fps 时 PC CPU 增量 < 3%（i5 级）；手机页面隐藏后停止请求。
6. Tally 与 OBS 实际状态一致；录制/推流计时误差 < 1 秒。
7. 提词器手机控制与 PC 显示同步，漂移 < 1 行。
8. 错误码可读、中英双语、带下一步。
9. 默认关闭；首次开启有明确说明与防火墙指引；不自动加规则。
10. 手机页面 360×640 到 430×932 不横向滚动；触控目标 ≥ 44 px；键盘可操作。

### 4.3 手机摄像头 / 麦克风（执行序 6~9）

#### 三条路线

| 路线 | 链路 | 延迟 | 音频 | 依赖 | 结论 |
|---|---|---|---|---|---|
| **A（推荐）** | 手机 App（Larix/OBS Camera）→ SRT/RTMP → 本机 mediamtx → RTSP → OBS 媒体源 | SRT 约 0.3~1 s；RTMP 1~3 s | 有 | 可选 sidecar（MIT） | **3.4.0 必做** |
| **B（后续）** | 手机浏览器 `getUserMedia` → WHIP → mediamtx → OBS | 0.15~0.4 s | 有 | HTTPS/secure context、mediamtx | 技术验证（不作为 46 项验收） |
| **C（USB 兜底）** | Android: scrcpy 2.x 摄像头 → 窗口采集；iOS: OBS Camera/Iriun/Camo 虚拟摄像头 | 低 | 视工具 | 外部工具/驱动 | **3.4.0 必做（引导）** |
| **D（实验）** | 手机浏览器 → WebSocket MJPEG → 本机 → OBS 浏览器源 | 0.3~0.8 s | 无 | 纯 BCL | 不推荐做主路径 |

**为什么选 A**：不需要自研 WebRTC/编解码；手机端有成熟 App；SRT 抗抖动；音频随流；Windows 上 mediamtx 一个二进制即可；OBS 原生媒体源能拉 RTSP；许可证 MIT，可单独下载。

#### 用户流程

1. 用户在「手机联动 → 摄像头」选择画质（720p30 / 1080p30）、布局（全屏 / 右下角 / 左侧三分 / 竖屏裁切）、是否带音频。
2. 应用检测 sidecar 是否已安装；未安装则展示来源、版本、许可证、SHA-256 与体积，用户确认后下载。
3. 启动 `mediamtx`：随机 SRT/RTMP/RTSP 端口，随机路径 key，API 只绑 `127.0.0.1`；仅监听私网地址。
4. 应用在 OBS 新建/复用「手机机位」场景，创建 `ffmpeg_source`，URL 指向 `rtsp://127.0.0.1:<rtspPort>/live/<key>`；设置重连、缓冲、音频轨。
5. 手机扫码或复制推流信息：
   - SRT：`srt://<PC私网IP>:<srtPort>?streamid=publish:live/<key>`
   - RTMP：`rtmp://<PC私网IP>:<rtmpPort>/live/<key>`
   - 应用内给出 Larix Broadcaster / OBS Camera 的图文步骤（不自动安装）。
6. 手机开始推流；应用轮询 mediamtx API/RTSP 状态，就绪后切到该场景（可选），显示码率/分辨率/丢帧。
7. 停止：手机停止推流；应用保留场景但释放 sidecar（若由本应用启动）；下次一键重启。
8. 断流：自动重连 3 次；失败给出「检查同一 Wi-Fi / 关闭 AP 隔离 / 换 5GHz / 改用 USB」的下一步。

#### mediamtx 配置要点

- 只开启需要的协议：`rtsp`、`rtmp`、`srt`；`hls: no`；`webrtc: no`（M10 远程嘉宾时再开）；`api: yes` 且只绑 `127.0.0.1`。
- 路径使用 128-bit 随机 key（能力 URL）；不暴露可枚举的固定路径。
- **不开启 UPnP / 公网候选**；`webrtcAdditionalHosts` 若启用只填私网 IP。
- 进程由 `ProcessGuard` 管理：退出应用、崩溃、用户关闭功能时杀掉进程树；不留下后台服务。
- 下载/更新：HTTPS 官方源、SHA-256、版本固定、许可证展示；不静默更新。
- Win7：不启动 sidecar，界面明确「Win7 请用 USB 或外部虚拟摄像头」。

#### OBS 媒体源

```text
input kind: ffmpeg_source
settings:
  is_local_file: false
  input: rtsp://127.0.0.1:<rtspPort>/live/<key>
  reconnect_delay_sec: 2
  close_when_inactive: false
  buffering_mb: 2
  hw_decode: false（可选 true，兼容性优先）
```

- 音频：媒体源自带音频轨；在 OBS 混音器里单独一路，避免与桌面音频重复。
- 同步：复用 `ObsConnectionService.SetAudioSyncOffsetMsAsync`；默认 0，界面提供 ±500 ms 微调。
- 布局：`PhoneCameraLayoutCore`（纯逻辑）按画布比例计算全屏/画中画/侧栏/竖屏裁切；与 Live2D 布局预设保持同一套数学。
- 备用：若 `ffmpeg_source` 拉流失败，提示改用 `vlc_source` 或 HTTP-FLV（mediamtx 开启 HLS/LL-HLS 时）。

#### 画质与延迟建议

| 场景 | 分辨率/帧率 | 码率 | 说明 |
|---|---|---|---|
| 默认 | 720p30 | 2.5 Mbps | 兼容性最好 |
| 特写/带货 | 1080p30 | 4~6 Mbps | 5GHz Wi-Fi，PC 与手机同网段 |
| 竖屏 | 720×1280 30 | 2.5~4 Mbps | 手机端直接竖屏推流，OBS 竖屏画布 |
| 低带宽 | 540p30 | 1.2 Mbps | 旧手机/2.4GHz |

- 延迟：SRT 约 0.3~1 s；RTMP 1~3 s；WHIP 后续可到 0.15~0.4 s。
- 音频：48 kHz，AAC 128 kbps；手机端开启降噪/自动增益需在 App 内设置，应用不篡改。

#### 安全边界

- 只允许**私网**手机推流；服务端校验来源 IP 属于 RFC1918/链路本地，公网来源拒绝。
- 随机路径 key 只显示一次；二维码/screen 不写入日志；配对页可一键重置 key。
- 不自动添加 Windows 防火墙规则；首次启动给出指引，由用户点「允许访问」。
- 检测到公网 IP/公网 Wi-Fi 时强提示；不自动开热点、不改路由。
- sidecar 进程只监听必要端口；退出即停；不注册服务、不开机自启。
- 流不经过任何云；不做录制/转发，除非用户显式开启。

#### 验收标准

1. 720p30 连续 30 分钟不中断；Wi-Fi 断开后 10 秒内重连或给出明确失败原因。
2. 手机开始推流后 5 秒内 OBS 出现画面与音频；无双重音频。
3. 音频同步偏移可调，±200 ms 内可对齐。
4. 退出应用后 mediamtx 进程消失，端口释放。
5. 公网来源推流被拒绝；错误 key 被拒绝；日志无完整 key。
6. Win7 上功能入口可见但明确禁用 Wi-Fi 链路，给出 USB/外部应用指引。
7. 不下载 sidecar 时，其余功能（遥控/预览/提词器）不受影响。

### 4.4 弹幕聚合与互动 overlay（执行序 13~21）

#### 平台矩阵

| 平台 | 接入方式 | 登录 | 3.4.0 | 风险 |
|---|---|---|---|---|
| B 站 | 公开 WebSocket 弹幕协议 | 不需要 | **3.4.0** | 协议可能变；需处理压缩包 |
| Twitch | IRC over WebSocket（匿名 `justinfan`） | 不需要 | **3.4.0** | 限流/连接数 |
| YouTube | Data API `liveChatMessages` | API Key/OAuth | 后续扩展（不计入 46 项） | 配额、OAuth |
| 抖音/快手 | 无公开弹幕 API | — | **不做** | 逆向/ToS 风险 |
| Kick | 公开 API/WS（需复核） | 可能需要 | 后续扩展（不计入 46 项） | 稳定性 |

#### 架构

```text
BilibiliChatAdapter ─┐
TwitchChatAdapter ───┼─> ChatHub（去重/限流/过滤/统计）─┬─> 桌面面板
YouTubeChatAdapter ──┘                                   ├─> overlay（WS/SSE）
                                                         └─> TTS 队列
```

- `IChatAdapter` 统一接口：`ConnectAsync`、`DisconnectAsync`、`StateChanged`、`MessageReceived`。
- 纯逻辑拆出：`ChatCore`（解析/归一化/过滤/去重）、`BilibiliPacketCore`（协议包）、`TwitchIrcCore`（IRC 解析）。
- 每条消息归一化为 `ChatMessage`：平台、房间、用户显示名（可哈希）、文本、表情、类型、时间戳；**不保存原始 HTML**。
- 重连：指数退避（1/2/5/10/30 秒），心跳；断线时 UI 明确显示。
- 限流：每平台每秒最多 50 条入 UI；overlay 每秒最多 30 条；超出丢弃并计数，不阻塞。
- 过滤：长度上限 500；控制字符/零宽字符清理；屏蔽词、屏蔽用户、重复消息去重。
- 隐私：默认不落盘；可选本地保留 7 天；不读取/保存平台 cookie；不自动发消息。

#### B 站协议要点

- WebSocket 连接 + 认证包 + 30 秒心跳；解析 `DANMU_MSG`、`SEND_GIFT`、`SUPER_CHAT_MESSAGE`、`INTERACT_WORD`。
- 压缩：zlib/brotli；.NET 6+ 的 `ZLibStream`/`BrotliStream` 可处理，不引第三方包。
- 房间号：支持短号/长号；短号解析走公开接口；失败让用户填长号。
- 只读公开房间；需要登录/粉丝牌的字段可缺省，不因此失败。

#### Twitch 协议要点

- `wss://irc-ws.chat.twitch.tv:443`，匿名 `NICK justinfan<随机数>`，`JOIN #channel`。
- 解析 `PRIVMSG` 的 tags（display-name、emotes、color）；`USERNOTICE` 可选。
- 不要求 OAuth；不发送消息；频道名去 `#`、小写化、长度校验。

#### Overlay 与 TTS

- 复用 `RoomOverlayServer` 的静态页面模式，新增 `chat-live.html`；通过 WS/SSE 接收消息。
- CSP 继续 `default-src 'none'`；无远程字体/图片；消息文本必须 `textContent` 或转义，禁止 `innerHTML`。
- 主题：CSS 变量 + 预设；最多 50 条；自动滚动；可暂停；可隐藏用户/关键词。
- TTS：Windows SAPI 默认；piper 本地 sidecar 为可选增强；队列上限 10；跳过 URL/表情/过长文本；可一键静音。
- 互动命令（点歌/投票/抽奖）按序 17（C04）实现；先完成只读展示，不做自动回复。

#### 验收标准

1. B 站/Twitch 各连续连接 2 小时，断线自动重连。
2. 100 条/秒突发消息不卡 UI；overlay 不崩；丢弃有计数。
3. 屏蔽词/用户生效；消息不执行 HTML/JS。
4. 不保存 cookie/账号；日志无用户敏感信息。
5. 中英双语界面；无远程资源；CSP 生效。

### 4.5 实时字幕 / 翻译 / 语音控制（执行序 22~25）

#### 方案比较

| 方案 | 许可证 | 位置 | 延迟 | 准确率 | 依赖 | 结论 |
|---|---|---|---|---|---|---|
| whisper.cpp | MIT | 本地 | 1~3 s | 高 | sidecar + 模型 | **主方案** |
| Vosk | Apache-2.0 | 本地 | <1 s | 中 | 模型 + 原生库 | 备选/低配 |
| Windows SAPI | 系统 | 本地 | 低 | 低 | 语言包 | Win7 兜底 |
| 云端 ASR | 各厂商 | 云 | <1 s | 高 | API Key/费用/隐私 | 显式同意后可选 |

#### 音频采集

- **系统声音**：WASAPI loopback（Win7+），P/Invoke `IAudioClient`/`IAudioCaptureClient`；16 kHz/16-bit/单声道；重采样用纯 C# 线性/多相。
- **麦克风**：WASAPI capture 或 WinMM `waveIn`（Win7 兜底）。
- 环形缓冲 + VAD：静音不送 ASR；最长 4 秒切段；句尾静音 300~600 ms 触发。
- 线程：采集回调只写环形缓冲，不做分配/IO；ASR 在后台线程；UI 2~5 Hz 更新。
- 设备选择：默认系统默认设备；允许用户选麦克风/输出；设备消失时暂停并提示。

#### 管线

```text
WASAPI/WinMM → RingBuffer → VAD → Segmenter → IAsrEngine
                                              ├ WhisperCppEngine（sidecar）
                                              └ VoskEngine（备选）/ SapiEngine（兜底）
                                                   ↓
                                        SubtitleSegmenter（合并/标点/限长）
                                                   ↓
                                CaptionPublisher ─┬─> OBS 文本源（text_gdiplus_v3）
                                                  └─> overlay（WS/SSE）
```

- `IAsrEngine`：`StartAsync`、`StopAsync`、`SegmentReady`；引擎可替换。
- `WhisperCppEngine`：外部进程，参数数组传参（不经 shell）；模型路径校验；超时/退出码/错误输出映射错误码；临时 WAV 只写 `%TEMP%` 并即时删除。
- `CaptionPublisher`：`SetInputSettings` 更新文本源；节流 300~500 ms；失败回退 overlay。
- 翻译（S02/C08，按序 23/24）：云端/本地翻译引擎；双语两行；可关闭。

#### 隐私与性能

- **默认纯本地**；不上传音频；不保存音频；只保留最近 N 条字幕文本（可关）。
- 云端 ASR 必须显式勾选、显示将上传的音频类型与目的、可随时停止。
- CPU：默认 2 线程；可设上限；推流/录制中才启用；检测到掉帧自动降频/暂停并提示。
- 模型：首次使用下载，展示许可证/体积/SHA-256；可离线导入；不把模型打进主程序。
- Win7：whisper.cpp 不承诺；走 SAPI 或明确「不支持实时字幕」。

#### 验收标准

1. 中文/英文各 5 分钟样本，字幕延迟 < 2 s（默认模型/CPU）。
2. OBS 文本源每 300~500 ms 更新，不闪烁；overlay 不阻塞。
3. 默认无网络请求；抓包确认无音频上传。
4. 停止后释放音频设备、杀掉 sidecar；再次启动可恢复。
5. 设备切换/静音/断流有明确状态；错误码可读。

### 4.6 手机小功能（执行序 4~6：快传 / 补光 / USB 引导）

- **文件快传**：`POST /api/upload`，multipart，50 MB 上限；只接受 JPEG/PNG/MP4；魔数校验；保存到 `%LocalAppData%\OBS_Helper\mobile\uploads\`；默认不自动加入 OBS，用户确认后可建 `image_source`。
- **补光灯/色卡/倒计时**：手机页面全屏纯色（可调色温/亮度）、白平衡卡、开播倒计时；无网络请求；可离线。
- **USB 引导**：检测 `adb`/scrcpy 是否存在；Android 给出 `scrcpy --video-source=camera` 步骤；iOS 给出 OBS Camera/Iriun 的选择与注意事项；不自动安装驱动。

### 4.7 其余功能的执行要点

> §4.2~4.6 只展开手机/互动/字幕主线；其余功能按 §3.6 顺序执行。下面给每段的实现边界与额外门禁；验收以 §3.6 的“验收要点”为准。

| 序号 | 功能组 | 执行要点 | 额外门禁 |
|---|---|---|---|
| 10~12 | 多机位 / 远程嘉宾 / 通知上屏 | M09 复用多路 mediamtx + 场景预设；M10 用 LiveKit/mediasoup，先 LAN/自建，TURN 只做可选；M11 仅 Android（helper/ADB）、每次显式授权、只上屏用户确认的内容、可一键关闭；iOS 明确不支持 | 多路带宽/CPU；SFU 权限与踢人；通知隐私 |
| 26~31 | 独立智能与诊断 | 复用 `SessionReviewService`、`ColorCheck`、预检/备份；AI 只给建议，报告可导出；Q07 回放缓存设磁盘上限 | 不依赖未完成 ASR；断网可降级 |
| 32~39 | 切片 / 导播 / 安全 / 分发 | S05/S06 用外部 FFmpeg，参数白名单；S10 索引可删；S09 人工确认；Q06 一键 BRB 需确认；Q03 提供旁路对比；Q01 密钥不落日志；Q05 rclone 可撤销 | 输出路径/磁盘/带宽；失败回滚 |
| 40~46 | 硬件 / 生态 / 协作 / 数字人 | E01 MIDI 用 P/Invoke；E02 HTTP API 超时降级；E03 资产哈希 + CSP；E05 签名/沙箱/回滚；E04 最小权限 + 审计；S04 明确 AI 标注、可打断 | 供应链/权限/内容合规 |

---

## 5. 代码落点与数据设计

### 5.1 新增文件清单（建议）

| 路径 | 职责 | 纯逻辑/可单测 |
|---|---|---|
| `Services/Mobile/MobileLinkServer.cs` | LAN HTTP/WS 服务、静态页面、API 路由、生命周期 | 否 |
| `Services/Mobile/PairingCore.cs` | 配对 token、TTL、一次性、常量时间比较、设备名校验 | **是** |
| `Services/Mobile/MobileCommandCore.cs` | 命令解析/校验/权限矩阵/幂等 | **是** |
| `Services/Mobile/MobileStateCore.cs` | 快照/增量、Tally、计时、状态合并 | **是** |
| `Services/Mobile/MobileSessionStore.cs` | 设备注册、token 哈希、角色、撤销、过期 | 部分（存储 IO 隔离） |
| `Services/Mobile/MobileCommandRouter.cs` | 调用 `ObsConnectionService`/`TeleprompterService` | 否 |
| `Services/Mobile/MobilePreviewService.cs` | 截图、JPEG 编码、节流、背压 | 部分 |
| `Services/Mobile/PhoneCameraCore.cs` | SRT/RTMP URL、随机 key、画质/布局映射、私网校验 | **是** |
| `Services/Mobile/MediaServerManager.cs` | sidecar 启停、端口、配置、进程树回收 | 否 |
| `Services/Mobile/UsbCameraGuide.cs` | scrcpy/OBS Camera 检测与步骤 | 部分 |
| `Services/Mobile/MobilePageAssets.cs` | 手机页面 HTML/CSS/JS（内嵌字符串或资源） | **是**（可做静态检查） |
| `Services/Interop/SidecarManifest.cs` | 清单解析、URL/哈希/许可证校验 | **是** |
| `Services/Interop/SidecarManager.cs` | 下载、校验、解压、启动、停止、版本 | 部分 |
| `Services/Interop/ProcessGuard.cs` | 进程树、退出清理、孤儿回收 | 否 |
| `Services/Chat/ChatModels.cs` | `ChatMessage`、`ChatRoom`、状态 | **是** |
| `Services/Chat/ChatCore.cs` | 归一化、过滤、去重、限流、统计 | **是** |
| `Services/Chat/BilibiliPacketCore.cs` | B 站协议包解析 | **是** |
| `Services/Chat/TwitchIrcCore.cs` | Twitch IRC 解析 | **是** |
| `Services/Chat/BilibiliChatAdapter.cs` | WS 连接、心跳、重连 | 否 |
| `Services/Chat/TwitchChatAdapter.cs` | IRC WS、重连 | 否 |
| `Services/Chat/ChatHub.cs` | 适配器聚合、事件总线、overlay 广播 | 否 |
| `Services/Chat/ChatTtsService.cs` | SAPI/piper 队列、过滤、限速 | 部分 |
| `Services/Subtitles/WasapiLoopbackCapture.cs` | WASAPI/WinMM 采集 | 否 |
| `Services/Subtitles/AsrCore.cs` | VAD/分段/合并/限长 | **是** |
| `Services/Subtitles/WhisperCppEngine.cs` | 外部进程、参数、超时、错误映射 | 部分 |
| `Services/Subtitles/CaptionPublisher.cs` | OBS 文本源/overlay 发布 | 否 |
| `Services/Clips/HighlightCore.cs` | 高光评分/窗口/去重 | **是** |
| `Services/Clips/FfmpegRunner.cs` | FFmpeg 参数白名单/超时/输出 | 部分 |
| `Services/Security/LanPolicy.cs` | 私网判断、来源校验、CORS/CSP、限流 | **是** |
| `Models/Mobile/MobileModels.cs` | 设备、会话、命令、快照 | **是** |
| `Models/Chat/ChatModels.cs` | 消息/房间/状态 | **是** |
| `Views/MobileLinkPage.xaml(.cs)` | 手机联动页（遥控/摄像头/设备） | 否 |
| `Controls/PairingCard.xaml` | 二维码/短码/过期/设备列表 | 否 |
| `Views/InteractionPage.xaml(.cs)` | 弹幕/字幕/切片（执行序 13~25、32~34） | 否 |
| `Views/InsightsPage.xaml(.cs)` | 复盘/封面/看板（执行序 26~27、42） | 否 |
| `Views/StreamPage.xaml(.cs)` | 推流健康/转推/归档/安全/预案（执行序 28~39） | 否 |
| `Views/AutomationPage.xaml(.cs)` | 硬件/市场/团队/数字人/AI 导播（执行序 35、40~46） | 否 |
| `Assets/sidecars.json` | sidecar 清单（**不含二进制**） | **是** |
| `tools/MobileSmoke/` | 无 OBS/无网络配对与命令冒烟 | **是** |
| `tools/ChatSmoke/` | 假 WS 服务器协议冒烟 | **是** |
| `Services/Live/MultiCamCore.cs` | 多机位布局/切换/画中画、路数上限 | **是** |
| `Services/Live/RemoteGuestService.cs` | LiveKit/mediasoup 房间、token、踢人、混流 | 否 |
| `Services/Mobile/MobileNotifyService.cs` | 手机通知/剪贴板上屏（授权、过滤、确认） | 部分 |
| `Services/Analytics/SessionCoachCore.cs` | 复盘评分、行动清单、报告导出 | **是** |
| `Services/Analytics/ViewerStatsCore.cs` | 本地互动/留存统计、图表数据 | **是** |
| `Services/Obs/StreamHealthCore.cs` | 带宽/掉帧/断流评分与建议 | **是** |
| `Services/Obs/CompositionCheckCore.cs` | 构图/亮度/色温/人脸评分 | **是** |
| `Services/Obs/RunbookCore.cs` | 开播预案、检查项、失败回滚 | **是** |
| `Services/Shell/IncidentClipService.cs` | 事故回放/环形缓冲、导出、磁盘上限 | 部分 |
| `Services/Clips/ReframeCore.cs` | 9:16 安全区、主体跟踪、裁切窗口 | **是** |
| `Services/Clips/RecordingSearchCore.cs` | 转写索引、检索、删除索引 | **是** |
| `Services/Clips/DirectorCore.cs` | AI 导播规则/建议、人工确认 | **是** |
| `Services/Shell/PanicService.cs` | 安全哨兵：静音/BRB/停流、确认、审计 | 部分 |
| `Services/Shell/MultiPlatformRelayService.cs` | 本地转推目标、健康、密钥管理 | 部分 |
| `Services/Audio/AudioChainService.cs` | 降噪/响度/变声、旁路对比 | 部分 |
| `Services/Recording/ArchiveUploader.cs` | rclone 归档、校验、重试、撤销 | 部分 |
| `Services/Interop/Hardware/MidiCore.cs` | WinMM MIDI 映射、冲突检测 | **是** |
| `Services/Interop/Hardware/WledClient.cs` | WLED HTTP JSON、超时降级 | 部分 |
| `Services/Plugins/PluginTrustCore.cs` | 签名/评分/依赖/沙箱策略 | **是** |
| `Services/Live/ThemeMarketCore.cs` | 主题资产包、哈希、预览、回滚 | **是** |
| `Services/Shell/TeamRolesCore.cs` | 角色/邀请/审计/踢人 | **是** |
| `Services/Ai/AiHostService.cs` | TTS + 口型 + 打断 + AI 标注 | 部分 |

**其余功能的落点**：按同一分层追加 `Services/<Domain>/`；纯逻辑放 `*Core.cs` 并直接链接进单测工程；WPF/IO/进程放 `*Service.cs`。不要把所有新功能塞进 `MobileLinkServer` 或 `ChatHub`。

### 5.2 组合根与生命周期

- 在 `AppServices` 新增 Lazy 单例：`MobileLink`、`MobileSessions`、`MobileRouter`、`MediaServer`、`MultiCam`、`RemoteGuests`、`ChatHub`、`AsrPipeline`、`Sidecars`、`StreamHealth`、`Panic`、`Relay`、`Archive`、`Hardware`、`ThemeMarket`、`TeamRoles`、`AiHost`。
- `AppServices.InitializeAsync()` 不自动启动任何局域网服务；只初始化存储与清单。
- `AppServices.ShutdownServices()` 按序停止：MobileLink → MediaServer → MultiCam → RemoteGuests → ChatHub → AsrPipeline → Sidecars → Relay → Hardware；每步 try/catch，不让单点失败阻塞退出。
- 页面/托盘/全局热键只调用服务，不直接开端口/进程。
- `MainWindow` 仍只注入 `Navigation`/`Busy`/`Toast`；手机页面通过 `AppServices` 取服务。

### 5.3 路由、页面与导航

- 新增 `Routes.MobileLink = "mobile"`：**不新增一级导航项**，入口放在首页卡片、控制台工具栏、托盘菜单与设置页；避免一级导航继续膨胀。
- 本版交付「互动」，新增 `Routes.Interaction = "interaction"`，同样先卡片入口；稳定后再评估一级导航。
- 新路由必须加入 `MainWindow` 自检 `cases`，并同步 `check_docs.py` 的路由数字。
- 页面实现 `INavigationAware`：进入时刷新设备/状态，离开时停预览计时器、停 WS（手机服务本身可继续）。

**页面归并（避免 46 项各开一个页面）**

| 页面 / 入口 | 路由 | 承载功能 | 说明 |
|---|---|---|---|
| 手机联动 | `mobile` | M01~M12 | 分段：遥控 / 摄像头 / 多机位 / 设备 |
| 直播互动 | `interaction` | C01~C10、S01~S03、C08、S05/S06/S10 | 分段：弹幕 / 字幕 / 互动 / 切片 |
| 智能与复盘 | `insights` | S07~S10、E06 | 复盘报告、封面文案、观众看板 |
| 画质与分发 | `stream` | Q01~Q08 | 推流健康、转推、归档、安全哨兵、预案 |
| 自动化与生态 | `automation` | E01~E05、S04、S09 | 硬件、灯光、插件/装修市场、团队、数字人、AI 导播 |

> 5 个入口都先走首页卡片 / 控制台 / 设置页，不直接塞进一级导航；稳定后再评估是否提升为一级导航。

### 5.4 数据与目录

```text
%LocalAppData%\OBS_Helper\
├─ prefs.json                 # mobile.* / chat.* / caption.* / stream.* / hardware.*
├─ secrets.dat                # 推流码、API Key、rclone/WebDAV 凭据（DPAPI + AES-GCM）
├─ mobile\
│  ├─ devices.json            # deviceId、name、role、tokenHash、created、lastSeen
│  └─ uploads\                # 手机上传（默认不自动进 OBS）
├─ sidecars\
│  └─ <id>\<version>\         # mediamtx / whisper.cpp / ffmpeg / rclone（可选下载）
├─ chat\
│  └─ logs\                   # 可选，默认关闭，最多 7 天
├─ clips\                     # 高光切片、竖屏导出、缩略图
├─ analytics\                 # 复盘报告、观众看板（本地）
├─ themes\                    # 装修市场资产包（哈希校验）
├─ hardware\                  # MIDI/WLED 映射（不含密钥）
└─ remote\                    # 嘉宾房间配置（token 只进 secrets.dat）
```

- `devices.json` **不存明文 token**；只存 SHA-256 + salt；撤销即删除。
- 会话 token 只在内存；应用重启后手机需重新配对（或使用“记住设备”短期 token，后续增强）。
- 推流码、YouTube API Key 只进 `secrets.dat`；`prefs.json` 不出现。
- 日志脱敏：token、stream key、完整 URL 查询串不落盘；聊天用户 ID 可哈希。
- 路径全部过 `ObsSafePath` 同类护栏；上传文件名不直接使用用户输入。

### 5.5 线程与背压

- HTTP/WS 在后台线程；OBS 事件经既有 Dispatcher 封送；命令执行 async。
- 预览：单飞（同一时刻最多一个截图请求）；手机端 ACK 后再发下一帧；慢客户端丢帧不排队。
- 聊天：适配器后台收包；`ChatHub` 批量 10 Hz 推 UI；overlay 独立队列，慢消费者丢弃旧消息。
- 字幕：采集回调零分配；ASR 串行；字幕发布节流；掉帧时降频。
- 远程嘉宾/SFU、转推、FFmpeg 一律后台；进程输出限流；不阻塞 UI。
- 切片/归档任务队列串行（可配置并发上限）；失败可重试/取消；磁盘/带宽不足先拒绝。
- 看板/复盘按分钟聚合；图表只推增量，避免高频 UI 更新。
- sidecar：启动/停止串行；进程退出事件回收；不阻塞 UI。

### 5.6 错误码

| 前缀 | 用途 | 示例 |
|---|---|---|
| `MOB_*` | 手机配对/会话/命令/上传 | `MOB_PAIR_EXPIRED`、`MOB_ROLE_DENIED` |
| `CAM_*` | 手机摄像头/媒体服务器 | `CAM_SIDECAR_MISSING`、`CAM_PORT_IN_USE` |
| `CHAT_*` | 弹幕平台 | `CHAT_CONNECT_FAILED`、`CHAT_PROTOCOL_CHANGED` |
| `ASR_*` | 语音识别/字幕 | `ASR_MODEL_MISSING`、`ASR_AUDIO_DEVICE` |
| `CLIP_*` | 切片/FFmpeg | `CLIP_FFMPEG_MISSING`、`CLIP_OUTPUT_FAILED` |
| `MULTI_*` | 多机位 | `MULTI_LIMIT`、`MULTI_SOURCE_FAILED` |
| `REMOTE_*` | 远程嘉宾/SFU | `REMOTE_ROOM_FULL`、`REMOTE_TURN_FAILED` |
| `THEME_*` | 装修市场 | `THEME_HASH_MISMATCH`、`THEME_UNSAFE_ASSET` |
| `HARD_*` | 硬件/灯光 | `HARD_MIDI_CONFLICT`、`HARD_WLED_TIMEOUT` |
| `STREAM_*` | 推流健康/转推 | `STREAM_BANDWIDTH_LOW`、`STREAM_RELAY_FAILED` |
| `ARCHIVE_*` | 录制归档 | `ARCHIVE_REMOTE_MISSING`、`ARCHIVE_UPLOAD_FAILED` |
| `COACH_*` | 复盘/看板 | `COACH_NO_DATA`、`COACH_REPORT_FAILED` |
| `VOICE_*` | 语音控制 | `VOICE_WAKE_MISSING`、`VOICE_COMMAND_DENIED` |
| `TRANS_*` | 翻译 | `TRANS_ENGINE_MISSING`、`TRANS_RATE_LIMITED` |
| `DIRECTOR_*` | AI 导播 | `DIRECTOR_CONFIRM_REQUIRED`、`DIRECTOR_RULE_CONFLICT` |
| `AIHOST_*` | 数字人/AI 搭档 | `AIHOST_TTS_FAILED`、`AIHOST_LIPSYNC_FAILED` |

错误码写入 `Errors/ErrorCodes.cs`，带中英双语文案与“下一步”；UI 按码渲染，不拼裸字符串。

---

## 6. 安全与合规

### 6.1 威胁模型

| 威胁 | 场景 | 对策 |
|---|---|---|
| 局域网扫描 | 同网段恶意设备扫端口 | 默认关闭；随机端口；token；来源私网校验；限流；错误不区分“无权限/不存在” |
| 恶意网页 | 手机浏览器打开钓鱼页/被注入 | CSP；无 CORS；无远程资源；token 放 fragment/first-message；`textContent` 渲染 |
| token 被盗 | 手机丢失/截屏/日志 | 短 TTL；一次性配对；可撤销；设备绑定；不落日志；HTTPS（后续增强） |
| 恶意上传 | 手机上传可执行文件 | 大小/类型/魔数校验；只存不执行；用户确认后才进 OBS |
| 媒体服务器暴露 | 公网/邻居网访问 | 只绑私网；随机路径；API 回环；无 UPnP；退出即停；公网 IP 强提示 |
| sidecar 供应链 | 下载被替换/带毒 | HTTPS 官方源；SHA-256；版本固定；用户确认；不静默更新；许可证 |
| 聊天内容 XSS | 弹幕含 HTML/JS | 文本转义；禁止 `innerHTML`；长度/控制字符过滤；CSP |
| ASR 隐私 | 音频被上传/留存 | 默认本地；不保存音频；云端显式同意；指示灯；随时停止 |
| AI 误操作 | 模型调用停流/重置 | 破坏性动作二次确认；工具白名单；审计；AI 不直接执行 |
| 远程嘉宾/SFU | 房间被陌生人进入、TURN 暴露 | 房间 token；默认仅 LAN/自建；踢人；TURN 可选且不默认公网 |
| 装修/插件市场 | 不可信资产/插件执行代码 | 哈希/签名；CSP；沙箱；预览；回滚；不静默安装 |
| 数字人/声音克隆 | 冒充真人、未授权音色 | 明确 AI 标注；可打断；音色授权与来源记录 |
| 许可证 | GPL 代码进 MIT 主程序 | 外部进程/插件边界；不复制/不静态链接；署名与源码链接 |

### 6.2 默认值与上限

| 项 | 默认 | 上限/策略 |
|---|---|---|
| 手机联动 | 关闭 | 用户显式开启；退出应用即停 |
| 绑定地址 | 仅私网 | 拒绝公网来源；不监听 0.0.0.0 以外的公网网卡（按需） |
| 配对有效期 | 120 秒 | 一次性；过期作废 |
| 会话 | 12 小时 | 空闲 30 分钟；可撤销 |
| 设备数 | 5 | 超出需先撤销 |
| 并发会话 | 3 | 超出拒绝并提示 |
| 命令频率 | 20 次/秒/设备 | 超出限流 |
| 预览 | 1 fps | 最高 5 fps；页面隐藏即停 |
| 上传 | 关闭 | 50 MB；JPEG/PNG/MP4 |
| 聊天保留 | 不落盘 | 可选 7 天；可清空 |
| 字幕音频 | 不保存 | 云端需显式勾选 |
| 远程嘉宾 | 关闭 | 仅 LAN/自建；最多 4 人；TURN 需显式开启 |
| 装修/插件市场 | 不自动安装 | 哈希/签名；预览；回滚；来源展示 |
| 硬件/灯光 | 关闭 | 本地 API；超时降级；不依赖云 |
| 切片/归档 | 关闭 | 输出目录可配；磁盘/带宽上限；可撤销 |
| 复盘/看板 | 本地 | 可导出/清空；不上传 |

### 6.3 许可证红线

- 主程序 **MIT**；GPL/AGPL 代码**不得**复制、静态链接、捆绑进主程序。
- MIT/Apache-2.0/BSD/ISC：可内嵌源码，但必须保留版权与许可证；Apache-2.0 还要保留 NOTICE。
- LGPL：只做外部进程或动态链接；不静态链接；提供源码获取方式。
- GPL：只做**外部进程/用户自装插件**；不捆绑、不自动安装；在 `OPEN_SOURCE_ATTRIBUTION.md` 给出项目链接与许可证。
- 专有：Live2D Cubism Core、NDI SDK、VTube Studio、平台直播软件——只做用户自装/外部调用，不 redistributing。
- 模型权重：Whisper、Vosk、Piper、RVC 等模型有各自许可证；下载前展示，记录在 sidecar 清单。
- FFmpeg：优先 LGPL 构建；不启用/不捆绑 GPL 组件；外部调用，用户可自备。
- 字体/素材/音效：只用可商用/可再分发素材；来源与许可证入库。

### 6.4 平台 ToS 与隐私

- 弹幕只读公开数据；不抓登录态、不存 cookie、不自动发消息、不绕过风控。
- 不提供抖音/快手协议逆向；只给官方/外部工具引导。
- 录制/直播需遵守当地法律与参与者知情同意；应用只做提示，不替代用户责任。
- 不上传音频/视频/聊天内容到自有服务器；云能力由用户自带 Key，显式发起。
- 隐私说明更新：手机页面/媒体流/字幕/弹幕各自的“数据去哪了”。

### 6.5 新增功能的额外边界

| 功能 | 额外边界 |
|---|---|
| M10 远程嘉宾 | 默认仅局域网/自建；TURN 只做可选；房间 token、踢人、录制需所有参与者同意 |
| M11 通知/剪贴板上屏 | 每次显式授权；验证码/密码/银行卡等敏感内容默认过滤；不持久化；一键关闭 |
| C04/C05 互动/礼物特效 | 不自动回平台；冷却/防刷；特效只走白名单场景/滤镜/音效 |
| S04 AI 数字人 | 明确 AI 标注；不冒充真人；可打断；声音克隆需授权与来源记录 |
| Q01 多平台转推 | 推流码只进 `secrets.dat`；不落日志；目标域名白名单；失败不回显密钥 |
| Q06 安全哨兵 | 静音/BRB/停流需确认；动作审计；可整体关闭 |
| E03 装修市场 | 资产包哈希；CSP；禁止远程代码；预览后应用；可回滚 |
| E05 插件市场 2.0 | 签名/扫描/沙箱；不静默安装；保留回滚；显示来源与许可证 |
| E04 团队协作 | 最小权限；邀请过期；操作审计；一键撤销所有设备 |
| E01/E02 硬件/灯光 | 本地 API token；超时降级；不把设备密钥写日志；不依赖云 |
| Q05 录制归档 | 用户显式配置；凭据进 `secrets.dat`；默认不上传；可撤销/删除 |
| S08/E06 复盘/看板 | 只存本机；可导出/清空；不上传平台；不采集非必要个人信息 |

---

## 7. 兼容性与降级

### 7.1 双 TFM 矩阵

| 能力 | net10 / Win10-11 | net6 / Win7 | 说明 |
|---|---|---|---|
| 手机遥控 | 可用 | 可用 | Win7 用 SSE/长轮询，不用 `HttpListener` WebSocket |
| 手机预览 | 可用 | 可用（1 fps） | 截图走 obs-websocket 4.x/5.x 分支 |
| 手机提词器 | 可用 | 可用 | 复用现有提词器 |
| Wi-Fi 摄像头 | 可用 | **禁用** | sidecar/Win7 不承诺；走 USB/外部应用 |
| USB 摄像头引导 | 可用 | 可用 | scrcpy/OBS Camera 外部 |
| 弹幕 | 可用 | 可用（TLS 1.2） | B 站/Twitch；YouTube 后续扩展 |
| 实时字幕 | 可用 | 降级/禁用 | SAPI 兜底或明确提示 |
| 高光切片 | 可用 | 外部 FFmpeg | CPU/路径 |
| 多平台转推 | 可用（sidecar） | 不承诺 Win7 | mediamtx/SRS |
| 多机位/远程嘉宾 | 可用 | **禁用** | SFU/TURN sidecar；Win7 走外部 |
| 装修/插件市场 | 可用 | 可用（TLS 1.2） | 供应链/签名/回滚 |
| 硬件/灯光 | 可用 | 可用（部分驱动） | MIDI P/Invoke；WLED HTTP |
| 通知/剪贴板上屏 | Android 可用 | 不适用 | iOS 受系统限制；必须显式授权 |
| 数字人/3D overlay | 可用 | 降级/禁用 | 依赖 OBS browser source 与 WebGL |
| 复盘/看板 | 可用 | 可用 | 本地统计；可导出 |
| 无障碍 | 可用 | 可用 | 手机页面同样适配 |

### 7.2 零 NuGet 的实现策略

| 需求 | 不要引入 | 替代方案 |
|---|---|---|
| QR 生成 | QRCoder NuGet | 内嵌 MIT 源码或极简字节模式编码器 |
| HTTP/WS 服务 | ASP.NET Core | `TcpListener` + 手写 HTTP/RFC6455；Win7 用 SSE |
| 音频采集 | NAudio | WASAPI/WinMM P/Invoke；只写需要的接口 |
| 聊天 | TwitchLib NuGet | 手写 IRC/WS；协议核心可单测 |
| WebRTC | SIPSorcery NuGet | 优先 mediamtx sidecar；纯 C# 仅技术验证 |
| ASR | Whisper.net NuGet | whisper.cpp sidecar |
| 视频处理 | FFmpeg.AutoGen | 外部 ffmpeg |
| JSON | Newtonsoft.Json | `System.Text.Json` |
| TTS | edge-tts NuGet | Windows SAPI；可选 piper sidecar |
| 本地推理 | ONNX Runtime NuGet | 外部 sidecar/原生 DLL（按需） |

> 如果确实必须引 NuGet：先写 ADR，说明体积/许可证/维护风险，并同步修改 README「零第三方依赖」表述；默认否决。

### 7.3 离线-first 降级

- 手机遥控/预览/提词器/补光灯：**完全局域网，不需要互联网**。
- 手机摄像头：sidecar 首次下载需要网络；之后离线可用；可提供离线包手动放置。
- 弹幕：需要互联网；断网时显示“离线”，不影响 OBS 控制。
- 字幕：模型首次下载；之后离线；云端翻译可选。
- 切片/归档：本地 FFmpeg/rclone；云端归档可选。
- AI：沿用现有“本地规则 → 免费 AI → 云端”降级链，不因新功能改变。

### 7.4 防火墙与杀软

- 首次启动 LAN 服务会触发 Windows 防火墙提示；应用给出图文说明，**不自动执行 `netsh advfirewall`**。
- 提供“打开 Windows 防火墙设置”按钮（打开系统界面，不代改）。
- sidecar 可能被杀软误报：提供 SHA-256、官方来源、版本固定；不引导关闭杀软。
- 不做 UPnP、不做端口转发、不做 hosts/代理修改。

---

## 8. 技术参考：可复用 / 二次开发仓库

> **核对说明（2026-10-30）**：下表仓库的存在性、归档状态与 SPDX 许可证通过 GitHub API 核对。
> 标注“Other/需复核”表示 GitHub 未识别为标准许可证，落地前必须读 `LICENSE` 原文并让法务/维护者确认。
> 许可证会变、仓库会归档；**引入前重新核对**，不要只信本文。

### 8.1 复用方式定义

| 方式 | 含义 | 适用许可证 | 要求 |
|---|---|---|---|
| **源码内嵌** | 把源码复制进本仓库并编译 | MIT / Apache-2.0 / BSD / ISC | 保留版权与许可证；Apache 保留 NOTICE；写入 `OPEN_SOURCE_ATTRIBUTION.md` |
| **协议复刻** | 只按公开协议自行实现，不复制代码 | 任意（含 GPL） | 不抄代码/注释/常量表；协议文档链接入库 |
| **外部进程** | 作为独立 sidecar 下载/启动 | MIT / LGPL / GPL / 专有 | 用户确认；哈希；许可证；不静态链接；退出即停 |
| **外部插件** | 用户自行安装的 OBS 插件 | GPL / 专有 | 不捆绑、不静默安装；只做检测/引导 |
| **仅参考** | 只参考设计/算法/协议 | 任意 | 不复制代码；注明参考来源 |

### 8.2 推荐优先复用（开源许可，含协议复刻）

| 仓库 | 许可证 | 用途 | 3.4.0 落点 | 复用方式 | 注意 |
|---|---|---|---|---|---|
| [`bluenviron/mediamtx`](https://github.com/bluenviron/mediamtx) | MIT | SRT/RTMP/WebRTC/RTSP 本地媒体服务器 | 手机摄像头主链路 | 外部进程 | 体积/端口/防火墙；Win7 不承诺；API 只绑回环 |
| [`ossrs/srs`](https://github.com/ossrs/srs) | MIT | RTMP/WebRTC 媒体服务器、转推 | 多平台转推（序 38） | 外部进程 | 配置复杂；HTTP API；Win7 不承诺 |
| [`pion/webrtc`](https://github.com/pion/webrtc) | MIT | Go WebRTC 实现 | WHIP/WHEP 方案参考 | 仅参考 | 非 C#；不能直接进主程序 |
| [`versatica/mediasoup`](https://github.com/versatica/mediasoup) | ISC | SFU，多嘉宾/多机位 | 远程嘉宾（序 11） | 外部进程 | Node/C++；部署与 TURN 复杂 |
| [`livekit/livekit`](https://github.com/livekit/livekit) | Apache-2.0 | SFU / 实时音视频 | 远程嘉宾（序 11） | 外部进程/云 | 重；需要 token 服务；隐私边界 |
| [`Genymobile/scrcpy`](https://github.com/Genymobile/scrcpy) | Apache-2.0 | Android USB 屏幕/摄像头 | USB 兜底引导 | 外部工具/可选下载 | 需 adb；不捆绑；驱动/权限 |
| [`josephdadams/TallyArbiter`](https://github.com/josephdadams/TallyArbiter) | MIT | Tally 灯协议/设计 | Tally 设计参考 | 仅参考/可选集成 | 外部服务；不要引入整套 |
| [`BarRaider/obs-websocket-dotnet`](https://github.com/BarRaider/obs-websocket-dotnet) | MIT | .NET obs-websocket 客户端 | 协议对照/测试 | 仅参考 | 现有客户端已手写，不替换 |
| [`xfgryujk/blivedm`](https://github.com/xfgryujk/blivedm) | MIT | B 站弹幕协议 | B 站适配器 | 协议复刻 | Python 参考；不复制 |
| [`TwitchLib/TwitchLib`](https://github.com/TwitchLib/TwitchLib) | MIT | Twitch IRC/API | Twitch 适配器 | 协议复刻 | 零 NuGet；只取 IRC 逻辑 |
| [`jabbany/CommentCoreLibrary`](https://github.com/jabbany/CommentCoreLibrary) | MIT | 弹幕渲染 | overlay | 源码内嵌（前端） | CSP/XSS；前端资源 |
| [`hihkm/DanmakuFactory`](https://github.com/hihkm/DanmakuFactory) | MIT | 弹幕转 ASS | 录制后期（序 32） | 外部工具 | 落地前复核许可证；路径/编码 |
| [`ggml-org/whisper.cpp`](https://github.com/ggml-org/whisper.cpp) | MIT | 本地 ASR | 实时字幕 | 外部进程 | 模型许可；CPU/AVX；Win7 |
| [`rhasspy/piper`](https://github.com/rhasspy/piper) | MIT（已归档） | 本地 TTS | 弹幕朗读（序 16） | 外部进程 | 已归档；模型许可；可换维护分支 |
| [`ollama/ollama`](https://github.com/ollama/ollama) | MIT | 本地 LLM | AI 搭档（序 46） | 外部进程 | 体积/GPU；模型许可 |
| [`microsoft/onnxruntime`](https://github.com/microsoft/onnxruntime) | MIT | 本地推理 | 面捕/降噪（序 37/46） | 外部进程/原生 DLL | x64；DLL 分发 |
| [`xiph/rnnoise`](https://github.com/xiph/rnnoise) | BSD-3-Clause | 降噪 | 音频链（序 37） | 原生 DLL/外部 | 模型许可；采样率 |
| [`naudio/NAudio`](https://github.com/naudio/NAudio) | MIT | 音频采集参考 | WASAPI/MIDI | 仅参考/P-Invoke | 不引 NuGet |
| [`FFmpeg/FFmpeg`](https://github.com/FFmpeg/FFmpeg) | LGPL-2.1+（部分 GPL） | 转码/剪辑/推流 | 切片/转推 | 外部进程 | 只用 LGPL 构建；不捆 GPL |
| [`Breakthrough/PySceneDetect`](https://github.com/Breakthrough/PySceneDetect) | BSD-3-Clause | 场景切分 | 高光切片 | 仅参考 | Python；算法复刻 |
| [`emilianavt/OpenSeeFace`](https://github.com/emilianavt/OpenSeeFace) | BSD-2-Clause | 面捕 | VTuber 2.0 | 外部进程 | VMC 协议；模型 |
| [`pixiv/three-vrm`](https://github.com/pixiv/three-vrm) | MIT | VRM 渲染 | 3D 形象 overlay | 前端内嵌 | three.js；CSP |
| [`guansss/pixi-live2d-display`](https://github.com/guansss/pixi-live2d-display) | MIT | Live2D 渲染 | overlay | 前端内嵌 | Cubism Core 专有 |
| [`DenchiSoft/VTubeStudio`](https://github.com/DenchiSoft/VTubeStudio) | MIT | VTS API 文档 | 现有 Live2D | 协议复刻 | VTS 应用专有；用户安装 |
| [`rclone/rclone`](https://github.com/rclone/rclone) | MIT | 云/NAS 归档 | 自动归档（序 39） | 外部进程 | 配置/密钥；用户授权 |
| [`codebude/QRCoder`](https://github.com/codebude/QRCoder) | MIT（落地前复核） | QR 生成 | 配对二维码 | 源码内嵌 | 只取生成核心；保留版权 |
| [`WLED/WLED`](https://github.com/WLED/WLED) | EUPL-1.2 | 灯光 HTTP API | 灯光联动（序 41） | 仅调用 API | 不复制代码；EUPL 有传染性 |
| [`obsproject/obs-websocket`](https://github.com/obsproject/obs-websocket) | GPL-2.0 | OBS 控制协议 | 全部控制 | 协议复刻 | 不复制代码 |
| [`obsproject/obs-studio`](https://github.com/obsproject/obs-studio) | GPL-2.0 | OBS 本体/虚拟摄像头 | 依赖 | 外部 | 不复制/不捆绑 |

### 8.3 仅外部进程 / 外部插件（GPL / LGPL / 专有 / 需复核）

| 仓库/项目 | 许可证 | 用途 | 3.4.0 落点 | 边界 |
|---|---|---|---|---|
| [`obsproject/obs-browser`](https://github.com/obsproject/obs-browser) | GPL-2.0 | OBS browser source | overlay 渲染 | OBS 内置/外部；不捆绑 |
| [`Aitum/obs-vertical-canvas`](https://github.com/Aitum/obs-vertical-canvas) | GPL-2.0 | 竖屏画布 | 竖屏（序 33） | 外部插件；不捆绑 |
| [`sorayuki/obs-multi-rtmp`](https://github.com/sorayuki/obs-multi-rtmp) | GPL-2.0 | 多平台推流 | 序 38 | 外部插件；不捆绑 |
| [`WarmUpTill/SceneSwitcher`](https://github.com/WarmUpTill/SceneSwitcher) | GPL-2.0 | 自动化 | 参考 | 外部插件；不复制 |
| [`exeldro/obs-replay-source`](https://github.com/exeldro/obs-replay-source) | GPL-2.0 | 回放源 | 事故回放 | 外部插件；不捆绑 |
| [`DistroAV/DistroAV`](https://github.com/DistroAV/DistroAV) | GPL-2.0 | NDI | 采集链路 | 外部插件；不捆绑 |
| [`Off-World-Live/obs-spout2-plugin`](https://github.com/Off-World-Live/obs-spout2-plugin) | GPL-2.0 | Spout2 | 采集链路 | 外部插件；不捆绑 |
| [`bitfocus/companion`](https://github.com/bitfocus/companion) | Other（需复核） | Stream Deck | 硬件联动 | 外部；不捆绑 |
| [`sipsorcery-org/sipsorcery`](https://github.com/sipsorcery-org/sipsorcery) | Other（需复核） | 纯 C# WebRTC/SIP | 浏览器 WHIP 技术验证 | 源码内嵌前必须读 LICENSE；不默认引入 |
| [`CalcProgrammer1/OpenRGB`](https://github.com/CalcProgrammer1/OpenRGB) | GPL-2.0 | RGB | 灯光联动 | 外部/SDK；不静态链接 |
| [`rany2/edge-tts`](https://github.com/rany2/edge-tts) | LGPLv3（部分 MIT） | TTS | 朗读（序 16/46） | 外部进程；需联网；不静态链接 |
| [`RVC-Project/Retrieval-based-Voice-Conversion-WebUI`](https://github.com/RVC-Project/Retrieval-based-Voice-Conversion-WebUI) | MIT | 变声 | 音频链（序 37） | 外部进程；模型许可；GPU |
| [`lostromb/concentus`](https://github.com/lostromb/concentus) | BSD 风格（需复核） | Opus | 音频（备选） | 源码内嵌/参考；优先 sidecar |
| [`schellingb/UnityCapture`](https://github.com/schellingb/UnityCapture) | 未识别（需复核） | 虚拟摄像头 | USB/兜底 | 外部；不捆绑 |
| [`Palakis/obs-ndi`](https://github.com/Palakis/obs-ndi) | GPL-2.0（已归档） | NDI | 旧版 | 用 DistroAV 替代 |

> **平台/专有**：VTube Studio、OBS Studio、NDI SDK、Live2D Cubism Core、Larix Broadcaster、OBS Camera、Iriun、Camo、抖音直播伴侣、B 站直播姬等，均为**用户自行安装**的外部软件；本文不提供其代码/安装包，不做静默安装。

### 8.4 按功能的选型建议

| 3.4.0 能力 | 首选 | 备选 | 不建议 |
|---|---|---|---|
| 手机遥控 | 无新依赖：obs-websocket 协议 + 本地 HTTP/WS + QR | TallyArbiter 设计参考 | 引入整套 Web 框架 |
| 手机摄像头 | mediamtx（MIT）+ SRT/RTMP | scrcpy（USB Android）；OBS Camera/Iriun（iOS） | 自研 WebRTC/编解码 |
| 浏览器摄像头 | mediamtx WHIP（技术验证） | SIPSorcery（需许可证复核） | 在 Win7 上强推 |
| 弹幕 | 协议复刻（B 站/Twitch） | CommentCoreLibrary 渲染 | 抓登录态/逆向抖音 |
| 字幕 | whisper.cpp sidecar | Vosk（低配）/SAPI（Win7） | 默认云端上传 |
| 高光切片 | FFmpeg + PySceneDetect 算法 | OBS 回放缓存 | 自研视频编解码 |
| 3D 形象 | three-vrm / pixi-live2d-display（overlay） | OpenSeeFace（面捕） | 自研 Live2D 渲染器 |
| 多平台 | mediamtx/SRS 本地转推 | obs-multi-rtmp 外部插件 | 把推流码写进日志 |
| 归档 | rclone | 系统计划任务 + 复制 | 硬编码云盘密钥 |
| 硬件 | Companion（外部）/ WinMM MIDI | WLED HTTP | 捆绑 GPL SDK |

### 8.5 许可证与维护状态提醒

- **mediamtx**：MIT，活跃；但 Win7 兼容性不承诺；Go 二进制约数十 MB；需处理防火墙/杀软误报。
- **scrcpy**：Apache-2.0，活跃；需要 adb 与 USB 调试；Windows 上无系统级虚拟摄像头，通常走窗口采集。
- **whisper.cpp**：MIT，活跃；模型权重另有许可证；CPU 实时性取决于模型大小与 CPU。
- **piper**：MIT 但仓库已归档；可评估维护分支/替代 TTS。
- **FFmpeg**：许可证取决于构建选项；**只使用 LGPL 构建**，不要捆绑 `--enable-gpl` 产物。
- **QRCoder**：MIT 常见标注，但本轮 API 未完成许可证核对；落地前读 LICENSE 并保留版权。
- **sipsorcery/Companion/UnityCapture/Concentus**：GitHub 识别为 Other/未识别；**先读 LICENSE 原文**，不要仅凭“看起来像 MIT”就内嵌。
- **GPL 项目**：一律外部进程/插件；不复制、不静态链接；在 `OPEN_SOURCE_ATTRIBUTION.md` 与“关于”页给出来源与许可证。

---

## 9. 测试矩阵

### 9.1 单元 / 集成

**纯逻辑单测（新增约 150~220 项）**

- `PairingCoreTests`：token 长度/熵、TTL、一次性、常量时间、设备名清洗、角色矩阵。
- `MobileCommandCoreTests`：白名单、参数校验、权限、幂等、限流、破坏性操作确认。
- `MobileStateCoreTests`：快照/增量、Tally、计时、断线重连状态。
- `PhoneCameraCoreTests`：SRT/RTMP URL、随机 key、私网判断、画质/布局映射。
- `SidecarManifestTests`：URL 白名单、SHA-256、路径穿越、版本、许可证字段。
- `LanPolicyTests`：私网 IP、CORS/CSP、来源校验、限流。
- `ChatCoreTests`：归一化、转义、长度、去重、屏蔽、限速。
- `BilibiliPacketCoreTests`：zlib/brotli fixture、畸形包、超大包。
- `TwitchIrcCoreTests`：tags、表情、畸形行、频道名。
- `SubtitleSegmenterTests`：标点、合并、限长、双语。
- `WhisperArgsTests`：参数数组、路径、超时、无 shell。
- `HighlightCoreTests`：评分、窗口、去重、最短/最长。
- `FfmpegArgsTests`：参数白名单、输出路径、失败码。
- `MobilePageAssetTests`：无远程 URL、CSP、i18n 键、触控尺寸。
- `MultiCamCoreTests`：路数上限、布局/切换、并发。
- `RemoteGuestCoreTests`：房间 token、角色、踢人、录制同意。
- `MobileNotifyCoreTests`：授权、敏感内容过滤、确认上屏。
- `ChatInteractionCoreTests`：点歌/投票/抽奖状态机、冷却、防刷。
- `ChatModerationCoreTests`：敏感词、频率、屏蔽用户、导出。
- `CalendarCoreTests`：日程、时区、提醒、overlay。
- `TranslationCoreTests`：双语行、断句、限频、失败回退。
- `VoiceCommandCoreTests`：唤醒词、命令白名单、破坏性确认。
- `SessionCoachCoreTests`：评分、行动清单、报告导出。
- `CoverCopyCoreTests`：标题/话题候选、长度、敏感词。
- `StreamHealthCoreTests`：带宽/掉帧/断流评分、建议。
- `CompositionCoreTests`：亮度/色温/人脸/构图评分。
- `RunbookCoreTests`：检查项、失败回滚、顺序。
- `IncidentClipCoreTests`：环形窗口、导出、磁盘上限。
- `ReframeCoreTests`：9:16 安全区、主体跟踪、裁切窗口。
- `RecordingSearchCoreTests`：索引、检索、删除、隐私。
- `DirectorCoreTests`：规则/建议、人工确认、回退。
- `PanicCoreTests`：静音/BRB/停流、确认、审计。
- `AudioChainCoreTests`：降噪/响度/变声、旁路。
- `RelayCoreTests`：目标、密钥、健康、失败回滚。
- `ArchiveCoreTests`：计划、校验、重试、撤销。
- `HardwareMappingTests`：MIDI 映射、冲突、导入导出。
- `WledClientTests`：URL/超时/错误、无云依赖。
- `ViewerStatsCoreTests`：统计口径、导出、清空。
- `PluginTrustCoreTests`：签名/评分/依赖/沙箱策略。
- `ThemeMarketCoreTests`：资产哈希、CSP、预览、回滚。
- `TeamRolesCoreTests`：角色、邀请、审计、踢人。
- `AiHostCoreTests`：TTS 队列、口型、打断、AI 标注。
- `RoutesTests`/`OnboardingGuideTests`：新路由已注册、自检覆盖。

**无 OBS/无网络冒烟（仿 `tools/PluginSmoke`）**

- `tools/MobileSmoke`：启动 `MobileLinkServer`（回环测试模式）→ 配对 → 发命令 → 断言权限/限流/撤销。
- `tools/ChatSmoke`：假 WS 服务器 → 适配器 → 消息归一化/重连/过滤。
- `tools/AsrSmoke`：假音频/假引擎 → 分段/字幕发布；不下载模型。
- `tools/ClipSmoke`：假 FFmpeg 可执行/参数捕获 → 切片计划；不处理真实视频。
- `tools/StreamSmoke`：假 OBS/统计源 → 健康评分/预案/事故回放。
- `tools/InsightsSmoke`：假复盘/看板数据 → 报告/图表数据/导出。
- 其余功能至少要有“纯逻辑单测 + 一个集成/冒烟用例”，不能只靠手工。

### 9.2 手工测试

1. **手机兼容**：iPhone Safari、Android Chrome、360×640~430×932、横竖屏、深色模式、字体放大。
2. **网络**：同一路由器 2.4/5GHz、AP 隔离、VPN、双网卡、防火墙首次提示、手机热点（不推荐但可测）。
3. **OBS 真实环境**：32.2.2；场景/音频/录制/推流/虚拟摄像头/回放/演播室；媒体源 RTSP；音频同步。
4. **手机推流 App**：Larix（SRT/RTMP）、OBS Camera/Iriun（iOS）；30 分钟稳定性；断流重连。
5. **弹幕**：B 站/Twitch 真实房间；高并发；断网重连；屏蔽词；TTS。
6. **字幕**：中/英样本、口音、噪声、CPU 占用、设备切换、Win7 降级。
7. **无障碍**：NVDA/Narrator、仅键盘、200% DPI、200% 文本、高对比、老年模式、手机页面 44px。
8. **安全**：端口扫描、过期/错误 token、撤销设备、恶意上传、公网来源拒绝、sidecar 哈希不符、日志脱敏。
9. **Win7**：遥控/预览/提词器可用；摄像头/字幕明确降级；不崩溃。
10. **离线**：断网后遥控/预览/提词器/字幕（本地模型已下载）仍可用。
11. **多机位/远程嘉宾**：2~4 台手机同时推流；嘉宾浏览器进房、授权、踢人；TURN/局域网切换。
12. **装修/插件市场**：资产预览/应用/回滚；恶意资产拒绝；插件签名/沙箱/回滚。
13. **硬件/灯光**：Stream Deck/MIDI 映射冲突；WLED 超时降级；不依赖云。
14. **通知/剪贴板**：验证码/密码过滤；每次授权；一键关闭；不持久化。
15. **数字人/AI 搭档**：AI 标注、打断、声音克隆授权、不冒充真人。
16. **全量验收**：按 §3.6 的 46 项逐一打勾；`BLOCKED` 必须有记录与替代方案。

### 9.3 发布门禁

```powershell
dotnet build OBS_Helper.Wpf/OBS_Helper.Wpf.csproj -f net10.0-windows
dotnet build OBS_Helper.Wpf/OBS_Helper.Wpf.csproj -f net6.0-windows
dotnet test  OBS_Helper.Wpf.Tests/OBS_Helper.Wpf.Tests.csproj
python scripts/check_resources.py
python scripts/check_accessibility.py
python scripts/check_docs.py
$env:OBS_SELFTEST=1; dotnet run --project OBS_Helper.Wpf -f net10.0-windows
dotnet run --project NOBS/tools/PluginSmoke/PluginSmoke.csproj -c Debug
dotnet run --project NOBS/tools/MobileSmoke/MobileSmoke.csproj -c Debug
dotnet run --project NOBS/tools/ChatSmoke/ChatSmoke.csproj -c Debug
dotnet run --project NOBS/tools/StreamSmoke/StreamSmoke.csproj -c Debug
dotnet run --project NOBS/tools/InsightsSmoke/InsightsSmoke.csproj -c Debug
.\build.ps1 -SkipLegacy
```

**额外门禁**

- **46 项验收清单全部打勾**；未完成项不得以“后续版本”标注。
- `git diff` 中不得出现新的 `PackageReference`（除非有 ADR 并同步 README）。
- `OPEN_SOURCE_ATTRIBUTION.md` 已更新；新增 sidecar 的许可证/来源/哈希已记录。
- README 的“零第三方依赖”表述与新能力一致（主程序零 NuGet；可选 sidecar 单独下载）。
- 新路由已进自检；`check_docs.py` 数字一致。
- 手机页面/overlay 无远程资源；CSP 生效；中英双语键齐全。
- 每批次结束跑一次本门禁；发布前跑全量。
- 发布说明写清“新增/修复/已知边界/需人工测试项”。

---

## 10. 执行计划（无工期，按序交付）

### 10.1 执行规则

1. **严格按 §3.6 的 1~46 顺序推进**；前项验收未通过，不开始后项。
2. 每项一个 PR/提交：**纯逻辑 + 单测 → 服务/进程 → UI → 文案/文档**。
3. 每批结束跑一次全量门禁（§9.3）；失败先修复，不带着失败进入下一批。
4. 每项独立 feature flag；可单独关闭/回退；不在同一 PR 混入无关重构。
5. **不删减**：遇到平台/许可证硬阻断，记录 `BLOCKED`、影响范围与替代方案；不静默跳过、不把功能改成占位按钮。
6. 验收以 §3.6 的“验收要点”为准；复杂项再对照 §4 的详细设计。
7. 新增路由/服务/文案/错误码/许可证/测试与代码同批更新文档。

### 10.2 批次与门禁

| 批次 | 序号 | 交付 | 门禁 |
|---|---|---|---|
| **0** | 前置 | LAN/权限、sidecar 清单、错误码、i18n 键、HTTP/WS、FFmpeg 参数白名单、AI 工具注册、测试骨架 | 双 TFM 编译；单测；安全清单；sidecar 哈希/许可证测试 |
| **1** | 1~5 | M01/M02/M05/M07/M06：遥控、预览/Tally、提词器、补光、快传 | 扫码可用；权限/撤销/限流；预览 CPU；上传校验 |
| **2** | 6~9 | M08/M03/M04/M12：USB 引导、Wi-Fi 摄像头、麦克风、多手机监看 | 720p30 30 min；断流重连；音频同步；3 会话 |
| **3** | 10~12 | M09/M10/M11：多机位、远程嘉宾、通知上屏 | 2~4 路；SFU 权限/踢人；通知隐私开关 |
| **4** | 13~16 | C01/C02/C09/C03：弹幕聚合、overlay、敏感词、TTS | 2 h 稳定；100 msg/s；XSS/CSP；过滤 |
| **5** | 17~21 | C04/C05/C06/C10/C07：互动玩法、礼物特效、投稿、日历、AI 建议 | 状态机/冷却；审核；人工确认；不自动回平台 |
| **6** | 22~25 | S01/S02/C08/S03：字幕、翻译、弹幕翻译、语音控制 | 中英 <2 s；无音频上传；命令白名单 |
| **7** | 26~31 | S08/S07/Q02/Q04/Q08/Q07：复盘、封面、推流健康、构图、预案、事故回放 | 报告可导出；建议可执行；回放磁盘上限 |
| **8** | 32~39 | S05/S06/S10/S09/Q06/Q03/Q01/Q05：切片、竖屏、检索、导播、安全、音频、转推、归档 | 输出可回滚；密钥不落日志；安全动作需确认 |
| **9** | 40~46 | E01/E02/E06/E05/E03/E04/S04：硬件、灯光、看板、插件/装修市场、协作、数字人 | 权限/审计；供应链；AI 标注；内容合规 |

### 10.3 提交切分（按批次）

- **批次 0**：`chore(infra): ...`（错误码/i18n/sidecar/HTTP/测试骨架）
- **批次 1~3**：`feat(mobile): ...` / `feat(camera): ...` / `feat(multicam): ...`
- **批次 4~5**：`feat(chat): ...` / `feat(interaction): ...`
- **批次 6**：`feat(asr): ...` / `feat(translate): ...` / `feat(voice): ...`
- **批次 7~8**：`feat(insights): ...` / `feat(clips): ...` / `feat(stream): ...` / `feat(safety): ...`
- **批次 9**：`feat(automation): ...` / `feat(market): ...` / `feat(team): ...` / `feat(aihost): ...`

每个 PR 只做一项或一个紧耦合小步；**不要用一个 PR 提交 46 项**。每个 PR 的描述必须写：对应序号、验收项、开关名、回退方式、测试命令。

### 10.4 完成定义（DoD）

- **46 项全部通过 §3.6 验收**；不存在“占位按钮 / 仅 UI / 仅文档 / 后续再说”冒充完成。
- 双 TFM 编译 0 警告（或仅既有警告）；新增单测全绿；冒烟工具全过。
- `check_resources` / `check_accessibility` / `check_docs` 通过；自检新增路由通过。
- 无新增 NuGet；sidecar 清单/哈希/许可证完整；署名页更新。
- 中英双语文案齐全；手机页面/overlay 无硬编码中文。
- 安全复核：token/权限/限流/撤销/上传/日志脱敏/CSP/供应链逐项签字。
- 人工测试矩阵（§9.2）完成并记录；Win7 降级明确。
- README / 发布说明 / 隐私说明与实现一致；不夸大“零延迟/全平台/一键成功”。

---

## 11. 风险与对策

| 风险 | 概率 | 影响 | 对策 |
|---|---|---|---|
| iOS Safari 摄像头 secure context 限制 | 高 | 中 | 3.4.0 不承诺浏览器摄像头；主推 Larix/OBS Camera；WHIP 仅技术验证 |
| 防火墙/杀软拦截局域网/ sidecar | 高 | 高 | 首次图文引导；不自动改规则；提供哈希/来源；测试主流杀软 |
| Wi-Fi 抖动/AP 隔离 | 高 | 中 | 5GHz 建议；断流重连；USB 兜底；明确错误 |
| OBS 媒体源 RTSP 兼容性 | 中 | 中 | 多版本测试；`vlc_source`/HTTP-FLV 兜底 |
| Win7 不支持 sidecar | 高 | 中 | 明确禁用；USB/外部应用；不假支持 |
| 平台弹幕协议变更 | 中 | 中 | 适配器隔离；feature flag；快速热修；不做抖音逆向 |
| 字幕准确率/CPU | 中 | 中 | 模型分档；VAD；降频；SAPI/Vosk 兜底；用户可关 |
| 远程嘉宾/SFU 复杂度 | 高 | 中 | 先 LAN/自建；TURN 可选；明确不支持公网默认；失败回退到“屏幕共享/预录” |
| 装修/插件市场供应链 | 中 | 高 | 哈希/签名/沙箱/预览/回滚；不静默安装；来源展示 |
| 硬件/灯光兼容性 | 中 | 低 | 只做标准 MIDI/WLED HTTP；超时降级；不承诺所有设备 |
| 通知/剪贴板隐私 | 中 | 高 | 每次授权；敏感内容过滤；不持久化；一键关闭 |
| 数字人/声音克隆合规 | 中 | 高 | AI 标注；不冒充真人；音色授权；可打断 |
| GPL/许可证风险 | 中 | 高 | 外部进程边界；法务复核；署名；不复制 |
| 全量交付压力 | 高 | 高 | 严格按 §3.6 顺序；每项独立开关；批次门禁；不删减但可回退；失败记录 `BLOCKED` |
| 局域网暴露 | 中 | 高 | 默认关闭；token/角色/限流/撤销；公网 IP 提示；安全复核 |
| sidecar 体积/AV 误报 | 中 | 中 | 可选下载；版本固定；哈希；不捆绑；离线包 |
| OBS 高频事件拖慢 UI | 中 | 低 | 默认不订阅 `InputVolumeMeters`；按需/节流 |
| 多语言/无障碍债 | 中 | 中 | 键从第一天加；静态检查；手机页面测试 |
| 手机页面兼容性 | 中 | 中 | 无框架、系统字体、渐进增强；真机矩阵 |

---

## 12. 决策记录与待确认项

### 12.1 已确定（本次要求）

- **§3 的 46 项全部进 3.4.0**，不裁剪、不顺延、不以“后续版本”替代。
- **唯一顺序 = §3.6 的 1~46**；批次只控制合入节奏，不改变顺序。
- **不提供工期估算**；按序全部做完即可。
- 手机不直连 OBS；PC 侧做权限与命令白名单。
- 摄像头主链路 = mediamtx sidecar + SRT/RTMP；不自研 WebRTC。
- 所有 LAN 能力默认关闭；不做公网/UPnP。
- 弹幕先做 B 站/Twitch；抖音/快手不做；YouTube/Kick 为后续扩展。
- 字幕先做本地 Whisper；云翻译可选。
- 主程序零 NuGet；重能力走 sidecar；GPL 只做外部。

### 12.2 实施前需要确认

1. **sidecar 分发方式**：首次使用时下载（推荐）还是随安装包附带离线包？涉及体积/AV/更新。
2. **手机端形态**：3.4.0 只做浏览器 + 现有推流 App，还是同步启动原生 App 项目？
3. **摄像头主链路**：mediamtx 的 MIT 许可证与体积可接受吗？是否要求纯 C#（SIPSorcery）？
4. **字幕模型档位**：默认 base/tiny 还是 small？CPU 占用与准确率的取舍。
5. **页面入口**：5 个入口（手机/互动/智能/画质/自动化）先走卡片，还是直接提升为一级导航？
6. **是否需要公网远程**：本文明确不做；若要做，需单独的安全/中继/账号体系设计。
7. **硬阻断处理**：若某平台/许可证确实无法满足，是否接受 `BLOCKED` + 替代方案记录，而不是静默降级？默认按“记录、不删减”执行。
8. **M11 平台边界**：是否接受 Android-only（helper/ADB），iOS 明确不支持？默认按此执行。

---

## 附录 A：建议的新增路由与页面

| 路由/页面 | 类型 | 入口 | 说明 |
|---|---|---|---|
| `Routes.MobileLink = "mobile"` | 页面 | 首页卡片 / 控制台 / 托盘 / 设置 | 手机联动：遥控 / 摄像头 / 多机位 / 设备 |
| `Views/MobileLinkPage.xaml` | 页面 | 同上 | 分段按钮：遥控、摄像头、多机位、设备 |
| `Controls/PairingCard.xaml` | 控件 | 手机联动页 | 二维码、短码、过期、设备列表 |
| `Routes.Interaction = "interaction"` | 页面 | 首页卡片 / 虚拟直播页 | 弹幕、字幕、互动、切片 |
| `Views/InteractionPage.xaml` | 页面 | 同上 | 分段按钮：弹幕、字幕、互动、切片 |
| `Routes.Insights = "insights"` | 页面 | 首页卡片 / 性能页 | 复盘、封面文案、观众看板 |
| `Views/InsightsPage.xaml` | 页面 | 同上 | 分段按钮：复盘、封面、看板 |
| `Routes.Stream = "stream"` | 页面 | 首页卡片 / 控制台 | 推流健康、转推、归档、安全哨兵、预案 |
| `Views/StreamPage.xaml` | 页面 | 同上 | 分段按钮：健康、转推、归档、安全、预案 |
| `Routes.Automation = "automation"` | 页面 | 首页卡片 / 设置 / 工具箱 | 硬件、灯光、插件/装修市场、团队、数字人、AI 导播 |
| `Views/AutomationPage.xaml` | 页面 | 同上 | 分段按钮：硬件、市场、团队、数字人、导播 |

> 新路由必须加入 `MainWindow` 自检 `cases`；`check_docs.py` 会核对路由/自检数字。

## 附录 B：错误码建议

| 错误码 | 用户可见含义 | 下一步 |
|---|---|---|
| `MOB_DISABLED` | 手机联动未开启 | 到设置/手机联动页开启 |
| `MOB_NO_LAN` | 未找到可用局域网地址 | 检查 Wi-Fi/网线；关闭 VPN/虚拟网卡 |
| `MOB_PAIR_EXPIRED` | 配对码已过期 | 重新生成二维码 |
| `MOB_PAIR_BAD_SECRET` | 配对码不正确 | 重新扫码/输入短码 |
| `MOB_TOKEN_INVALID` | 设备会话失效 | 重新配对；检查是否被撤销 |
| `MOB_RATE_LIMITED` | 操作太频繁 | 稍后再试 |
| `MOB_ROLE_DENIED` | 当前角色无权限 | 让管理员在 PC 上调整权限 |
| `MOB_OBS_OFFLINE` | OBS 未连接 | 先在应用内连接 OBS |
| `MOB_COMMAND_INVALID` | 参数不合法 | 刷新页面后重试 |
| `MOB_PREVIEW_FAILED` | 预览截图失败 | 检查 OBS 是否在输出；降低帧率 |
| `CAM_SIDECAR_MISSING` | 媒体服务器未安装 | 按提示下载/选择离线包 |
| `CAM_SIDECAR_START_FAILED` | 媒体服务器启动失败 | 检查端口占用/杀软；查看日志 |
| `CAM_PORT_IN_USE` | 端口被占用 | 重启应用自动换端口 |
| `CAM_NO_PRIVATE_IP` | 没有私网地址 | 连接路由器 Wi-Fi；不要用公网 |
| `CAM_STREAM_TIMEOUT` | 未收到手机推流 | 检查同一 Wi-Fi/推流地址/防火墙 |
| `CAM_UNSUPPORTED_WIN7` | Win7 不支持 Wi-Fi 摄像头 | 改用 USB 或外部虚拟摄像头 |
| `CHAT_CONNECT_FAILED` | 弹幕连接失败 | 检查房间号/网络；稍后重试 |
| `CHAT_PROTOCOL_CHANGED` | 平台协议变化 | 更新应用；查看反馈 |
| `ASR_MODEL_MISSING` | 字幕模型未安装 | 下载/导入模型 |
| `ASR_AUDIO_DEVICE` | 音频设备不可用 | 选择其他设备；检查独占模式 |
| `ASR_UNSUPPORTED_WIN7` | Win7 不支持本地字幕 | 使用 SAPI 或关闭字幕 |
| `CLIP_FFMPEG_MISSING` | 未找到 FFmpeg | 指定路径/下载离线包 |
| `MULTI_LIMIT` | 机位数量超限 | 先移除一路或降低画质 |
| `REMOTE_ROOM_FULL` | 嘉宾房间已满 | 踢人/升级 SFU 配置 |
| `THEME_HASH_MISMATCH` | 主题资产校验失败 | 重新下载/换来源 |
| `HARD_MIDI_CONFLICT` | MIDI 映射冲突 | 重新分配/恢复默认 |
| `STREAM_RELAY_FAILED` | 转推失败 | 检查目标密钥/带宽/目标平台 |
| `ARCHIVE_REMOTE_MISSING` | 归档远端未配置 | 配置 rclone/WebDAV |
| `VOICE_COMMAND_DENIED` | 语音命令被拒绝 | 使用白名单命令或人工操作 |
| `AIHOST_LIPSYNC_FAILED` | 口型同步失败 | 检查 Live2D/VTS；降级纯 TTS |

## 附录 C：sidecar 清单字段（建议）

```json
{
  "id": "mediamtx",
  "version": "1.x.y",
  "url": "https://github.com/bluenviron/mediamtx/releases/download/v1.x.y/mediamtx_v1.x.y_windows_amd64.zip",
  "sha256": "<64 hex>",
  "size": 12345678,
  "license": "MIT",
  "source": "https://github.com/bluenviron/mediamtx",
  "minOs": "10.0",
  "arch": "x64",
  "args": ["<config path>"],
  "notes": "仅监听私网；API 绑定 127.0.0.1；退出即停"
}
```

- 清单本身入库；**二进制不入库**。
- 下载前展示来源/版本/许可证/体积/哈希；用户确认。
- 解压用安全解压（拒绝 `..`/绝对路径/符号链接）；可执行文件只允许清单内。
- 版本固定；更新需用户确认；保留上一版以便回滚。

## 附录 D：参考链接

- OBS WebSocket 协议：<https://github.com/obsproject/obs-websocket>
- mediamtx：<https://github.com/bluenviron/mediamtx>
- scrcpy：<https://github.com/Genymobile/scrcpy>
- whisper.cpp：<https://github.com/ggml-org/whisper.cpp>
- VTube Studio API：<https://github.com/DenchiSoft/VTubeStudio>
- B 站弹幕协议参考：<https://github.com/xfgryujk/blivedm>
- Twitch IRC：<https://dev.twitch.tv/docs/irc/>
- TallyArbiter：<https://github.com/josephdadams/TallyArbiter>
- CommentCoreLibrary：<https://github.com/jabbany/CommentCoreLibrary>
- QRCoder：<https://github.com/codebude/QRCoder>
- rclone：<https://github.com/rclone/rclone>
- WLED：<https://github.com/WLED/WLED>

> 本附录只列本文实际引用/推荐的仓库；未列出的项目不代表不可用，落地前请按 §6.3 的许可证红线重新核对。
