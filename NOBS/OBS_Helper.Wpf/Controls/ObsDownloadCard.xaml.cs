using System.Windows;
using System.Windows.Controls;
using OBS_Helper.Wpf.Services.Update;

namespace OBS_Helper.Wpf.Controls;

/// <summary>
/// 「获取官方 OBS Studio」下载卡（V2.9.1）。放置位置：搭建页、工具箱，两处同一枚控件，
/// 文案与入口只维护一份。
///
/// 三条入口都只指向官方渠道（常量集中在 <see cref="ObsDownloadLinks"/>）：
/// <list type="bullet">
///   <item>官网下载页（中文）；</item>
///   <item>GitHub 发布页；</item>
///   <item>当前稳定版 Windows 安装包直链 —— 走 GitHub API 解析，拿不到就退化打开发布页。</item>
/// </list>
/// 不做「静默下载到本地」：安装包交给浏览器的下载管理器，用户可以自己看到文件来源。
/// </summary>
public partial class ObsDownloadCard : UserControl
{
    public ObsDownloadCard()
    {
        InitializeComponent();
        SafetyText.Text = ObsDownloadLinks.SafetyNote;
    }

    private async void OnOpenOfficialDownload(object sender, RoutedEventArgs e)
        => await OpenAsync(ObsDownloadLinks.OfficialDownload, Strings.T("obs.status.officialOpened")).ConfigureAwait(true);

    private async void OnOpenGitHubReleases(object sender, RoutedEventArgs e)
        => await OpenAsync(ObsDownloadLinks.GitHubLatestRelease, Strings.T("obs.status.githubOpened")).ConfigureAwait(true);

    /// <summary>
    /// 解析并打开「当前稳定版 Windows 安装包」直链。解析失败不报错，退化为官方发布页，
    /// 并明确告诉用户「去 Assets 里选 Windows x64 Installer」。
    /// </summary>
    private async void OnDownloadLatestInstaller(object sender, RoutedEventArgs e)
    {
        DirectButton.IsEnabled = false;
        ShowStatus(Strings.T("obs.status.resolving"));

        try
        {
            var link = await AppServices.ObsRelease.GetWindowsInstallerLinkAsync().ConfigureAwait(true);
            var ok = await AppServices.Host.OpenExternalAsync(link.Url).ConfigureAwait(true);

            if (!ok)
            {
                ShowStatus(Strings.T("common.openBrowserFailed") + link.Url);
                AppServices.Toast.Show(Strings.T("obs.toast.directFailed"), "error");
                return;
            }

            if (link.IsDirect)
            {
                var ver = string.IsNullOrEmpty(link.Version) ? "" : $" {link.Version}";
                ShowStatus(Strings.T("obs.status.directStarted", ver, ObsDownloadLinks.WindowsSignerName));
            }
            else
            {
                ShowStatus(Strings.T("obs.status.directFallback"));
            }
        }
        catch (Exception ex)
        {
            ShowStatus(Strings.T("obs.status.downloadError", ex.Message));
            AppServices.Toast.Show(Strings.T("obs.toast.linkFailed", ex.Message), "error");
        }
        finally
        {
            DirectButton.IsEnabled = true;
        }
    }

    private async Task OpenAsync(string url, string okMessage)
    {
        try
        {
            var ok = await AppServices.Host.OpenExternalAsync(url).ConfigureAwait(true);
            if (ok)
            {
                ShowStatus(okMessage);
                return;
            }

            ShowStatus(Strings.T("common.openBrowserFailed") + url);
        }
        catch (Exception ex)
        {
            ShowStatus(Strings.T("obs.status.linkError", ex.Message));
        }
    }

    private void ShowStatus(string text)
    {
        StatusText.Text = text;
        StatusText.Visibility = Visibility.Visible;
    }
}
