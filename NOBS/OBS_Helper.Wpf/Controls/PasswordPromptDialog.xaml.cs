using System.Windows;
using OBS_Helper.Wpf.Localization;

namespace OBS_Helper.Wpf.Controls;

/// <summary>
/// OBS WebSocket 密码输入（V3.1.0 / D7）。
///
/// 用应用内样式而不是系统消息框；密码只交给服务层写进 DPAPI 存储，
/// 不进日志、不进诊断报告。取消返回 null。
/// </summary>
public partial class PasswordPromptDialog : Window
{
    private PasswordPromptDialog()
    {
        InitializeComponent();
    }

    /// <summary>弹出密码输入；返回 null 表示用户取消。</summary>
    public static string? Show(string title, string message)
    {
        var dlg = new PasswordPromptDialog
        {
            Owner = Application.Current?.MainWindow
        };
        dlg.Title = title;
        dlg.TitleText.Text = title;
        dlg.MessageText.Text = message;
        dlg.OkButton.Content = Strings.T("common.ok");
        dlg.CancelButton.Content = Strings.T("common.cancel");

        if (dlg.Owner is null || !dlg.Owner.IsVisible)
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        dlg.Loaded += (_, _) => dlg.PasswordInput.Focus();
        return dlg.ShowDialog() == true ? dlg.PasswordInput.Password : null;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
