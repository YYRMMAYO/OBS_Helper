# 实现状态（X1031 / V3.5.0）

> 本文件随开发过程更新；最终数字以三轮自检为准。

## 1. 计划 vs 实现

| 需求 | 计划文档 | 状态 | 代码落点 | 验证 |
| --- | --- | --- | --- | --- |
| 1 图标图文相符 | 03 | 进行中 | `Themes/Icons.xaml`、`FeatureCatalog.cs`、`MainWindow.xaml` | 资源检查 |
| 2 GitHub 加速引导 | 04 | 进行中 | `Views/PluginsPage.xaml(.cs)` | 手动 + 单测 |
| 3 已安装插件显示 | 04 | 进行中 | `PluginsPage.xaml.cs`、`PluginManagedListCore` | 单测 |
| 4 手机当摄像头 | 05 | 进行中 | `sidecars.json`、`MobileServices.cs`、`MobileLinkPage` | 单测 + 手动 |
| 5 虚拟主播模板 | 06 | 进行中 | `scene_templates.json`、`RoomOverlayAssets.cs` | 单测 |
| 6 小窗控制重构 | 07 | 进行中 | `MiniControlWindow.xaml(.cs)` | 手动 |
| 7 手机改提词稿 | 07 | 进行中 | `MobileCore.cs`、`MobileServices.cs`、`MobilePageAssets.cs` | 单测 |
| 8 指引更新 | 08 | 进行中 | `Assets/troubleshooting*.md` | check_docs |
| 9 新应用图标（仅 X） | 03 | 待开始 | `appicon_x.ico`、`gen_x_appicon.py`、csproj | 图标尺寸测试 |
| 10 全局背景 UI | 02 | 进行中 | `MainWindow.xaml`、`AppearanceService.cs`、`Palette.xaml` | 手动 |
| 手机连接页 / 功能中心 | 08 | 进行中 | `MobileLinkPage`、`FeatureHubPage` | 手动 |
| X/V 通道分离 | 09 | 待开始 | `ReleaseChannel.cs`、`UpdateService.cs`、CI | 单测 |

## 2. 三轮自检记录

### 第 1 轮

- 构建：
- 单测：
- 资源 / 无障碍 / 文档：
- 自检：

### 第 2 轮

- 构建：
- 单测：
- 资源 / 无障碍 / 文档：
- 自检：

### 第 3 轮

- 构建：
- 单测：
- 资源 / 无障碍 / 文档：
- 自检：

## 3. 已知边界

- 手机摄像头依赖用户自行安装支持 SRT/RTMP 的手机推流 App；局域网 HTTP 页面无法直接调用摄像头。
- 媒体服务仍需 Windows 10+；Win7 只提供 USB 采集卡引导。
- 未做真机手机配对 / 真机摄像头录制；相关结论以单测、资源检查与桌面端冒烟为准。
- 虚拟主播模板的 OBS 落地以 JSON 校验与坐标断言为准，未在真机 OBS 逐套截图。
