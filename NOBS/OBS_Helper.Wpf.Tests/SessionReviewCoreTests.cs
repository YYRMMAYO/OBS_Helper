using OBS_Helper.Wpf.Services.Diagnostics;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// 会话复盘（V3.0 / D5）的报告生成测试。
///
/// 关键点：命中计数**不受预警节流影响**（节流是「别打扰用户」，复盘要的是真实频次），
/// 报告必须在「一条都没命中」时也如实可导出，并且跨会话比较要能回答「这场比上场差在哪」。
/// </summary>
public class SessionReviewCoreTests
{
    private static SessionHit Hit(string code, string title, int count, string suggestion = "建议这样改")
        => new(code, title, suggestion, count,
            new DateTime(2026, 10, 5, 21, 0, 0),
            new DateTime(2026, 10, 5, 21, 30, 0));

    private static readonly DateTime Start = new(2026, 10, 5, 20, 0, 0);
    private static readonly DateTime End = new(2026, 10, 5, 23, 0, 0);

    [Fact]
    public void Build_NoHits_IsValidAndSaysSo()
    {
        var review = SessionReviewCore.Build(Start, End, null);

        Assert.Equal(0, review.TotalHits);
        Assert.Equal(0, review.KindCount);
        Assert.Null(review.Top);
        Assert.Null(review.DeltaVsPrevious);      // 没有历史 → 无基线
        Assert.Contains("没有命中", SessionReviewCore.BuildSummary(review));
        Assert.Contains("未命中", SessionReviewCore.BuildMarkdown(review));
    }

    /// <summary>按次数降序；次数相同按规则码稳定排序（否则同一次导出两次结果顺序会变）。</summary>
    [Fact]
    public void Build_OrdersByCountThenCode()
    {
        var review = SessionReviewCore.Build(Start, End, new[]
        {
            Hit("LOG-B", "B", 2),
            Hit("LOG-A", "A", 5),
            Hit("LOG-C", "C", 2),
        });

        Assert.Equal(new[] { "LOG-A", "LOG-B", "LOG-C" }, review.Hits.Select(h => h.Code));
        Assert.Equal("LOG-A", review.Top!.Code);
    }

    [Fact]
    public void Build_IgnoresZeroCountHits()
    {
        var review = SessionReviewCore.Build(Start, End, new[] { Hit("LOG-A", "A", 0) });
        Assert.Equal(0, review.KindCount);
    }

    /// <summary>结束时间早于开始时间（时钟回拨）时不能产出负数区间。</summary>
    [Fact]
    public void Build_ClampsEndBeforeStart()
    {
        var review = SessionReviewCore.Build(End, Start, null);
        Assert.Equal(End, review.EndedLocal);
    }

    // ---------------------------------------------------------------- 与上次比较

    [Fact]
    public void DeltaVsPrevious_ReportsMoreFewerAndSame()
    {
        var previous = new SessionSnapshot(Start.AddDays(-1), TotalHits: 10, KindCount: 2, TopCode: "LOG-A", TopCount: 6);

        var worse = SessionReviewCore.Build(Start, End, new[] { Hit("LOG-A", "A", 13) }, 0, previous);
        Assert.Equal(3, worse.DeltaVsPrevious);
        Assert.Contains("多 3 次", SessionReviewCore.BuildSummary(worse));

        var better = SessionReviewCore.Build(Start, End, new[] { Hit("LOG-A", "A", 4) }, 0, previous);
        Assert.Equal(-6, better.DeltaVsPrevious);
        Assert.Contains("少 6 次", SessionReviewCore.BuildSummary(better));

        var same = SessionReviewCore.Build(Start, End, new[] { Hit("LOG-A", "A", 10) }, 0, previous);
        Assert.Equal(0, same.DeltaVsPrevious);
        Assert.Contains("持平", SessionReviewCore.BuildSummary(same));
    }

    [Fact]
    public void DescribeDelta_CoversNoBaseline()
    {
        Assert.Contains("没有", SessionReviewCore.DescribeDelta(null));
        Assert.Contains("多", SessionReviewCore.DescribeDelta(2));
        Assert.Contains("少", SessionReviewCore.DescribeDelta(-2));
        Assert.Contains("持平", SessionReviewCore.DescribeDelta(0));
    }

    [Fact]
    public void ToSnapshot_KeepsWhatTheNextComparisonNeeds()
    {
        var review = SessionReviewCore.Build(Start, End, new[] { Hit("LOG-A", "A", 7), Hit("LOG-B", "B", 3) });
        var snapshot = SessionReviewCore.ToSnapshot(review);

        Assert.Equal(Start, snapshot.StartedLocal);
        Assert.Equal(10, snapshot.TotalHits);
        Assert.Equal(2, snapshot.KindCount);
        Assert.Equal("LOG-A", snapshot.TopCode);
        Assert.Equal(7, snapshot.TopCount);
    }

    // ---------------------------------------------------------------- 导出

    [Fact]
    public void BuildMarkdown_ContainsPeriodSummaryAndEachHitWithSuggestion()
    {
        var review = SessionReviewCore.Build(Start, End, new[]
        {
            Hit("LOG-ENC-OVERLOAD", "编码器过载", 4, "把预设调快一档"),
            Hit("LOG-DROP-NET", "网络丢帧", 2, "检查上行带宽"),
        }, analyzedProblemCount: 3);

        var md = SessionReviewCore.BuildMarkdown(review);

        Assert.Contains("# 会话复盘", md);
        Assert.Contains("2026-10-05 20:00", md);
        Assert.Contains("23:00", md);
        Assert.Contains("编码器过载", md);
        Assert.Contains("4 次", md);
        Assert.Contains("把预设调快一档", md);
        Assert.Contains("网络丢帧", md);
        Assert.Contains("日志分析共发现 3 条问题", md);
        Assert.Contains("由 OBS帮助助手生成", md);
    }

    [Fact]
    public void BuildMarkdown_MentionsPreviousSessionWhenAvailable()
    {
        var previous = new SessionSnapshot(Start.AddDays(-1), 8, 2, "LOG-A", 5);
        var review = SessionReviewCore.Build(Start, End, new[] { Hit("LOG-A", "A", 3) }, 0, previous);

        Assert.Contains("上次会话", SessionReviewCore.BuildMarkdown(review));
    }

    /// <summary>命中类别很多时只列前 N 条，并如实说明还有多少没列。</summary>
    [Fact]
    public void BuildMarkdown_CapsListedKindsAndSaysHowManyRemain()
    {
        var hits = Enumerable.Range(0, SessionReviewCore.MaxListedKinds + 3)
            .Select(i => Hit($"LOG-{i:D2}", $"问题 {i}", 1))
            .ToList();

        var md = SessionReviewCore.BuildMarkdown(SessionReviewCore.Build(Start, End, hits));

        Assert.Contains("另有 3 类问题未列出", md);
    }

    [Fact]
    public void BuildFileName_UsesTimestampAndMarkdownExtension()
    {
        var name = SessionReviewCore.BuildFileName(new DateTime(2026, 10, 5, 23, 4, 0));
        Assert.EndsWith(".md", name);
        Assert.Contains("20261005_2304", name);
    }
}
