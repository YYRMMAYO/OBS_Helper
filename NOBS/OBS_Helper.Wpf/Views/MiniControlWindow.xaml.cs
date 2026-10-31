using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OBS_Helper.Wpf.Models.Obs;
using OBS_Helper.Wpf.Services;
using OBS_Helper.Wpf.Services.Obs;

namespace OBS_Helper.Wpf.Views;

/// <summary>
/// 迷你小窗（V3.5.0 重构）：置顶的录制 / 推流 / 虚拟摄像头 / 回放 / 场景 / 音频 / 提词器快捷键。
///
/// 设计要点：
/// <list type="bullet">
///   <item>第一层是直播中最常用的四个开关，闭眼也能点；</item>
///   <item>第二层是场景与音频：全屏游戏时不用切回主窗口；</item>
///   <item>第三层是提词器 / 工作室 / 主窗口；</item>
///   <item>指标只在「可见 + 正在录/播」时刷新，不新增常驻采样；</item>
///   <item>点 ✕ / Alt+F4 只隐藏不销毁，位置记忆在 MiniWindowService。</item>
/// </list>
/// </summary>
public partial class MiniControlWindow : Window
{
    /// <summary>应用退出时由 <see cref="Services.Shell.MiniWindowService.Stop"/> 置位，允许真正关闭。</summary>
    public bool AllowClose { get; set; }

    /// <summary>指标行的秒级刷新。只在窗口可见且正在录制/推流时运行。</summary>
    private readonly System.Windows.Threading.DispatcherTimer _metricsTimer;

    /// <summary>防止程序化设置 ComboBox 时触发切场景。</summary>
    private bool _suppressSceneChange;

    /// <summary>上次构建音频行时的输入签名；变化才重建，避免每秒抢焦点。</summary>
    private string _audioSignature = "";

    public MiniControlWindow()
    {
        InitializeComponent();

        _metricsTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _metricsTimer.Tick += (_, _) =>
        {
            var o = AppServices.Obs;
            RefreshMetrics(o, o.RecordStatus.Active, o.StreamStatus.Active);
        };
        IsVisibleChanged += (_, _) => UpdateMetricsTimer();

        RefreshState();
    }

    /// <summary>按「可见 + 是否在录/播」启停秒级刷新。</summary>
    private void UpdateMetricsTimer()
    {
        var obs = AppServices.Obs;
        var needed = IsVisible && (obs.RecordStatus.Active || obs.StreamStatus.Active);
        if (needed && !_metricsTimer.IsEnabled) _metricsTimer.Start();
        else if (!needed && _metricsTimer.IsEnabled) _metricsTimer.Stop();
    }

    /// <summary>用户点 ✕ / Alt+F4 隐藏前的钩子（由服务注入，用于保存窗口位置）。</summary>
    public Action? UserHide { get; set; }

    /// <summary>按 Obs 当前状态刷新连接提示、按钮、场景与音频。调用方需保证在 UI 线程执行。</summary>
    public void RefreshState()
    {
        var obs = AppServices.Obs;
        var connected = obs.IsConnected;

        StatusText.Text = connected ? Strings.T("mini.statusConnected") : Strings.T("mini.statusDisconnected");
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, connected ? "OkBrush" : "WarnBrush");

        var rec = obs.RecordStatus.Active;
        RecordButtonText.Text = rec ? Strings.T("mini.stopRecord") : Strings.T("mini.startRecord");
        ApplyActiveState(RecordButton, RecordButtonText, rec, connected, primary: !rec);

        var stream = obs.StreamStatus.Active;
        StreamButtonText.Text = stream ? Strings.T("mini.stopStream") : Strings.T("mini.startStream");
        ApplyActiveState(StreamButton, StreamButtonText, stream, connected, primary: false);

        var vcam = obs.VirtualCamStatus.Active;
        VcamButtonText.Text = vcam ? Strings.T("mini.vcamOff") : Strings.T("mini.vcamOn");
        ApplyActiveState(VcamButton, VcamButtonText, vcam, connected, primary: false);

        ReplayButton.IsEnabled = connected && obs.ReplayBufferStatus.Active;
        TeleprompterButton.IsEnabled = true;
        StudioButton.IsEnabled = connected;
        MainWindowButton.IsEnabled = true;

        RefreshScenes(obs, connected);
        RefreshAudio(obs, connected);
        RefreshMetrics(obs, rec, stream);
        UpdateMetricsTimer();
    }

    /// <summary>进行中：文字变绿色；未连接：整组禁用。</summary>
    private static void ApplyActiveState(Button button, TextBlock text, bool active, bool connected, bool primary)
    {
        text.SetResourceReference(TextBlock.ForegroundProperty, active ? "OkBrush" : (primary ? "BrandForegroundBrush" : "TextBrush"));
        button.IsEnabled = connected;
    }

    // ------------------------------------------------------------ 场景 / 音频

    private void RefreshScenes(ObsConnectionService obs, bool connected)
    {
        var scenes = obs.Scenes.Select(s => s.Name).ToList();
        var current = obs.CurrentScene;

        var signature = string.Join("\n", scenes) + "\n#" + current;
        if (!string.Equals(SceneCombo.Tag as string, signature, StringComparison.Ordinal))
        {
            _suppressSceneChange = true;
            try
            {
                SceneCombo.Items.Clear();
                foreach (var name in scenes) SceneCombo.Items.Add(name);
                SceneCombo.Tag = signature;
            }
            finally
            {
                _suppressSceneChange = false;
            }
        }

        _suppressSceneChange = true;
        try
        {
            if (!string.IsNullOrEmpty(current) && SceneCombo.Items.Contains(current))
                SceneCombo.SelectedItem = current;
            else if (SceneCombo.Items.Count > 0 && SceneCombo.SelectedIndex < 0)
                SceneCombo.SelectedIndex = 0;
        }
        finally
        {
            _suppressSceneChange = false;
        }

        SceneCombo.IsEnabled = connected && scenes.Count > 0;
        PrevSceneButton.IsEnabled = SceneCombo.IsEnabled;
        NextSceneButton.IsEnabled = SceneCombo.IsEnabled;
    }

    private void RefreshAudio(ObsConnectionService obs, bool connected)
    {
        var inputs = obs.AudioInputs.Where(i => i.IsAudio).Take(2).ToList();
        var signature = string.Join("\n", inputs.Select(i => i.Name + "|" + i.Muted));
        if (!string.Equals(_audioSignature, signature, StringComparison.Ordinal))
        {
            _audioSignature = signature;
            AudioHost.Children.Clear();

            foreach (var input in inputs)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var name = new TextBlock
                {
                    Text = input.Name,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(2, 0, 8, 0)
                };
                name.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
                name.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                row.Children.Add(name);

                var mute = new Button
                {
                    Content = input.Muted ? Strings.T("mini.unmute") : Strings.T("mini.mute"),
                    Style = (Style)FindResource("MiniButton"),
                    MinWidth = 66,
                    MinHeight = 28,
                    Height = 28,
                    Margin = new Thickness(0),
                    IsEnabled = connected
                };
                mute.SetResourceReference(Button.ForegroundProperty, input.Muted ? "WarnBrush" : "TextBrush");
                var inputName = input.Name;
                mute.Click += async (_, _) => await ToggleMuteAsync(inputName);
                Grid.SetColumn(mute, 1);
                row.Children.Add(mute);

                AudioHost.Children.Add(row);
            }
        }

        AudioEmptyText.Visibility = inputs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var child in AudioHost.Children.OfType<Grid>())
        {
            if (child.Children.Count > 1 && child.Children[1] is Button b) b.IsEnabled = connected;
        }
    }

    private async Task ToggleMuteAsync(string inputName)
    {
        var input = AppServices.Obs.AudioInputs.FirstOrDefault(i =>
            string.Equals(i.Name, inputName, StringComparison.Ordinal));
        if (input is null) return;
        var result = await AppServices.Obs.SetMuteAsync(inputName, !input.Muted);
        if (!result.Ok) ShowActionFailure(result.Comment ?? Strings.T("mini.actionFailedUnknown", result.Code));
        else _ = AppServices.Obs.RefreshAudioInputsAsync();
        RefreshState();
    }

    private void OnSceneChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSceneChange || SceneCombo.SelectedItem is not string scene) return;
        if (string.Equals(scene, AppServices.Obs.CurrentScene, StringComparison.Ordinal)) return;
        _ = RunActionAsync(() => AppServices.Obs.SetSceneAsync(scene), Strings.T("mini.scene"));
    }

    private void OnPrevSceneClick(object sender, RoutedEventArgs e) => StepScene(-1);

    private void OnNextSceneClick(object sender, RoutedEventArgs e) => StepScene(1);

    private void StepScene(int delta)
    {
        var names = AppServices.Obs.Scenes.Select(s => s.Name).ToList();
        if (names.Count == 0) return;
        var index = names.FindIndex(n => string.Equals(n, AppServices.Obs.CurrentScene, StringComparison.Ordinal));
        if (index < 0) index = 0;
        var next = (index + delta + names.Count) % names.Count;
        _ = RunActionAsync(() => AppServices.Obs.SetSceneAsync(names[next]), Strings.T("mini.scene"));
    }

    // ------------------------------------------------------------ 指标

    private void RefreshMetrics(ObsConnectionService obs, bool rec, bool stream)
    {
        var parts = new List<string>();

        if (rec)
        {
            var elapsed = obs.RecordElapsed;
            if (elapsed > TimeSpan.Zero)
                parts.Add(Strings.T("mini.metricElapsed", FormatDuration(elapsed)));

            var progress = AppServices.SimpleRecord?.Progress;
            if (progress is not null && progress.RemainingMinutes > 0)
                parts.Add(Strings.T("mini.metricRemaining", progress.RemainingMinutes));
            else if (progress is not null)
                parts.Add(Strings.T("mini.metricFree", progress.FreeGb));
        }

        if (stream)
        {
            var dropped = obs.StreamStatus.DroppedRatio;
            if (dropped > 0.001) parts.Add(Strings.T("mini.metricDropped", dropped * 100));
            if (obs.StreamStatus.Congestion > 0.05) parts.Add(Strings.T("mini.metricCongestion", obs.StreamStatus.Congestion * 100));
            if (obs.StreamStatus.Reconnecting) parts.Add(Strings.T("mini.metricReconnecting"));
        }

        MetricsText.Text = string.Join(" · ", parts);
        MetricsText.Visibility = parts.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string FormatDuration(TimeSpan t)
        => t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";

    // ------------------------------------------------------------ 按钮动作

    private async void OnRecordClick(object sender, RoutedEventArgs e)
        => await RunActionAsync(() => AppServices.Obs.ToggleRecordAsync(), "录制开关");

    private async void OnStreamClick(object sender, RoutedEventArgs e)
        => await RunActionAsync(() => AppServices.Obs.ToggleStreamAsync(), "推流开关");

    private async void OnVcamClick(object sender, RoutedEventArgs e)
        => await RunActionAsync(() => AppServices.Obs.ToggleVirtualCamAsync(), "虚拟摄像头开关");

    private async void OnReplayClick(object sender, RoutedEventArgs e)
        => await RunActionAsync(() => AppServices.Obs.SaveReplayBufferAsync(), "保存回放");

    private async void OnStudioClick(object sender, RoutedEventArgs e)
        => await RunActionAsync(() => AppServices.Obs.ToggleStudioModeAsync(), "演播室模式");

    private void OnTeleprompterClick(object sender, RoutedEventArgs e)
    {
        try { AppServices.Teleprompter.Toggle(); }
        catch (Exception ex) { ShowActionFailure(ex.Message); }
    }

    private void OnMainWindowClick(object sender, RoutedEventArgs e)
    {
        if (Application.Current?.MainWindow is { } main)
        {
            if (!main.IsVisible) main.Show();
            if (main.WindowState == WindowState.Minimized) main.WindowState = WindowState.Normal;
            main.Activate();
        }
        Hide();
    }

    /// <summary>执行一次开关动作并把结果告诉用户（小窗场景下主窗口 Toast 不可见）。</summary>
    private async Task RunActionAsync(Func<Task<ObsRequestResult>> action, string what)
    {
        ObsRequestResult result;
        try
        {
            result = await action();
        }
        catch (Exception ex)
        {
            FileLogger.Warn("MiniWindow", $"{what}异常：{ex.Message}");
            ShowActionFailure(ex.Message);
            return;
        }

        if (!result.Ok)
        {
            var message = result.Comment ?? Strings.T("mini.actionFailedUnknown", result.Code);
            FileLogger.Warn("MiniWindow", $"{what}失败：{message}");
            ShowActionFailure(message);
            return;
        }

        RefreshState();
    }

    private void ShowActionFailure(string message)
    {
        RefreshState();
        StatusText.Text = Strings.T("mini.actionFailed", message);
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, "WarnBrush");
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        UserHide?.Invoke();
        Hide();
    }

    // ------------------------------------------------------------ 窗口行为

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (AllowClose) return;
        e.Cancel = true;
        UserHide?.Invoke();
        Hide();
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
}
