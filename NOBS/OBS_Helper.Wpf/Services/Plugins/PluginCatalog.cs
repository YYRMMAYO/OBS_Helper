using System.Text.Json;
using OBS_Helper.Wpf.Localization;

namespace OBS_Helper.Wpf.Services.Plugins;

/// <summary>插件广场的一个分类定义。</summary>
public sealed class PluginCategoryDef
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Icon { get; set; } = "";
}

/// <summary>插件广场的一个插件条目。</summary>
public sealed class PluginEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    /// <summary>徽标文案：热门 / AI 等；空表示无徽标。</summary>
    public string Badge { get; set; } = "";
    public string Desc { get; set; } = "";
    /// <summary>项目主页（GitHub 仓库或 OBS 论坛），点卡片打开。</summary>
    public string Url { get; set; } = "";
    /// <summary>GitHub 仓库（owner/repo），用于 Releases API 查最新版本；空表示查不了。</summary>
    public string Repo { get; set; } = "";
    /// <summary>本机插件 DLL 文件名主干（小写、不含扩展名，可多个别名），用于本地体检匹配。</summary>
    public List<string> Dlls { get; set; } = new();
    /// <summary>AI 类插件的开销说明（来自项目公开文档）；非 AI 插件为空。</summary>
    public string AiCostCpu { get; set; } = "";
    public string AiCostMem { get; set; } = "";

    /// <summary>
    /// 已知问题插件标注（V2.8，GAP-6）：「风险等级｜一句话说明」形式的风险提示；
    /// 空表示无标注。旧版 plugins.json 缺该字段时反序列化为空串，天然兼容。
    /// </summary>
    public string RiskNote { get; set; } = "";

    public bool HasRiskNote => RiskNote.Length > 0;

    public bool HasAiCost => AiCostCpu.Length > 0 || AiCostMem.Length > 0;

    /// <summary>
    /// 维护状态（V2.9，目录 v1.4）：<c>"active"</c> = 近 12 个月内有提交或发行，
    /// <c>"slow"</c> = 维护放缓。空值（旧目录 / 外部热更新文件缺字段）按「活跃」处理 ——
    /// 宁可不标注，也不要给维护正常的插件打上误导性的黄标。
    /// </summary>
    public string Maintain { get; set; } = "";

    /// <summary>维护状态的补充说明（如「最近提交 2024-01 · 暂无同类替代，仍可使用」）；无则空串。</summary>
    public string MaintainNote { get; set; } = "";

    /// <summary>维护放缓：需要在卡片上明示，避免用户装了才发现上游没人维护。</summary>
    public bool IsMaintenanceSlow => string.Equals(Maintain, "slow", StringComparison.OrdinalIgnoreCase);

    /// <summary>维护状态短标签（配合界面上的「维护：」前缀使用）。</summary>
    public string MaintenanceLabel => Strings.T(IsMaintenanceSlow ? "plugin.maintain.slow" : "plugin.maintain.active");

    /// <summary>维护状态行的展示文案：「活跃」/「放缓」+ 可选的补充说明。</summary>
    public string MaintenanceText
        => MaintainNote.Length > 0 ? $"{MaintenanceLabel} · {MaintainNote}" : MaintenanceLabel;

    /// <summary>
    /// 是否需要渲染维护状态行：维护放缓，或带补充说明（新收录条目标注了复核日期）时才展示。
    /// 「活跃且无说明」是所有条目的常态，逐张卡片重复一遍只是噪音，不展示。
    /// </summary>
    public bool HasMaintenanceInfo => IsMaintenanceSlow || MaintainNote.Length > 0;

    // ---------------------------------------------------------------- 与本机 OBS 的兼容性（V3.0 / D9）

    /// <summary>
    /// 与本机 OBS 的兼容性声明（可选字段，走热更新通道）。语法见
    /// <see cref="PluginCompatCore"/>：<c>30+</c> / <c>28-32</c> / <c>!33</c> / 逗号组合。
    /// 空值 = 未声明（不做任何提示）。
    /// </summary>
    public string ObsCompat { get; set; } = "";

    /// <summary>本机 OBS 版本（由插件页在渲染前注入；为空则不做兼容性判定）。</summary>
    public string LocalObsVersion { get; set; } = "";

    /// <summary>兼容性结论。</summary>
    public PluginCompatStatus CompatStatus => PluginCompatCore.Evaluate(ObsCompat, LocalObsVersion);

    /// <summary>是否需要在卡片上提示兼容性问题。</summary>
    public bool HasCompatWarning => PluginCompatCore.NeedsWarning(CompatStatus);

    /// <summary>
    /// 兼容性提示文案（含本机版本与插件要求），例如
    /// 「与本机 OBS 31.0.2 不匹配（该插件要求 30+）：升级 OBS 后再装」。
    /// </summary>
    public string CompatText
    {
        get
        {
            var requirement = PluginCompatCore.DescribeRequirement(ObsCompat);
            return CompatStatus switch
            {
                PluginCompatStatus.TooOld => Strings.T("plugin.compat.tooOld", LocalObsVersion, requirement),
                PluginCompatStatus.TooNew => Strings.T("plugin.compat.tooNew", LocalObsVersion, requirement),
                PluginCompatStatus.Broken => Strings.T("plugin.compat.broken", LocalObsVersion, requirement),
                _ => ""
            };
        }
    }
}

/// <summary>plugins.json 的根对象。</summary>
public sealed class PluginCatalogData
{
    public string Version { get; set; } = "";
    public string Updated { get; set; } = "";
    public string Note { get; set; } = "";
    public List<PluginCategoryDef> Categories { get; set; } = new();
    public List<PluginEntry> Plugins { get; set; } = new();
}

/// <summary>
/// 插件目录的纯逻辑部分：JSON 解析、DLL 名匹配、分类排序。
/// 不依赖 WPF / 注册表，可被单元测试工程直接链接编译。
/// </summary>
public static class PluginCatalogCore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>解析目录 JSON；内容无效（解析失败或没有条目）返回 null，调用方回退内置种子。</summary>
    public static PluginCatalogData? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var data = JsonSerializer.Deserialize<PluginCatalogData>(json, JsonOpts);
            if (data is null || data.Plugins.Count == 0) return null;
            return data;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>DLL 文件名 → 匹配用主干：小写、去掉路径与 .dll 扩展名。</summary>
    public static string NormalizeDllStem(string fileName)
    {
        var s = fileName ?? "";
        var slash = Math.Max(s.LastIndexOf('/'), s.LastIndexOf('\\'));
        if (slash >= 0) s = s[(slash + 1)..];
        if (s.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) s = s[..^4];
        return s.Trim().ToLowerInvariant();
    }

    /// <summary>按 DLL 文件名找广场条目；找不到返回 null。</summary>
    public static PluginEntry? MatchByDll(PluginCatalogData? catalog, string dllFileName)
    {
        if (catalog is null) return null;
        var stem = NormalizeDllStem(dllFileName);
        foreach (var p in catalog.Plugins)
        {
            foreach (var alias in p.Dlls)
            {
                if (string.Equals(alias?.Trim(), stem, StringComparison.Ordinal)) return p;
            }
        }
        return null;
    }

    /// <summary>按 id 找条目。</summary>
    public static PluginEntry? FindById(PluginCatalogData? catalog, string id)
    {
        if (catalog is null || string.IsNullOrEmpty(id)) return null;
        return catalog.Plugins.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>把插件按 categories 的声明顺序分组；未声明分类的插件排在最后。</summary>
    public static List<(PluginCategoryDef Category, List<PluginEntry> Items)> GroupByCategory(PluginCatalogData catalog)
    {
        var result = new List<(PluginCategoryDef, List<PluginEntry>)>();
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var cat in catalog.Categories)
        {
            var items = catalog.Plugins.Where(p => string.Equals(p.Category, cat.Key, StringComparison.OrdinalIgnoreCase)).ToList();
            known.Add(cat.Key);
            if (items.Count > 0) result.Add((cat, items));
        }

        // 数据里出现但 categories 未声明的分类：聚合为一个兜底组，避免条目消失
        var orphans = catalog.Plugins.Where(p => !known.Contains(p.Category)).ToList();
        if (orphans.Count > 0)
            result.Add((new PluginCategoryDef { Key = "_other", Label = Strings.T("plugin.categoryOther") }, orphans));

        return result;
    }

    /// <summary>接受 owner/repo 或完整 GitHub URL，归一化为 owner/repo；无法解析返回空串。</summary>
    public static string NormalizeRepo(string repo)
    {
        var s = (repo ?? "").Trim().TrimEnd('/');
        if (s.Length == 0) return "";

        if (Uri.TryCreate(s, UriKind.Absolute, out var uri))
        {
            // 只认 github.com 的绝对 URL；其他站点一律视为无效
            if (uri.Host != "github.com" && uri.Host != "www.github.com") return "";
            s = uri.AbsolutePath.TrimStart('/');
        }

        s = s.TrimStart('/');
        // 只保留 owner/repo 两段，忽略更深路径
        var parts = s.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2 ? parts[0] + "/" + parts[1] : "";
    }
}
