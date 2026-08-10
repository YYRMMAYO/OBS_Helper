# OBS 排障助手 · 架构文档（ARCHITECTURE）

> 本文描述 `OBS_Helper.Wpf`（Windows 原生 WPF 版）的整体架构。
> 代码清单见 [`docs/CODEBASE.md`](CODEBASE.md)，单文件职责不在此重复。

## 1. 定位与技术栈

- **定位**：OBS 直播排障桌面助手。连接本机 OBS（obs-websocket），提供问题知识库检索、日志分析、AI 三通道诊断、场景模板落地、配置备份/恢复/重置、系统监控、全局热键、迷你小窗等能力。
- **技术栈**：.NET 10（`net10.0-windows`）、WPF、C# 原生；**零第三方包**（纯 BCL，含 `System.Text.Json`、`System.Net.WebSockets` 等）。
- **形态**：单项目单进程；单实例（会话级 Mutex）；构建产物为自包含单文件（R2R）+ 安装包 / 便携 zip。

## 2. 分层总览

```
┌──────────────────────────── Views / Controls（XAML 界面层）────────────────────────────┐
│  Views/*Page.xaml(.cs)   Controls/*（自定义控件）   Themes/*（Palette/Controls/Icons） │
└──────────────┬───────────────────────────────────────────────▲────────────────────────┘
               │ 依赖服务（页面构造时从 AppServices 取用）        │ 事件回调（StateChanged 等）
┌──────────────▼───────────────────────────────────────────────┴────────────────────────┐
│                              Services（业务逻辑层）                                    │
│  连接 ObsConnectionService / ObsWebSocketClient    诊断 DiagnosticOrchestrator + 3 引擎 │
│  配置 ObsConfig/*（Path/SafePath/Backup/FileTx/Reset/Template）                         │
│  日志 ObsLogAnalyzer / LogSanitizer   更新 UpdateService   后台 Shell/*                 │
│  基础设施 LocalStore / FileLogger / Debounce / Toast / Busy                            │
└──────────────┬───────────────────────────────────────────────▲────────────────────────┘
               │ 依赖注入（构造函数手工装配）                    │
┌──────────────▼───────────────────────────────────────────────┴────────────────────────┐
│                     Models（纯数据模型） + Errors（错误码表）                            │
│  Obs/ObsConfig/Shell 各子命名空间                                                    │
└───────────────────────────────────────────────────────────────────────────────────────┘
```

依赖方向严格单向：**Views → Services → Models**。页面不直接持有 `Models` 之外的跨层引用；`Services` 内部允许互相依赖（组合根统一装配）。

## 3. 组合根：AppServices（无 DI 容器）

`AppServices.cs` 是唯一的服务装配点：**27 个 Lazy 单例**，构造函数注入依赖，刻意不用 DI 容器（服务数少、依赖是静态树、零依赖、启动快、依赖关系一屏可见）。

```text
Store ─┬─ BookmarkService ── AssistantService
       ├─ AppearanceService
       ├─ ObsSettingsService ── ObsConnectionService ──┬─ SceneTemplateService
       ├─ ObsPathService ──┬─ ObsBackupService ────────┤
       │                   └─ ObsResetService           └─ SceneAutoSwitcher / Tray / Hotkeys / Timer
       ├─ AiSettingsService ── CloudDiagnosticEngine
       ├─ FreeRateLimiter ── DiagnosticOrchestrator
       └─ (Hotkey/Mini/AutoSwitch 等 Shell 设置)

Host ── ObsSettingsService / AiSettingsService / CloudDiagnosticEngine / FreeDiagnosticEngine
Problems ── AssistantService / ObsToolRegistry / LocalDiagnosticEngine / Orchestrator
```

三个**运行时注入**的全局服务由 `MainWindow` 构造时挂进 `AppServices`：

- `AppServices.Navigation`（导航服务）
- `AppServices.Busy`（全局忙遮罩）
- `AppServices.Toast`（统一轻提示）

## 4. 启动与退出

**启动**（`App.xaml.cs` `OnStartup`）：

1. 单实例锁：`TryAcquireSingleInstance()` 用会话级 Mutex `Local\OBS_Helper.SingleInstance` 判定；非首实例发送 `Local\OBS_Helper.ShowMainWindow` 事件唤起已有窗口后立即退出。
2. 首实例执行 `KillStrayInstances()`：清理同名的僵尸 / 残留进程。
3. 挂三个全局异常钩子：`DispatcherUnhandledException`、`AppDomain.UnhandledException`、`TaskScheduler.UnobservedTaskException`——统一走 `ReportError`（弹窗节流 5 秒 + FileLogger 记录；`HeadlessTest` 模式下只累积不弹窗）。
4. `AppServices.InitializeAsync()`：并行加载 `ObsSettings` + `AiSettings` → 启动托盘 / 全局热键 / 场景自动切换。

**退出**（`MainWindow.OnClosed` → `AppServices.ShutdownServices()`）：按序停止 Mini → AutoSwitcher → Hotkeys → SystemMonitor → Timer → Tray，每步 try/catch 兜底，不让单点失败阻塞退出。

## 5. 导航模型

- **路由表**：`Navigation/Routes.cs` 集中定义路由名常量（`home` / `console` / `settings` 等），避免拼字符串。
- **导航服务** `NavigationService`：路由名 → 页面工厂；页面实例**缓存复用**（保滚动位置、避免重建）；维护前进/后退栈。
- **页面生命周期**：页面实现 `INavigationAware` 接口——`OnNavigatedToAsync(parameter)` 进入时加载数据；`OnNavigatedFromAsync()` 离开时对称退订事件/停计时器（接口默认实现，页面按需覆写）；`CanReleaseOnLeave` 决定离开后是否允许从缓存释放实例。
- **切换动画**：`MainWindow` 在 `Navigated` 事件里做淡入 + 上移动画（「减少动画」开启时直赋值）。

## 6. OBS 连接层

```
ObsWebSocketClient（裸协议）
   ├─ 连接 / 鉴权（ObsAuth 计算 challenge） / 请求-响应（带超时） / 事件推送
   └─ 断线自动重连（ObsReconnectPolicy 退避）
        │
ObsConnectionService（连接生命周期状态机）
   ├─ 状态（Disconnected/Connecting/Connected）→ 对外暴露 StateChanged 事件（线程安全封送 UI 线程）
   ├─ 订阅管理：场景列表、来源、音频、过渡、录制/推流状态
   └─ 提供 RawRequestAsync 给上层（模板落地、控制台操作）
```

- **线程**：`ObsWebSocketClient` 内部收包循环在后台线程；所有对外事件经 `Dispatcher` 封送回 UI 线程，页面事件处理器无需关心线程。
- **鉴权**：`ObsAuth` 依 obs-websocket v5 协议用 salt + challenge 计算响应。

## 7. AI 诊断三通道

```
DiagnosticOrchestrator（编排：按可用性选通道，汇总诊断项）
   ├── CloudDiagnosticEngine   云端大模型（API Key + function-calling）
   │      └── ObsToolRegistry   可调用工具（get_problem_detail 等，白名单注册）
   ├── FreeDiagnosticEngine    免费内置 AI（智谱内置密钥 / Pollinations 免 Key）
   │      ├── FreeAiKeyProvider   解密内置密钥（发布包内嵌，构建期注入）
   │      └── FreeRateLimiter     按通道独立本地限频（持久化 prefs.json）
   └── LocalDiagnosticEngine   本地搜索助手（知识库检索，无网络，文案统一「本地的搜索助手」）
```

- 所有引擎输出统一为 `DiagnosticItem`（`DiagnosticTypes.cs`），严重度经 `DiagnosticSeverityMapper` 映射。
- 云端 / 免费通道经 `HostBridge` 转发，强制 HTTPS 且拒绝内网/回环地址（SSRF 防护）。

## 8. 数据持久化与机密

- **存储介质** `LocalStore`：统一键值存储，落在 `%LocalAppData%\OBS_Helper\`。
  - `prefs.json`：普通设置（含非机密的限额计数、小窗位置等）。
  - `secrets.dat`：机密（OBS 密码、API Key）。
- **机密双层加密**（V1.7.0+）：
  1. 值级：PBKDF2-SHA256(MachineGuid + 应用熵) 派生密钥做 **AES-256-GCM**，格式 `v2:<nonce>:<tag>:<cipher>`；
  2. 文件级：**DPAPI**（CurrentUser + 应用熵）整体加密。
  - 旧版明文值读取时自动兼容，下次写入自动升级 v2。

## 9. OBS 配置管理模块（ObsConfig）

```
ObsPathService   定位 OBS 配置目录（注册表 + 常见路径探测，含版本兼容）
ObsSafePath      路径安全护栏：解析真实路径后二次校验，防 .. 穿越；越界抛 SafePathException
ObsBackupService 备份（zip 打包，含 scene_collection / 全局设置 / 可选密钥）与导入（校验 + 事务）
FileTx           文件事务：提交 / 回滚，目录级原子性（导入失败可恢复）
ObsResetService  配置重置：连接态校验（录制/推流中拒绝）、场景清空
SceneTemplateService 场景模板：在线落地（建专属配置集合 → 逐场景/来源创建，跨场景来源走 shared 标记）
                     / 离线导出（标准 OBS 场景集合 JSON，落 basic/scenes/）
```

所有涉及用户配置目录的读写一律先过 `ObsSafePath`，任何越界操作转成「拒绝执行」提示。

## 10. 后台 / 遥控能力（Services/Shell）

| 能力 | 实现 | 要点 |
|---|---|---|
| 系统托盘 | `TrayService` | 菜单、图标、常驻；承担磁盘预警通知 |
| 全局热键 | `GlobalHotkeyService` | 注册/注销、动作分发，跨页面生效 |
| 迷你小窗 | `MiniWindowService` | 显示/隐藏、位置记忆（prefs.json） |
| 系统监控 | `SystemMonitorService` | 每秒采样 CPU/内存/磁盘；`PerformancePage` 订阅展示；预警阈值见 Tray |
| 场景自动切换 | `SceneAutoSwitcher` | 正则匹配窗口标题，**ReDoS 超时保护**（匹配超时中止） |
| 定时停止 | `ControlTimerService` | 录制/推流定时停止 |

## 11. 线程模型

- **UI 线程**：所有 WPF 页面 / 控件 / 服务状态变更。
- **后台线程**：
  - WebSocket 收包循环（`ObsWebSocketClient.ReceiveLoop`）→ 事件经 Dispatcher 封送；
  - 系统采样计时器（`SystemMonitorService`）→ 数据发布到 UI 线程；
  - AI / HTTP / 文件 IO → `async/await`（`ConfigureAwait(false)` 后自行封送）；
  - 自检模式（`OBS_SELFTEST=1`）→ 无界面跑 14 条路由自检，结果写 `selftest_result.txt`。
- **跨线程事件约定**：服务只发布事件，不直接碰控件；页面在事件处理器里用 `Dispatcher` 或服务已封送的回调更新 UI。

## 12. 日志与错误处理

- **日志**：`FileLogger`（跨日滚动文件日志）+ `TraceLoggerListener`（Trace 接 FileLogger）。
- **异常**：三个全局钩子统一收编；同一报错码弹窗 5 秒节流；`HeadlessTest` 模式下只累积不弹窗（供 CI 自检）。
- **错误码**：`Errors/ErrorCodes.cs` 集中定义码表与用户可读说明（含解决建议）；业务层抛错误码而非裸字符串，UI 按码渲染。
- **用户提示**：`ToastService`（轻提示）+ `BusyService`（忙遮罩）+ 错误弹窗三级。

## 13. 更新双通道

`UpdateService`：

1. **蓝奏云**：内置 `DownloadUrl` 常量 + 密码，直接下载安装包；
2. **GitHub Release**：`releases/latest` 找 `OBS_Helper_Setup_*.exe` 资产，去 `V/v` 前缀后 `Version.TryParse` 比较；下载用随机临时名 + **MZ 头校验**。
   - Release 未建时方式二因「最新版不高于当前版」拒绝下载——这是特性。

`UpdateDialog` 提供四种用户选择：蓝奏云下载 / 应用内下载 / 稍后再说 / 打开 GitHub Release 页。

## 14. 安全设计清单

| 面 | 措施 |
|---|---|
| 机密 | DPAPI + AES-256-GCM 双层加密；熵与应用绑定；换机/换用户不可解 |
| 日志读取 | 目录限定 `%AppData%\obs-studio\logs|crashes`；真实路径二次校验；单文件 8MB 截尾 |
| 云端转发 | 强制 https；拒绝内网/回环地址（SSRF） |
| 路径操作 | `ObsSafePath` 护栏，防 `..` 穿越，越界抛异常 |
| Markdown | 链接白名单（http/https/mailto/站内），含引号或空白即丢弃 |
| 日志脱敏 | `LogSanitizer` 抹掉密钥/敏感串，保留 OBS 正常长串 |
| 免费 AI 限频 | 按通道独立限额（智谱 10 次/天强限制，Pollinations 20 次），本地强制 |
| 正则 | 场景自动切换匹配带 ReDoS 超时 |
| 单实例 | 会话级 Mutex + 残留进程清理 |

## 15. 构建与发布

- `build.ps1`：`dotnet publish`（Release、R2R、自包含单文件）→ Inno Setup 安装包 → 便携 zip；产物到 `PAKE/windows/`（gitignore）。
- 免费 AI 密钥：构建期由 `scripts/embed_free_ai_key.ps1` 注入 `Assets/free_ai_key.json`（真实密钥不入库）。
- 自检：`OBS_SELFTEST=1` 无界面跑 14 条路由自检，结果写 `selftest_result.txt`——「编译过但运行炸」类错误的最有效拦截。
- 数据脚本：`scripts/add_problems.py` / `add_templates.py` 可复用改知识库 / 模板数据。
