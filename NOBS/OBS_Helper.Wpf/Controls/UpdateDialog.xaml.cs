using System.Diagnostics;
using System.Windows;
using OBS_Helper.Wpf.Services;

namespace OBS_Helper.Wpf.Controls;

/// <summary>
/// 发现新版本时的提示弹窗。
///
/// 与 <see cref="ConfirmDialog"/> 同款窗口样式（不跟随系统、深色模式不闪白），
/// 额外承载两种下载方式：
/// <list type="bullet">
///   <item>方式一：蓝奏云网盘（打开浏览器）——保留原有路径；</item>
///   <item>方式二：应用内加载 GitHub——不依赖浏览器，在应用内直接下载最新版安装包并启动安装。</item>
/// </list>
/// 弹窗内只做展示、打开浏览器与下载安装，不含版本比较之外的更新逻辑。
/// </summary>
public partial class UpdateDialog : Window
{
    private UpdateDialog()
    {
        InitializeComponent();
        DownloadLink.RequestNavigate += (_, e) =>
        {
            // Hyperlink 默认会用自己的导航逻辑，这里改为统一走系统浏览器。
            // 注意不能用 e.Uri：未显式设置 NavigateUri 时它为 null，直接 ToString() 会抛异常。
            e.Handled = true;
            _ = AppServices.Host.OpenExternalAsync(UpdateService.DownloadUrl);
        };
    }

    /// <summary>弹出更新提示框。返回用户选择（去下载 / 稍后再说 / GitHub 仓库 / 应用内下载）。</summary>
    public static UpdateDialogResult Show(Version? current, Version? latest)
    {
        var dlg = new UpdateDialog
        {
            Owner = Application.Current?.MainWindow
        };
        dlg._currentVersion = current;

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

    /// <summary>当前应用版本（用于应用内下载前的版本比较）。</summary>
    private Version? _currentVersion;

    private UpdateDialogResult _result = UpdateDialogResult.Later;

    // ------------------------------------------------------------ 方式一：蓝奏云

    private async void OnDownload(object sender, RoutedEventArgs e)
    {
        _result = UpdateDialogResult.Download;
        // 「去下载」直接打开蓝奏云链接（弹窗关闭前触发，避免被调用方忽略返回值导致点了没反应）
        await AppServices.Host.OpenExternalAsync(UpdateService.DownloadUrl);
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

    // ------------------------------------------------------------ 方式二：应用内加载 GitHub

    /// <summary>
    /// 应用内加载 GitHub 下载：
    /// 1) 查最新 Release，定位安装包资产；
    /// 2) 版本号先去掉前头的 "V"（不区分大小写，V/v 都剥掉），再与当前版本比大小，避免把
    ///    "V1.4.8" 和 "1.4.8" 当成不同版本；最新版不高于当前版则直接提示，不下载；
    /// 3) 应用内下载到临时目录，完成后启动安装程序。
    /// </summary>
    private async void OnDownloadGithub(object sender, RoutedEventArgs e)
    {
        // 下载期间锁定全部按钮，避免重复点击 / 中途切换路径
        SetDownloading(true);

        var info = await AppServices.Updates.GetLatestReleaseAsync();
        if (!info.IsOk)
        {
            SetGithubStatus($"获取最新版本失败：{info.Error}（可改用蓝奏云，或到 GitHub 仓库手动下载。）");
            SetDownloading(false);
            return;
        }

        // 去掉版本号前头的 "V"（不论大小写）再比较版本大小：
        // 最新 Release 不高于当前版本时，没有下载的必要。
        var latestVersion = UpdateService.ParseVersion(info.Tag);
        if (latestVersion is not null && _currentVersion is not null && latestVersion <= _currentVersion)
        {
            SetGithubStatus($"GitHub 最新版本 {latestVersion} 不高于当前版本 {_currentVersion.Major}.{_currentVersion.Minor}.{_currentVersion.Build}，无需下载。");
            SetDownloading(false);
            return;
        }

        GithubStatusText.Text = latestVersion is null
            ? $"正在从 GitHub 下载最新版（{info.Tag}）…"
            : $"正在从 GitHub 下载 V{latestVersion} 安装包…";

        var progress = new Progress<(long Received, long? Total)>(p =>
        {
            if (p.Total is > 0)
            {
                GithubProgressBar.Value = Math.Min(100, p.Received * 100.0 / p.Total.Value);
                GithubStatusText.Text = $"正在下载… {FormatMb(p.Received)} / {FormatMb(p.Total.Value)}";
            }
            else
            {
                GithubStatusText.Text = $"正在下载… {FormatMb(p.Received)}";
            }
        });

        var path = await AppServices.Updates.DownloadReleaseAssetAsync(info.SetupAssetUrl!, progress);
        if (path is null)
        {
            SetGithubStatus("下载失败，请稍后重试；也可以改用蓝奏云网盘下载。");
            SetDownloading(false);
            return;
        }

        SetGithubStatus("下载完成，正在启动安装程序…");
        _result = UpdateDialogResult.Download;

        // 启动安装包（UAC 提权由安装程序自行申请）
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception)
        {
            SetGithubStatus($"安装包已下载到：{path}\n启动安装程序失败，请手动双击该文件安装。");
            SetDownloading(false);
            return;
        }

        Close();
    }

    private void SetDownloading(bool downloading)
    {
        GithubDownloadButton.IsEnabled = !downloading;
        DownloadButton.IsEnabled = !downloading;
        RepoButton.IsEnabled = !downloading;
        LaterButton.IsEnabled = !downloading;
        GithubProgressPanel.Visibility = downloading ? Visibility.Visible : Visibility.Collapsed;
        if (!downloading)
        {
            GithubProgressBar.Value = 0;
        }
    }

    private void SetGithubStatus(string text)
    {
        GithubStatusText.Text = text;
        GithubStatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "MutedBrush");
    }

    private static string FormatMb(long bytes)
        => bytes >= 1024 * 1024
            ? $"{bytes / 1024.0 / 1024.0:0.0} MB"
            : $"{bytes / 1024.0:0.0} KB";
}

/// <summary>用户在更新提示弹窗中的选择。</summary>
public enum UpdateDialogResult
{
    /// <summary>去下载（打开蓝奏云，或应用内完成 GitHub 下载）。</summary>
    Download,

    /// <summary>稍后再说。</summary>
    Later,

    /// <summary>打开 GitHub 仓库。</summary>
    Repo,
}
