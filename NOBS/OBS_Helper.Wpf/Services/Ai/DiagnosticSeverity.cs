namespace OBS_Helper.Wpf.Services.Ai;

/// <summary>诊断结论的严重程度（比日志侧 <see cref="LogSeverity"/> 多一档「建议」）。</summary>
public enum DiagnosticSeverity
{
    Info,
    Suggestion,
    Warning,
    Error,
    Critical
}
