# V2.9.4 详细设计：简单录像（一键配置 OBS 并开录）

> ⚠️ **本文是 2026-10 的 V2.9.4 设计稿快照**：其中的数字（知识库条数 / 测试项数 / 自检项数）
> 与**部分方法签名**已过期 —— 最新数字见 [`README.md`](../README.md)
> （当前：知识库 212 条 · 单测 630 项（V2.9.6 发布基线）· 无界面自检 22 项）。
> 历史结论**刻意保留原样**，不随版本回填。
>
> **读本文的正确顺序**：先读 **§0.1「实现与本文的差异」**，那里集中记录了设计稿与最终实现的
> 全部出入，并且**以 §0.1 为准**。正文里与 §0.1 冲突的段落已就地标了
> 「【已被 V2.9.4 实现取代，见 §0.1】」—— 只读某一节会照不存在的方法签名写代码。

> 上游结论见 [`FEATURES_USERVIEW_2026-10-05.md`](FEATURES_USERVIEW_2026-10-05.md)。
> 本文是施工图：逐文件改动、文案键、单测清单、验收口径。
> 所有纯逻辑放 `*Core.cs`（零 WPF、被单测工程直接链接），界面只做展示与确认。

## 0.1 实现与本文的差异（**施工中改动已回填，以本节为准**）

本文件是开工前的设计稿，落地过程中有几处按实现调整过。为避免「文档与代码各说一套」，
把这些差异集中记在这里（行号会漂移，故只描述事实）：

| 设计稿原写法 | 最终实现 | 为什么改 |
|---|---|---|
| `SimplePreset(Id, Key, Fps, SplitSeconds, EstimatedBitrateKbps)` | **多一个** `int SplitSizeMb = 0` | 游戏档同时按大小兜底分段 |
| `SimpleReadyCheck(Ready, BlockReason, Hint, WarnCount)` | **多一个** `FailCount` | 界面要能区分「失败项」与「警告项」 |
| `Check(viaWebSocket, obsRunning, blocked, streaming, failCount, warnCount)` | `Check(viaWebSocket, **canWriteFiles**, **obsProcessRunning**, streaming, **recording**, failCount, warnCount, blockedReason?, noChannelReason?)` | ① `recording` 也要阻断；② 「能不能写文件」由 `canWriteFiles` 判定而不是由 `obsRunning` 反推（OBS 没跑但读不到 `basic.ini` 也不可写）；③ 调用方已知的确切原因（配置目录缺失）优先于通用文案 |
| 优先级串里有「`obsRunning && !viaWebSocket` → 需要拉起 / 重启」一档 | **没有这一档** | 「需要拉起 OBS」不是阻断，是流程里的一步动作，由服务层执行，不该混进就绪判定 |
| `StaticTargets(int fps)` | `StaticTargets(RecordingEnvSnapshot s, int fps = Fps)`，只返回 **format / quality / canvas / fps 四键** | 需要 snapshot 才能算 `quality.Current`（高级模式下为空） |
| 安装根候选三档 | **四档**：`bin\64bit\obs64.exe` → `bin\64bit\obs32.exe` → `obs64.exe` → `bin\obs64.exe` | 兼容历史布局与 32 位安装 |
| 在 `ObsPathService` 新增 `LaunchObsAsync()` | **没有这个方法**：`ObsPathService` 只加 `FindObsExecutable()`，`Process.Start` 在 `SimpleRecordingService.LaunchAndWaitAsync` 里 | 启动要配合确认弹窗、就绪轮询与状态机，留在服务层更内聚 |
| 「`Views/SetupPage` 不改」 | 确实没改；卡片只挂在 **首页** | 首页才是「我就想录个像」的入口 |
| 「逐项可取消」 | **新卡不做逐项取消**：一键全量落地；要逐项取舍走「高级设置」的部署卡 | 一键开录的价值就在于不用勾选；两套勾选 UI 会让用户不知道该信哪个 |

另外两处口径修正：

- 录制时长**暂停不计入**（用 OBS 自报的 `outputTimecode`，它不累加暂停时间）；
- 「拉起 OBS 前会问一次」已实现（`ConfirmLaunch` 回调，**未注入时按「不允许」处理**）。

---

## 0. 版本与不变量

- 版本号：`2.9.4`（`OBS_Helper.Wpf.csproj`、`OBS_Helper_Setup.iss` 的 `#ifndef MyAppVersion` 默认值）。
- **不变量 A**：任何写 OBS 配置的动作 → 先整备份 → 逐项记旧值 → 写后读回校验 → 可回滚。
- **不变量 B**：不写推流参数、不写输出模式、不写编码器族、不写码率。
- **不变量 C**：录制中 / 推流中一律不落地配置（`Blocked`）。
- **不变量 D**：新文案中英成对，中文表与英文表键集完全一致、值为非空、英文表不出现汉字。
- **不变量 E**：双目标可编译（不使用 .NET 7+ 独有 API）。

---

## 1. 新增文件

### 1.1 `OBS_Helper.Wpf/Services/ObsConfig/SimpleRecordingCore.cs`（纯逻辑）

```csharp
public enum SimplePresetId { Quick, Meeting, Game }

/// 一档预设：只声明「录什么」，不声明「怎么编码」。
public sealed record SimplePreset(
    SimplePresetId Id, string Key, int Fps, int SplitSeconds, int EstimatedBitrateKbps);

public enum SimpleRecordingStep
{
    Idle, Checking, Blocked, SettingUp, LaunchingObs, WaitingObs, Recording, Stopping, Stopped
}

/// 「现在能不能录」的结论。
public sealed record SimpleReadyCheck(bool Ready, string? BlockReason, string? Hint, int WarnCount);

/// 一次开录要落地的项（复用 RecordingEnvItem / RecordingEnvTarget）。
public sealed record SimpleLaunchPlan(
    bool ViaWebSocket, string? BlockedReason, string? BasicIniPath, IReadOnlyList<RecordingEnvItem> Items);

public sealed record SimpleProgress(
    TimeSpan Elapsed, double FreeGb, double UsedMb, double RemainingMinutes, bool LowSpace);

public static class SimpleRecordingCore
{
    public static IReadOnlyList<SimplePreset> Presets { get; }        // 顺序：quick/meeting/game
    public static SimplePreset Get(SimplePresetId id);                // 未知 → Quick
    public static SimplePresetId Parse(string? key);                  // 归一化，未知 → Quick

    /// 预设 → 「部署录制环境」的推荐项（在 RecordingEnvCore.Build 之上叠加 fps 与分段）。
    public static List<RecordingEnvItem> BuildItems(
        SimplePreset preset, RecordingEnvSnapshot snapshot, string recPath);

    /// 就绪判定。参数全部是「已经读到的状态」，纯函数。
    public static SimpleReadyCheck Check(
        bool viaWebSocket, bool obsRunning, bool blocked, bool streaming,
        int failCount, int warnCount);

    /// 录制进度估算（剩余可录时长按预设码率 × 50% 冗余，沿用 DiskBenchmarkCore 口径）。
    public static SimpleProgress Estimate(
        TimeSpan elapsed, double freeGb, int bitrateKbps, double usedMb);

    /// 文件通道写完之后引导用户重启 OBS 才生效。
    public static bool NeedsObsRestart(bool viaWebSocket);
}
```

要点：

- `BuildItems` 的**基准项来自 `RecordingEnvCore.StaticTargets()`**（见 §2.1），
  只覆盖 4 个键（`fps` / `format` / `quality` / `canvas`），另加：
  - `path`：按传入的 `recPath`；
  - `sampleRate`：`WebSocketOnly = true`；
  - `split`（仅 `Game`）：`RecordingEnvTarget("SimpleOutput","RecSplitFile","true")` +
    `RecSplitFileTime`（秒）+ `RecSplitFileSize`（MB，仅 `game` 写）；
  - `recTracks`：`RecordingEnvTarget("SimpleOutput","RecTracks","1")`（兼容旧版 OBS 音频轨默认值）。
- `Estimate`：`RemainingMinutes = FreeGb * 1024 * 8 / (bitrateKbps * 1.5) / 60`，
  `LowSpace = RemainingMinutes < 10`，`UsedMb` 原样回传（由调用方从文件长度取）。
- `Check` 的判定优先级：`blocked` → `streaming` → （`!viaWebSocket && !obsRunning` 且下游无法写文件时）→
  `obsRunning && !viaWebSocket`（需要拉起 / 需要重启）→ `failCount > 0` → `warnCount > 0` → Ready。

### 1.2 `OBS_Helper.Wpf/Services/ObsConfig/ObsLaunchCore.cs`（纯逻辑）

```csharp
public sealed record ObsInstallCandidate(string ExePath, string Source, bool FromRegistry);

public static class ObsLaunchCore
{
    /// 从注册表 DisplayIcon 值解析 exe：形如 `"C:\...\bin\64bit\obs64.exe",0`。
    public static string? ExeFromDisplayIcon(string? displayIcon);
    /// 从安装根目录推导 exe：根 / bin/64bit/obs64.exe、根 / obs64.exe、根 / bin/obs64.exe。
    public static string? ExeFromInstallDir(string? installDir);
    /// 汇总候选（去重、保序），只返回「文件存在」的项。
    public static IReadOnlyList<ObsInstallCandidate> ResolveCandidates(
        IEnumerable<ObsInstallCandidate> raw, Func<string, bool> exists);
}
```

要点：**解析与探测分离**。注册表 / 目录枚举在 `ObsPathService`（需要 WPF 侧能力），
字符串与路径推导放纯逻辑，便于单测钉死（`DisplayIcon` 的引号、`",0"` 尾巴、多段路径）。

---

## 2. 修改文件

### 2.1 `Services/ObsConfig/RecordingEnvCore.cs`

- 新增 `public static List<RecordingEnvItem> StaticTargets(int fps)`：
  返回 `fps / format / quality / canvas` 四项（键名与现有 `Build` 完全一致），
  `Quality` 项在 `Advanced` 模式下的「不适用」语义由调用方用 `snapshot.OutputMode` 判断；
- `Build` 改为**调用 `StaticTargets`** 再补 `path` / `sampleRate`，键顺序保持
  `format, quality, path, canvas, fps, sampleRate`（现有单测断言了这个顺序）；
- **不改**任何已有键名与取值。

### 2.2 `Services/ObsConfig/RecordingEnvService.cs`

- 新增只读快照能力：`public async Task<RecordingEnvSnapshot> ReadSnapshotAsync()`（读 `basic.ini` 文本）；
- 新增 `public async Task<PreflightReport> RunPreflightAsync()`：复用 `PreflightCheckCore.Run`
  （`configDirExists` / `globalIni` / `basicIniText` / `freeBytesOf`），供简单录像的就绪判定；
- `BuildPlanAsync` 增加一条**硬阻断**：`流/录进行中` → `BlockedReason = env.blocked.recordingOrStreaming`；
  现有 `WebSocketAvailable` 已经排除 `RecordStatus.Active || StreamStatus.Active`，
  这里把「正在推流」的**原因**补上（原来会掉进「OBS 在跑但没连上」的误导性文案）。

### 2.3 `Services/ObsConfig/ObsPathService.cs`

> 【已被 V2.9.4 实现取代，见 §0.1 第 7 行 —— **`LaunchObsAsync()` 这个方法不存在**。
> 最终只新增 `FindObsExecutable()`，`Process.Start` 放在
> `SimpleRecordingService.LaunchAndWaitAsync`（配合确认弹窗、就绪轮询与状态机）。
> 下面的签名仅作设计史保留，**不要照它写代码**。】

- 新增 `public string? FindObsExecutable()`：注册表 `Uninstall\OBS Studio`（HKLM/HKCU/WOW6432Node）
  的 `DisplayIcon` → `ObsLaunchCore.ExeFromDisplayIcon`；再叠加已探测到的安装根
  → `ObsLaunchCore.ExeFromInstallDir`；全部走 `File.Exists` 校验；
- 新增 `public Task<(bool Ok, string? Error)> LaunchObsAsync()`：
  `Process.Start(new ProcessStartInfo { FileName = exe, UseShellExecute = true })`；
  找不到 exe 返回 `(false, "launch.notFound")` 文案键，**不猜路径**；
  启动失败捕获异常并返回可读原因。

### 2.4 `Services/Obs/ObsConnectionService.cs`

- 新增 `public DateTime? RecordStartedUtc { get; private set; }`；
- 新增 `public string LastRecordFile { get; private set; } = "";`
- `OnRecordStateChanged`：`outputActive` 由 false → true 时置 `RecordStartedUtc = DateTime.UtcNow`；
  变为 false 时清空（保留 `LastRecordFile`）；`e.Data.outputPath` 非空时写入 `LastRecordFile`；
- 暂停/恢复不动 `RecordStartedUtc`（时长优先用 OBS 自报的 `outputTimecode`，**暂停不计入** ——
  口径写在界面上，不让人自己猜）。

### 2.5 `AppServices.cs`

- 新增 `SimpleRecordingService` 与 `ObsLauncher`（或直接把启动能力挂在 `ObsPathService`），
  惰性装配并暴露 `public static SimpleRecordingService SimpleRecord => _simpleRecord.Value;`；
- 新增 `Services/Shell/SimpleRecordingService.cs`（见 §1 的能力；这是**服务层**，允许依赖 WPF 侧服务）：

```csharp
public sealed class SimpleRecordingService
{
    public SimpleRecordingStep Step { get; private set; }
    public SimplePresetId Preset { get; private set; }       // 持久化在 prefs.json
    public string StatusText { get; private set; }           // 已本地化的短状态
    public string? LastError { get; private set; }
    public event Action? StateChanged;

    public Task<SimpleReadyCheck> CheckAsync();
    public Task<SimpleLaunchPlan> BuildPlanAsync();
    public Task<bool> StartAsync(IProgress<string>? progress, CancellationToken ct);
    public Task<bool> StopAsync();
    public SimpleProgress? Progress { get; }                 // 录制中的时长/剩余
}
```

`StartAsync` 的分支：

| 条件 | 行为 |
|---|---|
| 未连 OBS 且 OBS 进程未运行，`basic.ini` 可写 | 落地（备份 + 校验）→ `LaunchingObs` → `WaitingObs`（≤75s，每 1s 轮询）→ `Recording` |
| 未连 OBS 但 OBS 进程在运行 | 不落地、不拉起，返回可照做的原因（去 OBS 开 WebSocket 或退出 OBS） |
| 已连 OBS | 落地（WebSocket 通道）→ `Recording` |
| 录制中 / 推流中 | `Blocked`，直接返回 |

### 2.6 `Controls/SimpleRecordCard.xaml(.cs)`（新控件）

- 标题 + 一句话说明 + 预设三选（默认取 `SimpleRecordingService.Preset`）；
- 「现在能不能录」一行结论（`SimpleReadyCheck`）；
- 主按钮：**开始简单录像 / 停止并保存**（按 `Step` 与 OBS 状态切换）；
- 进度文本（落地 / 拉起 / 等待就绪）；
- 录制中：时长、剩余可录、已写文件大小；
- 停止后：文件已生成 + 「打开目录」「转成 MP4」（`RecordingToolsService`）；
- 折叠区：逐项「当前 → 建议」列表（复用 `RecordingEnvItem` 的展示口径）+ 「高级设置」跳搭建页。

### 2.7 页面与托盘接入

| 文件 | 改动 |
|---|---|
| `Views/HomePage.xaml(.cs)` | 顶部插入 `<ctl:SimpleRecordCard x:Name="SimpleRecord" />`；`OnNavigatedToAsync` 调 `RefreshAsync()` |
| `Views/ConsolePage.xaml(.cs)` | 输出区新增录制信息行（时长 / 剩余可录 / 文件）；`RenderTimer` 同级新增 `RenderRecordingProgress()`，由既有 `StateChanged` 驱动 |
| `Views/SettingsPage.xaml(.cs)` | 「录制」段：默认预设下拉 + 「文件通道时允许自动启动 OBS 并开录」开关（持久化 `prefs.json`） |
| `Services/Shell/TrayService.cs` | 菜单新增「打开录像目录」，并让托盘 ToolTip 在录制中带上已录时长 |
| `MainWindow.xaml(.cs)` | 语言切换时把当前页面原地重放一次（既有机制）已覆盖新控件；无需新代码 |

### 2.8 版本与文案

| 文件 | 改动 |
|---|---|
| `OBS_Helper.Wpf.csproj` | `<Version>2.9.4</Version>` / `FileVersion` / `AssemblyVersion` |
| `OBS_Helper_Setup.iss` | `#define MyAppVersion "2.9.4"` |
| `Localization/StringTableZhHans.cs` | 新增 `simple.*` / `settings.record.*` / `tray.openRecordDir` / `recording.*` 文案 |
| `Localization/StringTableEnUs.cs` | 同键集英文 |
| `README.md` | 顶部「当前版本」与「V2.9.4 新增」段 |
| `RELEASE_NOTES_v2.9.4.md`（新） | 发布说明 |
| `OBS_Helper.Wpf/RELEASE_NOTES_v2.9.4.md`（新） | 工程内发布说明（与上一版同位置） |
| `docs/CODEBASE.md` / `docs/ARCHITECTURE.md` | 文件清单与架构段落更新 |

---

## 3. 文案键清单（中英必须成对）

前缀 `simple.`（新增）：

```
simple.home.title / .desc
simple.preset.label
simple.preset.quick / .meeting / .game
simple.preset.quick.desc / .meeting.desc / .game.desc
simple.check.checking / .ready / .blocked / .hint
simple.check.obsNotConnected / .obsRunning / .streaming / .warns
simple.start / .starting / .stop / .stopping
simple.state.preparing / .launching / .waiting / .recording / .stopped
simple.launch.title / .message / .yes / .no
simple.wait.timeout
simple.record.elapsed / .remaining / .fileSize / .unknown
simple.stopped.title / .message / .noFile
simple.openDir / .remux / .remuxNoFfmpeg
simple.summary / .advanced / .rescan / .cancel
simple.error.notFound / .launchFailed / .startFailed / .stopFailed / .backupFailed
simple.blocked.streaming / .obsRunning / .noProfile / .noConfig
simple.needRestart
settings.record.title / .preset / .presetDesc / .autoLaunch / .autoLaunchDesc
tray.openRecordDir
```

复用既有键（不新增）：`env.*`、`preflight.*`、`recording.*`、`console.*`、`common.*`。

---

## 4. 单测清单（`OBS_Helper.Wpf.Tests/`）

| 文件 | 钉什么 |
|---|---|
| `SimpleRecordingCoreTests.cs`（新） | 三档预设的数量/顺序/键唯一；`Parse` 归一化（大小写、未知、空）；`BuildItems` 的键顺序与取值；**只出现录制相关参数**（`DoesNotContain` Mode / StreamEncoder / VBitrate / RecEncoder）；`Game` 才写分段；`Check` 的每一种阻断原因；`Estimate` 的边界（0 码率不除零、0 剩余、低空间阈值）；`NeedsObsRestart` |
| `ObsLaunchCoreTests.cs`（新） | `DisplayIcon` 的 4 种形态（带引号 + `,0` / 无引号 / 只有路径 / 空）；`ExeFromInstallDir` 的 3 个候选顺序；`ResolveCandidates` 去重与存在性过滤 |
| `RecordingEnvCoreTests.cs`（改） | `StaticTargets` 与 `Build` 的键集合一致；`Build` 的既有断言全部保持 |
| `StringsTests.cs`（既有机制） | 新增键自动纳入「键集一致 / 非空 / 英文无汉字」检查 |

## 5. 验收口径

1. `dotnet test` 全绿（含新增用例）；
2. `dotnet build` 两个 TFM 均成功；
3. `python NOBS/scripts/check_resources.py` 全绿（XAML 资源引用可解析）；
4. `OBS_SELFTEST=1` 自检全 PASS（新控件进首页，不新增路由）；
5. ⚠️ **真机未验证项如实标注**：预设取值对真实 OBS 的落地效果、拉起 OBS 的实际路径识别、
   「文件通道 → 拉起 → 自动开录」的端到端时延。审查记录里必须写明这些是 `[人工]` 证据。
