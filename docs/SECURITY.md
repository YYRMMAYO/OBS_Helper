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

**不在范围内**：账号体系、远程后端、付费、遥测、屏幕/麦克风采集（App 仅展示设置步骤）。

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

## 3. 自审发现与处置

| # | 位置 | 风险 | 处置 |
| --- | --- | --- | --- |
| 1 | macOS `tauri.conf.json` | `csp: null`（无 CSP） | 已配置严格 CSP |
| 2 | macOS `main.rs` | 未限制导航 / 未关 devtools | 已加导航白名单 + 移除 `macos-private-api` 非必要特性 |
| 3 | Windows `MainForm.cs` | WebView2 未关 DevTools / 未拦截外链 | 已关闭调试相关能力 + 外链交系统浏览器 |
| 4 | `ProblemDetail.razor` | 外链未校验协议 | 已加 `IsSafeUrl` 仅放行 http/https |
| 5 | `BookmarkService.cs` | 加载缓存条件会导致“收藏丢失”错觉；`catch {}` 吞异常 | 改为 `_loaded` 标志；`catch (Exception)` 注释说明 |
| 6 | `MainLayout` / 输入控件 | 缺少无障碍语义（landmark / label / 跳过链接） | 已补 `<header>`/`<main>`/`<nav>` 角色、skip-link、visually-hidden 标签、`type="search"`、`focus-visible` |

## 4. 残余风险与建议

- **Windows 安装包无代码签名**：CI 产物未签名，用户首次运行可能触发 SmartScreen。
  建议在有证书时由 CI 对 `OBS_Helper.exe` 与 Inno 安装包做 Authenticode 签名。
- **macOS 包未公证**：CI 出包未签名/公证，需用户在「隐私与安全性」中手动允许。
  建议配置 Developer ID 证书 + `notarytool` 公证后再分发。
- **WebView2 未设 `AdditionalBrowserArguments` 禁用远程调试端口**：当前已通过禁用 DevTools +
  关闭 WebMessage 收敛，仍建议在 CI 内置固定运行时时附加 `--disable-remote-debugging`。
- **`problems.json` 体积（约 160KB）随包发布**：可接受；若后续引入运行时远程更新，
  必须加签名校验，否则会成为篡改入口。
