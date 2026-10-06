# OBS帮助助手 · 项目库代码清单（CODEBASE）

> 本文件是 `OBS_Helper.Wpf`（Windows 原生 WPF 版）的完整代码清单：
> 按模块列出每个源码 / 资源文件及其职责，方便快速定位代码。
> 架构总览见 [`docs/ARCHITECTURE.md`](ARCHITECTURE.md)。
>
> **为什么没有「行数」列**：原先每个文件标了「编写时近似行数」，但那列天然会漂 ——
> 删列前复核了一次（基线是提交里的那一版）：带行数的 **51 个 `.cs` 条目里有 34 个已经不准**，
> 偏差还很大 —— `ObsLogAnalyzer.cs` 标 451 行、当时实测 955 行（现在 1009 行）；
> `ObsWebSocketClient.cs` 标 351 行、实测 777 行；`AppServices.cs` 标 125 行、实测 221 行。
> 维护一列注定过期的数字只会制造新的「文档与代码事实漂移」，
> 所以整列删掉：量级看目录结构，精确行数直接看文件。
>
> 本文件里可自动核对的数字（服务数、路由数、自检项数、文案键数、知识库条数）
> 由 [`scripts/check_docs.py`](../scripts/check_docs.py) 从源码实测比对，不一致即非零退出。

## 1. 顶层结构

```
NOBS/
├── OBS_Helper.Wpf/          # 主程序（WPF，net10.0-windows）
├── OBS_Helper.Wpf.Tests/    # 单元测试（xUnit，逐个 <Compile Include> 链接纯逻辑文件；另以 ProjectReference 构建主工程做编译守护）
├── OBS_Helper.slnx          # 解决方案（单项目）
├── build.ps1                # 出包脚本：dotnet publish R2R + Inno Setup + 便携 zip
├── installer/               # 随仓库固定的 Inno Setup 语言文件
├── docs/                    # 架构 / 代码清单 / 设计文档
│   └── reviews/             # 版本发布审查报告
├── scripts/                 # 数据与资产生成脚本（Python / PowerShell）
├── RELEASE_NOTES_v*.md      # 各版本发布说明
├── .gitignore
├── README.md
└── LICENSE
```

## 2. 源码文件清单（按模块）

### 2.1 入口与组合根

| 文件 | 职责 |
|---|---|
| `App.xaml` | 应用资源字典装配（主题、图标、控件样式）。 |
| `App.xaml.cs` | 启动入口：单实例 Mutex、异常三挂钩（Dispatcher/AppDomain/Unobserved）、启动服务装配、`HeadlessTest` 自检模式、新手引导重播事件。 |
| `AppServices.cs` | 组合根：**52 个**服务的手工 Lazy 单例装配（刻意不用 DI 容器）。 |
| `MainWindow.xaml` / `.xaml.cs` | 主窗口：导航框架、页面路由表、页面过渡动画、托盘联动、**新手引导覆盖层**（首启分步引导 + 设置页即时重播；V2.9.1 起每步自动跳转到对应页面并渲染站内/站外跳转按钮）、`OBS_SELFTEST=1` 的 **22 项**无界面自检。 |
| `app.manifest` | Windows 清单（Per-Monitor V2 DPI 感知、执行级别、支持的 OS 版本）。 |

### 2.2 Models（数据模型）

| 文件 | 职责 |
|---|---|
| `Models/Problem.cs` | 问题条目（知识库单条）。 |
| `Models/ProblemData.cs` | `problems.json` 根对象。 |
| `Models/Category.cs` | 问题分类（首页九宫格 / 分类页）。 |
| `Models/Obs/ObsProtocol.cs` | obs-websocket 协议消息模型（Hello/挑战、事件、请求）。 |
| `Models/ObsConfig/ObsConfigModels.cs` | OBS 配置目录定位结果、导入导出模型。 |
| `Models/ObsConfig/SceneTemplate.cs` | 场景模板（画布 + 场景 + 来源）。 |
| `Models/Shell/ShellSettings.cs` | 托盘与后台行为设置。 |
| `Models/Shell/HotkeySettings.cs` | 全局热键配置。 |
| `Models/Shell/AutoSwitchSettings.cs` | 场景自动切换配置。 |
| `Models/Shell/MiniWindowSettings.cs` | 迷你小窗位置记忆。 |

### 2.3 Services（业务服务）

#### 2.3.1 连接层（OBS 通信）

| 文件 | 职责 |
|---|---|
| `Services/Obs/ObsWebSocketClient.cs` | obs-websocket 底层客户端：连接、鉴权、请求/响应、事件分发、重连。 |
| `Services/Obs/ObsConnectionService.cs` | 连接生命周期编排：自动连接、状态机、重连调度、事件订阅管理。 |
| `Services/Obs/ObsAuth.cs` | 基于 salt/challenge 的鉴权字符串计算。 |
| `Services/Obs/ObsReconnectPolicy.cs` | 重连退避策略。 |
| `Services/Obs/ObsLegacyV4Core.cs` | **obs-websocket 4.x（旧协议）兼容核心**（纯逻辑、零 IO、可单测）：把上层的 v5 请求名/字段翻译成 v4、把 v4 响应归一化回 v5 形状、把 v4 `update-type` 映射成 v5 事件，因此连接层与各页面一行都不用改。**只在 Win7 兼容构建启用**（`net6.0-windows` 定义 `WIN7_COMPAT`，默认端口 4444）—— OBS 28 起不再支持 Win7/8.1，Win7 能用的最新 OBS 是 27.x + obs-websocket 4.9.x 插件。v4 确实没有的能力（如 `SetProfileParameter` / `SetVideoSettings`）**不假装支持**，返回「旧协议不支持」由上层走文件通道。 |
| `Services/Obs/ObsSettingsService.cs` | 连接参数（地址/端口/密码）持久化与读取。 |
| `Models/Obs/ObsProtocol.cs` | 协议消息模型（见 Models）。 |

#### 2.3.2 诊断引擎（AI 三通道）

| 文件 | 职责 |
|---|---|
| `Services/Ai/DiagnosticOrchestrator.cs` | 诊断编排：按可用性选择云端 / 免费 / 本地引擎，汇总诊断项。 |
| `Services/Ai/CloudDiagnosticEngine.cs` | 云端大模型通道（API Key + function-calling 工具调用）。 |
| `Services/Ai/FreeDiagnosticEngine.cs` | 免费内置 AI 通道（智谱内置密钥 + Pollinations 免 Key）。 |
| `Services/Ai/LocalDiagnosticEngine.cs` | 本地搜索助手（知识库检索，无网络）。 |
| `Services/Ai/ObsToolRegistry.cs` | 供云端大模型调用的诊断工具注册表（get_problem_detail 等）。 |
| `Services/Ai/DiagnosticTypes.cs` | 诊断项 / 报告模型。 |
| `Services/Ai/DiagnosticSeverity.cs` | 严重程度枚举。 |
| `Services/Ai/DiagnosticSeverityMapper.cs` | 日志严重度 ↔ 知识库文案映射（纯函数）。 |
| `Services/Ai/FreeAiKeyProvider.cs` | 内置免费密钥的解密读取（构建期由脚本注入）。 |
| `Services/Ai/FreeRateLimiter.cs` | 免费通道本地限额（按通道独立限频，持久化 prefs.json）。 |
| `Services/Ai/AiSettingsService.cs` | AI 设置（模式选择、API Key、模型白名单）持久化。 |

#### 2.3.3 OBS 配置管理（备份 / 恢复 / 重置 / 模板 / 录制环境）

| 文件 | 职责 |
|---|---|
| `Services/ObsConfig/ObsPathService.cs` | OBS 配置目录定位（注册表 + 常见路径探测）。 |
| `Services/ObsConfig/ObsSafePath.cs` | 路径安全护栏：防目录穿越，越界抛 `SafePathException`。 |
| `Services/ObsConfig/ObsBackupService.cs` | 配置备份（zip 打包）与导入（校验、事务恢复）。 |
| `Services/ObsConfig/ObsResetService.cs` | 配置重置（连接态校验、场景清空）。 |
| `Services/ObsConfig/SceneTemplateService.cs` | 场景模板：在线落地（obs-websocket 建集合/场景/来源）与离线导出（JSON）。 |
| `Services/ObsConfig/RecordingToolsService.cs` | 录像工具（V2.6）：录像目录解析与直达、ffmpeg 探测、MKV/Hybrid MP4 → MP4 无损转封装。 |
| `Services/ObsConfig/SimpleRecordingCore.cs` | **简单录像纯逻辑**（V2.9.4）：三档预设、预设 → 推荐项、就绪判定、剩余可录估算、跨会话待回滚记录的存储键与文案口径。 |
| `Services/ObsConfig/ObsLaunchCore.cs` | **拉起 OBS 的纯逻辑**（V2.9.4）：`DisplayIcon` 解析（含引号 / `,0` 后缀 / 目录名带逗号）、安装根推导四档、去重与存在性过滤。探测留在 `ObsPathService`，这里只做字符串与路径推导。 |
| `Services/ObsConfig/RecordingEnvService.cs` | 一键部署录制环境（V2.9.3）：双通道落地 + 备份 / 回滚 / 读回校验；V2.9.4 增加「录制 / 推流中」硬阻断（建计划与执行点各判一次）与只读快照。 |
| `Services/ObsConfig/RecordingEnvCore.cs` | **一键部署录制环境的推荐项纯逻辑**（V2.9.3）与跨会话「待回滚记录」模型（V2.9.4）：每项给出「当前值 → 推荐值」与落地通道，由调用方决定勾选与落地方式。 |
| `Services/Shell/SimpleRecordingService.cs` | **简单录像服务**（V2.9.4）：状态机与落地编排、拉起 OBS 并等就绪、录制中进度、停止收尾、跨会话回滚记录的落盘与恢复。 |
| `Services/ObsConfig/FileTx.cs` | 文件事务：提交 / 回滚目录级操作。 |
| `Services/ObsConfig/SafeIniFile.cs` | 对 OBS 配置文件（`basic.ini` 一类）的**受护栏保护的原子写入**：写前留 `.obshelper.bak` → 过 `ObsSafePath` 护栏 → 同目录临时文件原子替换；任一步失败即抛异常，**绝不半途留下截断的文件**。 |

#### 2.3.4 日志分析

| 文件 | 职责 |
|---|---|
| `Services/Obs/ObsLogAnalyzer.cs` | OBS 日志解析：错误/警告/严重度分类、统计与报告。 |
| `Services/Obs/LogSanitizer.cs` | 日志脱敏：抹掉密钥/敏感串，同时保留 OBS 正常长串。 |

#### 2.3.5 宿主 / 存储 / 基础设施

| 文件 | 职责 |
|---|---|
| `Services/Host/HostBridge.cs` | 宿主能力：机密存储（DPAPI+AES-GCM 双层）、日志目录访问、SSRF 防护、环境信息。 |
| `Services/Host/LocalStore.cs` | 本地键值存储（prefs.json / secrets.dat 的底层读写）。 |
| `Services/Markdown/MarkdownRenderer.cs` | Markdown → 富文本渲染（安全链接白名单）。 |
| `Services/ProblemService.cs` | 知识库（problems.json）加载与检索。 |
| `Services/AssistantService.cs` | 助手对话会话管理。 |
| `Services/BookmarkService.cs` | 收藏（书签）持久化与变更通知。 |
| `Services/AppearanceService.cs` | 主题（深浅色）/ 字号档位 / 减少动画 / 应用内高对比的切换与持久化。 |
| `Services/UpdateService.cs` | 更新检查（蓝奏云 + GitHub Release 双通道，资产家族按语言严格匹配）、下载与安装。 |
| `Services/OsSupport.cs` | Windows 版本判定（V2.9.3，纯逻辑）：现代构建 ≥ Win10、兼容构建 Win7 SP1 ~ Win11 的分档判据；界面差异（如微软商店入口）按它显隐。 |
| `Services/EnvCheckItem.cs` | 系统侧体检的统一结论条目（黑屏 / 音频 / 虚拟摄像头体检共用），Status 四档 ok / info / warn / error。 |
| `Services/Tools/BandwidthAdvisorCore.cs` | 推流带宽顾问纯逻辑（V2.6）：上行 → 码率/分辨率/帧率推荐、多路推流所需上行。 |
| `Services/Tools/ConflictScannerCore.cs` | 冲突软件识别纯逻辑（V2.6）：已知干扰源表匹配进程名，输出风险与处置建议。 |
| `Services/Tools/DiskBenchmarkCore.cs` | 磁盘写入基准判定纯逻辑（V2.7）：实测吞吐 × 计划码率 → 三档结论。 |
| `Services/Tools/EncoderAdvisorCore.cs` | 编码顾问纯逻辑（V2.7）：显卡型号 + 场景 → 预设 / CQP 参数组合与双编码预算。 |
| `Services/Tools/IngestPingService.cs` | 推流节点 TCP 握手探测（V2.7）：候选清单、并发测 RTT、失败降级排序。 |
| `Services/Tools/VirtualCamCheckCore.cs` | 虚拟摄像头体检纯逻辑（V2.8，GAP-5）：DirectShow 滤镜注册项 + `win-dshow.dll` + obs64 进程 → 排查树。 |
| `Services/Tools/VirtualCamCheckService.cs` | 虚拟摄像头体检服务（V2.8，只读）：HKLM / HKCU `Classes\CLSID` 与插件文件探测。 |
| `Services/Obs/ColorCheckCore.cs` | 色彩体检纯逻辑（V2.7）：色彩范围 / 空间 / 格式三件套评估。 |
| `Services/ObsConfig/ColorCheckService.cs` | 色彩体检服务（V2.7）：配置定位链路 + 只读读取 basic.ini。 |
| `Services/Obs/PreflightCheckCore.cs` | **录前 / 开播前自检纯逻辑**（C1）：录像格式（MKV 防崩溃）、录像路径与剩余空间、是否软编、音频采样率、麦克风设备、关键帧间隔（V2.7）；只读，探测失败降级为「未通过」而非抛异常。 |
| `Services/Obs/PreflightCheckService.cs` | 录前自检服务（只读）：包装 `PreflightCheckCore`，负责 OBS 配置目录定位 → `global.ini` → 当前 Profile → `basic.ini` 的读取链路与「OBS 是否在运行」。 |
| `Services/Audio/SampleRateCheckCore.cs` | 音频采样率体检纯逻辑（V2.7）：OBS 与系统设备采样率一致性判定。 |
| `Services/Audio/SampleRateCheckService.cs` | 采样率体检服务（V2.7）：注册表 MMDevices 只读枚举活动设备共享模式采样率。 |
| `Services/Audio/AudioDeviceHealthCore.cs` | 音频设备深度体检纯逻辑（V2.8，GAP-3）：麦克风隐私权限 / 通信 Ducking / 音频服务状态 / OBS 所选设备漂移的确定性判定。 |
| `Services/Audio/AudioDeviceHealthService.cs` | 音频设备深度体检服务（V2.8，只读）：HKCU 注册表 + `sc query` + MMDevices 枚举 + websocket 快照，全程 try/catch 降级。 |
| `Services/SystemCheck/GraphicsEnvCheckCore.cs` | 黑屏专项体检纯逻辑（V2.8，GAP-2 + GAP-8）：管理员权限 → GPU 偏好 → HAGS → Game DVR / 游戏模式 → 驱动版本 → 电源与供电，三档结论 + 修复指引。 |
| `Services/SystemCheck/GraphicsEnvCheckService.cs` | 黑屏专项体检服务（V2.8，只读）：注册表 / `powercfg` / `PowerStatus` / 进程令牌探测；读取失败降级为「未知」，不写任何注册表。 |
| `Services/Update/ObsReleaseInfoService.cs` | OBS 新版本情报（V2.6）：GitHub 最新 Release 拉取 + 本地缓存回退，永不抛异常；V2.9.1 起兼作「当前稳定版 Windows 安装包直链」解析入口。 |
| `Services/Update/KnowledgeBaseUrls.cs` | **知识库 / 插件目录 raw 主通道地址常量**（V2.9.1）：仓库根下是 `NOBS/`，路径缺这段会静默 404；纯常量、可单测（形状 + 本机源码树落地校验）。 |
| `Services/Update/KnowledgeBaseUpdater.cs` | 知识库与插件目录的**双通道独立更新**：本地数据目录 > 内嵌种子 > GitHub raw 主通道 > Release 资产兜底；V2.9.3 起按语言分发地址、缓存与节流状态。 |
| `Services/Update/KbVersion.cs` | 知识库版本号比较纯逻辑（`1.4` / `1.5.2` 一类，与程序集版本完全解耦）。 |
| `Services/Update/ObsDownloadLinks.cs` | **官方 OBS 下载入口常量与白名单**（V2.9.1）：官网 / 官方 GitHub 两类地址 + `IsOfficialDownloadUrl`（https + 域名白名单）。 |
| `Services/Update/ObsInstallerAsset.cs` | **Windows 安装包直链解析纯逻辑**（V2.9.1）：从 `releases/latest` JSON 中挑 `*-Windows-x64-Installer.exe` 并校验官方域名，永不抛异常。 |
| `Services/Update/IncrementalUpdateService.cs` | 增量更新的客户端编排：下载增量包 → 逐文件 SHA-256 校验 → 交给自举进程应用；安装版（目录不可写）自动提权，便携版直接执行。 |
| `Services/Update/UpdateManifest.cs` | 增量包清单（`update_manifest.json`）反序列化模型：基准版本 / 目标版本 / 需覆盖文件 / 需删除文件，及单文件条目（相对路径 + 大小 + SHA-256）。 |
| `Services/Update/DeltaBuilder.cs` | 增量清单构建纯逻辑（无 IO）：对比新旧两版完整文件清单，算出新增 / 覆盖 / 删除集合，供构建脚本落盘。 |
| `Services/Update/FileHasher.cs` | 文件 SHA-256（64 位小写十六进制）：构建脚本比对清单与客户端校验下载共用同一实现。 |
| `Services/Update/UpdatePaths.cs` | 增量包清单路径归一化纯逻辑：正斜杠相对路径 → 本地分隔符路径，含 `..` 或为根路径即拒绝。 |
| `Services/Update/UpdaterBootstrap.cs` | 自举更新器（`OBS_Helper.exe --apply-update`）：等旧进程退出 → 用「重命名换位」替换被锁定的 exe / DLL → 写 DONE 标记 → 拉起新版本 → 清理 `pending` 与 `*.old` 残留。 |
| `Services/Update/InstallerCleanup.cs` | 安装包识别与清理策略纯逻辑：只认本应用四类产物（`Setup` / `Portable` / `Update` / `Manifest`），严格前缀 + 扩展名匹配，绝不碰其它文件。 |
| `Services/Update/InstallerCleanupService.cs` | 安装包自动清理：启动后延迟一次 + 下载完成后一次，每类保留最新一份，删除失败静默跳过并记日志。 |
| `Services/Update/FeedbackLinks.cs` | BUG 反馈入口常量与校验（V2.9.3）：表单 / Issue / 作者主页 / 仓库地址 + 「必须 https 且落在白名单域名」判定；二维码内容与按钮地址由单测钉成同一常量。 |
| `Services/FileLogger.cs` | 文件日志（跨日滚动）。 |
| `Services/TraceLoggerListener.cs` | Trace 输出接 FileLogger。 |
| `Services/ToastService.cs` | 全局轻提示（统一 Toast）。 |
| `Services/BusyService.cs` | 全局忙遮罩。 |
| `Services/Debounce.cs` | 输入防抖工具。 |
| `Services/TaskExtensions.cs` | Task 扩展（fire-and-forget 安全执行等）。 |

#### 2.3.6 Shell（托盘 / 热键 / 小窗 / 监控）

| 文件 | 职责 |
|---|---|
| `Services/Shell/TrayService.cs` | 系统托盘：菜单、图标、磁盘预警。 |
| `Services/Shell/GlobalHotkeyService.cs` | 全局热键注册与动作分发。 |
| `Services/Shell/MiniWindowService.cs` | 迷你小窗显示 / 隐藏与位置记忆。 |
| `Services/Shell/SystemMonitorService.cs` | 系统资源采样（CPU/内存/磁盘）与预警。 |
| `Services/Shell/SceneAutoSwitcher.cs` | 场景自动切换（正则匹配，带 ReDoS 超时保护）。 |
| `Services/Shell/ControlTimerService.cs` | 定时停止（录制/推流）控制。 |
| `Services/Shell/DiskProbe.cs` | 固定磁盘剩余空间枚举。 |
| `Services/Shell/LogTailerService.cs` | 实时日志尾随预警（V2.8，GAP-4，只读）：`FileShare.ReadWrite` 增量尾随最新会话日志，命中规则复用 `ObsLogAnalyzer`，节流后托盘提醒，自动跟随日志滚动。 |
| `Services/Shell/LogAlertThrottle.cs` | 实时告警节流纯逻辑：同规则码抑制窗口 + 每小时全局上限，防刷屏打爆托盘通知。 |
| `Services/Shell/RecordWatchdogCore.cs` | 录制守护判定纯逻辑（V2.8，GAP-1）：断连时正在录制 / 心跳连续失败 / 重连后发现录制丢失三类告警。 |
| `Services/Shell/RecordWatchdogService.cs` | 录制守护（V2.8，GAP-1）：DispatcherTimer 心跳轮询 + 重入护栏，出事托盘强提醒，同类只弹一次。 |
| `Services/Shell/OnboardingGuide.cs` | **新手引导纯逻辑**（V2.9）：步骤清单 + 游标状态机 + 是否展示判定，可单测。 |
| `Services/Shell/ObsLogFileFinder.cs` | **OBS 会话日志定位纯逻辑**（V2.9）：同时认 `.txt` / `.log`，按修改时间取最新（OBS 日志是 `.txt`，V2.8 只扫 `*.log` 导致实时预警从未运行）。 |

#### 2.3.7 兼容层（Win7 双目标，V2.9.3）

| 文件 | 职责 |
|---|---|
| `Compat/RequiredMembers.cs` | `required` 成员的三个特性 polyfill（net6.0 目标框架用），语义与 .NET 7+ 一致。 |
| `Services/Compat/Compat.cs` | 跨 TFM 的**语义等价**替换：`char.IsAsciiDigit` / `IsAsciiLetterOrDigit` 等价实现、`AesGcm` 新旧构造函数统一成 16 字节标签（两条分支密文完全一致）。 |
| `Services/Compat/FolderPicker.cs` | 「选择一个文件夹」的跨版本实现：net8+ 用 WPF 自带 `OpenFolderDialog`，net6.0 退到 WinForms `FolderBrowserDialog`；两条分支都返回「绝对路径 / 取消时 null」。 |

### 2.4 Controls（自定义控件）

| 文件 | 职责 |
|---|---|
| `Controls/Converters.cs` | 通用值转换器（Bool→Visibility 等）。 |
| `Controls/MetricStatusBrushConverter.cs` | 监控指标 → 语义状态色（P2 指标状态色）。 |
| `Controls/Sparkline.cs` | 迷你走势图绘制。 |
| `Controls/MarkdownView.xaml(.cs)` | Markdown 渲染控件（目录、滚动定位）。 |
| `Controls/ProblemCard.xaml(.cs)` | 问题卡片控件。 |
| `Controls/ConnectionBadge.xaml(.cs)` | 连接状态徽章。 |
| `Controls/ConfirmDialog.xaml(.cs)` | 通用确认对话框。 |
| `Controls/UpdateDialog.xaml(.cs)` | 更新提示对话框（四选一：蓝奏云/应用内/GitHub/稍后）。 |
| `Controls/ObsDownloadCard.xaml(.cs)` | **官方 OBS 下载卡**（V2.9.1）：官网下载页 / 官方 GitHub 发布页 / 当前稳定版 Windows 安装包直链；搭建页与工具箱共用同一枚控件。 |
| `Controls/RecordingEnvCard.xaml(.cs)` | **一键部署录制环境卡**（V2.9.3）：逐项列出推荐值与当前值、可单独取消、应用前整备份、可一键回滚。 |
| `Controls/SimpleRecordCard.xaml(.cs)` | **简单录像卡**（V2.9.4，挂在首页）：预设三选 → 「现在能不能录」一行结论 → 一键开始 / 停止 → 录制中时长与剩余可录 → 停止后打开目录 / 转 MP4；并提示并恢复「上次会话未回滚的改动」。 |
| `Controls/FeedbackCard.xaml(.cs)` | **帮助与反馈卡**（V2.9.4）：反馈表单 / GitHub Issue / 离线二维码 + 一键复制报错材料（版本 / 系统 / OBS 连接与场景 / 录前自检，不含路径与日志原文）+ 安全上报引导（中英双语）。 |

### 2.5 Views（页面）

| 文件 | 职责 |
|---|---|
| `Views/HomePage.xaml(.cs)` | 首页：九宫格导航 + 新手引导卡 + 连接状态。 |
| `Views/CategoryPage.xaml(.cs)` | 分类页（按分类列出问题）。 |
| `Views/ProblemPage.xaml(.cs)` | 问题详情页（步骤、收藏、进度勾选）。 |
| `Views/SearchPage.xaml(.cs)` | 搜索页（防旧结果覆盖的竞态保护）。 |
| `Views/AssistantPage.xaml(.cs)` | 助手对话页。 |
| `Views/DiagnosticPage.xaml(.cs)` | 诊断页：自检清单 + 三通道诊断 + 报告。 |
| `Views/LogsPage.xaml(.cs)` | 日志查看页（8MB 截尾读取）。 |
| `Views/ConsolePage.xaml(.cs)` | 控制台页：连接、场景/来源管理、音频、推流控制。 |
| `Views/PerformancePage.xaml(.cs)` | 监控页（CPU/内存/磁盘 + 走势图 + 状态色）。 |
| `Views/ObsConfigPage.xaml(.cs)` | OBS 配置管理页（备份/恢复/重置/定位）。 |
| `Views/TemplatePage.xaml(.cs)` | 场景模板页（在线落地 / 离线导出）。 |
| `Views/SettingsPage.xaml(.cs)` | 设置页（AI、连接、热键、外观、**新手引导重播**、更新等全部设置）。 |
| `Views/SetupPage.xaml(.cs)` | 新手搭建流程六步引导 + 竖屏 / 多平台进阶向导（V2.2）。 |
| `Views/PluginsPage.xaml(.cs)` | 插件广场：目录热更新 + 目录版本/复核日期 + 维护状态标注（V2.9）+ 本机体检 + 下载角标 + AI 预算提示 + 关注（V2.2）。 |
| `Views/ToolboxPage.xaml(.cs)` | 工具箱（V2.6）：录像工具 / 参数处方 / 隐私清单 / 冲突扫描 / 带宽计算 / 版本情报 / 快捷键速查。 |
| `Views/SetupWizardWindow.xaml(.cs)` | 分步向导窗口（竖屏双画布 / 多平台推流，V2.2）。 |
| `Views/GuidePage.xaml(.cs)` | 使用指引（随包资源）。 |
| `Views/FeedbackPage.xaml(.cs)` | **帮助与反馈页**（V2.9.4，一级导航）：反馈卡（表单 / Issue / 离线二维码）+ **一键复制报错材料**（版本 / 系统 / OBS 连接与场景 / 录前自检结论，不含路径与日志原文）+ 作者与仓库入口。 |
| `Views/MiniControlWindow.xaml(.cs)` | 迷你小窗（精简控制）。 |

### 2.6 Plugins（插件生态服务，V2.2）

| 文件 | 职责 |
|---|---|
| `Services/Plugins/PluginCatalog.cs` | 目录模型与纯逻辑：JSON 解析、DLL 名匹配、分类分组、repo 归一化、维护状态（`maintain` / `maintainNote`）判定。 |
| `Services/Plugins/PluginCatalogService.cs` | 目录数据访问（外部覆盖文件优先，内嵌种子兜底，可热重载）。 |
| `Services/Plugins/PluginScannerCore.cs` | 本机插件扫描纯逻辑（枚举 DLL + 版本信息，只读）+ 候选目录合并 + **OBS 32.x 一插件一目录布局路径推导**（V2.9）。 |
| `Services/Plugins/LocalPluginScanner.cs` | OBS 安装目录候选定位与体检入口（复用 ObsPathService 探测）。 |
| `Services/Plugins/PluginReleaseService.cs` | GitHub Releases 最新版本查询（内存 + 磁盘双层缓存、在途合并、失败静默）。 |
| `Services/Plugins/PluginWatchService.cs` | 关注插件启动静默查新（24h 节流，仅 Toast）。 |

### 2.7 导航 / 错误码

| 文件 | 职责 |
|---|---|
| `Navigation/Routes.cs` | 应用内路由名常量表（**17 条**路由；纯常量、零 WPF 依赖，V2.9.1 起独立成文件，供单测校验引导跳转目标）。 |
| `Navigation/NavigationService.cs` | 页面导航服务（路由 → 页面实例、参数传递、缓存策略）。 |
| `Errors/ErrorCodes.cs` | 统一错误码表与用户可读说明（含解决建议）。 |

### 2.8 主题 / 资源

| 文件 | 职责 |
|---|---|
| `Themes/Palette.xaml` | 配色（深浅两套语义色板）。 |
| `Themes/Controls.xaml` | 通用控件样式（按钮/卡片/输入框等）。 |
| `Themes/Icons.xaml` | 品牌 SVG 矢量图标（DrawingImage）。 |
| `Assets/appicon.ico` | 应用图标（7 档尺寸 16/24/32/48/64/128/256）。V2.9.6 起与仓库图标（`assets/icon.svg`）统一，由 `scripts/gen_appicon.py` 生成；小尺寸帧是单独绘制的简化版。 |
| `Assets/problems.json` | 离线知识库（问题库，独立热更新；当前 **212 条**、知识库版本 2.2、10 个分类）。 |
| `Assets/plugins.json` | 插件广场目录（V2.2 外置数据，独立热更新）。 |
| `Assets/scene_templates.json` | 场景模板数据（含推荐插件依赖标注）。 |
| `Assets/troubleshooting.md` | 疑难排查 Markdown 指引。 |
| `Assets/problems.en-US.json` / `plugins.en-US.json` / `scene_templates.en-US.json` / `troubleshooting.en-US.md` | 上列四份内容资产的**英文并列文件**（V2.9.3）；中文文件名刻意不变（raw 热更新地址、Release 资产名与本机缓存都依赖它）。 |
| `Assets/feedback_qr.png` | 反馈表单二维码（随包内嵌、离线可扫）；内容与按钮地址由单测钉成同一常量。 |
| `Assets/free_ai_key.json` | 内置免费 AI 密钥（构建期注入，不入库，见 .gitignore）。 |

### 2.9 项目配置 / 打包

| 文件 | 职责 |
|---|---|
| `OBS_Helper.Wpf.csproj` | 项目文件（双 TFM `net10.0-windows` / `net6.0-windows`，无 PackageReference，纯自包含）。 |
| `OBS_Helper_Setup.iss` | Inno Setup 安装脚本：`[Languages]` 中文（默认，`LanguageDetectionMethod=none` + 显式弹选择页）+ English，向导文字 / 快捷方式 / 卸载项走 `[CustomMessages]`，选定语言写 `{app}\language.ini` 供应用首启读取；文件按 UTF-8 带 BOM 保存。 |
| `installer/ChineseSimplified.isl` | 随仓库固定的 Inno Setup 简体中文语言文件（不依赖本机 Inno 安装目录，CI 才能编出安装包）。 |
| `OBS_Helper.slnx` | 解决方案（单项目）。 |

### 2.10 国际化（V2.9.2 / V2.9.3）

| 文件 | 职责 |
|---|---|
| `Localization/Strings.cs` | 文案表入口（纯 BCL、零 WPF）：当前语言、`T(key)` / `T(key, args)`、键集、语言标识归一化、缺键回退次序。 |
| `Localization/StringTableZhHans.cs` | 简体中文文案（默认语言，**2239 条键**，按模块分段）。 |
| `Localization/StringTableEnUs.cs` | English 文案（**2239 条键**，键集必须与中文表完全一致，有单测钉死）。 |
| `Localization/ContentAssets.cs` | 随包离线内容的**按语言取资产**统一入口（V2.9.3）：外部缓存 → 当前语言内嵌 → 中文内嵌（最后一级兜底必写 WARN，否则英文用户会「安静地看到中文内容」）。 |
| `Localization/DataValues.cs` | 数据驱动展示值的**跨语言**判定（severity / level / badge：中文「常见」与英文 `Common` 都认）。 |
| `Services/LocalizationService.cs` | 语言服务：读 / 写偏好（`prefs.json` 的 `obshelper.language`）、解析首启语言（用户选择 > 安装向导 `language.ini` > 中文）、把整表写进 `Application.Resources`、广播 `Changed`。 |
| `Views/SettingsPage`（语言段） | 「设置 → 语言」切换条：选项由 `Strings.Supported` 生成，切换后主窗口把当前页面原地重放一次刷新。 |

## 3. 脚本 / 工具（scripts/）

| 文件 | 职责 |
|---|---|
| `scripts/add_problems.py` | 向 `problems.json` 追加问题条目（可复用改数据）。 |
| `scripts/add_templates.py` | 向 `scene_templates.json` 追加模板。 |
| `scripts/check_resources.py` | XAML 资源引用体检：`Themes/*.xaml` 的主题键 **+ `Localization/StringTable*.cs` 的文案键**（映射成 `Loc.<键>`），校验每个 `{Static\|DynamicResource}` 引用都有定义；另含 `problems.json` 分类语义色白名单校验。 |
| `scripts/check_docs.py` | **文档数字一致性巡检**：从源码实测服务数 / 路由数 / 自检项数 / 文案键数 / 知识库条数 / 测试项数 / 版本号，与文档里写的数字比对，不一致即非零退出并打印「文档位置 + 文档值 + 实测值」。 |
| `scripts/gen_appicon.py` | 生成应用图标 `Assets/appicon.ico`（V2.9.6 重写）：48px 以上取自 `assets/icon.png`（仓库图标渲染图，补回透明圆角），16/24/32px 按同一套几何另画简化版，避免托盘尺寸糊成一团。只依赖 Pillow；旧版实现依赖本机参考图，已替换。 |
| `scripts/embed_free_ai_key.ps1` | 构建期注入免费 AI 密钥。 |
| `scripts/verify_delta.py` | 发布前校验增量包（清单完整性、可升级性），结果抄进发布说明。 |
| `build.ps1` | 出包脚本：publish R2R → Inno Setup → 便携 zip，产物进 `PAKE/windows/`（gitignore）。 |

## 4. 文档（docs/）

| 文件 | 职责 |
|---|---|
| `docs/ARCHITECTURE.md` | 架构总览（本仓库）。 |
| `docs/CODEBASE.md` | 本文件：代码清单。 |
| `docs/API免费实现方案.md` | 免费 AI 通道的实现方案设计稿。 |
| `docs/PLUGIN_AUDIT_2026-09.md` | **插件广场目录 v1.4 全量复核报告**（V2.9）：逐条实测数据、收录口径、剔除 / 新增 / 维护放缓清单。 |
| `docs/QA-REPORT.md` | **审查报告索引**：本仓各版本发布审查 / 优化评估报告的入口表，并说明这些报告的结论都有保质期（带日期的结论以当时版本为准）。 |
| `docs/OPTIMIZATION_PROPOSAL_2026-10.md` | **2026-10 优化与新增功能清单**（V2.9.6 基线）：A~F 六类问题的证据、影响、改法与体积。 |
| `docs/DEV_GUIDE_GAPS_2026-08.md` | V2.8 的缺口开发指引（GAP-1~8）——**2026-08 快照**，其中的知识库条数 / 测试项数已过期。 |
| `docs/OPTIMIZATION_V2.7.0.md` | V2.7.0 的实施依据与验收标准——**2026-08-24 快照**，其中的问题库条数 / 测试项数已过期。 |
| `docs/FEATURES_USERVIEW_2026-10-05.md` | V2.9.4「简单录像」的用户视角现状盘点与优先级——**2026-10-05 快照**，结论部分已被 V2.9.4 实现推翻。 |
| `docs/SIMPLE_RECORDING_DESIGN.md` | V2.9.4 详细设计（施工图）；§0.1 集中记录了设计稿与最终实现的差异，**以 §0.1 为准**。 |
| `docs/reviews/REVIEW_2026-08-08*.md` | 各版本发布审查报告（v1.7.0 / v1.7.1 / v1.8.0 / v1.8.1）。 |
| `docs/reviews/REVIEW_2026-09-27-v2.9.2.md` | **V2.9.2 发布审查报告（国际化）**：三轮审校的发现与修复、刻意保留中文的边界、全量验证结果。 |
| `docs/reviews/REVIEW_2026-10-05-v2.9.3.md` | **V2.9.3 发布审查报告**：数据安全与出站 / 双目标与构建边界 / 交互与文案一致性三视角，含证据边界标注。 |
| `docs/reviews/REVIEW_2026-10-05-v2.9.4.md` | **V2.9.4 发布审查报告**（S1~S7）：其中 S1（写 OBS 配置的路径护栏）在 2026-10 优化清单里升级为 A3。 |
| `docs/I18N_EN_CONTENT_PLAN.md` | **V2.9.3 计划：随包离线内容（知识库 / 模板 / 插件 / 指引）的英译**——字段约定（哪些是逻辑键不可译）、热更新通道改造、验收口径与风险点。 |
| `RELEASE_NOTES_v2.9.6.md` | 本版发布说明（更名「OBS帮助助手」+ 图标统一 + 文档同步；含改名时**刻意不动**的四件事与验证证据）。 |
| `RELEASE_NOTES_v2.9.2.md` | 发布说明（国际化）。 |
| `RELEASE_NOTES_v2.9.3.md` / `RELEASE_NOTES_v2.9.4.md` | 发布说明（随包内容英译 + 一键部署录制环境 / 简单录像）。 |

## 5. 快速定位索引

按「想做什么」找文件：

- **改连接 / 重连** → `ObsConnectionService.cs`、`ObsWebSocketClient.cs`、`ObsReconnectPolicy.cs`
- **改诊断逻辑** → `DiagnosticOrchestrator.cs` + `Services/Ai/*Engine.cs` + `ObsToolRegistry.cs`
- **改知识库数据** → `Assets/problems.json` + `scripts/add_problems.py`
- **改备份 / 恢复 / 重置** → `Services/ObsConfig/`（Backup / FileTx / Reset / SafePath）
- **改场景模板** → `Services/ObsConfig/SceneTemplateService.cs` + `Assets/scene_templates.json`
- **改日志分析 / 脱敏** → `ObsLogAnalyzer.cs`、`LogSanitizer.cs`
- **改更新逻辑** → `UpdateService.cs`、`Services/Update/`、`Controls/UpdateDialog.xaml(.cs)`
- **改知识库热更新（raw 通道地址 / 兜底 / 按语言分发）** → `Services/Update/KnowledgeBaseUrls.cs`、`KnowledgeBaseUpdater.cs`、`Localization/ContentAssets.cs`
- **改官方 OBS 下载入口** → `Services/Update/ObsDownloadLinks.cs`（地址与白名单）、`ObsInstallerAsset.cs`（直链解析）、`Controls/ObsDownloadCard.xaml(.cs)`（卡片外观）
- **改托盘 / 热键 / 小窗 / 监控** → `Services/Shell/`
- **改主题 / 样式 / 字号 / 无障碍** → `Themes/`（Palette / Controls / Icons）+ `AppearanceService.cs`
- **改页面 UI** → `Views/` + `Controls/`
- **改新手引导（步骤文案 / 顺序 / 跳转目标）** → `Services/Shell/OnboardingGuide.cs`（文案、游标、每步对应路由与跳转按钮等纯逻辑）+ `MainWindow.xaml`（覆盖层外观）+ `MainWindow.xaml.cs`（自动跳转与按钮渲染）；设置页入口在 `Views/SettingsPage.xaml(.cs)`
- **改插件广场数据（收录 / 维护状态）** → `Assets/plugins.json` + `docs/PLUGIN_AUDIT_2026-09.md`（复核报告），模型在 `Services/Plugins/PluginCatalog.cs`
- **改文档里的数字** → 先看 `scripts/check_docs.py` 校验的是哪几处，改完必须跑一遍
