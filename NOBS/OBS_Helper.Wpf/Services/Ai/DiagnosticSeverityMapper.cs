using OBS_Helper.Wpf.Localization;
using OBS_Helper.Wpf.Services.Obs;

namespace OBS_Helper.Wpf.Services.Ai;

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

    /// <summary>
    /// 严重度**文案** → 严重度枚举。
    ///
    /// 同时认中英两套取值（V2.9.2）：知识库的 severity 是展示文案，中英各一套；
    /// 本地已下载的外部知识库文件可能是另一种语言写的，而云端/免费 AI 回传的严重度
    /// 又来自 <see cref="DiagnosticItem.SeverityText"/>（跟随界面语言）。
    /// 只认一套会让另一套静默降级成「建议」。
    /// </summary>
    public static DiagnosticSeverity Map(string? severity) => (severity ?? "").Trim() switch
    {
        "严重" or "Critical" => DiagnosticSeverity.Critical,
        "错误" or "Error" => DiagnosticSeverity.Error,
        "警告" or "Warning" => DiagnosticSeverity.Warning,
        "一般" or "Occasional" => DiagnosticSeverity.Warning,
        "常见" or "Common" => DiagnosticSeverity.Suggestion,
        "建议" or "Suggestion" => DiagnosticSeverity.Suggestion,
        "提示" or "Info" => DiagnosticSeverity.Info,
        _ => DiagnosticSeverity.Suggestion
    };
}
