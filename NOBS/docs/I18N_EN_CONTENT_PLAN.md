# V2.9.3 计划：随包离线内容的英译（中英对等）

> **状态：已于 V2.9.3 执行完毕。** 本文档当时作为「作业规范」写给执行者（人 / AI），
> 内容仍然有效，可直接当作本次英译的口径说明阅读。
>
> 执行结果与本文档的差异（均为**有意的偏差**，已同步到发布说明与审校记录）：
>
> 1. §3.3 说 `plugins[].name` 不译 —— 但本软件自己的 Dock 插件名是中文
>    （`OBS帮助助手（Dock 版）`），而 §5 又要求英文资产零汉字。最终口径：
>    **已经是拉丁字母的名字原样保留，含汉字的才译**（`OBS Helper (Dock)`）。
> 2. §3.2 的 `settings` 一节没有覆盖 `settings.text` / `font.face`：
>    前者是会被写进 OBS 的画面文案（译了），后者用字体的官方拉丁名
>    （`微软雅黑` → `Microsoft YaHei`，Windows/OBS 都能解析）。
> 3. §3.1 的 severity / level 取值除了 `DataValues` 认识的那几个，还有
>    `Rare` / `Intermittent` / `Basic` / `Beginner` / `Common tip` / `Niche`
>    与 `Fallback` / `Note` —— 与既有 `severity.*` / `level.*` 文案键逐一对齐，
>    未进入 `DataValues` 语义分支的取值保持「Other」，与中文侧行为一致。
> 4. §6 的「人工三遍审校」执行成了**三轮分视角审校**并如实标注证据边界
>    （见 [`reviews/REVIEW_2026-10-05-v2.9.3.md`](reviews/REVIEW_2026-10-05-v2.9.3.md)）；
>    其中「对着英文版 OBS 核菜单路径」这一条**没有做**，已列为已知边界。
>
> 落地方式与本文档的 §4 一致：`Localization/ContentAssets` 统一装载、
> 两条热更新通道按语言分发、`build.ps1` 导出中英四份资产。

> 面向执行者（人 / AI）的作业指导书。V2.9.2 已完成**界面与所有代码内文案**的中英双语；
> 本文件描述剩下的一块：让**随包的离线内容**也具备英文版本。
>
> 现状（V2.9.2 结束时）：
> - 界面、托盘、通知、报错、体检结论：中英双份，默认中文，可即时切换；
> - 安装包：语言选择页（默认中文），选定结果写 `{app}\language.ini` 作为首启默认语言；
> - **内容数据（problems / scene_templates / plugins / troubleshooting）：仅中文**，
>   英文界面下这四处仍显示中文。

---

## 1. 为什么单独一个版本

| 维度 | 界面文案（已完成） | 内容数据（本计划） |
|---|---|---|
| 体量 | 1670 条键 × 2 语言 | `problems.json` 实测 **57,717 个汉字 / 212 条目**（含标题、症状、成因、分步标题+详情、小贴士、参考链接），另有 `plugins.json` 57 条、`scene_templates.json`、`troubleshooting.md`，合计约 10 万字符英文产出 |
| 出错代价 | 单个按钮文字不对 | **整份数据必须译完才有意义**：译一半的知识库不可交付（搜索、分类计数、`related` 关联、`severity` 排序都会显得半残） |
| 依赖链 | 无 | 需同时改造**两条热更新通道**（GitHub raw 主通道 + Release 资产兜底）按语言取资产，否则英文用户拿不到「提交即生效」 |
| 验收 | 单测可全覆盖 | 需**逐条对读**（中文 212 条 ↔ 英文 212 条），且要有机器可查的形状校验兜底 |

结论：把内容英译独立成 V2.9.3，先让「界面双语」这一版干净地发出去。

---

## 2. 交付物（Definition of Done）

1. 新增并列资产（**不改动现有中文文件名**，保证老通道零回归）：
   ```
   OBS_Helper.Wpf/Assets/problems.en-US.json
   OBS_Helper.Wpf/Assets/scene_templates.en-US.json
   OBS_Helper.Wpf/Assets/plugins.en-US.json
   OBS_Helper.Wpf/Assets/troubleshooting.en-US.md
   ```
2. **英文界面下这四处的显示内容全部为英文**；切回中文仍是原来那份，逐字不变。
3. 语言切换（含**热更新之后**）都能取到对应语言的内容，不需要重启。
4. 热更新通道按语言分发（见 §4）。
5. 机器可查的验收：§5 的「必做测试」全绿 + 人工「三遍审校」记录（见 §6）。

---

## 3. 数据约定（译的时候必须守）

**只译「展示文案」，不译「逻辑键」。** 逐字段清单：

### 3.1 `problems.json`

| 字段 | 译不译 | 说明 |
|---|---|---|
| `id` / `category` / 分类 `id` | **不译** | 被 `related`、`problem/<id>` 路由、`ProblemId` 关联引用，改了会断链 |
| `categories[].icon` / `semantic` | **不译** | emoji 与语义色键（`red`/`orange`/…），后者与 `Palette.xaml` 的 `Semantic{key}Brush` 一一对应，`check_resources.py` 会校验 |
| `categories[].title` / `description` | 译 | |
| `problems[].title` | 译 | 同时用于搜索，保持关键词密度（症状词要留在标题里） |
| `problems[].platforms` | **不译** | 取值是 `Windows` / `macOS` / `Linux`，本来就是英文 |
| `problems[].severity` | 译，但**取值必须落在两条约定内** | 中文取值：`严重`/`常见`/`一般`/`进阶`/`罕见`/`偶发`/`入门`/`常用技巧`/`小众`；对应的英文取值由 `Localization/DataValues.ClassifySeverity` 决定，**必须与它其中的字符串逐一对应**（当前：`Critical`/`Common`/`Occasional`/`Advanced`），否则卡片颜色会退化 |
| `problems[].symptoms[]` / `causes[]` / `tips[]` | 译 | 数组顺序保持一致（中文第 N 条 ↔ 英文第 N 条） |
| `problems[].steps[].title` / `.detail` | 译 | `steps[].level` 见下 |
| `steps[].level` | 译，取值同样受约束 | 中文：`基础`/`进阶`/`兜底`/`提示`；英文须与 `DataValues.IsAdvancedLevel`（`Advanced`）以及 `level.*` 文案键一致 |
| `links[].title` | 译 | `links[].url` **不译** |
| `version` / `updated` | **不译** | 版本比较依赖它们 |

### 3.2 `scene_templates.json`

| 字段 | 译不译 | 说明 |
|---|---|---|
| `id` / `icon` | **不译** | |
| `title` / `summary` / `notes` | 译 | 卡片文案 |
| `transition` | 译，且**别名表要认识** | 该值经 `SceneTemplateService.PickTransitionName` 映射到 OBS 的过渡名；别名表已经同时认 `淡入淡出`/`Fade`、`直接切换`/`Cut`，改值时必须确认命中 |
| `scenes[].name` / `sources[].name` | **译（注意一致性）** | 这些名字会被**写进 OBS**：同一模板内 `shared: true` 的来源靠 `name` 跨场景复用，同一来源在多处出现时**必须译成同一个名字**，否则会重复创建设备、占用摄像头 |
| `sources[].inputKind` / `fallbackKinds` | **不译** | OBS 来源类型 id |
| `sources[].placeholder.hint` | 译 | |
| `requiresPlugins[].id` | **不译** | 指向插件目录 id |
| `requiresPlugins[].reason` | 译 | |
| `canvas.*` / `hotkey` / `settings` 里的数值与 OBS 键名 | **不译** | |

### 3.3 `plugins.json`

| 字段 | 译不译 | 说明 |
|---|---|---|
| `id` / `url` / `repo` / `dlls[]` / `maintain` | **不译** | `repo` 必须能通过 `PluginCatalogCore.NormalizeRepo` 归一化成 `owner/repo`（有单测）；`dlls` 必须是小写主干（有单测）；`maintain` 只允许 `active`/`slow` |
| `name` | **不译** | 插件的官方项目名，社区通称，译了反而找不到 |
| `category` | **不译** | 指向 `categories[].key` |
| `categories[].label` / `icon` | `label` 译，`icon` 不译 | |
| `desc` / `badge` / `maintainNote` / `riskNote` / `aiCostCpu` / `aiCostMem` | 译 | `badge` 的取值要与 `DataValues.IsHotBadge`（`热门`/`Popular`）以及文案键 `plugin.badge.hot` 对齐 |
| `version` / `updated` / `note` | `version`/`updated` **不译**（版本比较用）；`note` 是给人看的说明，可译 | |

### 3.4 `troubleshooting.md`

整份翻译，但：

- 标题层级（`#` 数量）**保持不变** —— 页面右侧目录由 H2 生成（`GuidePage`），层级变了目录就乱了；
- 链接语法 `[文字](url)` 保留，**url 不动**；`MarkdownView` 对链接有白名单（http/https/mailto），别引入其它 scheme；
- 代码块围栏 ``` 成对保留。

---

## 4. 装载与热更新通道改造

### 4.1 装载（读取侧）

- 给资源名加上语言维度：`OBS_Helper.Wpf.Assets.problems.json` / `…problems.en-US.json`（其余同理）。
- 抽一个统一入口，例如 `Localization/ContentAssets.cs`：
  ```
  problems.json（zh-Hans）  ← 现有文件名，保持不变
  problems.en-US.json      ← 新增
  解析次序：当前语言的外部缓存文件 → 当前语言的随包内嵌 → 中文随包内嵌（最后兜底，且要写日志）
  ```
  ⚠️ 兜底到中文时**必须写一条 WARN 日志**：否则英文用户会「安静地看到中文内容」，正是 V2.9.1
  修 raw 404 时踩过的那个坑（静默失败能潜伏很久）。
- `ProblemService` / `PluginCatalogService` / `SceneTemplateService` / 指引读取处改为走该入口，
  并在语言切换时清缓存重载（`Reload()` 已经存在，语言切换时调用即可）。
- `.csproj` 为四个英文资产补 `EmbeddedResource`（`LogicalName` 与常量一一对应）。

### 4.2 热更新（两条通道都要按语言分发）

| 通道 | 现状 | 需要改成 |
|---|---|---|
| GitHub raw 主通道 | `KnowledgeBaseUrls.RawProblems` / `RawPlugins` 指向 `NOBS/OBS_Helper.Wpf/Assets/problems.json` | 增加带语言后缀的地址（`.en-US.json`），并同步 `KnowledgeBaseUrlsTests` 的「本机源码树落地校验」 |
| Release 资产兜底 | 匹配 `OBS_Helper_Knowledge_*.json` / `OBS_Helper_Plugins_*.json` | 英文资产名建议 `OBS_Helper_Knowledge_<ver>.en-US.json`；**匹配器必须先把英文资产排除掉**（否则中文匹配器会把英文资产当自己的，`FindAssetUrl` 取到错误文件） |
| 本地缓存文件 | `%LocalAppData%\OBS_Helper\` 下的单份文件 | 按语言分文件（zh 沿用现有文件名以兼容已下载的缓存） |

- `build.ps1` 需要一并产出并上传英文资产（`PAKE/windows/` 下同名 `.en-US.json`）。
- `KnowledgeBaseUpdater` 目前会在启动时静默刷新；改造后**至少刷新当前语言那一份**，
  否则用户切到英文后会拿到中文的热更新内容。

---

## 5. 必做测试（机器可查的部分）

在 `OBS_Helper.Wpf.Tests` 里补：

1. **形状对等**（最关键，防止「译漏一条」）：
   逐语言解析后断言 `problems` 条数、`id` 序列、每条的 `symptoms/causes/steps/tips` 数组长度、
   `links[].url`、`category` 全部与中文侧一致；分类 id/title 一一对应。
2. **取值合法**：英文 `severity` / `steps[].level` 取值必须落在 `DataValues` 认识的那几个值内；
   插件 `maintain` ∈ {active, slow}、`dlls` 全小写且无 `.dll`、`repo` 可归一化（中文侧已有这些用例，
   英文侧照抄一份）。
3. **无残留中文**：英文资产的字符串值里不出现汉字
   （与 V2.9.2 的 `StringsTests.EnglishTable_ContainsNoCjk` 同款做法；同理中文资产里不出现整句英文）。
4. **场景模板引用的插件 id 都存在**于英文插件目录里。
5. **热更新地址**：`KnowledgeBaseUrls` 的中英地址都是 https、形状正确，且**仓库相对路径在本机源码树里真实存在**
   （沿用现有 `KnowledgeBaseUrlsTests.RawUrls_MatchFilesInSourceTree` 的写法）。
6. **Release 资产匹配不串台**：给定一份同时含中英资产的资产列表，中文匹配器只能挑到中文那份（反之亦然）。

## 6. 人工三遍审校（机器查不出来的部分）

1. **逐条对读**：按 `id` 顺序中英对照通读一遍，重点看
   ① 数字/单位/路径是否照抄错（`48kHz`、`1920x1080`、`%ProgramData%\obs-studio\plugins`）；
   ② OBS 里的菜单路径是否与**英文版 OBS 的实际界面文案**一致（`设置 → 输出 → 录像` ⇒
   `Settings → Output → Recording`），这一条只能对着英文版 OBS 核；
   ③ 症状词是否还在（搜索命中率）。
2. **反向抽查**：从英文侧随机抽 20 条，把英文读一遍、猜它对应哪条中文，再核对 id（防止「张冠李戴」式错行）。
3. **实机走查**：应用内切到英文，走一遍「首页分类 → 搜索 → 问题详情（含复制全文）→ 助手 → 日志分析 →
   模板落地（确认写进 OBS 的场景/来源名是英文且 shared 来源没有重复创建）→ 插件广场 → 排障指引」，
   再切回中文确认逐字未变；最后**重装一次**并确认 `language.ini` 仍然只影响首启默认值。

## 7. 风险与注意

- **不要把中文文件的文件名改掉**（`.en-US.json` 是新文件）：现有中文热更新地址、Release 资产名、
  已下载到用户本机的缓存文件都依赖 `problems.json` 这个名字。
- **`severity` / `level` / `badge` 是跨语言的展示值**，取值必须与 `DataValues` 认识的那几个字符串对齐；
  拿不准时以「新增一个取值 + 同步改 `DataValues` + 补单测」的方式扩，而不是随手写个近义词。
- 场景模板里 `sources[].name` 的**一致性**是唯一会导致「功能坏了」的翻译错误（重复创建设备），
  审校第一遍就要专门核这一项。
- 四个英文资产会让内嵌体积增加约一倍（`problems.json` 446KB → 约 +450KB），
  对自包含发布（约 150MB）可忽略；若在意，可考虑只内嵌中文、英文走「首次联网下载」，
  但那会让英文用户失去开箱离线可用的承诺 —— 不建议。
