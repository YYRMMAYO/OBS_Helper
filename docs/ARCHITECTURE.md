# OBS 排障助手 · 架构说明

一个跨平台（Windows + macOS）的 OBS 直播排障助手：用同一套 **Blazor WebAssembly** 站点承载全部排障内容与交互，
再分别套上平台原生的轻量桌面壳（Windows 用 WebView2，macOS 用 Tauri/WKWebView）。
逻辑与界面只写一次，双端共享。

## 整体结构

```
OBS_Helper（仓库根）
├── OBS_Helper.Client/        # 共享前端：Blazor WebAssembly 站点（所有排障逻辑在这里）
│   ├── Pages/                # 路由页面：首页 / 分类 / 问题详情 / 搜索 / 助手 / 搭建向导 / 诊断
│   ├── Layout/               # 主布局（导航 / 外壳）
│   ├── Components/           # 复用组件：ProblemCard（问题卡片，被 5 个页面共用）
│   ├── Models/               # 数据模型：Problem / Step / Link
│   ├── Services/             # ProblemService（加载 problems.json）、BookmarkService、AssistantService
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
   - 站点完全本地、离线运行，**不主动发起任何网络请求**；所有外链（官方文档）由 WebView2 / 系统浏览器代开，且经 `IsSafeUrl` 校验仅放行 `http/https`。
   - macOS 端：Tauri 配置 `security.csp` 作为纵深防御；窗口禁用 devtools/远程调试，并对导航做白名单限制；capabilities 仅保留 `core:default`，不向前端暴露任何 IPC 命令。
   - Windows 端：WebView2 关闭 DevTools / 默认右键菜单 / WebMessage，渲染进程崩溃自动重载；外链通过 `NewWindowRequested` 交由系统默认浏览器打开。
   - 桌面壳与 `problems.json` 同源，内容可信，不存在用户可控输入注入路径（数据文件为打包内容，非运行时远程加载）。

## 本地开发

- 前端：`dotnet run --project OBS_Helper.Client`（Blazor Dev Server）
- Windows：`pwsh ./build.ps1` 一键构建 + 打包到 `PAKE/windows`
- macOS：在 macOS 上 `bash OBS_Helper.Mac/src-tauri/build-mac.sh`
- 验证：构建后 `python scripts/cdp_smoke.py --root "<发布目录>/wwwroot"`
