using System.Windows;
using OBS_Helper.Wpf.Localization;
using OBS_Helper.Wpf.Services.ObsConfig;

namespace OBS_Helper.Wpf.Controls;

/// <summary>
/// 首次「由本工具拉起 OBS」的同意对话框（V3.1.0 / D2）。
///
/// 三个出口：继续并记住（allow）/ 每次都问（ask）/ 我自己开（deny）。
/// 只有 ask 会在之后每次点击时再次出现 —— 这是对 V2.9.4「每次都确认」的替代。
/// </summary>
public partial class LaunchConsentDialog : Window
{
    private SimpleRecordLaunchConsent _choice = SimpleRecordLaunchConsent.Ask;

    private LaunchConsentDialog()
    {
        InitializeComponent();
    }

    /// <summary>展示对话框；关闭窗口（Esc / 右上角）按「每次都问」处理。</summary>
    public static SimpleRecordLaunchConsent Show(string title, string message)
    {
        var dlg = new LaunchConsentDialog
        {
            Owner = Application.Current?.MainWindow
        };
        dlg.Title = title;
        dlg.TitleText.Text = title;
        dlg.MessageText.Text = message;
        dlg.AllowButton.Content = Strings.T("simple.launch.consent.allow");
        dlg.AskButton.Content = Strings.T("simple.launch.consent.ask");
        dlg.DenyButton.Content = Strings.T("simple.launch.consent.deny");

        if (dlg.Owner is null || !dlg.Owner.IsVisible)
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        dlg.Loaded += (_, _) => dlg.AllowButton.Focus();
        dlg.ShowDialog();
        return dlg._choice;
    }

    private void OnAllow(object sender, RoutedEventArgs e) => Finish(SimpleRecordLaunchConsent.Allow);

    private void OnAsk(object sender, RoutedEventArgs e) => Finish(SimpleRecordLaunchConsent.Ask);

    private void OnDeny(object sender, RoutedEventArgs e) => Finish(SimpleRecordLaunchConsent.Deny);

    private void Finish(SimpleRecordLaunchConsent choice)
    {
        _choice = choice;
        // 模态窗口：设置 DialogResult 会结束 ShowDialog（不需要再调 Close）
        DialogResult = true;
    }
}
