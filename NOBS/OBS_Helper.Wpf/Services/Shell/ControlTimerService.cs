using System.Windows.Threading;
using OBS_Helper.Wpf.Services.Obs;

namespace OBS_Helper.Wpf.Services.Shell;

/// <summary>定时停止的目标。</summary>
public enum TimerTarget
{
    Record,
    Stream
}

/// <summary>一个进行中的定时任务。</summary>
public sealed class ActiveTimer
{
    public required TimerTarget Target { get; init; }
    public required DateTime EndUtc { get; init; }
    public required int TotalSeconds { get; init; }
}

/// <summary>
/// 录制 / 推流定时器：到点自动停止，并在托盘弹出通知。
///
/// 用 <see cref="DispatcherTimer"/> 跑在 UI 线程（服务在 AppServices 中于 UI 线程构造，
/// 控制台页直接用它的倒计时显示，不需要跨线程）。
/// </summary>
public sealed class ControlTimerService : IDisposable
{
    private readonly ObsConnectionService _obs;
    private readonly TrayService _tray;
    private readonly DispatcherTimer _timer;

    private ActiveTimer? _current;
    private DateTime _startUtc;
    private int _remainingSeconds;
    private bool _fired;

    /// <summary>启动后的宽限期：录制/推流的启动是异步的，状态要等 OBS 确认后才刷新，
    /// 这段窗口内不做「目标已手动停止」检查，避免刚启动就误判取消。</summary>
    private static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(10);

    public ControlTimerService(ObsConnectionService obs, TrayService tray)
    {
        _obs = obs;
        _tray = tray;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
    }

    /// <summary>倒计时变化（每秒）或开始 / 结束 / 取消时触发。</summary>
    public event Action? StateChanged;

    public bool IsRunning => _current is not null;

    public ActiveTimer? Current => _current;

    /// <summary>剩余秒数（当前这一秒的近似值）。</summary>
    public int RemainingSeconds => _remainingSeconds;

    /// <summary>开启（或替换）一个定时任务。</summary>
    public void Start(TimerTarget target, TimeSpan duration)
    {
        var total = (int)Math.Max(1, Math.Round(duration.TotalSeconds));
        _current = new ActiveTimer
        {
            Target = target,
            EndUtc = DateTime.UtcNow.AddSeconds(total),
            TotalSeconds = total
        };
        _startUtc = DateTime.UtcNow;
        _remainingSeconds = total;
        _fired = false;
        _timer.Start();
        StateChanged?.Invoke();
    }

    /// <summary>取消当前定时任务。</summary>
    public void Cancel()
    {
        if (_current is null) return;
        _current = null;
        _timer.Stop();
        StateChanged?.Invoke();
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
    }

    // ------------------------------------------------------------ 内部

    private void OnTick(object? sender, EventArgs e)
    {
        if (_current is null)
        {
            _timer.Stop();
            return;
        }

        // 目标已被手动停止 / 连接断开：自动取消定时，避免到点后执行无意义的停止。
        // 启动后的宽限期内不做此检查（状态刷新有延迟，见 GracePeriod）。
        var active = _current.Target == TimerTarget.Record ? _obs.RecordStatus.Active : _obs.StreamStatus.Active;
        if (!active && !_fired && DateTime.UtcNow - _startUtc >= GracePeriod)
        {
            var label = _current.Target == TimerTarget.Record ? "录制" : "推流";
            var was = _current;
            _current = null;
            _timer.Stop();
            _tray.Notify("定时器已取消", $"{label}已手动停止，定时自动停止已取消。");
            StateChanged?.Invoke();
            return;
        }

        _remainingSeconds--;
        StateChanged?.Invoke();

        if (_remainingSeconds > 0) return;

        // 到点：停止输出 + 通知
        _fired = true;
        var target = _current.Target;
        var t = _current;
        _current = null;
        _timer.Stop();

        if (target == TimerTarget.Record)
        {
            _ = FireAndForgetAsync(_obs.StopRecordAsync);
            _tray.Notify("定时停止录制", $"已按定时设置（{t.TotalSeconds / 60} 分钟）自动停止录制。");
        }
        else
        {
            _ = FireAndForgetAsync(_obs.StopStreamAsync);
            _tray.Notify("定时停止推流", $"已按定时设置（{t.TotalSeconds / 60} 分钟）自动停止推流。");
        }
        StateChanged?.Invoke();
    }

    private static async Task FireAndForgetAsync(Func<Task<Models.Obs.ObsRequestResult>> action)
    {
        try { await action(); }
        catch (Exception) { /* 停止失败：状态事件会刷新 UI，托盘通知已给出提示 */ }
    }
}
