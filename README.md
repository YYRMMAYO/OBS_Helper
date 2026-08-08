# OBS 排障助手 · WPF 版（Windows）

面向直播新手的 OBS Studio 排障工具。**纯离线可用**：95 条问题的知识库、排障指引、日志分析规则全部内嵌在程序里，不联网也能查。连上 OBS 之后还能远程控制场景、录制与推流，并做一键体检。

这是原 Blazor WebAssembly + WebView2 版本的**原生 WPF 重构**（源码在 `NOBS/`），功能一比一对齐，但去掉了浏览器内核这一层，并在此基础上增加了后台遥控能力。

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

- **知识库** — 10 个分类、95 条问题，含现象 / 成因 / 分步解决 / 小贴士 / 相关问题，步骤可勾选且进度会记住
- **搜索** — 边打边搜，跨标题、现象、成因匹配
- **问我一下** — 用大白话描述现象，自动匹配最可能的问题
- **智能诊断** — 连上 OBS 后一键体检；三种引擎可选：本地规则引擎（默认，离线零成本）、**免费 AI（内置，智谱免费模型 免费通道，密钥加密内嵌、开箱即用，本地强制限频，适合低频使用）**、云端大模型（OpenAI 兼容接口 + function calling，接入自己的 API Key）；云端/免费失败自动回退本地；**结果可导出为 Markdown 报告**
- **日志分析** — 离线解析 OBS 日志，23 条规则 + 3 项量化比值；日志在分析前会先脱敏
- **直播搭建** — 从零到开播的 6 步流程 + 10 个主流平台的推流配置
- **OBS 控制台** — 场景切换、元素显隐、音频静音与音量、录制 / 推流 / 虚拟摄像头、实时统计、**定时停止录制/推流**、**一键打开录制目录**
- **系统托盘 + 通知** — 关闭窗口最小化到托盘，托盘菜单直接控制录制 / 推流 / 虚拟摄像头；录制 / 推流状态变化弹系统通知（可关）
- **迷你小窗** — 置顶小窗一键开始 / 停止录制与推流（含虚拟摄像头），可拖拽、记住位置；**托盘菜单、控制台页按钮**或全局热键（默认 Ctrl+Alt+M）随时呼出
- **全局热键** — 系统级快捷键（默认 Ctrl+Alt+R 录制 / Ctrl+Alt+S 推流 / Ctrl+Alt+C 虚拟摄像头 / Ctrl+Alt+M 小窗 / Ctrl+Alt+O 显示隐藏窗口，全部可在设置页改键或停用）
- **场景自动切换** — 按前台窗口标题自动切换场景，支持关键词 / 正则规则，规则表在设置页管理
- **系统监控** — CPU / 内存 / 网络上下行 / 磁盘空间实时曲线（近 2 分钟），与 OBS 渲染帧率、丢帧数据联动，磁盘空间不足时预警
- **场景模板** — 6 套内置直播间模板（游戏直播 / 竖屏带货 / 双人连麦 / 教学录屏 / 电台待机 / 开播三件套），连上 OBS 一键落地场景与来源（自动设置场景切换过渡与过渡时长），未连接时可导出为标准场景集合 JSON
- **OBS 配置管理** — 配置目录检测、备份 / 导出（ZIP，默认脱敏不含推流密钥）、导入（覆盖 / 合并，自动预备份）、轻度重置（新建干净配置集合）与彻底重置（恢复初始状态，强制自动备份）
- **排障指引** — 通用排查思路速查手册
- **外观** — 浅色 / 深色 / 跟随系统，4 档字号，高对比与减少动画

## 隐私

所有数据只存在本机：

- 偏好（外观、收藏、步骤进度、连接设置、**热键键位、自动切换规则、托盘行为**）→ `%LocalAppData%\OBS_Helper\prefs.json`（明文 JSON，均不含任何凭据）
- OBS 密码与 AI API Key → `%LocalAppData%\OBS_Helper\secrets.dat`（**双层加密**：值先经 AES-256-GCM（密钥由本机 MachineGuid 经 PBKDF2-SHA256 派生）加密，整个文件再经 DPAPI（绑定当前 Windows 用户 + 应用熵）加密；换机 / 换用户无法解开，即使文件被离线窃取也无法还原）

只有在你主动开启「免费 AI」或「云端诊断引擎」并发起诊断时才会联网，且请求前会先对日志脱敏。

### 免费 AI（内置）

内置免费 AI，**开箱即用**，两条通道可选、共用同一套本地限频：`智谱免费模型`（国内直连最稳；密钥在构建时由 `scripts/embed_free_ai_key.ps1` 加密内嵌进安装包，多重加密 + 不入 git 仓库，详见 `docs/` 审查报告）与 `Pollinations`（国外免 Key 公共通道，无需任何密钥，适合能直连国际网络的用户）。为了不给免费服务加压，**免费档由应用本地强制限频**：

- 每台机器每天最多 **20 次** 诊断（每次发起计 1 次，失败重试也计数），且两次请求间隔至少 **10 秒**；
- 计数保存在本机 `prefs.json`，跨重启生效，每天 0 点自动恢复；
- 额度用尽或免费服务不可用时，诊断自动回退到本地引擎并在结果中说明原因。

免费通道为单轮普通对话（不做知识库工具调用），响应受智谱免费档自身限流影响；需要多轮深度排查或更高频使用时，请切换到「云端大模型」接入你自己的 API（接口地址 + API Key 双层加密保存在本机，不会外泄）。

应用内「检查更新」会对比 GitHub 仓库的最新 tag（仅当有更高版本时才提示），提供两种下载方式：蓝奏云网盘（提取码 `YYKWY`）与应用内加载 GitHub 直接下载安装包；检查失败不影响正常使用。

OBS 配置备份 / 导出默认不包含推流密钥，密码与 Token 会自动脱敏。如需完整备份可手动勾选「包含推流密钥」。

## 构建

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download)。打安装包还需要 [Inno Setup 6](https://jrsoftware.org/isdl.php)。

```powershell
# 进到 WPF 工程目录（本仓库的 Windows 版代码都在 NOBS/ 下）
cd NOBS

# 跑起来看看
dotnet run --project OBS_Helper.Wpf

# 出安装包 + 便携 zip -> NOBS\PAKE\windows\（版本号取自 csproj 的 <Version>，并自动覆盖 Inno 脚本里的版本）
.\build.ps1

# 额外出一个单文件 exe
.\build.ps1 -SingleFile

# 没装 Inno Setup 时只出便携包
.\build.ps1 -SkipInstaller
```

产物落在 `NOBS\PAKE\windows\`：

- `OBS_Helper_Setup_1.8.0.exe` — 安装包
- `OBS_Helper_Portable_1.8.0.zip` — 解压即用
- `OBS_Helper_Portable_1.8.0.exe` — 单文件（需 `-SingleFile`）

## 工程结构

```
NOBS/
  OBS_Helper.Wpf/
    App.xaml(.cs)          应用入口、全局异常 -> 报错码弹窗
    MainWindow.xaml(.cs)   左侧导航 + 顶栏 + 页面容器，路由注册在这里
    AppServices.cs         组合根：所有服务的惰性单例，手工装配
    Navigation/            极简路由（路由名 -> 页面工厂，带缓存与后退栈）
    Views/                 13 个页面
    Controls/              共享控件与值转换器
    Themes/                Palette.xaml 调色板 + Controls.xaml 样式库
    Models/
      Obs/                 obs-websocket 协议模型
      ObsConfig/           OBS 配置模型
      Shell/               热键 / 自动切换 / 托盘行为设置模型
    Services/
      Host/                HostBridge（DPAPI、日志读取、AI 转发）、LocalStore
      Obs/                 WebSocket 客户端、连接服务、日志分析、脱敏
      ObsConfig/           OBS 配置定位、备份/导出/导入、重置、场景模板落地
      Ai/                  本地 / 免费 / 云端诊断引擎与编排（含免费档本地限频器）
      Shell/               系统托盘、全局热键、场景自动切换、定时器、系统监控
      Markdown/            排障指引的 Markdown 解析
    Assets/                problems.json、troubleshooting.md、scene_templates.json（内嵌资源）、图标
  build.ps1                Windows 构建与打包脚本
```

换肤靠的是把调色板写进 `Application.Resources`，XAML 一律用 `DynamicResource` 引用，所以主题切换是整窗即时生效的。

## 仓库结构

| 目录 | 说明 |
| --- | --- |
| `NOBS/` | **当前维护中的 Windows 原生 WPF 版**（本文档介绍的就是它） |
| `OBS_Helper.Client/` | 旧版共享前端（Blazor WASM），仅存档 |
| `OBS_Helper.Win/` | 旧版 Windows 桌面壳（WebView2），仅存档 |
| `OBS_Helper.Mac/` | macOS 桌面壳（Tauri v2），仅存档 |
| `docs/` | 旧版架构 / 报错码 / 依赖清单等文档，仅存档 |

## 许可

MIT，见 [LICENSE](LICENSE)。