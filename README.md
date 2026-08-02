# OBS 排障助手 (OBS Helper)

复杂问题，分步解决。一个覆盖 **黑屏、卡顿、音画不同步、推流失败、直播间搭建** 等 OBS 高频难题的
跨平台桌面助手，支持 Windows 与 macOS。

- 一份 **Blazor WebAssembly** 站点承载全部排障内容与交互
- **Windows** 用 WebView2 桌面壳，**macOS** 用 Tauri v2 桌面壳
- 每个问题都给出「症状 → 原因 → 分步解决 → 官方文档跳转」，并可勾选完成进度、收藏
- 内置统一 **报错码** 体系（见 `docs/ERROR_CODES.md`）

---

## 一、功能概览

- 10 大分类、85+ 条排障方案，覆盖黑屏 / 编码过载 / 卡顿掉帧 / 音画不同步 / 音频 / 推流失败 / 直播间搭建 / 录制 / 基础配置 / 崩溃兼容
- 每个方案附 **官方文档 / 参考链接**（OBS 官网、日志分析器、macOS 权限指南、黑屏修复教程等），一键跳转
- 分步勾选 + 收藏，进度本地保存
- 搜索、分类浏览、智能助手（离线问答）
- 双端原生桌面体验，离线可用

---

## 二、Windows 安装指引

### 方式 A：安装包（推荐）
1. 从发布页 / CI 制品下载 `OBS_Helper_Setup_1.0.0.exe`（位于 `PAKE/windows/`）。
2. 双击运行，按向导安装到 `C:\Program Files\OBS 排障助手`。
3. 首次启动若提示「WebView2 初始化失败 (OBS101)」：
   - 大多数 Win10/11 已自带 WebView2；如缺失，到微软官网安装
     [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) 后重试。

### 方式 B：便携版
- 解压 `OBS_Helper_Portable_1.0.0.zip`，直接运行其中的 `OBS_Helper.exe`。
- 注意：`OBS_Helper.exe` 必须与 `wwwroot` 文件夹放在同一目录（否则报 `OBS102`）。

### 系统要求
- Windows 10 / 11（64 位）
- 已安装或可接受自动获取 WebView2 Runtime
- 安装包为自包含发布，无需预先安装 .NET

---

## 三、macOS 安装指引

1. 从发布页 / CI 制品下载 `OBS 排障助手-1.0.0.dmg`（位于 `PAKE/macos/`）。
2. 打开 `.dmg`，把 `OBS 排障助手.app` 拖到「应用程序」。
3. 首次打开若被 Gatekeeper 拦截（CI 构建的包未做 Apple 签名）：
   - 右键 App → 「打开」，在弹窗中再次确认；或
   - 系统设置 → 隐私与安全性 → 允许该开发者 App。
4. 若使用屏幕录制 / 麦克风相关功能，按 App 内指引到
   **系统设置 → 隐私与安全性** 授予 OBS 相应权限（App 本身不采集屏幕/麦克风，仅展示设置步骤）。

### 系统要求
- macOS 10.15 (Catalina) 及以上
- 需自行签名/公证后才能无损分发（仓库 CI 产出为未签名包，适合自测与内部分发）

---

## 四、从源码构建

### 准备
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Windows 端：Inno Setup 6（生成安装包）
- macOS 端：[Rust 工具链](https://www.rust-lang.org/) + `@tauri-apps/cli`（`npm i -g @tauri-apps/cli`）

### Windows
```powershell
pwsh ./build.ps1 -Configuration Release -Runtime win-x64
# 产物：PAKE/windows/OBS_Helper_Setup_1.0.0.exe 与 OBS_Helper_Portable_1.0.0.zip
```

### macOS（需在 Mac 上执行）
```bash
bash OBS_Helper.Mac/src-tauri/build-mac.sh
# 产物：PAKE/macos/*.dmg 与 *.app
```
> 可选代码签名 / 公证：设置环境变量 `MAC_SIGN_IDENTITY`（Developer ID Application）与
> `MAC_NOTARY_KEYCHAIN_PROFILE`（notarytool 钥匙串配置文件）后脚本会自动深度签名并提交公证；
> 未设置则产出未签名包（适合自测 / 内部分发）。

### 验证运行（无头冒烟测试）
```bash
pip install websocket-client
python scripts/cdp_smoke.py --root "OBS_Helper.Win/bin/Release/net10.0-windows10.0.19041.0/win-x64/publish/wwwroot"
# 通过条件：#app 成功渲染且无未捕获异常
```

---

## 五、目录与文档

| 路径 | 说明 |
| --- | --- |
| `OBS_Helper.Client/` | 共享前端（Blazor WASM），全部排障逻辑 |
| `OBS_Helper.Win/` | Windows 桌面壳（WebView2）+ Inno 安装脚本 |
| `OBS_Helper.Mac/src-tauri/` | macOS 桌面壳（Tauri v2） |
| `content/problems.json` | 主数据源（问题 + 官方链接） |
| `docs/ERROR_CODES.md` | 报错码 → 现象 → 解决方案 对照表 |
| `docs/LIBRARIES.md` | 依赖 / 第三方库清单（版本 + 许可证） |
| `docs/ARCHITECTURE.md` | 架构与构建说明 |
| `PAKE/` | 统一打包输出目录（git 忽略） |

---

## 六、许可证

本项目以 **MIT 许可证** 发布，详见 `LICENSE`。
第三方运行库与工具的许可证见 `docs/LIBRARIES.md`；OBS 相关文档版权归 OBS Project 所有。
