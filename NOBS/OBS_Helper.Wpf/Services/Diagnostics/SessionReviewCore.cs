using System.Text;
using OBS_Helper.Wpf.Localization;

namespace OBS_Helper.Wpf.Services.Diagnostics;

/// <summary>一次会话中某一类问题的命中累计（V3.0 / D5）。</summary>
public sealed record SessionHit(
    string Code,
    string Title,
    string Suggestion,
    int Count,
    DateTime FirstLocal,
    DateTime LastLocal);

/// <summary>上次会话的摘要（用于「这场比上场差在哪」）。</summary>
public sealed record SessionSnapshot(DateTime StartedLocal, int TotalHits, int KindCount, string TopCode, int TopCount);

/// <summary>会话复盘报告。</summary>
public sealed record SessionReview(
    DateTime StartedLocal,
    DateTime EndedLocal,
    IReadOnlyList<SessionHit> Hits,
    /// <summary>本次分析出的日志问题条数（来自日志页的分析结果，可为 0）。</summary>
    int AnalyzedProblemCount,
    /// <summary>上一次会话的摘要；没有历史时为 null。</summary>
    SessionSnapshot? Previous)
{
    public int TotalHits => Hits.Sum(h => h.Count);
    public int KindCount => Hits.Count;

    /// <summary>最突出的一类问题（次数最多）；无命中时为 null。</summary>
    public SessionHit? Top => Hits.Count > 0 ? Hits[0] : null;

    /// <summary>与上次相比的命中次数差（正数 = 比上次多）。没有历史时为 null。</summary>
    public int? DeltaVsPrevious => Previous is { } prev ? TotalHits - prev.TotalHits : null;
}

/// <summary>
/// 会话复盘核心（V3.0 / D5）。纯逻辑、零 IO，单测工程直接链接编译。
///
/// 回答三个原本答不出来的问题：
/// <list type="number">
///   <item>今晚一共被提醒了哪些问题、各多少次（<see cref="SessionReview.Hits"/>）；</item>
///   <item>这场比上场差在哪（<see cref="SessionReview.DeltaVsPrevious"/>）；</item>
///   <item>该怎么改（沿用规则自带的建议，不另起一套话术）。</item>
/// </list>
/// </summary>
public static class SessionReviewCore
{
    /// <summary>报告里最多列出的问题类别（按次数降序）。</summary>
    public const int MaxListedKinds = 10;

    /// <summary>构造复盘报告。命中为空时也返回一份合法报告（如实说「一条都没命中」）。</summary>
    public static SessionReview Build(
        DateTime startedLocal,
        DateTime endedLocal,
        IEnumerable<SessionHit>? hits,
        int analyzedProblemCount = 0,
        SessionSnapshot? previous = null)
    {
        var ordered = (hits ?? Enumerable.Empty<SessionHit>())
            .Where(h => h.Count > 0)
            .OrderByDescending(h => h.Count)
            .ThenBy(h => h.Code, StringComparer.Ordinal)
            .ToList();

        return new SessionReview(
            StartedLocal: startedLocal,
            EndedLocal: endedLocal < startedLocal ? startedLocal : endedLocal,
            Hits: ordered,
            AnalyzedProblemCount: Math.Max(0, analyzedProblemCount),
            Previous: previous);
    }

    /// <summary>把报告压成摘要，供下次会话比较。</summary>
    public static SessionSnapshot ToSnapshot(SessionReview review)
        => new(
            StartedLocal: review.StartedLocal,
            TotalHits: review.TotalHits,
            KindCount: review.KindCount,
            TopCode: review.Top?.Code ?? "",
            TopCount: review.Top?.Count ?? 0);

    /// <summary>一句话总结（放在报告顶部，用户先看这句）。</summary>
    public static string BuildSummary(SessionReview review)
    {
        if (review.KindCount == 0)
            return Strings.T("d5.summary.clean");

        var head = Strings.T("d5.summary.hits", review.KindCount, review.TotalHits);

        if (review.DeltaVsPrevious is { } delta)
        {
            head += " " + (delta > 0
                ? Strings.T("d5.summary.worse", delta)
                : delta < 0
                    ? Strings.T("d5.summary.better", -delta)
                    : Strings.T("d5.summary.same"));
        }

        return head;
    }

    /// <summary>
    /// 生成可导出的 Markdown 复盘。
    ///
    /// 复用 <c>diagnostic.report.*</c> 的标题风格（同一套报告在用户眼里应该长得一样），
    /// 但内容只讲本次会话 —— 不把「日志分析发现的问题清单」重复一遍，
    /// 那是日志页已经给出的东西（避免同一件事在导出里出现两次）。
    /// </summary>
    public static string BuildMarkdown(SessionReview review)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Strings.T("d5.report.title"));
        sb.AppendLine();
        sb.AppendLine(Strings.T("d5.report.period",
            review.StartedLocal.ToString("yyyy-MM-dd HH:mm"),
            review.EndedLocal.ToString("HH:mm")));
        sb.AppendLine(Strings.T("d5.report.summary", BuildSummary(review)));
        if (review.AnalyzedProblemCount > 0)
            sb.AppendLine(Strings.T("d5.report.analyzed", review.AnalyzedProblemCount));

        if (review.Previous is { } prev)
        {
            sb.AppendLine(Strings.T("d5.report.previous",
                prev.StartedLocal.ToString("yyyy-MM-dd HH:mm"), prev.TotalHits));
        }

        sb.AppendLine();
        if (review.KindCount == 0)
        {
            sb.AppendLine(Strings.T("d5.report.noHits"));
        }
        else
        {
            sb.AppendLine(Strings.T("d5.report.hitsHeading"));
            foreach (var hit in review.Hits.Take(MaxListedKinds))
            {
                sb.AppendLine(Strings.T("d5.report.hitItem", hit.Code, hit.Title, hit.Count,
                    hit.LastLocal.ToString("HH:mm:ss")));
                if (!string.IsNullOrWhiteSpace(hit.Suggestion))
                    sb.AppendLine("  - " + hit.Suggestion.Replace("\n", "\n    "));
            }

            if (review.Hits.Count > MaxListedKinds)
                sb.AppendLine(Strings.T("d5.report.moreHits", review.Hits.Count - MaxListedKinds));
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine(Strings.T("d5.report.footer"));
        return sb.ToString();
    }

    /// <summary>导出文件名（与诊断报告同一命名风格）。</summary>
    public static string BuildFileName(DateTime endedLocal)
        => Strings.T("d5.report.fileName", endedLocal.ToString("yyyyMMdd_HHmm"));

    /// <summary>把「比上次多/少」的差值压成人话（正数 = 变差）。</summary>
    public static string DescribeDelta(int? delta) => delta switch
    {
        null => Strings.T("d5.delta.noBaseline"),
        > 0 => Strings.T("d5.delta.worse", delta.Value),
        < 0 => Strings.T("d5.delta.better", -delta.Value),
        _ => Strings.T("d5.delta.same")
    };
}
