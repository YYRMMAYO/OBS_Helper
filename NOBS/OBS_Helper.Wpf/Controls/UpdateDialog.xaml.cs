using System.Windows;
using OBS_Helper.Wpf.Services;

namespace OBS_Helper.Wpf.Controls;

/// <summary>
/// 发现新版本时的提示弹窗。
///
/// 与 <see cref="ConfirmDialog"/> 同款窗口样式（不跟随系统、深色模式不闪白），
/// 额外承载下载链接、网盘密码与 GitHub 仓库入口。
/// 弹窗内只做展示与打开浏览器，不承担任何下载逻辑。
/// </summary>
public partial class UpdateDialog : Window
{
    private UpdateDialog()
    {
        InitializeComponent();
        DownloadLink.RequestNavigate += (_, e) =>
        {
            // Hyperlink 默认会用自己的导航逻辑，这里改为统一走系统浏览器
            e.Handled = true;
            _ = AppServices.Host.OpenExternalAsync(e.Uri.ToString());
        };
    }

    /// <summary>弹出更新提示框。返回用户选择（去下载 / 稍后再说 / GitHub 仓库）。</summary>
    public static UpdateDialogResult Show(Version? current, Version? latest)
    {
        var dlg = new UpdateDialog
        {
            Owner = Application.Current?.MainWindow
        };

        dlg.CurrentRun.Text = current is null
            ? "当前版本 —"
            : $"当前版本 {current.Major}.{current.Minor}.{current.Build}";
        dlg.LatestRun.Text = latest is null
            ? "最新版本 —"
            : $"最新版本 V{latest.Major}.{latest.Minor}.{latest.Build}";
        dlg.TitleText.Text = latest is null ? "发现新版本" : $"发现新版本 V{latest.Major}.{latest.Minor}.{latest.Build}";
        dlg.PasswordText.Text = UpdateService.UpdatePassword;

        // Owner 未显示时 CenterOwner 会退化到屏幕左上，兜底居中屏幕
        if (dlg.Owner is null || !dlg.Owner.IsVisible)
        {
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        dlg.ShowDialog();
        return dlg._result;
    }

    private UpdateDialogResult _result = UpdateDialogResult.Later;

    private void OnDownload(object sender, RoutedEventArgs e)
    {
        _result = UpdateDialogResult.Download;
        Close();
    }

    private void OnLater(object sender, RoutedEventArgs e)
    {
        _result = UpdateDialogResult.Later;
        Close();
    }

    private async void OnOpenRepo(object sender, RoutedEventArgs e)
    {
        _result = UpdateDialogResult.Repo;
        await AppServices.Host.OpenExternalAsync(UpdateService.RepoUrl);
        Close();
    }
}

/// <summary>用户在更新提示弹窗中的选择。</summary>
public enum UpdateDialogResult
{
    /// <summary>去下载（打开蓝奏云）。</summary>
    Download,

    /// <summary>稍后再说。</summary>
    Later,

    /// <summary>打开 GitHub 仓库。</summary>
    Repo,
}
