using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using OBS_Helper.Wpf.Services;
using OBS_Helper.Wpf.Services.Compat;
using OBS_Helper.Wpf.Services.Timing;

namespace OBS_Helper.Wpf.Views;

/// <summary>
/// 无边框置顶计时窗（V3.6.0）。
///
/// 位置用 Win32 <c>SetWindowPos</c> 的物理像素直接落地：多显示器 + 不同 DPI 下，
/// WPF 的 <c>Left/Top</c>（DIP）与 Windows Forms 的工作区（物理像素）不是同一坐标系，
/// 直接混用会在副屏上偏移。保存位置同样用 <c>GetWindowRect</c>，拖动/缩放结束后防抖落盘。
/// </summary>
public partial class TimerWindow : Window, ITimerView
{
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct WinRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out WinRect lpRect);

    private readonly TimerService _service = AppServices.TimerClock;
    private readonly DispatcherTimer _saveDebounce = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private TimerSettings _settings = new();
    private IntPtr _hwnd;
    private bool _hasBounds;
    private bool _suppressSave;
    private bool _wasOvertime;

    public TimerWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        LocationChanged += (_, _) => ScheduleSave();
        SizeChanged += (_, _) => ScheduleSave();
        Closing += (_, _) =>
        {
            _saveDebounce.Stop();
            SaveBoundsNow();
        };

        _saveDebounce.Tick += (_, _) =>
        {
            _saveDebounce.Stop();
            SaveBoundsNow();
        };
    }

    // ------------------------------------------------------------ ITimerView

    public void ShowView()
    {
        if (!IsVisible) Show();
        ApplySavedBoundsIfNeeded(force: true);
        Topmost = true;
    }

    public void HideView()
    {
        SaveBoundsNow();
        Hide();
    }

    public void ApplySettings(TimerSettings settings, IReadOnlyList<TimerScreen> screens)
    {
        _settings = settings;
        Opacity = TimerCore.ClampOpacity(settings.Opacity);
        TimeText.FontSize = Math.Clamp(72 * settings.FontScale, 28, 220);
        SecondaryText.FontSize = Math.Clamp(16 * settings.FontScale, 10, 56);
        StateLabel.FontSize = Math.Clamp(11 * settings.FontScale, 9, 28);
        ClickThroughMenuItem.IsChecked = settings.ClickThrough;
        CaptureMenuItem.IsChecked = settings.ExcludeFromCapture;

        if (_hwnd != IntPtr.Zero)
        {
            ApplyClickThrough(settings.ClickThrough);
            ApplyCapture(settings.ExcludeFromCapture);
        }

        // 可见时不重新定位：避免拖动/缩放过程中被设置刷新拉回原位。
        if (!IsVisible) ApplySavedBoundsIfNeeded(force: false);
    }

    public void UpdateView(TimerSnapshot snapshot, TimerSettings settings)
    {
        _settings = settings;
        TimeText.Text = snapshot.Display;
        SecondaryText.Text = snapshot.Secondary;
        StateLabel.Text = Strings.T(snapshot.State switch
        {
            TimerState.Running => "timer.state.running",
            TimerState.Paused => "timer.state.paused",
            TimerState.Finished => "timer.state.finished",
            _ => "timer.state.idle",
        });

        var brushKey = snapshot.State switch
        {
            TimerState.Paused => "WarnBrush",
            TimerState.Finished => "DangerBrush",
            TimerState.Running => snapshot.IsOvertime ? "DangerBrush" : "BrandBrush",
            _ => "TextBrush",
        };
        TimeText.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        StateDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, brushKey);
        Root.BorderBrush = snapshot.IsOvertime
            ? (Brush)FindResource("DangerBrush")
            : (Brush)FindResource("LineBrush");

        AutomationProperties.SetName(TimeText,
            Strings.T("timer.window.automation", StateLabel.Text, snapshot.Display));

        if (snapshot.IsOvertime && !_wasOvertime && settings.SoundOnOvertime)
        {
            try { SystemSounds.Exclamation.Play(); }
            catch (Exception ex) { FileLogger.Warn("Timer", $"超时提示音失败：{ex.Message}"); }
        }
        _wasOvertime = snapshot.IsOvertime;
    }

    public void MoveToScreen(int index)
    {
        var screens = _service.GetScreens();
        var bounds = TimerCore.ResolveBounds(null, screens, index, _settings.WindowWidth, _settings.WindowHeight);
        ApplyBounds(bounds);
        SaveBoundsNow();
    }

    // ------------------------------------------------------------ 位置 / 样式

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        WindowCaptureExclusion.ApplyToolWindow(_hwnd);
        ApplyClickThrough(_settings.ClickThrough);
        ApplyCapture(_settings.ExcludeFromCapture);
        ApplySavedBoundsIfNeeded(force: false);
    }

    private void ApplySavedBoundsIfNeeded(bool force)
    {
        if (_hasBounds && !force) return;

        TimerRect? saved = null;
        if (_settings.WindowLeft is { } left && _settings.WindowTop is { } top)
            saved = new TimerRect(left, top,
                Math.Max(TimerCore.MinWindowWidth, _settings.WindowWidth),
                Math.Max(TimerCore.MinWindowHeight, _settings.WindowHeight));

        ApplyBounds(_service.ResolveWindowBounds(saved));
    }

    private void ApplyBounds(TimerRect bounds)
    {
        if (_hwnd == IntPtr.Zero) return;

        _suppressSave = true;
        SetWindowPos(_hwnd, IntPtr.Zero,
            (int)Math.Round(bounds.Left), (int)Math.Round(bounds.Top),
            (int)Math.Round(bounds.Width), (int)Math.Round(bounds.Height),
            SwpNoZOrder | SwpNoActivate);
        _hasBounds = true;
        Dispatcher.BeginInvoke(() => _suppressSave = false);
    }

    private void ScheduleSave()
    {
        if (_suppressSave || !IsVisible) return;
        _saveDebounce.Stop();
        _saveDebounce.Start();
    }

    private void SaveBoundsNow()
    {
        if (_hwnd == IntPtr.Zero || _suppressSave) return;
        if (!GetWindowRect(_hwnd, out var rect)) return;
        _service.SaveWindowBounds(new TimerRect(
            rect.Left, rect.Top,
            Math.Max(1, rect.Right - rect.Left),
            Math.Max(1, rect.Bottom - rect.Top)));
    }

    private void ApplyClickThrough(bool enabled)
    {
        if (_hwnd == IntPtr.Zero) return;
        if (!WindowCaptureExclusion.SetClickThrough(_hwnd, enabled))
            FileLogger.Warn("Timer", "切换点击穿透失败");
    }

    private void ApplyCapture(bool enabled)
    {
        if (_hwnd == IntPtr.Zero) return;
        var result = enabled
            ? WindowCaptureExclusion.Apply(_hwnd, true)
            : WindowCaptureExclusion.Clear(_hwnd) ? CaptureExclusionResult.Disabled : CaptureExclusionResult.Failed;
        Root.ToolTip = result switch
        {
            CaptureExclusionResult.Applied => Strings.T("timer.capture.applied"),
            CaptureExclusionResult.UnsupportedOs => Strings.T("timer.capture.unsupported"),
            CaptureExclusionResult.Failed => Strings.T("timer.capture.failed"),
            _ => null,
        };
    }

    // ------------------------------------------------------------ 交互

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.ButtonState != MouseButtonState.Pressed) return;
        try { DragMove(); }
        catch (Exception) { /* 拖动被打断（例如窗口正在关闭）时忽略 */ }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => _service.HideTimerWindow();

    private void OnMenuHide(object sender, RoutedEventArgs e) => _service.HideTimerWindow();

    private void OnMenuNextScreen(object sender, RoutedEventArgs e) => _service.MoveToNextScreen();

    private void OnMenuClickThrough(object sender, RoutedEventArgs e)
        => _service.UpdateSettings(s => s.ClickThrough = ClickThroughMenuItem.IsChecked);

    private void OnMenuCapture(object sender, RoutedEventArgs e)
        => _service.UpdateSettings(s => s.ExcludeFromCapture = CaptureMenuItem.IsChecked);
}
