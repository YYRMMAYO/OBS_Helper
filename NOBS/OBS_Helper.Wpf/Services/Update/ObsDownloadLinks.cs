using OBS_Helper.Wpf.Localization;
namespace OBS_Helper.Wpf.Services.Update;

/// <summary>
/// OBS Studio 的**官方**获取入口常量（纯常量 + 纯校验，可被单测工程直接链接编译）。
///
/// 为什么要有这个类：不少用户「获取不到官方 OBS」——被搜索引擎广告位或软件下载站
/// 引导到仿冒站，装到捆绑包 / 木马（银狐类）版本。应用里所有「去下载 OBS」的入口
/// 一律走这里的常量，不散落 URL 字面量，也不允许出现第三方站点。
///
/// 官方渠道核实（2026-09 实测，HTTP 200）：
/// <list type="bullet">
///   <item><c>obsproject.com</c>（含 <c>/zh-cn</c> 中文站）—— OBS Project 官方站点；</item>
///   <item><c>obsproject.com/zh-cn/download</c> —— 官方下载页，安装包托管在 cdn-fastly.obsproject.com；</item>
///   <item><c>github.com/obsproject/obs-studio</c> —— 官方源码与 Release 二进制。</item>
/// </list>
/// 真伪判据（写进提示文案，便于用户自己核对）：OBS 完全免费开源、无需注册或付费、
/// 没有 Pro / 加速版；官方 Windows 安装包数字签名为 <c>OBS Project Corporation</c>。
/// </summary>
public static class ObsDownloadLinks
{
    /// <summary>官方厂商名（数字签名主体，提示文案里可用来核对）。</summary>
    public const string Vendor = "OBS Project";

    /// <summary>Windows 安装包数字签名主体。</summary>
    public const string WindowsSignerName = "OBS Project Corporation";

    /// <summary>官网首页（中文）。</summary>
    public const string OfficialSite = "https://obsproject.com/zh-cn";

    /// <summary>官网下载页（中文）—— 推荐首选入口。</summary>
    public const string OfficialDownload = "https://obsproject.com/zh-cn/download";

    /// <summary>官方 GitHub 仓库。</summary>
    public const string GitHubRepo = "https://github.com/obsproject/obs-studio";

    /// <summary>官方 GitHub Releases 页（全部版本）。</summary>
    public const string GitHubReleases = "https://github.com/obsproject/obs-studio/releases";

    /// <summary>官方 GitHub 最新 Release（始终指向当前稳定版）。</summary>
    public const string GitHubLatestRelease = "https://github.com/obsproject/obs-studio/releases/latest";

    /// <summary>放给用户看的安全提示：只认官方两处入口，并给出可自行核对的判据。</summary>
    public static string SafetyNote => Strings.T("downloads.safetyNote", WindowsSignerName);

    /// <summary>允许的官方域名：obsproject.com 及其子域（如 cdn-fastly.obsproject.com）。</summary>
    public static bool IsOfficialObsHost(string? host)
        => !string.IsNullOrEmpty(host)
           && (host.Equals("obsproject.com", StringComparison.OrdinalIgnoreCase)
               || host.EndsWith(".obsproject.com", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 允许的官方 GitHub 仓库地址：github.com/obsproject/obs-studio 本身或其子路径
    /// （要求路径边界完整——`/obsproject/obs-studio-evil` 这类同前缀的其它仓库不算）。
    /// </summary>
    public static bool IsOfficialGitHubUrl(Uri uri)
    {
        if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return false;

        const string repoPath = "/obsproject/obs-studio";
        var path = uri.AbsolutePath;
        return path.Equals(repoPath, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(repoPath + "/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 是否为「可以放给用户点」的官方下载入口：必须 https，且落在官方域名 / 官方仓库下。
    /// 用于校验动态拿到的安装包直链（GitHub API 返回的 browser_download_url），
    /// 防止被篡改的响应把用户引到第三方站点。
    /// </summary>
    public static bool IsOfficialDownloadUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps) return false;
        return IsOfficialObsHost(uri.Host) || IsOfficialGitHubUrl(uri);
    }
}