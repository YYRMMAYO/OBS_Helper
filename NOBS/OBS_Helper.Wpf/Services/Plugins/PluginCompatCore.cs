namespace OBS_Helper.Wpf.Services.Plugins;

/// <summary>插件与本机 OBS 版本的兼容性结论。</summary>
public enum PluginCompatStatus
{
    /// <summary>目录未声明兼容性（旧目录 / 外部热更新文件缺字段）——不做任何提示。</summary>
    Unknown,
    /// <summary>声明与本机版本匹配。</summary>
    Compatible,
    /// <summary>本机 OBS 版本偏低（插件要求更高版本）。</summary>
    TooOld,
    /// <summary>本机 OBS 版本偏高（插件尚未跟上大版本）。</summary>
    TooNew,
    /// <summary>目录**明确**声明与本机这个版本不兼容。</summary>
    Broken
}

/// <summary>
/// 插件 × OBS 版本兼容性核心（V3.0 / D9）。纯 BCL、零 IO，单测工程直接链接编译。
///
/// 背景（提案原文）：OBS 每次大版本都会产生一批「插件悄悄失效」的需求 —— 插件装上、
/// OBS 升级、然后捕获源没了/面板不显示，而用户完全不知道原因。此前只能靠问题库条目事后接。
/// 现在插件目录多一维声明（<c>obsCompat</c>），界面在**装之前**就能提示。
///
/// 声明语法（刻意做得小而易读，写错不如不写）：
/// <list type="bullet">
///   <item><c>30+</c>：30 及以上；</item>
///   <item><c>28-32</c>：28 到 32（含两端）；</item>
///   <item><c>!33</c>：明确**不**兼容主版本 33；</item>
///   <item>逗号组合（全部条件都要满足）：<c>30+,!33</c>。</item>
/// </list>
/// 任何解析不出来的声明一律判为 <see cref="PluginCompatStatus.Unknown"/> ——
/// 宁可什么都不说，也不要给用户一个假的「不兼容」警告。
/// </summary>
public static class PluginCompatCore
{
    /// <summary>
    /// 解析 OBS 版本号（如 <c>31.0.2</c> / <c>30.1.2-rc1</c> / <c>31</c>）。
    /// 解析不出来返回 null。
    /// </summary>
    public static (int Major, int Minor)? ParseObsVersion(string? obsVersion)
    {
        if (string.IsNullOrWhiteSpace(obsVersion)) return null;

        // 取第一段连续的数字与点（"30.1.2-rc1" → "30.1.2"，"31" → "31"）
        var start = -1;
        var end = -1;
        for (var i = 0; i < obsVersion.Length; i++)
        {
            var c = obsVersion[i];
            if (char.IsDigit(c) || c == '.')
            {
                if (start < 0) start = i;
                end = i;
            }
            else if (start >= 0)
            {
                break;
            }
        }
        if (start < 0) return null;

        var numeric = obsVersion.Substring(start, end - start + 1).Trim('.');
        var parts = numeric.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        if (!int.TryParse(parts[0], out var major)) return null;

        var minor = 0;
        if (parts.Length > 1 && !int.TryParse(parts[1], out minor)) minor = 0;
        return (major, minor);
    }

    /// <summary>
    /// 判定某个声明与本机 OBS 版本的兼容性。
    ///
    /// 本机版本解析不出来、或声明为空 / 语法不认识时，一律返回
    /// <see cref="PluginCompatStatus.Unknown"/>（界面据此不提示）。
    /// </summary>
    public static PluginCompatStatus Evaluate(string? obsCompat, string? localObsVersion)
    {
        if (string.IsNullOrWhiteSpace(obsCompat)) return PluginCompatStatus.Unknown;
        if (ParseObsVersion(localObsVersion) is not { } local) return PluginCompatStatus.Unknown;

        // 结果优先级：明确不兼容 > 版本偏高 > 版本偏低 > 兼容
        var sawAny = false;
        var tooOld = false;
        var tooNew = false;

        foreach (var raw in obsCompat.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var token = raw.Trim();
            if (token.Length == 0) continue;

            if (token.StartsWith('!'))
            {
                if (!TryParseMajor(token[1..], out var excluded)) return PluginCompatStatus.Unknown;
                sawAny = true;
                if (local.Major == excluded) return PluginCompatStatus.Broken;
                continue;
            }

            if (token.EndsWith('+'))
            {
                if (!TryParseMajor(token[..^1], out var min)) return PluginCompatStatus.Unknown;
                sawAny = true;
                if (local.Major < min) tooOld = true;
                continue;
            }

            var dash = token.IndexOf('-');
            if (dash > 0)
            {
                if (!TryParseMajor(token[..dash], out var lo) || !TryParseMajor(token[(dash + 1)..], out var hi))
                    return PluginCompatStatus.Unknown;
                sawAny = true;
                if (local.Major < lo) tooOld = true;
                else if (local.Major > hi) tooNew = true;
                continue;
            }

            // 裸主版本号："31" 视作 "31"（只匹配该主版本）
            if (TryParseMajor(token, out var exact))
            {
                sawAny = true;
                if (local.Major < exact) tooOld = true;
                else if (local.Major > exact) tooNew = true;
                continue;
            }

            // 有一个 token 读不懂：整条声明作废（不猜）
            return PluginCompatStatus.Unknown;
        }

        if (!sawAny) return PluginCompatStatus.Unknown;
        if (tooOld) return PluginCompatStatus.TooOld;
        if (tooNew) return PluginCompatStatus.TooNew;
        return PluginCompatStatus.Compatible;
    }

    private static bool TryParseMajor(string s, out int major)
        => int.TryParse(s.Trim().TrimStart('v', 'V'), out major) && major is > 0 and < 1000;

    /// <summary>是否需要向用户**提示**（未声明与匹配都不提示，避免给正常条目刷屏）。</summary>
    public static bool NeedsWarning(PluginCompatStatus status)
        => status is PluginCompatStatus.TooOld or PluginCompatStatus.TooNew or PluginCompatStatus.Broken;

    /// <summary>声明文案（直接展示给用户看插件要求什么版本）。</summary>
    public static string DescribeRequirement(string? obsCompat)
        => string.IsNullOrWhiteSpace(obsCompat) ? "" : obsCompat.Trim();
}
