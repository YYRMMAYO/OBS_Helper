using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

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

    public MiniControlWindow()
    {
        InitializeComponent();
        RefreshState();
    }

    /// <summary>用户点 ✕ / Alt+F4 隐藏前的钩子（由服务注入，用于保存窗口位置）。</summary>
    public Action? UserHide { get; set; }

    /// <summary>按 Obs 当前状态刷新连接提示与按钮。Obs 状态事件来自任意线程，调用方需保证在 UI 线程执行。</summary>
    public void RefreshState()
    {
        var obs = AppServices.Obs;
        var connected = obs.IsConnected;

        StatusText.Text = connected ? "已连接 · 就绪" : "未连接 OBS";
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, connected ? "OkBrush" : "WarnBrush");

        var rec = obs.RecordStatus.Active;
        RecordButtonText.Text = rec ? "停止录制" : "开始录制";
        ApplyActiveState(RecordButton, RecordButtonText, rec, connected);

        var stream = obs.StreamStatus.Active;
        StreamButtonText.Text = stream ? "停止推流" : "开始推流";
        ApplyActiveState(StreamButton, StreamButtonText, stream, connected);

        var vcam = obs.VirtualCamStatus.Active;
        VcamButtonText.Text = vcam ? "关闭虚拟摄像头" : "开启虚拟摄像头";
        ApplyActiveState(VcamButton, VcamButtonText, vcam, connected);
    }

    /// <summary>进行中：文字变绿色；未连接：整组禁用。</summary>
    private static void ApplyActiveState(Button button, TextBlock text, bool active, bool connected)
    {
        text.SetResourceReference(TextBlock.ForegroundProperty, active ? "OkBrush" : "TextBrush");
        button.IsEnabled = connected;
    }

    // ------------------------------------------------------------ 按钮动作

    private async void OnRecordClick(object sender, RoutedEventArgs e)
    {
        try { await AppServices.Obs.ToggleRecordAsync(); }
        catch (Exception) { /* 失败由 Obs 状态事件纠正显示 */ }
        RefreshState();
    }

    private async void OnStreamClick(object sender, RoutedEventArgs e)
    {
        try { await AppServices.Obs.ToggleStreamAsync(); }
        catch (Exception) { /* 失败由 Obs 状态事件纠正显示 */ }
        RefreshState();
    }

    private async void OnVcamClick(object sender, RoutedEventArgs e)
    {
        try { await AppServices.Obs.ToggleVirtualCamAsync(); }
        catch (Exception) { /* 失败由 Obs 状态事件纠正显示 */ }
        RefreshState();
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
