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
        var (text, brushKey) = obs.State switch
        {
            ObsConnectionState.Connected => (Strings.T("badge.connected"), "OkBrush"),
            ObsConnectionState.Connecting => (Strings.T("badge.connecting"), "WarnBrush"),
            ObsConnectionState.Authenticating => (Strings.T("badge.authenticating"), "WarnBrush"),
            ObsConnectionState.Reconnecting => (obs.ReconnectInSeconds > 0
                ? Strings.T("badge.reconnectIn", obs.ReconnectInSeconds)
                : Strings.T("badge.reconnecting"), "WarnBrush"),
            ObsConnectionState.Failed => (Strings.T("badge.failed"), "DangerBrush"),
            _ => (Strings.T("badge.disconnected"), "MutedBrush")
        };

        Label.Text = text;
        Dot.Fill = TryFindResource(brushKey) as Brush ?? Brushes.Gray;
        Root.ToolTip = obs.State == ObsConnectionState.Failed && !string.IsNullOrEmpty(obs.LastError)
            ? obs.LastError
            : Strings.T("badge.tipDisconnected");
    }

    private void OnClick(object sender, MouseButtonEventArgs e)
        => AppServices.Navigation?.Navigate(Routes.Console);
}
