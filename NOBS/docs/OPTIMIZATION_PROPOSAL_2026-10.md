# OBS帮助助手 · 优化与新增功能清单（2026-10 评估）

> **评估对象**：V2.9.6（`OBS_Helper.Wpf`，`net10.0-windows` + `net6.0-windows` 双构建）
>
> **方法**：① 通读全部设计与发布文档；② 逐文件读主源码；③ 与 obs-websocket v5 的「已用 / 未用」能力逐一比对；
> ④ 四路并行深审（**性能与架构** / **界面与文案** / **可靠性·安全·测试** / **文档与代码一致性**），
> 本文只收录经复核的结论（最高严重度的几条由 Lead 逐行复核过）；
> ⑤ 与本仓既有四份缺口清单去重（`DEV_GUIDE_GAPS_2026-08` / `OPTIMIZATION_V2.7.0` / `FEATURES_USERVIEW_2026-10-05` /
> `REVIEW_2026-10-05-v2.9.4` 的 S1~S7）。
>
> **证据边界（如实说明）**：
> 1. 每条都给了 `文件:行`；标 **推测** 的是工程判断而非实测；
> 2. **本会话无法执行** `dotnet build` / `dotnet test` / `OBS_SELFTEST=1`（本机 pwsh 恒以 `0xC0000142` 退出，
>    是会话环境问题而非仓库问题）。所以「630 项测试全绿」沿用发布说明，**未复现**；
>    「界面上看得到什么」这类结论基于 XAML / 文案 / 代码推导，建议真机复核；
> 3. obs-websocket 能力清单依据官方 v5 协议（字段名用第三方绑定
>    [obws 0.11.4 `Stats`](https://docs.rs/obws/0.11.4/obws/responses/general/struct.Stats.html) 交叉核对），
>    **未**对真实 OBS 实例发过请求。
>
> **本文不是施工图**：每条只到「问题 / 证据 / 影响 / 改法 / 体积」，落地前仍需按本仓惯例补设计稿与验收口径。

---

## 0. 一句话结论

**先别急着加功能。**这个产品的功能面已经相当完整（200+ 条知识库、三通道 AI、9 张只读体检卡、
录制守护、实时日志预警、简单录像、插件生态、中英双语、双 TFM、630 项单测），
但这次深审在三处「用户看不见的地方」找到了**会丢用户数据**的问题：

1. **文件事务回滚会把唯一的恢复副本删掉**（`FileTx.cs:73-82`）—— 导入/重置中途失败时，
   用户全部场景集合与 profile 可能永久丢失；
2. **应用内更新链路没有任何完整性校验**，且自举替换中途失败不回滚（`UpdateService.cs:533-547`、
   `UpdaterBootstrap.cs:109-140`）—— 装上去的是不是我们要发布的包，客户端无法判断；
3. **写 OBS 配置的路径没走护栏，且 profile 目录名可被根化 / 经由导入的备份包注入**
   （`RecordingEnvService.cs:86-90`）—— 写入目标可以逃出 OBS 配置目录。

这三条正好落在「本仓自己反复承认没有端到端验证」的区域里（`RELEASE_NOTES_v2.9.4` 已知边界、
`REVIEW_2026-10-05-v2.9.4.md:134-143`）：**风险最集中、测试最空白的地方完全重叠**。

功能侧真正值得做的，是把已有的零件**串起来**并**往下游走一步**：
「简单开播」（推流侧还没有对称物）、「回放缓存一键存片」（obs-websocket 现成能力，全仓 0 处调用）、
「本次会话复盘」（实时告警已能命中，却只闪一条托盘通知）。

---

## 1. 结论速览

### A 类 · 数据与可靠性（建议在加任何新功能之前清掉）

| # | 项目 | 严重度 | 成本 |
|---|---|---|---|
| A1 | 文件事务回滚失败仍删除唯一恢复副本 | **高（数据丢失）** | S |
| A2 | 更新链路无签名/哈希校验；自举替换失败不回滚 | **高（供应链 + 变砖）** | M |
| A3 | `profileDir` 可根化/可经导入包注入 → 越界写盘；写 `basic.ini` 全不过护栏 | **高** | S–M |
| A4 | 备份静默跳过文件却报「已备份」；回收站无恢复入口；解压绕过事务 | 中高 | M |
| A5 | 机密读失败被当空存储 → 下一次写清空全部密钥 | 中高 | S |
| A6 | 连接失败与从未连接在界面上不可区分 | 中高（排障产品头号任务静默失败） | S |
| A7 | 热键 / 小窗 / 后台刷新失败全静默（含实时预警自身失效） | 中高 | S |
| A8 | `LocalStore` 损坏即静默清空（连带丢掉「跨会话可回滚」记录） | 中 | S |
| A9 | 重连额度被双扣，8 次退避实际约 4 次 | 中 | S |
| A10 | 回滚路径是唯一「无备份、无读回校验」的写盘路径 | 中 | S |
| A11 | 危险确认框默认焦点在「确认」，彻底重置是连续两次 | 中 | S |
| A12 | 崩溃时日志可能丢失；危险操作零日志；无诊断包导出 | 中 | M |

### B 类 · 安全与隐私一致性

| # | 项目 | 严重度 | 成本 |
|---|---|---|---|
| B1 | `LogSanitizer` 漏点：SRT `passphrase`、公网 IPv6、标准 Base64、`Bearer <jwt>` | 中高 | S |
| B2 | 导出包「脱敏失败」时原样打包 `service.json`（含流密钥）却仍标记已脱敏 | 中高 | S |
| B3 | AI 出站没有面向用户的明示（发给谁 / 是否留存 / 日志正文） | 中 | S |
| B4 | 出站白名单口径不一致：`HostBridge.OpenExternalAsync` 只校验 scheme，热更新内容里的任意链接都能开 | 中 | S |
| B5 | 发布物无 Authenticode 签名（插件线 `PRNOBS/build.ps1:130-137` 反而有 signtool） | 中 | M |

### C 类 · 体验优化

| # | 项目 | 价值 | 成本 |
|---|---|---|---|
| C1 | **已经采到却没显示的数据**：画布/输出/帧率、推流健康度（拥塞/丢帧/重连）、音频电平表 | 高 | S–M |
| C2 | 迷你小窗只回答「能不能按」，不回答「录得怎么样」 | 高 | S |
| C3 | 「剩余可录」用写死的预设码率估算 | 中高 | S |
| C4 | 搜索/助手只做子串匹配（拼音首字母、同义词、字段权重全无） | 中高 | M |
| C5 | 诊断页 8 项自检清单手填且**不落盘**（而问题页步骤勾选是落盘的） | 中高 | S |
| C6 | 工具箱 18 个分区一页到底；首页把「简单录像」压在分类之前 | 中 | S |
| C7 | Toast 2.5 秒自动消失、无关闭无历史，错误也走它 | 中 | S |
| C8 | 文档与代码事实漂移 13 处（含服务数 45 vs 28/20、自检 19 vs 22） | 中 | S |
| C9 | 无障碍：`AutomationProperties` 全仓零覆盖、分段控件无焦点指示、高 DPI 最小宽度 | 中 | M |

### D 类 · 新功能

| # | 项目 | 价值 | 成本 |
|---|---|---|---|
| D1 | **回放缓存一键存片**（obs-websocket 现成，全仓 0 处调用） | **最高** | M |
| D2 | **简单开播**（与简单录像对称：自检→场景→录制→推流→复盘） | 高 | L |
| D3 | 录制档案 + 收尾流水线（登记 / 重命名 / 批量转 MP4 / 剪辑打点） | 高 | M |
| D4 | 自检清单自动回填 + 「开播前 60 秒体检」一键处方 | 高 | M |
| D5 | 本次会话复盘报告（实时命中归集 + 结束一键分析） | 中高 | M |
| D6 | 「我的模板」：把当前场景集合反向存成模板（现有模板只有单向） | 中高 | L |
| D7 | 其余未用的 obs-websocket 能力：滤镜 / 媒体控制 / 配置集快切 / 画面缩略图 / 音画同步偏移 | 中高 | 每条 S–M |
| D8 | 知识库贡献闭环（本地收集 + 用户手动提交，不自动上传） | 中 | M |
| D9 | 繁体中文 / 日语；插件目录加「与 OBS 版本兼容性」字段 | 中 | S–M |

### E 类 · 工程与可观测性

| # | 项目 | 价值 | 成本 |
|---|---|---|---|
| E1 | CI 补三件事：无界面自检、增量包校验、tag 自动发版 | 高 | M |
| E2 | **测试工程结构性问题**：服务层根本没被编译进测试程序集 | 高 | L |
| E3 | 可观测性：崩溃 Flush、日志版本头、危险操作日志、错误码补全 | 中高 | S–M |
| E4 | 文档一致性巡检脚本（`check_docs.py`） | 中 | S |

### F 类 · 性能与资源（未实测，均为代码级推定）

| # | 项目 | 价值 | 成本 |
|---|---|---|---|
| F1 | **进过一次插件广场，系统监控计时器就永久常驻**（1 秒一次，直到退出进程） | 高（真缺陷） | S |
| F2 | 每次重连泄漏一个 `CancellationTokenSource` | 高（真缺陷） | S |
| F3 | 首页首屏在 UI 线程串了 3~4 轮完整 OBS 环境探测 | 高 | M |
| F4 | 日志分析：正则全部未编译、8MB 全文留 3 份副本 | 中高 | M |
| F5 | 语言切换不彻底（部分文案被冻结在首次加载） | 中高（与 V2.9.2 承诺冲突） | M |
| F6 | 插件广场进页面对 57 个仓库各打一次 GitHub API；搜索无防抖 | 中 | S–M |
| F7 | 页面缓存无上限（17 个页面实例常驻，日志页还握着 8MB 文本） | 中 | M |
| F8 | 零散 UI 线程同步 IO（含 `Thread.Sleep(60)×3`） | 中 | S |
| F9 | 检索无索引（212 条每次全量重建） | 中 | S |
| F10 | 打包：WinForms ≈24MB、便携包用 Deflate、带了 pdb | 中 | M |

---

## 2. A 类：数据与可靠性

### A1. 文件事务回滚失败时，仍删除唯一的恢复副本 ★ 最高优先

- **证据**：`Services/ObsConfig/FileTx.cs:70-84`
  ```csharp
  foreach (var (src, trash) in ((IEnumerable<(string, string)>)_moves).Reverse())
  {
      try { CopyItem(trash, src); } catch (Exception) { /* 恢复失败留待人工处理 */ }
      try { SafeDelete(trash); } catch (Exception) { }   // ← 恢复失败也照样删
  }
  ...
  SafeDelete(_txDir);                                    // ← 连事务目录一起清
  ```
  而 `StageMove`（`FileTx.cs:52-54`）已经**把原文件从原位删掉了** —— 回收副本就是唯一副本。
- **触发**：`ObsResetService.FullResetAsync`（`ObsResetService.cs:124-180`）或
  `ObsBackupService.ExtractIntoConfig`（`ObsBackupService.cs:331-371`）中途失败/取消，
  而此时 `src` 被 OBS、杀软或资源管理器占用 → `CopyItem` 抛异常 → 副本被删。
- **后果**：用户的全部场景集合 / profile 永久丢失。注释写着「留待人工处理」，下一行就把要留的东西删了。
- **改法**：① 恢复失败时**不得**删除该副本；② 收集 `(src, trash)` 失败清单并返回给调用方；
  ③ 只有全部恢复成功才 `SafeDelete(_txDir)`；④ `Dispose` 里的 `catch {}`（`:86-94`）改为落 `FileLogger`。
- **体积**：S。**这条几乎零成本、收益最大**，建议无条件先修。

### A2. 更新链路没有完整性校验；自举替换失败不回滚

- **证据**：
  - `UpdateService.cs:533-547` 的 `IsValidPeExecutable` 只读**前两个字节**判 `MZ`；
    `:463-501` 全程无 SHA-256、无长度比对、无 Authenticode；
  - 增量包有逐文件 SHA-256（`IncrementalUpdateService.cs:154-179`），但校验的是
    「zip 内容 == zip 内自带的 `update_manifest.json`」——**清单本身没有签名，没有任何外部锚点**；
  - `UpdaterBootstrap.cs:109-140`：先把自身 exe 改名 `*.old`，再逐文件复制；任一文件失败只 `TryRestoreSelf`
    （仅当 exe 不存在时恢复），**已复制进去的新 DLL 不回退**；`CopyWithRenameSwap`（`:195-208`）
    先 `File.Move(dst, old)` 再 `Copy`，Copy 失败则该 DLL 彻底缺失，且 `CleanupResidue`（`:246-253`）
    下次启动会**无条件删除** `*.old`；
  - 对比：插件线 `PRNOBS/build.ps1:130-137,185-187` 反而有 signtool 签名步骤，主 `build.ps1` 全仓无签名。
- **后果**：① HTTPS 中间人 / Release 资产被替换 / 蓝奏云第三方镜像被换 → 任意 exe 被当作更新安装，
  且自举路径会用 `runas` 提权（`IncrementalUpdateService.cs:219-222`）；
  ② 更新中途磁盘满或文件被占用 → 主程序与依赖版本错配，启动即崩且无自动修复。
- **改法**：① 客户端校验 `SHA256SUMS`（或 `.sha256` 资产）并校验 Authenticode 签名者，二者其一不过即拒绝；
  ② 发布流程给 exe / 安装包签名（自签证书也好过空白）；
  ③ 自举改为「先全部 `*.old` 就位 → 全量复制 → 全部成功才写 DONE」，失败按记录逆序回滚；
  ④ `CleanupResidue` 删 `*.old` 前先确认新版本可启动。
- **体积**：M。

### A3. `profileDir` 可被根化 / 经导入包注入 —— 写盘目标能逃出 OBS 配置目录

- **证据**：`Services/ObsConfig/RecordingEnvService.cs:86-90`
  ```csharp
  ini.TryGetValue("basic.profiledir", out var profileDir);
  if (!string.IsNullOrWhiteSpace(profileDir))
      basicIniPath = Path.Combine(loc.ConfigDir, "basic", "profiles", profileDir!, "basic.ini");
  ```
  `Path.Combine` 遇到**根化**的段会丢弃前面的前缀：`profileDir = C:\Windows\System32` →
  写 `C:\Windows\System32\basic.ini`；`profileDir = ..\..\..\..` → 逃出配置目录。
- **可达链**：`global.ini` 可以**由「导入备份包」写入**（`ObsBackupService.cs:359-362,426-430`，
  Overwrite 模式直接落 `config/global.ini`）→ 之后任何一次「一键部署录制环境 / 简单录像 / 回滚」
  都会写到那个路径。这三处写入**全部不过 `ObsSafePath`**：
  `RecordingEnvService.cs:284-285`（apply）、`:370`（rollback）、`SimpleRecordingService.cs:686-687`、
  `SceneTemplateService.cs:414`。
  全仓 `ObsSafePath.*` 只有 5 个调用点（`ObsBackupService.cs:523` + `ObsResetService.cs:131/140/152/162`）。
- **另一头也不安全**：`ObsSafePath.cs:44-48` 的闸只看**目录名字面**是不是 `obs-studio`，
  而配置目录允许用户手动指定（`ObsPathService.cs:71-79`）—— 手动指到非该名的目录 → 彻底重置必被拒（功能不可用）；
  手动指到任意一个名为 `obs-studio` 的目录 → 护栏放行。两头都不成立。
- **这是 V2.9.4 审校已记录的 S1，至今未修**；V2.9.4 把「写 `basic.ini`」变成首页一次点击就能触发，风险面比立项时更大。
- **改法**：① 所有写 OBS 配置的入口统一 `AssertWritable`；② `profileDir` 先 `Path.GetFileName` 净化，
  再断言解析结果落在 `ConfigDir` 之内；③ 手动指定目录时**记录并锁定**该 root，护栏按记录值校验而不是目录名。
- **体积**：S–M。补一条单测：注入 `..\..\..\..` 与根化路径，断言抛异常且**文件未被创建**。

### A4. 备份「报成功」但静默跳过文件；回收站承诺没有入口；解压绕过事务

- **证据**：
  - `ObsBackupService.cs:587-602`：`AddFileRaw` 捕获异常后只 `Debug.WriteLine`（Release 下等于没有痕迹），
    调用方 `:122-126`、`:154` 无条件 `entryCount++`，函数返回 zip 路径，UI 据此展示「已备份」。
    而代码注释自己承认「OBS 运行中 `global.ini` / `service.json` 被独占是常态」；
  - 文案承诺可找回：`StringTableZhHans.cs:1632`「已移入回收站（可在应用备份目录找回）」，
    但 `ObsPathService.cs:47-63` 的 `CleanupTrash` **只删不列**，`FileTx.RecoveryPath`（`FileTx.cs:33`）
    全仓无读取方，没有任何浏览/恢复入口；
  - `ObsBackupService.cs:325-374`：FileTx 只用于 `StageExistingConfig`，真正写文件的
    `ExtractRaw`（`:521-526`）/ `ExtractSceneCollectionMerge`（`:528-553`）/ `ExtractProfileFile`（`:555-583`）
    都是直接写，**不进事务**；`FileTx.StageCreate`（`FileTx.cs:58-64`）全仓无调用者。
    另外 `ScanZip` 把白名单外扩展名记为「已跳过」（`:481`）并展示给用户，但解压分发（`:343-367`）
    **不再校验扩展名**，这些文件照样落盘。
- **后果**：① 导入/重置前的「安全网」可能缺关键文件，而用户以为已备份 —— 而 A1 的回收副本又不可靠；
  ② 回滚后新解压的额外文件残留；③ `.lnk` / `.inf` / `.url` 不在黑名单（`ObsBackupService.cs:42-45` 只挡可执行类）。
- **改法**：① 跳过清单必须返回并在 UI 明确列出，`global.ini` / `service.json` 失败应视为**备份失败**；
  ② 加「回收站浏览 / 恢复」入口，或把文案改成如实描述；强杀残留的 `tx_*` 应在下次启动被扫描并提示；
  ③ 解压写入统一走事务；解压前按 scan 结果过滤。
- **体积**：M。

### A5. 机密存储：读失败被当成「空存储」，下一次写就把其它密钥全清掉

- **证据**：`Services/Host/HostBridge.cs:228-264` 的 `LoadSecrets` 在 `catch (Exception)` 里返回**空字典**
  （文件被占用 / 瞬时 IO 错误也走这里）；`:318-327` 的 `SetSecretAsync` 在这个字典上加一项后
  **整份覆盖写回**；`:361-379` 的 `DeleteSecretAsync` 同理。
- **触发**：`secrets.dat` 被杀软扫描/占用导致 `ReadAllBytes` 抛异常，此时用户改任意一个密钥。
- **后果**：OBS WebSocket 密码、AI API Key 一并静默消失，而界面提示「已保存」。
- **改法**：区分「文件不存在」与「读取失败」，后者必须返回失败而不是空字典；写入前校验读回条数；
  顺带把 `Set`失败（`:330-333`）与 `Delete` 失败（`:371-374`）的返回值接出来 ——
  `ObsSettingsService.cs:95-110` 目前**完全忽略**返回值。
- **体积**：S。

### A6. 连接失败与「从未连接」在界面上不可区分 ★ 排障产品最不该有的静默

- **证据**：`ObsConnectionService.cs:150-155` 的通用失败分支把状态置为 **`Disconnected`**（不是 `Failed`），
  原因字符串进了 `LastError`；而 `Controls/ConnectionBadge.xaml.cs:50-52` **只有 `Failed` 态**才把它放进 ToolTip；
  `Views/ConsolePage.xaml.cs:157-160` 也只在控制台页内渲染。
- **后果**：全产品最高频的任务（连不上 OBS）失败时，首页、诊断页、托盘全无提示；
  文案表里已经写好的三条排查（`StringTableZhHans.cs:1596`「① OBS 已启动 ② 勾选开启 WebSocket 服务器 ③ 端口一致」）
  根本没机会露面。
- **改法**：连接失败也用 `Failed` 态（或新增「曾失败」标记），徽章常驻原因摘要、点击展开；
  首页/诊断页补一条 Toast；参考已有的 `LogAlertThrottle` 做节流。
- **体积**：S。

### A7. 热键 / 小窗 / 后台刷新失败全静默

- **证据**：
  - `Services/Shell/GlobalHotkeyService.cs:203-206`：`catch (Exception) { }` + 注释「热键动作失败静默」；
  - `Views/MiniControlWindow.xaml.cs:66-67,73-74,79-81`：同样吞掉；
  - `ObsConnectionService.cs:657-661` 的 `FireAndForget` 不落任何日志（5 处调用：`:512,542,551,566,582`），
    而同仓库 `Services/TaskExtensions.cs:10-20` 明确要求「异常一律落 FileLogger」——**同一类行为两种口径**；
  - `Services/Shell/LogTailerService.cs:172-179`：**实时预警自身失效也无感知**。
- **后果**：未连 OBS 时按录制热键毫无反应，用户以为热键没生效；连接着但状态长期刷新失败时，
  界面显示陈旧数据且日志里没有任何线索。用户形成「没消息 = 没问题」的错误心智。
- **改法**：统一走带日志的 `FireAndForget`；面向用户失败给一句原因（复用 `TrayService`/`ToastService` + 节流）。
- **体积**：S。

### A8~A10（各 S，一笔带过但都值得顺手修）

| # | 问题 | 证据 | 改法 |
|---|---|---|---|
| A8 | `LocalStore` 损坏即静默清空 | `Services/Host/LocalStore.cs:38-51`（Load 失败→空字典）、`:53-79`（Flush 异常全吞、**无一处 FileLogger**） | Load 失败保留 `*.corrupt` 备份 + 落日志；「跨会话待回滚记录」独立文件存放（现在它和 prefs 同生共死，`SimpleRecordingService.cs:557-575`） |
| A9 | 重连额度被双扣：失败路径 `catch → ScheduleReconnect()`，同时接收循环结束又广播 `Closed`，`OnClosed` 只对 `Failed` 早退 | `ObsConnectionService.cs:150-155`、`:173-183`、`:189`；`ObsWebSocketClient.cs:307-313` | 用「每次尝试一个代次号」去重，或 `OnClosed` 仅在 `Connected` 态触发重连。`MaxAttempts=8`（`ObsReconnectPolicy.cs:15`）实际约 4 次（~60s）就放弃 |
| A10 | 回滚路径无备份、无读回校验 | `RecordingEnvService.cs:320-380`（整个 `RollbackAsync` 无 `CreateBackupAsync`，文件分支写完即返回） | 与 apply 同规格：先整备份 → 写 `.obshelper.bak` → 读回逐项校验 |

### A11. 危险确认框默认焦点在「确认」，彻底重置是连续两次

- **证据**：`Controls/ConfirmDialog.xaml:39-41`（`OkButton` 带 `IsDefault="True"`）；
  `Views/ObsConfigPage.xaml.cs:319-336`（两次 `ConfirmDialog.Show(danger: true)`）。
- **后果**：Enter → Enter 连过两道闸门，完成不可撤销的彻底重置。
- **改法**：`danger: true` 时初始焦点给「取消」（或落在弹窗本身），并让第二次确认要求输入确认词 / 勾选。
- **体积**：S。

### A12. 出事了却拿不到证据：崩溃日志可能丢、危险操作零日志

- **证据**：
  - `FileLogger` 是异步 Channel（`FileLogger.cs:23-26,63`），只有 `OnExit`（`App.xaml.cs:308`）与
    `UpdaterBootstrap`（`:190`）调 `Flush()`；`AppDomain.UnhandledException`（`App.xaml.cs:257-264`）与
    `DispatcherUnhandledException`（`:312-317`）**都只入队不 Flush** → 崩溃那一条（最需要的那行）常常不在文件里；
  - `ObsBackupService` / `ObsResetService` / `FileTx` / `LocalStore` / `HostBridge` / `ObsSafePath`
    **完全没有 FileLogger 调用** —— 「备份了哪个包、导入了哪个 zip、移走了哪些文件、失败在哪一步」
    在线上无任何记录，而这些正是可能造成数据损失的操作；
  - 日志没有版本/会话头：80 处 `FileLogger.*` 里没有一处记录启动版本/OS（线上拿到日志无法判断是哪个构建、哪次运行）；
  - `ErrorCodes.cs:20-62` 只到 8xx/9xx，更新/自举/热更新/免费 AI/简单录像**没有专属码**，
  失败走 Toast 或原始异常消息（`SimpleRecordingService.cs:364`、`RecordingEnvService.cs:290`）→ 用户截图给不出可检索的码。
- **改法**：① 两个致命处理器里 `Flush()`（或致命级改同步写）；② 危险操作补起止日志（含路径、entryCount、跳过清单、
  提交/回滚结果）；③ 启动写一行 `ver/os/pid`；④ 补 `OBS901` 更新校验失败 / `OBS902` 自举失败 /
  `OBS903` 热更新失败 / `OBS904` 文件事务回滚失败，并统一走 `ErrorCodes.Format` 出口。
- **体积**：S–M。

---

## 3. B 类：安全与隐私（口径一致性）

### B1. `LogSanitizer` 漏点（脱敏是「对外发送」与「反馈材料」的最后一道闸）

- **证据**：`Services/Obs/LogSanitizer.cs`
  - `KeyValueSecret`（`:33-35`）含 `passwd|password` 但**没有 `passphrase`**；
    SRT 的 `srt://host:port?passphrase=…` 也不会被 `StreamUrl` 覆盖（`:28-30` 的路径组 `(/\S*)?` 只吃以 `/` 开头的部分）
    → query 原样保留。10~23 字符的 SRT 口令既躲过键值规则（长度门槛）又躲过长串规则（`:58-59`）；
  - **公网 IPv6 未脱敏**（只有 `Ipv4` 规则 `:46-47`）；
  - 标准 Base64（含 `+`/`/`/`=`）会被长串规则拆碎（字符集排除 `+/=`），片段一短就漏；
  - `Authorization: Bearer <jwt>` 中 `bearer` 后跟空格而非 `[:=]`，键值规则不匹配，只能靠长串规则部分兜底。
- **改法**：按上述四类补规则，并在 `LogSanitizerTests` 里逐条加边界用例（当前这些路径应当是红的）。
- **体积**：S。

### B2. 导出包「脱敏失败」时原样打包 `service.json`，却仍标记为已脱敏

- **证据**：`ObsBackupService.cs:604-618` —— `RedactServiceJson` 返回 null（JSON 解析失败）或抛异常时，
  `catch` 里调用 `AddFileRaw` **原样打包** `service.json`（含 `key` / `bearer_token`），
  而 manifest 仍写 `includesStreamKey=false` 并把该文件列进 `redactedFiles`（`:147-148`）。
- **后果**：用户以为导出包已脱敏，转手发到群里求助 → 流密钥外泄。这是明确的外泄面。
- **改法**：脱敏失败必须**失败**或降级为「不包含该文件 + 明确警告」，绝不允许「标记已脱敏但实际原样」。
- **体积**：S。

### B3. AI 出站没有面向用户的明示

- **证据**：`CloudDiagnosticEngine.cs:174-193` 的 `BuildUserPrompt` 把「脱敏日志正文（上限 16000 字）+
  实时快照 + 发现清单」发给第三方；`ObsToolRegistry.cs:108-140` 的 `SnapshotJson` 含**用户自定义的场景名与音频输入名**
  （可能是真名/公司名）。设置页文案（`StringTableZhHans.cs:859-878`）只讲限频，没有说「数据发给谁、是否留存」。
  （**好的一面**：工具集严格只读 4 个，`ObsToolRegistry.cs:50-98`，模型无法写 OBS —— 这点设计是对的。）
- **改法**：AI 设置页显式列出目标服务商与留存策略；提供「不发送日志正文」开关；
  免费通道（内置密钥 → 智谱）首次使用时确认。
- **体积**：S。

### B4. 出站白名单口径不一致

- **证据**：`HostBridge.cs:496-509` 的 `OpenExternalAsync` **只校验 scheme 是 http/https**，不校验域名；
  而 `Views/ProblemPage.xaml.cs:440-445` 与 `Controls/MarkdownView.xaml.cs:385-391` 会用它打开
  **热更新 JSON 里写的任意链接**。对照 `ObsDownloadLinks` 那条链是严格的域名白名单。
  另外 `UpdateService.cs:99-106` 等出站点跟随重定向且不校验目标（AI 通道反而是正确做法：
  `HostBridge.cs:554-581` 关重定向 + `ConnectCallback` 校验真实 IP）。
- **后果**：README / `SECURITY.md` 宣称的「出站 https 白名单」与实际不一致 —— 口径不一是安全问题，
  因为它会让下一个人误以为有保护。
- **改法**：统一到一套域名判定（下载类走白名单，知识库外链走「https + 非私网 + 明确提示」）；
  出站统一用「https-only + 拒绝私网 + 限制跳数」的 handler。
- **体积**：S–M。

### B5. 发布物没有 Authenticode 签名

见 A2（同一根因）。插件线 `PRNOBS/build.ps1:130-137` 已有 signtool 步骤，桌面线可以照抄。
**顺带纠正一个容易误判的点**：机密存储的 DPAPI(CurrentUser) + PBKDF2(MachineGuid) 双层
（`HostBridge.cs:100-226`）中，`MachineGuid` 在 HKLM 可读，所以第二层只提高「离线窃取文件」的成本，
对同机同用户进程无防护 —— 建议把这些写进文档的威胁模型，而不是让读者以为它是「抗同机攻击」。

---

## 4. C 类：体验优化

### C1. 已经采到、却没显示出来的数据（性价比最高的一类）

| 数据 | 已经采到的位置 | 现状 | 建议落点 |
|---|---|---|---|
| 画布 / 输出分辨率 / 帧率 | `ObsConnectionService.cs:266-276`（`GetVideoSettings`） | 唯一消费方是 AI（`ObsToolRegistry.cs:116-118`）；监控页只显示 OBS 版本（`PerformancePage.xaml.cs:194`） | 监控页 / 控制台一行「1920×1080 @60 → 输出 1920×1080 @60」——**所有建议都以这些值为前提**，用户却要切回 OBS 核对 |
| 推流健康度：`outputReconnecting` / `outputCongestion` / `outputSkippedFrames` | `ObsConnectionService.cs:401-413` | 界面只消费 `Active`（`ConsolePage.xaml.cs:401-406`）；`Congestion`/`DroppedRatio` 唯一消费方是 AI | 控制台输出区一行 + 监控页一张卡 + 迷你小窗一个数字（直播中「正在重连 / 拥塞 / 丢帧」现在完全看不见） |
| 音频电平（峰值表） | **未采**：`ObsProtocol.cs:45-52` 定义了 `InputVolumeMeters = 1<<16`，注释写着「仅在监控页可见时按需开启」，但 `ObsConnectionService.cs:135` **永远传 `Default`**，这个「按需开启」从未实现 | 控制台只有「我设的音量」滑杆，没有「实际电平」 | 打开监控页时按需 `Reidentify` 订阅电平表；控制台加峰值指示与削波提示 |
| 音画同步偏移 | 全仓 `SyncOffset` **零命中** | 知识库在教用户手动试错 | 见 D7（`SetInputAudioSyncOffset`） |

### C2. 迷你小窗只回答「能不能按」，不回答「录得怎么样」

- **证据**：`Views/MiniControlWindow.xaml:38-82` —— 只有「🎛 标题 + 连接状态 + 录制/推流/虚拟摄像头三个按钮 + 一行 footer」，
  没有时长、没有剩余可录、没有丢帧；`MiniControlWindow.xaml.cs:34-60` 只刷这三处。
  `Win7`… 顺带：`ShowActivated="False"`（`:12`）→ 热键呼出后焦点仍在别的窗口，键盘用不了。
- **改法**：加一行紧凑指标（已录时长 / 剩余可录 / 丢帧率），数据全都在服务里；
  进阶可加 `GetSourceScreenshot` 的节目画面缩略图（D7）。
- **体积**：S（带缩略图 M）。

### C3. 「剩余可录」用写死的预设码率估算

- **证据**：`Services/ObsConfig/SimpleRecordingCore.cs:107,111-113` ——
  `BitrateSafetyFactor = 1.5`，预设码率硬编码 20 000 / 8 000 / 30 000 kbps。
  而本产品**刻意不写码率**（V2.9.4 不变量 B），用户实际写入速率由他自己的编码设置决定，可以差好几倍。
- **改法**：录制中已在读文件大小（`ConsolePage.xaml.cs:445`、`SimpleRecordCard.xaml.cs:297`），
  用两三个采样点的**实测写入速率**动态校正，并把口径写在界面上（「按当前实测写入速率估算」）；
  首次采样前回退到预设常数。
- **体积**：S（纯逻辑可单测）。

### C4. 搜索与助手只做子串匹配

- **证据**：`ProblemService.cs:203-212`（拼成一大串后 `Contains(query)`，无分词、无权重、无模糊）；
  `AssistantService.cs:46-67`（CJK 二元切分 + 长度加权，仍是「包含即得分」）。
- **推测后果**：拼音首字母（`hp`→黑屏、`ls`→蓝屏）零结果；「落帧」查不到「掉帧」；带空格的多词查询整串匹配失败。
- **改法**：① 数据侧给条目加同义词/别名（走已有热更新通道，不改程序也能补）；
  ② 查询侧加拼音首字母索引（纯 BCL）；③ 排序改为字段加权（标题 > 症状 > 步骤）；④ 无结果时给 3 条最相关建议。
- **体积**：M。

### C5. 诊断页的 8 项自检清单手填且不落盘

- **证据**：`Views/DiagnosticPage.xaml.cs:20`（注释自认「勾选状态只存在内存里…刷新即重置」）、`:27-37`、`:315-320`；
  对照 `Views/ProblemPage.xaml.cs:175-178,361`（`Bookmarks.SetCompletedSteps` **落盘**）。
- **改法**：短期把勾选落 `LocalStore`；中期按 D4 让其中 6~7 项**按本机实测自动回填**。
- **体积**：S（落盘）/ M（自动回填）。

### C6. 信息架构与页面组织

- **工具箱已是一页 18 个分区**：`Views/ToolboxPage.xaml` 的 `SectionTitle` 落在第 12/30/52/68/112/148/173/188/216/244/272/290/305/334/346/360/367 行，
  全在同一个 `ScrollViewer` 里，无目录、无搜索、无折叠 → 加分区索引卡（点击滚动）或拆成「体检 / 工具」两个子 Tab。
- **首页把简单录像压在分类之前**：`HomePage.xaml:31` 的 `SimpleRecordCard` 排在 `:83,:89` 的「问题分类」之前，
  未连接时还多一张欢迎卡（`:34-74`），而 `MainWindow.xaml:7` 最小高度 620 —— 新用户首屏只剩 1~2 行分类。
- **搭建页六步是纯跳板**：`Views/SetupPage.xaml.cs:20-28` 六步 Href 全部跳走，`:92-161` 卡片只有编号+标题+说明，
  无进度、无完成态、结尾无动作（对照问题页有持久化勾选与进度条）。
- **平台筛选用「标题含关键词」而不是数据里已有的 `platforms` 字段**：`SetupPage.xaml.cs:34-46,279-291`
  （`p.Title.Contains(keyword) || p.Id.Contains(keyword)`）；而 212 条问题每条都带显式 `platforms` 数组，
  `ProblemPage.xaml.cs:114` 就在用它渲染徽章 → 筛选结果靠猜，`setup.platformEmpty` 会莫名出现，
  且 Windows-only 应用里还能选到 macOS chip。
- **体积**：各 S。

### C7. Toast 2.5 秒自动消失、无关闭、无历史，而错误也走它

- **证据**：`Services/ToastService.cs:16-17`（`LifeTime = 2.5s`）、`:31-47`（无关闭按钮、无历史）；
  错误大量走 Toast（`SimpleRecordCard.xaml.cs:460,481,503`、`DiagnosticPage.xaml.cs:350`）。
- **后果**：`simple.error.startFailed`（「开录失败…参数可能已经落地了一部分…需要撤销时可在『高级设置』里回滚」）
  这种**要照着做**的文案，2.5 秒后读不到了。
- **改法**：错误类 Toast 常驻到用户关闭 + 可点「详情」；加一个最近 N 条的提示历史（托盘菜单入口）。
- **体积**：S。

### C8. 文档与代码事实漂移 13 处

| # | 文档位置 | 文档说 | 实际 |
|---|---|---|---|
| 1 | `docs/ARCHITECTURE.md:37` | AppServices 是「**28 个** Lazy 单例」 | `AppServices.cs:22-86` 实为 **45 个** |
| 2 | `docs/CODEBASE.md:31` | 「**20 个**服务」，文件 **125 行** | 45 个；实际 **221 行** |
| 3 | `AppServices.cs:17`（代码注释） | 「服务只有**十来个**」 | 45 个 |
| 4 | `docs/ARCHITECTURE.md:155` / `:204` | 自检 = 「17 条路由 + 引导 + 小窗 = **19 项**」 | 导航用例 **18 个**（覆盖 `Routes.cs` 17 条路由，`plugins` 带参跑两次）+ 4 项非路由检查 = **22 项**，与 `README.md:234` 一致 |
| 5 | `docs/CODEBASE.md:233` / `README.md:169` | 文案表「约 **1670** 条键」 | 中表 1853 / 英表 1851 / 键集并集 **1852** |
| 6 | `OBS_Helper.Wpf.csproj:29` | 注释「软件名从『**OBS帮助助手**』改为『**OBS帮助助手**』」 | 前后同名 —— V2.9.6 改名时漏替换（`build.ps1:2` 同病） |
| 7 | `docs/CODEBASE.md:187` 与 `:198` | 两个小节**都编号 2.6** | 后者应为 2.7，其后顺延 |
| 8 | `docs/CODEBASE.md` §2.3.5/§2.4/§2.6 | 号称「完整代码清单」 | **24 个已存在的源码文件未收录**（`PreflightCheckService`/`PreflightCheckCore`/`Compat/*`/`SystemCheck/*`/`AudioDeviceHealth*`/`VirtualCamCheck*`/`RecordWatchdog*`/`LogTailerService`/`LogAlertThrottle`/`IncrementalUpdateService`/`KnowledgeBaseUpdater`/`InstallerCleanup*`/`FileHasher`/`UpdateManifest`/`DeltaBuilder`/`UpdaterBootstrap`/`RecordingEnvCore`/`LocalizationService`/`RecordingEnvCard`/`FeedbackCard` …） |
| 9 | `docs/DEV_GUIDE_GAPS_2026-08.md:4,30`、`docs/OPTIMIZATION_V2.7.0.md:141-148` | 「知识库 **149 条**」「测试 **241 项**」「version=2.0」 | 知识库 **212 条**（`problems.json` 已 `version 2.2`）、测试 ≈630；而前者仍被 `README.md:269` 推荐阅读 |
| 10 | `docs/FEATURES_USERVIEW_2026-10-05.md:31,35-36` | 「全仓库**没有**启动 obs64.exe 的能力」 | V2.9.4 已落地 `SimpleRecordingService.cs:759-790`、`ObsReadyTimeoutSeconds = 75`（`:36`）、1 秒轮询（`:864`） |
| 11 | `docs/SIMPLE_RECORDING_DESIGN.md:153` | 「新增 `LaunchObsAsync()`」 | 同文 `:20` 已声明「**没有这个方法**」——只读该节会照不存在的方法签名写代码 |
| 12 | `docs/QA-REPORT.md` | — | **0 字节空文件** |
| 13 | `docs/ARCHITECTURE.md:170` | GitHub 通道只找 `OBS_Helper_Setup_*.exe` | 已扩展到增量包/知识库/插件资产并**按语言严格匹配**（`UpdateService.cs:256-397` 的 `AssetMatchesLanguage`）——按文档核对会漏掉这条真实安全面 |

- **为什么当缺陷修**：本仓有「文档与事实差 11 处」专项修过的先例（`REVIEW_2026-10-05-v2.9.4.md` 的 F12），
  这 13 条属同一纪律。数字漂移的真正代价是**判断失真**：同一个事实在三份「已同步到 2.9.6」的文档里有 20/28/45 三个答案。
- **改法**：① 逐条修正；② 新增 `scripts/check_docs.py`：从 `AppServices.cs` 数 Lazy、从 `Routes.cs` 数路由、
  从 `problems.json`/`plugins.json` 数条目、从文案表数键，与文档数字比对，不一致就非零退出（接进 CI，见 E1）；
  ③ `CODEBASE.md` 的「行数」列建议整列删掉或改成量级 —— 它天然会漂。
- **体积**：S。

---

## 5. F 类：性能与资源

> 全部为**代码级推定**，本会话无法实测（见文首证据边界）。定量结论需要真机 profile。

### F1. 进过一次插件广场，系统监控计时器就永久常驻 ★ 真缺陷

- **证据**：`Views/PluginsPage.xaml.cs:891` —— `ComputeAiBudgetHint` 里写着
  `if (!monitor.IsRunning) monitor.Start();`，而它在导航进入页面时必被调用（`:106`）。
  全仓**只有** `Views/PerformancePage.xaml.cs:49` 会 `Stop()`。
- **后果**：`SystemMonitorService` 的 1 秒 `DispatcherTimer`（`SystemMonitorService.cs:62,104-121`）
  常驻 UI 线程直到进程退出，每次 tick 都要 `PerformanceCounter.NextValue` + 2×`GlobalMemoryStatusEx`
  + 网卡统计 + `DiskProbe.Sample` —— 用户只是看了一眼插件列表，就要一直付这份电量与 CPU。
- **改法**：AI 预算提示改为「进入页面取一次快照」的一次性取值；或把它挂到与监控页相同的生命周期上（离开即停）。
- **体积**：S。**建议与 A1 一起放进 2.10 的第一批。**

### F2. 每次重连泄漏一个 `CancellationTokenSource`

- **证据**：`Services/Obs/ObsWebSocketClient.cs:410-429` —— 第 415 行已经 `_loopCts = null`，
  第 427 行的 `_loopCts?.Dispose()` 于是**恒为 null**（`oldLoopCts` 永不释放）。
  而这个 CTS 是 `CreateLinkedTokenSource` 出来的（`:75`），长连接反复重连会持续累积。
- **改法**：先保住局部引用再 Dispose（`var old = _loopCts; _loopCts = null; old?.Dispose();`）。
- **体积**：S（一行级）。补一条「重连 N 次后不增长」的测试最好，但见 E2 的结构性障碍。

### F3. 首页首屏在 UI 线程串了 3~4 轮完整 OBS 环境探测

- **证据（调用链）**：`MainWindow.xaml.cs:109/132` → `HomePage.xaml.cs:67` →
  `SimpleRecordCard.xaml.cs:46-54,131-149` → `SimpleRecordingService.cs:172-223,226,233-244` →
  `PreflightCheckService.cs:19-58` + `RecordingEnvService.cs:74-104` + `RecordingToolsService.cs:34` →
  `ObsPathService.cs:66,156-208,312-370`。
  其中 `ObsPathService.cs:66` 用 `Task.FromResult(ResolveLocation())` 把**同步**探测伪装成异步 ——
  await 它等于在 UI 线程原地跑完（进程枚举 ×3 + 注册表 ×6 + DriveInfo ×3 布局 + ini 读取）。
- **后果**：窗口已经 `Show` 了，但首页内容与交互要等这段同步 IO 走完；**每次回首页都会重复**。
- **改法**：① `ObsPathService` 的定位结果做缓存（失效条件：用户改过配置目录、OBS 装/卸）；
  ② 真探测器挪到 `Task.Run`（而不是 `Task.FromResult`）；③ 首页先渲染骨架，自检结论回来再更新卡片。
- **体积**：M。

### F4. 日志分析：正则未编译 + 全文留三份副本

- **证据**：`Views/LogsPage.xaml.cs:24,114,175-195`（8MB 尾读）；
  `Services/Host/HostBridge.cs:64,448-477`；`Services/Obs/LogSanitizer.cs:25,28-59,89-124`（每行 8 条脱敏正则）；
  `Services/Obs/ObsLogAnalyzer.cs:134,194-436,441-488`（每行最多 34 条规则正则）——**全部未用 `RegexOptions.Compiled`**；
  同一份 8MB 文本同时存在 `rawText`（UTF-16 约 16MB）+ `sanitizedLines` + `SanitizedText` 三处。
- **改法**：规则正则 `Compiled`（构建期一次）+ 先用廉价字面量做预筛（命中才上正则）+
  `SanitizedText` 改懒构造（只在导出/复制时拼接）。
- **体积**：M。

### F5. 语言切换不彻底：部分文案被冻结在首次加载

- **证据**：`Services/LocalizationService.cs:131-141` 每次（启动 + 每次切语言）把约 **1853** 个键逐条写进
  `Application.Resources`；同时多处文案在类型初始化 / 首次加载时就被冻住：
  `Views/GuidePage.xaml.cs:18,30`（`_loaded` 直接早退，不再重取）、`Views/DiagnosticPage.xaml.cs:27-37`、
  `Views/PluginsPage.xaml.cs:27-45`、`Services/Obs/ObsLogAnalyzer.cs:713-721`（`DropKinds`）。
- **后果**：V2.9.2 宣称「切语言即时生效、不用重启」，但**这几个页面切完仍显示旧语言**。
  现有自检只覆盖「顶栏 / 徽章 / 托盘 / 当前页重放」，覆盖不到这些路径。
  这与 V2.9.4 审校 F13（预设按钮语言不跟随）是同一类问题，只是这次发生在更多页面。
- **改法**：把这四处改成属性/延迟取值（仓库里已有正确写法的先例，`AssistantService.Suggestions` 就是）；
  自检补一条「中英往返 + 逐页重放」。
- **体积**：M（改法本身 S，代价在补自检）。

### F6. 插件广场：进页面就可能把 GitHub 匿名额度吃干

- **证据**：`plugins.json` 恰好 **57** 条、**57** 个 repo；`Services/Plugins/PluginReleaseService.cs:30`
  是 `static SemaphoreSlim(1,1)` 全局**串行** + 单次 10s 超时 → 首屏 57 次请求串行；
  GitHub 匿名限额 60 次/小时/IP。另外 `Views/PluginsPage.xaml.cs:307`
  （`OnSearchTextChanged => RenderList()`）**没有防抖**，`:555-583` 每次击键全量重建 57 张卡并重新触发角标请求（`:796-797`）。
- **改法**：① 角标只对**当前可见/已关注**的条目取；② 进页面先渲染缓存，版本信息后台补；
  ③ 搜索框加 300ms 防抖（项目已有 `Services/Debounce.cs`，其他页面都在用）。
- **体积**：S–M。

### F7. 页面缓存无上限

- **证据**：`Navigation/NavigationService.cs:41,97-101` 只在 `CanReleaseOnLeave:true` 时逐出，
  而全仓**只有** `Views/PerformancePage.xaml.cs:54` 声明了 `true` → 17 个页面实例及其控件树常驻
  （`LogsPage` 还持有 8MB 脱敏全文）。
- **改法**：LRU 上限（比如 6~8 页），重页面（日志 / 控制台 / 插件）离开即释放。
  **注意**：`HomePage.xaml.cs:37-40` 是故意不退订事件的（与「页面全缓存」配套），做 LRU 时必须一并改，否则会出现重复订阅。
- **体积**：M。

### F8. 零散 UI 线程同步 IO

| 位置 | 问题 |
|---|---|
| `Services/ObsConfig/ObsBackupService.cs:216-247` ← `Views/ObsConfigPage.xaml.cs:81-85` | 每个备份 zip `OpenRead` + 读 manifest；调用方法标了 `async` 却**没有 await** |
| `Views/ToolboxPage.xaml.cs:156-176` | 同步 `Process.GetProcesses()` |
| `Views/FeedbackPage.xaml.cs:178-193` | `Thread.Sleep(60)` ×3（写剪贴板重试） |
| `Services/Host/HostBridge.cs:341-358` + `:228-264` ← `Views/SettingsPage.xaml.cs:149-166` | `GetSecretAsync` 同步做 DPAPI + AES + JSON，每次进设置页触发 |

- **改法**：逐条挪到 `Task.Run` / 改真异步；剪贴板重试用 `DispatcherTimer` 或重试循环代替 `Thread.Sleep`。
- **体积**：S。

### F9. 检索没有索引

- **证据**：`Services/AssistantService.cs:36-67` 每次提问都对 **212** 条问题重新
  `ProblemService.BuildText` + `ToLowerInvariant`（在 UI 线程，`AssistantPage.xaml.cs:72-81`）；
  `Services/Plugins/LocalPluginScanner.cs:45-75` 无记忆化；`Views/LogsPage.xaml.cs:210`
  每次命中性能线索都重扫全盘 / Steam / 注册表。
- **改法**：建一次倒排/文本缓存（数据变了再失效）；扫描结果按「OBS 目录 mtime + 注册表键」做记忆化。
- **体积**：S（与 C4 的搜索增强一起做最划算）。

### F10. 打包：三个可落地点（附硬数据）

- **现状**：`RELEASE_NOTES_v2.9.6.md:94-100` —— 安装包 50.8MB / win7 47.8MB / 便携 zip 66.8MB / win7 62.9MB。
  `csproj:54-55` 的 `PublishTrimmed=false` 是**正确的**（WPF 不支持裁剪，别开）；R2R 已在 `build.ps1:111` 打开。
- **可落地点**：
  1. **WinForms 家族在发布目录占 ≈24MB**（`System.Windows.Forms.dll` 13.7MB + Primitives 3.6MB + Design 6.1MB
     + WindowsFormsIntegration / System.Design / System.Drawing.Design；见 `manifest_2.9.6.json` 对应条目），
     而代码里**只用了 4 处**（`TrayService.cs:3`、`MiniWindowService.cs:113-114`、
     `Services/Compat/FolderPicker.cs:24-28`（仅 net6）、`GlobalHotkeyService.cs:246-258` 只用到 `Keys` 常量）
     → 去掉 `UseWindowsForms` 换 P/Invoke，**未压缩约省 20MB**（托盘图标/文件夹选择器/热键常量都有纯 Win32 替代）。
  2. **便携包用 `Compress-Archive`（Deflate，`build.ps1:153`）**，而安装包已是 lzma2 + solid（`.iss:68-69`）
     → 便携包改 LZMA，**推测再省 15~25%**。
  3. 发布里带了 `OBS_Helper.pdb`（373KB，`manifest_2.9.6.json`）→ 可排除。
- **体积**：M（第 1 条要动 4 个调用点 + 双 TFM 都要验）。

### F11. 明确「未发现问题」的方向（避免误改）

- 全仓**无 `.Result` 死锁**（只有 7 处 `Wait`），唯一的 `GetAwaiter().GetResult()`（`SceneTemplateService.cs:421`）
  包的是 `Task.FromResult`，不会死锁；
- **无 `ObservableCollection`**（grep 0 命中），也没有「上千条未虚拟化列表」（各列表都在几十条量级）；
- **网络缓存设计良好**（`PluginReleaseService` 三层缓存 + 在途合并；`ObsReleaseInfoService` 6h 内存缓存 + 2min 失败退避），
  除插件角标外没有「每次进页面都打请求」；
- **事件订阅基本成对**（`ObsConnectionService:685-691`、`ConsolePage:75-93`、`PerformancePage:34-51`、
  `TrayService:96/234`、`SimpleRecordingService:72/981`、`MiniWindowService:77-89`、`ConnectionBadge:21-24`），
  例外是 `HomePage:37-40`（见 F7 的注意事项）。

---

### F12. 需要实测才能定量的点（本清单里的性能数字都不是测量值）

1. **首屏时间分布**：1853 次资源写入、MainWindow 的 XAML/BAML 解析、`InitializeAsync` 的 8 步、
   F3 的探测链，谁是大头需要 `Stopwatch`/ETW 埋点；
2. **8MB 日志分析的真实耗时与内存峰值**（代码注释里的「上百毫秒」是设计期估计，与未编译正则的实现不太相符）；
3. **插件广场 57 次请求是否真会触发 GitHub 匿名限额**（57 vs 60 次/小时，取决于 24h 缓存命中）；
4. **每次击键重建 57 张卡的实际帧耗时**；
5. **`PerformanceCounter` 构造耗时与每秒采样的真实 CPU / 续航占用**（机器差异很大）;
6. **页面全缓存的内存增长**（「遍历 17 页 + 反复进出」的私有工作集，预期单调上升不回落）；
7. **切语言写入 1853 键的实际耗时**，以及 WPF 是否有可用的「整表替换」批量 API（落地 F5 建议前需确认）；
8. **去掉 WinForms 后的真实体积收益**（本清单是从发布清单的未压缩字节推算的）。

---

## 6. D 类：新功能

> 判据：**「已有的东西」× 「真实痛点」× 「不违反既有红线」**。
> 红线（沿用现有文档）：不静默改配置、不碰推流参数/输出模式/编码器族/码率、只读探测优先、
> 零第三方 NuGet、双 TFM 可编译、不做无人值守的后台改写、数据仅存本机。

### D1. 回放缓存（Replay Buffer）一键存片 —— 本清单性价比最高的一条

- **现状证据**：全仓搜索 `Replay` **零处调用**（命中的只有「新手引导重播」这个不相干的词）；
  `Models/Obs/ObsProtocol.cs:38,41` 定义了 `Filters` / `MediaInputs` 订阅位，`Default:52` 没用上；
  控制台、托盘菜单（`TrayService.cs:293-328`）与迷你小窗只有录制/推流/虚拟摄像头三个开关。
- **能力底座**（obs-websocket v5 现成）：`StartReplayBuffer` / `StopReplayBuffer` / `ToggleReplayBuffer` /
  `SaveReplayBuffer` / `GetReplayBufferStatus` + `ReplayBufferSaved` 事件。
- **用户价值**：这是 OBS 生态被反复自发补上的洞 —— 社区项目
  [backtrack](https://github.com/ilyambr/backtrack)（「NVIDIA Shadowplay 式的 OBS 剪辑工具」）、
  [photongenic](https://github.com/Tina-otoge/photongenic)（多实例回放导出）都在填；
  官方侧连[回放缓存文件不保存章节标记（#13567）](https://obs-versions.com/community/issues/13567)都仍是开放问题。
- **差异化**：不是再做一个插件，而是把「存片」接到**已有的守护与档案**上：
  热键存片 → 立刻提示「已保存到 X（时长/大小）」→ 自动登记进录制档案（D3）→ 一键转 MP4 / 打开目录。
  正好复用 `SimpleRecordingService` 的收尾链路与 `RecordingToolsService` 的转封装。
- **落点**：`ObsConnectionService` 加请求封装 + `ReplayBufferSaved` 订阅；
  `GlobalHotkeyService`（已有 5 个动作）+ 托盘菜单 + 迷你小窗。
- **风险**：低（纯 OBS 原生输出，不写任何配置文件）。**体积**：M。

### D2. 简单开播：把「简单录像」的成功经验复制到推流

- **现状**：录制侧已有「配好 → 验好 → 开录 → 录得住 → 录完找得到」的首页一张卡；
  推流侧只有一个「开始推流」按钮，没有「现在能不能播」的一行结论，也没有开播前的一次过。
- **用户价值**：第一场直播的焦虑比第一次录制更大（推流码、场景、麦克风、节点四件事同时要对）。
- **改法**：复用现有零件 —— `PreflightCheckCore`、`IngestPingService`（节点 RTT）、
  `BandwidthAdvisorCore`（上行→码率）、`SceneTemplateService`（一键建教学场景）、
  `RecordWatchdogService`（开播后守护）—— 组成 `SimpleStreamingService`（与简单录像同构，
  纯逻辑进 `SimpleStreamingCore`）。**红线**：推流密钥与推流参数一律不写，只「读取 + 校验 + 提示 + 调 `StartStream`」。
- **体积**：L（硬件能力全是现成的，主要是状态机 + 文案 + 单测）。

### D3. 录制档案 + 收尾流水线

- **现状证据**：录制产物只留一个**单值** `ObsConnectionService.cs:64-69` 的 `LastRecordFile` / `RecordFileUtc`
  （下一轮即被覆盖）；没有历史、没有清单、没有重命名规则；停止后只有「打开目录 / 转 MP4」。
- **改法**：`%LocalAppData%\OBS_Helper\recordings.json`（复用 `LocalStore`），每次停止写一条：
  时间、时长（用 OBS 自报 timecode，**暂停不计入**的口径已有）、大小、路径、丢帧率、是否分段。围绕它做：
  1. 按规则重命名（日期/场景/预设前缀）——插件生态里 Record Rename 就是靠这个上位的；
  2. 批量转 MP4（现有 `RecordingToolsService` 的批量版）；
  3. **录制中按热键打点**，停止后生成章节标记（`.txt` / ffmpeg metadata），正好补官方 #13567 那个洞；
  4. 控制台「最近 10 次录制」列表 —— 一眼看出哪次是空文件。
- **体积**：M。

### D4. 自检清单自动回填 + 「开播前 60 秒体检」

- **现状证据**：诊断页 8 条清单靠用户自己勾（C5），而这 8 条里 6~7 条本机可判定：
  管理员权限 → `GraphicsEnvCheckCore`；硬件编码 → `PreflightCheckCore`；码率 ≤ 上行 75% → `BandwidthAdvisorCore`；
  捕获方式/双显卡 → `GraphicsEnvCheckCore` + `ObsLogAnalyzer`；48kHz → `SampleRateCheckCore`；
  Chrome/Discord 硬件加速 → `ConflictScannerCore`；有线/丢帧 → `ObsLogAnalyzer` + `IngestPingService`。
- **改法**：新增纯逻辑 `ChecklistAutoFillCore`（finding → 清单项映射，便于单测）+ 诊断页「按本机实测回填」；
  再进一步就是「一键处方」：把黑屏体检 / 编码顾问 / 带宽计算 / 磁盘测速 / 节点探测**合成一个结论**
  （`MachineProfileCore`），落地仍复用 `RecordingEnvService`。
  **边界**：推流部分只给建议不落地，避免越过既有纪律。
- **体积**：M。

### D5. 本次会话复盘报告

- **现状证据**：`LogsPage.xaml.cs:193` 每次覆盖 `Orchestrator.LatestReport`；实时预警只发托盘通知，
  `LogTailerService` 的 public 面（`:35-60`）只有 Start/Stop/ApplyEnabled/Enabled，**没有命中累计**。
  → 「这场比上场差在哪」「今晚被提醒了哪些问题」都答不了。
- **改法**：`LogTailerService` 增加 `SessionHits`（按规则码去重计数）；
  新增 `SessionReviewCore`（命中 + 本次日志分析 → 报告，复用已有 `diagnostic.report.*` 文案键）；
  落点在监控页或日志页顶部，可导出。
- **体积**：M。

### D6. 「我的模板」：把当前场景集合反向存成模板

- **现状证据**：现有模板能力是**单向**的 —— 官方 12 套 → 在线落地 / 离线导出（`SceneTemplateService`）；
  用户自己调好的一套（摄像头裁剪、滤镜、降噪）想换机或重装，只能靠整包配置备份，粒度太粗。
- **可行性**：反向读取已具备（`ObsConnectionService.RefreshScenesAsync/RefreshSceneItemsAsync` + `GetInputSettings`），
  导出 schema 就是现成的 `Models/ObsConfig/SceneTemplate`，落地器可直接复用。
- **体积**：L。

### D7. obs-websocket 剩下那批「现成但未用」的能力

| 能力 | 官方请求 | 本仓现状 | 建议落点 |
|---|---|---|---|
| 回放缓存 | `SaveReplayBuffer` 等 | **0 处** | D1 |
| 节目画面截图 | `GetSourceScreenshot` / `SaveSourceScreenshot` | **0 处** | 迷你小窗/托盘的**实时缩略图**（「画面还在不在」一眼可见） |
| 滤镜控制 | `GetSourceFilterList` / `SetSourceFilterEnabled` | `ObsProtocol.cs:38` 定义了 `Filters` 位但未订阅 | 控制台「常用滤镜」开关（降噪、色键、锐化）；托盘一键切 |
| 媒体控制 | `TriggerMediaInputAction` | **0 处** | BGM / 视频源的播放-暂停-下一首 |
| 音频监听与同步偏移 | `SetInputAudioMonitorType` / `SetInputAudioSyncOffset` | 控制台只有静音 + 音量（`ObsConnectionService.cs:482-485`） | 落地 `DEV_GUIDE_GAPS_2026-08.md` 的 **GAP-7 音画同步校准助手** —— 那份文档连方案都写好了，只差实现 |
| 配置集 / 场景集合切换 | `SetCurrentProfile` / `SetCurrentSceneCollection` | **0 处**（只读列表用于重置与模板） | 托盘 + 小窗的「游戏 / 会议 / 竖屏」三套一键切换 |
| 演播室模式 | `SetStudioModeEnabled` / `TriggerStudioModeTransition` | **0 处** | 双屏主播的「预览 → 切换」远程按钮 |

- **建议排序**：回放缓存 → 同步偏移 → 场景集合快切 → 缩略图 → 滤镜 → 媒体控制 → 演播室模式。
- **每条 S–M**（封装请求 + UI 入口 + 连接态护栏）。**一次全做会失焦**。

### D8. 知识库贡献闭环（本地收集 + 用户手动提交，不自动上传）

- **现状**：知识库只能被动接收热更新（`KnowledgeBaseUpdater` 的双通道 + 语言分发做得很扎实），
  用户端**没有回写路径**；反馈只有腾讯表单 / GitHub Issue，且明确「不含路径、设备名与日志原文」。
- **改法**：① 问题详情页底部「这条解决了我的问题吗？」→ 生成 Markdown 让用户自己贴到 Issue/邮箱
  （标题带问题 id 与知识库版本）；② 允许本地新增条目（`%LocalAppData%\OBS_Helper\data\my-problems.json`，
  与热更新文件同机制），把老用户自己的踩坑笔记沉淀成资产。
- **体积**：M（① 只要 S）。

### D9. 语言与生态延伸

- **繁体中文 / 日语**：i18n 架构（`Localization/*`、`DataValues`、按语言资产装载）在 V2.9.2/2.9.3 已完成，
  加语言已降到**纯数据成本**（新增文案表 + 内容资产并列文件）。考虑 README 已有英文侧，**繁体中文投入产出比最高**。
- **插件目录加「与本机 OBS 版本兼容性」一维**：`plugins.json` 已有 `maintain` / `maintainNote` / `riskNote`
  三个可选字段与热更新通道（`Services/Plugins/PluginCatalog.cs`），再加一维是纯数据改动。
  背景：OBS 33 已在 beta（[33.0.0-beta4](https://github.com/obsproject/obs-studio/releases/tag/33.0.0-beta4)），
  历史上每次大版本都会产生一批「插件悄悄失效」的需求，现在只能靠问题库条目事后接。
- **体积**：S–M。

---

## 7. E 类：工程与可观测性

### E1. CI 已经不错，缺三件事

- **现状证据**：`F:\OBS\.github\workflows\ci.yml` 已在 push/PR 上跑
  ① `dotnet test`（`:29-31`）、② `build.ps1` 双 TFM 出包（`:39-41`）、③ `check_resources.py`（`:48-50`）、④ 上传产物（`:52-57`）。
- **缺**：
  1. **无界面自检**：`OBS_SELFTEST=1` 那 22 项没进 CI，而它是发布说明里每次人工跑的项目 —— 应自动跑并断言 `0 FAIL`；
  2. **增量包可升级性校验**：`python scripts/verify_delta.py …` 目前发版时手工执行、结果抄进发布说明
     （`RELEASE_NOTES_v2.9.6.md:118`），而它拦的正是「增量包把用户装坏」这类最贵的故障；
  3. **tag 触发自动发版**：现在手工 `gh release create`、SHA-256 手工抄录，因此每年都要在发布说明里解释一遍
     「二进制在提交之前构建，exe 的 ProductVersion 指向上一版提交号」（`RELEASE_NOTES_v2.9.6.md:142-144`）——
     改成 `on: push: tags: ['V*']` 后，这段解释可以永久删掉。
- **顺带**：`actions/cache` 缓存 NuGet；把 net6 的 EOL 警告边界写进 workflow 注释。
- **体积**：M。

### E2. 测试工程的结构性问题：服务层根本没被编译进测试程序集

- **证据**：测试工程不引用主工程，而是用 `<Compile Include>` 逐个链接源文件
  （`OBS_Helper.Wpf.Tests\OBS_Helper.Wpf.Tests.csproj:21-69`，约 45 个文件）。
  因此在测试目录里 grep `FileTx|HostBridge|ObsBackupService|ObsConnectionService|ObsWebSocketClient|IncrementalUpdateService|UpdaterBootstrap|LocalStore|FileLogger|RecordingEnvService|SimpleRecordingService|ObsSafePath|ObsResetService|CloudDiagnosticEngine|ObsPathService`
  —— **0 命中**。这不是「没写测试」，而是**结构上无法测**。
- **为什么这决定了后面每一步**：A1~A5、B2 全部落在这些「进不了测试程序集」的文件里。
- **改法**：把 IO / 网络抽成可注入的接缝（例如 `ObsWebSocketClient` 抽 `IObsTransport`、
  把 `SecretCodec` 从 `HostBridge` 里拆出来、`FileTx` 的 `CopyItem` 可注入），
  再把服务层**项目引用**进测试工程；先补 A1/A2/A5 三条回归测试。不引入任何第三方包。
- **体积**：L（需要一轮小重构），但它是「把未验证变成有回归保护」的唯一路径。

### E3. 可观测性

见 A12。**另外**：`TraceLoggerListener` 包在 `#if DEBUG` 内（`App.xaml.cs:273-278`），
Release 下 XAML 绑定失败（静默留空）没有任何线上痕迹 —— 目前靠 `check_resources.py` + `StringsCallSiteTests` 静态兜底。
若采纳 A12 的诊断包方案，这一层可以一并解决。

### E4. 无障碍（本仓目前基本空白）

- **证据**：全仓 `AutomationProperties` / `AutomationPeer` / `LiveSetting` **零命中**。
  具体后果：Toast（`Themes/Controls.xaml:840-878`）不会播报「已连接 OBS」「开录失败」；
  新增动态卡片（诊断项、日志发现、体检结果）都不会被朗读。
- **键盘焦点**：`Themes/Controls.xaml:404-433` 的 `SegmentButton` 只有 `IsMouseOver` / `IsChecked` 触发器，
  **缺 `IsKeyboardFocused`** —— 而 `CardButton:349-352`、`NavTabButton:393-396`、`AppCheckBox:569-572`、
  `AppSwitch:607-610`、`BaseButton:216-219` 都有。受影响的正是语言/主题/字号/录像预设这些纯键盘最容易迷路的控件。
  `AppComboBox`（`:652-685`）与其 `ComboBoxItem`（`:687-709`）同样没有焦点反馈。
- **字号与缩放**：档位最大 1.28×（`AppearanceService.cs:411-425`），不跟随系统「文本大小」；
  另有 6 处硬编码字号绕过档位（`Themes/Controls.xaml:554`、`UpdateDialog.xaml:24`、`ProblemCard.xaml:44`、
  `ConfirmDialog.xaml:24`、`HomePage.xaml:114`、`ProblemPage.xaml.cs:209`、`SetupPage.xaml.cs:190`）。
- **高 DPI 最小宽度**：`MainWindow.xaml:6-7`（`MinWidth=920`）+ `app.manifest:7-8`（PerMonitorV2）——
  在 1366×768 上开 150%/200% 缩放，可用逻辑宽只有 911/683 DIP，**小于 MinWidth**，而窗口没有横向滚动，
  右侧内容会落到屏幕外且无法触达（**推测**，建议真机复核）。
- **高对比主题**：`AppearanceService.cs:225-227,333` 只有应用内 `HighContrast` 设置，
  grep `SystemParameters` 零命中 —— 系统级高对比用户得不到应有的配色。
- **体积**：M（分项做，每条都不大）。

### E5. 国际化残留

- **好的部分**：`.xaml` 里的中文**全部**是注释；`.cs` 里 `Text/Content/Title/ToolTip/Message = "中文"`
  与 `Toast.Show("中文")` 均**零命中** —— i18n 工程纪律是达标的。
- **残留**：`Models/ObsConfig/SceneTemplate.cs:22`（`Transition` 默认值写成 `"淡入淡出"`）；
  `Views/SetupPage.xaml.cs:40,41,45`（`YouTube`/`Twitch`/`macOS` 直接写在 chip 数组里，没有对应文案键）。
- **英文用户会静默看到中文内容**：内容资产回退到中文时**只写 WARN 日志**
  （`ProblemService.cs:135-136,173-174`、`PluginCatalogService.cs:110-111`、`SceneTemplateService.cs:96-97`），
  **界面上没有任何提示**。这正是 V2.9.1 raw 404 那类「静默且合法」的坑的同一形状。
  建议：语言为英文且发生回退时，在对应页顶部给一条可关闭的提示条。
- **体积**：S。

---

## 8. 明确不建议做

| 想法 | 为什么不做 |
|---|---|
| 自动改 OBS 的推流参数 / 输出模式 / 编码器 / 码率 | 与 V2.9.3 以来的不变量 B 冲突；取值未在真机验证，改错直接毁掉用户的直播 |
| 后台无人值守改配置、静默装插件 | 与「只读优先 + 写操作需显式确认」冲突，也违背 `SECURITY.md` 的承诺 |
| 内置 ffmpeg 二进制 | 便携包已 67MB；转封装用用户已有 ffmpeg + OBS 自带重封装两条路已够 |
| 引入 DI 容器 / MVVM 框架 / 第三方 UI 库 | 45 个服务的静态树一屏可见；换框架的收益远小于「零依赖 + 双 TFM + Win7 可用」的既有收益 |
| 自动上传遥测 / 规则命中上报 | 与「数据仅存本机」的定位直接冲突 |
| **上传类后期处理**（自动传 B 站/YouTube） | 与 `app.brandSubtitle`「离线可用 · 数据仅存本机」的品牌口径冲突。**打点与切分清单可以做，上传不做** |
| 重画导航图标风格、统一卡片间距这类「视觉翻新」 | `.workbuddy/memory/MEMORY.md:5` 记录过用户明确拒绝过这批；A6/C6 里**功能性**的问题不在此列 |
| 顺手砍掉：诊断页的「关于 macOS 端」整张卡、工具箱里第 4 个官方下载入口 | 本产品是 Windows-only（`app.manifest:13-19`），macOS 内容属于知识库/指引页；官方下载入口在首页/搭建页/引导里已有三处，工具箱那份可删。**这两条是纯减法** |

---

## 9. 建议排期

**2.10 · 「先把会丢数据的地方堵上」+ 一条高价值新功能**

1. **A1 文件事务回滚**（S，收益最大，无条件先做）→ **F1 常驻计时器**（S）→ **F2 CTS 泄漏**（S）
   —— 这三条都是「一行到十行级」的确定性修复，先做掉
2. **A3 路径护栏与 `profileDir` 净化**（S–M）→ **A5 机密读失败**（S）→ **A2 更新完整性 + 自举回滚**（M）→ **B5 签名**
3. A6/A7/A8/A9/A10/A11（一串 S，一次清理「静默失败」）
4. **D1 回放缓存一键存片**（M）
5. C1 已采未显示的数据 + C2 小窗 + C3 实测速率（S–M）
6. **F5 语言切换不彻底**（用户可见的正确性缺陷，与 V2.9.2 承诺冲突）+ F3 首屏探测（M）
7. C8 文档 13 处 + `check_docs.py`；E1 CI 三件事；F10 打包三项（风险最低的是便携包 LZMA 与排除 pdb）

**2.11 · 产品能力上台阶**

1. **D2 简单开播**（L）
2. **D3 录制档案 + 收尾流水线（含打点）**（M）
3. **E2 服务层可测性重构**（L，为后面每一步铺路）
4. C4 搜索增强（含 F9 检索索引）、C5/D4 自检自动回填与开播前体检、D5 会话复盘
5. B1/B2/B3/B4 安全与隐私口径
6. F4 日志分析性能、F6 插件广场网络与防抖、F7 页面 LRU、F8 UI 线程同步 IO

**2.12+ · 数据与生态**

A4 备份/回收站与导入事务、D6 我的模板、D7 其余 websocket 能力、D8 知识库闭环、D9 语言与插件兼容性、
C9/E4 无障碍、E5 i18n 残留。

**贯穿（可与任意版本并行）**：E3 可观测性、F10 打包（便携包 LZMA + 排除 pdb 零风险，去掉 WinForms 需回归双 TFM）、
以及**把 F 类里所有「推测」换成实测数字**（见 F 类文末的待实测清单）。

---

## 10. 与既有缺口文档的关系（避免重复立项）

| 既有文档 | 已落地 | 仍未落地 → 本清单的处理 |
|---|---|---|
| `docs/DEV_GUIDE_GAPS_2026-08.md`（GAP-1~8） | GAP-1 守护 / GAP-2 黑屏 / GAP-3 音频 / GAP-4 实时尾随 / GAP-5 虚拟摄像头 / GAP-6 风险插件 / GAP-8 电源 | **GAP-7 音画同步校准助手** → D7（现在有了 `SetInputAudioSyncOffset` 的明确落点） |
| `docs/OPTIMIZATION_V2.7.0.md`（F1~F6、G1~G3） | 全部 | 无剩余 |
| `docs/FEATURES_USERVIEW_2026-10-05.md` P1~P3 | 录中信息、停止收尾 | P1「录制中断后一键重开」→ 建议并入 D1/D2 的守护可操作化；P2「文件完整性快检」→ 并入 D3；P3 小窗常显 → C2；P3「启动静默 5 秒体检」→ D4 |
| `docs/reviews/REVIEW_2026-10-05-v2.9.4.md` S1~S7 | — | **S1 → A3**（并补出「可经导入包注入」的完整链）；S2（`.obshelper.bak` 被无条件覆盖）→ 并入 A3 的改法；S3（回滚写空值）需真机验证后再定；S5（`_busy` 无重入）/ S6（`ConfirmLaunch` 单槽）→ 结构重构，暂缓；S7 属本机信任边界 |
| `PRNOBS/docs/OPTIMIZATION_CHECKLIST.md`（Dock 插件线） | A1、A3 已修 | A2/A4~A8、B1~B8、C1~C5 需与桌面线**分别排期**。另注：`PRNOBS/dock/CMakeLists.txt:3` 是 **2.8.0**，桌面线已 2.9.6 —— 两条产品线的节奏已错开一截，建议明确「插件线继续维护」还是「只保证与新版 OBS 的兼容」 |

### 附：一个值得单独决策的问题 —— Win7 兼容构建还要不要留

- **事实**：`Services/OsSupport.cs:6-11` 明确写着兼容构建覆盖 **Windows 7 SP1 ~ 11**（`net6.0-windows`，
  Inno 的 `MinVersion=6.1sp1`），发布资产因此多出两份（`_win7` 安装包 47.8MB、`_win7` 便携包 62.9MB），
  代码里还有一层 `Services/Compat/*` polyfill，且每个功能都要过一遍「net6 可编译」。
- **但本产品的连接层只讲 obs-websocket **v5** 协议**（`Models/Obs/ObsProtocol.cs:1-6` 的协议文档指向 v5），
  而 v5 是随 OBS 28 一起内置的；OBS 28 起已不再支持 Windows 7/8.1。
  也就是说 **Win7 上能跑的最新 OBS 用的是旧协议，本工具的「连上 OBS」这半边能力在最需要它的用户那里根本用不上**，
  他们只剩离线知识库那一半。
- **建议**：要么把 Win7 构建**定位讲清**（「仅离线知识库可用」），要么评估停掉两条 `_win7` 资产与 `Compat` 层，
  把省下的构建与验证成本投到 E2/E4 上。这条属产品决策，本文只把事实摆出来。
