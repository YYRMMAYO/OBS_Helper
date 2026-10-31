# 实现状态（X1031Y2 / X1031 / V3.5.0）

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

## 1.1 X1031Y2（2026-10-31）

| 需求 | 计划文档 | 状态 | 代码落点 | 验证 |
| --- | --- | --- | --- | --- |
| 同步 V3.5.2 及此前全部内容（X 图标除外） | 本文 §1 | ✅ | 同一份 `OBS_Helper.Wpf` 源码；X 构建仅替换图标资源 | 构建 + 单测 |
| X 系列专属字标图标（exe / 安装包 / 窗口 / 托盘 / 快捷方式） | 11 | ✅ | `appicon_x.ico`、`XSeriesIcon`、`OBS_Helper_Setup.iss`、`build.ps1` | 构建 + 产物图标提取 |
| 插件卡片 / 粘贴仓库应用内拉取 | 11 | ✅ | `PluginsPage.xaml(.cs)`、`PluginInstallService`、`GitHubSearchService` | 构建 + 资源检查 |
| 下载前网络 / 加速器确认 | 11 | ✅ | `PluginsPage.ConfirmNetwork`、`plugin.network.*` 文案 | 资源检查 |
| 开源依赖一键配置 | 11 | ✅ | `DependencyInstallCore`、`DependencyInstallService`、`DependencyCore.GitHubRepo` | 13 项单测 |
| 本机插件卸载 / 迁移 / 接管 | 11 | ✅ | `LocalPluginManageCore`、`LocalPluginManageService`、`PluginsPage` | 20 项单测 |
| 卸载 / 回滚路径护栏 | 11 | ✅ | `PluginInstallCore.IsSafeManagedDirectory`、`PluginInstallService` | 单测 |
| sidecar / 增量包解压上限 | 11 | ✅ | `SidecarManager.SafeExtract`、`IncrementalUpdateService.SafeExtractToDirectory` | 构建 + 全量单测 |
| 问题库精简与更新 | 11 | ✅ | `Assets/problems*.json`（208 条 / v2.3 / 14 分类） | 中英对等 42 项单测 |
| README / 发布说明 / CODEBASE | 11 | ✅ | `README*.md`、`NOBS/README*.md`、`RELEASE_NOTES_x1031.md`、`docs/CODEBASE.md` | check_docs |

### X1031Y2（2026-10-31，三轮）

| 轮次 | 构建 | 单测 | 资源 / 无障碍 / 文档 | 真实插件拉取 |
| --- | --- | --- | --- | --- |
| 1 | `dotnet build OBS_Helper.slnx -c Release` 成功 | 1790 / 1790 | 全部通过 | 待执行（第 3 轮） |
| 2 | `dotnet publish` net10 / net6 `1031.2.0` 成功，FileVersion = 1031.2.0.0 | 1790 / 1790 | 全部通过 | 待执行（第 3 轮） |
| 3 | `dotnet build OBS_Helper.slnx -c Release` 成功 | 1790 / 1790 | 全部通过 | `exeldro/obs-move-transition` 3.2.1：资产下载 1.69 MB、SHA-256 匹配、压缩包 / DLL 布局校验通过 |

- 真实拉取发现并修复了一个真实缺陷：`ValidateArchive` 把 zip 里的目录条目当成「无扩展名文件」拒绝，导致带 `bin/`、`data/` 目录的真实插件包全部安装失败；新增 `ValidateArchive_AcceptsDirectoryEntries` 回归测试。
- 资源检查：49 个 XAML、2756 处引用、3502 条中英文案键、14 个分类语义色全部可解析。
- 文档检查：34 项通过（CODEBASE 未收录 108 个历史文件为非阻塞提示）。

## 1.2 X1031Y3（2026-10-31）

| 需求 | 计划文档 | 状态 | 代码落点 | 验证 |
| --- | --- | --- | --- | --- |
| 修复 GitHub Release 资产 302 后最终域名被拒导致的插件/依赖下载失败 | 12 | ✅ | `PluginInstallCore`、`DependencyInstallCore`、`SidecarCore` 白名单；`PluginInstallService`、`DependencyInstallService` 日志 | 1799 单测 + 真实插件安装 + 下载探测 |
| 下载失败可诊断 | 12 | ✅ | `PluginInstallService`、`DependencyInstallService`（host / HTTP 状态 / 体积 / 异常日志） | 单测 + 日志字段审查 |
| 功能中心全模块宣传 vs 实现审查 | 12 | ✅ | `12_X1031Y3_FEATURE_CENTER_READINESS.md` | 代码级初审；未验证项已标注 |
| 依赖一键安装 / whisper 模型 / 摄像头诊断 / 防火墙 | 12 | ✅ | `DependencyProvisioner`、`WhisperModelService`、`MobileLinkPage`、`FirewallHelper` | 构建 + 单测 + 真实插件 / 模型探测 |
| 功能中心环境准备 / 状态位、配对上限、Q05、C10/E06 落盘 | 12 | ✅ | `FeatureHubPage`、`FeatureCatalog.KnownStatus`、`AutoArchiveService`、`InsightsCore`、`InteractionPage` | 构建 + 单测 |
| Y4 并入：WebRTC 视频监看、历史录像索引 | 12 | ✅ | `PhoneCameraCore`、`MediaServerManager`、`RecordingLibraryService` | 构建 + MobileCoreTests |
| Y4 边界：LLM / AI 导播 / 主题市场 / 联机协作 / 多平台统计 / 浏览器摄像头 | 12 | ◐ 指引 | 开发指引 §13 | 未接入；不宣传为已完成 |

### X1031Y3（2026-10-31，三轮）

| 轮次 | 构建 | 单测 | 资源 / 无障碍 / 文档 | 真实下载 |
| --- | --- | --- | --- | --- |
| 1 | `dotnet test OBS_Helper.slnx -c Release` 成功 | 1799 / 1799 | check_resources / check_accessibility / check_docs 通过（CODEBASE 108 项非阻塞提示） | — |
| 2 | net10 / net6 `publish 1031.2.0` 成功 | 1799 / 1799 | `OBS_SELFTEST=1` 36 PASS / 0 FAIL | — |
| 3 | 真实服务探针 | 1799 / 1799 | 文档/资源检查通过 | 插件 `exeldro/obs-move-transition` 3.2.1 安装成功（1.69 MB，SHA-256 匹配）；MediaMTX 依赖下载到 49% 因本机网络中止，302 → `release-assets.githubusercontent.com` 后写盘已验证 |

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
