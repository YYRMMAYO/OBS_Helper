using OBS_Helper.Wpf.Models;
using OBS_Helper.Wpf.Services.Search;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// 检索核心（V3.0 / C4）的回归测试。
///
/// 四条改法各自都有对应断言：同义词（落帧→掉帧）、拼音首字母（hp→黑屏）、
/// 字段加权（标题命中必须压过步骤命中）、无结果建议（不许返回空白）。
///
/// 以及一条**边界**断言：内置拼音表是「常见字表」而非完整拼音库 ——
/// 未收录的字必须只是跳过（不猜、不报错），子串匹配始终照常工作。
/// </summary>
public class SearchQueryCoreTests
{
    private static SearchableProblem P(
        string title,
        string[]? symptoms = null,
        string[]? steps = null,
        string[]? synonyms = null,
        string category = "画面")
        => new(new Problem { Id = title, Title = title },
            Title: title,
            Category: category,
            Symptoms: symptoms ?? Array.Empty<string>(),
            Causes: Array.Empty<string>(),
            Steps: steps ?? Array.Empty<string>(),
            Tips: Array.Empty<string>(),
            Synonyms: synonyms ?? Array.Empty<string>());

    // ---------------------------------------------------------------- 分词

    [Fact]
    public void Tokenize_SplitsOnPunctuationAndAddsCjkBigrams()
    {
        var tokens = SearchQueryCore.Tokenize("黑屏，没有画面");

        Assert.Contains("黑屏", tokens);
        Assert.Contains("没有画面", tokens);
        Assert.Contains("没有", tokens);      // 二元组
        Assert.Contains("画面", tokens);
    }

    [Fact]
    public void Tokenize_EmptyQueryYieldsNothing()
        => Assert.Empty(SearchQueryCore.Tokenize("   "));

    [Theory]
    [InlineData("hp", true)]
    [InlineData("abc123", true)]
    [InlineData("黑屏", false)]
    [InlineData("hp黑屏", false)]
    public void IsAsciiQuery_OnlyLettersAndDigits(string q, bool expected)
        => Assert.Equal(expected, SearchQueryCore.IsAsciiQuery(q));

    // ---------------------------------------------------------------- 拼音首字母

    [Theory]
    [InlineData("黑屏", "hp")]
    [InlineData("掉帧严重", "dzyz")]
    [InlineData("蓝屏", "lp")]
    [InlineData("卡顿", "kd")]
    [InlineData("麦克风", "mkf")]
    [InlineData("摄像头", "sxt")]
    [InlineData("采样率", "cyl")]
    [InlineData("编码器", "bmq")]
    [InlineData("分辨率", "fbl")]
    public void PinyinInitials_CoversCommonVocabulary(string text, string expected)
        => Assert.Equal(expected, SearchQueryCore.PinyinInitials(text));

    /// <summary>ASCII 原样保留、未收录的字跳过（不猜）—— 这是「常见字表」的诚实边界。</summary>
    [Fact]
    public void PinyinInitials_KeepsAsciiAndSkipsUnknownCharacters()
    {
        Assert.Equal("obs", SearchQueryCore.PinyinInitials("OBS"));
        // 「龘」不在表里 → 不产生任何字符，也不抛异常
        Assert.Equal("hp", SearchQueryCore.PinyinInitials("黑龘屏"));
        Assert.Equal("", SearchQueryCore.PinyinInitials("龘"));
    }

    // ---------------------------------------------------------------- 同义词

    [Fact]
    public void ExpandWithSynonyms_MapsColloquialToWrittenWord()
    {
        var expanded = SearchQueryCore.ExpandWithSynonyms(new[] { "落帧" });

        Assert.Contains("落帧", expanded);
        Assert.Contains("掉帧", expanded);
        Assert.Contains("丢帧", expanded);
    }

    [Fact]
    public void ExpandWithSynonyms_ExpandsFromSubstringOfLongerToken()
    {
        // 用户打的是「严重落帧」，也要能扩到 掉帧/丢帧
        var expanded = SearchQueryCore.ExpandWithSynonyms(SearchQueryCore.Tokenize("严重落帧"));
        Assert.Contains("掉帧", expanded);
    }

    [Fact]
    public void ExpandWithSynonyms_IncludesDataProvidedAliases()
    {
        var expanded = SearchQueryCore.ExpandWithSynonyms(new[] { "模糊" }, new[] { "不清楚", "糊" });
        Assert.Contains("不清楚", expanded);
        Assert.Contains("糊", expanded);
    }

    // ---------------------------------------------------------------- 打分

    [Fact]
    public void Score_SynonymQueryFindsWrittenWordProblem()
    {
        var problem = P("录像掉帧", symptoms: new[] { "画面不流畅" });
        var hit = SearchQueryCore.Score(problem, "落帧");

        Assert.NotNull(hit);
        Assert.True(hit!.Score > 0);
    }

    [Fact]
    public void Score_PinyinQueryFindsChineseTitle()
    {
        var problem = P("黑屏怎么办");
        var hit = SearchQueryCore.Score(problem, "hp");

        Assert.NotNull(hit);
        Assert.Contains("拼音首字母", hit!.Reason);
    }

    /// <summary>字段加权：标题命中必须明显高于步骤命中（用户要的是「这条讲的就是这件事」）。</summary>
    [Fact]
    public void Score_TitleOutranksSteps()
    {
        var inTitle = SearchQueryCore.Score(P("黑屏", steps: new[] { "检查线材" }), "黑屏")!;
        var inStep = SearchQueryCore.Score(P("画面异常", steps: new[] { "如果是黑屏，先换线" }), "黑屏")!;

        Assert.True(inTitle.Score > inStep.Score,
            $"标题命中 {inTitle.Score} 应高于步骤命中 {inStep.Score}");
    }

    [Fact]
    public void Score_NoMatchReturnsNull()
    {
        Assert.Null(SearchQueryCore.Score(P("黑屏"), "麦克风"));
        Assert.Null(SearchQueryCore.Score(P("黑屏"), "   "));
    }

    /// <summary>带空格的多词查询：过去整串 Contains 必然失败，现在按词分别命中即可。</summary>
    [Fact]
    public void Score_MultiWordQueryMatchesAcrossFields()
    {
        var problem = P("推流卡顿", symptoms: new[] { "编码器过载" });
        var hit = SearchQueryCore.Score(problem, "推流 编码器");

        Assert.NotNull(hit);
        Assert.True(hit!.Score > 0);
    }

    [Fact]
    public void Rank_OrdersByScoreAndIsStable()
    {
        var items = new[]
        {
            P("画面问题", steps: new[] { "黑屏时检查线材" }),
            P("黑屏", symptoms: new[] { "完全没画面" }),
            P("黑屏且花屏", synonyms: new[] { "没有画面" }),
        };

        var ranked = SearchQueryCore.Rank(items, "黑屏");
        Assert.Equal(items[1].Title, ranked[0].Problem.Title);
        // 同分时按标题稳定排序：两次调用结果必须一致
        Assert.Equal(ranked.Select(h => h.Problem.Title), SearchQueryCore.Rank(items, "黑屏").Select(h => h.Problem.Title));
    }

    // ---------------------------------------------------------------- 无结果建议

    [Fact]
    public void Suggest_ReturnsClosestEntriesInsteadOfNothing()
    {
        var items = new[]
        {
            P("黑屏怎么办", category: "画面"),
            P("麦克风没声音", category: "音频"),
            P("推流卡顿", category: "网络"),
        };

        var suggestions = SearchQueryCore.Suggest(items, "黑屏闪一下");
        Assert.NotEmpty(suggestions);
        Assert.Equal("黑屏怎么办", suggestions[0].Problem.Title);
        Assert.Contains("建议", suggestions[0].Reason);
    }

    [Fact]
    public void Suggest_RespectsMaxCount()
    {
        var items = Enumerable.Range(0, 10).Select(i => P($"黑屏 {i}")).ToArray();
        Assert.Equal(3, SearchQueryCore.Suggest(items, "黑屏", max: 3).Count);
    }

    [Fact]
    public void Suggest_EmptyQueryReturnsNothing()
        => Assert.Empty(SearchQueryCore.Suggest(new[] { P("黑屏") }, "  "));

    // ---------------------------------------------------------------- 常量口径

    [Fact]
    public void Weights_AreOrderedTitleToTips()
    {
        Assert.True(SearchQueryCore.TitleWeight > SearchQueryCore.SymptomWeight);
        Assert.True(SearchQueryCore.SymptomWeight > SearchQueryCore.CauseWeight);
        Assert.True(SearchQueryCore.CauseWeight > SearchQueryCore.StepWeight);
        Assert.True(SearchQueryCore.StepWeight > SearchQueryCore.TipWeight);
    }
}
