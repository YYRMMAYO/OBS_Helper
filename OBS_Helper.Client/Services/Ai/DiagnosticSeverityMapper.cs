using OBS_Helper.Client.Services.Obs;

namespace OBS_Helper.Client.Services.Ai;

/// <summary>严重程度映射工具（纯函数）：在日志侧 <see cref="LogSeverity"/> 与知识库侧文案之间做桥接，便于单测与服务间复用。</summary>
public static class DiagnosticSeverityMapper
{
    public static DiagnosticSeverity Map(LogSeverity s) => s switch
    {
        LogSeverity.Critical => DiagnosticSeverity.Critical,
        LogSeverity.Error => DiagnosticSeverity.Error,
        LogSeverity.Warning => DiagnosticSeverity.Warning,
        _ => DiagnosticSeverity.Info
    };

    public static DiagnosticSeverity Map(string? severity) => severity switch
    {
        "严重" => DiagnosticSeverity.Critical,
        "错误" => DiagnosticSeverity.Error,
        "警告" => DiagnosticSeverity.Warning,
        "一般" => DiagnosticSeverity.Warning,
        "常见" => DiagnosticSeverity.Suggestion,
        _ => DiagnosticSeverity.Suggestion
    };
}
