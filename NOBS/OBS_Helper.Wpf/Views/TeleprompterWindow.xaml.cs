using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using OBS_Helper.Wpf.Localization;
using OBS_Helper.Wpf.Models.Shell;
using OBS_Helper.Wpf.Services;
using OBS_Helper.Wpf.Services.Compat;
using OBS_Helper.Wpf.Services.Host;
using OBS_Helper.Wpf.Services.Shell;

namespace OBS_Helper.Wpf.Views;

/// <summary>
/// 提词器窗口（V3.1.0 / §12.1）。
///
/// 独立顶层窗口：置顶、无边框、可拖动、可缩放、可点击穿透。
/// 录制兼容性：窗口采集 / 游戏采集天然不包含它；显示器采集只在 Windows 10 2004+
/// 且系统允许 <c>WDA_EXCLUDEFROMCAPTURE</c> 时才能排除，因此不承诺「绝对不会被录到」。
/// </summary>
public partial class TeleprompterWindow : Window
{
    private readonly TeleprompterService _service;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _autosaveTimer;
    private readonly DispatcherTimer _toastTimer;

    private TeleprompterSettings _settings;
    private string _script;
    private double _offset;
    private DateTime _lastTick = DateTime.UtcNow;
    private bool _playing;
    private bool _editing;
    private bool _applying;
    private bool _clickThrough;
    private bool _dragging;
    private Point _dragOrigin;
    private double _dragStartOffset;
    private readonly DispatcherTimer _boundsSaveTimer;

    public TeleprompterWindow(TeleprompterService service)
    {
        _service = service;
        _settings = TeleprompterCore.Normalize(service.Settings.Clone());
        _script = service.Script;

        // 构造期间 XAML 会触发滑杆 ValueChanged：先挡住，免得把 0 写进设置
        _applying = true;
        InitializeComponent();
        _applying = false;

        // 捕获意外丢失（例如 Alt+Tab）时结束拖动，避免自动滚动一直停着
        Scroller.LostMouseCapture += (_, _) => EndDrag();

        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += OnTick;

        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _autosaveTimer.Tick += OnAutosaveTick;

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.6) };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); ToastBorder.Visibility = Visibility.Collapsed; };

        // V3.1.1：窗口位置 / 尺寸变化后延迟落盘，避免拖动过程中反复写 prefs.json
        _boundsSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _boundsSaveTimer.Tick += (_, _) => { _boundsSaveTimer.Stop(); SaveSettings(includeBounds: true); };

        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        SizeChanged += OnSizeChanged;
        LocationChanged += OnLocationChanged;
        IsVisibleChanged += OnVisibleChanged;
        PreviewKeyDown += OnPreviewKeyDown;
        Deactivated += (_, _) => { /* ShowActivated=false：不要因为失焦而打断滚动 */ };
    }

    /// <summary>捕获排除的结果（供服务 / 设置页显示）。</summary>
    public CaptureExclusionResult CaptureExclusionState { get; private set; } = CaptureExclusionResult.UnsupportedOs;

    /// <summary>当前是否处于点击穿透。</summary>
    public bool IsClickThrough => _clickThrough;

    // ------------------------------------------------------------ 生命周期

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ScriptText.Text = _script;
        ApplySettings(_settings, restoreBounds: true);
        RestoreScriptIfEmpty();
        UpdateToolbarText();
        UpdateCaptureHint();
        SetPlaying(false);
        ScrollTo(0);
        _lastTick = DateTime.UtcNow;
        _timer.Start();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        WindowCaptureExclusion.ApplyToolWindow(hwnd);
        ApplyCaptureExclusion();
        if (_settings.ClickThrough) SetClickThrough(true, save: false);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (GuideLine is null) return;
        GuideLine.Margin = new Thickness(0, GuideLineY, 0, 0);
        _offset = TeleprompterCore.ClampOffset(_offset, MaxScrollOffset);
        QueueBoundsSave();
    }

    private void OnLocationChanged(object? sender, EventArgs e) => QueueBoundsSave();

    private void QueueBoundsSave()
    {
        if (_applying || !IsLoaded) return;
        _boundsSaveTimer.Stop();
        _boundsSaveTimer.Start();
    }

    private void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // 隐藏时停表，避免后台空转；重新显示从当前张继续
        if (IsVisible)
        {
            _lastTick = DateTime.UtcNow;
            if (!_timer.IsEnabled) _timer.Start();
        }
        else
        {
            _timer.Stop();
            EndDrag();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        _autosaveTimer.Stop();
        _toastTimer.Stop();
        _boundsSaveTimer.Stop();
        if (_editing)
        {
            try { _service.SaveScript(EditorBox.Text); }
            catch (Exception) { /* 关闭路径，忽略 */ }
        }
        base.OnClosed(e);
    }

    // ------------------------------------------------------------ 设置

    /// <summary>由服务在设置变化时调用（例如设置页改了速度 / 字号）。</summary>
    public void ApplySettings(TeleprompterSettings settings)
        => ApplySettings(settings, restoreBounds: false);

    private void ApplySettings(TeleprompterSettings settings, bool restoreBounds)
    {
        _applying = true;
        try
        {
            _settings = TeleprompterCore.Normalize(settings.Clone());

            ScriptText.FontSize = _settings.FontSize;
            ScriptText.LineHeight = TeleprompterCore.LineHeightPixels(_settings.FontSize, _settings.LineHeight);
            ScriptText.Foreground = BrushFrom(_settings.TextColor, Colors.White);
            ScriptText.Margin = new Thickness(_settings.SideMargin, 40, _settings.SideMargin, 220);

            Backdrop.Background = BrushFrom(_settings.Background, Colors.Black);
            Backdrop.Opacity = _settings.BackgroundOpacity;

            Topmost = _settings.Topmost;
            GuideLine.Visibility = _settings.ShowGuide ? Visibility.Visible : Visibility.Collapsed;
            GuideLine.Margin = new Thickness(0, GuideLineY, 0, 0);

            var (sx, sy) = TeleprompterCore.MirrorScale(_settings.MirrorHorizontal, _settings.MirrorVertical);
            ContentRoot.LayoutTransform = new ScaleTransform(sx, sy);

            OpacitySlider.Value = Math.Round(_settings.BackgroundOpacity * 100);
            LineHeightSlider.Value = Math.Round(_settings.LineHeight * 100);
            MarginSlider.Value = Math.Round(_settings.SideMargin);

            if (restoreBounds) RestoreWindowBounds();
            UpdateMirrorButtons();
            UpdateToolbarText();
            UpdateCaptureHint();
        }
        finally
        {
            _applying = false;
        }
    }

    private void RestoreWindowBounds()
    {
        Width = Math.Max(MinWidth, _settings.Width);
        Height = Math.Max(MinHeight, _settings.Height);

        if (!double.IsNaN(_settings.Left) && !double.IsNaN(_settings.Top))
        {
            Left = ClampToVirtualScreen(_settings.Left, SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenWidth, Width);
            Top = ClampToVirtualScreen(_settings.Top, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenHeight, Height);
        }
        else
        {
            CenterOnMonitor(_settings.Monitor);
        }
    }

    private static double ClampToVirtualScreen(double value, double origin, double length, double size)
    {
        var min = origin;
        var max = origin + Math.Max(0, length - size);
        if (double.IsNaN(value) || double.IsInfinity(value)) return min;
        return Math.Clamp(value, min, max);
    }

    private void CenterOnMonitor(int monitorIndex)
    {
        try
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            if (screens.Length == 0) return;

            var index = TeleprompterCore.NormalizeMonitor(monitorIndex, screens.Length);
            var work = screens[index].WorkingArea;
            var dpi = VisualTreeHelper.GetDpi(this);
            var left = work.Left / dpi.DpiScaleX;
            var top = work.Top / dpi.DpiScaleY;
            var width = work.Width / dpi.DpiScaleX;
            var height = work.Height / dpi.DpiScaleY;

            var (x, y) = TeleprompterCore.CenterOn(left, top, width, height, Width, Height);
            Left = x;
            Top = y;
        }
        catch (Exception)
        {
            // 多屏信息拿不到：保持系统默认位置
        }
    }

    private void OnMonitorClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            if (screens.Length == 0) return;

            var current = TeleprompterCore.NormalizeMonitor(_settings.Monitor, screens.Length);
            var next = screens.Length <= 1 ? 0 : (current + 1) % screens.Length;
            _settings.Monitor = next;
            CenterOnMonitor(next);
            SaveSettings(includeBounds: true);
            ShowToast(Strings.T("teleprompter.monitorChanged", next + 1));
        }
        catch (Exception ex)
        {
            ShowToast(Strings.T("teleprompter.monitorFailed", ex.Message));
        }
    }

    // ------------------------------------------------------------ 播放 / 滚动

    /// <summary>阅读参考线的纵坐标（默认窗口高度的 40%）。</summary>
    private double GuideLineY => Math.Max(40, ActualHeight * 0.4);

    /// <summary>
    /// 可滚动到的最大偏移（V3.1.1）：滚到「最后一行正好停在参考线」为止。
    /// 直接用 <c>ScrollableHeight</c> 会把底部留白也滚进来，最后只剩一片黑。
    /// </summary>
    private double MaxScrollOffset => TeleprompterCore.ScrollEnd(
        Scroller.ScrollableHeight, ScriptText.ActualHeight, ScriptText.Margin.Top, GuideLineY);

    private void OnTick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var seconds = (now - _lastTick).TotalSeconds;
        _lastTick = now;

        // 拖动文字时暂停自动滚动，否则手指和计时器会互相抢偏移
        if (!_playing || _editing || _dragging) return;

        var max = MaxScrollOffset;
        _offset = TeleprompterCore.Advance(_offset, max, _settings.Speed, seconds, paused: false);
        Scroller.ScrollToVerticalOffset(_offset);

        if (max > 0 && _offset >= max)
            SetPlaying(false);
    }

    /// <summary>把滚动偏移写回 ScrollViewer（统一夹到有效范围）。</summary>
    private void ScrollTo(double offset)
    {
        _offset = TeleprompterCore.ClampOffset(offset, MaxScrollOffset);
        Scroller.ScrollToVerticalOffset(_offset);
    }

    private void SetPlaying(bool playing)
    {
        _playing = playing;
        _lastTick = DateTime.UtcNow;
        PlayPauseButton.Content = playing ? "⏸" : "▶";
        PlayPauseButton.SetValue(System.Windows.Automation.AutomationProperties.NameProperty,
            Strings.T(playing ? "teleprompter.pause" : "teleprompter.play"));
    }

    public void TogglePlayPause() => SetPlaying(!_playing);

    private void OnPlayPauseClick(object sender, RoutedEventArgs e) => TogglePlayPause();

    private void OnSpeedDownClick(object sender, RoutedEventArgs e) => ChangeSpeed(-5);

    private void OnSpeedUpClick(object sender, RoutedEventArgs e) => ChangeSpeed(+5);

    private void ChangeSpeed(double delta)
    {
        _settings.Speed = Math.Clamp(_settings.Speed + delta,
            TeleprompterCore.MinSpeed, TeleprompterCore.MaxSpeed);
        UpdateToolbarText();
        SaveSettings();
    }

    private void OnFontDownClick(object sender, RoutedEventArgs e) => ChangeFont(-2);

    private void OnFontUpClick(object sender, RoutedEventArgs e) => ChangeFont(+2);

    private void ChangeFont(double delta)
    {
        _settings.FontSize = Math.Clamp(_settings.FontSize + delta,
            TeleprompterCore.MinFontSize, TeleprompterCore.MaxFontSize);
        ApplySettings(_settings);
        SaveSettings();
    }

    // ------------------------------------------------------------ 手动上下移动

    private void OnScrollUpClick(object sender, RoutedEventArgs e) => ScrollBy(-LineStep);

    private void OnScrollDownClick(object sender, RoutedEventArgs e) => ScrollBy(+LineStep);

    private void OnScrollTopClick(object sender, RoutedEventArgs e) => ScrollTo(0);

    /// <summary>一次手动滚动的距离：一行文字。</summary>
    private double LineStep => Math.Max(24, ScriptText.LineHeight > 0 ? ScriptText.LineHeight : 60);

    private void OnContentWheel(object sender, MouseWheelEventArgs e)
    {
        if (_editing) return;
        e.Handled = true;
        ScrollBy(-e.Delta * 0.6);
    }

    /// <summary>按住文字上下拖动：像拖动纸张一样移动内容，不依赖键盘焦点。</summary>
    private void OnContentMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_editing) return;

        // 点击即激活：否则 ShowActivated=False 的窗口永远收不到键盘事件，
        // 用户按空格 / 方向键会「没有反应」。
        try
        {
            Activate();
            Scroller.Focus();
        }
        catch (Exception) { /* 激活失败不影响拖动 */ }

        _dragging = true;
        _dragOrigin = e.GetPosition(Scroller);
        _dragStartOffset = _offset;
        Scroller.CaptureMouse();
        e.Handled = true;
    }

    private void OnContentMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndDrag();
            return;
        }

        var delta = e.GetPosition(Scroller).Y - _dragOrigin.Y;
        ScrollTo(TeleprompterCore.PanOffset(_dragStartOffset, delta, MaxScrollOffset));
    }

    private void OnContentMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => EndDrag();

    private void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        Scroller.ReleaseMouseCapture();
        _lastTick = DateTime.UtcNow;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_editing)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                FinishEditing();
            }
            return;
        }

        // Ctrl + 方向键：移动窗口本身（V3.1.1），与方向键滚动文字区分开
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.Left: e.Handled = true; MoveWindowBy(-WindowMoveStep, 0); return;
                case Key.Right: e.Handled = true; MoveWindowBy(+WindowMoveStep, 0); return;
                case Key.Up: e.Handled = true; MoveWindowBy(0, -WindowMoveStep); return;
                case Key.Down: e.Handled = true; MoveWindowBy(0, +WindowMoveStep); return;
            }
        }

        switch (e.Key)
        {
            case Key.Space:
                e.Handled = true;
                TogglePlayPause();
                break;
            case Key.Home:
                e.Handled = true;
                ScrollTo(0);
                break;
            case Key.End:
                e.Handled = true;
                ScrollTo(MaxScrollOffset);
                break;
            case Key.Up:
                e.Handled = true;
                ScrollBy(-LineStep);
                break;
            case Key.Down:
                e.Handled = true;
                ScrollBy(+LineStep);
                break;
            case Key.PageUp:
                e.Handled = true;
                ScrollBy(-Math.Max(LineStep, Scroller.ViewportHeight * 0.8));
                break;
            case Key.PageDown:
                e.Handled = true;
                ScrollBy(+Math.Max(LineStep, Scroller.ViewportHeight * 0.8));
                break;
            case Key.Escape:
                e.Handled = true;
                Hide();
                break;
        }
    }

    private void ScrollBy(double delta) => ScrollTo(_offset + delta);

    private const double WindowMoveStep = 10;

    /// <summary>移动窗口（Ctrl+方向键），并夹在虚拟屏幕内；位置会在 600ms 后自动保存。</summary>
    private void MoveWindowBy(double deltaX, double deltaY)
    {
        var (left, top) = TeleprompterCore.MoveWindow(
            Left, Top, deltaX, deltaY,
            SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight,
            ActualWidth, ActualHeight);
        Left = left;
        Top = top;
        QueueBoundsSave();
    }

    // ------------------------------------------------------------ 镜像 / 穿透 / 捕获

    private void UpdateMirrorButtons()
    {
        MirrorHButton.Opacity = _settings.MirrorHorizontal ? 1 : 0.55;
        MirrorVButton.Opacity = _settings.MirrorVertical ? 1 : 0.55;
    }

    private void OnMirrorHorizontalClick(object sender, RoutedEventArgs e)
    {
        _settings.MirrorHorizontal = !_settings.MirrorHorizontal;
        ApplySettings(_settings);
        SaveSettings();
    }

    private void OnMirrorVerticalClick(object sender, RoutedEventArgs e)
    {
        _settings.MirrorVertical = !_settings.MirrorVertical;
        ApplySettings(_settings);
        SaveSettings();
    }

    private void OnClickThroughClick(object sender, RoutedEventArgs e)
        => SetClickThrough(!_clickThrough, save: true);

    /// <summary>开启 / 关闭点击穿透。开启后鼠标落到下层应用，只能用全局热键关掉。</summary>
    public void SetClickThrough(bool enabled, bool save)
    {
        _clickThrough = enabled;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            WindowCaptureExclusion.SetClickThrough(hwnd, enabled);

        TopBar.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        if (enabled) EditorPanel.Visibility = Visibility.Collapsed;
        ClickThroughButton.Opacity = enabled ? 1 : 0.55;

        if (save)
        {
            _settings.ClickThrough = enabled;
            SaveSettings();
        }

        ShowToast(enabled
            ? Strings.T("teleprompter.clickThroughOn")
            : Strings.T("teleprompter.clickThroughOff"));
    }

    private void ApplyCaptureExclusion()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        CaptureExclusionState = WindowCaptureExclusion.Apply(hwnd, _settings.ExcludeFromCapture);
        if (CaptureExclusionState == CaptureExclusionResult.UnsupportedOs && _settings.ExcludeFromCapture)
            FileLogger.Warn("Teleprompter", "当前系统不支持从显示器采集中排除提词器窗口，建议改用窗口采集或第二屏。");
        UpdateCaptureHint();
    }

    private void UpdateCaptureHint()
    {
        if (CaptureHintText is null) return;
        CaptureHintText.Text = CaptureExclusionState switch
        {
            CaptureExclusionResult.Applied => Strings.T("teleprompter.captureApplied"),
            CaptureExclusionResult.Failed => Strings.T("teleprompter.captureFailed"),
            CaptureExclusionResult.Disabled => Strings.T("teleprompter.captureDisabled"),
            _ => Strings.T("teleprompter.captureUnsupported")
        };
    }

    // ------------------------------------------------------------ 编辑 / 脚本

    private void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (_editing)
        {
            FinishEditing();
            return;
        }

        _editing = true;
        SetPlaying(false);
        EditorPanel.Visibility = Visibility.Visible;
        EditorBox.Text = _script;
        EditorBox.CaretIndex = EditorBox.Text.Length;
        EditorBox.Focus();
        UpdateCaptureHint();
    }

    private void OnDoneEditingClick(object sender, RoutedEventArgs e) => FinishEditing();

    private void FinishEditing()
    {
        _autosaveTimer.Stop();
        _service.SaveScript(EditorBox.Text);
        _script = _service.Script;
        ScriptText.Text = _script;
        _editing = false;
        EditorPanel.Visibility = Visibility.Collapsed;
        ScrollTo(0);
    }

    private void OnEditorTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_applying || !_editing) return;
        _autosaveTimer.Stop();
        _autosaveTimer.Start();
    }

    private void OnAutosaveTick(object? sender, EventArgs e)
    {
        _autosaveTimer.Stop();
        if (_editing) _service.SaveScript(EditorBox.Text);
    }

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = Strings.T("teleprompter.import"),
            Filter = "Text / Markdown (*.txt;*.md)|*.txt;*.md|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var text = File.ReadAllText(dlg.FileName);
            EditorBox.Text = TeleprompterCore.NormalizeScript(text);
            ShowToast(Strings.T("teleprompter.imported", Path.GetFileName(dlg.FileName)));
        }
        catch (Exception ex)
        {
            ShowToast(Strings.T("teleprompter.importFailed", ex.Message));
        }
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        EditorBox.Text = TeleprompterCore.DefaultScript;
        ShowToast(Strings.T("teleprompter.cleared"));
    }

    private void OnReloadClick(object sender, RoutedEventArgs e)
    {
        _script = _service.ReloadScript();
        EditorBox.Text = _script;
        ScriptText.Text = _script;
        ShowToast(Strings.T("teleprompter.reloaded"));
    }

    private void RestoreScriptIfEmpty()
    {
        if (!string.IsNullOrWhiteSpace(_script)) return;
        _script = TeleprompterCore.DefaultScript;
        ScriptText.Text = _script;
    }

    /// <summary>由服务在脚本被外部替换时调用。</summary>
    public void SetScript(string text)
    {
        _script = TeleprompterCore.NormalizeScript(text);
        ScriptText.Text = _script;
        if (_editing) EditorBox.Text = _script;
        ScrollTo(0);
    }

    // ------------------------------------------------------------ 场景联动

    /// <summary>切到某个场景时跳到对应段落（只滚动，不切 OBS 场景）。</summary>
    public void ApplyScene(string sceneName)
    {
        if (_editing) return;
        var sections = TeleprompterCore.ParseSections(_script);
        var index = TeleprompterCore.FindSectionForScene(sections, sceneName);
        if (index < 0)
        {
            ShowToast(Strings.T("teleprompter.sceneLink.missing", sceneName));
            return;
        }

        var section = sections[index];
        // 近似定位：按段落前的行数 × 行高估算。折行会让它略偏，但比按字符比例稳。
        var before = _script[..Math.Min(section.StartIndex, _script.Length)];
        var lines = before.Count(c => c == '\n');
        var linePx = TeleprompterCore.LineHeightPixels(_settings.FontSize, _settings.LineHeight);
        ScrollTo(lines * linePx + 40);
        ShowToast(Strings.T("teleprompter.sceneLink", sceneName));
    }

    // ------------------------------------------------------------ 倒计时 / 提示 / 工具

    public async void StartCountdown(int seconds)
    {
        if (_editing) return;
        SetPlaying(false);
        CountdownText.Visibility = Visibility.Visible;
        foreach (var value in TeleprompterCore.CountdownSteps(seconds))
        {
            if (!IsLoaded) break;
            CountdownText.Text = value.ToString();
            await Task.Delay(800);
        }
        CountdownText.Visibility = Visibility.Collapsed;
        if (IsLoaded) SetPlaying(true);
    }

    private void OnCountdownClick(object sender, RoutedEventArgs e) => StartCountdown(3);

    private void ShowToast(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        ToastText.Text = message;
        ToastBorder.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Hide();

    private void OnDragAreaMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_clickThrough) return;
        try { DragMove(); }
        catch (Exception) { /* 拖动失败无妨 */ }
    }

    private void UpdateToolbarText()
    {
        SpeedText.Text = Strings.T("teleprompter.speedValue", TeleprompterCore.FormatSpeed(_settings.Speed));
        HintText.Text = Strings.T("teleprompter.hintShort");
        ClickThroughButton.Opacity = _clickThrough ? 1 : 0.55;
    }

    private void SaveSettings(bool includeBounds = false)
    {
        if (_applying) return;
        var s = _settings.Clone();
        if (includeBounds)
        {
            s.Left = Left;
            s.Top = Top;
            s.Width = Width;
            s.Height = Height;
        }
        _service.SaveSettings(s, applyToWindow: false);
        _settings = TeleprompterCore.Normalize(_service.Settings.Clone());
    }

    // ------------------------------------------------------------ 设置面板滑杆

    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_applying) return;
        _settings.BackgroundOpacity = e.NewValue / 100.0;
        Backdrop.Opacity = _settings.BackgroundOpacity;
        SaveSettings();
    }

    private void OnLineHeightChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_applying) return;
        _settings.LineHeight = e.NewValue / 100.0;
        ScriptText.LineHeight = TeleprompterCore.LineHeightPixels(_settings.FontSize, _settings.LineHeight);
        SaveSettings();
    }

    private void OnMarginChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_applying) return;
        _settings.SideMargin = e.NewValue;
        ScriptText.Margin = new Thickness(_settings.SideMargin, 40, _settings.SideMargin, 220);
        SaveSettings();
    }

    private static Brush BrushFrom(string hex, Color fallback)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
        catch (Exception)
        {
            var brush = new SolidColorBrush(fallback);
            brush.Freeze();
            return brush;
        }
    }
}
