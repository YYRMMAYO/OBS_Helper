using System.IO;
using System.Windows;
using System.Windows.Threading;
using OBS_Helper.Wpf.Models.Obs;
using OBS_Helper.Wpf.Services.Host;
using OBS_Helper.Wpf.Services.Obs;
using OBS_Helper.Wpf.Services.ObsConfig;
using OBS_Helper.Wpf.Services.Recording;
using OBS_Helper.Wpf.Services.Tools;

namespace OBS_Helper.Wpf.Services.Shell;

/// <summary>一次文件通道落地的结果。</summary>
internal sealed record SimpleFileApplyResult(
    bool Ok,
    string? Error,
    IReadOnlyList<RecordingRollbackEntry> Rollback);

/// <summary>
/// 「简单录像」的服务层（V2.9.4）：把「自检 → 落地 → （拉起 OBS）→ 开录 → 录中信息 → 停止收尾」
/// 串成一次点击。
///
/// 刻意不做的事：
/// <list type="bullet">
///   <item><b>不碰输出模式 / 编码器族 / 码率 / 推流参数</b>：那些是用户自己的选择，
///         改了会连带影响他正在用的工作流（与 V2.9.3 一键部署同一口径）；</item>
///   <item><b>不在录制 / 推流中动手</b>：硬阻断，不是提示；</item>
///   <item><b>不静默</b>：任何写入都先整备份，可回滚；拉不起 OBS 时如实说「找不到」而不是猜路径。</item>
/// </list>
///
/// 线程约定：服务在 UI 线程构造（<c>AppServices</c> 的惰性字段），
/// 外部事件（OBS 状态变化）经 <see cref="Dispatcher"/> 转回 UI 线程再改动自身状态。
/// </summary>
public sealed class SimpleRecordingService : IDisposable
{
    /// <summary>等 obs-websocket 就绪的上限：OBS 冷启动 + 插件加载通常 3~8 秒，给足余量。</summary>
    public const int ObsReadyTimeoutSeconds = 75;

    private readonly ObsPathService _paths;
    private readonly RecordingEnvService _recEnv;
    private readonly RecordingToolsService _recTools;
    private readonly ObsConnectionService _obs;
    private readonly TrayService _tray;
    private readonly LocalStore _store;

    private readonly Dispatcher? _dispatcher;
    private DispatcherTimer? _tick;

    private SimplePresetId _preset = SimplePresetId.Quick;
    private SimpleReadyCheck _ready = new(false, null, null, 0, 0);
    private SimpleLaunchPlan? _plan;
    private bool _busy;

    public SimpleRecordingService(
        ObsPathService paths,
        RecordingEnvService recEnv,
        RecordingToolsService recTools,
        ObsConnectionService obs,
        TrayService tray,
        LocalStore store)
    {
        _paths = paths;
        _recEnv = recEnv;
        _recTools = recTools;
        _obs = obs;
        _tray = tray;
        _store = store;

        // 无界面自检模式下 Application.Current 为 null：此时不需要封送，直接原地执行
        _dispatcher = Application.Current?.Dispatcher;

        _preset = SimpleRecordingCore.Parse(tray.Settings.SimpleRecordPreset);
        _obs.StateChanged += OnObsStateChanged;
    }

    /// <summary>状态或进度变化（已封送到 UI 线程）。</summary>
    public event Action? StateChanged;

    public SimpleRecordingStep Step { get; private set; } = SimpleRecordingStep.Idle;

    /// <summary>当前选中的预设。</summary>
    public SimplePresetId Preset
    {
        get => _preset;
        set
        {
            if (_preset == value) return;
            _preset = value;
            try
            {
                _tray.Settings.SimpleRecordPreset = SimpleRecordingCore.Get(value).Key;
                _tray.SaveSettings();
            }
            catch (Exception ex)
            {
                FileLogger.Warn("SimpleRecord", $"保存预设失败：{ex.Message}");
            }
            Notify();
        }
    }

    /// <summary>当前预设的完整定义。</summary>
    public SimplePreset CurrentPreset => SimpleRecordingCore.Get(_preset);

    /// <summary>最近一次就绪判定的结论。</summary>
    public SimpleReadyCheck Ready => _ready;

    /// <summary>最近一次构建的落地计划（可能为 null：还没检查过）。</summary>
    public SimpleLaunchPlan? Plan => _plan;

    /// <summary>给用户看的短状态（已本地化）。</summary>
    public string StatusText { get; private set; } = "";

    /// <summary>最近一次失败的原因（已本地化）；没有失败时为 null。</summary>
    public string? LastError { get; private set; }

    /// <summary>是否正在执行开始 / 停止（期间界面应禁用按钮）。</summary>
    public bool Busy => _busy;

    /// <summary>OBS 是否正在录制。</summary>
    public bool IsRecording => _obs.RecordStatus.Active;

    /// <summary>停止后本次录制落盘的文件路径（存在且可读才给值）。</summary>
    public string? LastOutputFile { get; private set; }

    /// <summary>停止后本次录制落盘文件的大小（MB）；读不到为 0。</summary>
    public double LastOutputMb { get; private set; }

    /// <summary>本次录制是否真的产出了文件。</summary>
    public bool LastStopProducedFile { get; private set; }

    // ---------------------------------------------------------------- 打点（V3.0 / D3）

    private readonly List<RecordingMarker> _sessionMarkers = new();
    private DateTime _sessionStartUtc;

    /// <summary>本次录制的打点（按位置排序）。</summary>
    public IReadOnlyList<RecordingMarker> SessionMarkers => _sessionMarkers;

    /// <summary>
    /// 在**当前录制位置**打一个点（热键 / 托盘菜单调用）。返回打点位置；不在录制时返回 null。
    ///
    /// 为什么值得做：OBS 官方至今没有「录制中标记精彩瞬间」的能力（issue #13567），
    /// 用户只能事后靠时间戳回忆。打点之后停止时会把章节写进档案，并可一键带章节转 MP4。
    ///
    /// 去重：2 秒内的重复打点会被合并（热键很容易连按两下），见 <see cref="RecordingArchiveCore.AddMarker"/>。
    /// </summary>
    public TimeSpan? AddMarker(string? label = null)
    {
        if (!IsRecording) return null;

        var at = _obs.RecordElapsed;
        var before = _sessionMarkers.Count;

        var merged = RecordingArchiveCore.AddMarker(_sessionMarkers, at, label);
        _sessionMarkers.Clear();
        _sessionMarkers.AddRange(merged);

        if (_sessionMarkers.Count == before) return null;   // 被当成误触合并掉了

        Notify();
        return at;
    }

    /// <summary>本次录制是否打过点。</summary>
    public bool HasMarkers => _sessionMarkers.Count > 0;

    /// <summary>
    /// 只落地录制环境、**不开始录制**（V3.0 / D4：供「开播前体检」的一键处方使用）。
    ///
    /// 复用与 <see cref="StartAsync"/> 完全相同的落地链路（自检 → 计划 → 备份 → 应用 → 回滚表），
    /// 只是不发出 <c>StartRecord</c>。返回 (false, 原因) 表示当前条件下不能落地。
    /// 返回类型用 (bool, string?) 而不是 <see cref="RecordingEnvResult"/>：两条通道各自有自己的结果类型
    /// （文件通道多带一张回滚表），这里只关心「成没成、为什么没成」。
    /// </summary>
    public async Task<(bool Ok, string? Error)> ApplyPresetAsync(
        SimplePresetId preset, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (_busy || IsRecording) return (false, Strings.T("simple.error.startFailed", ""));

        _busy = true;
        try
        {
            Preset = preset;   // 档位选择本身会持久化（用户下次进来还是这个档）

            var check = await CheckAsync().ConfigureAwait(true);
            var legacyLive = _obs.IsConnected && _obs.IsLegacyProtocol;

            _plan = await BuildPlanAsync().ConfigureAwait(true);
            if (!legacyLive && _plan.BlockedReason is { Length: > 0 } blocked)
            {
                LastError = blocked;
                return (false, blocked);
            }

            if (legacyLive)
            {
                // 旧协议改不了设置：如实说明，不假装落地
                LastError = Strings.T("simple.legacy.skipApply");
                return (false, LastError);
            }

            if (!_plan.ViaWebSocket && ObsProcessRunning)
            {
                // 文件通道要求 OBS 没在运行（否则写了会被退出时的 OBS 覆盖）
                LastError = Strings.T("env.blocked.obsRunning");
                return (false, LastError);
            }

            StatusText = Strings.T("simple.state.preparing");
            Notify();

            if (_plan.ViaWebSocket)
            {
                var ws = await ApplyViaWebSocketAsync(_plan, progress, ct).ConfigureAwait(true);
                if (!ws.Ok) LastError = ws.Error;
                Notify();
                return (ws.Ok, ws.Error);
            }

            var file = await ApplyViaFileAsync(_plan, progress).ConfigureAwait(true);
            if (!file.Ok) LastError = file.Error;
            Notify();
            return (file.Ok, file.Error);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            FileLogger.Warn("SimpleRecord", $"落地录制档位失败：{ex.Message}");
            return (false, ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>是否需要「重启 OBS 才生效」的提示（文件通道落地过）。</summary>
    public bool PendingObsRestart { get; private set; }

    /// <summary>
    /// 「要拉起 OBS 了，可以吗？」的确认回调（V2.9.4）。
    ///
    /// 由界面注入（首页卡用 <c>ConfirmDialog</c>）；<b>没有注入时按「不允许」处理</b> ——
    /// 拉起别的程序并自动开始录制是有效副作用的动作，宁可少做一步，也不能在没有界面的路径上
    /// （自检 / 后台调用）悄悄替用户执行。
    /// </summary>
    public Func<bool>? ConfirmLaunch { get; set; }

    /// <summary>文件通道落地时的回滚表（本次会话内有效）。</summary>
    private IReadOnlyList<RecordingRollbackEntry> _lastFileRollback = Array.Empty<RecordingRollbackEntry>();

    public bool HasFileRollback => _lastFileRollback.Count > 0;

    /// <summary>上一会话遗留的待回滚记录（文件通道落地后程序被关掉时会落盘）。</summary>
    public PendingRollbackRecord? PendingRollback { get; private set; }

    /// <summary>是否有「上次会话的改动还没回滚」需要提示用户。</summary>
    public bool HasPendingRollback => PendingRollback is { IsEmpty: false };

    /// <summary>录制中的进度估算；不在录制时为 null。</summary>
    public SimpleProgress? Progress
    {
        get
        {
            if (!IsRecording) return null;
            var elapsed = _obs.RecordElapsed;
            var freeGb = FreeGbOf(CurrentOutputRoot());
            var usedMb = CurrentOutputMb();
            SampleWriteRate(usedMb);
            return SimpleRecordingCore.Estimate(elapsed, freeGb, CurrentPreset.EstimatedBitrateKbps, usedMb, _measuredKbps);
        }
    }

    // ---- 实测写入速率（V3.0 / C3）：用文件增长量校正「还能录多久」 ----

    private DateTime _rateAnchorUtc;
    private double _rateAnchorMb;
    private double _measuredKbps;

    /// <summary>实测写入速率（kbps）；还没有足够样本时为 0。</summary>
    public double MeasuredWriteKbps => _measuredKbps;

    /// <summary>
    /// 用「已写 MB 的增量 ÷ 时间增量」估算真实写入速率。
    ///
    /// 采样口径：至少 5 秒样本才认（太短噪声极大）；文件变小（换盘 / 换文件）时重设锚点；
    /// 结果做一次半衰滑动，避免单次抖动把估算带偏。
    /// 这个值只用于「剩余可录」的估算与展示，不参与任何写配置的决策。
    /// </summary>
    private void SampleWriteRate(double usedMb)
    {
        var now = DateTime.UtcNow;

        if (_rateAnchorUtc == default || usedMb < _rateAnchorMb)
        {
            _rateAnchorUtc = now;
            _rateAnchorMb = usedMb;
            _measuredKbps = 0;
            return;
        }

        var seconds = (now - _rateAnchorUtc).TotalSeconds;
        if (seconds < 5) return;

        var deltaMb = usedMb - _rateAnchorMb;
        _rateAnchorUtc = now;
        _rateAnchorMb = usedMb;
        if (deltaMb <= 0) return;   // 这一轮几乎没写入（暂停 / 静止画面）：保留上一次的估计

        var kbps = deltaMb * 8 * 1024 / seconds;
        _measuredKbps = _measuredKbps <= 0 ? kbps : (_measuredKbps + kbps) / 2;
    }

    /// <summary>开始新一轮录制时丢弃旧的速率样本。</summary>
    private void ResetWriteRateSample()
    {
        _rateAnchorUtc = default;
        _rateAnchorMb = 0;
        _measuredKbps = 0;
    }

    // ---------------------------------------------------------------- 只读检查

    /// <summary>
    /// 读一次现状并给出「现在能不能录」的结论。只读：不写任何文件、不启动任何进程。
    /// </summary>
    public async Task<SimpleReadyCheck> CheckAsync()
    {
        Step = SimpleRecordingStep.Checking;
        Notify();

        try
        {
            // 只读一次环境计划：就绪判定与「要落地什么」都以它为准，避免两次读取之间状态漂移
            var envPlan = await _recEnv.BuildPlanAsync().ConfigureAwait(true);
            _plan = await BuildPlanAsync(envPlan).ConfigureAwait(true);

            var report = await _preflightAsync().ConfigureAwait(true);
            var failCount = report.FailCount;
            var warnCount = report.WarnCount;

            // 文件通道是否真的可写：必须能定位到当前配置集的 basic.ini
            var canWriteFiles = !ObsProcessRunning && envPlan.BasicIniPath is { Length: > 0 } && File.Exists(envPlan.BasicIniPath);

            // 「没有通道可用」有两种完全不同的原因，文案不能混：
            //   · OBS 在跑却没连上 → 去开 WebSocket 或退出 OBS；
            //   · OBS 没跑、但找不到配置目录 / 读不到 basic.ini → 先跑一次 OBS 或手动指定目录。
            string? noChannelReason = null;
            if (!canWriteFiles && !ObsProcessRunning)
            {
                noChannelReason = envPlan.BlockedReason is { Length: > 0 } planBlocked
                    ? planBlocked
                    : Strings.T("simple.blocked.noProfile");
            }

            // 旧协议（obs-websocket 4.x）：连着就说明「有通道可用」—— 录制开关照常，
            // 只是不能在线改设置（见 StartAsync 里的取舍说明）。若不这样处理，
            // 就会被误判成「OBS 在跑但没连上」，把能录的用户挡在门外。
            var legacyLive = _obs.IsConnected && _obs.IsLegacyProtocol;

            _ready = SimpleRecordingCore.Check(
                viaWebSocket: _plan.ViaWebSocket || legacyLive,
                canWriteFiles: canWriteFiles,
                obsProcessRunning: ObsProcessRunning,
                streaming: _obs.StreamStatus.Active,
                recording: _obs.RecordStatus.Active,
                failCount: failCount,
                warnCount: warnCount,
                // 旧协议下「OBS 在跑」不是阻断（我们本来就不打算在线改配置），把计划里的阻断原因丢掉
                blockedReason: legacyLive ? null : _plan.BlockedReason,
                noChannelReason: noChannelReason);
        }
        catch (Exception ex)
        {
            FileLogger.Warn("SimpleRecord", $"就绪检查失败：{ex.Message}");
            _ready = new SimpleReadyCheck(false, Strings.T("simple.error.checkFailed", ex.Message), null, 0, 0);
        }

        Step = _ready.Ready ? SimpleRecordingStep.Idle : SimpleRecordingStep.Blocked;
        StatusText = _ready.Ready ? Strings.T("simple.check.ready") : "";
        LastError = _ready.BlockReason;
        Notify();
        return _ready;
    }

    /// <summary>录前自检（只读）。单独抽出来是为了让就绪检查与界面能复用同一份报告。</summary>
    private Task<PreflightReport> _preflightAsync() => AppServices.Preflight.RunAsync();

    /// <summary>
    /// 构建「这次要落地什么」的计划（只读）。
    /// <paramref name="envPlan"/> 为空时自己读一次环境计划；就绪检查已经读过时直接传进来，
    /// 避免同一个页面刷新里读两遍配置 —— 两次读取之间状态可能漂移。
    /// </summary>
    public async Task<SimpleLaunchPlan> BuildPlanAsync(RecordingEnvPlan? envPlan = null)
    {
        envPlan ??= await _recEnv.BuildPlanAsync().ConfigureAwait(true);
        var snapshot = await _recEnv.ReadSnapshotAsync().ConfigureAwait(true);

        var dir = await _recTools.TryGetRecordingDirAsync().ConfigureAwait(true);
        var targetPath = !string.IsNullOrWhiteSpace(dir.Dir) && dir.Exists
            ? dir.Dir!
            : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

        var items = SimpleRecordingCore.BuildItems(CurrentPreset, snapshot, targetPath);

        return new SimpleLaunchPlan(
            envPlan.ViaWebSocket,
            envPlan.BlockedReason,
            envPlan.BasicIniPath,
            items)
        {
            ConfigDir = envPlan.ConfigDir
        };
    }

    // ---------------------------------------------------------------- 开始 / 停止

    /// <summary>
    /// 一键开始。返回 true 表示「确实进入了录制状态」。
    ///
    /// 分支：
    /// <list type="number">
    ///   <item>已连 OBS → 落地（WebSocket 通道）→ 开录；</item>
    ///   <item>OBS 已退出且配置可写 → 落地（文件通道）→ 按设置决定是否拉起 OBS → 等就绪 → 开录；</item>
    ///   <item>OBS 在跑但没连上 → 不落地不拉起，返回可照做的原因。</item>
    /// </list>
    /// </summary>
    public async Task<bool> StartAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (_busy) return false;

        LastError = null;
        LastOutputFile = null;
        LastStopProducedFile = false;

        if (_obs.RecordStatus.Active)
        {
            Step = SimpleRecordingStep.Recording;
            StartTick();
            Notify();
            return true;
        }

        _busy = true;
        try
        {
            var check = await CheckAsync().ConfigureAwait(true);
            if (!check.Ready)
            {
                LastError = check.BlockReason;
                Step = SimpleRecordingStep.Blocked;
                Notify();
                return false;
            }

            _plan = await BuildPlanAsync().ConfigureAwait(true);

            // 旧协议（obs-websocket 4.x，Win7 上 OBS 27 那一代）：连着，但**改不了设置**
            // （v4 没有 SetProfileParameter），而文件通道又要求 OBS 没在跑。
            // 取舍：不假装能落地，跳过配置写入，直接开录 —— 录制本身在旧协议下完全可用。
            //
            // 审查发现的关键点：必须先算 legacyLive，**再**判 BlockedReason。
            // 旧协议下 WebSocketAvailable 为 false、而 OBS 正在跑，计划会给出
            // 「OBS 在跑但没连上（WebSocket 未开或密码不对）」这条阻断 —— 与实际相反，
            // 且会让下面的 legacyLive 分支永远不可达（本版新增的那条路径形同虚设）。
            var legacyLive = _obs.IsConnected && _obs.IsLegacyProtocol;

            if (!legacyLive && _plan.BlockedReason is { Length: > 0 } blocked)
            {
                LastError = blocked;
                Step = SimpleRecordingStep.Blocked;
                Notify();
                return false;
            }

            // 文件通道 + OBS 没跑：这时候还不能开录，得先把 OBS 拉起来
            var needLaunch = !legacyLive && !_plan.ViaWebSocket && !ObsProcessRunning;

            if (legacyLive)
            {
                StatusText = Strings.T("simple.legacy.skipApply");
                Notify();
            }
            else if (!needLaunch)
            {
                Step = SimpleRecordingStep.SettingUp;
                StatusText = Strings.T("simple.state.preparing");
                Notify();

                var applied = _plan.ViaWebSocket
                    ? await ApplyViaWebSocketAsync(_plan, progress, ct).ConfigureAwait(true)
                    : await ApplyViaFileAsync(_plan, progress).ConfigureAwait(true);

                if (!applied.Ok)
                {
                    LastError = applied.Error ?? Strings.T("simple.error.startFailed", "");
                    Step = SimpleRecordingStep.Blocked;
                    Notify();
                    return false;
                }
            }
            else
            {
                Step = SimpleRecordingStep.SettingUp;
                StatusText = Strings.T("simple.state.preparing");
                Notify();

                var applied = await ApplyViaFileAsync(_plan, progress).ConfigureAwait(true);
                if (!applied.Ok)
                {
                    LastError = applied.Error ?? Strings.T("simple.error.startFailed", "");
                    Step = SimpleRecordingStep.Blocked;
                    Notify();
                    return false;
                }

                PendingObsRestart = true;

                if (!_tray.Settings.SimpleRecordAutoLaunch)
                {
                    LastError = Strings.T("simple.needRestart");
                    Step = SimpleRecordingStep.Blocked;
                    Notify();
                    return false;
                }

                if (!await LaunchAndWaitAsync(progress, ct).ConfigureAwait(true))
                    return false;   // LastError / Step 已在内部设好
            }

            // 到这里通道已就绪：开录并确认状态真的翻转了
            if (!await StartRecordAsync(ct).ConfigureAwait(true)) return false;

            Step = SimpleRecordingStep.Recording;
            StatusText = Strings.T("simple.state.recording");
            PendingObsRestart = false;
            StartTick();
            Notify();
            return true;
        }
        catch (Exception ex)
        {
            FileLogger.Warn("SimpleRecord", $"开始录制异常：{ex.Message}");
            LastError = Strings.T("simple.error.startFailed", ex.Message);
            Step = SimpleRecordingStep.Blocked;
            Notify();
            return false;
        }
        finally
        {
            _busy = false;
            Notify();
        }
    }

    /// <summary>停止录制，并记录本次产出的文件（供「打开目录 / 转 MP4」使用）。</summary>
    public async Task<bool> StopAsync(CancellationToken ct = default)
    {
        if (_busy) return false;
        _busy = true;
        Step = SimpleRecordingStep.Stopping;
        StatusText = Strings.T("simple.state.stopping");
        Notify();

        try
        {
            var r = await _obs.StopRecordAsync().ConfigureAwait(true);
            if (!r.Ok)
            {
                LastError = Strings.T("simple.error.stopFailed", Describe(r));
                Notify();
                return false;
            }

            // 等 OBS 把文件收尾（Moov / 索引写入需要一点时间），再读大小
            for (var i = 0; i < 20 && _obs.RecordStatus.Active; i++)
            {
                await Task.Delay(250, ct).ConfigureAwait(true);
            }

            Step = SimpleRecordingStep.Stopped;
            StatusText = Strings.T("simple.state.stopped");
            CaptureLastOutput();
            StopTick();
            Notify();
            return true;
        }
        catch (Exception ex)
        {
            FileLogger.Warn("SimpleRecord", $"停止录制异常：{ex.Message}");
            LastError = Strings.T("simple.error.stopFailed", ex.Message);
            Notify();
            return false;
        }
        finally
        {
            _busy = false;
            Notify();
        }
    }

    /// <summary>把本次会话记录的回滚表写回 OBS 配置（文件通道）。</summary>
    public async Task<bool> RollbackFileChangesAsync()
    {
        if (_lastFileRollback.Count == 0)
        {
            LastError = Strings.T("simple.rollback.nothing");
            Notify();
            return false;
        }

        try
        {
            var result = await _recEnv.RollbackAsync(_lastFileRollback).ConfigureAwait(true);
            if (!result.Ok)
            {
                LastError = result.Message;
                Notify();
                return false;
            }
            _lastFileRollback = Array.Empty<RecordingRollbackEntry>();
            PendingObsRestart = false;
            if (HasPendingRollback) ClearPendingRollback();
            Notify();
            return true;
        }
        catch (Exception ex)
        {
            LastError = Strings.T("simple.error.rollbackFailed", ex.Message);
            Notify();
            return false;
        }
    }

    /// <summary>停止后把录像转成 MP4（需要 ffmpeg；没有时给替代指引）。</summary>
    public Task<(bool Ok, string Message)> RemuxLastAsync()
        => LastOutputFile is { Length: > 0 } file
            ? RecordingToolsService.RemuxToMp4Async(file)
            : Task.FromResult((false, Strings.T("recording.remux.fileMissing")));

    // ---------------------------------------------------------------- 跨会话回滚

    /// <summary>
    /// 读取上一会话遗留的待回滚记录（启动时由界面触发一次）。
    ///
    /// 为什么需要它：文件通道落地后要重启 OBS 才生效，用户很可能在这之前就关掉了本程序。
    /// 不落盘的话，回滚按钮随进程一起消失，用户只剩同目录那份 .bak 可翻。
    /// </summary>
    public void LoadPendingRollback()
    {
        try
        {
            if (_store.GetItem(SimpleRecordingCore.PendingRestartKey) is null)
            {
                PendingRollback = null;
                return;
            }
            PendingRollback = _store.GetObject<PendingRollbackRecord>(SimpleRecordingCore.PendingRollbackKey);
        }
        catch (Exception ex)
        {
            // 存储里是脏数据：当作没有待回滚记录，绝不让它拦住功能
            FileLogger.Warn("SimpleRecord", $"读取待回滚记录失败：{ex.Message}");
            PendingRollback = null;
        }
    }

    /// <summary>把「上次会话的改动还没回滚」的提示写成一行文案（供界面直接显示）。</summary>
    public string? PendingRollbackNotice()
    {
        if (PendingRollback is not { IsEmpty: false } pending) return null;

        var channel = string.Equals(pending.Channel, SimpleRecordingCore.ChannelWebSocket, StringComparison.Ordinal)
            ? Strings.T("env.viaWs")
            : Strings.T("env.viaFile");

        return SimpleRecordingCore.PendingNoticeText(channel, pending.AppliedAt, pending.Entries.Count);
    }

    /// <summary>恢复上一会话改动前的值。</summary>
    public async Task<bool> RestorePendingRollbackAsync()
    {
        if (PendingRollback is not { IsEmpty: false } pending)
        {
            LastError = Strings.T("simple.rollback.nothing");
            Notify();
            return false;
        }

        try
        {
            var result = await _recEnv.RollbackAsync(pending.Entries).ConfigureAwait(true);
            if (!result.Ok)
            {
                LastError = result.Message;
                Notify();
                return false;
            }

            ClearPendingRollback();
            LastError = null;
            StatusText = Strings.T("simple.pending.restored");
            Notify();
            return true;
        }
        catch (Exception ex)
        {
            LastError = Strings.T("simple.error.rollbackFailed", ex.Message);
            Notify();
            return false;
        }
    }

    /// <summary>丢弃待回滚记录（用户不想恢复）。</summary>
    public void DiscardPendingRollback()
    {
        ClearPendingRollback();
        StatusText = Strings.T("simple.pending.discarded");
        Notify();
    }

    private void ClearPendingRollback()
    {
        PendingRollback = null;
        try
        {
            _store.RemoveItem(SimpleRecordingCore.PendingRestartKey);
            _store.RemoveItem(SimpleRecordingCore.PendingRollbackKey);
        }
        catch (Exception ex)
        {
            FileLogger.Warn("SimpleRecord", $"清除待回滚记录失败：{ex.Message}");
        }
    }

    /// <summary>文件通道落地成功后落盘待回滚记录（下次启动仍可恢复）。</summary>
    private void SavePendingRollback(IReadOnlyList<RecordingRollbackEntry> entries)
    {
        try
        {
            var record = new PendingRollbackRecord(
                CurrentPreset.Key,
                SimpleRecordingCore.ChannelFile,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                entries);
            _store.SetItem(SimpleRecordingCore.PendingRestartKey, "1");
            _store.SetObject(SimpleRecordingCore.PendingRollbackKey, record);
            PendingRollback = record;
        }
        catch (Exception ex)
        {
            // 落盘失败不影响本次会话内的回滚（_lastFileRollback 仍在内存里）
            FileLogger.Warn("SimpleRecord", $"写入待回滚记录失败：{ex.Message}");
        }
    }

    /// <summary>打开录像目录（优先本次文件所在目录，其次 OBS 配置的目录）。</summary>
    public static string? OpenRecordingFolder(string? dir)
        => RecordingToolsService.OpenInExplorer(dir ?? "");

    /// <summary>OBS 进程是否在跑。</summary>
    public bool ObsProcessRunning => _paths.IsObsRunning();

    /// <summary>当前解析到的录像目录（只读）。</summary>
    public async Task<string?> GetRecordingDirAsync()
    {
        var d = await _recTools.TryGetRecordingDirAsync().ConfigureAwait(true);
        return d.Dir;
    }

    // ---------------------------------------------------------------- 落地实现

    /// <summary>WebSocket 通道落地：逐项 SetProfileParameter（与一键部署卡口径一致）。</summary>
    private async Task<SimpleFileApplyResult> ApplyViaWebSocketAsync(
        SimpleLaunchPlan plan, IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report(Strings.T("env.progress.backup"));

        string backupPath;
        try
        {
            backupPath = await AppServices.ObsBackups.CreateBackupAsync(
                Strings.T("backup.reason.simpleRecord"), includeKey: false, includePluginConfig: false, null)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            return new SimpleFileApplyResult(false, Strings.T("simple.error.backupFailed", ex.Message), Array.Empty<RecordingRollbackEntry>());
        }

        progress?.Report(Strings.T("env.progress.applyWs"));

        // 旧值仍从文件读：OBS 改设置会立刻落盘，文件就是当前生效值（与一键部署同一口径）
        var iniText = plan.BasicIniPath is { Length: > 0 } p && File.Exists(p) ? TryRead(p) ?? "" : "";
        var rollback = BuildRollback(plan.Items, iniText);

        foreach (var item in plan.Items)
        {
            foreach (var t in item.Targets)
            {
                try
                {
                    var r = await _obs.RawRequestAsync("SetProfileParameter",
                        new { category = t.Category, parameter = t.Parameter, value = t.Value }, ct)
                        .ConfigureAwait(true);
                    if (!r.Ok)
                        return new SimpleFileApplyResult(false, Describe(r), rollback);
                }
                catch (Exception ex)
                {
                    return new SimpleFileApplyResult(false, ex.Message, rollback);
                }
            }
        }

        await _obs.RefreshAllAsync().ConfigureAwait(true);

        // 不清空上一次的旧值，而是**换成这一次的**：WebSocket 通道的旧值就是「本次落地之前」的真实现状，
        // 比上一次文件通道记的更接近现在。清空的话，用户「先文件通道落地、后来连上 OBS 再落地」
        // 之后就再也回不到用本工具之前的状态了（而界面一直承诺「可一键回滚」）。
        _lastFileRollback = rollback;
        PendingObsRestart = false;
        // WebSocket 通道即时生效，不存在「重启后还要回滚」的问题：清掉上一会话遗留的待办记录
        if (HasPendingRollback) ClearPendingRollback();
        return new SimpleFileApplyResult(true, null, rollback);
    }

    /// <summary>
    /// 文件通道落地：先整备份 → 落一个同目录 .bak → 按<b>参数名</b>幂等写入
    /// → 重新读回逐项校验。与一键部署卡的区别只有两点：备份原因不同、写入是幂等的
    /// （同一个参数最后写一次，避免两轮落地把值写重复）。
    /// </summary>
    private async Task<SimpleFileApplyResult> ApplyViaFileAsync(SimpleLaunchPlan plan, IProgress<string>? progress)
    {
        var path = plan.BasicIniPath;
        if (path is null || !File.Exists(path))
            return new SimpleFileApplyResult(false, Strings.T("env.blocked.noProfile"), Array.Empty<RecordingRollbackEntry>());

        progress?.Report(Strings.T("env.progress.backup"));

        try
        {
            await AppServices.ObsBackups.CreateBackupAsync(
                Strings.T("backup.reason.simpleRecord"), includeKey: false, includePluginConfig: false, null)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            return new SimpleFileApplyResult(false, Strings.T("simple.error.backupFailed", ex.Message), Array.Empty<RecordingRollbackEntry>());
        }

        progress?.Report(Strings.T("env.progress.applyFile"));

        var iniText = TryRead(path) ?? "";
        var rollback = BuildRollback(plan.Items, iniText);

        // 幂等：同一个键只保留最后一次写入的值
        var targets = MapTargets(plan.Items);
        var updated = iniText;
        foreach (var t in targets)
            updated = RecordingEnvCore.PatchIni(updated, t.Category, t.Parameter, t.Value);

        try
        {
            // V3.0：与前两处写 basic.ini 的路径统一 —— 先过路径护栏，再「留 .bak → 临时文件 → 原子替换」
            SafeIniFile.Write(path, updated, plan.ConfigDir ?? "");
        }
        catch (Exception ex)
        {
            return new SimpleFileApplyResult(false, Strings.T("env.writeFailed", ex.Message), rollback);
        }

        // 写盘后重新读回逐项校验：写没写进去不能靠「我以为写对了」
        var readBack = TryRead(path) ?? "";
        foreach (var t in targets)
        {
            var got = RecordingEnvCore.ReadIni(readBack, t.Category, t.Parameter);
            if (!string.Equals(got, t.Value, StringComparison.OrdinalIgnoreCase))
            {
                return new SimpleFileApplyResult(false,
                    Strings.T("env.readBackMismatch", t.Category + "." + t.Parameter, t.Value, got),
                    rollback);
            }
        }

        _lastFileRollback = rollback;
        PendingObsRestart = true;

        // 落盘：文件通道要重启 OBS 才生效，用户很可能在那之前关掉本程序。
        // 不落盘的话重启后回滚按钮就没了，与界面上「可回滚」的承诺不符。
        SavePendingRollback(rollback);
        return new SimpleFileApplyResult(true, null, rollback);
    }

    /// <summary>把「参数名 → 目标值」展开成带分类、同键去重的目标列表（保持首次出现的顺序）。</summary>
    private static List<RecordingEnvTarget> MapTargets(IEnumerable<RecordingEnvItem> items)
    {
        var result = new List<RecordingEnvTarget>();
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            foreach (var t in item.Targets)
            {
                if (seen.TryGetValue(t.Parameter, out var idx))
                {
                    result[idx] = t;   // 同一参数以最后一次为准
                    continue;
                }
                seen[t.Parameter] = result.Count;
                result.Add(t);
            }
        }
        return result;
    }

    /// <summary>记下每个目标的旧值（回滚用）。</summary>
    private static List<RecordingRollbackEntry> BuildRollback(IEnumerable<RecordingEnvItem> items, string iniText)
    {
        var result = new List<RecordingRollbackEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            foreach (var t in item.Targets)
            {
                if (!seen.Add(t.Parameter)) continue;
                result.Add(new RecordingRollbackEntry(t.Category, t.Parameter,
                    RecordingEnvCore.ReadIni(iniText, t.Category, t.Parameter)));
            }
        }
        return result;
    }

    // ---------------------------------------------------------------- 拉起 OBS

    /// <summary>拉起 OBS 并等 obs-websocket 就绪（失败时把原因写进 <see cref="LastError"/>）。</summary>
    private async Task<bool> LaunchAndWaitAsync(IProgress<string>? progress, CancellationToken ct)
    {
        var exe = _paths.FindObsExecutable();
        if (exe is null)
        {
            LastError = Strings.T("simple.error.notFound");
            Step = SimpleRecordingStep.Blocked;
            Notify();
            return false;
        }

        // 先问一次：拉起别的程序并自动开录是有副作用的动作（Release 说明里对用户是这样承诺的）
        if (ConfirmLaunch is null || !ConfirmLaunch())
        {
            // 用户选了「我自己去开 OBS」：这不是错误 —— 参数已经落地了，
            // 只是本工具不去启动 OBS。所以别用「需要重启 OBS」那句把人绕回去，
            // 回到 Idle 并告诉他手动开 OBS 后怎么续上。
            LastError = null;
            Step = SimpleRecordingStep.Idle;
            StatusText = Strings.T("simple.launch.declined");
            Notify();
            return false;
        }

        Step = SimpleRecordingStep.LaunchingObs;
        StatusText = Strings.T("simple.state.launching");
        progress?.Report(StatusText);
        Notify();

        try
        {
            using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            LastError = Strings.T("simple.error.launchFailed", ex.Message);
            Step = SimpleRecordingStep.Blocked;
            Notify();
            return false;
        }

        Step = SimpleRecordingStep.WaitingObs;
        StatusText = Strings.T("simple.state.waiting");
        progress?.Report(StatusText);
        Notify();

        var waited = 0;
        while (waited < ObsReadyTimeoutSeconds)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(1000, ct).ConfigureAwait(true);
            waited++;

            if (_obs.IsConnected) return true;

            // OBS 起来之后又退了（崩溃 / 用户关掉）：没必要再等满 75 秒
            if (!_paths.IsObsRunning())
            {
                LastError = Strings.T("simple.error.launchFailed", Strings.T("simple.error.obsExited"));
                Step = SimpleRecordingStep.Blocked;
                Notify();
                return false;
            }
        }

        LastError = Strings.T("simple.wait.timeout", ObsReadyTimeoutSeconds);
        Step = SimpleRecordingStep.Blocked;
        Notify();
        return false;
    }

    /// <summary>发 StartRecord 并等状态翻转（最多 10 秒）。</summary>
    private async Task<bool> StartRecordAsync(CancellationToken ct)
    {
        var r = await _obs.StartRecordAsync().ConfigureAwait(true);
        if (!r.Ok)
        {
            LastError = Strings.T("simple.error.startFailed", Describe(r));
            Step = SimpleRecordingStep.Blocked;
            Notify();
            return false;
        }

        for (var i = 0; i < 40 && !_obs.RecordStatus.Active; i++)
            await Task.Delay(250, ct).ConfigureAwait(true);

        if (!_obs.RecordStatus.Active)
        {
            LastError = Strings.T("simple.error.startFailed", Strings.T("simple.error.noStateFlip"));
            Step = SimpleRecordingStep.Blocked;
            Notify();
            return false;
        }
        return true;
    }

    // ---------------------------------------------------------------- 进度

    private void StartTick()
    {
        ResetWriteRateSample();   // 新一轮录制：丢掉上一次的写入速率样本
        _sessionMarkers.Clear();  // V3.0（D3）：打点按「本次录制」重新开始
        _sessionStartUtc = DateTime.UtcNow;
        if (_tick is not null) { _tick.Start(); return; }
        _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick += (_, _) => Notify();
        _tick.Start();
    }

    private void StopTick() => _tick?.Stop();

    private void OnObsStateChanged() => Post(() =>
    {
        // V3.0 审查修正：用户从 OBS 界面 / 热键自己开始录制时，也要重置实测速率采样并起表。
        // 原先只在「本服务发起的开录」里调 StartTick，于是从外部开录时锚点还停在上一次录制 ——
        // 采样窗口跨越两次录制，实测速率被严重低估，剩余时间被高估，低空告警不再触发。
        if (_obs.RecordStatus.Active && Step != SimpleRecordingStep.Recording)
        {
            ResetWriteRateSample();
            StartTick();
        }

        if (!_obs.RecordStatus.Active && Step == SimpleRecordingStep.Recording)
        {
            // 用户自己按了停止（热键 / 托盘 / OBS 界面）：本服务同步到「已停止」并记下文件
            Step = SimpleRecordingStep.Stopped;
            StatusText = Strings.T("simple.state.stopped");
            CaptureLastOutput();
            StopTick();
        }
        Notify();
    });

    /// <summary>记录本次产出的文件（只在确实存在且有内容时才认为「录到了」）。</summary>
    private void CaptureLastOutput()
    {
        LastOutputFile = null;
        LastOutputMb = 0;
        LastStopProducedFile = false;

        var file = _obs.LastRecordFile;
        if (string.IsNullOrWhiteSpace(file)) return;

        try
        {
            var info = new FileInfo(file);
            if (!info.Exists) return;
            LastOutputFile = info.FullName;
            LastOutputMb = info.Length / 1024.0 / 1024.0;
            LastStopProducedFile = info.Length > 0;

            RegisterInArchive(info);
        }
        catch (Exception ex)
        {
            FileLogger.Warn("SimpleRecord", $"读取录制文件信息失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 把本次产物登记进录制档案（V3.0 / D3）。
    ///
    /// 放在「停止收尾」里而不是别处：只有这一刻才同时拿得到文件、时长、丢帧与打点。
    /// 时长优先用 OBS 自报的 timecode（暂停不计入），拿不到时退回本地墙钟。
    /// </summary>
    private void RegisterInArchive(FileInfo info)
    {
        try
        {
            var duration = _obs.RecordElapsed;
            if (duration <= TimeSpan.Zero && _sessionStartUtc != default)
            {
                var delta = DateTime.UtcNow - _sessionStartUtc;
                if (delta > TimeSpan.Zero) duration = delta;
            }

            var started = _obs.RecordStartedUtc?.ToLocalTime()
                          ?? (_sessionStartUtc == default ? DateTime.Now : _sessionStartUtc.ToLocalTime());

            AppServices.Archive.Add(new RecordingArchiveEntry(
                Path: info.FullName,
                StartedLocal: started,
                Duration: duration,
                Bytes: info.Length,
                // 录制侧的丢帧口径就是「输出跳过帧占比」（OBS 的 GetRecordStatus 不单列丢帧）
                DroppedRatio: _obs.Stats.OutputSkipRatio,
                Segmented: _obs.RecordFileSwitchCount > 0,
                Markers: _sessionMarkers.ToList()));
        }
        catch (Exception ex)
        {
            // 档案是辅助信息：登记失败不能影响「停止收尾」本身
            FileLogger.Warn("SimpleRecord", $"登记录制档案失败：{ex.Message}");
        }
    }

    /// <summary>当前录像文件所在盘（拿不到就用系统「视频」目录所在的盘）。</summary>
    private string CurrentOutputRoot()
    {
        var file = _obs.LastRecordFile;
        if (!string.IsNullOrWhiteSpace(file))
        {
            try
            {
                var root = Path.GetPathRoot(Path.GetFullPath(file));
                if (!string.IsNullOrEmpty(root)) return root!;
            }
            catch (Exception) { }
        }
        return Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
    }

    /// <summary>当前已写入的字节数（MB）。优先读文件真实长度，读不到退回 OBS 报的 outputBytes。</summary>
    private double CurrentOutputMb()
    {
        var file = _obs.LastRecordFile;
        try
        {
            if (!string.IsNullOrWhiteSpace(file))
            {
                var info = new FileInfo(file);
                if (info.Exists) return info.Length / 1024.0 / 1024.0;
            }
        }
        catch (Exception) { }
        return _obs.RecordStatus.Bytes / 1024.0 / 1024.0;
    }

    private static double FreeGbOf(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root)) return 0;
            return new DriveInfo(root).AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    // ---------------------------------------------------------------- 工具

    private static string Describe(ObsRequestResult r)
        => !string.IsNullOrWhiteSpace(r.Comment) ? r.Comment! : Strings.T("scene.apply.errorCode", r.Code);

    private static string? TryRead(string file)
    {
        try { return File.Exists(file) ? File.ReadAllText(file) : null; }
        catch (Exception) { return null; }
    }

    private void Notify()
    {
        var handler = StateChanged;
        if (handler is null) return;
        if (_dispatcher is null || _dispatcher.CheckAccess()) handler();
        else _dispatcher.BeginInvoke(handler);
    }

    private void Post(Action action)
    {
        if (_dispatcher is null || _dispatcher.CheckAccess()) action();
        else _dispatcher.BeginInvoke(action);
    }

    public void Dispose()
    {
        _obs.StateChanged -= OnObsStateChanged;
        StopTick();
        _tick = null;
    }
}
