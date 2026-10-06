namespace OBS_Helper.Wpf.Localization;

/// <summary>
/// 数据驱动展示值的判定（V2.9.2）。
///
/// 知识库 / 外部热更新数据里的 <c>severity</c>、<c>level</c> 这类字段是**展示文案**，
/// 中英各有一套取值（<c>常见</c> / <c>Common</c>）。它们同时被逻辑用来选颜色、决定排版，
/// 因此判定必须**同时认两种语言** —— 用户切换语言后，本地已下载的外部知识库文件
/// 可能是另一种语言写的，只认中文会让颜色与排版突然塌掉。
///
/// 这里集中放这类「跨语言语义判定」，避免把中文常量散落到各个分支里。
/// </summary>
public static class DataValues
{
    /// <summary>严重度的语义分类（与展示文案解耦）。</summary>
    public enum SeverityKind
    {
        /// <summary>没有对应语义（数据里的未知值，或缺省）。</summary>
        Other,
        /// <summary>严重 / Critical。</summary>
        Critical,
        /// <summary>常见 / Common。</summary>
        Common,
        /// <summary>一般 / Occasional。</summary>
        Normal,
        /// <summary>进阶 / Advanced。</summary>
        Advanced
    }

    private static bool Matches(string? value, string zhHans, string enUs)
    {
        var v = (value ?? "").Trim();
        if (v.Length == 0) return false;
        return string.Equals(v, zhHans, StringComparison.OrdinalIgnoreCase)
            || string.Equals(v, enUs, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>把知识库条目的严重度文案归到语义分类（中英取值都认）。</summary>
    public static SeverityKind ClassifySeverity(string? severity)
    {
        if (Matches(severity, "严重", "Critical")) return SeverityKind.Critical;
        if (Matches(severity, "常见", "Common")) return SeverityKind.Common;
        if (Matches(severity, "一般", "Occasional")) return SeverityKind.Normal;
        if (Matches(severity, "进阶", "Advanced")) return SeverityKind.Advanced;
        return SeverityKind.Other;
    }

    /// <summary>步骤难度是否为「进阶」（问题详情页用它决定步骤卡的强调样式）。</summary>
    public static bool IsAdvancedLevel(string? level) => Matches(level, "进阶", "Advanced");

    /// <summary>插件卡片的角标是否为「热门」（数据里的 badge 文案，中英各一套）。</summary>
    public static bool IsHotBadge(string? badge) => Matches(badge, "热门", "Popular");

    /// <summary>
    /// 把 OBS 的滤镜**种类 id** 翻成界面文案（V3.0 / D7）。
    ///
    /// OBS 返回的是 <c>noise_suppress_filter</c> 这类内部 id，直接显示给用户没有意义；
    /// 用「包含关键词」而不是精确匹配，是因为同一类滤镜在 v4 / v5 / 不同 OBS 版本里
    /// 出现过 <c>chroma_key_filter</c> / <c>chroma_key_filter_v2</c> 这类后缀差异 ——
    /// 精确匹配会让新版本一升级就全部退回显示 id。认不出来时原样显示，至少信息没丢。
    /// </summary>
    public static string FilterKindLabel(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind)) return "";
        var k = kind.Trim().ToLowerInvariant();

        if (k.Contains("noise_suppress") || k.Contains("noise_suppression")) return Strings.T("filterkind.noise");
        if (k.Contains("noise_gate")) return Strings.T("filterkind.noiseGate");
        if (k.Contains("chroma_key")) return Strings.T("filterkind.chromaKey");
        if (k.Contains("color_key")) return Strings.T("filterkind.colorKey");
        if (k.Contains("luma_key")) return Strings.T("filterkind.lumaKey");
        if (k.Contains("color_correction") || k.Contains("color_filter")) return Strings.T("filterkind.colorCorrection");
        if (k.Contains("sharpen")) return Strings.T("filterkind.sharpen");
        if (k.Contains("scale")) return Strings.T("filterkind.scale");
        if (k.Contains("crop")) return Strings.T("filterkind.crop");
        if (k.Contains("scroll")) return Strings.T("filterkind.scroll");
        if (k.Contains("async_delay") || k.Contains("delay")) return Strings.T("filterkind.delay");
        if (k.Contains("compressor")) return Strings.T("filterkind.compressor");
        if (k.Contains("limiter")) return Strings.T("filterkind.limiter");
        if (k.Contains("expander")) return Strings.T("filterkind.expander");
        if (k.Contains("gain")) return Strings.T("filterkind.gain");
        if (k.Contains("vst")) return Strings.T("filterkind.vst");
        if (k.Contains("eq")) return Strings.T("filterkind.eq");

        return kind.Trim();   // 未知种类：原样显示，别把信息藏起来
    }
}
