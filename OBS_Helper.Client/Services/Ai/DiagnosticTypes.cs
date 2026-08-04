using OBS_Helper.Client.Models.Obs;
using OBS_Helper.Client.Services.Host;
using OBS_Helper.Client.Services.Obs;

namespace OBS_Helper.Client.Services.Ai;

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

    /// <summary>系统体检结果（方向 A）。宿主不可用或未体检时为 null。</summary>
    public SystemHealthReport? System { get; }

    /// <summary>OBS 配置体检结果（方向 B）。读不到配置目录时为 null。</summary>
    public ObsConfigReport? Config { get; }

    public DiagnosticContext(
        ObsConnectionService connection,
        ObsLogAnalyzer analyzer,
        ProblemService problems,
        AssistantService assistant,
        HostBridge host,
        ObsLogReport? report,
        SystemHealthReport? system = null,
        ObsConfigReport? config = null)
    {
        Connection = connection;
        Analyzer = analyzer;
        Problems = problems;
        Assistant = assistant;
        Host = host;
        Report = report;
        System = system;
        Config = config;
    }
}
