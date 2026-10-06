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
    ///
    /// <paramref name="irreversible"/> = 「不可撤销」：只有这种对话框才把默认按钮换成「取消」。
    /// V3.0 审查补正：此前只要 <c>danger</c>（默认 true）就把默认按钮改成取消，
    /// 而全仓 12 个调用点里有 11 个没传 <c>danger:false</c> —— 于是「应用模板」「导入备份」这类
    /// **用户主动发起的正向操作**也变成「按回车 = 取消」，用户会以为按钮坏了。
    /// </summary>
    public static bool Show(string title, string message,
                            string? okText = null, string? cancelText = null,
                            bool danger = true, string icon = "⚠️", bool irreversible = false)
    {
        var dlg = new ConfirmDialog
        {
            Owner = Application.Current?.MainWindow
        };

        // 无障碍：WindowAutomationPeer 读的是 Window.Title，读屏播报对话框名就靠它
        dlg.Title = title;

        dlg.TitleText.Text = title;
        dlg.MessageText.Text = message;
        dlg.OkButton.Content = okText ?? Strings.T("common.confirm");
        dlg.CancelButton.Content = cancelText ?? Strings.T("common.cancel");
        dlg.IconText.Text = icon;

        if (!danger)
        {
            dlg.OkButton.Style = dlg.TryFindResource("PrimaryButton") as Style;
        }

        // 不可撤销的操作：默认按钮给「取消」，初始焦点也给「取消」
        if (irreversible)
        {
            dlg.OkButton.IsDefault = false;
            dlg.CancelButton.IsDefault = true;
        }

        // 无障碍：弹窗打开后焦点必须落在某个按钮上，读屏才会播报「对话框 + 按钮名」
        var initialFocus = irreversible ? dlg.CancelButton : dlg.OkButton;
        dlg.Loaded += (_, _) => initialFocus.Focus();

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
