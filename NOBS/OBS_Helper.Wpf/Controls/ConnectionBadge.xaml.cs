using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OBS_Helper.Wpf.Models.Obs;
using OBS_Helper.Wpf.Navigation;

namespace OBS_Helper.Wpf.Controls;

/// <summary>
/// OBS 连接状态的小药丸。挂在侧栏底部和顶栏，订阅连接服务的状态变化自动刷新，
/// 点一下直接跳控制台。
/// </summary>
public partial class ConnectionBadge : UserControl
{
    public ConnectionBadge()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            AppServices.Obs.StateChanged += OnStateChanged;
            Refresh();
        };
        Unloaded += (_, _) => AppServices.Obs.StateChanged -= OnStateChanged;
    }

    private void OnStateChanged() => Dispatcher.BeginInvoke(new Action(Refresh));

    /// <summary>
    /// 按当前连接状态刷新文案与状态点。
    /// public：语言切换后主窗口要主动刷一次（徽章属于窗口 chrome，不在页面里）。
    /// </summary>
    public void Refresh()
    {
        var obs = AppServices.Obs;

        // V3.0：「连接失败」与「从未连接」在界面上必须能分开。
        //
        // 原来通用失败分支把状态置成 Disconnected（不是 Failed），而徽章只在 Failed 时才把原因放进
        // ToolTip —— 于是全产品最高频的任务（连不上 OBS）失败后，首页 / 诊断页 / 托盘全无提示，
        // 文案表里写好的三条排查也永远不露面。现在：只要还带着失败原因，就用红色 +「连接失败」。
        //
        // V3.0 审查修正：判据从「LastError 非空」改成 `LastErrorIsFailure` ——
        // 正常关闭 OBS 也会写一条原因（「OBS 正在退出」），旧判据会把正常关闭渲染成故障。
        var hasFailure = obs.LastErrorIsFailure && !string.IsNullOrEmpty(obs.LastError);

        var (text, brushKey) = obs.State switch
        {
            ObsConnectionState.Connected => (Strings.T("badge.connected"), "OkBrush"),
            ObsConnectionState.Connecting => (Strings.T("badge.connecting"), "WarnBrush"),
            ObsConnectionState.Authenticating => (Strings.T("badge.authenticating"), "WarnBrush"),
            ObsConnectionState.Reconnecting => (obs.ReconnectInSeconds > 0
                ? Strings.T("badge.reconnectIn", obs.ReconnectInSeconds)
                : Strings.T("badge.reconnecting"), "WarnBrush"),
            ObsConnectionState.Failed => (Strings.T("badge.failed"), "DangerBrush"),
            ObsConnectionState.Disconnected when hasFailure => (Strings.T("badge.connectFailed"), "DangerBrush"),
            _ => (Strings.T("badge.disconnected"), "MutedBrush")
        };

        Label.Text = text;
        Dot.Fill = TryFindResource(brushKey) as Brush ?? Brushes.Gray;

        // 提示里仍可显示「非失败」的原因（例如「OBS 正在退出」），它是有用的上下文
        var tip = !string.IsNullOrEmpty(obs.LastError) ? obs.LastError : Strings.T("badge.tipDisconnected");

        // V3.0：旧协议用户/支持人员需要知道当前走的是哪一代协议（部分能力不可用就来自这里）
        if (obs.State == ObsConnectionState.Connected && obs.IsLegacyProtocol)
            tip = Strings.T("badge.legacyTip") + "\n" + tip;
        Root.ToolTip = tip;

        // V3.0（E4）：可访问名称带**状态**（「已连接 OBS」/「连接失败」），
        // 否则读屏用户只知道「有个徽章」，不知道现在连没连上 —— 而这是本产品最高频的状态。
        System.Windows.Automation.AutomationProperties.SetName(Root, text);
        System.Windows.Automation.AutomationProperties.SetHelpText(Root, tip);
    }

    private void OnClick(object sender, MouseButtonEventArgs e)
        => AppServices.Navigation?.Navigate(Routes.Console);

    /// <summary>
    /// 键盘激活（V3.0 / E4）：Border 不是控件，不响应空格/回车，必须自己接。
    /// 键盘用户此前只能走侧栏导航进控制台 —— 功能不丢，但徽章本身不可操作。
    /// </summary>
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space)) return;
        e.Handled = true;
        AppServices.Navigation?.Navigate(Routes.Console);
    }
}
