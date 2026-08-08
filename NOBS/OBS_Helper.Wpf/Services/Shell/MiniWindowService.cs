using System.Windows;
using OBS_Helper.Wpf.Models.Shell;
using OBS_Helper.Wpf.Services.Host;
using OBS_Helper.Wpf.Views;

namespace OBS_Helper.Wpf.Services.Shell;

/// <summary>
/// 迷你小窗（置顶录制 / 推流控制）的服务封装：托盘菜单与全局热键共用的呼出入口。
///
/// 实现要点：
/// <list type="bullet">
///   <item>窗口惰性创建，单实例；点击 ✕ 只隐藏，不销毁（见 <see cref="MiniControlWindow"/>）；</item>
///   <item>窗口位置记忆到 <c>prefs.json</c>（key <c>obshelper.mini</c>），恢复时夹回屏幕工作区；</item>
///   <item>Obs 状态变化（任意线程触发）经 Dispatcher 转回 UI 线程刷新按钮；</item>
///   <item>调用方须在 UI 线程调用 <see cref="Toggle"/>（托盘 / 热键入口都已派发到 UI 线程）。</item>
/// </list>
/// </summary>
public sealed class MiniWindowService
{
    private const string StorageKey = "obshelper.mini";

    private readonly LocalStore _store;
    private MiniControlWindow? _window;
    private bool _subscribed;

    public MiniWindowService(LocalStore store) => _store = store;

    /// <summary>切换小窗显示 / 隐藏。</summary>
    public void Toggle()
    {
        var w = GetWindow();
        if (w.IsVisible)
        {
            SavePosition(w);
            w.Hide();
        }
        else
        {
            RestorePosition(w);
            w.Show();
            w.RefreshState();
        }
    }

    /// <summary>应用退出时释放窗口（置 AllowClose 后真正关闭，并保存位置）。</summary>
    public void Stop()
    {
        Unsubscribe();
        if (_window is not null)
        {
            SavePosition(_window);
            _window.AllowClose = true;
            _window.Close();
            _window = null;
        }
    }

    private MiniControlWindow GetWindow()
    {
        if (_window is not null) return _window;

        _window = new MiniControlWindow();
        // 点 ✕ / Alt+F4 隐藏也会触发位置保存，保证「记住位置」在所有隐藏路径都生效
        _window.UserHide = () => SavePosition(_window!);
        _window.Closed += OnWindowClosed;
        Subscribe();
        return _window;
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        Unsubscribe();
        _window = null;
    }

    private void Subscribe()
    {
        if (_subscribed) return;
        AppServices.Obs.StateChanged += OnObsStateChanged;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        AppServices.Obs.StateChanged -= OnObsStateChanged;
        _subscribed = false;
    }

    private void OnObsStateChanged()
    {
        // Obs 状态事件可能来自 WebSocket 线程，切回 UI 线程再刷新按钮
        try
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() => _window?.RefreshState()));
        }
        catch (Exception)
        {
            // 应用退出途中 Dispatcher 已关闭时忽略，窗口随进程结束
        }
    }

    // ------------------------------------------------------------ 位置记忆

    private void RestorePosition(MiniControlWindow w)
    {
        var p = _store.GetObject<MiniWindowSettings>(StorageKey);
        if (p is not null && !double.IsNaN(p.X) && !double.IsNaN(p.Y))
        {
            // 保存点落在哪块屏幕就用哪块屏幕的工作区做夹取（多屏下不会把窗口拉回主屏）；
            // 若已不在任何屏幕内，Screen.FromPoint 会返回最近的屏幕，窗口仍能找回
            var pt = new System.Drawing.Point((int)p.X, (int)p.Y);
            var area = System.Windows.Forms.Screen.FromPoint(pt).WorkingArea;
            w.Left = Math.Clamp(p.X, area.Left, Math.Max(area.Left, area.Right - w.Width));
            w.Top = Math.Clamp(p.Y, area.Top, Math.Max(area.Top, area.Bottom - w.Height));
        }
        else if (Application.Current?.MainWindow is { } main)
        {
            // 首次呼出：放在主窗口下方，贴近右下角
            w.Left = Math.Max(0, main.Left + main.Width - w.Width - 32);
            w.Top = main.Top + main.Height + 12;
        }
    }

    private void SavePosition(MiniControlWindow w)
    {
        // 自检期间不写用户偏好（测试窗口的位置不应落盘）
        if (App.HeadlessTest) return;
        _store.SetObject(StorageKey, new MiniWindowSettings { X = w.Left, Y = w.Top });
    }
}
