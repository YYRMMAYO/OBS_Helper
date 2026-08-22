using OBS_Helper.Wpf.Models.Obs;
using OBS_Helper.Wpf.Services.Host;
using OBS_Helper.Wpf.Services.Obs;

namespace OBS_Helper.Wpf.Services.Ai;

/// <summary>一条诊断结论，可对应一个离线知识库条目或一个实时/日志发现。</summary>
public sealed class DiagnosticItem
{
    /// <summary>关联的知识库问题 id（空表示无对应条目）。</summary>
    public string ProblemId { get; set; } = "";

    public string Title { get; set; } = "";

    public DiagnosticSeverity Severity { get; set; } = DiagnosticSeverity.Suggestion;

    /// <summary>来源：日志分析 / 知识库 / 实时状态 / 知识库(云端)。</summary>
    public string Source { get; set; } = "";

    /// <summary>为什么被标记（命中次数、关键词匹配等）。</summary>
    public string Reason { get; set; } = "";

    /// <summary>证据原文（已脱敏），云端路径也只放脱敏内容。</summary>
    public string Evidence { get; set; } = "";

    /// <summary>
    /// 嫌疑插件 / 模块名（V2.2 P0-2）：日志线索命中插件加载失败或崩溃肇事模块时提取。
    /// UI 据此给出「在插件广场查看」的跳转入口；空表示无嫌疑模块。
    /// </summary>
    public string SuspectModule { get; set; } = "";

    /// <summary>知识库分步方案的标题列表。</summary>
    public List<string> Steps { get; set; } = new();

    /// <summary>严重度文案，便于 UI 直接显示。</summary>
    public string SeverityText => Severity switch
    {
        DiagnosticSeverity.Critical => "严重",
        DiagnosticSeverity.Error => "错误",
        DiagnosticSeverity.Warning => "警告",
        DiagnosticSeverity.Suggestion => "建议",
        _ => "提示"
    };
}

/// <summary>一次诊断的完整结果，本地与云端引擎共用，UI 只认这一个结构。</summary>
public sealed class DiagnosticResult
{
    public bool Success { get; set; } = true;

    /// <summary>引擎标识：local / cloud。</summary>
    public string Engine { get; set; } = "local";

    /// <summary>面向用户的中文结论文本。</summary>
    public string Summary { get; set; } = "";

    public List<DiagnosticItem> Items { get; set; } = new();

    /// <summary>失败时的说明（可展示给用户）。</summary>
    public string? Error { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>云端失败回退到本地时为 true，UI 可据此提示用户。</summary>
    public bool FellBackToLocal { get; set; }
}

/// <summary>一次诊断所需的全部上下文，由编排器组装后传给具体引擎。</summary>
public sealed class DiagnosticContext
{
    public ObsConnectionService Connection { get; }
    public ObsLogAnalyzer Analyzer { get; }
    public ProblemService Problems { get; }
    public AssistantService Assistant { get; }
    public HostBridge Host { get; }
    public ObsLogReport? Report { get; }

    public DiagnosticContext(
        ObsConnectionService connection,
        ObsLogAnalyzer analyzer,
        ProblemService problems,
        AssistantService assistant,
        HostBridge host,
        ObsLogReport? report)
    {
        Connection = connection;
        Analyzer = analyzer;
        Problems = problems;
        Assistant = assistant;
        Host = host;
        Report = report;
    }
}
