using System.Windows;

namespace OBS_Helper.Wpf.Controls;

/// <summary>
/// 二次确认弹窗。用在会中断直播的危险操作上（断开连接、停止录制、停止推流）。
///
/// 不用系统 MessageBox 的原因：系统弹窗不跟随应用主题，深色模式下会突兀地闪出一块白。
/// </summary>
public partial class ConfirmDialog : Window
{
    private ConfirmDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 弹出确认框。<paramref name="danger"/> 为 true 时确认按钮为红色。
    /// </summary>
    public static bool Show(string title, string message,
                            string okText = "确认", string cancelText = "取消",
                            bool danger = true, string icon = "⚠️")
    {
        var dlg = new ConfirmDialog
        {
            Owner = Application.Current?.MainWindow
        };

        dlg.TitleText.Text = title;
        dlg.MessageText.Text = message;
        dlg.OkButton.Content = okText;
        dlg.CancelButton.Content = cancelText;
        dlg.IconText.Text = icon;

        if (!danger)
        {
            dlg.OkButton.Style = dlg.TryFindResource("PrimaryButton") as Style;
        }

        // Owner 未显示时 CenterOwner 会退化到屏幕左上，兜底居中屏幕
        if (dlg.Owner is null || !dlg.Owner.IsVisible)
        {
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return dlg.ShowDialog() == true;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
