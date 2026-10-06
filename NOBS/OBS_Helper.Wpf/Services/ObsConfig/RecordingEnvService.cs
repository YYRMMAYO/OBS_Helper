using System.IO;
using System.Text;
using System.Text.Json;
using OBS_Helper.Wpf.Models.Obs;
using OBS_Helper.Wpf.Services.Obs;

namespace OBS_Helper.Wpf.Services.ObsConfig;

/// <summary>一键部署的「计划」：要么走 obs-websocket，要么改 basic.ini。</summary>
public sealed record RecordingEnvPlan(
    /// <summary>走 obs-websocket（OBS 在跑且已连上）。</summary>
    bool ViaWebSocket,
    /// <summary>不能落地时的原因文案（已经本地化）；可落地时为空。</summary>
    string? BlockedReason,
    /// <summary>推荐的配置集下 basic.ini 路径（文件通道用）。</summary>
    string? BasicIniPath,
    /// <summary>OBS 配置目录（缺省时为空）。</summary>
    string? ConfigDir,
    IReadOnlyList<RecordingEnvItem> Items);

/// <summary>单个推荐项的落地结果。</summary>
public sealed record RecordingEnvStepResult(string Key, bool Ok, string? Error);

// 回滚条目（RecordingRollbackEntry）与「上次会话待回滚记录」都是纯数据模型，
// 放在 RecordingEnvCore.cs 里 —— 那个文件零 WPF 依赖、被单测工程直接链接，
// 模型放那里才能被纯逻辑与测试一起用（V2.9.4）。

/// <summary>一次一键部署的结果。</summary>
public sealed record RecordingEnvResult(
    bool Ok,
    string? BackupPath,
    IReadOnlyList<RecordingEnvStepResult> Steps,
    IReadOnlyList<RecordingRollbackEntry> Rollback,
    string Message,
    string? Error);

/// <summary>
/// 一键部署录制环境（V2.9.3）。
///
/// 落地通道（按可用性自动选择，双通道都先自动备份）：
/// <list type="number">
///   <item><b>obs-websocket</b>（首选，OBS 在跑且已连接）：逐参数发 <c>SetProfileParameter</c>，
///         改完立即生效、不需要重启 OBS；</item>
///   <item><b>basic.ini 文件层</b>（OBS 已退出）：直接改当前配置集下的 <c>basic.ini</c>，
///         写盘前先整体备份，写盘后重新读回校验；采样率这类只有 WebSocket 通道能改的项会跳过。</item>
/// </list>
///
/// <b>为什么不做静默全自动</b>：写的是用户自己的 OBS 配置，任何一项都可能影响他正在用的工作流，
/// 因此这里只提供「清单 + 勾选 + 备份 + 回滚」，绝不自作主张。
/// </summary>
public sealed class RecordingEnvService
{
    private readonly ObsPathService _paths;
    private readonly ObsBackupService _backups;
    private readonly ObsConnectionService _obs;
    private readonly RecordingToolsService _recTools;

    public RecordingEnvService(ObsPathService paths, ObsBackupService backups,
        ObsConnectionService obs, RecordingToolsService recTools)
    {
        _paths = paths;
        _backups = backups;
        _obs = obs;
        _recTools = recTools;
    }

    /// <summary>
    /// 走 obs-websocket 落地是否可用。
    ///
    /// V3.0：旧协议（obs-websocket 4.x）<b>没有 <c>SetProfileParameter</c></b>，因此即便连着也改走文件通道 ——
    /// 文件通道本来就有整备份 / 写后读回 / 可回滚，功能不打折，只是要重启 OBS 才生效。
    /// 这正是「向下部分兼容」的取舍：不假装支持不存在的请求。
    /// </summary>
    public bool WebSocketAvailable => _obs.IsConnected && !_obs.IsLegacyProtocol
        && !_obs.RecordStatus.Active && !_obs.StreamStatus.Active;

    /// <summary>OBS 进程是否在跑（文件通道要求它没跑）。</summary>
    public bool ObsProcessRunning => _paths.IsObsRunning();

    /// <summary>构建推荐计划：读当前配置 → 算出「当前值 → 推荐值」。</summary>
    public async Task<RecordingEnvPlan> BuildPlanAsync(CancellationToken ct = default)
    {
        var loc = await _paths.LocateAsync().ConfigureAwait(false);
        if (!loc.Exists)
            return new RecordingEnvPlan(false, Strings.T("env.blocked.noConfig"), null, null,
                new List<RecordingEnvItem>());

        string? basicIniPath = null;
        var snapshot = new RecordingEnvSnapshot();
        var iniText = "";
        try
        {
            var globalIni = TryRead(Path.Combine(loc.ConfigDir, "global.ini"));
            var ini = PreflightCheckCore.ParseIni(globalIni ?? "");
            ini.TryGetValue("basic.profiledir", out var rawProfileDir);
            // V3.0：净化后再拼接（同 ResolveBasicIniPath 的理由，见那里的注释）
            var profileDir = ObsSafePath.SafeProfileDir(rawProfileDir);
            if (profileDir is not null)
            {
                var candidate = Path.Combine(loc.ConfigDir, "basic", "profiles", profileDir, "basic.ini");
                try
                {
                    ObsSafePath.AssertWritable(candidate, loc.ConfigDir);
                    basicIniPath = candidate;
                }
                catch (ObsSafePathException ex)
                {
                    FileLogger.Warn("RecordingEnv", "配置集路径未通过护栏，按「未配置」处理：" + ex.Message);
                }
            }

            if (basicIniPath is not null) iniText = TryRead(basicIniPath) ?? "";
            snapshot = ReadSnapshot(iniText);
        }
        catch (Exception ex)
        {
            FileLogger.Warn("RecordingEnv", "读取 OBS 配置失败：" + ex.Message);
        }

        // 录像目录：现成的可用目录优先，否则回退系统「视频」文件夹。
        var dir = await _recTools.TryGetRecordingDirAsync().ConfigureAwait(false);
        var targetPath = !string.IsNullOrWhiteSpace(dir.Dir) && dir.Exists
            ? dir.Dir!
            : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

        var items = RecordingEnvCore.Build(snapshot, targetPath);

        // 通道判定顺序（V2.9.4 起第一条是硬阻断）：
        // 正在录制 / 推流 → 一律不改（改了会打断用户正在进行的工作）；
        // 连上 OBS 走 WebSocket；没连但 OBS 也没跑，才允许改文件；
        // OBS 在跑却没连上（没开 WebSocket / 密码不对）时两者都不能用 —— 直接说清楚。
        if (_obs.RecordStatus.Active || _obs.StreamStatus.Active)
            return new RecordingEnvPlan(false, Strings.T("env.blocked.recordingOrStreaming"),
                basicIniPath, loc.ConfigDir, items);

        if (WebSocketAvailable)
            return new RecordingEnvPlan(true, null, basicIniPath, loc.ConfigDir, items);

        if (ObsProcessRunning)
            return new RecordingEnvPlan(false, Strings.T("env.blocked.obsRunning"), basicIniPath, loc.ConfigDir, items);

        if (basicIniPath is null || !File.Exists(basicIniPath))
            return new RecordingEnvPlan(false, Strings.T("env.blocked.noProfile"), basicIniPath, loc.ConfigDir, items);

        return new RecordingEnvPlan(false, null, basicIniPath, loc.ConfigDir, items);
    }

    /// <summary>
    /// 只读读一次当前录制环境快照（V2.9.4）。与 <see cref="BuildPlanAsync"/> 读的是同一套键，
    /// 只是不带「推荐值」，供「简单录像」的就绪判定与展示使用。任何失败都降级为空快照。
    /// </summary>
    public async Task<RecordingEnvSnapshot> ReadSnapshotAsync()
    {
        try
        {
            var loc = await _paths.LocateAsync().ConfigureAwait(false);
            if (!loc.Exists) return new RecordingEnvSnapshot();

            var basicIni = ResolveBasicIniPath(loc.ConfigDir);
            if (basicIni is null) return new RecordingEnvSnapshot();

            return ReadSnapshot(TryRead(basicIni) ?? "");
        }
        catch (Exception ex)
        {
            FileLogger.Warn("RecordingEnv", "读取录制环境快照失败：" + ex.Message);
            return new RecordingEnvSnapshot();
        }
    }

    /// <summary>
    /// 解析当前配置集下 basic.ini 的<b>实际路径</b>（不存在返回 null）。
    /// 与 <see cref="ResolveBasicIniPathAsync"/> 同一套链路，抽出来给只读调用方复用。
    /// </summary>
    private static string? ResolveBasicIniPath(string configDir)
    {
        try
        {
            var ini = PreflightCheckCore.ParseIni(TryRead(Path.Combine(configDir, "global.ini")) ?? "");
            if (!ini.TryGetValue("basic.profiledir", out var rawProfileDir)) return null;
            // V3.0：profile 目录名来自本机 global.ini（而「导入备份包」可以整份改写它），
            // 必须先净化再拼接 —— 否则根化路径会丢弃前缀、`..` 会逃出配置目录。
            var profileDir = ObsSafePath.SafeProfileDir(rawProfileDir);
            if (profileDir is null) return null;
            var path = Path.Combine(configDir, "basic", "profiles", profileDir, "basic.ini");
            // 解析后再确认一次仍在配置目录之内（双保险，防净化被绕过）
            ObsSafePath.AssertWritable(path, configDir);
            return File.Exists(path) ? path : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 应用勾选的推荐项。<paramref name="selectedKeys"/> 为空表示「全部勾选」。
    /// 无论走哪条通道，都会先做一次整体备份；备份失败即中止，绝不在没有退路的情况下改用户配置。
    /// </summary>
    public async Task<RecordingEnvResult> ApplyAsync(
        RecordingEnvPlan plan, IReadOnlyCollection<string> selectedKeys,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (plan.BlockedReason is not null)
            return Fail(plan.BlockedReason);

        // 执行点复查（V2.9.4）：计划可能是几分钟前建的（卡片进页面时读一次），
        // 这期间用户完全可能按了录制热键。只在「建计划时」守卫等于没有守卫。
        if (_obs.RecordStatus.Active || _obs.StreamStatus.Active)
            return Fail(Strings.T("env.blocked.recordingOrStreaming"));

        var chosen = plan.Items
            .Where(i => selectedKeys.Count == 0 || selectedKeys.Contains(i.Key))
            .Where(i => !(plan.ViaWebSocket == false && i.WebSocketOnly))
            .ToList();

        if (chosen.Count == 0)
            return Fail(Strings.T("env.nothingSelected"));

        progress?.Report(Strings.T("env.progress.backup"));
        string backupPath;
        try
        {
            backupPath = await _backups.CreateBackupAsync(
                Strings.T("backup.reason.preRecordingEnv"), includeKey: false, includePluginConfig: false, null)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new RecordingEnvResult(false, null, Array.Empty<RecordingEnvStepResult>(),
                Array.Empty<RecordingRollbackEntry>(), Strings.T("env.backupFailed"), ex.Message);
        }

        // 旧值一律从文件读：OBS 改设置会立刻落盘，文件就是当前生效值；
        // 回滚也按同一份旧值写回，读与写用的是同一套口径。
        var rollback = new List<RecordingRollbackEntry>();
        var iniText = plan.BasicIniPath is not null && File.Exists(plan.BasicIniPath)
            ? TryRead(plan.BasicIniPath) ?? ""
            : "";

        foreach (var item in chosen)
        {
            foreach (var t in item.Targets)
                rollback.Add(new RecordingRollbackEntry(t.Category, t.Parameter,
                    RecordingEnvCore.ReadIni(iniText, t.Category, t.Parameter)));
        }

        progress?.Report(plan.ViaWebSocket ? Strings.T("env.progress.applyWs") : Strings.T("env.progress.applyFile"));

        return plan.ViaWebSocket
            ? await ApplyViaWebSocketAsync(chosen, backupPath, rollback, ct).ConfigureAwait(false)
            : ApplyViaFile(plan, chosen, iniText, backupPath, rollback);
    }

    private async Task<RecordingEnvResult> ApplyViaWebSocketAsync(
        List<RecordingEnvItem> chosen, string backupPath, List<RecordingRollbackEntry> rollback, CancellationToken ct)
    {
        var steps = new List<RecordingEnvStepResult>();
        var allOk = true;

        foreach (var item in chosen)
        {
            string? error = null;
            foreach (var t in item.Targets)
            {
                try
                {
                    var r = await _obs.RawRequestAsync("SetProfileParameter",
                        new { category = t.Category, parameter = t.Parameter, value = t.Value }, ct)
                        .ConfigureAwait(false);
                    if (!r.Ok) { error = Describe(r); break; }
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    break;
                }
            }

            if (error is null) steps.Add(new RecordingEnvStepResult(item.Key, true, null));
            else
            {
                allOk = false;
                // SetProfileParameter 是 obs-websocket 5.4 才有的请求：老版本会直接报未知请求，
                // 这时明确告诉用户「去升级 OBS/插件，或关掉 OBS 用文件通道」。
                steps.Add(new RecordingEnvStepResult(item.Key, false, error));
            }
        }

        if (allOk) await _obs.RefreshAllAsync().ConfigureAwait(false);

        return new RecordingEnvResult(allOk, backupPath, steps, rollback,
            allOk ? Strings.T("env.applied.ok") : Strings.T("env.applied.partial"), null);
    }

    private RecordingEnvResult ApplyViaFile(
        RecordingEnvPlan plan, List<RecordingEnvItem> chosen, string iniText,
        string backupPath, List<RecordingRollbackEntry> rollback)
    {
        var path = plan.BasicIniPath!;
        var updated = iniText;
        foreach (var item in chosen)
            foreach (var t in item.Targets)
                updated = RecordingEnvCore.PatchIni(updated, t.Category, t.Parameter, t.Value);

        try
        {
            // V3.0：先过路径护栏（ObsSafePath），再「留 .bak → 临时文件 → 原子替换」。
            // 原来这里是无护栏的两次 File.WriteAllText：既可能写到配置目录之外，也可能留下半截 ini。
            SafeIniFile.Write(path, updated, plan.ConfigDir ?? "");
        }
        catch (Exception ex)
        {
            return new RecordingEnvResult(false, backupPath, Array.Empty<RecordingEnvStepResult>(),
                rollback, Strings.T("env.writeFailed", ex.Message), null);
        }

        // 写盘后重新读回逐项校验：写没写进去不能靠「我以为写对了」。
        var readBack = TryRead(path) ?? "";
        var steps = new List<RecordingEnvStepResult>();
        var allOk = true;
        foreach (var item in chosen)
        {
            var bad = item.Targets
                .Select(t => new { t.Category, t.Parameter, t.Value, Got = RecordingEnvCore.ReadIni(readBack, t.Category, t.Parameter) })
                .FirstOrDefault(x => !string.Equals(x.Got, x.Value, StringComparison.OrdinalIgnoreCase));

            if (bad is null) steps.Add(new RecordingEnvStepResult(item.Key, true, null));
            else
            {
                allOk = false;
                steps.Add(new RecordingEnvStepResult(item.Key, false,
                    Strings.T("env.readBackMismatch", bad.Category + "." + bad.Parameter, bad.Value, bad.Got)));
            }
        }

        return new RecordingEnvResult(allOk, backupPath, steps, rollback,
            allOk ? Strings.T("env.applied.ok") : Strings.T("env.applied.partial"), null);
    }

    /// <summary>
    /// 回滚：把 <see cref="RecordingEnvResult.Rollback"/> 里的旧值写回去。
    /// 已经连上 OBS 走 WebSocket，否则改 basic.ini。
    /// </summary>
    public async Task<RecordingEnvResult> RollbackAsync(
        IReadOnlyList<RecordingRollbackEntry> entries, IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (entries.Count == 0) return Fail(Strings.T("env.rollback.nothing"));

        // 执行点复查（V2.9.4）：回滚也是写用户配置，同样不能在录制 / 推流中做
        if (_obs.RecordStatus.Active || _obs.StreamStatus.Active)
            return Fail(Strings.T("env.blocked.recordingOrStreaming"));

        var loc = await _paths.LocateAsync().ConfigureAwait(false);
        var ws = WebSocketAvailable;

        progress?.Report(ws ? Strings.T("env.progress.rollbackWs") : Strings.T("env.progress.rollbackFile"));

        if (ws)
        {
            var steps = new List<RecordingEnvStepResult>();
            var allOk = true;
            foreach (var e in entries)
            {
                try
                {
                    var r = await _obs.RawRequestAsync("SetProfileParameter",
                        new { category = e.Category, parameter = e.Parameter, value = e.OldValue }, ct)
                        .ConfigureAwait(false);
                    if (!r.Ok) { allOk = false; steps.Add(new RecordingEnvStepResult(e.Parameter, false, Describe(r))); }
                }
                catch (Exception ex)
                {
                    allOk = false;
                    steps.Add(new RecordingEnvStepResult(e.Parameter, false, ex.Message));
                }
            }
            if (allOk) await _obs.RefreshAllAsync().ConfigureAwait(false);
            return new RecordingEnvResult(allOk, null, steps, Array.Empty<RecordingRollbackEntry>(),
                allOk ? Strings.T("env.rollback.ok") : Strings.T("env.applied.partial"), null);
        }

        if (ObsProcessRunning) return Fail(Strings.T("env.blocked.obsRunning"));

        var ini = await ResolveBasicIniPathAsync(loc.ConfigDir).ConfigureAwait(false);
        if (ini is null) return Fail(Strings.T("env.blocked.noProfile"));

        // V3.0（原 V2.9.4 审校 S2/M2）：回滚同样要先整备份 —— 它是唯一「无备份、无读回校验」的写盘路径，
        // 而回滚恰恰是用户配置已经出问题时才点的按钮，写坏了没有第二次机会。
        //
        // 但备份是**尽力而为**：备份目录在 %LocalAppData%（系统盘），而 OBS 配置可能在别的盘上。
        // 系统盘满/只读时若因「备份失败」拒绝回滚，等于把用户最后的救命按钮锁上了（审查意见），
        // 因此这里失败只记日志并继续 —— SafeIniFile 仍会在同目录留一份 .obshelper.bak。
        progress?.Report(Strings.T("env.progress.backup"));
        try
        {
            await _backups.CreateBackupAsync(
                Strings.T("backup.reason.preRollback"), includeKey: false, includePluginConfig: false, null)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            FileLogger.Warn("RecordingEnv", $"回滚前的整备份失败（继续回滚，同目录 .obshelper.bak 仍在）：{ex.Message}");
        }

        var text = TryRead(ini) ?? "";
        foreach (var e in entries)
            text = RecordingEnvCore.PatchIni(text, e.Category, e.Parameter, e.OldValue);

        try
        {
            SafeIniFile.Write(ini, text, loc.ConfigDir);
        }
        catch (Exception ex)
        {
            return new RecordingEnvResult(false, null, Array.Empty<RecordingEnvStepResult>(),
                Array.Empty<RecordingRollbackEntry>(), Strings.T("env.writeFailed", ex.Message), null);
        }

        // 写盘后读回逐项校验：回滚「以为写回去了」而实际没写，等于没回滚
        var readBack = TryRead(ini) ?? "";
        var rbSteps = new List<RecordingEnvStepResult>();
        var rbOk = true;
        foreach (var e in entries)
        {
            var got = RecordingEnvCore.ReadIni(readBack, e.Category, e.Parameter);
            if (string.Equals(got, e.OldValue, StringComparison.OrdinalIgnoreCase))
                rbSteps.Add(new RecordingEnvStepResult(e.Parameter, true, null));
            else
            {
                rbOk = false;
                rbSteps.Add(new RecordingEnvStepResult(e.Parameter, false,
                    Strings.T("env.readBackMismatch", e.Category + "." + e.Parameter, e.OldValue, got)));
            }
        }

        return new RecordingEnvResult(rbOk, null, rbSteps,
            Array.Empty<RecordingRollbackEntry>(),
            rbOk ? Strings.T("env.rollback.ok") : Strings.T("env.applied.partial"), null);
    }

    // ------------------------------------------------------------ 辅助

    /// <summary>从 basic.ini 文本里读出一份「当前环境快照」。</summary>
    public static RecordingEnvSnapshot ReadSnapshot(string iniText)
    {
        var mode = RecordingEnvCore.ReadIni(iniText, "Output", "Mode");
        var advanced = string.Equals(mode, "Advanced", StringComparison.OrdinalIgnoreCase);
        var recSection = advanced ? "AdvOut" : "SimpleOutput";

        // 录像格式：OBS 30.2 起用 RecFormat2，旧版用 RecFormat，两个都读一遍取到就用。
        var format = RecordingEnvCore.ReadIni(iniText, recSection, "RecFormat2");
        if (format.Length == 0) format = RecordingEnvCore.ReadIni(iniText, recSection, "RecFormat");

        var recPath = RecordingEnvCore.ReadIni(iniText, recSection, advanced ? "RecFilePath" : "FilePath");

        return new RecordingEnvSnapshot
        {
            OutputMode = mode,
            RecFormat = format,
            RecQuality = RecordingEnvCore.ReadIni(iniText, "SimpleOutput", "RecQuality"),
            RecPath = recPath,
            BaseCx = RecordingEnvCore.ReadIni(iniText, "Video", "BaseCX"),
            BaseCy = RecordingEnvCore.ReadIni(iniText, "Video", "BaseCY"),
            OutCx = RecordingEnvCore.ReadIni(iniText, "Video", "OutputCX"),
            OutCy = RecordingEnvCore.ReadIni(iniText, "Video", "OutputCY"),
            FpsCommon = RecordingEnvCore.ReadIni(iniText, "Video", "FPSCommon"),
            SampleRate = RecordingEnvCore.ReadIni(iniText, "Audio", "SampleRate"),
            RecTracks = RecordingEnvCore.ReadIni(iniText, "SimpleOutput", "RecTracks"),
            RecSplitFile = RecordingEnvCore.ReadIni(iniText, "SimpleOutput", "RecSplitFile"),
        };
    }

    private Task<string?> ResolveBasicIniPathAsync(string configDir)
        => Task.FromResult(ResolveBasicIniPath(configDir));

    private static RecordingEnvResult Fail(string message)
        => new(false, null, Array.Empty<RecordingEnvStepResult>(), Array.Empty<RecordingRollbackEntry>(), message, null);

    private static string Describe(ObsRequestResult r)
        => !string.IsNullOrWhiteSpace(r.Comment) ? r.Comment! : Strings.T("scene.apply.errorCode", r.Code);

    private static string? TryRead(string file)
    {
        try { return File.Exists(file) ? File.ReadAllText(file) : null; }
        catch (Exception) { return null; }
    }
}
