using System.Windows.Threading;
using OBS_Helper.Wpf.Models.Obs;
using OBS_Helper.Wpf.Services.Obs;

namespace OBS_Helper.Wpf.Services.Shell;

/// <summary>
/// 录制守护（V2.8，GAP-1）：直播 / 录制时监控三层异常信号，出事立刻托盘强提醒。
///
/// <list type="number">
///   <item>WebSocket 断连时正在录制 → 提醒「连接已断开，录制可能中断」；</item>
///   <item>录制中心跳（GetRecordStatus）连续失败 ≥3 次（约 6 秒）→ 提醒 OBS 疑似假死；</item>
///   <item>自动重连成功后发现录制已被中断 → 提醒确认丢失并引导重启录制。</item>
/// </list>
///
/// 生命周期同 <see cref="ControlTimerService"/>：UI 线程构造、DispatcherTimer 驱动，
/// 心跳请求异步发出并用重入护栏串行化。同类告警只弹一次，状态恢复正常后复位。
/// </summary>
public sealed class RecordWatchdogService : IDisposable
{
    private readonly ObsConnectionService _obs;
    private readonly TrayService _tray;
    private readonly DispatcherTimer _timer;

    /// <summary>心跳轮询间隔。</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    // 观测状态
    private ObsConnectionState _lastState = ObsConnectionState.Disconnected;
    private bool _recording;
    private int _heartbeatFailures;

    // 告警去重：同一类告警在一次异常期间只弹一次
    private readonly HashSet<WatchdogAlertKind> _alerted = new();

    // 心跳重入护栏
    private int _polling;

    public RecordWatchdogService(ObsConnectionService obs, TrayService tray)
    {
        _obs = obs;
        _tray = tray;
        _timer = new DispatcherTimer { Interval = PollInterval };
        _timer.Tick += OnTick;
    }

    /// <summary>启动守护（幂等）。默认随应用启动开启，可在设置中关闭。</summary>
    public void Start()
    {
        if (!Enabled) return;
        _lastState = _obs.State;
        _recording = _obs.RecordStatus.Active;
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    /// <summary>设置变更后调用：按最新开关决定启停。</summary>
    public void ApplyEnabled()
    {
        if (Enabled) Start();
        else Stop();
    }

    /// <summary>开关读取自 ShellSettings（与托盘共用一份持久化配置）。</summary>
    public bool Enabled => _tray.Settings.RecordWatchdogEnabled;

    public void Dispose() => Stop();

    // ------------------------------------------------------------ 内部

    private void OnTick(object? sender, EventArgs e)
    {
        var state = _obs.State;
        var connected = state == ObsConnectionState.Connected;
        var reconnected = _lastState == ObsConnectionState.Reconnecting && state == ObsConnectionState.Connected;
        var wasRecording = _recording;
        _lastState = state;

        // 维护「应然录制」状态：只置位、不在此处清零——
        // 清零只由心跳带回的实际状态做（事件驱动的 RecordStatus 刷新可能先于用户意图到达，
        // 例如定时器刚发出 StopRecord 的窗口期，贸然清零会漏掉「重连后录制丢失」的判定）。
        if (connected && _obs.RecordStatus.Active) _recording = true;

        // 异步心跳成功后会带回落定的实际录制状态，交给回调统一判定
        if (_recording && connected)
        {
            _ = PollHeartbeatAsync(reconnected);
            return;
        }

        // 不需要心跳的路径：直接用当前快照判定
        DecideAndNotify(wasRecording, connected, reconnected, recordActiveNow: _obs.RecordStatus.Active);
    }

    /// <summary>向 OBS 查一次录制状态作为心跳。失败累计，成功清零。</summary>
    private async Task PollHeartbeatAsync(bool reconnected)
    {
        // believed = 本轮判定用的「应然状态」；实际状态只在判定后回写，避免污染本轮结论
        var believed = _recording;
        var observedActive = _obs.RecordStatus.Active;
        var heartbeatOk = false;

        if (Interlocked.Exchange(ref _polling, 1) == 1)
        {
            DecideAndNotify(believed, true, reconnected, observedActive);
            return;
        }

        try
        {
            var r = await _obs.RawRequestAsync("GetRecordStatus").ConfigureAwait(true);
            if (r.Ok && r.Data is { } d && d.TryGetProperty("outputActive", out var a))
            {
                _heartbeatFailures = 0;
                heartbeatOk = true;
                observedActive = a.ValueKind == System.Text.Json.JsonValueKind.True;

                // 心跳正常说明连接健康：断连类告警可以复位了
                _alerted.Remove(WatchdogAlertKind.ConnectionLostWhileRecording);
            }
            else
            {
                _heartbeatFailures++;
            }
        }
        catch (Exception)
        {
            _heartbeatFailures++;
        }
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
        }

        DecideAndNotify(believed, _obs.IsConnected, reconnected, observedActive);

        // 判定完成后才同步「实然状态」：连接健康时以 OBS 实际回答为准（用户手动停录 → 解除守护）
        if (heartbeatOk)
        {
            _recording = observedActive;
        }
    }

    private void DecideAndNotify(bool recording, bool connected, bool reconnected, bool recordActiveNow)
    {
        WatchdogDecision decision;
        try
        {
            decision = RecordWatchdogCore.Evaluate(recording, connected, reconnected, recordActiveNow, _heartbeatFailures);
        }
        catch (Exception ex)
        {
            FileLogger.Warn("RecordWatchdog", $"判定异常: {ex.Message}");
            return;
        }

        if (!decision.Alert)
        {
            // 一切正常：心跳恢复时清空失败计数并允许后续再次告警
            if (connected && _heartbeatFailures == 0) _alerted.Clear();
            return;
        }

        if (!_alerted.Add(decision.Kind)) return;

        _tray.Notify(decision.Title, decision.Message);
        FileLogger.Warn("RecordWatchdog", $"{decision.Title}");
    }
}
