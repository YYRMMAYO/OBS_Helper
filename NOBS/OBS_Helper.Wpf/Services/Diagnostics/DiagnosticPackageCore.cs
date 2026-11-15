using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using OBS_Helper.Wpf.Services.Obs;

namespace OBS_Helper.Wpf.Services.Diagnostics;

/// <summary>诊断包里的一条检查结论（不依赖 WPF / 文案表，便于单测）。</summary>
public sealed record DiagnosticCheck(string Name, string Status, string Detail);

/// <summary>生成诊断包所需的只读快照。</summary>
public sealed class DiagnosticPackageInput
{
    public string AppVersion { get; init; } = "";
    public string OsDescription { get; init; } = "";
    public DateTime GeneratedAt { get; init; } = DateTime.Now;
    public string ObsState { get; init; } = "";
    public string ObsLastError { get; init; } = "";
    public bool Recording { get; init; }
    public bool Streaming { get; init; }
    public IReadOnlyList<DiagnosticCheck> Checks { get; init; } = Array.Empty<DiagnosticCheck>();
    public IReadOnlyList<string> RecentToasts { get; init; } = Array.Empty<string>();
    public string LogTail { get; init; } = "";
}

/// <summary>
/// 诊断包纯逻辑（V3.6.0 / G08）。
///
/// 隐私口径：导出文件默认**不含绝对路径与密钥**。
/// <list type="bullet">
///   <item>所有文本先过 <see cref="LogSanitizer"/>（流密钥 / Token / Bearer / IPv6 / Base64）；</item>
///   <item>再把 Windows 绝对路径与 UNC 路径替换为 <c>&lt;path&gt;</c>；</item>
///   <item>日志只保留尾部 200 行、最多 20 KB；</item>
///   <item>不读取 OBS 配置原文、不读取用户目录清单。</item>
/// </list>
/// </summary>
public static class DiagnosticPackageCore
{
    private const int MaxLogLines = 200;
    private const int MaxLogChars = 20_000;

    private static readonly Regex WindowsPath = new(
        @"[A-Za-z]:\\[^\r\n\t""'<>|]*", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UncPath = new(
        @"\\\\[^\s""'<>|]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string BuildFileName(DateTime now)
        => $"diagnostic_{now:yyyyMMdd_HHmmss}.md";

    /// <summary>组装 Markdown 诊断包。</summary>
    public static string BuildMarkdown(DiagnosticPackageInput input)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# OBS 帮助助手诊断包");
        sb.AppendLine();
        sb.AppendLine("- 生成时间：" + input.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        sb.AppendLine("- 应用版本：" + Clean(input.AppVersion));
        sb.AppendLine("- 系统：" + Clean(input.OsDescription));
        sb.AppendLine("- 说明：本文件由用户手动导出；默认不含路径与密钥。");
        sb.AppendLine();

        sb.AppendLine("## OBS 连接");
        sb.AppendLine("- 状态：" + Clean(input.ObsState));
        if (!string.IsNullOrWhiteSpace(input.ObsLastError))
            sb.AppendLine("- 最近原因：" + Clean(input.ObsLastError));
        sb.AppendLine("- 录制：" + (input.Recording ? "进行中" : "未录制"));
        sb.AppendLine("- 推流：" + (input.Streaming ? "进行中" : "未推流"));
        sb.AppendLine();

        sb.AppendLine("## 录前自检");
        if (input.Checks.Count == 0)
        {
            sb.AppendLine("- （本次没有取到自检结果）");
        }
        else
        {
            foreach (var check in input.Checks)
                sb.AppendLine($"- [{Clean(check.Status)}] {Clean(check.Name)}：{Clean(check.Detail)}");
        }
        sb.AppendLine();

        sb.AppendLine("## 最近提示");
        if (input.RecentToasts.Count == 0)
        {
            sb.AppendLine("- （无）");
        }
        else
        {
            foreach (var toast in input.RecentToasts.Take(30))
                sb.AppendLine("- " + Clean(toast));
        }
        sb.AppendLine();

        sb.AppendLine("## 日志尾部（已脱敏）");
        var tail = Clean(TrimLog(input.LogTail));
        sb.AppendLine(string.IsNullOrWhiteSpace(tail) ? "（本次没有读到日志）" : "```text");
        if (!string.IsNullOrWhiteSpace(tail)) sb.AppendLine(tail);
        if (!string.IsNullOrWhiteSpace(tail)) sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("如该文件仍包含你不希望公开的信息，请只截取需要的段落再发送。");
        return sb.ToString();
    }

    /// <summary>对任意文本做「脱敏 + 去路径」；公开给单测直接校验。</summary>
    public static string SanitizeForDiagnostics(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var masked = LogSanitizer.Sanitize(text);
        masked = WindowsPath.Replace(masked, "<path>");
        masked = UncPath.Replace(masked, "<path>");
        return masked;
    }

    private static string Clean(string? text) => SanitizeForDiagnostics(text);

    private static string TrimLog(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var tail = lines.Length <= MaxLogLines ? lines : lines[^MaxLogLines..];
        var joined = string.Join('\n', tail);
        return joined.Length <= MaxLogChars ? joined : joined[^MaxLogChars..];
    }
}
