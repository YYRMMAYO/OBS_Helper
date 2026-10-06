using OBS_Helper.Wpf.Localization;

namespace OBS_Helper.Wpf.Services.Update;

/// <summary>
/// BUG 反馈入口的常量与校验（V2.9.3）。纯常量 + 纯校验，可被单测工程直接链接编译。
///
/// 为什么单独成一个类：反馈入口是**用户按了会看到的东西**，把它散落在 UI 代码里
/// 很容易出现「链接失效但没人发现」。集中之后单测可以钉住「必须是 https + 限定域名」，
/// 也可以钉住「二维码资源名与 csproj 的 LogicalName 一致」。
/// </summary>
public static class FeedbackLinks
{
    /// <summary>BUG 收集表（腾讯文档）。用户点按钮或扫二维码都到这里。</summary>
    public const string BugReportForm = "https://docs.qq.com/form/page/DSXJMWkNDYVFUWHBo";

    /// <summary>表单页所在的域名（用于校验，避免被替换成钓鱼页）。</summary>
    public const string FormHost = "docs.qq.com";

    /// <summary>二维码图片的内嵌资源名（PNG，随包离线可扫；由 scripts 生成）。</summary>
    public const string QrResourceName = "OBS_Helper.Wpf.Assets.feedback_qr.png";

    /// <summary>二维码编码的内容必须与 <see cref="BugReportForm"/> 一模一样，单测会断言这一点。</summary>
    public const string QrPayload = BugReportForm;

    /// <summary>问题反馈页（GitHub Issues）：需要附日志 / 贴截图时更适合。</summary>
    public const string GitHubIssues = "https://github.com/YYRMMAYO/OBS_Helper/issues";

    /// <summary>作者 GitHub 主页（V2.9.4：反馈页与关于段展示开发者身份，走同一个常量）。</summary>
    public const string AuthorProfile = "https://github.com/YYRMMAYO";

    /// <summary>项目仓库主页（V2.9.4）。</summary>
    public const string RepositoryUrl = "https://github.com/YYRMMAYO/OBS_Helper";

    /// <summary>仓库 slug（owner/name）：用于拼「新建 Issue」链接（V3.0 / D8）。</summary>
    public const string RepositorySlug = "YYRMMAYO/OBS_Helper";

    /// <summary>
    /// 反馈 / 作者 / 仓库入口是否可以放给用户点：必须 https，且落在以下三者之一：
    /// <list type="bullet">
    ///   <item>表单域名 <see cref="FormHost"/>；</item>
    ///   <item><b>本仓库</b>路径 <c>/YYRMMAYO/OBS_Helper…</c>；</item>
    ///   <item><b>作者账户主页</b> <c>github.com/YYRMMAYO</c> 本身（V2.9.4）。</item>
    /// </list>
    ///
    /// 作者主页这条是**精确前缀**判定（<c>/YYRMMAYO</c> 或 <c>/YYRMMAYO/…</c>），
    /// 因而 <c>github.com/YYRMMAYOEvil</c> 这类同前缀仿冒账户仍被拒绝 ——
    /// 少一个斜杠的宽松写法（<c>StartsWith("/YYRMMAYO")</c>）会把仿冒账户一起放进来。
    /// </summary>
    public static bool IsTrustedFeedbackUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps) return false;

        if (uri.Host.Equals(FormHost, StringComparison.OrdinalIgnoreCase)) return true;
        if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return false;

        var path = uri.AbsolutePath;
        return path.StartsWith("/YYRMMAYO/OBS_Helper", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/YYRMMAYO", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/YYRMMAYO/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>表单地址的自检（启动自检与单测共用同一判据）。</summary>
    public static bool FormUrlIsValid => IsTrustedFeedbackUrl(BugReportForm);
}
