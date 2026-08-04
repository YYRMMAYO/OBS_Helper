# OBS 排障助手 · 安全模型与审查说明

本应用定位为**离线、本地优先**的排障工具：UI 与数据（`problems.json`）均随包发布，运行时
不依赖远程服务。以下为安全设计与自审结论。

## 1. 威胁模型与边界

| 资产 | 说明 |
| --- | --- |
| 本地站点（Blazor WASM） | 来自 `OBS_Helper.Client/wwwroot`，与桌面壳同包发布，视为**可信** |
| 问题数据 `problems.json` | 打包内容，非运行时远程拉取，不受外部篡改 |
| 用户本地存储 | 收藏 / 步骤进度写入 `localStorage`，仅在用户本机 |
| 外链（官方文档） | 用户点击才打开，指向 `obsproject.com` 等公开文档站 |
| 云端 AI 端点（用户配置） | 仅在「设置」显式开启云端模式后，由**宿主进程**代发 HTTPS 请求；密钥不进前端 |
| API Key（云端 AI） | 经桌面壳加密落盘（Windows DPAPI / macOS Keychain），运行时仅宿主进程可解密 |

**不在范围内**：账号体系、内置远程后端、付费、遥测、屏幕/麦克风采集（App 仅展示设置步骤）。

> 注：云端 AI 诊断属**可选功能**——默认关闭，需用户在「设置」中自行填写 https 端点与密钥名；
> 请求由宿主进程代发（密钥不进前端），不纳入上述“内置远程后端”范畴，安全约束见 §2.5。

## 2. 关键安全措施（已落地）

### 2.1 输出净化 / 渲染安全
- Blazor 使用 Razor 语法渲染文本，默认对 `@变量` 做 HTML 转义，天然防 XSS。
- 所有外链在 `ProblemDetail.razor` 经 `IsSafeUrl` 校验，**仅放行 `http/https`**，
  屏蔽 `javascript:` / `data:` / `file:` 等危险协议（纵深防御，即便数据被篡改也不会触发脚本注入）。

### 2.2 内容安全策略（CSP，macOS / Tauri）
`tauri.conf.json` 配置：
```
default-src 'self' ipc:;
img-src 'self' data: blob:;
style-src 'self' 'unsafe-inline';
script-src 'self' 'wasm-unsafe-eval';
font-src 'self' data:;
connect-src 'self' ipc:;
base-uri 'self'; form-action 'none'; frame-ancestors 'none'
```
限制脚本、连接、表单与嵌套来源，仅允许本地资源与 WASM 执行。

### 2.3 桌面壳最小权限
- **macOS（Tauri v2）**：capabilities 仅保留 `core:default`，**不向站点暴露任何 IPC 命令**；
  窗口关闭 devtools / 远程调试，并注册导航白名单（仅允许 `localhost` / `asset:`），阻断外部页面跳转。
- **Windows（WebView2）**：关闭 `AreDevToolsEnabled` / `AreDefaultContextMenusEnabled` /
  `IsWebMessageEnabled`，避免本地内容被篡改后借助调试协议逃逸；
  外链通过 `NewWindowRequested` 交由系统默认浏览器打开，WebView2 自身不导航到外部页；
  user-data 目录改为 `LocalAppData`（修复 Program Files 下无写权限导致启动失败）；
  渲染进程崩溃自动重载。

### 2.4 隐私
- 不采集任何个人信息、不发送网络请求、不集成分析 SDK。
- 全站 `<meta name="referrer" content="no-referrer">`，外链跳转不携带来源信息。
- `localStorage` 不可用时静默降级，不阻断浏览。

### 2.5 云 AI 密钥与安全转发（可选功能）
- **密钥不进前端**：API Key 仅存于桌面壳进程。WebView/Blazor 永远拿不到明文密钥；`AiSettingsService` 只持久化「密钥名」（指向壳内密钥库），不持久化密钥值。
- **加密落盘**：密钥经桌面壳写入系统密钥库——Windows 用 DPAPI、macOS 用 Keychain；明文不写文件、不写 `localStorage`。
- **宿主中转 + 协议约束**：`HostBridge.AiChatAsync` 由宿主实现，强制 **仅 https** 目标；非 https 直接拒绝，天然抑制明文外泄与对内网明文服务的 SSRF。
- **SSRF 防护**：宿主侧对目标地址做阻断（loopback / 私网 / link-local / 云元数据 `169.254.169.254` 等），避免把请求打到内网或云元数据服务。
- **PII 脱敏**：日志 / 连接快照送 AI 前必经 `LogSanitizer` 掩码（密钥、URL、邮箱、MAC、IP、Token），降低泄露面。
- **失败回退**：云端不可用（未配置 / 网络 / 解析异常）时 `DiagnosticOrchestrator` 自动回退本地规则引擎，保证离线可用。
- **本地模型零网络**：默认 Local 模式完全离线，不发起任何请求。

## 3. 自审发现与处置

| # | 位置 | 风险 | 处置 |
| --- | --- | --- | --- |
| 1 | macOS `tauri.conf.json` | `csp: null`（无 CSP） | 已配置严格 CSP |
| 2 | macOS `main.rs` | 未限制导航 / 未关 devtools | 已加导航白名单 + 移除 `macos-private-api` 非必要特性 |
| 3 | Windows `MainForm.cs` | WebView2 未关 DevTools / 未拦截外链 | 已关闭调试相关能力 + 外链交系统浏览器 |
| 4 | `ProblemDetail.razor` | 外链未校验协议 | 已加 `IsSafeUrl` 仅放行 http/https |
| 5 | `BookmarkService.cs` | 加载缓存条件会导致“收藏丢失”错觉；`catch {}` 吞异常 | 改为 `_loaded` 标志；`catch (Exception)` 注释说明 |
| 6 | `MainLayout` / 输入控件 | 缺少无障碍语义（landmark / label / 跳过链接） | 已补 `<header>`/`<main>`/`<nav>` 角色、skip-link、visually-hidden 标签、`type="search"`、`focus-visible` |
| 7 | 云端 AI（`CloudDiagnosticEngine` / `HostBridge.AiChatAsync`） | 引入外发网络与密钥面 | 密钥仅存宿主、强制 https、SSRF 阻断、PII 脱敏、失败回退本地（§2.5） |
| 8 | `LogSanitizer` | 日志/快照含敏感信息（密钥 / 路径 / Token） | 展示与送 AI 前统一 `LogSanitizer` 掩码 |
| 9 | `ObsAuth` / obs-websocket | 密码明文传输/落盘风险 | 采用 obs-websocket 5.x 挑战应答（SHA256 摘要），密码不落盘 |

## 4. 残余风险与建议

- **Windows 安装包无代码签名**：CI 产物未签名，用户首次运行可能触发 SmartScreen。
  建议在有证书时由 CI 对 `OBS_Helper.exe` 与 Inno 安装包做 Authenticode 签名。
- **macOS 包未公证**：CI 出包未签名/公证，需用户在「隐私与安全性」中手动允许。
  建议配置 Developer ID 证书 + `notarytool` 公证后再分发。
- **WebView2 未设 `AdditionalBrowserArguments` 禁用远程调试端口**：当前已通过禁用 DevTools +
  关闭 WebMessage 收敛，仍建议在 CI 内置固定运行时时附加 `--disable-remote-debugging`。
- **云端 AI 端点由用户自配**：默认关闭且无内置端点；建议用户仅填写可信 https 地址。宿主侧 SSRF 阻断列表需随依赖/部署环境持续维护。
- **obs-websocket 连接**：仅在用户于「控制台」显式填写地址/端口/密码后建立；密码经挑战应答不落盘，建议仅在可信局域网内启用。
- **`problems.json` 体积（约 160KB）随包发布**：可接受；若后续引入运行时远程更新，
  必须加签名校验，否则会成为篡改入口。
