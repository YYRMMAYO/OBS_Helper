# OBS 排障助手 · 项目库代码清单（CODEBASE）

> 本文件是 `OBS_Helper.Wpf`（Windows 原生 WPF 版）的完整代码清单：
> 按模块列出每个源码 / 资源文件及其职责，方便快速定位代码。
> 架构总览见 [`docs/ARCHITECTURE.md`](ARCHITECTURE.md)。
> 行数为编写时近似值，仅作体量参考。

## 1. 顶层结构

```
NOBS/
├── OBS_Helper.Wpf/          # 主程序（WPF，net10.0-windows）
├── OBS_Helper.slnx          # 解决方案（单项目）
├── build.ps1                # 出包脚本：dotnet publish R2R + Inno Setup + 便携 zip
├── docs/                    # 架构 / 代码清单 / 设计文档
│   └── reviews/             # 版本发布审查报告
├── scripts/                 # 数据与资产生成脚本（Python / PowerShell）
├── .gitignore
├── README.md
└── LICENSE
```

## 2. 源码文件清单（按模块）

### 2.1 入口与组合根

| 文件 | 行数 | 职责 |
|---|---|---|
| `App.xaml` | - | 应用资源字典装配（主题、图标、控件样式）。 |
| `App.xaml.cs` | - | 启动入口：单实例 Mutex、异常三挂钩（Dispatcher/AppDomain/Unobserved）、启动服务装配、`HeadlessTest` 自检模式、新手引导重播事件。 |
| `AppServices.cs` | 125 | 组合根：20 个服务的手工 Lazy 单例装配（刻意不用 DI 容器）。 |
| `MainWindow.xaml` / `.xaml.cs` | - | 主窗口：导航框架、页面路由表、页面过渡动画、托盘联动、**新手引导覆盖层**（首启分步引导 + 设置页即时重播；V2.9.1 起每步自动跳转到对应页面并渲染站内/站外跳转按钮）。 |
| `app.manifest` | - | Windows 清单（DPI 感知、执行级别）。 |

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

| 文件 | 行数 | 职责 |
|---|---|---|
| `Services/Obs/ObsWebSocketClient.cs` | 351 | obs-websocket 底层客户端：连接、鉴权、请求/响应、事件分发、重连。 |
| `Services/Obs/ObsConnectionService.cs` | 538 | 连接生命周期编排：自动连接、状态机、重连调度、事件订阅管理。 |
| `Services/Obs/ObsAuth.cs` | 32 | 基于 salt/challenge 的鉴权字符串计算。 |
| `Services/Obs/ObsReconnectPolicy.cs` | 30 | 重连退避策略。 |
| `Services/Obs/ObsSettingsService.cs` | 145 | 连接参数（地址/端口/密码）持久化与读取。 |
| `Models/Obs/ObsProtocol.cs` | 171 | 协议消息模型（见 Models）。 |

#### 2.3.2 诊断引擎（AI 三通道）

| 文件 | 行数 | 职责 |
|---|---|---|
| `Services/Ai/DiagnosticOrchestrator.cs` | 144 | 诊断编排：按可用性选择云端 / 免费 / 本地引擎，汇总诊断项。 |
| `Services/Ai/CloudDiagnosticEngine.cs` | 251 | 云端大模型通道（API Key + function-calling 工具调用）。 |
| `Services/Ai/FreeDiagnosticEngine.cs` | 123 | 免费内置 AI 通道（智谱内置密钥 + Pollinations 免 Key）。 |
| `Services/Ai/LocalDiagnosticEngine.cs` | 159 | 本地搜索助手（知识库检索，无网络）。 |
| `Services/Ai/ObsToolRegistry.cs` | 205 | 供云端大模型调用的诊断工具注册表（get_problem_detail 等）。 |
| `Services/Ai/DiagnosticTypes.cs` | 87 | 诊断项 / 报告模型。 |
| `Services/Ai/DiagnosticSeverity.cs` | 11 | 严重程度枚举。 |
| `Services/Ai/DiagnosticSeverityMapper.cs` | 25 | 日志严重度 ↔ 知识库文案映射（纯函数）。 |
| `Services/Ai/FreeAiKeyProvider.cs` | 151 | 内置免费密钥的解密读取（构建期由脚本注入）。 |
| `Services/Ai/FreeRateLimiter.cs` | 132 | 免费通道本地限额（按通道独立限频，持久化 prefs.json）。 |
| `Services/Ai/AiSettingsService.cs` | 226 | AI 设置（模式选择、API Key、模型白名单）持久化。 |

#### 2.3.3 OBS 配置管理（备份 / 恢复 / 重置 / 模板）

| 文件 | 行数 | 职责 |
|---|---|---|
| `Services/ObsConfig/ObsPathService.cs` | 271 | OBS 配置目录定位（注册表 + 常见路径探测）。 |
| `Services/ObsConfig/ObsSafePath.cs` | 155 | 路径安全护栏：防目录穿越，越界抛 `SafePathException`。 |
| `Services/ObsConfig/ObsBackupService.cs` | 736 | 配置备份（zip 打包）与导入（校验、事务恢复）。 |
| `Services/ObsConfig/ObsResetService.cs` | 253 | 配置重置（连接态校验、场景清空）。 |
| `Services/ObsConfig/SceneTemplateService.cs` | 824 | 场景模板：在线落地（obs-websocket 建集合/场景/来源）与离线导出（JSON）。 |
| `Services/ObsConfig/RecordingToolsService.cs` | 220 | 录像工具（V2.6）：录像目录解析与直达、ffmpeg 探测、MKV/Hybrid MP4 → MP4 无损转封装。 |
| `Services/ObsConfig/FileTx.cs` | 129 | 文件事务：提交 / 回滚目录级操作。 |

#### 2.3.4 日志分析

| 文件 | 行数 | 职责 |
|---|---|---|
| `Services/Obs/ObsLogAnalyzer.cs` | 451 | OBS 日志解析：错误/警告/严重度分类、统计与报告。 |
| `Services/Obs/LogSanitizer.cs` | 183 | 日志脱敏：抹掉密钥/敏感串，同时保留 OBS 正常长串。 |

#### 2.3.5 宿主 / 存储 / 基础设施

| 文件 | 行数 | 职责 |
|---|---|---|
| `Services/Host/HostBridge.cs` | 776 | 宿主能力：机密存储（DPAPI+AES-GCM 双层）、日志目录访问、SSRF 防护、环境信息。 |
| `Services/Host/LocalStore.cs` | 136 | 本地键值存储（prefs.json / secrets.dat 的底层读写）。 |
| `Services/Markdown/MarkdownRenderer.cs` | 176 | Markdown → 富文本渲染（安全链接白名单）。 |
| `Services/ProblemService.cs` | 147 | 知识库（problems.json）加载与检索。 |
| `Services/AssistantService.cs` | 95 | 助手对话会话管理。 |
| `Services/BookmarkService.cs` | 150 | 收藏（书签）持久化与变更通知。 |
| `Services/AppearanceService.cs` | 290 | 主题（深浅色）切换与持久化。 |
| `Services/UpdateService.cs` | 406 | 更新检查（蓝奏云 + GitHub Release 双通道）、下载与安装。 |
| `Services/Tools/BandwidthAdvisorCore.cs` | 120 | 推流带宽顾问纯逻辑（V2.6）：上行 → 码率/分辨率/帧率推荐、多路推流所需上行。 |
| `Services/Tools/ConflictScannerCore.cs` | 110 | 冲突软件识别纯逻辑（V2.6）：已知干扰源表匹配进程名，输出风险与处置建议。 |
| `Services/Tools/DiskBenchmarkCore.cs` | - | 磁盘写入基准判定纯逻辑（V2.7）：实测吞吐 × 计划码率 → 三档结论。 |
| `Services/Tools/EncoderAdvisorCore.cs` | - | 编码顾问纯逻辑（V2.7）：显卡型号 + 场景 → 预设 / CQP 参数组合与双编码预算。 |
| `Services/Tools/IngestPingService.cs` | - | 推流节点 TCP 握手探测（V2.7）：候选清单、并发测 RTT、失败降级排序。 |
| `Services/Obs/ColorCheckCore.cs` | - | 色彩体检纯逻辑（V2.7）：色彩范围 / 空间 / 格式三件套评估。 |
| `Services/ObsConfig/ColorCheckService.cs` | - | 色彩体检服务（V2.7）：配置定位链路 + 只读读取 basic.ini。 |
| `Services/Audio/SampleRateCheckCore.cs` | - | 音频采样率体检纯逻辑（V2.7）：OBS 与系统设备采样率一致性判定。 |
| `Services/Audio/SampleRateCheckService.cs` | - | 采样率体检服务（V2.7）：注册表 MMDevices 只读枚举活动设备共享模式采样率。 |
| `Services/Update/ObsReleaseInfoService.cs` | 180 | OBS 新版本情报（V2.6）：GitHub 最新 Release 拉取 + 本地缓存回退，永不抛异常；V2.9.1 起兼作「当前稳定版 Windows 安装包直链」解析入口。 |
| `Services/Update/KnowledgeBaseUrls.cs` | - | **知识库 / 插件目录 raw 主通道地址常量**（V2.9.1）：仓库根下是 `NOBS/`，路径缺这段会静默 404；纯常量、可单测（形状 + 本机源码树落地校验）。 |
| `Services/Update/ObsDownloadLinks.cs` | - | **官方 OBS 下载入口常量与白名单**（V2.9.1）：官网 / 官方 GitHub 两类地址 + `IsOfficialDownloadUrl`（https + 域名白名单）。 |
| `Services/Update/ObsInstallerAsset.cs` | - | **Windows 安装包直链解析纯逻辑**（V2.9.1）：从 `releases/latest` JSON 中挑 `*-Windows-x64-Installer.exe` 并校验官方域名，永不抛异常。 |
| `Services/FileLogger.cs` | 104 | 文件日志（跨日滚动）。 |
| `Services/TraceLoggerListener.cs` | 23 | Trace 输出接 FileLogger。 |
| `Services/ToastService.cs` | 80 | 全局轻提示（统一 Toast）。 |
| `Services/BusyService.cs` | 45 | 全局忙遮罩。 |
| `Services/Debounce.cs` | 82 | 输入防抖工具。 |
| `Services/TaskExtensions.cs` | 33 | Task 扩展（fire-and-forget 安全执行等）。 |

#### 2.3.6 Shell（托盘 / 热键 / 小窗 / 监控）

| 文件 | 行数 | 职责 |
|---|---|---|
| `Services/Shell/TrayService.cs` | 333 | 系统托盘：菜单、图标、磁盘预警。 |
| `Services/Shell/GlobalHotkeyService.cs` | 274 | 全局热键注册与动作分发。 |
| `Services/Shell/MiniWindowService.cs` | 132 | 迷你小窗显示 / 隐藏与位置记忆。 |
| `Services/Shell/SystemMonitorService.cs` | 213 | 系统资源采样（CPU/内存/磁盘）与预警。 |
| `Services/Shell/SceneAutoSwitcher.cs` | 185 | 场景自动切换（正则匹配，带 ReDoS 超时保护）。 |
| `Services/Shell/ControlTimerService.cs` | 148 | 定时停止（录制/推流）控制。 |
| `Services/Shell/DiskProbe.cs` | 42 | 固定磁盘剩余空间枚举。 |
| `Services/Shell/OnboardingGuide.cs` | - | **新手引导纯逻辑**（V2.9）：步骤清单 + 游标状态机 + 是否展示判定，可单测。 |
| `Services/Shell/ObsLogFileFinder.cs` | - | **OBS 会话日志定位纯逻辑**（V2.9）：同时认 `.txt` / `.log`，按修改时间取最新（OBS 日志是 `.txt`，V2.8 只扫 `*.log` 导致实时预警从未运行）。 |

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

### 2.5 Views（页面）

| 文件 | 行数 | 职责 |
|---|---|---|
| `Views/HomePage.xaml(.cs)` | 140 | 首页：九宫格导航 + 新手引导卡 + 连接状态。 |
| `Views/CategoryPage.xaml(.cs)` | 84 | 分类页（按分类列出问题）。 |
| `Views/ProblemPage.xaml(.cs)` | 457 | 问题详情页（步骤、收藏、进度勾选）。 |
| `Views/SearchPage.xaml(.cs)` | 167 | 搜索页（防旧结果覆盖的竞态保护）。 |
| `Views/AssistantPage.xaml(.cs)` | 158 | 助手对话页。 |
| `Views/DiagnosticPage.xaml(.cs)` | 406 | 诊断页：自检清单 + 三通道诊断 + 报告。 |
| `Views/LogsPage.xaml(.cs)` | 371 | 日志查看页（8MB 截尾读取）。 |
| `Views/ConsolePage.xaml(.cs)` | 754 | 控制台页：连接、场景/来源管理、音频、推流控制。 |
| `Views/PerformancePage.xaml(.cs)` | 210 | 监控页（CPU/内存/磁盘 + 走势图 + 状态色）。 |
| `Views/ObsConfigPage.xaml(.cs)` | 412 | OBS 配置管理页（备份/恢复/重置/定位）。 |
| `Views/TemplatePage.xaml(.cs)` | 343 | 场景模板页（在线落地 / 离线导出）。 |
| `Views/SettingsPage.xaml(.cs)` | 786 | 设置页（AI、连接、热键、外观、**新手引导重播**、更新等全部设置）。 |
| `Views/SetupPage.xaml(.cs)` | 220 | 新手搭建流程六步引导 + 竖屏 / 多平台进阶向导（V2.2）。 |
| `Views/PluginsPage.xaml(.cs)` | - | 插件广场：目录热更新 + 目录版本/复核日期 + 维护状态标注（V2.9）+ 本机体检 + 下载角标 + AI 预算提示 + 关注（V2.2）。 |
| `Views/ToolboxPage.xaml(.cs)` | 330 | 工具箱（V2.6）：录像工具 / 参数处方 / 隐私清单 / 冲突扫描 / 带宽计算 / 版本情报 / 快捷键速查。 |
| `Views/SetupWizardWindow.xaml(.cs)` | 250 | 分步向导窗口（竖屏双画布 / 多平台推流，V2.2）。 |
| `Views/GuidePage.xaml(.cs)` | 95 | 使用指引（随包资源）。 |
| `Views/MiniControlWindow.xaml(.cs)` | 106 | 迷你小窗（精简控制）。 |

### 2.6 Plugins（插件生态服务，V2.2）

| 文件 | 职责 |
|---|---|
| `Services/Plugins/PluginCatalog.cs` | 目录模型与纯逻辑：JSON 解析、DLL 名匹配、分类分组、repo 归一化、维护状态（`maintain` / `maintainNote`）判定。 |
| `Services/Plugins/PluginCatalogService.cs` | 目录数据访问（外部覆盖文件优先，内嵌种子兜底，可热重载）。 |
| `Services/Plugins/PluginScannerCore.cs` | 本机插件扫描纯逻辑（枚举 DLL + 版本信息，只读）+ 候选目录合并 + **OBS 32.x 一插件一目录布局路径推导**（V2.9）。 |
| `Services/Plugins/LocalPluginScanner.cs` | OBS 安装目录候选定位与体检入口（复用 ObsPathService 探测）。 |
| `Services/Plugins/PluginReleaseService.cs` | GitHub Releases 最新版本查询（内存 + 磁盘双层缓存、在途合并、失败静默）。 |
| `Services/Plugins/PluginWatchService.cs` | 关注插件启动静默查新（24h 节流，仅 Toast）。 |

### 2.6 导航 / 错误码

| 文件 | 职责 |
|---|---|
| `Navigation/Routes.cs` | 应用内路由名常量表（纯常量、零 WPF 依赖，V2.9.1 起独立成文件，供单测校验引导跳转目标）。 |
| `Navigation/NavigationService.cs` | 页面导航服务（路由 → 页面实例、参数传递、缓存策略）。 |
| `Errors/ErrorCodes.cs` | 统一错误码表与用户可读说明（含解决建议）。 |

### 2.7 主题 / 资源

| 文件 | 职责 |
|---|---|
| `Themes/Palette.xaml` | 配色（深浅两套语义色板）。 |
| `Themes/Controls.xaml` | 通用控件样式（按钮/卡片/输入框等）。 |
| `Themes/Icons.xaml` | 品牌 SVG 矢量图标（DrawingImage）。 |
| `Assets/appicon.ico` | 应用图标。 |
| `Assets/problems.json` | 离线知识库（问题库，独立热更新 v1.6）。 |
| `Assets/plugins.json` | 插件广场目录（V2.2 外置数据，独立热更新）。 |
| `Assets/scene_templates.json` | 场景模板数据（含推荐插件依赖标注）。 |
| `Assets/troubleshooting.md` | 疑难排查 Markdown 指引。 |
| `Assets/free_ai_key.json` | 内置免费 AI 密钥（构建期注入，不入库，见 .gitignore）。 |

### 2.8 项目配置 / 打包

| 文件 | 职责 |
|---|---|
| `OBS_Helper.Wpf.csproj` | 项目文件（net10.0-windows，无 PackageReference，纯自包含）。 |
| `OBS_Helper_Setup.iss` | Inno Setup 安装脚本。 |
| `OBS_Helper.slnx` | 解决方案（单项目）。 |

## 3. 脚本 / 工具（scripts/）

| 文件 | 职责 |
|---|---|
| `scripts/add_problems.py` | 向 `problems.json` 追加问题条目（可复用改数据）。 |
| `scripts/add_templates.py` | 向 `scene_templates.json` 追加模板。 |
| `scripts/check_resources.py` | 扫描 `Themes/*.xaml` 资源一致性校验。 |
| `scripts/gen_appicon.py` | 生成应用图标。 |
| `scripts/embed_free_ai_key.ps1` | 构建期注入免费 AI 密钥。 |
| `build.ps1` | 出包脚本：publish R2R → Inno Setup → 便携 zip，产物进 `PAKE/windows/`（gitignore）。 |

## 4. 文档（docs/）

| 文件 | 职责 |
|---|---|
| `docs/ARCHITECTURE.md` | 架构总览（本仓库）。 |
| `docs/CODEBASE.md` | 本文件：代码清单。 |
| `docs/API免费实现方案.md` | 免费 AI 通道的实现方案设计稿。 |
| `docs/PLUGIN_AUDIT_2026-09.md` | **插件广场目录 v1.4 全量复核报告**（V2.9）：逐条实测数据、收录口径、剔除 / 新增 / 维护放缓清单。 |
| `docs/reviews/REVIEW_2026-08-08*.md` | 各版本发布审查报告（v1.7.0 / v1.7.1 / v1.8.0 / v1.8.1）。 |

## 5. 快速定位索引

按「想做什么」找文件：

- **改连接 / 重连** → `ObsConnectionService.cs`、`ObsWebSocketClient.cs`、`ObsReconnectPolicy.cs`
- **改诊断逻辑** → `DiagnosticOrchestrator.cs` + `Services/Ai/*Engine.cs` + `ObsToolRegistry.cs`
- **改知识库数据** → `Assets/problems.json` + `scripts/add_problems.py`
- **改备份 / 恢复 / 重置** → `Services/ObsConfig/`（Backup / FileTx / Reset / SafePath）
- **改场景模板** → `Services/ObsConfig/SceneTemplateService.cs` + `Assets/scene_templates.json`
- **改日志分析 / 脱敏** → `ObsLogAnalyzer.cs`、`LogSanitizer.cs`
- **改更新逻辑** → `UpdateService.cs`、`Controls/UpdateDialog.xaml(.cs)`
- **改知识库热更新（raw 通道地址 / 兜底）** → `Services/Update/KnowledgeBaseUrls.cs`（地址常量 + 单测）、`KnowledgeBaseUpdater.cs`（双通道逻辑）
- **改官方 OBS 下载入口** → `Services/Update/ObsDownloadLinks.cs`（地址与白名单）、`ObsInstallerAsset.cs`（直链解析）、`Controls/ObsDownloadCard.xaml(.cs)`（卡片外观）
- **改托盘 / 热键 / 小窗 / 监控** → `Services/Shell/`
- **改主题 / 样式** → `Themes/`（Palette / Controls / Icons）+ `AppearanceService.cs`
- **改页面 UI** → `Views/` + `Controls/`
- **改新手引导（步骤文案 / 顺序 / 跳转目标）** → `Services/Shell/OnboardingGuide.cs`（文案、游标、每步对应路由与跳转按钮等纯逻辑）+ `MainWindow.xaml`（覆盖层外观）+ `MainWindow.xaml.cs`（自动跳转与按钮渲染）；设置页入口在 `Views/SettingsPage.xaml(.cs)`
- **改插件广场数据（收录 / 维护状态）** → `Assets/plugins.json` + `docs/PLUGIN_AUDIT_2026-09.md`（复核报告），模型在 `Services/Plugins/PluginCatalog.cs`
