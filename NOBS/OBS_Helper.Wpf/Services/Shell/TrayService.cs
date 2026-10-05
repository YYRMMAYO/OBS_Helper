using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using OBS_Helper.Wpf.Models.Shell;
using OBS_Helper.Wpf.Services.Host;
using OBS_Helper.Wpf.Services.Obs;
using OBS_Helper.Wpf.Services.ObsConfig;

namespace OBS_Helper.Wpf.Services.Shell;

/// <summary>
/// 系统托盘：最小化后仍可控制 OBS，状态变化（录制 / 推流）通过托盘提示告知用户。
///
/// 实现要点：
/// <list type="bullet">
///   <item>WinForms <see cref="NotifyIcon"/> 必须跑在带消息循环的 STA 线程上，
///         这里开一个专用线程 + <see cref="Application.Run()"/> 空消息循环，
///         主线程通过 <see cref="SynchronizationContext"/> 投递更新；</item>
///   <item>所有对 <c>NotifyIcon</c> / 菜单的读写都投递到托盘线程，杜绝跨线程访问；</item>
///   <item>菜单动作直接复用 <see cref="ObsConnectionService"/> 的切换方法，
///         状态刷新由 <see cref="ObsConnectionService.StateChanged"/> 驱动；</item>
///   <item>通知走 <see cref="NotifyIcon.ShowBalloonTip"/>，纯本地、无需网络；</item>
///   <item>录制 / 推流状态翻转时按设置弹通知（可关）。</item>
/// </list>
/// </summary>
public sealed class TrayService : IDisposable
{
    private const string IconResource = "OBS_Helper.Wpf.Assets.appicon.ico";
    private const string SettingsKey = "obshelper.shell";

    /// <summary>磁盘剩余空间低于该值（GB）时触发预警通知。</summary>
    private const double DiskWarnGb = 10;

    /// <summary>磁盘预警检查间隔（原挂在监控页每秒采样上，现下沉为托盘独立低频检查）。</summary>
    private static readonly TimeSpan DiskWarnInterval = TimeSpan.FromMinutes(30);

    private readonly ObsConnectionService _obs;
    private readonly LocalStore _store;
    private readonly object _gate = new();

    private Thread? _thread;
    private NotifyIcon? _icon;
    private ToolStripMenuItem? _recordItem;
    private ToolStripMenuItem? _streamItem;
    private ToolStripMenuItem? _virtualCamItem;
    private SynchronizationContext? _traySync;
    private System.Threading.Timer? _diskWarnTimer;

    // 上一次看到的状态（用于翻转检测 → 通知）
    private bool _lastRecActive;
    private bool _lastStreamActive;
    private bool _lastVcamActive;

    /// <summary>首次刷新只记录当前状态、不弹「已开始」假通知（启动时 OBS 可能已在录制/推流）。</summary>
    private bool _primed;

    /// <summary>托盘菜单「显示主窗口」或双击托盘图标时触发。</summary>
    public event Action? ShowRequested;

    /// <summary>托盘菜单「退出」时触发（由 MainWindow 决定真正退出流程）。</summary>
    public event Action? ExitRequested;

    /// <summary>托盘菜单「小窗控制」时触发（由 MainWindow 切到 UI 线程呼出小窗）。</summary>
    public event Action? MiniWindowRequested;

    /// <summary>托盘菜单「打开录像目录」时触发（V2.9.4；由 MainWindow 走只读解析后打开资源管理器）。</summary>
    public event Action? OpenRecordingFolderRequested;

    public TrayService(ObsConnectionService obs, LocalStore store)
    {
        _obs = obs;
        _store = store;
        LoadSettings();
    }

    public ShellSettings Settings { get; private set; } = new();

    public void LoadSettings()
    {
        var s = _store.GetObject<ShellSettings>(SettingsKey);
        if (s is not null) Settings = s;
    }

    public void SaveSettings()
    {
        _store.SetObject(SettingsKey, Settings);
    }

    /// <summary>启动托盘线程。重复调用是安全的（已在运行则忽略）。</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_thread is { IsAlive: true }) return;

            _obs.StateChanged += OnObsStateChanged;

            _thread = new Thread(TrayThreadMain)
            {
                IsBackground = true,
                Name = "OBS-Helper-Tray"
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();

            StartDiskWarning();
        }
    }

    /// <summary>
    /// 磁盘空间低频预警：独立于监控页采样的 30 分钟定时器。
    /// 托盘常驻应用生命周期，即使监控页从未打开或已离开，预警依然生效。
    /// 回调在 ThreadPool 线程执行，<see cref="Notify"/> 内部会投递到托盘线程，线程安全。
    /// </summary>
    private void StartDiskWarning()
    {
        if (_diskWarnTimer is not null) return;
        _diskWarnTimer = new System.Threading.Timer(_ =>
        {
            try
            {
                var lowest = DiskProbe.Sample().OrderBy(d => d.FreeGb).FirstOrDefault();
                if (lowest is null || lowest.FreeGb >= DiskWarnGb) return;
                Notify(Strings.T("tray.diskLowTitle"), Strings.T("tray.diskLowMessage", lowest.Name, lowest.FreeGb));
            }
            catch (Exception)
            {
                // 采样或通知失败：低频任务，静默跳过等下一个周期
            }
        }, null, DiskWarnInterval, DiskWarnInterval);
    }

    private void StopDiskWarning()
    {
        var t = _diskWarnTimer;
        _diskWarnTimer = null;
        try { t?.Dispose(); } catch (Exception) { }
    }

    /// <summary>托盘通知（录制/推流状态变化、定时到点等）。可在任意线程调用。</summary>
    public void Notify(string title, string text)
    {
        Post(() => _icon?.ShowBalloonTip(5000, title, text, ToolTipIcon.Info));
    }

    /// <summary>Obs 状态变化（任意线程触发）→ 刷新托盘。</summary>
    private void OnObsStateChanged() => RefreshState();

    /// <summary>刷新托盘菜单文本与 ToolTip（订阅 Obs 状态变化后自动调用）。</summary>
    public void RefreshState()
    {
        Post(() =>
        {
            if (_icon is null) return;

            var rec = _obs.RecordStatus.Active;
            var stream = _obs.StreamStatus.Active;
            var vcam = _obs.VirtualCamStatus.Active;

            if (_recordItem is not null)
            {
                _recordItem.Text = rec ? Strings.T("tray.stopRecord") : Strings.T("tray.startRecord");
                _recordItem.Enabled = _obs.IsConnected;
            }
            if (_streamItem is not null)
            {
                _streamItem.Text = stream ? Strings.T("tray.stopStream") : Strings.T("tray.startStream");
                _streamItem.Enabled = _obs.IsConnected;
            }
            if (_virtualCamItem is not null)
            {
                _virtualCamItem.Text = vcam ? Strings.T("tray.disableVirtualCam") : Strings.T("tray.enableVirtualCam");
                _virtualCamItem.Enabled = _obs.IsConnected;
            }

            var tip = Strings.T("tray.tooltip");
            // 录制中把已录时长带进 ToolTip（V2.9.4）：全屏游戏里托盘图标是唯一能瞄一眼的地方
            if (rec)
            {
                var elapsed = _obs.RecordElapsed;
                tip += Strings.T("tray.tooltipRecording");
                if (elapsed > TimeSpan.Zero)
                    tip += " " + SimpleRecordingCore.FormatDuration(elapsed);
            }
            if (stream) tip += Strings.T("tray.tooltipStreaming");
            if (vcam) tip += Strings.T("tray.tooltipVirtualCam");
            _icon.Text = tip.Length > 63 ? tip[..63] : tip;   // NotifyIcon.Text 上限 63 字符

            NotifyStateFlips(rec, stream, vcam);
        });
    }

    /// <summary>检测录制 / 推流 / 虚拟摄像头的状态翻转并通知（按设置开关）。</summary>
    private void NotifyStateFlips(bool rec, bool stream, bool vcam)
    {
        if (!Settings.NotifyStateChange || _icon is null) return;

        // 首次刷新：只同步基线，避免把「启动时已存在」的录制/推流状态当成刚翻转弹通知
        if (!_primed)
        {
            _primed = true;
            _lastRecActive = rec;
            _lastStreamActive = stream;
            _lastVcamActive = vcam;
            return;
        }

        if (rec != _lastRecActive)
        {
            _lastRecActive = rec;
            _icon.ShowBalloonTip(4000, rec ? Strings.T("tray.recordStartedTitle") : Strings.T("tray.recordStoppedTitle"),
                rec ? Strings.T("tray.recordStartedMessage") : Strings.T("tray.recordStoppedMessage"), ToolTipIcon.Info);
        }
        if (stream != _lastStreamActive)
        {
            _lastStreamActive = stream;
            _icon.ShowBalloonTip(4000, stream ? Strings.T("tray.streamStartedTitle") : Strings.T("tray.streamStoppedTitle"),
                stream ? Strings.T("tray.streamStartedMessage") : Strings.T("tray.streamStoppedMessage"), ToolTipIcon.Info);
        }
        if (vcam != _lastVcamActive)
        {
            _lastVcamActive = vcam;
            _icon.ShowBalloonTip(4000, vcam ? Strings.T("tray.vcamOnTitle") : Strings.T("tray.vcamOffTitle"), "", ToolTipIcon.Info);
        }
    }

    /// <summary>停止托盘线程并释放资源（应用退出时调用）。</summary>
    public void Stop()
    {
        lock (_gate)
        {
            StopDiskWarning();

            _obs.StateChanged -= OnObsStateChanged;
            Post(() =>
            {
                try
                {
                    if (_icon is not null)
                    {
                        _icon.Visible = false;
                        _icon.Dispose();
                        _icon = null;
                    }
                }
                catch (Exception) { /* 退出路径，忽略 */ }
                try { Application.ExitThread(); } catch (Exception) { }
            });

            var t = _thread;
            _thread = null;
            if (t is { IsAlive: true })
            {
                try { t.Join(TimeSpan.FromSeconds(2)); } catch (Exception) { }
            }
        }
    }

    public void Dispose() => Stop();

    /// <summary>
    /// 语言切换后重建托盘菜单与提示文本（V2.9.2）：菜单项在托盘线程上创建，
    /// 必须在同一线程重建，否则 WinForms 控件会跨线程访问。
    /// </summary>
    public void RefreshLanguage() => Post(() =>
    {
        if (_icon is null) return;
        _icon.ContextMenuStrip = BuildMenu();
        RefreshState();
    });

    // ------------------------------------------------------------ 托盘线程

    private void TrayThreadMain()
    {
        _traySync = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = Strings.T("tray.tooltip"),
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _icon.DoubleClick += (_, _) => ShowRequested?.Invoke();

        RefreshState();

        // 空消息循环：NotifyIcon 的回调消息靠它分发
        Application.Run();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var show = new ToolStripMenuItem(Strings.T("tray.showWindow"));
        show.Click += (_, _) => ShowRequested?.Invoke();

        _recordItem = new ToolStripMenuItem(Strings.T("tray.startRecord"));
        _recordItem.Click += (_, _) => FireAndForget(_obs.ToggleRecordAsync);

        _streamItem = new ToolStripMenuItem(Strings.T("tray.startStream"));
        _streamItem.Click += (_, _) => FireAndForget(_obs.ToggleStreamAsync);

        _virtualCamItem = new ToolStripMenuItem(Strings.T("tray.enableVirtualCam"));
        _virtualCamItem.Click += (_, _) => FireAndForget(_obs.ToggleVirtualCamAsync);

        var miniItem = new ToolStripMenuItem(Strings.T("tray.miniWindow"));
        miniItem.Click += (_, _) => MiniWindowRequested?.Invoke();

        var openDirItem = new ToolStripMenuItem(Strings.T("tray.openRecordDir"));
        openDirItem.Click += (_, _) => OpenRecordingFolderRequested?.Invoke();

        var exit = new ToolStripMenuItem(Strings.T("tray.exit"));
        exit.Click += (_, _) => ExitRequested?.Invoke();

        menu.Items.Add(show);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_recordItem);
        menu.Items.Add(_streamItem);
        menu.Items.Add(_virtualCamItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(miniItem);
        menu.Items.Add(openDirItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exit);
        return menu;
    }

    // ------------------------------------------------------------ 辅助

    /// <summary>把动作投递到托盘线程执行；线程未启动时直接忽略。</summary>
    private void Post(Action action)
    {
        var sync = _traySync;
        if (sync is null) return;
        try { sync.Post(_ => action(), null); }
        catch (Exception) { /* 线程已退出 */ }
    }

    private static async void FireAndForget(Func<Task<Models.Obs.ObsRequestResult>> action)
    {
        try { await action(); }
        catch (Exception) { /* 托盘动作失败：Obs 状态事件会刷新显示，无需弹窗 */ }
    }

    private static Icon LoadIcon()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream(IconResource);
            if (stream is not null) return new Icon(stream);
        }
        catch (Exception) { /* 图标缺失时退回系统图标 */ }
        return SystemIcons.Application;
    }
}
