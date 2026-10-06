using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using OBS_Helper.Wpf.Services;
using OBS_Helper.Wpf.Services.Update;

namespace OBS_Helper.Wpf.Controls;

/// <summary>
/// 「获取官方 OBS Studio」下载卡（V2.9.1 起，V2.9.3 扩充）。放置位置：搭建页、工具箱，
/// 两处同一枚控件，文案与入口只维护一份。
///
/// 入口分三层：
/// <list type="number">
///   <item><b>官方</b>（常量集中在 <see cref="ObsDownloadLinks"/>）：官网下载页 / GitHub 发布页 /
///         当前稳定版安装包直链（走 GitHub API 解析，拿不到就退化打开发布页）；</item>
///   <item><b>应用内下载</b>：直接下官方安装包到临时目录，带进度与官方域名校验，下完可一键启动
///         安装向导 —— 但**不做静默安装**，UAC 与安装路径始终由用户决定；</item>
///   <item><b>备用通道</b>：GitHub 拉不动时先装瓦特工具箱（原 Steam++）给 GitHub 加速，
///         或改用微软商店的 OBS。两者都标注「不是 OBS 官方源」，只解决「下得到」的问题。</item>
/// </list>
/// </summary>
public partial class ObsDownloadCard : UserControl
{
    /// <summary>下载中的取消开关（点「取消」时置位）。</summary>
    private CancellationTokenSource? _downloadCts;

    /// <summary>已下好的安装包路径（临时文件），用于「运行安装向导」。</summary>
    private string? _installerPath;

    public ObsDownloadCard()
    {
        InitializeComponent();
        ApplyLanguage();

        // 微软商店需要 Win10 1809+；老系统上把入口隐掉，而不是留一个点了报错的按钮。
        var storeOk = OsSupport.IsMicrosoftStoreAvailable(OsSupport.Current);
        StoreButton.Visibility = storeOk ? Visibility.Visible : Visibility.Collapsed;
        if (!storeOk)
        {
            ToolkitButton.Margin = new Thickness(0, 0, 8, 8);
        }
    }

    /// <summary>
    /// 重新取一遍文案（V3.0 / F5）。
    ///
    /// 与 <see cref="FeedbackCard"/> 同一个原因：本控件被搭建页 / 工具箱共用，
    /// 文案原先只在构造函数里取一次，切语言后仍是旧语言。宿主页面进入时调用即可。
    /// （商店入口的显隐只取决于系统版本，留在构造函数里。）
    /// </summary>
    public void ApplyLanguage()
    {
        SafetyText.Text = ObsDownloadLinks.SafetyNote;
        AlternateText.Text = ObsDownloadLinks.AlternativeNote;
    }

    // ---------------------------------------------------------------- 官方入口

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

    // ---------------------------------------------------------------- 备用通道

    private async void OnOpenToolkit(object sender, RoutedEventArgs e)
        => await OpenAlternateAsync(ObsDownloadLinks.WattToolkitSite,
            Strings.T("obs.status.toolkitOpened"), Strings.T("obs.toast.toolkitFailed")).ConfigureAwait(true);

    private async void OnOpenStore(object sender, RoutedEventArgs e)
        => await OpenAlternateAsync(ObsDownloadLinks.MicrosoftStoreObs,
            Strings.T("obs.status.msStoreOpened"), Strings.T("obs.toast.msStoreFailed")).ConfigureAwait(true);

    private async Task OpenAlternateAsync(string url, string okMessage, string failMessage)
    {
        // 备用通道同样先过白名单：地址常量写成别处（被误改 / 被替换）时宁可不开，也不跳第三方。
        if (!ObsDownloadLinks.IsTrustedAlternativeChannel(url))
        {
            ShowStatus(Strings.T("obs.dl.insecure"));
            return;
        }

        try
        {
            var ok = await AppServices.Host.OpenExternalAsync(url).ConfigureAwait(true);
            if (ok)
            {
                ShowStatus(okMessage);
                return;
            }

            ShowStatus(failMessage);
        }
        catch (Exception ex)
        {
            ShowStatus(Strings.T("obs.status.linkError", ex.Message));
        }
    }

    // ---------------------------------------------------------------- 应用内下载

    /// <summary>
    /// 应用内下载官方安装包：解析直链 → 校验官方域名 → 流式下载（带进度）→ 下完露出「运行安装向导」。
    /// 失败时把「改用官网 / 先装瓦特工具箱」两条退路写进提示里。
    /// </summary>
    private async void OnDownloadInstaller(object sender, RoutedEventArgs e)
    {
        if (_downloadCts is not null) return;

        _downloadCts = new CancellationTokenSource();
        _installerPath = null;
        RunButton.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Visible;
        DownloadButton.IsEnabled = false;
        DownloadBar.Value = 0;
        DownloadBar.Visibility = Visibility.Visible;
        ShowStatus(Strings.T("obs.dl.preparing"));

        try
        {
            var link = await AppServices.ObsRelease.GetWindowsInstallerLinkAsync().ConfigureAwait(true);
            if (!link.IsDirect)
            {
                // 拿不到直链：如实说明并打开发布页，而不是假装在下载。
                ShowStatus(Strings.T("obs.dl.fallback"));
                await AppServices.Host.OpenExternalAsync(link.Url).ConfigureAwait(true);
                return;
            }

            if (!ObsDownloadLinks.IsOfficialDownloadUrl(link.Url))
            {
                ShowStatus(Strings.T("obs.dl.insecure"));
                return;
            }

            var progress = new Progress<(long Received, long? Total)>(p =>
            {
                if (p.Total is > 0)
                {
                    var pct = Math.Min(100.0, p.Received * 100.0 / p.Total.Value);
                    DownloadBar.Value = pct;
                    StatusText.Text = Strings.T("obs.dl.progress",
                        Mb(p.Received), Mb(p.Total.Value), (int)pct);
                }
                else
                {
                    StatusText.Text = Strings.T("obs.dl.unknownSize", Mb(p.Received));
                }
            });

            var path = await AppServices.Updates
                .DownloadReleaseAssetAsync(link.Url, progress, _downloadCts.Token, "OBS-Studio-Setup_")
                .ConfigureAwait(true);

            if (path is null)
            {
                // 失败原因已经写在状态行里（含「改用官网 / 先装瓦特工具箱」的退路），这里不再叠一个更含糊的 toast。
                ShowStatus(Strings.T("obs.dl.failed", StatusText.Text));
                return;
            }

            _installerPath = path;
            DownloadBar.Value = 100;
            ShowStatus(Strings.T("obs.dl.done", path));
            RunButton.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException)
        {
            ShowStatus(Strings.T("obs.dl.cancelled"));
        }
        catch (Exception ex)
        {
            ShowStatus(Strings.T("obs.dl.failed", ex.Message));
        }
        finally
        {
            CancelButton.Visibility = Visibility.Collapsed;
            DownloadButton.IsEnabled = true;
            _downloadCts?.Dispose();
            _downloadCts = null;
        }
    }

    private void OnCancelDownload(object sender, RoutedEventArgs e)
    {
        try { _downloadCts?.Cancel(); }
        catch (Exception) { /* 取消失败无妨，下载自身有超时 */ }
    }

    /// <summary>
    /// 启动刚下好的官方安装包。用 ShellExecute 打开：**不传任何静默参数**，
    /// 让 OBS 自己的安装向导（含 UAC 与安装路径）完整走一遍。
    /// </summary>
    private void OnRunInstaller(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_installerPath)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _installerPath,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            ShowStatus(Strings.T("obs.dl.runFailed", ex.Message));
        }
    }

    // ---------------------------------------------------------------- 辅助

    private static string Mb(long bytes) => (bytes / 1024.0 / 1024.0).ToString("0.0") + " MB";

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
