namespace OBS_Helper.Wpf.Services.Security;

/// <summary>
/// 外链白名单（V3.6.0 / G04）。
///
/// 背景：知识库、插件目录、排障指引与平台入口都会经热更新通道下发，里面的链接是**外部数据**。
/// 旧实现只校验 scheme，任何 <c>https://</c> 都能被 <c>Process.Start</c> 打开；一旦上游内容被篡改，
/// 用户点一下就会离开产品语境。这里把「本产品会主动打开哪些站点」收缩成一份可审计的域名表：
/// <list type="bullet">
///   <item>只允许 https，且必须是默认端口 443；</item>
///   <item>拒绝带 userinfo 的 URL（<c>https://github.com@evil.example</c> 一类混淆）；</item>
///   <item>允许精确域名与其子域名（<c>raw.githubusercontent.com</c> 命中 <c>githubusercontent.com</c>）；</item>
///   <item>本机 / 私网地址一律拒绝（外链不是本地服务入口）。</item>
/// </list>
/// 纯 BCL、零 IO，单测直接链接。
/// </summary>
public static class ExternalLinkPolicy
{
    /// <summary>允许打开的站点（精确域名；子域名自动放行）。</summary>
    private static readonly string[] AllowedDomains =
    {
        // OBS 官方与文档
        "obsproject.com",
        "obsproject.com.cn",
        "obs-versions.com",
        "obs-studio-app.github.io",

        // GitHub / 代码托管
        "github.com",
        "githubusercontent.com",
        "github.io",

        // 反馈 / 网盘 / 赞助入口
        "lanzou.com",
        "lanzoui.com",
        "lanzoux.com",
        "lanzouw.com",
        "lanzoub.com",
        "lanzoue.com",
        "lanzouu.com",
        "steampp.net",

        // 直播 / 视频 / 社交平台
        "bilibili.com",
        "douyin.com",
        "kuaishou.com",
        "xiaohongshu.com",
        "weixin.qq.com",
        "qq.com",
        "twitch.tv",
        "youtube.com",
        "youtu.be",
        "x.com",
        "instagram.com",
        "facebook.com",
        "tiktok.com",
        "nicovideo.jp",
        "trovo.live",
        "kick.com",
        "sooplive.co.kr",

        // 软件 / 运行时 / 模型
        "steampowered.com",
        "steamcommunity.com",
        "microsoft.com",
        "aka.ms",
        "ffmpeg.org",
        "rclone.org",
        "huggingface.co",
        "bigmodel.cn",
        "pollinations.ai",

        // 手机摄像头推流 App（用户自行安装，仅提供官方入口）
        "softvelum.com",

        // 常见排障资料站（知识库 / 指引会引用）
        "w3.org",
        "csdn.net",
        "appuals.com",
        "cubix.design",
        "eventlive.pro",
        "tsight.io",
        "techtippr.com",
        "fixlabguide.com",
        "positioniseverything.net",
        "google.com",
        "apple.com",
    };

    /// <summary>URL 是否允许交给系统浏览器打开。</summary>
    public static bool IsAllowed(string? url) => TryNormalize(url, out _);

    /// <summary>
    /// 归一化并校验；通过时返回绝对 URI。拒绝：非 https、非默认端口、带 userinfo、
    /// 空 host、本机/私网、以及不在白名单内的域名。
    /// </summary>
    public static bool TryNormalize(string? url, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed)) return false;

        if (parsed.Scheme != Uri.UriSchemeHttps) return false;
        if (!parsed.IsDefaultPort) return false;
        if (!string.IsNullOrEmpty(parsed.UserInfo)) return false;

        var host = parsed.IdnHost.TrimEnd('.').ToLowerInvariant();
        if (host.Length == 0) return false;

        foreach (var domain in AllowedDomains)
        {
            if (host.Equals(domain, StringComparison.Ordinal)
                || host.EndsWith("." + domain, StringComparison.Ordinal))
            {
                uri = parsed;
                return true;
            }
        }
        return false;
    }
}
