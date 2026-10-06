using OBS_Helper.Wpf.Localization;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// 语言注册表（V3.0 / D9）的守护测试。
///
/// 目的很具体：**让「加一门语言」真的只是数据活**。在注册表出现之前，语言在两处硬编码
/// （<c>Strings.Table</c> 的三元判断、<c>ContentAssets.IsEnglish</c>），加语言要改代码且容易漏。
/// 这里把「注册一门语言必须满足什么」钉死：
/// <list type="number">
///   <item>至少两种语言、第一项是默认语言、标识唯一、展示名非空；</item>
///   <item><b>所有语言的键集完全一致</b> —— 半成品翻译（少几十个键）会整片回退成中文，必须拦住；</item>
///   <item>别名能正确归一化；认不出的输入回退默认语言；</item>
///   <item>内容资产后缀来自注册表（而不是「是不是英文」）。</item>
/// </list>
/// </summary>
public class LanguageRegistryTests
{
    [Fact]
    public void Registry_HasAtLeastTwoLanguages_WithDefaultFirst()
    {
        Assert.True(LanguageRegistry.All.Count >= 2);
        Assert.Equal(Strings.ZhHans, LanguageRegistry.Default.Code);   // 默认语言是简体中文
        Assert.Equal(Strings.ZhHans, LanguageRegistry.All[0].Code);
    }

    [Fact]
    public void Registry_CodesAreUniqueAndMatchSupportedList()
    {
        var codes = LanguageRegistry.All.Select(l => l.Code).ToList();
        Assert.Equal(codes.Count, codes.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(codes, Strings.Supported);
    }

    [Fact]
    public void Registry_EveryLanguageHasDisplayNameAndSuffix()
    {
        Assert.All(LanguageRegistry.All, l =>
        {
            Assert.False(string.IsNullOrWhiteSpace(l.DisplayName));
            Assert.NotNull(l.ContentSuffix);
            Assert.NotNull(l.Table);
        });

        // 默认语言没有内容后缀（内置资产就是默认语言）
        Assert.Equal("", LanguageRegistry.Default.ContentSuffix);
    }

    /// <summary>关键守护：任何一门语言的键集都必须与默认语言完全一致。</summary>
    [Fact]
    public void EveryLanguage_HasExactlyTheSameKeySetAsDefault()
    {
        var expected = LanguageRegistry.Default.Table.Keys.ToHashSet(StringComparer.Ordinal);

        foreach (var lang in LanguageRegistry.All.Skip(1))
        {
            var actual = lang.Table.Keys.ToHashSet(StringComparer.Ordinal);
            var missing = expected.Except(actual).ToList();
            var extra = actual.Except(expected).ToList();

            Assert.True(missing.Count == 0, $"{lang.Code} 缺少 {missing.Count} 个键，例如：{string.Join(", ", missing.Take(5))}");
            Assert.True(extra.Count == 0, $"{lang.Code} 多出 {extra.Count} 个键，例如：{string.Join(", ", extra.Take(5))}");
        }
    }

    /// <summary>任何语言的任何键都不能是空值（空值等于「界面上一片空白」）。</summary>
    [Fact]
    public void EveryLanguage_HasNoEmptyValues()
    {
        foreach (var lang in LanguageRegistry.All)
        {
            var empty = lang.Table.Where(kv => string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key).Take(5).ToList();
            Assert.True(empty.Count == 0, $"{lang.Code} 有空文案：{string.Join(", ", empty)}");
        }
    }

    [Theory]
    [InlineData("zh-Hans", "zh-Hans")]
    [InlineData("zh", "zh-Hans")]
    [InlineData("zh-CN", "zh-Hans")]
    [InlineData("chinese", "zh-Hans")]
    [InlineData("en-US", "en-US")]
    [InlineData("en", "en-US")]
    [InlineData("en-GB", "en-US")]
    [InlineData("English", "en-US")]
    [InlineData("  en-us  ", "en-US")]
    public void Normalize_MapsAliasesToRegisteredCodes(string input, string expected)
        => Assert.Equal(expected, Strings.Normalize(input));

    [Theory]
    [InlineData("ja-JP")]
    [InlineData("de")]
    [InlineData("klingon")]
    [InlineData("")]
    [InlineData(null)]
    public void Normalize_UnknownLanguageFallsBackToDefault(string? input)
        => Assert.Equal(LanguageRegistry.Default.Code, Strings.Normalize(input));

    [Fact]
    public void Resolve_ReturnsRegisteredDefinitionOrDefault()
    {
        Assert.Equal("English", LanguageRegistry.Resolve("en").DisplayName);
        Assert.Equal(LanguageRegistry.Default.Code, LanguageRegistry.Resolve("nope").Code);
        Assert.Null(LanguageRegistry.Find("nope"));
        Assert.NotNull(LanguageRegistry.Find("en-US"));
    }

    /// <summary>内容资产后缀必须来自注册表：英文有后缀、默认语言没有。</summary>
    [Fact]
    public void ContentSuffix_ComesFromRegistry()
    {
        Assert.Equal("", LanguageRegistry.ContentSuffixOf("zh-Hans"));
        Assert.Equal(ContentAssets.EnglishSuffix, LanguageRegistry.ContentSuffixOf("en-US"));
        // 未注册的语言按默认语言处理（不加后缀）
        Assert.Equal("", LanguageRegistry.ContentSuffixOf("ja-JP"));

        Assert.True(ContentAssets.IsEnglish("en-GB"));
        Assert.False(ContentAssets.IsEnglish("zh-Hans"));
        Assert.False(ContentAssets.IsEnglish("ja-JP"));
    }

    /// <summary>
    /// 加一门语言的**模拟验证**：注册表驱动意味着只要给一门语言配好表与后缀，
    /// 归一化、表格选择、内容后缀三处都会自动跟着走（这正是本次重构要证明的事）。
    /// </summary>
    [Fact]
    public void Registry_DrivesTableSelectionAndSuffixTogether()
    {
        foreach (var lang in LanguageRegistry.All)
        {
            // 用自己的标识、别名、展示名查询，都必须回到同一个定义
            Assert.Same(lang, LanguageRegistry.Resolve(lang.Code));
            Assert.Same(lang, LanguageRegistry.Find(lang.Aliases[0]));
            Assert.Equal(lang.Code, Strings.Normalize(lang.Aliases[0]));
            Assert.Equal(lang.Table, Strings.Table(lang.Code));
            Assert.Equal(lang.ContentSuffix, LanguageRegistry.ContentSuffixOf(lang.Code));
        }
    }
}
