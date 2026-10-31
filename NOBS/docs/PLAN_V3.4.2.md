# OBS 帮助助手 V3.4.2 技术开发规划

> **版本**：V3.4.2（patch / 界面信息架构整理版）
> **基线**：V3.4.1（`7aeb850`），`net10.0-windows` 主构建 + `net6.0-windows` Win7 兼容构建
> **文档日期**：2026-10-31
> **执行原则**：先改信息架构与文案，再改页面结构，最后统一验证与出包；不新增联网能力、不改 OBS 配置写入逻辑。
> **交付物**：源码、单元测试、README（中英）、RELEASE_NOTES、GitHub 代码推送与 Release 资产。

---

## 0. 结论速览

V3.4.2 不增加新功能，只做四件事：

1. **功能中心去掉“版本新增”标签**：左侧导航不再出现 `V3.4.x 新功能`，功能中心与首页不再用版本号暗示“哪些是本版新增”。
2. **设置页重做信息架构**：单列长滚动 → 左侧分类导航 + 右侧单页设置 + 顶部类别指引 + 设置搜索；把“后台与遥控”“排障指引”里混住的功能拆成独立设置页。
3. **工具箱重新编排**：17 个工具按“录制与文件 / 画面体检 / 音频体检 / 网络与推流 / 参数与隐私 / 冲突与版本 / 帮助与反馈”分组，增加分组筛选与分组目录；修复语言切换后“跳到小节”重复的缺陷。
4. **代码巡检与发布**：修复构建警告中的重复 `using`、可空引用警告、导航重复构建等问题；跑全量单测；更新 README / 发布说明；本地出包并推送 GitHub。

---

## 1. 背景与问题

| 编号 | 现象 | 根因 | 影响 |
|---|---|---|---|
| P1 | 左侧导航出现“V3.4.0 新功能”，功能中心页有“本版新增 · V3.4.0”，首页有“V3.4.0”大字 | 版本标签被当作功能分区 | 用户会误以为“只有这些是新功能”，下一版又会被暗示“新版本没有新增” |
| P2 | 设置页是一条 10 余张卡片的长滚动，语言、AI、外观、热键、录像、自动切换、数据、OBS 配置、关于混在一起 | 每次新增设置都往同一个 `StackPanel` 追加卡片 | 找不到设置；不敢改；页面对新用户没有“从哪里开始”的指引 |
| P3 | 工具箱顶部“跳到小节”出现两份相同条目 | `BuildSectionNav()` 只追加不清空，`OnNavigatedToAsync` 每次进入都会重建 | 目录失效、页面显得杂乱 |
| P4 | 工具箱 17 个工具没有分组，标题、卡片、说明的层级不一致 | 按开发时间顺序追加 | 没有章序，用户不知道先看哪一组 |
| P5 | 少量编译警告（重复 `using`、可空引用） | 历史迭代遗留 | 噪音掩盖真实问题 |

---

## 2. 目标与非目标

### 2.1 目标

- **G1** 所有“版本新增”小字从用户可见界面移除，保留“功能中心”这一稳定入口。
- **G2** 设置页按“基础设置 / 录制与自动化 / AI 与诊断 / 数据与存储 / 关于与帮助”五组、十二个设置页组织；同一时刻只显示一个设置页。
- **G3** 设置页提供搜索框、分组标题、每页一句话指引；保留全部既有设置项与处理逻辑。
- **G4** 工具箱按七个分组重新排序；分组筛选、分组目录、统一小节标题；目录不再重复。
- **G5** 全量单元测试通过，主工程双 TFM 编译无新增警告（现有可修警告清零）。
- **G6** README 中英同步、发布说明、版本号与安装脚本更新，产物可安装、可增量更新。

### 2.2 非目标

- 不新增手机联动 / 弹幕 / 字幕 / AI 等业务能力。
- 不改 OBS 配置写入、推流、录像等运行时行为。
- 不做设置项云同步、账号体系。
- 不重写整个设置页的业务代码（只做布局、分组、导航与文案）。

---

## 3. 功能中心：去版本标签（G1）

### 3.1 改动点

| 位置 | 现状 | 改后 |
|---|---|---|
| `MainWindow.xaml` 左侧导航 | 分隔线后 `TextBlock Loc.nav.newFeatures`（“V3.4.0 新功能”）+ 功能中心 + 手机联动 | 删除该 `TextBlock`，保留分隔线与“功能中心 / 手机联动” |
| `FeatureHubPage.xaml` 主推卡 | 右侧 `V3.4.0` 圆角徽标 | 删除徽标 |
| `FeatureHubPage.xaml` 列表 | “本版新增 · V3.4.0” + “常用功能”两段 | 单段“全部功能”，按目录顺序展示，手机联动置顶 |
| `FeatureHubPage.xaml.cs` | 按 `module.Version == CurrentVersion` 分流 | 全部加入同一个列表；不再渲染版本胶囊 |
| `HomePage.xaml` 主推卡 | 右侧 `V3.4.0` 大字 | 删除；标题去掉版本前缀 |
| 文案表 | `nav.newFeatures`、`feature.hub.new`、`home.hero.title` 等 | 删除版本前缀；新增 `feature.hub.all` |

### 3.2 文案调整

- `nav.newFeatures` 不再被引用，文案表可保留（避免破坏历史键），但 UI 不再渲染。
- `home.hero.title`：`V3.4.0 · 手机联动已上线` → `手机联动 · 把手机变成第二块屏幕`
- `home.hero.all`：`查看全部新功能` → `查看全部功能`
- `feature.hub.new`：不再使用；新增 `feature.hub.all` = `全部功能` / `All features`
- `feature.hub.note`：改为说明“功能按能力域展示；模块入口同时保留在首页主推卡与左侧导航”。

### 3.3 验收

- [ ] 左侧导航、功能中心、首页均不再出现 `V3.4.x 新功能/本版新增` 字样。
- [ ] 功能中心仍能看到 46 项能力、12 个模块，手机联动为唯一高亮模块。
- [ ] 英文界面无残留中文，中文界面无英文占位符。

---

## 4. 设置页：分类导航与指引（G2 / G3）

### 4.1 信息架构

左侧固定分类栏，右侧一次只显示一个设置页：

| 分组 | 设置页 | 卡片元素名 | 标题键 | 指引键 |
|---|---|---|---|---|
| 基础设置 | 语言与地区 | `CardLanguage` | `language.section` | `language.hint` |
| 基础设置 | 外观与无障碍 | `CardAppearance` | `settings.appearance.title` | `settings.appearance.desc`（新增） |
| 基础设置 | 后台与通知 | `CardShell` | `settings.shell.title` | `settings.shell.desc` |
| 录制与自动化 | 简单录像 | `CardRecord` | `settings.record.title` | `settings.record.desc`（新增） |
| 录制与自动化 | 场景自动切换 | `CardAutoSwitch` | `settings.autoSwitch.title` | `settings.autoSwitch.desc` |
| 录制与自动化 | 全局热键 | `CardHotkey`（从后台卡片拆出） | `settings.hotkey.title` | `settings.hotkey.desc` |
| AI 与诊断 | AI 诊断引擎 | `CardAi` | `settings.ai.title` | `settings.ai.desc` |
| AI 与诊断 | 排障指引 | `CardGuide` | `settings.guide.title` | `settings.guide.desc` |
| 数据与存储 | 本地数据 | `CardData`（从排障卡片拆出） | `settings.data.title` | `settings.data.desc`（新增） |
| 数据与存储 | OBS 配置管理 | `CardObsConfig` | `settings.obsconfig.title` | `settings.obsconfig.desc`（新增） |
| 关于与帮助 | 新手引导 | `CardOnboarding` | `settings.onboarding.title` | `settings.onboarding.desc` |
| 关于与帮助 | 关于与许可 | `CardAbout` | `settings.about.title` | `settings.about.desc`（新增） |

分组标题 / 描述：`settings.group.basics|recording|ai|data|about` + `.desc`。

### 4.2 页面布局

```
┌────────────────────────── 设置页（Grid 两列） ──────────────────────────┐
│ 左栏 248px（SurfaceBrush + 右分隔线）        │ 右栏（PageScroll）       │
│ ┌ 搜索框 ───────────────────────────────┐ │ ┌ 类别图标 + 分组名 + 标题 ┐ │
│ │ 搜索设置…                              │ │ │ 一句话指引（换行）        │ │
│ └────────────────────────────────────────┘ │ └──────────────────────────┘ │
│ 基础设置                                    │ 只显示当前设置页的卡片        │
│  ○ 语言与地区                               │                              │
│  ○ 外观与无障碍                             │  ┌ Card ───────────────────┐ │
│  ○ 后台与通知                               │  │ 既有控件（原样保留）      │ │
│ 录制与自动化                                │  └──────────────────────────┘ │
│  ○ 简单录像 …                                │                              │
└─────────────────────────────────────────────┴──────────────────────────────┘
```

- 左栏顶部搜索框：按“标题 + 指引 + 关键词”过滤设置页；无结果显示空态文案。
- 分类使用 `RadioButton` + 现有 `NavTabButton` 样式（选中品牌色、键盘焦点可见）。
- 右侧头部：品牌色图标底、分组名（小字）、设置页标题（H2）、指引（MutedText）。
- 切换设置页时右侧滚动到顶部；页面实例缓存，选中项跨导航保留。

### 4.3 代码结构

新增 `OBS_Helper.Wpf/Services/Shell/SettingsSections.cs`（纯 BCL，可被单测链接）：

```csharp
public sealed record SettingsGroup(string Id, string TitleKey, string DescKey);
public sealed record SettingsSection(
    string Id, string GroupId, string CardName, string TitleKey, string DescKey,
    string IconKey, string[] Keywords);

public static class SettingsSections
{
    public static readonly SettingsGroup[] Groups = ...;
    public static readonly SettingsSection[] All = ...;
    public static string? GroupTitleKeyOf(string groupId);
}
```

`SettingsPage.xaml.cs` 增加：

- `BuildSettingsNav()`：按分组生成导航；先 `Children.Clear()`，防止语言切换后重复。
- `ShowSettingsSection(string id)`：仅显示对应卡片，更新头部标题 / 指引 / 图标。
- `OnSettingsSearchChanged`：重建导航并显示空态。
- `OnLanguageChecked` 后重建导航与头部（当前页保持在原设置页）。
- `OnNavigatedToAsync` 刷新导航与头部，处理缓存页面的语言变更。

### 4.4 文案

新增中英各 10 条左右：分组标题 5、分组描述 5、设置页指引 5、搜索框 / 空态 2、功能中心 1。

### 4.5 验收

- [ ] 任一时刻右侧只显示一个设置页；所有原有控件与事件仍可正常使用。
- [ ] 搜索“热键 / 语言 / 更新 / API”等能定位到对应设置页；无结果有空态。
- [ ] 切换语言后左栏、头部、右栏文案同步，无重复导航项。
- [ ] 表格中 12 个卡片元素名都真实存在（单测校验）。
- [ ] 键盘 Tab / 方向键可完成分类切换，焦点可见。

---

## 5. 工具箱：分组、筛选与目录修复（G4）

### 5.1 分组与顺序

页面自上而下按分组重排，`ToolboxSections.Keys` 同步：

| # | 分组 | 小节（顺序） |
|---|---|---|
| 01 | 录制与文件 | 录像工具、磁盘写入基准 |
| 02 | 画面体检 | 色彩体检、黑屏专项体检、虚拟摄像头体检、编码顾问 |
| 03 | 音频体检 | 音频采样率体检、音频设备深度体检 |
| 04 | 网络与推流 | 推流带宽计算器、推流节点探测、浏览器源健康检查 |
| 05 | 参数与隐私 | 场景化参数处方、录前隐私清单、常用快捷键速查 |
| 06 | 冲突与版本 | 冲突软件扫描、OBS 新版本情报、获取官方 OBS |
| 07 | 帮助与反馈 | BUG 反馈 |

### 5.2 页面结构

```
工具箱引导卡（只读说明 + 分组筛选 chips：全部 / 01 录制与文件 / …）
跳到小节：
  录制与文件   [录像工具] [磁盘写入基准]
  画面体检     [色彩体检] [黑屏专项体检] …
  …
┌ 01 录制与文件 ─ 一句话分组说明 ──────────────┐
│  录像工具 / 卡片                               │
│  磁盘写入基准 / 卡片                           │
└────────────────────────────────────────────────┘
┌ 02 画面体检 … ────────────────────────────────┐
…
```

- 分组容器 `Group_section_<id>`：包含组头与组内小节；筛选时整组显示/隐藏。
- “跳到小节”按分组生成，组名以 `CaptionText` 分隔；`BuildSectionNav()` 首行 `Children.Clear()`。
- 小节标题继续使用 `Sec_*` 命名，保持 `ToolboxSectionsTests` 的“导航 ↔ XAML”一致性检查。
- 新增 `Sec_toolbox_feedback`，让反馈卡也出现在目录里。

### 5.3 代码结构

`ToolboxSections.cs` 扩展：

```csharp
public sealed record ToolboxGroup(string Id, string TitleKey, string DescKey);
public static readonly ToolboxGroup[] Groups;
public static string GroupOf(string sectionKey);
public static string GroupElementNameOf(string groupId); // "Group_section_" + id
```

`ToolboxPage.xaml.cs`：

- `BuildGroupChips()`：根据 `ToolboxSections.Groups` 生成筛选 chips。
- `ApplyGroupFilter(string? groupId)`：切换 `Group_section_*` 可见性并重建目录。
- 目录重建时先清空；语言切换时重建 chips / 目录 / 组头文案（组头用 `DynamicResource`，无需重建）。

### 5.4 文案

新增 `toolbox.group.<id>` / `.desc` 共 14 条，加 `toolbox.feedback`、`toolbox.filter.all`、`toolbox.filter.hint`。

### 5.5 验收

- [ ] 目录条目数量 = 18，无重复；语言切换 3 次后仍无重复。
- [ ] 分组筛选只影响显示，不影响工具状态与已填参数。
- [ ] 所有原有事件处理仍可用；`ToolboxSectionsTests` 全绿并新增分组覆盖校验。
- [ ] 中英界面组名、组说明、筛选 chips 完整。

---

## 6. 代码巡检与缺陷修复（G5）

按“编译警告 → 测试 → 关键交互路径”三层检查：

1. `AppServices.cs` 重复 `using`（`Services.Recording`、`Services.Audio`）删除。
2. `AppServices.cs:174` 可空引用警告：在 lambda 内改为 `_mobileLink.Value.RevokeDevice(deviceId)` 或使用非空局部变量，语义不变。
3. `ToolboxPage` 目录重复构建：`BuildSectionNav()` 先清空。
4. 设置页 / 工具箱新增导航的构造期取文案：统一在语言切换与 `OnNavigatedToAsync` 重建，避免缓存页面显示旧语言。
5. 检查新增 XAML 的 `DynamicResource` 键、`x:Name`、事件处理器均存在；运行 `dotnet build` 双 TFM + `dotnet test`。
6. 补充单测：
   - `SettingsSectionsTests`：12 个卡片名存在、分组合法、键在中文表存在、无重复。
   - `ToolboxSectionsTests`：新增分组断言（每节属于一个组、组不重复、组顺序与页面容器一致）。

---

## 7. 版本与文档

1. `OBS_Helper.Wpf.csproj`：`Version/FileVersion/AssemblyVersion` → `3.4.2`。
2. `OBS_Helper_Setup.iss` 默认版本 → `3.4.2`（build.ps1 仍以 csproj 为准覆盖）。
3. `docs/PLAN_V3.4.2.md`（本文）、`docs/V3.4.2_IMPLEMENTATION_STATUS.md`（完成后记录验证结果）。
4. `RELEASE_NOTES_v3.4.2.md`：按“界面整理 / 设置重构 / 工具箱重排 / 修复 / 验证”组织。
5. `README.md` / `README.en.md`：当前版本、亮点、测试数字、设置与工具箱说明同步。

---

## 8. 实施顺序（逐项验收）

| 序号 | 任务 | 产出 | 验收 |
|---|---|---|---|
| 01 | 功能中心去版本标签 | MainWindow / FeatureHub / HomePage / 文案 | 全界面无版本新增小字 |
| 02 | 新增 `SettingsSections` + 设置页布局 | 左栏导航 / 搜索 / 头部指引 | 12 页可切换、搜索可用 |
| 03 | 拆分“全局热键”“本地数据”卡片 | 两张独立卡片 | 事件、数据回填正常 |
| 04 | 工具箱分组重排 + 目录修复 | 7 组 / 18 节 / chips | 无重复、筛选正常 |
| 05 | 文案中英补齐 | 两张文案表 | 键集一致、无 CJK 残留 |
| 06 | 代码巡检与警告清理 | AppServices 等 | 双 TFM 构建无新增警告 |
| 07 | 单测补充并全量运行 | 测试工程 | 全绿 |
| 08 | README / 发布说明 / 版本号 | 文档与 csproj | 数字与版本一致 |
| 09 | 本地出包 | `PAKE/windows/*` | 安装包 / 便携包 / 增量包生成 |
| 10 | 提交与推送 GitHub、创建 Release | commit / tag / release | 远端代码与资产可见 |

---

## 9. 风险与回退

| 风险 | 影响 | 缓解 |
|---|---|---|
| 设置页 XAML 重排破坏控件名 | 编译失败或事件失效 | 保留全部 `x:Name` 与事件绑定；单测校验卡片名；逐页编译验证 |
| 卡片拆分后状态回填遗漏 | 热键 / 数据摘要显示旧值 | `SyncControls` / `RefreshDataSummary` 名称不变，仅改变位置 |
| 工具箱重排导致目录顺序断言失败 | 测试红 | `ToolboxSections.Keys` 与页面顺序同步更新，测试先跑 |
| 语言切换后动态生成文案不更新 | 混排 | 导航 / chips / 目录统一在语言切换回调重建 |
| 发布资产缺文件 | 自动更新失败 | 沿用 `build.ps1` 清单与增量包流程，发布前核对资产列表 |

---

## 10. 发布步骤

```powershell
# 1) 验证
dotnet test OBS_Helper.Wpf.Tests/OBS_Helper.Wpf.Tests.csproj -c Debug
dotnet build OBS_Helper.slnx -c Release

# 2) 出包（含主构建 + Win7 兼容构建 + 安装包 + 便携包 + 增量包）
.\build.ps1

# 3) 提交与推送
git add <本次改动文件>
git commit -m "feat(V3.4.2): 功能中心去版本标签、设置分类重构与工具箱重排"
git push origin master

# 4) 打标签并发布
git tag V3.4.2
git push origin V3.4.2
gh release create V3.4.2 --title "V3.4.2 ..." --notes-file RELEASE_NOTES_v3.4.2.md <资产>
```

发布资产至少包含：主安装包、Win7 安装包、主便携包、Win7 便携包、增量包、知识库资产；沿用 V3.4.1 的命名与校验清单。
