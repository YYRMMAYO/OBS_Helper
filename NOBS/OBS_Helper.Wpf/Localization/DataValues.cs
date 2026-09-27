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
}
