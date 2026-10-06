using System.Diagnostics;
using System.Windows.Threading;
using OBS_Helper.Wpf.Services.Host;
using OBS_Helper.Wpf.Services.Obs;
using OBS_Helper.Wpf.Services.ObsConfig;

namespace OBS_Helper.Wpf.Services.Shell;

/// <summary>
/// 「简单开播」（V3.0 / D2）：一次点击把「检查 → （可选）同时录制 → 开播 → 确认真的播出去了」走完。
///
/// 为什么需要它：对新手来说，OBS 里「开始推流」按下去没反应的头号原因是**推流服务没填全**
/// （服务器或串流密钥为空），而 OBS 自己的提示很短、一闪而过。本服务在按之前就把这件事查清楚，
/// 并把「已在下行丢帧 / 拥塞 / 正在同时录制」这类会影响直播的风险提前说出来。
///
/// 边界（与「简单录像」同一套取舍）：
/// <list type="bullet">
///   <item><b>不替用户决定编码与码率</b>：推流参数与平台强相关，猜错比不猜更糟，因此只读不写；</item>
///   <item><b>不声称「已开播」直到 OBS 确认</b>：<c>StartStream</c> 只是「命令被接受」，
///         真正的 <c>outputActive</c> 会稍后才翻转 —— 本服务轮询确认后再改状态；</item>
///   <item><b>读密钥只判存在性</b>：<see cref="SimpleStreamServiceInfo"/> 只带两个布尔，
///         密钥本身不进入本应用的数据模型、日志与界面。</item>
/// </list>
///
/// V3.0 审查（第二轮对抗式复核）后修掉的状态机缺陷：
/// <list type="bullet">
///   <item>回到首页时「已在推流」曾被判成 Blocked，导致唯一按钮变成**禁用的「开始推流」**，
///         用户再也停不掉直播 —— 现在已推流即 <see cref="SimpleStreamStep.Streaming"/>；</item>
///   <item>停播失败/超时曾无条件回到 Streaming，叠加事件守卫会造成**不可自愈的卡死** ——
///         现在一律以 OBS 自报状态为准（<see cref="SimpleStreamingCore.StepAfterStopAttempt"/>）；</item>
///   <item>顺带开的录制曾在「停之前」就清标记，失败后既不重试也不提示，界面说「已停止推流」
///         而 OBS 仍在录 —— 现在只在**确实停掉**后才清标记，失败会保留标记并如实提醒。</item>
/// </list>
/// </summary>
public sealed class SimpleStreamingService : IDisposable
{
    private readonly ObsConnectionService _obs;
    private readonly SimpleRecordingService _record;
    private readonly LocalStore _store;
    private readonly DispatcherTimer _tick;

    private bool _busy;
    private bool _tickRunning;

    /// <summary>本次开播是否由本服务顺带开始了录制 —— 停止时只停「我们自己开的」那个。</summary>
    private bool _recordStartedByUs;

    /// <summary>「开播时同时本地录制」偏好的落盘键。</summary>
    private const string AlsoRecordKey = "simple_stream_also_record";

    public SimpleStreamingService(ObsConnectionService obs, SimpleRecordingService record, LocalStore store)
    {
        _obs = obs;
        _record = record;
        _store = store;

        // 偏好要落盘：卡片上写着「记住」，而用户勾完下次进来却被重置，是最容易被当成 bug 的那种不一致
        AlsoRecord = string.Equals(_store.GetItem(AlsoRecordKey), "1", StringComparison.Ordinal);

        _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick += OnTick;

        _obs.StateChanged += OnObsStateChanged;
    }

    /// <summary>状态变化事件（界面据此刷新；可能在非 UI 线程触发，界面需自行切回）。</summary>
    public event Action? StateChanged;

    // ---------------------------------------------------------------- 只读状态

    public SimpleStreamStep Step { get; private set; } = SimpleStreamStep.Idle;

    /// <summary>一句话结论 / 当前状态文案。</summary>
    public string StatusText { get; private set; } = "";

    /// <summary>能播但要先知道的提醒（无则 null）。</summary>
    public string? WarningText { get; private set; }

    /// <summary>最近一次失败的原因（成功时为 null）。</summary>
    public string? LastError { get; private set; }

    /// <summary>「开播时同时本地录制」—— 默认关（用户显式勾选才做，避免意外的磁盘占用）。</summary>
    public bool AlsoRecord
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            try { _store.SetItem(AlsoRecordKey, value ? "1" : "0"); }
            catch (Exception ex) { FileLogger.Warn("SimpleStream", $"同步录制偏好保存失败：{ex.Message}"); }
        }
    }

    public bool IsStreaming => _obs.StreamStatus.Active;

    /// <summary>已播时长；未在推流时为 <see cref="TimeSpan.Zero"/>。</summary>
    public TimeSpan Elapsed => IsStreaming ? _obs.StreamElapsed : TimeSpan.Zero;

    /// <summary>当前推流码率（kbps）；样本不足时为 0。</summary>
    public double CurrentKbps => SimpleStreamingCore.EstimateKbps(_obs.StreamStatus.Bytes, Elapsed);

    // ---------------------------------------------------------------- 只读检查

    /// <summary>
    /// 读一次现状并给出「现在能不能播」的结论。只读：不写任何配置、不改 OBS 状态。
    /// </summary>
    public async Task<SimpleStreamCheck> CheckAsync()
    {
        // 已在推流：这**不是**「不能播」，而是「正在播」——必须让停止按钮可用。
        // （审查发现这里曾走 Apply 置成 Blocked，导致回首页后按钮变成禁用的「开始推流」。）
        if (_obs.StreamStatus.Active)
        {
            Step = SimpleStreamStep.Streaming;
            StatusText = Strings.T("simple.stream.state.streaming");
            WarningText = _obs.RecordStatus.Active ? Strings.T("simple.stream.warn.recording") : null;
            LastError = null;
            Notify();
            return new SimpleStreamCheck(true, null, WarningText);
        }

        Step = SimpleStreamStep.Checking;
        Notify();

        if (!_obs.IsConnected)
        {
            var offline = SimpleStreamingCore.Check(false, false, false, null);
            Apply(offline);
            return offline;
        }

        var info = await _obs.GetStreamServiceInfoAsync();
        var check = SimpleStreamingCore.Check(
            connected: true,
            streaming: false,
            recording: _obs.RecordStatus.Active,
            service: info,
            congestion: _obs.StreamStatus.Congestion,
            droppedRatio: DroppedRatio());

        Apply(check);
        return check;
    }

    private void Apply(SimpleStreamCheck check)
    {
        WarningText = check.Warning;
        if (check.Ready)
        {
            Step = SimpleStreamStep.Ready;
            StatusText = Strings.T("simple.stream.ready");
            LastError = null;
        }
        else
        {
            Step = SimpleStreamStep.Blocked;
            StatusText = check.BlockReason ?? Strings.T("simple.stream.blocked.unknown");
        }
    }

    private double DroppedRatio()
        => _obs.StreamStatus.TotalFrames > 0
            ? (double)_obs.StreamStatus.SkippedFrames / _obs.StreamStatus.TotalFrames
            : 0;

    // ---------------------------------------------------------------- 开始 / 停止

    /// <summary>一键开播。返回 true 表示 OBS 已确认在推流。永不抛异常（失败写进 <see cref="LastError"/>）。</summary>
    public async Task<bool> StartAsync()
    {
        if (_busy) return false;
        if (_obs.StreamStatus.Active)
        {
            Step = SimpleStreamStep.Streaming;
            StatusText = Strings.T("simple.stream.state.streaming");
            Notify();
            return true;
        }

        _busy = true;
        try
        {
            var check = await CheckAsync();
            if (!check.Ready)
            {
                LastError = check.BlockReason;
                Step = SimpleStreamStep.Blocked;
                Notify();
                return false;
            }

            Step = SimpleStreamStep.Starting;
            StatusText = Strings.T("simple.stream.state.starting");
            Notify();

            // 1) 先起录制（若用户勾了）：录制失败的代价只是「没录上」，
            //    不该因此拦下直播本身 —— 如实提醒后继续开播。
            _recordStartedByUs = false;
            if (AlsoRecord && !_obs.RecordStatus.Active)
            {
                var recorded = await _record.StartAsync();
                _recordStartedByUs = recorded;
                if (!recorded) WarningText = Strings.T("simple.stream.warn.recordFailed", _record.LastError ?? "");
            }

            // 2) 发开播命令
            var r = await _obs.StartStreamAsync();
            if (!r.Ok)
            {
                LastError = r.Comment ?? Strings.T("simple.stream.failed");
                Step = SimpleStreamStep.Blocked;
                StatusText = Strings.T("simple.stream.state.startFailed", LastError);
                await StopOurRecordingAsync();
                Notify();
                return false;
            }

            // 3) 等 OBS 真的开始输出（StartStream 只是「命令被接受」）
            if (!await WaitForStreamStateAsync(expected: true).ConfigureAwait(true))
            {
                LastError = Strings.T("simple.stream.notStarted");
                Step = SimpleStreamStep.Blocked;
                StatusText = LastError;
                await StopOurRecordingAsync();
                Notify();
                return false;
            }

            Step = SimpleStreamStep.Streaming;
            StatusText = Strings.T("simple.stream.state.streaming");
            LastError = null;
            StartTick();
            Notify();
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Step = SimpleStreamStep.Blocked;
            StatusText = Strings.T("simple.stream.state.startFailed", ex.Message);
            try { await StopOurRecordingAsync(); } catch (Exception) { /* 已尽力 */ }
            Notify();
            return false;
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>停止推流；若本次录制是开播时顺带开的，一并停掉（否则用户会一直录下去还不知道）。</summary>
    public async Task<bool> StopAsync()
    {
        if (_busy) return false;

        _busy = true;
        try
        {
            Step = SimpleStreamStep.Stopping;
            StatusText = Strings.T("simple.stream.state.stopping");
            Notify();

            var r = await _obs.StopStreamAsync();
            if (!r.Ok)
            {
                // 命令没被接受 **不等于** 还在推流：先问 OBS 的真实状态再决定（审查发现的卡死路径）
                await _obs.RefreshOutputsAsync().ConfigureAwait(true);
                Step = SimpleStreamingCore.StepAfterStopAttempt(_obs.StreamStatus.Active);
                if (Step == SimpleStreamStep.Streaming)
                {
                    LastError = r.Comment ?? Strings.T("simple.stream.stopFailed");
                    StatusText = LastError;
                    StartTick();
                    Notify();
                    return false;
                }
            }
            else if (!await WaitForStreamStateAsync(expected: false).ConfigureAwait(true))
            {
                // 超时未翻转：同样以 OBS 自报状态为准，而不是凭「命令已发出」宣称停播成功
                await _obs.RefreshOutputsAsync().ConfigureAwait(true);
                if (_obs.StreamStatus.Active)
                {
                    LastError = Strings.T("simple.stream.notStopped");
                    StatusText = LastError;
                    Step = SimpleStreamStep.Streaming;
                    StartTick();
                    Notify();
                    return false;
                }
            }

            await StopOurRecordingAsync();

            StopTick();
            Step = SimpleStreamStep.Stopped;
            StatusText = Strings.T("simple.stream.state.stopped");
            LastError = null;
            Notify();
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            StatusText = ex.Message;
            // 异常路径同样不能凭空断言「还在推流」
            try
            {
                await _obs.RefreshOutputsAsync().ConfigureAwait(true);
                Step = SimpleStreamingCore.StepAfterStopAttempt(_obs.StreamStatus.Active);
                if (Step == SimpleStreamStep.Streaming) StartTick();
            }
            catch (Exception)
            {
                Step = SimpleStreamStep.Streaming;
                StartTick();
            }
            Notify();
            return false;
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// 停掉「本次开播顺带开的」录制。
    ///
    /// 关键顺序（审查发现的缺陷）：**先停、确认停掉之后才清标记**。
    /// 原先先清标记，于是 <c>StopAsync</c> 返回 false（例如录制服务正忙）时既不会重试，
    /// 界面也照样显示「已停止推流」，而 OBS 还在往磁盘录。
    /// </summary>
    private async Task StopOurRecordingAsync()
    {
        if (!_recordStartedByUs) return;

        if (!_obs.RecordStatus.Active)
        {
            _recordStartedByUs = false;   // 已经不在录了（自行结束 / 用户已停）
            return;
        }

        try
        {
            var stopped = await _record.StopAsync();
            await _obs.RefreshOutputsAsync().ConfigureAwait(true);
            if (stopped || !_obs.RecordStatus.Active)
            {
                _recordStartedByUs = false;
                return;
            }

            // 没停掉：保留标记（下次停止推流仍会重试），并如实告诉用户还在录
            WarningText = Strings.T("simple.stream.warn.recordStopFailed");
            FileLogger.Warn("SimpleStream", "停播后未能停止顺带开始的录制，OBS 仍在录制");
        }
        catch (Exception ex)
        {
            WarningText = Strings.T("simple.stream.warn.recordStopFailed");
            FileLogger.Warn("SimpleStream", $"停止顺带录制失败（保留标记，稍后可重试）：{ex.Message}");
        }
    }

    /// <summary>
    /// 轮询等待输出状态翻转到期望值。
    ///
    /// 用**时间预算**（默认 8 秒）而不是固定次数：单次状态查询自身最长可等 10 秒（请求超时），
    /// 按次数循环最坏会拖到约 168 秒，而界面文案承诺的是「8 秒内」——
    /// 这是审查指出的「文案与实现不符」，这里以预算为准。
    /// </summary>
    private async Task<bool> WaitForStreamStateAsync(bool expected, int budgetMs = 8000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < budgetMs)
        {
            await _obs.RefreshOutputsAsync().ConfigureAwait(true);
            if (_obs.StreamStatus.Active == expected) return true;
            await Task.Delay(400).ConfigureAwait(true);
        }

        await _obs.RefreshOutputsAsync().ConfigureAwait(true);
        return _obs.StreamStatus.Active == expected;
    }

    // ---------------------------------------------------------------- 计时器

    private void StartTick()
    {
        if (_tickRunning) return;
        _tickRunning = true;
        _tick.Start();
    }

    private void StopTick()
    {
        _tickRunning = false;
        _tick.Stop();
    }

    /// <summary>
    /// 秒级刷新指标。带重入护栏（<c>async void</c> 事件在本项目里出过事），
    /// 并且**断了连接或已不在推流就自动停表** —— 否则会对着已关闭的 socket 每秒空转（审查指出）。
    /// </summary>
    private async void OnTick(object? sender, EventArgs e)
    {
        if (_busy) return;

        try
        {
            if (!_obs.IsConnected || !_obs.StreamStatus.Active)
            {
                StopTick();
                if (SimpleStreamingCore.ShouldSyncExternalStop(Step) && !_obs.StreamStatus.Active)
                {
                    Step = SimpleStreamStep.Stopped;
                    StatusText = Strings.T("simple.stream.state.stopped");
                }
                Notify();
                return;
            }

            await _obs.RefreshOutputsAsync().ConfigureAwait(true);
            Notify();
        }
        catch (Exception ex)
        {
            // 计时器里的异常绝不能逃逸（会把进程带崩）：停表并留日志
            StopTick();
            FileLogger.Warn("SimpleStream", $"推流指标刷新失败，已停止刷新：{ex.Message}");
        }
    }

    private void OnObsStateChanged()
    {
        try
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                // 录制自行结束（用户按了停止 / 磁盘满 / 出错）后，标记必须复位 ——
                // 否则用户之后自己开的那次录制会在「停止推流」时被顺手停掉（审查发现）。
                if (!_obs.RecordStatus.Active) _recordStartedByUs = false;

                if (!_obs.IsConnected)
                {
                    // 断线时推流状态已由 ObsConnectionService 复位，这里同步状态机并停表
                    StopTick();
                    if (Step is SimpleStreamStep.Streaming or SimpleStreamStep.Starting or SimpleStreamStep.Stopping)
                    {
                        Step = SimpleStreamStep.Idle;
                        StatusText = Strings.T("simple.stream.blocked.notConnected");
                    }
                }
                else if (!_obs.StreamStatus.Active && SimpleStreamingCore.ShouldSyncExternalStop(Step))
                {
                    // 用户从别处（OBS 界面 / 托盘 / 热键）停了推流：同步过来。
                    // 注意不能只认 Streaming —— 点停止后状态先进入 Stopping，此时事件到达若被忽略，
                    // 就会永久卡在「停止推流」上（审查发现的不可自愈缺陷）。
                    StopTick();
                    Step = SimpleStreamStep.Stopped;
                    StatusText = Strings.T("simple.stream.state.stopped");
                    LastError = null;
                }

                Notify();
            }));
        }
        catch (Exception)
        {
            // 退出途中 Dispatcher 已关闭，忽略
        }
    }

    private void Notify() => StateChanged?.Invoke();

    /// <summary>应用退出 / 服务释放：退订事件并停表（原先从不退订，是本项目刚清理过的那类泄漏）。</summary>
    public void Dispose()
    {
        try { _obs.StateChanged -= OnObsStateChanged; } catch (Exception) { }
        try { StopTick(); } catch (Exception) { }
    }
}
