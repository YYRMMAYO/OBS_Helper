using System.Net.Http;

namespace OBS_Helper.Wpf.Services.Plugins;

/// <summary>GitHub 连通性探测结果。</summary>
public enum GitHubReachabilityStatus
{
    Unknown,
    Reachable,
    Timeout,
    Failed,
}

/// <summary>
/// GitHub 连通性探测（V3.5.0 加速器引导）。
///
/// 只发一次很小的 GET（<c>api.github.com/rate_limit</c>），不携带任何用户数据；
/// 失败不抛异常，交给界面显示「超时 / 失败 + 下一步」。
/// </summary>
public static class GitHubReachability
{
    /// <summary>探测地址：GitHub API 的公开限流端点，体积小且不需要认证。</summary>
    public const string ProbeUrl = "https://api.github.com/rate_limit";

    /// <summary>界面文案键：探测结果 → 本地化 key。</summary>
    public static string MessageKey(GitHubReachabilityStatus status) => status switch
    {
        GitHubReachabilityStatus.Reachable => "plugin.accelerator.reachable",
        GitHubReachabilityStatus.Timeout => "plugin.accelerator.timeout",
        _ => "plugin.accelerator.failed",
    };

    /// <summary>把异常归类成超时 / 失败；纯逻辑，可单测。</summary>
    public static GitHubReachabilityStatus Classify(Exception? error, bool success)
    {
        if (success) return GitHubReachabilityStatus.Reachable;
        if (error is TaskCanceledException or TimeoutException) return GitHubReachabilityStatus.Timeout;
        if (error is HttpRequestException { InnerException: TaskCanceledException }) return GitHubReachabilityStatus.Timeout;
        return GitHubReachabilityStatus.Failed;
    }

    /// <summary>执行一次探测；永不抛异常。返回状态与失败原因（成功时为空）。</summary>
    public static async Task<(GitHubReachabilityStatus Status, string Error)> ProbeAsync(
        HttpClient? http = null, CancellationToken ct = default)
    {
        var client = http ?? SharedHttp;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            using var resp = await client.GetAsync(ProbeUrl, timeout.Token).ConfigureAwait(false);
            return resp.IsSuccessStatusCode
                ? (GitHubReachabilityStatus.Reachable, "")
                : (GitHubReachabilityStatus.Failed, $"HTTP {(int)resp.StatusCode}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException)
        {
            return (Classify(ex, false), ex.Message);
        }
        catch (Exception ex)
        {
            return (GitHubReachabilityStatus.Failed, ex.Message);
        }
    }

    private static readonly HttpClient SharedHttp = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OBS-Helper/3.5 (+https://github.com/YYRMMAYO/OBS_Helper)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
