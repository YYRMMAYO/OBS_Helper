using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using OBS_Helper.Wpf.Models.Shell;
using OBS_Helper.Wpf.Services.Host;
using OBS_Helper.Wpf.Services.Obs;

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

    private readonly ObsConnectionService _obs;
    private readonly LocalStore _store;
    private readonly object _gate = new();

    private Thread? _thread;
    private NotifyIcon? _icon;
    private ToolStripMenuItem? _recordItem;
    private ToolStripMenuItem? _streamItem;
    private ToolStripMenuItem? _virtualCamItem;
    private SynchronizationContext? _traySync;

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
        }
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
                _recordItem.Text = rec ? "停止录制（进行中）" : "开始录制";
                _recordItem.Enabled = _obs.IsConnected;
            }
            if (_streamItem is not null)
            {
                _streamItem.Text = stream ? "停止推流（进行中）" : "开始推流";
                _streamItem.Enabled = _obs.IsConnected;
            }
            if (_virtualCamItem is not null)
            {
                _virtualCamItem.Text = vcam ? "关闭虚拟摄像头" : "开启虚拟摄像头";
                _virtualCamItem.Enabled = _obs.IsConnected;
            }

            var tip = "OBS 排障助手";
            if (rec) tip += " · 录制中";
            if (stream) tip += " · 推流中";
            if (vcam) tip += " · 虚拟摄像头";
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
            _icon.ShowBalloonTip(4000, rec ? "录制已开始" : "录制已停止",
                rec ? "OBS 正在录制。" : "录制已结束。", ToolTipIcon.Info);
        }
        if (stream != _lastStreamActive)
        {
            _lastStreamActive = stream;
            _icon.ShowBalloonTip(4000, stream ? "推流已开始" : "推流已停止",
                stream ? "OBS 正在推流。" : "推流已结束。", ToolTipIcon.Info);
        }
        if (vcam != _lastVcamActive)
        {
            _lastVcamActive = vcam;
            _icon.ShowBalloonTip(4000, vcam ? "虚拟摄像头已开启" : "虚拟摄像头已关闭", "", ToolTipIcon.Info);
        }
    }

    /// <summary>停止托盘线程并释放资源（应用退出时调用）。</summary>
    public void Stop()
    {
        lock (_gate)
        {
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

    // ------------------------------------------------------------ 托盘线程

    private void TrayThreadMain()
    {
        _traySync = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "OBS 排障助手",
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

        var show = new ToolStripMenuItem("显示主窗口");
        show.Click += (_, _) => ShowRequested?.Invoke();

        _recordItem = new ToolStripMenuItem("开始录制");
        _recordItem.Click += (_, _) => FireAndForget(_obs.ToggleRecordAsync);

        _streamItem = new ToolStripMenuItem("开始推流");
        _streamItem.Click += (_, _) => FireAndForget(_obs.ToggleStreamAsync);

        _virtualCamItem = new ToolStripMenuItem("开启虚拟摄像头");
        _virtualCamItem.Click += (_, _) => FireAndForget(_obs.ToggleVirtualCamAsync);

        var exit = new ToolStripMenuItem("退出");
        exit.Click += (_, _) => ExitRequested?.Invoke();

        menu.Items.Add(show);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_recordItem);
        menu.Items.Add(_streamItem);
        menu.Items.Add(_virtualCamItem);
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
