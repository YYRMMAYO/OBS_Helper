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
                    Source = "日志分析",
                    Reason = f.Occurrences > 1 ? $"日志中命中 {f.Occurrences} 次" : "日志中命中",
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
                    Source = "知识库",
                    Reason = string.IsNullOrEmpty(m.Reason) ? "关键词匹配" : $"关键词匹配：{m.Reason}"
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
            items.Add(WarnItem("lag-skip", "实时渲染滞后", "实时渲染丢帧率偏高，GPU 渲染跟不上，画面可能卡顿。",
                "来自 OBS 实时统计：renderSkipRatio 超过 1%。"));
        if (c.Stats.OutputSkipRatio > 0.01)
            items.Add(WarnItem("enc-overload", "编码压力偏大", "输出丢帧率偏高，编码器可能跟不上，建议下调分辨率/帧率或改用硬件编码。",
                "来自 OBS 实时统计：outputSkipRatio 超过 1%。"));
        if (c.StreamStatus.Active && c.StreamStatus.DroppedRatio > 0.01)
            items.Add(WarnItem("lag-network", "推流丢帧", "当前推流存在丢帧，上行带宽可能不足。",
                "来自 OBS 实时统计：streamDroppedRatio 超过 1%。"));
        if (c.StreamStatus.Active && c.StreamStatus.Congestion > 0.3)
            items.Add(WarnItem("lag-network", "推流拥塞", "推流拥塞度较高，上行链路吃紧，考虑降低码率。",
                "来自 OBS 实时统计：streamCongestion 超过 0.3。"));
    }

    private static DiagnosticItem WarnItem(string id, string title, string reason, string evidence)
        => new()
        {
            ProblemId = id,
            Title = title,
            Severity = DiagnosticSeverity.Warning,
            Source = "实时状态",
            Reason = reason,
            Evidence = evidence
        };

    private static string BuildSummary(List<DiagnosticItem> items, DiagnosticContext ctx, string? query)
    {
        var sb = new StringBuilder();
        if (items.Count == 0)
        {
            sb.Append("未从当前的日志或连接状态中发现明显异常。");
            if (!string.IsNullOrWhiteSpace(query))
                sb.Append("针对你描述的现象，已为你匹配下方知识库条目，可点开查看分步方案。");
            else
                sb.Append("你可以：① 在「日志分析」里打开一份 OBS 日志做深度扫描；② 直接在对话框描述你遇到的现象（如「推流一直重连」）。");
            return sb.ToString();
        }

        var critical = items.Count(i => i.Severity == DiagnosticSeverity.Critical);
        var error = items.Count(i => i.Severity == DiagnosticSeverity.Error);
        sb.Append($"本地离线诊断完成，共发现 {items.Count} 项");
        if (critical > 0) sb.Append($"（其中严重 {critical} 项");
        if (error > 0) sb.Append($"、错误 {error} 项");
        if (critical > 0 || error > 0) sb.Append('）');
        sb.Append("：\n");

        foreach (var it in items.Take(6))
        {
            sb.Append($"· [{it.SeverityText}] {it.Title}");
            if (!string.IsNullOrEmpty(it.ProblemId)) sb.Append($"（知识库：{it.ProblemId}）");
            sb.Append('\n');
        }
        if (items.Count > 6) sb.Append("…（更多见下方列表）\n");

        if (ctx.Connection.IsConnected)
            sb.Append("\n提示：当前已连接 OBS，可在「控制台」直接查看/调整相关设置。");
        sb.Append("\n如需更细致的多轮分析，可在「AI 设置」中切换到免费 AI 或云端大模型。");
        return sb.ToString();
    }
}
