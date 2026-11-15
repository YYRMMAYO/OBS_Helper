using System.Windows;

namespace OBS_Helper.Wpf.Controls;

/// <summary>
/// 计时器首次使用署名弹窗（V3.6.0）。
///
/// 本模块的功能设计参考了 zanjie1999 的开源项目「咩计时 meTimer」，原项目采用咩License 1.1；
/// 本应用中的计时器是独立实现，不复制其源代码。按用户要求保留原作者、原项目、仓库与许可证链接。
/// </summary>
public partial class TimerAttributionDialog : Window
{
    private const string ProjectUrl = "https://github.com/zanjie1999/meTimer";
    private const string LicenseUrl = "https://github.com/zanjie1999/LICENSE";

    public TimerAttributionDialog()
    {
        InitializeComponent();
    }

    public static void Show(Window? owner)
    {
        var dialog = new TimerAttributionDialog { Owner = owner ?? Application.Current?.MainWindow };
        dialog.ShowDialog();
    }

    private async void OnOpenProject(object sender, RoutedEventArgs e)
        => await AppServices.Host.OpenExternalAsync(ProjectUrl).ConfigureAwait(true);

    private async void OnOpenLicense(object sender, RoutedEventArgs e)
        => await AppServices.Host.OpenExternalAsync(LicenseUrl).ConfigureAwait(true);

    private void OnAck(object sender, RoutedEventArgs e) => Close();
}
