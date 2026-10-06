using System.IO;
using OBS_Helper.Wpf.Services.Diagnostics;
using System.Text;

namespace OBS_Helper.Wpf.Services.Shell;

/// <summary>
/// 实时日志尾随预警（V2.8，GAP-4，只读）。
///
/// 直播进行中主播看不到 OBS 窗口，掉帧 / 过载 / 断流过去只能事后翻日志。
/// 本服务尾随 %AppData%\obs-studio\logs 下最新的会话日志：
/// <list type="bullet">
///   <item>FileStream + FileShare.ReadWrite 增量读取（OBS 写日志不锁文件）；</item>
///   <item>命中规则复用 <c>ObsLogAnalyzer.Rules</c>（一处维护），脱敏后仅取警告级以上；</item>
///   <item>同类告警 90 秒抑制、每小时全局限流（<see cref="LogAlertThrottle"/>），托盘提醒；</item>
///   <item>自动跟随 OBS 滚动到新日志文件；大行跨块读取有缓冲兜底。</item>
/// </list>
/// </summary>
public sealed class LogTailerService : IDisposable
{
    /// <summary>轮询间隔：实时性够用且几乎零开销。</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    /// <summary>单次最多消费的增量字节数（防止首次启动时整文件灌入）。</summary>
    private const long MaxChunkBytes = 256 * 1024;

    private readonly TrayService _tray;
    private readonly LogAlertThrottle _throttle = new();
    private readonly StringBuilder _lineBuffer = new(1024);

    private System.Threading.Timer? _timer;
    private string? _currentFile;
    private long _offset;
    private int _polling;

    /// <summary>
    /// 本次会话的命中累计（V3.0 / D5）。
    ///
    /// 为什么需要：实时预警只在**首次**命中时弹一条托盘气泡（有 90 秒抑制与每小时限流），
    /// 于是「今晚一共被提醒了哪些问题、哪一类反复出现」在事后完全无从回答 ——
    /// 而这两个问题恰恰是主播复盘时最想知道的。
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SessionHit> _hits = new();

    /// <summary>本次会话开始统计的本地时刻。</summary>
    public DateTime SessionStartedLocal { get; private set; } = DateTime.Now;

    /// <summary>命中累计发生变化（界面可据此刷新；可能在计时器线程触发）。</summary>
    public event Action? HitsChanged;

    public LogTailerService(TrayService tray) => _tray = tray;

    /// <summary>
    /// 本次会话命中的问题（按次数降序）。只统计**警告及以上**，与实时预警同一口径 ——
    /// 把 info 级也计进来只会让复盘变成噪音。
    /// </summary>
    public IReadOnlyList<SessionHit> SessionHits
        => _hits.Values.OrderByDescending(h => h.Count).ThenBy(h => h.Code, StringComparer.Ordinal).ToList();

    /// <summary>本次会话命中总次数（同一类问题按实际命中次数累加）。</summary>
    public int SessionHitTotal => _hits.Values.Sum(h => h.Count);

    /// <summary>开始新的一次会话统计（例如用户点了「结束本次统计」之后）。</summary>
    public void ResetSession()
    {
        _hits.Clear();
        SessionStartedLocal = DateTime.Now;
        HitsChanged?.Invoke();
    }

    /// <summary>开关读取自 ShellSettings（与托盘共用一份持久化配置）。</summary>
    public bool Enabled => _tray.Settings.RealtimeLogAlertEnabled;

    /// <summary>启动尾随（幂等）。默认随应用启动开启，可在设置中关闭。</summary>
    public void Start()
    {
        if (!Enabled || _timer is not null) return;
        _timer = new System.Threading.Timer(_ => PollSafe(), null, PollInterval, PollInterval);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>设置变更后调用：按最新开关决定启停。</summary>
    public void ApplyEnabled()
    {
        if (Enabled) Start();
        else Stop();
    }

    public void Dispose() => Stop();

    // ------------------------------------------------------------ 内部

    internal static string LogsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "obs-studio", "logs");

    /// <summary>logs 目录下最新的会话日志；目录不存在 / 为空返回 null。</summary>
    internal static string? FindNewestLogFile()
    {
        // 注意：OBS 会话日志是 *.txt（不是 *.log）。V2.8 这里只扫 *.log，
        // 结果在真实 OBS 上永远找不到文件、实时预警整条链路静默失效。
        // 命中规则收敛到 ObsLogFileFinder 并配了单元测试，避免回归。
        return ObsLogFileFinder.FindNewest(LogsDirectory);
    }

    private void PollSafe()
    {
        if (Interlocked.Exchange(ref _polling, 1) == 1) return;
        var hitsDirty = false;
        try
        {
            Poll();
            hitsDirty = _hitsDirty;
            _hitsDirty = false;
        }
        catch (Exception)
        {
            // 尾随失败静默跳过，下个周期重试；绝不影响主程序
        }
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
            // 每个轮询周期最多通知一次：一批日志里可能命中几十行，
            // 逐行触发事件会让界面重建几十次（界面刷新是「看结果」，不需要每行都看一眼）
            if (hitsDirty) HitsChanged?.Invoke();
        }
    }

    private volatile bool _hitsDirty;

    private void Poll()
    {
        var newest = FindNewestLogFile();
        if (newest is null)
        {
            SwitchFile(null);
            return;
        }

        if (!string.Equals(newest, _currentFile, StringComparison.OrdinalIgnoreCase))
        {
            SwitchFile(newest);
        }

        var lines = ReadIncrement(newest);
        foreach (var rawLine in lines)
        {
            HandleLine(rawLine);
        }
    }

    private void SwitchFile(string? path)
    {
        _currentFile = path;
        // -1 哨兵：首次挂载从文件末尾开始，只看「之后新增」的内容，
        // 避免把上一个会话的历史告警在启动时全部重放一遍
        _offset = -1;
        lock (_lineBuffer) _lineBuffer.Clear();
        _throttle.Reset(); // 新会话新窗口
    }

    /// <summary>从上次偏移读取新增的完整行；文件被截断（重写）时自动归零重来。</summary>
    private List<string> ReadIncrement(string file)
    {
        var lines = new List<string>();
        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (_offset < 0)
            {
                _offset = fs.Length; // 首次挂载：跳过历史，从当前末尾开始尾随
                return lines;
            }
            if (fs.Length < _offset)
            {
                _offset = 0; // 日志被截断/重建
                lock (_lineBuffer) _lineBuffer.Clear();
            }
            if (fs.Length <= _offset) return lines;

            fs.Seek(_offset, SeekOrigin.Begin);
            var take = (int)Math.Min(fs.Length - _offset, MaxChunkBytes);
            var buf = new byte[take];
            var read = 0;
            while (read < buf.Length)
            {
                var n = fs.Read(buf, read, buf.Length - read);
                if (n <= 0) break;
                read += n;
            }
            _offset += read;

            var chunk = Encoding.UTF8.GetString(buf, 0, read);
            List<string> completed;
            lock (_lineBuffer)
            {
                _lineBuffer.Append(chunk);
                var text = _lineBuffer.ToString();
                var lastNl = text.LastIndexOf('\n');
                if (lastNl < 0) return lines; // 还没有完整行

                completed = SplitLines(text[..lastNl]);
                _lineBuffer.Clear();
                _lineBuffer.Append(text[(lastNl + 1)..]);
            }

            lines.AddRange(completed.Where(l => l.Trim().Length > 0));
        }
        catch (IOException)
        {
            // 文件暂时被占用等瞬时错误：跳过本轮
        }
        catch (Exception)
        {
            // 其他异常同样吞掉：监控功能不允许反过来打扰用户
        }
        return lines;
    }

    private static List<string> SplitLines(string text)
    {
        var list = new List<string>();
        var parts = text.Split('\n');
        foreach (var p in parts)
        {
            list.Add(p.EndsWith('\r') ? p[..^1] : p);
        }
        return list;
    }

    private void HandleLine(string rawLine)
    {
        var line = Services.Obs.LogSanitizer.SanitizeLine(rawLine);
        if (line.Length == 0) return;

        foreach (var rule in Services.Obs.ObsLogAnalyzer.Rules)
        {
            if (rule.Severity < Obs.LogSeverity.Warning) continue; // 实时通道只报警告及以上
            try
            {
                if (!rule.Pattern.IsMatch(line)) continue;
            }
            catch (Exception)
            {
                continue;
            }

            RecordHit(rule.Code, rule.Title, rule.Suggestion);

            if (_throttle.ShouldNotify(rule.Code, DateTime.UtcNow))
            {
                _tray.Notify(Strings.T("logtailer.alertTitle", rule.Title), rule.Suggestion);
            }
        }
    }

    /// <summary>
    /// 累计一次命中（V3.0 / D5）。**每次命中都记**，不受预警节流影响 ——
    /// 节流是「别打扰用户」，而复盘要的是真实频次；两者目的不同，不能共用一个计数。
    /// </summary>
    private void RecordHit(string code, string title, string suggestion)
    {
        try
        {
            var now = DateTime.Now;
            _hits.AddOrUpdate(code,
                _ => new SessionHit(code, title, suggestion, 1, now, now),
                (_, existing) => existing with { Count = existing.Count + 1, LastLocal = now });

            _hitsDirty = true;   // 由轮询周期统一发一次通知（见 PollSafe）
        }
        catch (Exception)
        {
            // 统计失败绝不能影响实时预警这条主链路
        }
    }
}
