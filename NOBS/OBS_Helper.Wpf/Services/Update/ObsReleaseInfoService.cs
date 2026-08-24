using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace OBS_Helper.Wpf.Services.Update;

/// <summary>OBS Studio 最新版本信息。</summary>
public sealed record ObsReleaseInfo(
    string Tag,
    DateTime PublishedAt,
    string Url,
    string Summary,
    /// <summary>来源：live = 刚从 GitHub 拉取；cache = 离线缓存回退。</summary>
    string Source)
{
    public string PublishedText => PublishedAt == default ? "未知" : PublishedAt.ToLocalTime().ToString("yyyy-MM-dd");
}

/// <summary>
/// OBS 新版本情报服务（V2.6 工具箱）：
/// 拉取 obsproject/obs-studio 的最新 GitHub Release（tag、日期、说明摘要），
/// 给「是否值得升级」提供依据。结果本地缓存 6 小时；
/// 网络失败时回退到任意时间的缓存并标注来源，彻底无数据则返回 null。
/// 永不抛异常。
/// </summary>
public sealed class ObsReleaseInfoService
{
    private const string ApiUrl = "https://api.github.com/repos/obsproject/obs-studio/releases/latest";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);

    private static string CachePath
        => Path.Combine(Host.HostBridge.AppDataDirectory, "cache", "obs_release.json");

    private readonly HttpClient _http;

    public ObsReleaseInfoService() =>
        _http = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
        })
        {
            Timeout = TimeSpan.FromSeconds(12),
        };

    /// <summary>获取最新 Release 信息；失败回退缓存；两者皆无返回 null。</summary>
    public async Task<ObsReleaseInfo?> GetLatestAsync()
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, ApiUrl);
            req.Headers.UserAgent.ParseAdd("OBS-Helper/2.6");
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var resp = await _http.SendAsync(req).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            var info = Parse(json);
            if (info is not null)
            {
                WriteCache(info with { Source = "cache" });
                return info with { Source = "live" };
            }
        }
        catch (Exception ex)
        {
            FileLogger.Info("ObsRelease", $"在线获取失败，尝试缓存：{ex.Message}");
        }

        return ReadCache();
    }

    // -------------------------------------------------------------- 解析

    internal static ObsReleaseInfo? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var tag = root.TryGetProperty("tag_name", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString() ?? "" : "";
            if (tag.Length == 0) return null;

            var published = root.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String &&
                            DateTime.TryParse(p.GetString(), out var dt)
                ? dt : default;

            var url = root.TryGetProperty("html_url", out var u) && u.ValueKind == JsonValueKind.String
                ? u.GetString() ?? "" : "https://github.com/obsproject/obs-studio/releases";

            return new ObsReleaseInfo(tag, published, url, SummarizeBody(root), "live");
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>从 release body 提取要点：取 New Features / 变更段落的前几条，截断到 500 字。</summary>
    private static string SummarizeBody(JsonElement root)
    {
        try
        {
            if (!root.TryGetProperty("body", out var b) || b.ValueKind != JsonValueKind.String) return "";
            var body = b.GetString() ?? "";
            var lines = body.Split('\n');
            var picked = new List<string>();
            foreach (var raw in lines)
            {
                var line = raw.TrimEnd('\r').Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith('#')) continue;                       // 跳过标题行
                if (picked.Count == 0 && line.Length > 0 && !line.StartsWith('-') && !line.StartsWith('*'))
                    continue;                                             // 从第一条列表项开始收集
                if (line.StartsWith("- ") || line.StartsWith("* "))
                    picked.Add(line[2..].Trim());
                if (picked.Count >= 5) break;
            }
            var summary = string.Join("\n· ", picked.Take(5));
            if (summary.Length == 0) summary = body.Length > 300 ? body[..300] + "…" : body;
            else if (summary.Length > 500) summary = summary[..500] + "…";
            return summary.Length > 0 ? "· " + summary : "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    // -------------------------------------------------------------- 缓存

    /// <summary>缓存文件结构。public 以确保 System.Text.Json 反序列化无障碍。</summary>
    public sealed record CacheFile(ObsReleaseInfo Info, DateTime FetchedAt);

    private void WriteCache(ObsReleaseInfo info)
    {
        try
        {
            var path = CachePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new CacheFile(info, DateTime.UtcNow)));
        }
        catch (Exception) { }
    }

    private static ObsReleaseInfo? ReadCache()
    {
        try
        {
            if (!File.Exists(CachePath)) return null;
            var cached = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(CachePath));
            if (cached?.Info is null) return null;

            // 超过 TTL 的缓存标注为离线快照，避免误导
            var stale = DateTime.UtcNow - cached.FetchedAt > CacheTtl;
            return cached.Info with
            {
                Source = stale ? "cache-stale" : "cache",
                Summary = (stale ? "[离线快照]\n" : "") + cached.Info.Summary
            };
        }
        catch (Exception)
        {
            return null;
        }
    }
}
