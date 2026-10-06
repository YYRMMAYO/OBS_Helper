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
/// 迷你小窗：置顶的录制 / 推流 / 虚拟摄像头快捷开关。
///
/// 设计要点：
/// <list type="bullet">
///   <item>按钮文本与状态由 <see cref="RefreshState"/> 驱动（Obs 状态事件 → 服务 → 本窗口刷新）；</item>
///   <item>点击 ✕ 或 Alt+F4 只隐藏不销毁，再次呼出立即可用（<see cref="AllowClose"/> 由服务在退出时置位）；</item>
///   <item>整窗可拖拽；位置记忆在 <c>MiniWindowService</c> 里（存 prefs.json）。</item>
/// </list>
/// </summary>
public partial class MiniControlWindow : Window
{
    /// <summary>应用退出时由 <see cref="Services.Shell.MiniWindowService.Stop"/> 置位，允许真正关闭。</summary>
    public bool AllowClose { get; set; }

    /// <summary>
    /// 指标行的秒级刷新（V3.0 / C2）。只在**窗口可见且正在录制/推流**时运行 ——
    /// 隐藏时或空闲时立刻停掉，不让一个常驻计时器跟着进程跑（插件广场那个教训）。
    /// </summary>
    private readonly System.Windows.Threading.DispatcherTimer _metricsTimer;

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

    /// <summary>按 Obs 当前状态刷新连接提示与按钮。Obs 状态事件来自任意线程，调用方需保证在 UI 线程执行。</summary>
    public void RefreshState()
    {
        var obs = AppServices.Obs;
        var connected = obs.IsConnected;

        StatusText.Text = connected ? Strings.T("mini.statusConnected") : Strings.T("mini.statusDisconnected");
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, connected ? "OkBrush" : "WarnBrush");

        var rec = obs.RecordStatus.Active;
        RecordButtonText.Text = rec ? Strings.T("mini.stopRecord") : Strings.T("mini.startRecord");
        ApplyActiveState(RecordButton, RecordButtonText, rec, connected);

        var stream = obs.StreamStatus.Active;
        StreamButtonText.Text = stream ? Strings.T("mini.stopStream") : Strings.T("mini.startStream");
        ApplyActiveState(StreamButton, StreamButtonText, stream, connected);

        var vcam = obs.VirtualCamStatus.Active;
        VcamButtonText.Text = vcam ? Strings.T("mini.vcamOff") : Strings.T("mini.vcamOn");
        ApplyActiveState(VcamButton, VcamButtonText, vcam, connected);

        RefreshMetrics(obs, rec, stream);
        UpdateMetricsTimer();
    }

    /// <summary>
    /// 实时指标行（V3.0 / C2）：录制时长 · 剩余可录 · 推流丢帧与拥塞。
    ///
    /// 这些数据本来就采到了（首页卡与控制台已经在显示），只是没进小窗 ——
    /// 而小窗恰恰是「游戏全屏时唯一能瞄一眼的地方」。没有任何新增的采集开销。
    /// </summary>
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

    /// <summary>进行中：文字变绿色；未连接：整组禁用。</summary>
    private static void ApplyActiveState(Button button, TextBlock text, bool active, bool connected)
    {
        text.SetResourceReference(TextBlock.ForegroundProperty, active ? "OkBrush" : "TextBrush");
        button.IsEnabled = connected;
    }

    // ------------------------------------------------------------ 按钮动作

    private async void OnRecordClick(object sender, RoutedEventArgs e)
        => await RunActionAsync(() => AppServices.Obs.ToggleRecordAsync(), "录制开关");

    private async void OnStreamClick(object sender, RoutedEventArgs e)
        => await RunActionAsync(() => AppServices.Obs.ToggleStreamAsync(), "推流开关");

    private async void OnVcamClick(object sender, RoutedEventArgs e)
        => await RunActionAsync(() => AppServices.Obs.ToggleVirtualCamAsync(), "虚拟摄像头开关");

    /// <summary>
    /// 执行一次开关动作并把结果告诉用户。
    ///
    /// V3.0 审查补正：<c>Toggle*Async</c> 在**所有**常见失败路径上都是「返回 Ok=false」而不是抛异常
    /// （未连接、服务端拒绝、超时……），所以原先只写 <c>catch</c> 的代码基本是死代码 ——
    /// 失败时按钮毫无反应。现在检查返回值，并把提示写在小窗**自己的状态行**上：
    /// 小窗的使用场景恰恰是主窗口被最小化或游戏全屏，弹到主窗口的 Toast 用户根本看不见。
    /// </summary>
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
        // 用户关闭只隐藏，保持单实例可复用；应用真正退出时由服务置 AllowClose 放行
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
