using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using OBS_Helper.Wpf.Services.Update;

namespace OBS_Helper.Wpf.Controls;

/// <summary>
/// BUG 反馈卡（V2.9.3）：二维码 + 表单 / Issue 两个按钮。
///
/// 二维码是内嵌 PNG（<see cref="FeedbackLinks.QrResourceName"/>），**离线可扫**；
/// 资源缺失时不让界面留一块白框，而是给出「请用上面的按钮」的说明 —— 静默空白
/// 正是这个项目一直在避免的那种失败（看得见才好排查）。
/// </summary>
public partial class FeedbackCard : UserControl
{
    public FeedbackCard()
    {
        InitializeComponent();

        var qr = LoadQrImage();
        if (qr is null)
        {
            // 资源缺失：把二维码那一整格收掉（留个白框更让人困惑），并说明改用按钮打开表单。
            QrPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            QrImage.Source = qr;
        }

        ApplyLanguage();
    }

    /// <summary>
    /// 重新取一遍文案（V3.0 / F5）。
    ///
    /// 原先这几句文案只在**构造函数**里取一次，而本控件被搭建页 / 工具箱 / 反馈页共用、
    /// 页面实例又是被导航服务缓存的 —— 于是切语言后这几张卡仍是旧语言（自检「逐页语言重放」
    /// 就是这样把它抓出来的）。宿主页面在进入时调用本方法即可。
    /// </summary>
    public void ApplyLanguage()
    {
        TitleText.Text = Strings.T("feedback.title");
        DescText.Text = Strings.T("feedback.desc");
        QrHintText.Text = Strings.T("feedback.qrHint");
        // 安全上报引导：明确告诉用户「公开 Issue 不能贴细节」时该走哪里
        SecurityNoteText.Text = Strings.T("feedback.securityNote");

        if (QrImage.Source is null)
        {
            // 二维码资源确实缺失时把降级说明补回去（与构造函数里的判断同一口径）
            DescText.Text = Strings.T("feedback.desc") + "  " + Strings.T("feedback.qrMissing");
        }
    }

    private async void OnOpenForm(object sender, RoutedEventArgs e)
        => await OpenAsync(FeedbackLinks.BugReportForm).ConfigureAwait(true);

    private async void OnOpenIssues(object sender, RoutedEventArgs e)
        => await OpenAsync(FeedbackLinks.GitHubIssues).ConfigureAwait(true);

    private async Task OpenAsync(string url)
    {
        // 出站前先校验：常量被误改 / 被替换成第三方地址时，宁可不开也不把用户带走。
        if (!FeedbackLinks.IsTrustedFeedbackUrl(url))
        {
            ShowStatus(Strings.T("feedback.linkFailed") + url);
            return;
        }

        try
        {
            var ok = await AppServices.Host.OpenExternalAsync(url).ConfigureAwait(true);
            if (!ok) ShowStatus(Strings.T("feedback.linkFailed") + url);
        }
        catch (Exception ex)
        {
            ShowStatus(Strings.T("feedback.linkFailed") + url + "\n" + ex.Message);
        }
    }

    private void ShowStatus(string text)
    {
        StatusText.Text = text;
        StatusText.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// 读内嵌二维码 PNG。用 <see cref="BitmapCacheOption.OnLoad"/> 并在读完立刻关流，
    /// 否则文件句柄会一直被这个 BitmapImage 持有（单文件发布下更明显）。
    /// </summary>
    private static BitmapImage? LoadQrImage()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(FeedbackLinks.QrResourceName);
            if (stream is null) return null;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = stream;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }
}
