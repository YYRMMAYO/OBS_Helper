namespace OBS_Helper.Wpf.Services.Shell;

/// <summary>单个功能条目（用于功能中心卡片上的能力清单）。</summary>
public sealed record FeatureEntry(string Id, string TitleKey, string Route, string Tab = "");

/// <summary>一个功能模块（对应一个页面 / 一组能力）。</summary>
public sealed record FeatureModule(
    string Id,
    string TitleKey,
    string DescKey,
    string IconKey,
    string Route,
    string Tab,
    string Version,
    bool Highlighted,
    IReadOnlyList<FeatureEntry> Features);

/// <summary>
/// 功能模块目录（V3.4.0 起）。
///
/// 约定：以后每个版本新增的便捷功能，都必须登记为一个模块或加入既有模块；
/// 功能中心按「本版新增 / 常用功能」两段展示，避免新能力只藏在某个页面深处。
/// 新增时补文案键与路由，<c>FeatureCatalogTests</c> 会校验数量、ID 唯一与路由存在。
/// </summary>
public static class FeatureCatalog
{
    public const string CurrentVersion = "V3.4.0";

    public static readonly FeatureModule[] Modules =
    {
        // ------------------------------------------------------------ 本版新增
        new("mobile", "feature.module.mobile.title", "feature.module.mobile.desc", "IconGeomDeviceMobile",
            "mobile", "remote", CurrentVersion, true,
            new FeatureEntry[]
            {
                new("M01", "feature.M01", "mobile", "remote"),
                new("M02", "feature.M02", "mobile", "remote"),
                new("M03", "feature.M03", "mobile", "camera"),
                new("M04", "feature.M04", "mobile", "camera"),
                new("M05", "feature.M05", "mobile", "remote"),
                new("M06", "feature.M06", "mobile", "devices"),
                new("M07", "feature.M07", "mobile", "remote"),
                new("M08", "feature.M08", "mobile", "camera"),
                new("M09", "feature.M09", "mobile", "multicam"),
                new("M10", "feature.M10", "mobile", "multicam"),
                new("M11", "feature.M11", "mobile", "camera"),
                new("M12", "feature.M12", "mobile", "multicam"),
            }),
        new("interaction", "feature.module.interaction.title", "feature.module.interaction.desc", "IconGeomBroadcast",
            "interaction", "", CurrentVersion, false,
            new FeatureEntry[]
            {
                new("C01", "feature.C01", "interaction"),
                new("C02", "feature.C02", "interaction"),
                new("C03", "feature.C03", "interaction"),
                new("C04", "feature.C04", "interaction"),
                new("C05", "feature.C05", "interaction"),
                new("C06", "feature.C06", "interaction"),
                new("C07", "feature.C07", "interaction"),
                new("C08", "feature.C08", "interaction"),
                new("C09", "feature.C09", "interaction"),
                new("C10", "feature.C10", "interaction"),
                new("S01", "feature.S01", "interaction"),
                new("S02", "feature.S02", "interaction"),
                new("S03", "feature.S03", "interaction"),
                new("S05", "feature.S05", "interaction"),
                new("S06", "feature.S06", "interaction"),
                new("S10", "feature.S10", "interaction"),
            }),
        new("insights", "feature.module.insights.title", "feature.module.insights.desc", "IconGeomSparkles",
            "insights", "", CurrentVersion, false,
            new FeatureEntry[]
            {
                new("S07", "feature.S07", "insights"),
                new("S08", "feature.S08", "insights"),
                new("E06", "feature.E06", "insights"),
            }),
        new("stream", "feature.module.stream.title", "feature.module.stream.desc", "IconGeomVideo",
            "stream", "", CurrentVersion, false,
            new FeatureEntry[]
            {
                new("Q01", "feature.Q01", "stream"),
                new("Q02", "feature.Q02", "stream"),
                new("Q03", "feature.Q03", "stream"),
                new("Q04", "feature.Q04", "stream"),
                new("Q05", "feature.Q05", "stream"),
                new("Q06", "feature.Q06", "stream"),
                new("Q07", "feature.Q07", "stream"),
                new("Q08", "feature.Q08", "stream"),
            }),
        new("automation", "feature.module.automation.title", "feature.module.automation.desc", "IconGeomRobot",
            "automation", "", CurrentVersion, false,
            new FeatureEntry[]
            {
                new("E01", "feature.E01", "automation"),
                new("E02", "feature.E02", "automation"),
                new("E03", "feature.E03", "automation"),
                new("E04", "feature.E04", "automation"),
                new("E05", "feature.E05", "automation"),
                new("S04", "feature.S04", "automation"),
                new("S09", "feature.S09", "automation"),
            }),

        // ------------------------------------------------------------ 常用功能（V3.3.0 及更早）
        new("recording", "feature.module.recording.title", "feature.module.recording.desc", "IconGeomVideo",
            "home", "", "常用", false,
            new FeatureEntry[]
            {
                new("R01", "feature.R01", "home"),
                new("R02", "feature.R02", "home"),
                new("R03", "feature.R03", "console"),
                new("R04", "feature.R04", "console"),
                new("R05", "feature.R05", "toolbox"),
                new("R06", "feature.R06", "toolbox"),
                new("R07", "feature.R07", "obsconfig"),
            }),
        new("virtual", "feature.module.virtual.title", "feature.module.virtual.desc", "IconGeomVirtualLive",
            "virtuallive", "avatar", "V3.3.0", false,
            new FeatureEntry[]
            {
                new("V01", "feature.V01", "virtuallive", "avatar"),
                new("V02", "feature.V02", "virtuallive", "room"),
                new("V03", "feature.V03", "virtuallive", "golive"),
                new("V04", "feature.V04", "virtuallive", "avatar"),
                new("V05", "feature.V05", "plugins"),
                new("V06", "feature.V06", "plugins"),
                new("V07", "feature.V07", "settings"),
            }),
        new("tools", "feature.module.tools.title", "feature.module.tools.desc", "IconGeomTool",
            "diagnostic", "", "常用", false,
            new FeatureEntry[]
            {
                new("D01", "feature.D01", "diagnostic"),
                new("D02", "feature.D02", "assistant"),
                new("D03", "feature.D03", "search"),
                new("D04", "feature.D04", "logs"),
                new("D05", "feature.D05", "setup"),
                new("D06", "feature.D06", "performance"),
                new("D07", "feature.D07", "templates"),
                new("D08", "feature.D08", "toolbox"),
                new("D09", "feature.D09", "obsconfig"),
                new("D10", "feature.D10", "guide"),
            }),
        new("deps", "feature.module.deps.title", "feature.module.deps.desc", "IconGeomPackage",
            "plugins", "", "V3.4.1", false,
            new FeatureEntry[]
            {
                new("P01", "feature.P01", "plugins"),
            }),
        new("system", "feature.module.system.title", "feature.module.system.desc", "IconGeomSettings",
            "settings", "", "常用", false,
            new FeatureEntry[]
            {
                new("SYS01", "feature.SYS01", "settings"),
                new("SYS02", "feature.SYS02", "settings"),
                new("SYS03", "feature.SYS03", "settings"),
                new("SYS04", "feature.SYS04", "performance"),
                new("SYS05", "feature.SYS05", "settings"),
                new("SYS06", "feature.SYS06", "home"),
            }),
    };

    /// <summary>V3.4.0 交付的 46 项功能（M/C/S/Q/E 编号）。</summary>
    public static int NewFeatureCount => Modules
        .Where(m => m.Version == CurrentVersion)
        .Sum(m => m.Features.Count);

    public static int FeatureCount => Modules.Sum(m => m.Features.Count);

    public static FeatureModule? Find(string id)
        => Modules.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
}
