# 实现状态（X1031 / V3.5.0）

> 最终数字以三轮本地自检为准：2026-10-31。

## 1. 计划 vs 实现

| 需求 | 计划文档 | 状态 | 代码落点 | 验证 |
| --- | --- | --- | --- | --- |
| 1 图标图文相符 | 03 | ✅ | `Themes/Icons.xaml`、`FeatureCatalog.cs`、`MainWindow.xaml` | 资源检查 |
| 2 GitHub 加速引导 | 04 | ✅ | `Views/PluginsPage.xaml(.cs)`、`GitHubReachability.cs` | 单测 + 加速器探测 |
| 3 已安装插件显示 | 04 | ✅ | `PluginManagedListCore.cs`、`PluginsPage.xaml.cs` | 5 项单测 |
| 4 手机当摄像头 | 05 | ✅ | `sidecars.json`（mediamtx v1.21.3 + SHA-256）、`MobileServices.cs`、`MobileLinkPage` | 单测 + 启动解耦 |
| 5 虚拟主播模板 | 06 | ✅ | `scene_templates*.json`（22 套 / 10 套 VTuber）、`RoomOverlayAssets.cs` | 坐标与结构单测 |
| 6 小窗控制重构 | 07 | ✅ | `MiniControlWindow.xaml(.cs)` | 自检 mini + 手动 |
| 7 手机改提词稿 | 07 | ✅ | `MobileCore.cs`、`MobileServices.cs`、`TeleprompterService.cs`、`MobilePageAssets.cs` | 单测 |
| 8 指引更新 | 08 | ✅ | `Assets/troubleshooting*.md` | check_docs |
| 9 新应用图标（仅 X） | 03 | ✅ | `appicon_x.ico`、`gen_x_appicon.py`、X 提交 | 7 档尺寸单测 |
| 10 全局背景 / UI 重构 | 02 | ✅ | `MainWindow.xaml`、`Palette.xaml`、`AppearanceService.cs`、`SettingsPage`、`SplashWindow` | 构建 + 自检 |
| 毛玻璃 / 字体 / 启动动画 | 02 | ✅ | `WallpaperService.cs`（模糊壁纸底）、`AppearanceService.cs`（字体/圆角/透明度）、`SplashWindow` | 构建 + 自检 |
| 手机连接页 / 功能中心 | 08 | ✅ | `MobileLinkPage`、`FeatureHubPage` | 构建 + 自检 |
| X/V 通道分离（日期 + Y 序号） | 09 | ✅ | `ReleaseChannel.cs`、`UpdateService.cs`、`build.ps1`、CI | 42 项单测 |
| X 更新弹窗直显 Release 正文 | 09 | ✅ | `UpdateDialog.xaml(.cs)`、`UpdateService.cs`（Release body） | 单测 + 手动 |

## 2. 三轮自检记录

### X1031 重置（日期码 + Y 序号，本地出包）

| 检查 | 结果 |
| --- | --- |
| 单元测试 | 1750 / 1750 通过 |
| 构建 | net10.0-windows / net6.0-windows 均成功 |
| 资源 / 无障碍 / 文档 | check_resources / check_accessibility / check_docs 全部通过 |
| 无界面自检 | 36 PASS / 0 FAIL |

### V3.5.0（含全面 UI 重构，历史基线）

| 轮次 | 构建 | 单测 | 资源 / 无障碍 / 文档 | 无界面自检 |
| --- | --- | --- | --- | --- |
| 1 | 成功 | 1729 / 1729 | 全部通过 | 36 PASS / 0 FAIL |
| 2 | 成功 | 1729 / 1729 | 全部通过 | 36 PASS / 0 FAIL |
| 3 | 成功 | 1729 / 1729 | 全部通过 | 36 PASS / 0 FAIL |

- 资源检查：48 个 XAML、2699 处引用全部可解析；文案键 3388（中英键集一致）。
- 文档检查：csproj / iss 版本 3.5.0 一致；README / CODEBASE 数字一致。
- 无界面自检：路由 28 项 + 引导 / 小窗 / 文案 / 语言重放 / 回退提示 / D7 护栏 / 双协议分支，共 36 项。

## 3. 已知边界

- 手机摄像头依赖用户自行安装支持 SRT/RTMP 的手机推流 App；局域网 HTTP 页面无法直接调用摄像头。
- 媒体服务仍需 Windows 10+；Win7 只提供 USB 采集卡引导。
- 未做真机手机配对 / 真机摄像头录制；相关结论以单测、资源检查与桌面端冒烟为准。
- 虚拟主播模板的 OBS 落地以 JSON 校验与坐标断言为准，未在真机 OBS 逐套截图。
- 毛玻璃用「壁纸缩小 + 高斯模糊」实现，不是系统级 Acrylic / Mica；字体只引用系统已安装字体，不随包分发。
- X1031 不含 V3.5.0 的全面 UI 重构、手机连接页重排与功能中心体验优化。
