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

    // V3.0（第四轮验证发现）：这两个字段原先直接读 HostBridge.AppDataDirectory ——
    // 只要数据目录不可创建，静态构造就抛异常，而第一个触到 FileLogger 的往往是全局异常处理器，
    // 于是「日志目录建不出来」升级成「进程启动即死、界面全白」。日志是**辅助设施**，
    // 任何时候都不该有能力把主程序带走：拿不到目录就退化成「不写文件」。
    private static readonly string LogDirectory = ResolveLogDirectory();
    private static readonly Task Writer;

    private static string ResolveLogDirectory()
    {
        try { return Path.Combine(HostBridge.AppDataDirectory, "logs"); }
        catch (Exception) { return Path.Combine(Path.GetTempPath(), "OBS_Helper_logs"); }
    }

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

    /// <summary>
    /// 启动横幅（V3.0）。
    ///
    /// 为什么需要：日志文件只有「时间 + 分类 + 消息」时，拿到一份日志无法判断
    /// 它是哪个版本、哪种构建（主构建 / Win7 兼容构建）、哪一次运行、数据目录在哪 ——
    /// 而这几件事恰好决定「用户看到的行为」是否可能与当前代码一致。
    /// </summary>
    public static void LogStartupBanner(string version, string buildFlavor)
    {
        try
        {
            Info("Startup",
                $"ver={version} build={buildFlavor} os={Environment.OSVersion.Version} " +
                $"pid={Environment.ProcessId} data={HostBridge.AppDataDirectory} exe={Environment.ProcessPath}");
        }
        catch (Exception)
        {
            // 横幅只影响可诊断性，绝不能因为它拦住启动
        }
    }

    private static void Enqueue(string level, string category, string message, Exception? ex)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] [{category}] {message}";
        if (ex is not null)
        {
            // V3.0：递归展开 InnerException —— AggregateException / 包装异常的**根因**原本会被丢掉，
            // 线上只看到一句「发生一个或多个错误」，等于没有线索。
            var depth = 0;
            for (var cur = ex; cur is not null && depth < 8; cur = cur.InnerException, depth++)
            {
                var prefix = depth == 0 ? "  " : "  " + new string('>', depth) + " ";
                line += $"\n{prefix}{cur.GetType().FullName}: {cur.Message}";
                if (depth == 0 && cur.StackTrace is { Length: > 0 }) line += $"\n  {cur.StackTrace}";
            }
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

    /// <summary>
    /// 等待队列排空（**不关闭**队列）。用于「异常处理完还要继续跑」的场合：
    /// 例如 <c>DispatcherUnhandledException</c> 标记 <c>Handled = true</c> 之后进程仍然活着。
    ///
    /// 为什么必须与 <see cref="Flush"/> 分开：Flush 会 <c>TryComplete()</c> 关闭队列，
    /// 而队列一旦关闭，之后所有 <c>Info/Warn/Error</c> 的写入都是静默丢弃 ——
    /// 「崩溃后继续运行」的场景里，等于把本次会话剩下的日志全丢掉，与提升可诊断性的目标正好相反。
    /// </summary>
    public static void Drain(TimeSpan? timeout = null)
    {
        try
        {
            var writer = Writer;
            if (writer is null || writer.IsCompleted) return;
            writer.Wait(timeout ?? TimeSpan.FromSeconds(2));
        }
        catch (Exception)
        {
            // 排空失败（超时等）不影响主流程
        }
    }

    /// <summary>应用退出前调用：关闭队列并等待后台写盘完成（最多 2 秒）。<b>只应调用一次</b>。</summary>
    public static void Flush()
    {
        Queue.Writer.TryComplete();
        try { Writer.Wait(TimeSpan.FromSeconds(2)); } catch (Exception) { }
    }
}
