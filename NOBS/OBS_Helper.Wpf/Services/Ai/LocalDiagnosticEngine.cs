using System.Text;
using OBS_Helper.Wpf.Models;
using OBS_Helper.Wpf.Models.Obs;
using OBS_Helper.Wpf.Services.Obs;

namespace OBS_Helper.Wpf.Services.Ai;

/// <summary>
/// 本地离线诊断引擎（技术计划 §4.5「本地规则引擎」）。
///
/// 不联网、不依赖密钥，输入是「日志分析报告 + OBS 实时快照 + 用户问题」，
/// 输出与云端引擎完全一致的 <see cref="DiagnosticResult"/>。逻辑：
/// <list type="number">
///   <li>日志分析发现（最权威、已脱敏）：直接转成诊断项，并关联知识库拿分步方案；</li>
///   <li>用户提问驱动：用 <see cref="AssistantService"/> 在知识库里做关键词匹配；</li>
///   <li>已连接但无日志：从实时性能统计里推导渲染/编码/网络告警；</li>
///   <li>按严重程度排序，生成一段中文结论。</li>
/// </list>
/// </summary>
public sealed class LocalDiagnosticEngine
{
    private readonly ProblemService _problems;
    private readonly AssistantService _assistant;

    public LocalDiagnosticEngine(ProblemService problems, AssistantService assistant)
    {
        _problems = problems;
        _assistant = assistant;
    }

    public async Task<DiagnosticResult> DiagnoseAsync(DiagnosticContext ctx, string? query)
    {
        var result = new DiagnosticResult { Engine = "local" };
        var items = new List<DiagnosticItem>();

        // 1) 日志分析发现：最权威，且证据已脱敏
        if (ctx.Report is { HasIssues: true })
        {
            foreach (var f in ctx.Report.Findings)
            {
                var item = new DiagnosticItem
                {
                    ProblemId = f.ProblemId ?? "",
                    Title = f.Title,
                    Severity = DiagnosticSeverityMapper.Map(f.Severity),
                    Source = Strings.T("log.source.logAnalysis"),
                    Reason = f.Occurrences > 1 ? Strings.T("log.reason.occurrences", f.Occurrences) : Strings.T("log.reason.hit"),
                    Evidence = f.Evidence,
                    SuspectModule = f.SuspectModule ?? ""
                };
                if (!string.IsNullOrEmpty(f.ProblemId))
                {
                    var p = await _problems.GetByIdAsync(f.ProblemId);
                    if (p is not null) AttachSteps(item, p);
                }
                if (string.IsNullOrEmpty(item.Evidence)) item.Evidence = f.Suggestion;
                items.Add(item);
            }
        }

        // 2) 用户提问驱动的知识库匹配（去重：日志已覆盖的不再重复）
        if (!string.IsNullOrWhiteSpace(query))
        {
            var matches = await _assistant.AskAsync(query);
            foreach (var m in matches)
            {
                if (items.Any(i => i.ProblemId == m.Problem.Id)) continue;
                var item = new DiagnosticItem
                {
                    ProblemId = m.Problem.Id,
                    Title = m.Problem.Title,
                    Severity = DiagnosticSeverityMapper.Map(m.Problem.Severity),
                    Source = Strings.T("log.source.knowledgeBase"),
                    Reason = string.IsNullOrEmpty(m.Reason) ? Strings.T("log.reason.keyword") : Strings.T("log.reason.keywordWith", m.Reason)
                };
                AttachSteps(item, m.Problem);
                items.Add(item);
            }
        }

        // 3) 已连接但无日志：从实时性能统计推导告警
        if (ctx.Connection.IsConnected && ctx.Report is null)
        {
            AppendLiveWarnings(items, ctx.Connection);
        }

        items.Sort((a, b) => b.Severity.CompareTo(a.Severity));
        result.Items = items;
        result.Summary = BuildSummary(items, ctx, query);
        result.Success = true;
        return result;
    }

    private static void AttachSteps(DiagnosticItem item, Problem p)
    {
        foreach (var s in p.Steps) item.Steps.Add(s.Title);
    }

    private static void AppendLiveWarnings(List<DiagnosticItem> items, ObsConnectionService c)
    {
        if (c.Stats.RenderSkipRatio > 0.01)
            items.Add(WarnItem("lag-skip", Strings.T("log.live.renderTitle"), Strings.T("log.live.renderReason"),
                Strings.T("log.live.renderEvidence")));
        if (c.Stats.OutputSkipRatio > 0.01)
            items.Add(WarnItem("enc-overload", Strings.T("log.live.encodeTitle"), Strings.T("log.live.encodeReason"),
                Strings.T("log.live.encodeEvidence")));
        if (c.StreamStatus.Active && c.StreamStatus.DroppedRatio > 0.01)
            items.Add(WarnItem("lag-network", Strings.T("log.live.dropTitle"), Strings.T("log.live.dropReason"),
                Strings.T("log.live.dropEvidence")));
        if (c.StreamStatus.Active && c.StreamStatus.Congestion > 0.3)
            items.Add(WarnItem("lag-network", Strings.T("log.live.congestionTitle"), Strings.T("log.live.congestionReason"),
                Strings.T("log.live.congestionEvidence")));
    }

    private static DiagnosticItem WarnItem(string id, string title, string reason, string evidence)
        => new()
        {
            ProblemId = id,
            Title = title,
            Severity = DiagnosticSeverity.Warning,
            Source = Strings.T("log.source.liveState"),
            Reason = reason,
            Evidence = evidence
        };

    private static string BuildSummary(List<DiagnosticItem> items, DiagnosticContext ctx, string? query)
    {
        var sb = new StringBuilder();
        if (items.Count == 0)
        {
            sb.Append(Strings.T("log.summary.empty"));
            if (!string.IsNullOrWhiteSpace(query))
                sb.Append(Strings.T("log.summary.emptyWithQuery"));
            else
                sb.Append(Strings.T("log.summary.emptyNoQuery"));
            return sb.ToString();
        }

        var critical = items.Count(i => i.Severity == DiagnosticSeverity.Critical);
        var error = items.Count(i => i.Severity == DiagnosticSeverity.Error);
        sb.Append(Strings.T("log.summary.header", items.Count));
        if (critical > 0) sb.Append(Strings.T("log.summary.critical", critical));
        if (error > 0) sb.Append(Strings.T("log.summary.error", error));
        if (critical > 0 || error > 0) sb.Append(Strings.T("log.summary.close"));
        sb.Append(Strings.T("log.summary.colon"));

        foreach (var it in items.Take(6))
        {
            sb.Append(Strings.T("log.summary.item", it.SeverityText, it.Title));
            if (!string.IsNullOrEmpty(it.ProblemId)) sb.Append(Strings.T("log.summary.itemFromKbTail", it.ProblemId));
            sb.Append('\n');
        }
        if (items.Count > 6) sb.Append(Strings.T("common.moreItems") + "\n");

        if (ctx.Connection.IsConnected)
            sb.Append(Strings.T("log.summary.connectedHint"));
        sb.Append(Strings.T("log.summary.moreHint"));
        return sb.ToString();
    }
}
