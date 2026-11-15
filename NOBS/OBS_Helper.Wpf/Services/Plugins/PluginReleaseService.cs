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
/// 匿名限额只有 60 次/小时/IP，因此做四层节流：
/// <list type="bullet">
///   <item>内存缓存：进程内 24h 内直接复用，页面反复进出零请求；</item>
///   <item>磁盘缓存：%LocalAppData%\OBS_Helper\data\plugin_releases.json，重启后仍有效；</item>
///   <item>在途合并：同一仓库的并发请求只发一次；全局串行队列降低瞬时压力；</item>
///   <item>失败冷却（V3.0 / F6）：失败也留一条短 TTL 的记忆。只有成功缓存时，限流 / 离线
///     会让「边打边筛」的每一次输入都把同一批 403 重打一遍，白白烧掉本就只有 60 次/小时的额度。</item>
/// </list>
/// 失败静默：返回过期缓存或 null，绝不打扰用户。
/// </summary>
public sealed class PluginReleaseService
{
    /// <summary>单条缓存的保鲜期。GitHub 上多数活跃插件月更一次，24h 足够。</summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    /// <summary>失败冷却的最大间隔。指数退避从 10 秒起步，最多 5 分钟。</summary>
    public static readonly TimeSpan FailureTtl = TimeSpan.FromMinutes(5);

    /// <summary>首次失败的冷却时间；连续失败按 2 倍递增，封顶 <see cref="FailureTtl"/>。</summary>
    public static readonly TimeSpan FailureBaseDelay = TimeSpan.FromSeconds(10);

    private const string ApiBase = "https://api.github.com/repos/";

    private static readonly HttpClient Http = CreateClient();
    private static readonly SemaphoreSlim NetworkGate = new(2, 2);
    private static readonly ConcurrentDictionary<string, Lazy<Task<PluginReleaseInfo?>>> InFlight = new(StringComparer.OrdinalIgnoreCase);

    private static string CacheFile => Path.Combine(Host.HostBridge.AppDataDirectory, "data", "plugin_releases.json");

    private sealed record FailureState(int Count, DateTime NextAttemptUtc);

    /// <summary>
    /// 「这个仓库刚刚查失败过」的内存记忆（仓库 → 失败次数 + 下次可重试时刻）。
    /// 成功缓存 24h 且落盘；失败记忆只活在进程内，按指数退避自动失效。
    /// </summary>
    private readonly ConcurrentDictionary<string, FailureState> _failures = new(StringComparer.OrdinalIgnoreCase);

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
    /// 查询一个仓库的最新 Release 版本。命中新鲜缓存立即返回；处于失败冷却期直接给结论（过期缓存或 null）；
    /// 网络失败时回退过期缓存并记下这次失败；两者皆无返回 null。永不抛异常。
    /// </summary>
    public Task<PluginReleaseInfo?> GetLatestAsync(string repo) => GetLatestAsync(repo, forceRefresh: false);

    /// <summary>带强制刷新的查询（设置页「手动刷新」等场景）。</summary>
    public async Task<PluginReleaseInfo?> GetLatestAsync(string repo, bool forceRefresh)
    {
        var key = NormalizeRepo(repo);
        if (key.Length == 0) return null;

        // 内存新鲜缓存
        if (!forceRefresh && TryGetFresh(key, out var fresh)) return fresh;

        // 失败冷却：刚失败过的仓库在 TTL 内不再打网络。这一条挡的是「在途合并」之外的那一大类重复 ——
        // 上一次请求早已结束，用户在搜索框里每敲一下都会重新问一遍同样这批仓库（F6）。
        if (!forceRefresh && IsCoolingDown(key)) return GetStale(key);

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
        if (!forceRefresh && IsCoolingDown(key)) return GetStale(key);

        var fetched = await FetchFromGitHubAsync(key).ConfigureAwait(false);
        if (fetched is not null)
        {
            // 这次成功了：之前的失败记忆立刻作废，别让一条旧记录压着已经拿得到的结果
            _failures.TryRemove(key, out _);
            SaveToCaches(key, fetched);
            return fetched;
        }

        // 记下这次失败：按连续失败次数指数退避（10s → 20s → 40s …，封顶 5 分钟）
        _failures.AddOrUpdate(key,
            _ => new FailureState(1, DateTime.UtcNow + FailureBaseDelay),
            (_, old) =>
            {
                var next = old.Count + 1;
                var seconds = Math.Min(
                    FailureBaseDelay.TotalSeconds * Math.Pow(2, Math.Min(next - 1, 6)),
                    FailureTtl.TotalSeconds);
                return new FailureState(next, DateTime.UtcNow + TimeSpan.FromSeconds(seconds));
            });

        // 网络失败：有过期缓存就先用着
        return GetStale(key);
    }

    /// <summary>该仓库是否处在失败冷却期内（此刻不该再打网络）。</summary>
    private bool IsCoolingDownCore(string key)
        => _failures.TryGetValue(key, out var state) && DateTime.UtcNow < state.NextAttemptUtc;

    /// <summary>供页面显示「查询冷却中」；仓库为空时返回 false。</summary>
    public bool IsCoolingDown(string repo)
        => IsCoolingDownCore(NormalizeRepo(repo));

    /// <summary>取仍然可用的过期磁盘缓存；没有则 null（失败回退与冷却期返回的是同一个东西）。</summary>
    private PluginReleaseInfo? GetStale(string key)
        => _diskCache is not null && _diskCache.TryGetValue(key, out var stale) ? stale : null;

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
