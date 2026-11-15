using System.IO;
using System.Text;
using OBS_Helper.Wpf.Services.Host;
using OBS_Helper.Wpf.Services.Obs;

namespace OBS_Helper.Wpf.Services.Diagnostics;

/// <summary>
/// 一键导出诊断包（V3.6.0 / G08）。
///
/// 只收集「支持人员真正需要、且用户能理解」的内容：版本、系统、OBS 连接、录前自检、
/// 最近 30 条 Toast、当前会话日志尾部。所有文本在 <see cref="DiagnosticPackageCore"/> 里
/// 统一脱敏并去掉绝对路径；导出动作由用户点击触发，本服务不做任何上传。
/// </summary>
public sealed class DiagnosticPackageService
{
    /// <summary>生成并落盘一份诊断包，返回文件路径。</summary>
    public async Task<string> ExportAsync(CancellationToken ct = default)
    {
        var obs = AppServices.Obs;
        var checks = new List<DiagnosticCheck>();

        try
        {
            var report = await AppServices.Preflight.RunAsync().ConfigureAwait(false);
            foreach (var item in report.Items)
                checks.Add(new DiagnosticCheck(item.Title, item.StatusText, item.Detail));
        }
        catch (Exception ex)
        {
            checks.Add(new DiagnosticCheck("录前自检", "unknown", ex.Message));
        }

        var toasts = AppServices.Toast?.Recent
            .Take(30)
            .Select(t => $"{t.LocalTime:HH:mm:ss} [{t.Severity}] {t.Message}")
            .ToList() ?? new List<string>();

        var input = new DiagnosticPackageInput
        {
            AppVersion = FeedbackPageVersion(),
            OsDescription = Environment.OSVersion.VersionString,
            GeneratedAt = DateTime.Now,
            ObsState = obs.IsConnected ? "Connected" : obs.LastErrorIsFailure ? "Failed" : "Disconnected",
            ObsLastError = obs.LastError ?? "",
            Recording = obs.RecordStatus.Active,
            Streaming = obs.StreamStatus.Active,
            Checks = checks,
            RecentToasts = toasts,
            LogTail = await ReadLogTailAsync().ConfigureAwait(false),
        };

        var text = DiagnosticPackageCore.BuildMarkdown(input);
        var dir = Path.Combine(HostBridge.AppDataDirectory, "diagnostics");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, DiagnosticPackageCore.BuildFileName(DateTime.Now));
        await File.WriteAllTextAsync(path, text, new UTF8Encoding(false), ct).ConfigureAwait(false);
        FileLogger.Info("Diagnostics", $"诊断包已导出：{path}");
        return path;
    }

    private static string FeedbackPageVersion()
    {
        try
        {
            return typeof(AppServices).Assembly.GetName().Version?.ToString() ?? "unknown";
        }
        catch (Exception)
        {
            return "unknown";
        }
    }

    private static async Task<string> ReadLogTailAsync()
    {
        try
        {
            var dir = Path.Combine(HostBridge.AppDataDirectory, "logs");
            if (!Directory.Exists(dir)) return "";

            var file = new DirectoryInfo(dir)
                .GetFiles("app-*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();
            if (file is null) return "";

            const int maxBytes = 64 * 1024;
            await using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);
            var start = Math.Max(0, stream.Length - maxBytes);
            stream.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return await reader.ReadToEndAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Diagnostics", $"读取日志尾部失败：{ex.Message}");
            return "";
        }
    }
}
