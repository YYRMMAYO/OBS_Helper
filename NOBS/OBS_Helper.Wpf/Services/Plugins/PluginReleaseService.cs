using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace OBS_Helper.Wpf.Services.Plugins;

/// <summary>一次插件最新版本查询结果。</summary>
public sealed record PluginReleaseInfo(string Repo, string Tag, DateTime CheckedUtc);

/// <summary>
/// 插件最新版本查询（路线图 P1-1）：GitHub Releases API 取各插件仓库的最新 tag。
///
/// 匿名限额只有 60 次/小时/IP，因此做三层节流：
/// <list type="bullet">
///   <item>内存缓存：进程内 24h 内直接复用，页面反复进出零请求；</item>
///   <item>磁盘缓存：%LocalAppData%\OBS_Helper\data\plugin_releases.json，重启后仍有效；</item>
///   <item>在途合并：同一仓库的并发请求只发一次；全局串行队列降低瞬时压力。</item>
/// </list>
/// 失败静默：返回过期缓存或 null，绝不打扰用户。
/// </summary>
public sealed class PluginReleaseService
{
    /// <summary>单条缓存的保鲜期。GitHub 上多数活跃插件月更一次，24h 足够。</summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    private const string ApiBase = "https://api.github.com/repos/";

    private static readonly HttpClient Http = CreateClient();
    private static readonly SemaphoreSlim NetworkGate = new(1, 1);
    private static readonly ConcurrentDictionary<string, Lazy<Task<PluginReleaseInfo?>>> InFlight = new(StringComparer.OrdinalIgnoreCase);

    private static string CacheFile => Path.Combine(Host.HostBridge.AppDataDirectory, "data", "plugin_releases.json");

    private Dictionary<string, PluginReleaseInfo>? _diskCache = LoadDiskCache();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        // GitHub API 强制要求 User-Agent
        var ver = typeof(PluginReleaseService).Assembly.GetName().Version;
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"OBS_Helper.Wpf-Plugins/{ver?.Major}.{ver?.Minor}.{ver?.Build}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>
    /// 查询一个仓库的最新 Release 版本。命中新鲜缓存立即返回；
    /// 网络失败时回退过期缓存；两者皆无返回 null。永不抛异常。
    /// </summary>
    public Task<PluginReleaseInfo?> GetLatestAsync(string repo) => GetLatestAsync(repo, forceRefresh: false);

    /// <summary>带强制刷新的查询（设置页「手动刷新」等场景）。</summary>
    public async Task<PluginReleaseInfo?> GetLatestAsync(string repo, bool forceRefresh)
    {
        var key = NormalizeRepo(repo);
        if (key.Length == 0) return null;

        // 内存新鲜缓存
        if (!forceRefresh && TryGetFresh(key, out var fresh)) return fresh;

        // 在途合并：同仓库并发只发一次请求
        var lazy = InFlight.GetOrAdd(key, k => new Lazy<Task<PluginReleaseInfo?>>(
            () => FetchWithFallbackAsync(k, forceRefresh), LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            return await lazy.Value.ConfigureAwait(false);
        }
        finally
        {
            InFlight.TryRemove(key, out _);
        }
    }

    private async Task<PluginReleaseInfo?> FetchWithFallbackAsync(string key, bool forceRefresh)
    {
        if (!forceRefresh && TryGetFresh(key, out var fresh)) return fresh;

        var fetched = await FetchFromGitHubAsync(key).ConfigureAwait(false);
        if (fetched is not null)
        {
            SaveToCaches(key, fetched);
            return fetched;
        }

        // 网络失败：有过期缓存就先用着
        return _diskCache is not null && _diskCache.TryGetValue(key, out var stale) ? stale : null;
    }

    private bool TryGetFresh(string key, out PluginReleaseInfo info)
    {
        info = null!;
        PluginReleaseInfo? found = null;
        if (_diskCache is not null && _diskCache.TryGetValue(key, out var disk) &&
            DateTime.UtcNow - disk.CheckedUtc < CacheTtl)
        {
            found = disk;
        }
        if (found is null) return false;
        info = found;
        return true;
    }

    private async Task<PluginReleaseInfo?> FetchFromGitHubAsync(string repo)
    {
        await NetworkGate.WaitAsync().ConfigureAwait(false);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var resp = await Http.GetAsync($"{ApiBase}{repo}/releases/latest", cts.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null; // 404 = 无 Release；403 = 限流，都静默

            var body = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("tag_name", out var tagProp)) return null;
            var tag = tagProp.GetString();
            if (string.IsNullOrWhiteSpace(tag)) return null;

            return new PluginReleaseInfo(repo, tag.Trim(), DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
        finally
        {
            NetworkGate.Release();
        }
    }

    private void SaveToCaches(string key, PluginReleaseInfo info)
    {
        var cache = _diskCache ??= new Dictionary<string, PluginReleaseInfo>(StringComparer.OrdinalIgnoreCase);
        cache[key] = info;
        PersistDiskCache(cache);
    }

    // ------------------------------------------------------------ 磁盘缓存

    private sealed class CacheEntry
    {
        public string Tag { get; set; } = "";
        public DateTime CheckedUtc { get; set; }
    }

    private static Dictionary<string, PluginReleaseInfo>? LoadDiskCache()
    {
        try
        {
            var file = CacheFile;
            if (!File.Exists(file)) return new Dictionary<string, PluginReleaseInfo>(StringComparer.OrdinalIgnoreCase);
            var raw = JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(File.ReadAllText(file));
            if (raw is null) return new Dictionary<string, PluginReleaseInfo>(StringComparer.OrdinalIgnoreCase);
            var result = new Dictionary<string, PluginReleaseInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var (repo, entry) in raw)
            {
                if (!string.IsNullOrWhiteSpace(repo) && !string.IsNullOrWhiteSpace(entry.Tag))
                    result[repo] = new PluginReleaseInfo(repo, entry.Tag, entry.CheckedUtc);
            }
            return result;
        }
        catch (Exception)
        {
            return new Dictionary<string, PluginReleaseInfo>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void PersistDiskCache(Dictionary<string, PluginReleaseInfo> cache)
    {
        try
        {
            var dir = Path.GetDirectoryName(CacheFile);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var dict = cache.ToDictionary(kv => kv.Key, kv => new CacheEntry { Tag = kv.Value.Tag, CheckedUtc = kv.Value.CheckedUtc });
            File.WriteAllText(CacheFile, JsonSerializer.Serialize(dict));
        }
        catch (Exception)
        {
            // 缓存写盘失败不影响功能
        }
    }

    /// <summary>接受 owner/repo 或完整 GitHub URL，归一化为 owner/repo；无法解析返回空串。</summary>
    public static string NormalizeRepo(string repo) => PluginCatalogCore.NormalizeRepo(repo);
}
