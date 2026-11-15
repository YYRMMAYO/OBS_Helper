using System.Diagnostics;
using System.Windows.Threading;
using OBS_Helper.Wpf.Services.Host;
using OBS_Helper.Wpf.Services.Obs;

namespace OBS_Helper.Wpf.Services.Timing;

/// <summary>
/// 计时窗需要实现的最小接口。服务层不直接 new 窗口，避免「纯逻辑 / 服务 / 视图」三层互相绑死，
/// 也让无界面自检可以在不创建窗口的情况下构造页面。
/// </summary>
public interface ITimerView
{
    bool IsVisible { get; }
    void ShowView();
    void HideView();
    void ApplySettings(TimerSettings settings, IReadOnlyList<TimerScreen> screens);
    void UpdateView(TimerSnapshot snapshot, TimerSettings settings);
    void MoveToScreen(int index);
}

/// <summary>
/// 导播计时器服务（V3.6.0）。
///
/// 职责：
/// <list type="bullet">
///   <item>把 <see cref="TimerMachine"/> 的纯逻辑接到真实时钟、持久化与 UI；</item>
///   <item>用 <see cref="Stopwatch"/> 做单调计时，检测休眠/时钟跳变并补时；</item>
///   <item>持有计时窗生命周期（通过 <see cref="ITimerView"/> 工厂）；</item>
///   <item>可选订阅 OBS 录制/推流事件自动开始/停止（默认关闭）。</item>
/// </list>
///
/// 与既有 <c>ControlTimerService</c>（定时停止录制/推流）区分：本服务只管「导播计时」，
/// 因此组合根里的属性名是 <c>TimerClock</c>，不占用既有的 <c>Timer</c>。
/// </summary>
public sealed class TimerService : IDisposable
{
    private const string SettingsKey = "timer.settings";
    private const string RecordsKey = "timer.records";
    private const string AttributionKey = "timer.attribution";

    private readonly LocalStore _store;
    private readonly ObsConnectionService? _obs;
    private readonly Func<DateTime> _wallClock;
    private readonly Dispatcher? _dispatcher;
    private readonly DispatcherTimer _ticker;
    private readonly Stopwatch _monotonic = Stopwatch.StartNew();
    private readonly TimerMachine _machine = new();

    private readonly List<TimerRecord> _records = new();
    private TimerSettings _settings;
    private Func<ITimerView>? _viewFactory;
    private ITimerView? _view;
    private TimerOverlayServer? _overlay;
    private int _nextRecordId = 1;

    private DateTime _anchorWall;
    private DateTime _lastWall;
    private TimeSpan _lastMono;
    private TimeSpan _sleepBank = TimeSpan.Zero;
    private bool _wasRecording;
    private bool _wasStreaming;
    private bool _disposed;

    public TimerService(LocalStore store, ObsConnectionService? obs = null, Func<DateTime>? wallClock = null)
    {
        _store = store;
        _obs = obs;
        _wallClock = wallClock ?? (() => DateTime.Now);
        // 手机命令可能从 HTTP 线程首次碰到计时器；DispatcherTimer 必须挂在 UI 线程的 Dispatcher 上，
        // 否则后台线程会拿到一个没有消息循环的 Dispatcher，计时永远不刷新。
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _settings = LoadSettings();
        LoadRecords();

        _machine.Mode = _settings.Mode;
        _machine.Planned = TimeSpan.FromSeconds(_settings.PlannedSeconds);

        _anchorWall = _wallClock();
        _lastWall = _anchorWall;
        _lastMono = _monotonic.Elapsed;

        _ticker = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(200),
        };
        _ticker.Tick += (_, _) => Tick();

        if (_obs is not null)
        {
            _wasRecording = _obs.RecordStatus.Active;
            _wasStreaming = _obs.StreamStatus.Active;
            _obs.StateChanged += OnObsStateChanged;
        }
    }

    // ------------------------------------------------------------ 对外状态

    public TimerSettings Settings => _settings;

    public IReadOnlyList<TimerRecord> Records => _records;

    public TimerSnapshot Snapshot => _machine.Snapshot(MachineNow(), _settings.ShowHours);

    public bool IsWindowVisible => _view?.IsVisible == true;

    public bool AttributionAcknowledged
    {
        get => string.Equals(_store.GetItem(AttributionKey), "1", StringComparison.Ordinal);
        set
        {
            if (value) _store.SetItem(AttributionKey, "1");
            else _store.RemoveItem(AttributionKey);
        }
    }

    public event Action? Changed;
    public event Action<TimerRecord>? RecordAdded;

    /// <summary>注入计时窗工厂（由页面 / 主窗口在首次显示时调用）。</summary>
    public void SetViewFactory(Func<ITimerView> factory)
    {
        _viewFactory = factory;
    }

    // ------------------------------------------------------------ 控制

    public void Start()
    {
        if (_disposed) return;
        _machine.Mode = _settings.Mode;
        _machine.Planned = TimeSpan.FromSeconds(_settings.PlannedSeconds);
        _machine.Start(MachineNow());
        EnsureTicker();
        RaiseChanged();
    }

    public void Pause()
    {
        _machine.Pause(MachineNow());
        StopTicker();
        RaiseChanged();
    }

    public void Resume()
    {
        _machine.Resume(MachineNow());
        EnsureTicker();
        RaiseChanged();
    }

    public void ToggleStartPause()
    {
        switch (_machine.State)
        {
            case TimerState.Running:
                Pause();
                break;
            case TimerState.Paused:
                Resume();
                break;
            default:
                Start();
                break;
        }
    }

    public void Stop()
    {
        var record = _machine.Stop(MachineNow(), _nextRecordId);
        StopTicker();
        if (record is not null)
        {
            _nextRecordId = record.Id + 1;
            _records.Insert(0, record);
            if (_records.Count > TimerCore.MaxRecords)
                _records.RemoveRange(TimerCore.MaxRecords, _records.Count - TimerCore.MaxRecords);
            SaveRecords();
            FileLogger.Info("Timer", $"计时停止：计划 {record.PlannedSeconds}s，实际 {record.ActualSeconds}s，超时 {record.OvertimeSeconds}s");
            RecordAdded?.Invoke(record);
        }
        RaiseChanged();
    }

    public void Reset()
    {
        _machine.Reset();
        StopTicker();
        FileLogger.Info("Timer", "用户重置计时（未写入记录）");
        RaiseChanged();
    }

    public void SetMode(TimerMode mode)
    {
        if (_settings.Mode == mode) return;
        _settings.Mode = mode;
        _machine.Mode = mode;
        SaveSettings();
        RaiseChanged();
    }

    public void SetPlanned(TimeSpan planned)
    {
        var seconds = Math.Clamp((int)Math.Floor(planned.TotalSeconds), 0, TimerCore.MaxPlannedSeconds);
        _settings.PlannedSeconds = seconds;
        _machine.Planned = TimeSpan.FromSeconds(seconds);
        SaveSettings();
        RaiseChanged();
    }

    public void UpdateSettings(Action<TimerSettings> mutate)
    {
        mutate(_settings);
        NormalizeSettings();
        _machine.Mode = _settings.Mode;
        if (_machine.State == TimerState.Idle)
            _machine.Planned = TimeSpan.FromSeconds(_settings.PlannedSeconds);
        SaveSettings();
        _view?.ApplySettings(_settings, GetScreens());
        RaiseChanged();
    }

    // ------------------------------------------------------------ 窗口

    public void ShowTimerWindow()
    {
        EnsureView();
        _view?.ShowView();
        _settings.ShowTimerWindow = true;
        SaveSettings();
        RaiseChanged();
    }

    public void HideTimerWindow()
    {
        _view?.HideView();
        _settings.ShowTimerWindow = false;
        SaveSettings();
        RaiseChanged();
    }

    public void ToggleTimerWindow()
    {
        if (IsWindowVisible) HideTimerWindow();
        else ShowTimerWindow();
    }

    public void MoveToNextScreen()
    {
        var screens = GetScreens();
        if (screens.Count <= 1) return;
        var current = TimerCore.NormalizeMonitorIndex(_settings.MonitorIndex, screens);
        var next = (current + 1) % screens.Count;
        _settings.MonitorIndex = next;
        _view?.MoveToScreen(next);
        SaveSettings();
        RaiseChanged();
    }

    /// <summary>给窗口用的屏幕列表（物理像素；窗口层再按 DPI 换算成设备无关单位）。</summary>
    public IReadOnlyList<TimerScreen> GetScreens()
    {
        try
        {
            var all = System.Windows.Forms.Screen.AllScreens;
            if (all.Length == 0) return Array.Empty<TimerScreen>();
            var primaryIndex = Array.FindIndex(all, s => s.Primary);
            return all.Select((s, i) => new TimerScreen(
                i,
                new TimerRect(s.WorkingArea.Left, s.WorkingArea.Top, s.WorkingArea.Width, s.WorkingArea.Height),
                i == (primaryIndex < 0 ? 0 : primaryIndex))).ToList();
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Timer", $"枚举显示器失败：{ex.Message}");
            return Array.Empty<TimerScreen>();
        }
    }

    public TimerRect ResolveWindowBounds(TimerRect? saved)
        => TimerCore.ResolveBounds(saved, GetScreens(), _settings.MonitorIndex,
            _settings.WindowWidth, _settings.WindowHeight);

    /// <summary>窗口拖动/缩放结束后保存物理像素位置（不触发重新定位）。</summary>
    public void SaveWindowBounds(TimerRect bounds)
    {
        _settings.WindowLeft = bounds.Left;
        _settings.WindowTop = bounds.Top;
        _settings.WindowWidth = Math.Max(TimerCore.MinWindowWidth, bounds.Width);
        _settings.WindowHeight = Math.Max(TimerCore.MinWindowHeight, bounds.Height);
        SaveSettings();
    }

    // ------------------------------------------------------------ 记录

    public string CopyTsv()
    {
        try
        {
            return TimerCore.BuildTsv(_records);
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Timer", $"生成 TSV 失败：{ex.Message}");
            return "";
        }
    }

    /// <summary>启动浏览器叠加服务并返回页面地址；失败返回 null（不影响计时）。</summary>
    public string? EnsureOverlayUrl()
    {
        _overlay ??= new TimerOverlayServer(() => Snapshot);
        return _overlay.Start() ? _overlay.PageUrl : null;
    }

    public bool IsOverlayRunning => _overlay?.IsRunning == true;

    public void DeleteRecord(int id)
    {
        if (_records.RemoveAll(r => r.Id == id) == 0) return;
        SaveRecords();
        FileLogger.Info("Timer", $"删除计时记录 #{id}");
        RaiseChanged();
    }

    public void ClearRecords()
    {
        if (_records.Count == 0) return;
        var count = _records.Count;
        _records.Clear();
        SaveRecords();
        FileLogger.Info("Timer", $"清空计时记录（{count} 条）");
        RaiseChanged();
    }

    // ------------------------------------------------------------ 时钟

    /// <summary>给状态机用的时间：单调时钟 + 休眠补时；记录里的 StartedAt 用墙钟。</summary>
    private DateTime MachineNow()
    {
        var mono = _monotonic.Elapsed - _lastMono;
        var wall = _wallClock();
        var wallDelta = wall - _lastWall;

        // 休眠 / 系统时钟前跳：Stopwatch 可能没走，墙钟走了；把差额补进单调时间。
        if (wallDelta > mono + TimeSpan.FromSeconds(2))
            _sleepBank += wallDelta - mono;
        else if (wallDelta < TimeSpan.FromSeconds(-2))
            FileLogger.Warn("Timer", $"检测到系统时钟回拨 {wallDelta.TotalSeconds:0}s，计时按单调时间继续");

        _lastWall = wall;
        _lastMono = _monotonic.Elapsed;
        return _anchorWall + _monotonic.Elapsed + _sleepBank;
    }

    private void Tick()
    {
        var now = MachineNow();
        _machine.Tick(now);
        // 倒计时归零后停在 Finished，但仍要继续刷新超时；只有暂停/停止/重置才停表。
        RaiseChanged();
    }

    private void EnsureTicker()
    {
        if ((_machine.State is TimerState.Running or TimerState.Finished) && !_ticker.IsEnabled)
            _ticker.Start();
    }

    private void StopTicker()
    {
        if (_ticker.IsEnabled) _ticker.Stop();
    }

    // ------------------------------------------------------------ OBS 联动

    private void OnObsStateChanged()
    {
        if (_disposed || _obs is null) return;
        if (_dispatcher is not null && !_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(OnObsStateChanged);
            return;
        }

        var recording = _obs.RecordStatus.Active;
        var streaming = _obs.StreamStatus.Active;
        var started = recording && !_wasRecording || streaming && !_wasStreaming;
        var stopped = !recording && !streaming && (_wasRecording || _wasStreaming);
        _wasRecording = recording;
        _wasStreaming = streaming;

        if (started && _settings.AutoStartWithRecording
            && _machine.State is TimerState.Idle or TimerState.Finished)
        {
            Start();
        }
        else if (stopped && _settings.AutoStopWithRecording
                 && _machine.State is TimerState.Running or TimerState.Paused or TimerState.Finished)
        {
            Stop();
        }
    }

    // ------------------------------------------------------------ 持久化

    private TimerSettings LoadSettings()
    {
        try
        {
            var loaded = _store.GetObject<TimerSettings>(SettingsKey);
            if (loaded is null) return new TimerSettings();
            Normalize(loaded);
            return loaded;
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Timer", $"读取计时器设置失败，使用默认值：{ex.Message}");
            return new TimerSettings();
        }
    }

    private void NormalizeSettings() => Normalize(_settings);

    private static void Normalize(TimerSettings s)
    {
        s.PlannedSeconds = Math.Clamp(s.PlannedSeconds, 0, TimerCore.MaxPlannedSeconds);
        s.Opacity = TimerCore.ClampOpacity(s.Opacity);
        s.FontScale = TimerCore.ClampFontScale(s.FontScale);
        s.WindowWidth = Math.Clamp(s.WindowWidth <= 0 ? TimerCore.DefaultWindowWidth : s.WindowWidth,
            TimerCore.MinWindowWidth, 4096);
        s.WindowHeight = Math.Clamp(s.WindowHeight <= 0 ? TimerCore.DefaultWindowHeight : s.WindowHeight,
            TimerCore.MinWindowHeight, 2160);
        if (s.MonitorIndex < -1) s.MonitorIndex = -1;
    }

    private void SaveSettings()
    {
        try
        {
            _store.SetObject(SettingsKey, _settings);
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Timer", $"保存计时器设置失败：{ex.Message}");
        }
    }

    private void LoadRecords()
    {
        try
        {
            var loaded = _store.GetObject<List<TimerRecord>>(RecordsKey);
            if (loaded is null) return;
            foreach (var record in loaded.Where(r => r is not null).OrderBy(r => r.Id))
            {
                _records.Insert(0, record);
                if (_records.Count >= TimerCore.MaxRecords) break;
            }
            _nextRecordId = _records.Count == 0 ? 1 : _records.Max(r => r.Id) + 1;
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Timer", $"读取计时记录失败，按空列表处理：{ex.Message}");
        }
    }

    private void SaveRecords()
    {
        try
        {
            _store.SetObject(RecordsKey, _records.ToList());
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Timer", $"保存计时记录失败：{ex.Message}");
        }
    }

    // ------------------------------------------------------------ 生命周期

    private void EnsureView()
    {
        if (_view is not null) return;
        if (_viewFactory is null) return;
        _view = _viewFactory();
        _view.ApplySettings(_settings, GetScreens());
        _view.UpdateView(Snapshot, _settings);
    }

    private void RaiseChanged()
    {
        Changed?.Invoke();
        _view?.UpdateView(Snapshot, _settings);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopTicker();
        if (_obs is not null) _obs.StateChanged -= OnObsStateChanged;
        _overlay?.Dispose();
        _overlay = null;
        try { _view?.HideView(); } catch (Exception) { /* 退出阶段忽略 */ }
        _view = null;
        SaveSettings();
        SaveRecords();
    }
}
