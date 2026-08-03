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

    private void Refresh()
    {
        var obs = AppServices.Obs;
        var (text, brushKey) = obs.State switch
        {
            ObsConnectionState.Connected => ("已连接", "OkBrush"),
            ObsConnectionState.Connecting => ("连接中…", "WarnBrush"),
            ObsConnectionState.Authenticating => ("验证中…", "WarnBrush"),
            ObsConnectionState.Reconnecting => (obs.ReconnectInSeconds > 0
                ? $"{obs.ReconnectInSeconds}s 后重连"
                : "重连中…", "WarnBrush"),
            ObsConnectionState.Failed => ("连接失败", "DangerBrush"),
            _ => ("未连接", "MutedBrush")
        };

        Label.Text = text;
        Dot.Fill = TryFindResource(brushKey) as Brush ?? Brushes.Gray;
        Root.ToolTip = obs.State == ObsConnectionState.Failed && !string.IsNullOrEmpty(obs.LastError)
            ? obs.LastError
            : "点击进入 OBS 控制台";
    }

    private void OnClick(object sender, MouseButtonEventArgs e)
        => AppServices.Navigation?.Navigate(Routes.Console);
}
