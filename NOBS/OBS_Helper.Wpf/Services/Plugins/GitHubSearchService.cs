using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using OBS_Helper.Wpf.Models.Plugins;

namespace OBS_Helper.Wpf.Services.Plugins;

/// <summary>
/// GitHub 搜索 / Release 查询（V3.3.0 / P0-5）。
///
/// 限频策略（PLAN_V3.3.0 §8.1）：搜索 API 匿名额度只有 10 次/分钟，因此：
/// <list type="bullet">
///   <item>只在用户点击「搜索」时发起，不做输入即搜；</item>
///   <item>结果缓存 5 分钟（同一关键词重复点不耗额度）；</item>
///   <item>403/429 时给出明确错误码并进入 60 秒冷却，不自动重试；</item>
///   <item>可选 token 由调用方提供（存 secrets.dat），没有就用匿名。</item>
/// </list>
/// </summary>
public sealed class GitHubSearchService : IDisposable
{
    private readonly HttpClient _http;
    private readonly Func<Task<string?>>? _tokenProvider;
    private readonly Dictionary<string, (DateTime At, IReadOnlyList<GitHubRepoResult> Data)> _searchCache = new();
    private readonly Dictionary<string, (DateTime At, string Version, IReadOnlyList<PluginReleaseAsset> Assets)> _releaseCache = new();
    private readonly object _gate = new();
    private DateTime _cooldownUntil = DateTime.MinValue;

    public GitHubSearchService(HttpClient? http = null, Func<Task<string?>>? tokenProvider = null)
    {
        _http = http ?? new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        _tokenProvider = tokenProvider;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("OBS-Helper/3.3 (+https://github.com/YYRMMAYO/OBS_Helper)");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    /// <summary>搜索 OBS 插件仓库。失败时返回空列表 + 错误码（<c>NET_*</c>）。</summary>
    public async Task<(IReadOnlyList<GitHubRepoResult> Repos, string ErrorCode)> SearchAsync(string query, CancellationToken ct)
    {
        var key = (query ?? "").Trim().ToLowerInvariant();
        lock (_gate)
        {
            if (_searchCache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < TimeSpan.FromMinutes(5))
                return (hit.Data, "");
            if (DateTime.UtcNow < _cooldownUntil) return (Array.Empty<GitHubRepoResult>(), "NET_RATE_LIMITED");
        }

        var url = PluginInstallCore.BuildSearchUrl(key);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            await AddTokenAsync(req).ConfigureAwait(false);
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);

            if (resp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                lock (_gate) _cooldownUntil = DateTime.UtcNow.AddSeconds(60);
                return (Array.Empty<GitHubRepoResult>(), "NET_RATE_LIMITED");
            }
            if (!resp.IsSuccessStatusCode) return (Array.Empty<GitHubRepoResult>(), "NET_HTTP_" + (int)resp.StatusCode);

            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var repos = GitHubSearchCore.ParseSearch(json, out var parseError);
            if (parseError.Length > 0) return (Array.Empty<GitHubRepoResult>(), parseError);

            lock (_gate) _searchCache[key] = (DateTime.UtcNow, repos);
            return (repos, "");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (Array.Empty<GitHubRepoResult>(), "NET_TIMEOUT");
        }
        catch (Exception ex)
        {
            FileLogger.Warn("GitHub", $"搜索失败（{query}）：{ex.Message}");
            return (Array.Empty<GitHubRepoResult>(), "NET_OFFLINE");
        }
    }

    /// <summary>读取最新 Release 的资产（用于「粘贴仓库导入」）。</summary>
    public async Task<(string Version, IReadOnlyList<PluginReleaseAsset> Assets, string ErrorCode)> GetLatestReleaseAsync(
        string ownerRepo, CancellationToken ct)
    {
        if (!PluginInstallCore.TryParseOwnerRepo(ownerRepo, out var slug))
            return ("", Array.Empty<PluginReleaseAsset>(), "NET_BAD_REPO");

        lock (_gate)
        {
            if (_releaseCache.TryGetValue(slug, out var hit) && DateTime.UtcNow - hit.At < TimeSpan.FromMinutes(10))
                return (hit.Version, hit.Assets, "");
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, PluginInstallCore.BuildLatestReleaseUrl(slug));
            await AddTokenAsync(req).ConfigureAwait(false);
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);

            if (resp.StatusCode == HttpStatusCode.NotFound)
                return ("", Array.Empty<PluginReleaseAsset>(), "NET_NO_RELEASE");
            if (resp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                lock (_gate) _cooldownUntil = DateTime.UtcNow.AddSeconds(60);
                return ("", Array.Empty<PluginReleaseAsset>(), "NET_RATE_LIMITED");
            }
            if (!resp.IsSuccessStatusCode)
                return ("", Array.Empty<PluginReleaseAsset>(), "NET_HTTP_" + (int)resp.StatusCode);

            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var (version, assets, error) = GitHubSearchCore.ParseLatestRelease(json);
            if (error.Length > 0) return ("", Array.Empty<PluginReleaseAsset>(), error);

            lock (_gate) _releaseCache[slug] = (DateTime.UtcNow, version, assets);
            return (version, assets, "");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ("", Array.Empty<PluginReleaseAsset>(), "NET_TIMEOUT");
        }
        catch (Exception ex)
        {
            FileLogger.Warn("GitHub", $"读取 Release 失败（{slug}）：{ex.Message}");
            return ("", Array.Empty<PluginReleaseAsset>(), "NET_OFFLINE");
        }
    }

    private async Task AddTokenAsync(HttpRequestMessage req)
    {
        if (_tokenProvider is null) return;
        try
        {
            var token = await _tokenProvider().ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(token))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token!.Trim());
            }
        }
        catch (Exception)
        {
            // token 读不到就匿名请求，不让它把搜索拖垮
        }
    }

    public void Dispose() => _http.Dispose();
}
