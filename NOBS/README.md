# OBS 排障助手 · WPF 版

面向直播新手的 OBS Studio 排障工具。**纯离线可用**：85 条问题的知识库、排障指引、日志分析规则全部内嵌在程序里，不联网也能查。连上 OBS 之后还能远程控制场景、录制与推流，并做一键体检。

这是原 Blazor WebAssembly + WebView2 版本的**原生 WPF 重构**，功能一比一对齐，但去掉了浏览器内核这一层。

## 与旧版的差别

| | 旧版（Blazor + WebView2） | 本版（WPF） |
|---|---|---|
| 界面技术 | Blazor WASM 站点，跑在 WebView2 里 | 原生 WPF |
| 运行前提 | 目标机需要 WebView2 Runtime | 无（自包含发布已含 .NET 运行时） |
| 安装体积 | 站点 + 运行时 + WebView2 | 单一自包含目录 |
| 冷启动 | 需初始化 WebView2 + 下载 WASM | 直接起窗口 |
| 知识库读取 | HTTP 拉 `wwwroot/problems.json` | 程序集内嵌资源 |
| 偏好存储 | 浏览器 localStorage | `%LocalAppData%\OBS_Helper\prefs.json` |
| 密码 / API Key | 经 JS 桥转发给宿主 DPAPI | 进程内直接 DPAPI 加密 |
| 平台 | Windows + macOS | 仅 Windows |

## 功能

- **知识库** — 10 个分类、85 条问题，含现象 / 成因 / 分步解决 / 小贴士 / 相关问题，步骤可勾选且进度会记住
- **搜索** — 边打边搜，跨标题、现象、成因匹配
- **问我一下** — 用大白话描述现象，自动匹配最可能的问题
- **智能诊断** — 连上 OBS 后一键体检；可切换本地规则引擎或云端大模型（OpenAI 兼容接口 + function calling，云端失败自动回退本地）
- **日志分析** — 离线解析 OBS 日志，23 条规则 + 3 项量化比值；日志在分析前会先脱敏
- **直播搭建** — 从零到开播的 6 步流程 + 10 个主流平台的推流配置
- **OBS 控制台** — 场景切换、元素显隐、音频静音与音量、录制 / 推流 / 虚拟摄像头、实时统计
- **排障指引** — 通用排查思路速查手册
- **外观** — 浅色 / 深色 / 跟随系统，4 档字号，高对比与减少动画

## 隐私

所有数据只存在本机：

- 偏好（外观、收藏、步骤进度、连接设置）→ `%LocalAppData%\OBS_Helper\prefs.json`（明文 JSON）
- OBS 密码与 AI API Key → `%LocalAppData%\OBS_Helper\secrets.dat`（DPAPI 加密，绑定当前 Windows 用户，换机 / 换用户无法解开）

只有在你主动开启「云端诊断引擎」并发起诊断时才会联网，且请求前会先对日志脱敏。

## 构建

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download)。打安装包还需要 [Inno Setup 6](https://jrsoftware.org/isdl.php)。

```powershell
# 跑起来看看
dotnet run --project OBS_Helper.Wpf

# 出安装包 + 便携 zip -> PAKE\windows\
.\build.ps1

# 额外出一个单文件 exe
.\build.ps1 -SingleFile

# 没装 Inno Setup 时只出便携包
.\build.ps1 -SkipInstaller
```

产物落在 `PAKE\windows\`：

- `OBS_Helper_Setup_1.0.0.exe` — 安装包
- `OBS_Helper_Portable_1.0.0.zip` — 解压即用
- `OBS_Helper_Portable_1.0.0.exe` — 单文件（需 `-SingleFile`）

## 工程结构

```
OBS_Helper.Wpf/
  App.xaml(.cs)          应用入口、全局异常 -> 报错码弹窗
  MainWindow.xaml(.cs)   左侧导航 + 顶栏 + 页面容器，路由注册在这里
  AppServices.cs         组合根：所有服务的惰性单例，手工装配
  Navigation/            极简路由（路由名 -> 页面工厂，带缓存与后退栈）
  Views/                 11 个页面
  Controls/              共享控件与值转换器
  Themes/                Palette.xaml 调色板 + Controls.xaml 样式库
  Models/                知识库与 obs-websocket 协议的数据模型
  Services/
    Host/                HostBridge（DPAPI、日志读取、AI 转发）、LocalStore
    Obs/                 WebSocket 客户端、连接服务、日志分析、脱敏
    Ai/                  本地 / 云端诊断引擎与编排
    Markdown/            排障指引的 Markdown 解析
  Assets/                problems.json、troubleshooting.md（内嵌资源）、图标
```

换肤靠的是把调色板写进 `Application.Resources`，XAML 一律用 `DynamicResource` 引用，所以主题切换是整窗即时生效的。

## 许可

MIT，见 [LICENSE](LICENSE)。
