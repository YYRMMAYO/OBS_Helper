using System.Text.Json;
using OBS_Helper.Wpf.Services.Host;

namespace OBS_Helper.Wpf.Services.Plugins;

/// <summary>一个被关注插件的新版本发现。</summary>
public sealed record PluginWatchUpdate(string PluginId, string PluginName, string NewTag);

/// <summary>
/// 插件关注与更新提醒（路线图 P2-1）：用户在插件广场「关注」插件后，
/// 启动时静默查新（节流 24h），有新版本只 Toast 不弹窗。
///
/// 数据全部存 LocalStore（prefs.json）：
/// <list type="bullet">
///   <item><c>plugin_watch_v1</c>：关注的插件 id 列表；</item>
///   <item><c>plugin_watch_seen_v1</c>：每个插件上次已知的 Release tag（首次关注时静默建立基线）；</item>
///   <item><c>plugin_watch_lastcheck</c>：上次静默查新时间（UTC ISO），节流用。</item>
/// </list>
/// </summary>
public sealed class PluginWatchService
{
    /// <summary>静默查新节流：24h。手动进入插件广场不触发，只有启动维护调用。</summary>
    public static readonly TimeSpan SilentThrottle = TimeSpan.FromHours(24);

    private const string KeyWatch = "plugin_watch_v1";
    private const string KeySeen = "plugin_watch_seen_v1";
    private const string KeyLastCheck = "plugin_watch_lastcheck";

    private readonly LocalStore _store;
    private readonly Func<PluginCatalogData> _catalog;
    private readonly PluginReleaseService _releases;

    public PluginWatchService(LocalStore store, Func<PluginCatalogData> catalog, PluginReleaseService releases)
    {
        _store = store;
        _catalog = catalog;
        _releases = releases;
    }

    // ------------------------------------------------------------ 关注列表

    public IReadOnlyList<string> GetWatchedIds()
        => _store.GetObject<List<string>>(KeyWatch) ?? new List<string>();

    public bool IsWatched(string pluginId) => GetWatchedIds().Contains(pluginId, StringComparer.Ordinal);

    public void SetWatched(string pluginId, bool watched)
    {
        var ids = new List<string>(GetWatchedIds());
        var changed = false;
        if (watched && !ids.Contains(pluginId, StringComparer.Ordinal))
        {
            ids.Add(pluginId);
            changed = true;
        }
        else if (!watched && ids.RemoveAll(x => string.Equals(x, pluginId, StringComparison.Ordinal)) > 0)
        {
            changed = true;
            // 取消关注时顺带清掉基线，重新关注时重建
            var seen = GetSeenTags();
            if (seen.Remove(pluginId)) _store.SetObject(KeySeen, seen);
        }
        if (changed) _store.SetObject(KeyWatch, ids);
    }

    private Dictionary<string, string> GetSeenTags()
        => _store.GetObject<Dictionary<string, string>>(KeySeen) ?? new Dictionary<string, string>();

    // ------------------------------------------------------------ 静默查新

    /// <summary>
    /// 对所有关注插件做一次静默查新。
    /// 返回「有新版本」的插件列表；失败 / 节流内返回空列表，永不抛异常。
    /// 首次查询只为建立版本基线，不算「新版本」（避免刚关注就被轰炸）。
    /// </summary>
    public async Task<List<PluginWatchUpdate>> CheckForUpdatesAsync(bool force = false)
    {
        var result = new List<PluginWatchUpdate>();
        try
        {
            if (!force)
            {
                var last = _store.GetItem(KeyLastCheck);
                if (DateTime.TryParse(last, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t) &&
                    DateTime.UtcNow - t.ToUniversalTime() < SilentThrottle)
                {
                    return result; // 节流内跳过
                }
            }
            _store.SetItem(KeyLastCheck, DateTime.UtcNow.ToString("o"));

            var watched = GetWatchedIds();
            if (watched.Count == 0) return result;

            var data = _catalog();
            var seen = GetSeenTags();

            foreach (var id in watched)
            {
                var entry = PluginCatalogCore.FindById(data, id);
                if (entry is null || string.IsNullOrWhiteSpace(entry.Repo)) continue;

                var latest = await _releases.GetLatestAsync(entry.Repo).ConfigureAwait(false);
                if (latest is null || string.IsNullOrWhiteSpace(latest.Tag)) continue;

                var known = seen.GetValueOrDefault(id, "");
                if (string.IsNullOrEmpty(known))
                {
                    // 基线：第一次见到这个插件的最新版本号，静默记录
                    seen[id] = latest.Tag;
                    continue;
                }

                if (!string.Equals(known, latest.Tag, StringComparison.Ordinal))
                {
                    seen[id] = latest.Tag;
                    result.Add(new PluginWatchUpdate(entry.Id, entry.Name, latest.Tag));
                }
            }

            _store.SetObject(KeySeen, seen);
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Plugins", "关注插件查新异常：" + ex.Message);
        }
        return result;
    }
}
