using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using OBS_Helper.Wpf.Navigation;
using OBS_Helper.Wpf.Services.Update;

namespace OBS_Helper.Wpf.Views;

/// <summary>
/// 帮助与反馈页（V2.9.4）。
///
/// 三块内容：
/// <list type="number">
///   <item>反馈入口（复用 V2.9.3 的 <see cref="Controls.FeedbackCard"/>：表单 / GitHub Issue / 离线二维码）；</item>
///   <item><b>一键复制报错材料</b>：把版本号、系统、OBS 连接与场景、录前自检结论拼成一段可粘贴的文本 ——
///         用户反馈时报错信息越全，定位越快；而让人自己翻三个页面抄版本号是不现实的；</item>
///   <item>作者与仓库（开发者署名 + 直达链接）。</item>
/// </list>
///
/// 隐私口径：复制的内容<b>只有版本号与本地检测结论</b>，不含路径、设备名、日志原文；
/// 是否发送完全由用户自己决定（本页不做任何上传）。
/// </summary>
public partial class FeedbackPage : UserControl, INavigationAware
{
    public FeedbackPage()
    {
        InitializeComponent();
    }

    public Task OnNavigatedToAsync(object? parameter)
    {
        VersionTextBlock.Text = Strings.T("feedback.report.version", AppVersion());
        AuthorText.Text = Strings.T("feedback.about.author", FeedbackLinks.AuthorProfile);
        StatusText.Visibility = Visibility.Collapsed;

        // 共用反馈卡的文案只在构造函数里取过一次（V3.0 / F5）：页面被缓存复用，必须每次刷新
        FeedbackCardControl.ApplyLanguage();

        // 页面实例被导航缓存复用：每次进页面都要让自检结论失效。
        // 不重置的话，之后每一次「复制材料」都会复用第一次算出的旧结论 ——
        // 而这一页的全部价值就是材料准（V2.9.4 审查发现）。
        _preflightLoaded = false;
        _preflightFail = 0;
        _preflightWarn = 0;
        return Task.CompletedTask;
    }

    /// <summary>当前应用版本（取 AssemblyInformationalVersion，取不到退回 FileVersion）。</summary>
    internal static string AppVersion()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info)) return info;

            var file = asm.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
            if (!string.IsNullOrWhiteSpace(file)) return file;
        }
        catch (Exception)
        {
            // 读取版本信息失败不该阻断页面
        }
        return "unknown";
    }

    /// <summary>
    /// 组装「报错材料」。抽成 internal static 是为了让自检 / 单测能直接校验
    /// 「一定包含版本号」这类不变量，而不用去点按钮。
    ///
    /// <paramref name="preflightFail"/> 为负表示本次没取到自检结果 —— 那时**不写具体数字**，
    /// 否则用户会粘出「录前自检：-1 项未通过、-1 项提醒」这种既不像话也不可读的句子。
    /// </summary>
    internal static string BuildReportText(string version, string os, bool obsConnected,
        string scene, int sceneCount, int preflightFail, int preflightWarn)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Strings.T("feedback.report.heading"));
        sb.AppendLine("- " + Strings.T("feedback.report.lineVersion", version));
        sb.AppendLine("- " + Strings.T("feedback.report.lineOs", os));
        sb.AppendLine("- " + Strings.T("feedback.report.lineObs",
            obsConnected ? Strings.T("feedback.report.obsConnected") : Strings.T("feedback.report.obsDisconnected")));
        if (obsConnected)
            sb.AppendLine("- " + Strings.T("feedback.report.lineScene", scene, sceneCount));

        sb.AppendLine(preflightFail < 0 || preflightWarn < 0
            ? "- " + Strings.T("feedback.report.linePreflightUnknown")
            : "- " + Strings.T("feedback.report.linePreflight", preflightFail, preflightWarn));

        sb.AppendLine();
        sb.AppendLine(Strings.T("feedback.report.footer"));
        return sb.ToString();
    }

    private async void OnCopyReport(object sender, RoutedEventArgs e)
    {
        try
        {
            // 自检结论延迟到第一次复制时才跑（进页面就跑一次磁盘检查没必要），跑过一次就复用
            if (!_preflightLoaded) await RefreshPreflightAsync().ConfigureAwait(true);

            var obs = AppServices.Obs;
            var text = BuildReportText(
                AppVersion(),
                Environment.OSVersion.VersionString,
                obs.IsConnected,
                obs.CurrentScene,
                obs.Scenes.Count,
                _preflightFail,
                _preflightWarn);

            if (await TrySetClipboardAsync(text).ConfigureAwait(true))
                ShowStatus(Strings.T("feedback.report.copied"), error: false);
            else
                ShowStatus(Strings.T("feedback.report.copyFailed"), error: true);
        }
        catch (Exception ex)
        {
            ShowStatus(Strings.T("feedback.report.copyFailed") + " " + ex.Message, error: true);
        }
    }

    private int _preflightFail;
    private int _preflightWarn;
    private bool _preflightLoaded;

    /// <summary>
    /// 录前自检结论是粘贴材料里最有价值的一段，但它是异步的 ——
    /// 用户点「复制」时才现算，避免进页面就跑一次磁盘检查。
    /// </summary>
    private async Task RefreshPreflightAsync()
    {
        try
        {
            var report = await AppServices.Preflight.RunAsync().ConfigureAwait(true);
            _preflightFail = report.FailCount;
            _preflightWarn = report.WarnCount;
        }
        catch (Exception)
        {
            // 自检跑不起来不是反馈页的错误：用 -1 表明「本次没测出来」
            _preflightFail = -1;
            _preflightWarn = -1;
        }
        _preflightLoaded = true;
    }

    private void OnOpenLogs(object sender, RoutedEventArgs e)
        => AppServices.Navigation.Navigate(Routes.Logs);

    private async void OnOpenAuthor(object sender, RoutedEventArgs e)
        => await OpenTrustedAsync(FeedbackLinks.AuthorProfile);

    private async void OnOpenRepo(object sender, RoutedEventArgs e)
        => await OpenTrustedAsync(FeedbackLinks.RepositoryUrl);

    private static async Task OpenTrustedAsync(string url)
    {
        // 与反馈卡同一套白名单：常量被误改时不把用户带走
        if (!FeedbackLinks.IsTrustedFeedbackUrl(url))
        {
            AppServices.Toast.Show(Strings.T("feedback.linkFailed") + url, "error");
            return;
        }
        try
        {
            var ok = await AppServices.Host.OpenExternalAsync(url).ConfigureAwait(true);
            if (!ok) AppServices.Toast.Show(Strings.T("feedback.linkFailed") + url, "error");
        }
        catch (Exception ex)
        {
            AppServices.Toast.Show(Strings.T("feedback.linkFailed") + url + "\n" + ex.Message, "error");
        }
    }

    /// <summary>
    /// 剪贴板可能被别的进程占着（Clipboard 抛 COMException 是常态），重试几次再放弃 ——
    /// 一次失败就告诉用户「复制失败」体验很差。
    ///
    /// V3.0（F8）：重试等待由 <c>Thread.Sleep(60)</c> 改为 <c>await Task.Delay</c>。
    /// 剪贴板是 UI 线程资源（必须在 UI 线程调用），所以不能整体挪到后台线程；
    /// 但**等待**期间必须让出 UI 线程，否则重试的两百毫秒里界面是卡住的。
    /// </summary>
    private static async Task<bool> TrySetClipboardAsync(string text)
    {
        for (var i = 0; i < 3; i++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (Exception)
            {
                if (i < 2) await Task.Delay(60).ConfigureAwait(true);
            }
        }
        return false;
    }

    private void ShowStatus(string text, bool error)
    {
        StatusText.Text = text;
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, error ? "DangerBrush" : "OkBrush");
        StatusText.Visibility = Visibility.Visible;
    }
}
