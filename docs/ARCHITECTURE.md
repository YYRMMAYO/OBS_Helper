# OBS 排障助手 · 架构说明

一个跨平台（Windows + macOS）的 OBS 直播排障助手：用同一套 **Blazor WebAssembly** 站点承载全部排障内容与交互，
再分别套上平台原生的轻量桌面壳（Windows 用 WebView2，macOS 用 Tauri/WKWebView）。
逻辑与界面只写一次，双端共享。

## 整体结构

```
OBS_Helper（仓库根）
├── OBS_Helper.Client/        # 共享前端：Blazor WebAssembly 站点（所有排障逻辑在这里）
│   ├── Pages/                # 路由页面：首页 / 分类 / 问题详情 / 搜索 / 助手 / 搭建向导
│   │                        #   / 诊断(智能诊断+自检) / 控制台(obs-websocket) / 日志分析
│   │                        #   / 排障指引(内置 troubleshooting.md, Markdown 渲染) / 设置
│   ├── Layout/               # 主布局（导航 / 外壳 / 外观 data-* 注入）
│   ├── Components/           # 复用组件：ProblemCard、ConnectionBadge、ConfirmDialog
│   ├── Models/               # 数据模型：Problem / Step / Link
│   ├── Services/
│   │   ├── ProblemService / BookmarkService / AssistantService   # 原有 KB 服务（Scoped）
│   │   ├── Host/             # HostBridge：壳↔站点桥；密钥加密落盘、读取日志、AI 转发
│   │   ├── Obs/              # ObsConnectionService（状态机+重连）、ObsSettingsService、
│   │   │                    #   ObsWebSocketClient、ObsAuth、ObsReconnectPolicy
│   │   ├── Log/              # ObsLogAnalyzer：离线日志分析（23 规则 + 量化滞后比）
│   │   └── Ai/               # 可切换 AI 诊断引擎（见下文“AI 诊断引擎”）
│   ├── Errors/               # ErrorCodes.cs（统一报错码）
│   └── wwwroot/              # 静态站点：index.html、css/app.css、data/problems.json、_framework
│
├── OBS_Helper.Win/           # Windows 桌面壳（.NET 10 + WebView2 + WinForms）
│   ├── MainForm.cs           # WebView2 初始化、加载站点、报错弹窗（带 OBS 报错码）
│   ├── Errors/AppError.cs    # 桌面壳侧报错码文案
│   └── OBS_Helper_Setup.iss  # Inno Setup 安装包脚本（输出到 PAKE/windows）
│
├── OBS_Helper.Mac/           # macOS 桌面壳（Rust + Tauri v2）
│   └── src-tauri/            # Cargo.toml / tauri.conf.json / main.rs / 能力 / 图标
│       └── build-mac.sh      # 发布客户端站点 -> tauri icon -> tauri build（输出 PAKE/macos）
│
├── content/                  # 主数据源 problems.json（85 条问题 + 官方链接）+ 生成脚本
│   └── problems.json         # 单一事实来源，构建时同步到 Client wwwroot/data
│
├── build.ps1                 # Windows 端一键构建 + 打包到 PAKE/windows
├── scripts/cdp_smoke.py      # 无头 Edge 冒烟测试（验证站点可渲染、无异常）
├── .github/workflows/ci.yml  # 双端 CI：Windows(Inno) + macOS(Tauri) -> 制品 PAKE/*
└── PAKE/                     # 统一输出目录（git 忽略）：windows/、macos/
```

## 关键设计

1. **一份站点，双端壳。** 排障内容（`problems.json`）、UI、交互均在 `OBS_Helper.Client` 中，
   两个桌面壳只负责把站点当本地页面加载。改一处，两端同时生效。

2. **数据驱动。** 所有问题与解决方案来自 `content/problems.json`，由 `ProblemService` 在启动时加载。
   `augment_problems.py` 负责为每条问题追加官方文档链接、并按需补充新常见问题。

3. **报错码体系。** 客户端与桌面壳共用 `OBSxxx` 编码规则；桌面壳弹窗与网页兜底错误都带码，便于定位。
   详见 `docs/ERROR_CODES.md`。

4. **统一打包出口 `PAKE`。** 不论哪个系统，安装包与可运行软件都落到 `F:/OBS/PAKE` 下：
   - Windows：`PAKE/windows/OBS_Helper_Setup_1.0.0.exe`（安装包）+ `OBS_Helper_Portable_1.0.0.zip`（便携包）
   - macOS：`PAKE/macos/*.dmg` + `*.app`

5. **CI 双端验证。** GitHub Actions 在 `windows-latest` 与 `macos-latest` 上分别构建并产出制品；
   Windows 端额外跑无头 Edge 冒烟测试，验证站点真实渲染且无控制台异常。

6. **安全模型（本地优先、最小权限）。** 详见 `docs/SECURITY.md`。要点：
   - 站点默认完全本地、离线运行，**不主动发起任何网络请求**；所有外链（官方文档）由 WebView2 / 系统浏览器代开，且经 `IsSafeUrl` 校验仅放行 `http/https`。唯独「云端 AI 诊断」在用户于「设置」显式开启并填写 https 端点后，由**宿主进程**代发请求（密钥不进前端、仅 https、带 SSRF 防护）——详见 SECURITY.md §2.5。
   - macOS 端：Tauri 配置 `security.csp` 作为纵深防御；窗口禁用 devtools/远程调试，并对导航做白名单限制；capabilities 仅保留 `core:default`，不向前端暴露任何 IPC 命令。
   - Windows 端：WebView2 关闭 DevTools / 默认右键菜单 / WebMessage，渲染进程崩溃自动重载；外链通过 `NewWindowRequested` 交由系统默认浏览器打开。
   - 桌面壳与 `problems.json` 同源，内容可信，不存在用户可控输入注入路径（数据文件为打包内容，非运行时远程加载）。

7. **可切换 AI 诊断引擎。** 见下「AI 诊断引擎」专节：默认纯本地规则引擎离线运行；用户在「设置」可一键切到云端 LLM（OpenAI 兼容），由宿主进程转发并自动降级回本地。

8. **obs-websocket 控制台 + 离线日志分析。** 见「控制台与日志分析」专节：`/console` 连接 OBS 做实时控制，`/logs` 离线分析 OBS 日志，结论汇入智能诊断。

9. **外观与无障碍（data-* 驱动）。** `<html>` 上挂 `data-theme` / `data-font-scale` / `data-contrast` / `data-motion`，由 `AppearanceService` 控制并持久化；`index.html` 内联防闪烁脚本避免主题闪烁。

## AI 诊断引擎（可切换本地 / 云端）

核心类型在 `OBS_Helper.Client/Services/Ai/`：

- `DiagnosticOrchestrator`：对外唯一入口，`DiagnoseAsync(query)` 按 `AiSettingsService.Mode` 分派，并维护 `LatestReport`。
- `LocalDiagnosticEngine`：纯规则、离线。结合 KB（`AssistantService.AskAsync` 查询式匹配）+ 实时连接状态（`ObsConnectionService` 的 `ObsStats` / `StreamStatus`）+ 离线日志结论（`ObsLogAnalyzer`），无需联网。
- `CloudDiagnosticEngine`：经 `HostBridge.AiChatAsync(url, secretKeyName, body)` 调用宿主进程转发 **OpenAI 兼容 `chat/completions`**，支持 function calling；`ObsToolRegistry` 向模型暴露四个工具：`get_connection_snapshot`、`get_log_findings`、`get_problem_detail`、`search_problems`，返回值同时被本地引擎复用。
- `AiSettingsService`：持有运行期设置（模式 / 云端 URL / 密钥名 / 模型），持久化到 `localStorage` 键 `obshelper.ai`，**只存密钥名不存密钥值**。
- 自动降级：云端失败（未配置密钥 / 网络异常 / 响应解析失败）时回退本地，结果标记 `FellBackToLocal=true` 并保留原始 `Error`。

## obs-websocket 控制台与离线日志分析

- **控制台 `/console`**：`ObsConnectionService` 连接 obs-websocket 5.x（状态机 + 指数退避重连，`ObsReconnectPolicy`；密码经 `ObsAuth` SHA256 摘要），提供场景切换、音频静音/音量、录制/推流/虚拟摄像头开关。所有写操作经 `ConfirmDialog` 二次确认。
- **日志 `/logs`**：经 `HostBridge.logs.list/read` 读取 OBS 日志，`ObsLogAnalyzer` 单遍扫描（31 条规则 + 3 个量化滞后比）产出结构化结论；结论写入 `DiagnosticOrchestrator.LatestReport` 供智能诊断引用，也支持拖拽本地日志文件。
- **隐私脱敏**：`LogSanitizer`（纯函数）在展示或送 AI 前对密钥 / URL / 邮箱 / MAC / IP / Token 做掩码。

## 系统体检与配置体检（环境层诊断）

诊断数据从「日志 + 连接」两路扩展到四路，统一汇入 `DiagnosticContext`：

- `SystemHealthService`（`Services/Obs/`）：调 `host.system.info` 拿到平台 / 显卡列表 / HAGS 开关 /
  游戏模式 / 录制盘余量 / OBS 进程（是否运行、是否管理员、内存占用、版本），再按规则给出结论——
  HAGS 开启、双显卡（已过滤 Parsec / VMware / Microsoft Basic 等虚拟显卡）、磁盘 <20GB 警告
  / <5GB 严重、OBS 未以管理员运行、OBS 内存 >4GB、版本落后（语义化版本比较）。
  可选调 `host.obs.latestVersion` 查最新版；**任一宿主调用失败都只降级为 `Available=false`，不影响其它诊断**。
- `ObsConfigScanner`（`Services/Obs/`）：经 `host.config.list` / `host.config.read` 读取
  `obs-studio` 配置目录（Win `%AppData%\obs-studio`，mac `~/Library/Application Support/obs-studio`），
  解析 `basic.ini` / 场景集合，校验编码器、码率、关键帧间隔、采样率、录制格式等设置项。
- `DiagnosticOrchestrator.ScanEnvironmentAsync()` 统一触发并缓存两份报告（`LatestSystem` / `LatestConfig`），
  `DiagnoseAsync` 未命中缓存时自动补扫。
- `LocalDiagnosticEngine` 按「日志 > 系统 > 配置 > 知识库」四级优先级合并，按 `ProblemId` 去重；
  `CloudDiagnosticEngine` 把系统环境与配置结论作为 `[本机系统环境]` / `[OBS 配置体检发现]` 段落拼进提示词。

## 实时监控与阈值告警

`LiveMonitorService`（`Services/Obs/`，`IAsyncDisposable`）在控制台连接成功后自动启动，
2 秒一次 `PeriodicTimer` 轮询 `GetStats` 与输出状态：

- **关键点**：obs-websocket 返回的丢帧数是 **OBS 启动以来的累计值**，直接算比率会把早期的一次卡顿
  永久摊进平均数，实时性失真。因此本服务保留上一次采样，用 `Δskipped / Δtotal` 计算**窗口增量丢帧率**，
  才能反映"此刻"的状态；界面在有窗口值时优先展示它，无历史样本时回落到累计值。
- 阈值：渲染 / 编码丢帧 2%（警告）/ 10%（严重），推流丢帧 1% / 5%，CPU 80%，
  磁盘 10GB / 2GB，实际帧率低于目标 90%。
- 每个告警码有 60 秒冷却，最多保留 50 条告警与 60 个采样点；告警携带 `ProblemId` 可直达对应排障方案。

## 外观与无障碍（data-* 驱动）

- `<html>` 根元素挂四个属性：`data-theme`(light/dark)、`data-font-scale`(sm/md/lg/xl)、`data-contrast`(high)、`data-motion`(reduce)，对应 CSS 变量。
- `AppearanceService` 负责读写并持久化；设置页 `/settings` 提供对应控件。
- `index.html` 内联极小脚本在首帧前注入 data-*，避免主题/字号切换时的闪烁（FOUC）。
- 沿用既有 `prefers-color-scheme` 媒体查询作为默认主题兜底。

## 本地开发

- 前端：`dotnet run --project OBS_Helper.Client`（Blazor Dev Server）
- Windows：`pwsh ./build.ps1` 一键构建 + 打包到 `PAKE/windows`
- macOS：在 macOS 上 `bash OBS_Helper.Mac/src-tauri/build-mac.sh`
- 验证：构建后 `python scripts/cdp_smoke.py --root "<发布目录>/wwwroot"`
