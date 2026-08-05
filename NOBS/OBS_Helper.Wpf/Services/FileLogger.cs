using System.IO;
using System.Text;
using System.Threading.Channels;
using OBS_Helper.Wpf.Services.Host;

namespace OBS_Helper.Wpf.Services;

/// <summary>
/// 最小文件日志：应用自身运行日志，供离线排障（崩溃 / 异常可追溯）。
///
/// 设计取舍：
/// <list type="bullet">
///   <item>落盘到 %LocalAppData%\OBS_Helper\logs\app-yyyyMMdd.log，按日滚动，保留最近 14 份；</item>
///   <item><see cref="Channel{T}"/> 无界队列 + 单个后台 Task 顺序写盘，任意线程调用都线程安全；</item>
///   <item>日志写入失败（磁盘满 / 权限）静默丢弃，绝不影响主流程——日志是辅助，不是功能本身；</item>
///   <item>静态类而非注入单例：全局异常处理发生在 AppServices 装配之前，静态入口最可靠。</item>
/// </list>
/// </summary>
public static class FileLogger
{
    private const int KeepDays = 14;

    private static readonly Channel<string> Queue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = true
    });

    private static readonly string LogDirectory = Path.Combine(HostBridge.AppDataDirectory, "logs");
    private static readonly Task Writer;

    private static string _currentDay = "";
    private static string _currentFile = "";

    static FileLogger()
    {
        try { Directory.CreateDirectory(LogDirectory); } catch (Exception) { }
        PruneOldLogs();

        Writer = Task.Run(async () =>
        {
            await foreach (var line in Queue.Reader.ReadAllAsync())
            {
                try { File.AppendAllText(CurrentFile(), line + Environment.NewLine, new UTF8Encoding(false)); }
                catch (Exception) { /* 磁盘满 / 只读：丢弃该条，继续下一条 */ }
            }
        });
    }

    public static void Info(string category, string message) => Enqueue("INFO", category, message, null);
    public static void Warn(string category, string message) => Enqueue("WARN", category, message, null);
    public static void Error(string category, string message) => Enqueue("ERROR", category, message, null);
    public static void Error(string category, Exception ex) => Enqueue("ERROR", category, ex.Message, ex);

    private static void Enqueue(string level, string category, string message, Exception? ex)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] [{category}] {message}";
        if (ex is not null)
        {
            line += $"\n  {ex.GetType().FullName}: {ex.Message}";
            if (ex.StackTrace is { Length: > 0 }) line += $"\n  {ex.StackTrace}";
        }
        // 队列已关闭（退出 Flush 之后）时静默丢弃，调用方无需感知
        Queue.Writer.TryWrite(line);
    }

    /// <summary>当前日期对应的日志文件（跨日自动切换）。</summary>
    private static string CurrentFile()
    {
        var day = DateTime.Now.ToString("yyyyMMdd");
        if (day != _currentDay)
        {
            _currentDay = day;
            _currentFile = Path.Combine(LogDirectory, $"app-{day}.log");
        }
        return _currentFile;
    }

    /// <summary>删除超过保留天数的旧日志。</summary>
    private static void PruneOldLogs()
    {
        try
        {
            if (!Directory.Exists(LogDirectory)) return;
            var cutoff = DateTime.Today.AddDays(-KeepDays);
            foreach (var f in Directory.GetFiles(LogDirectory, "app-*.log"))
            {
                try
                {
                    var fi = new FileInfo(f);
                    if (fi.LastWriteTime < cutoff) File.Delete(f);
                }
                catch (Exception) { /* 单个文件删除失败跳过 */ }
            }
        }
        catch (Exception) { /* 枚举失败跳过，下次启动再试 */ }
    }

    /// <summary>应用退出前调用：关闭队列并等待后台写盘完成（最多 2 秒）。</summary>
    public static void Flush()
    {
        Queue.Writer.TryComplete();
        try { Writer.Wait(TimeSpan.FromSeconds(2)); } catch (Exception) { }
    }
}
