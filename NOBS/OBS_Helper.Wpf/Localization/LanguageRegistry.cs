namespace OBS_Helper.Wpf.Localization;

/// <summary>
/// 一种语言的完整定义（V3.0 / D9）：标识、展示名、内容资产后缀、文案表与归一化别名。
///
/// 为什么要有这个类型：在它之前，语言是**两处硬编码**的 ——
/// <c>Strings.Table(language) = language == EnUs ? 英文表 : 中文表</c>，
/// 以及 <c>ContentAssets.IsEnglish(language)</c>。于是「加第三种语言」并不是纯数据成本：
/// 还得改这两处判断，而且任何一处漏改都会静默走错分支（例如繁体用户拿到简体文案）。
/// 现在加一门语言 = **加一个表类 + 在 <see cref="LanguageRegistry.All"/> 里加一行 + 并列一份内容资产**。
/// </summary>
public sealed record LanguageDefinition(
    /// <summary>语言标识（BCP-47 风格，如 <c>zh-Hans</c> / <c>en-US</c> / <c>zh-Hant</c> / <c>ja-JP</c>）。</summary>
    string Code,
    /// <summary>展示名（各语言用各自的写法：简体中文 / English / 繁體中文 / 日本語）。</summary>
    string DisplayName,
    /// <summary>内容资产文件名后缀（默认语言为空串；英文为 <c>.en-US</c>）。</summary>
    string ContentSuffix,
    /// <summary>该语言的文案表。</summary>
    IReadOnlyDictionary<string, string> Table,
    /// <summary>
    /// 归一化别名（大小写不敏感）。用于把系统区域 / 用户输入映射到本语言：
    /// 例如 <c>zh</c> / <c>zh-CN</c> / <c>chinese</c> → <c>zh-Hans</c>。
    /// </summary>
    IReadOnlyList<string> Aliases);

/// <summary>
/// 语言注册表（V3.0 / D9）。纯 BCL、零 WPF 依赖，单测工程直接链接编译。
///
/// 加语言的完整清单（这里就是「唯一真源」）：
/// <list type="number">
///   <item>新建 <c>StringTableXxx.cs</c>（键集必须与既有表**完全一致**，有单测守护）；</item>
///   <item>在 <see cref="All"/> 里加一行（顺序即界面展示顺序，第一项是默认语言）；</item>
///   <item>并列一份内容资产（<c>problems.&lt;suffix&gt;.json</c> 等），后缀填进 <see cref="LanguageDefinition.ContentSuffix"/>。</item>
/// </list>
/// 顺序有意如此：**先有表再注册**，避免注册了一门语言却没有文案（界面会整片回退成中文）。
/// </summary>
public static class LanguageRegistry
{
    /// <summary>全部语言；顺序即界面展示顺序，<b>第一项是默认语言</b>。</summary>
    public static readonly IReadOnlyList<LanguageDefinition> All = new[]
    {
        new LanguageDefinition(
            Code: Strings.ZhHans,
            DisplayName: "简体中文",
            ContentSuffix: "",
            Table: StringTableZhHans.Table,
            Aliases: new[] { "zh-Hans", "zh", "zh-CN", "zh-SG", "chinese", "chinesesimplified", "简体中文" }),

        new LanguageDefinition(
            Code: Strings.EnUs,
            DisplayName: "English",
            ContentSuffix: ContentAssets.EnglishSuffix,
            Table: StringTableEnUs.Table,
            Aliases: new[] { "en-US", "en", "en-GB", "en-AU", "english", "英语", "英文" }),
    };

    /// <summary>默认语言（注册表第一项）。</summary>
    public static LanguageDefinition Default => All[0];

    /// <summary>全部语言标识（顺序同 <see cref="All"/>）。</summary>
    public static IReadOnlyList<string> Codes => All.Select(l => l.Code).ToList();

    /// <summary>按标识或别名查找；找不到返回 null。</summary>
    public static LanguageDefinition? Find(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return null;
        var s = language.Trim();

        foreach (var lang in All)
        {
            if (string.Equals(lang.Code, s, StringComparison.OrdinalIgnoreCase)) return lang;
        }

        // 别名：先精确匹配，再按主语言标签匹配（容忍 en-GB / zh-HK 这类变体）
        foreach (var lang in All)
        {
            if (lang.Aliases.Any(a => string.Equals(a, s, StringComparison.OrdinalIgnoreCase))) return lang;
        }

        var primary = s.Split('-', '_', ' ', '.')[0];
        foreach (var lang in All)
        {
            if (lang.Aliases.Any(a => string.Equals(a.Split('-', '_')[0], primary, StringComparison.OrdinalIgnoreCase)))
                return lang;
        }

        return null;
    }

    /// <summary>解析为受支持的语言；无法识别时回退默认语言（简体中文）。</summary>
    public static LanguageDefinition Resolve(string? language) => Find(language) ?? Default;

    /// <summary>按 <see cref="LanguageDefinition.ContentSuffix"/> 取内容资产后缀。</summary>
    public static string ContentSuffixOf(string? language) => Resolve(language).ContentSuffix;
}
