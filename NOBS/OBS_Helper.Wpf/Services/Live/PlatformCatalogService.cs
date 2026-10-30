using System.Text.Json;
using OBS_Helper.Wpf.Localization;
using OBS_Helper.Wpf.Models.Live;

namespace OBS_Helper.Wpf.Services.Live;

/// <summary>
/// 平台目录服务（V3.3.0 / P0-4）：读随包 <c>platforms.json</c> / <c>platforms.en-US.json</c>。
///
/// 数据驱动的原因：平台入口 / 按钮路径会变，写在代码里就等于每次改版都要发版。
/// 目录条目带 <c>verifiedAt</c>，界面必须如实展示「这条核对于哪天」——
/// 未核验的条目不承诺一键成功（PLAN_V3.3.0 §14.3）。
/// </summary>
public sealed class PlatformCatalogService
{
    private readonly object _lock = new();
    private PlatformCatalogData? _cache;
    private string _loadedLanguage = "";
    private string? _loadError;

    /// <summary>数据加载失败时的错误码（供界面展示）。</summary>
    public string? LoadError
    {
        get { lock (_lock) return _loadError; }
    }

    /// <summary>语言切换后丢弃缓存。</summary>
    public void Reload()
    {
        lock (_lock)
        {
            _cache = null;
            _loadError = null;
            _loadedLanguage = "";
        }
    }

    public PlatformCatalogData GetData()
    {
        lock (_lock)
        {
            if (_cache is not null && string.Equals(_loadedLanguage, Strings.Current, StringComparison.Ordinal))
                return _cache;

            _loadedLanguage = Strings.Current;
            try
            {
                var raw = ContentAssets.ReadEmbeddedWithFallback("platforms", Strings.Current, out var fellBack);
                if (fellBack)
                    FileLogger.Warn("Platforms", "英文平台目录缺失，已回退中文随包内容（英文界面下会显示中文）。");

                if (raw is null)
                {
                    _loadError = "PLATFORM_ASSET_MISSING";
                    _cache = new PlatformCatalogData();
                    return _cache;
                }

                var data = JsonSerializer.Deserialize<PlatformCatalogData>(raw, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (data is null)
                {
                    _loadError = "PLATFORM_PARSE_FAILED";
                    _cache = new PlatformCatalogData();
                    return _cache;
                }

                _loadError = null;
                _cache = data;
                return _cache;
            }
            catch (Exception ex)
            {
                FileLogger.Warn("Platforms", $"读取平台目录失败：{ex.Message}");
                _loadError = "PLATFORM_LOAD_FAILED";
                _cache = new PlatformCatalogData();
                return _cache;
            }
        }
    }

    /// <summary>按 id 找平台；找不到返回 null。</summary>
    public LivePlatform? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return GetData().Platforms.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>按 id 找第三方客户端；找不到返回 null。</summary>
    public CompanionApp? FindCompanion(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return GetData().Companions.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>某平台关联的第三方客户端。</summary>
    public IReadOnlyList<CompanionApp> CompanionsOf(LivePlatform? platform)
    {
        if (platform is null) return Array.Empty<CompanionApp>();
        var data = GetData();
        return platform.Companions
            .Select(id => data.Companions.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)))
            .Where(c => c is not null)
            .Select(c => c!)
            .ToList();
    }

    /// <summary>按区域分组（界面先展示国内，再展示海外）。</summary>
    public (IReadOnlyList<LivePlatform> China, IReadOnlyList<LivePlatform> Global) GroupByRegion()
    {
        var all = GetData().Platforms;
        return (
            all.Where(p => string.Equals(p.Region, "cn", StringComparison.OrdinalIgnoreCase)).ToList(),
            all.Where(p => !string.Equals(p.Region, "cn", StringComparison.OrdinalIgnoreCase)).ToList());
    }
}
