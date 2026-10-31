# 图标体系（Tabler 风格）与应用图标

## 1. 界面图标

来源与许可：图标几何按 [Tabler Icons](https://tabler.io/icons)（MIT）的 24×24 / 2px 描边风格重绘，
以 WPF `StreamGeometry` 存入 `Themes/Icons.xaml`，不引入字体文件或网络依赖。

### 1.1 新增 / 替换清单

| 资源键 | 语义 | 使用位置 |
| --- | --- | --- |
| `IconGeomLayoutGrid` | 功能中心 / 模块总览 | 一级导航「功能中心」 |
| `IconGeomDeviceMobile` | 手机联动 | 一级导航「手机联动」、功能中心手机模块 |
| `IconGeomTool` | 工具箱 | 一级导航「工具箱」 |
| `IconGeomBroadcast` | 直播推流 | 功能中心「直播互动 / 画质与分发」 |
| `IconGeomVideo` | 录制 | 功能中心「开播与录制」 |
| `IconGeomPackage` | 依赖 / 插件 | 功能中心「虚拟直播与插件」「开源依赖」 |
| `IconGeomSparkles` | 智能与复盘 | 功能中心「智能与复盘」 |
| `IconGeomRobot` | 自动化 | 功能中心「自动化与生态」 |

### 1.2 原则

- 一个入口只用一个语义图标，禁止「功能中心 = 模板」「工具箱 = 加号」这类复用；
- 导航图标使用描边；状态点 / 录制点使用实心；
- 图标不承担唯一信息：始终有文字标签与可访问名称；
- 新增图标必须同时补 `check_resources.py` 可解析的引用。

### 1.3 V3.5.1 设置详情图标修复

V3.5.1 修掉了一类**渲染方式**错误：`Icons.xaml` 里的几何全部是描边路径，
但设置页详情头部与功能中心模块卡曾用 `Fill` 渲染，圆 / 方框会被填成实心色块
（「语言与地区」因此只剩一个圆点）。现在两处统一改为 `Stroke`：

```xml
<Path Stroke="{DynamicResource BrandBrush}" StrokeThickness="1.8"
      StrokeStartLineCap="Round" StrokeEndLineCap="Round" StrokeLineJoin="Round" />
```

新增 / 替换的语义图标（Tabler Icons，MIT，路径取自官方 outline SVG）：

| 资源键 | 语义 | 设置分区 |
| --- | --- | --- |
| `IconGeomLanguage` | 地球 | 语言与地区 |
| `IconGeomPalette` | 调色板 | 外观与无障碍 |
| `IconGeomTray` | 托盘 / 收件箱 | 后台与遥控 |
| `IconGeomVideo` | 摄像机 | 录制 |
| `IconGeomArrowsSwitch` | 双向箭头 | 场景自动切换 |
| `IconGeomKeyboard` | 键盘 | 全局热键 |
| `IconGeomCpu` | 芯片 | AI 诊断引擎 |
| `IconGeomDiagnostic` | 脉冲线 | 排障指引 |
| `IconGeomDatabase` | 数据库 | 本地数据 |
| `IconGeomArchive` | 归档 | OBS 配置管理 |
| `IconGeomCompass` | 罗盘 | 新手引导 |
| `IconGeomInfo` | 信息圆 | 关于 |

验收：`SettingsSectionsTests.EverySectionIconExists` 校验每个 `IconKey` 都在
`Icons.xaml` 中存在；本地用 WPF `RenderTargetBitmap` 渲染全部图标，确认均为描边轮廓。

## 2. X1031 应用图标

- 设计：黑色圆角方块（`#0B0F14`），白色 `OBS` 大字标，下方 `helper` 小字标；
  左侧一个与 `O` 重叠的青绿色圆弧，形成「重叠元素」；无渐变、无阴影、无小字噪点。
- 小尺寸（16/24/32）自动简化：只保留 `O` 与一条重叠弧线，确保任务栏可辨认。
- 文件：`Assets/appicon_x.ico`，含 16/24/32/48/64/128/256 七档。
- 生成脚本：`scripts/gen_x_appicon.py`（Pillow，可复现）。
- V3.5.0 **不使用**该图标：`csproj` 与托盘、主窗口继续使用 `appicon.ico`；
  X 构建通过 `ReleaseChannel=X` 选择 `appicon_x.ico`。
- 不修改 `AssemblyName` / 安装目录 / 数据目录，保证升级链路。

## 3. 验收

- `ProductNameTests.AppIcon_ContainsAllShellSizes` 对新图标同样成立；
- X 构建的资源管理器、任务栏、托盘、安装包图标一致；
- V3.5.0 构建的图标与 V3.4.2 一致。
