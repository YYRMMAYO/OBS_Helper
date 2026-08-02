# OBS 排障助手 · 依赖与第三方库清单

本文件列出构建与运行本软件所依赖的运行库、框架与第三方工具，含版本与许可证，便于合规审查与再分发。

## 一、应用自身

| 名称 | 版本 | 许可证 | 说明 |
| --- | --- | --- | --- |
| OBS 排障助手 (OBS Helper) | 1.0.0 | MIT | 本仓库代码，见 `LICENSE` |

## 二、Windows 桌面端（WebView2 宿主）

| 库 / 框架 | 版本 | 许可证 | 用途 |
| --- | --- | --- | --- |
| .NET Runtime | 10.0 (自包含发布) | MIT | 应用运行时，随安装包内置，目标机无需预装 |
| Microsoft.Web.WebView2 | 1.0.4078.44 | 微软 WebView2 SDK 许可（免费再分发） | 承载 Blazor WASM 站点的内置浏览器引擎 |
| Inno Setup | 6.x | 免费软件（非商业免费；商业用途需购买许可证） | 生成 `.exe` 安装包 |

> 自包含发布（`SelfContained=true`）会把 .NET 运行时一并打进安装包，因此最终用户无需安装 .NET。
> WebView2 Runtime 由 Windows 10/11 自带（Evergreen），安装包会确保环境可用。

## 三、共享前端（Blazor WebAssembly）

| 库 / 框架 | 版本 | 许可证 | 用途 |
| --- | --- | --- | --- |
| .NET (Blazor WebAssembly) | net10.0 | MIT / Apache-2.0 | 客户端框架 |
| Microsoft.AspNetCore.Components.WebAssembly | 10.0.9 | Apache-2.0 | Blazor 组件模型与 WASM 宿主 |
| Microsoft.AspNetCore.Components.WebAssembly.DevServer | 10.0.9 | Apache-2.0 | 本地开发服务器（仅开发期，不进发布包） |

## 四、macOS 桌面端（Tauri v2 宿主）

| 库 / 框架 | 版本 | 许可证 | 用途 |
| --- | --- | --- | --- |
| Rust（工具链） | 1.77.2+ | MIT / Apache-2.0 | 编译 macOS 宿主 |
| tauri (Rust crate) | 2.x | MIT / Apache-2.0 | 桌面外壳与系统集成 |
| tauri-build | 2.x | MIT / Apache-2.0 | 构建期代码生成 |
| serde | 1.x | MIT / Apache-2.0 | 配置反序列化 |
| serde_json | 1.x | MIT / Apache-2.0 | JSON 处理 |
| @tauri-apps/cli (npm) | latest | MIT / Apache-2.0 | CI 中执行 `tauri build` / `tauri icon` |
| WKWebView（系统框架） | 随 macOS | Apple 系统框架 | macOS 原生网页视图 |

> macOS 端通过 `OBS_Helper.Mac/src-tauri/Cargo.toml` 固定主要依赖；`Cargo.lock` 已随仓库提交以保证可复现构建。

## 五、数据来源（运行时引用，非编译依赖）

问题数据与官方文档链接来自本地 `content/problems.json`，其中「官方文档 / 参考链接」指向下列公开资源（仅在用户点击时跳转，应用不抓取内容）：

- OBS 官方：https://obsproject.com
- OBS 日志分析器：https://obsproject.com/tools/analyzer
- OBS 官方Windows 排障：https://obs-studio-app.github.io/obs-studio-troubleshooting-windows.html
- OBS Versions 黑屏/编码教程：https://obs-versions.com/blog/fix-obs-black-screen
- OBS macOS 权限指南：https://obsproject.com/kb/macos-permissions-guide
- OBS 中文站：https://www.obsproject.com.cn

## 六、再分发须知

- 安装包与可运行软件统一输出到仓库根目录 `PAKE/`（`windows/`、`macos/`），该目录已被 `.gitignore` 忽略，不参与源码提交。
- 若用于商业分发，请注意 **Inno Setup** 与 **WebView2 SDK** 的许可条款；本项目代码本身以 MIT 许可证发布。
- 安装包/可运行软件应当发布到 **GitHub Releases**（而非源码仓库），因为它们体积较大（`.exe` ≈ 57MB、`.zip` ≈ 77MB），且被 `.gitignore` 排除。

## 七、代码签名（证书）状态

> 本仓库目前**未配置任何代码签名**。下列条目为发布前需补齐的手动步骤（需要你自行购买/申请证书，助手无法代签）。

| 平台 | 状态 | 影响 | 所需证书 / 操作 |
| --- | --- | --- | --- |
| Windows 安装包 (`OBS_Helper_Setup_1.0.0.exe`) | ❌ 未签名 | 安装时触发 SmartScreen / “未知发布者” 警告 | Authenticode 代码签名证书（如 Sectigo/DigiCert）；在 Inno Setup 中配置 `SignTool` 对 `setup.exe` 与内含文件签名 |
| Windows 主程序 (`OBS_Helper.exe`) | ❌ 未签名 | 同上 | 与安装包同证书，构建后签名 |
| macOS 应用 (`.app` / `.dmg`) | ❌ 未签名 | Gatekeeper 拦截，需用户手动 `xattr -rd com.apple.quarantine` 或“右键-打开” | Apple Developer ID Application 证书 + `notarytool` 公证；在 `tauri.conf.json` 的 `bundle.macOS` 配置 `signingIdentity` 与公证凭据 |

- 许可证文本：仓库根 `LICENSE`（MIT）现已在 Windows 安装向导中展示，并随安装包复制到 `{app}\LICENSE`；macOS/Tauri 通过 `Cargo.toml` 的 `license = "MIT"` 声明。
- 程序元数据：两端 `.csproj` 已写入 `Copyright` / `Authors` / `PackageLicenseExpression=MIT` / 仓库地址；Inno Setup 已写入 `VersionInfo*` 与 `AppPublisher`。
