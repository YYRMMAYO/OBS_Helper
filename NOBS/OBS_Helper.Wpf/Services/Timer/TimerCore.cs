using System.Globalization;
using System.Text;

namespace OBS_Helper.Wpf.Services.Timing;

/// <summary>
/// 计时器纯逻辑（零 WPF、零 IO）。
///
/// 设计要点：
/// <list type="bullet">
///   <item><b>时间由调用方注入</b>：<see cref="TimerMachine"/> 的每个操作都接收 <c>DateTime now</c>，
///         因此单测可以精确控制时间，不依赖 Thread.Sleep。</item>
///   <item><b>倒计时归零后 State=Finished</b>：计时窗停在 00:00，控制窗继续累计超时；
///         用户停止时才生成记录。</item>
///   <item><b>顺计时不进入 Finished</b>：超过计划时长只把 <see cref="TimerSnapshot.IsOvertime"/> 置真，
///         计时继续。</item>
///   <item><b>格式化不用 TimeSpan 的 hh 格式</b>：超过 24 小时会回绕，这里自己算。</item>
/// </list>
/// </summary>
public static class TimerCore
{
    public const int MaxPlannedSeconds = 99 * 3600 + 59 * 60 + 59;
    public const double MinOpacity = 0.2;
    public const double MaxOpacity = 1.0;
    public const double DefaultOpacity = 0.92;
    public const double MinFontScale = 0.6;
    public const double MaxFontScale = 3.0;
    public const double DefaultFontScale = 1.0;
    public const int MinWindowWidth = 220;
    public const int MinWindowHeight = 100;
    public const int DefaultWindowWidth = 360;
    public const int DefaultWindowHeight = 160;
    public const int MaxRecords = 200;

    /// <summary>把用户输入解析成时长；非法输入返回 <see cref="TimeSpan.Zero"/>。</summary>
    /// <remarks>支持 <c>900</c>（秒）、<c>15:00</c>（分:秒）、<c>1:02:03</c>（时:分:秒）。</remarks>
    public static TimeSpan ParseDuration(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return TimeSpan.Zero;
        var parts = text.Trim().Split(':');
        if (parts.Length is < 1 or > 3) return TimeSpan.Zero;

        var values = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out values[i]))
                return TimeSpan.Zero;
            if (values[i] < 0) return TimeSpan.Zero;
        }

        long seconds = parts.Length switch
        {
            1 => values[0],
            2 when values[1] < 60 => values[0] * 60L + values[1],
            3 when values[1] < 60 && values[2] < 60 => values[0] * 3600L + values[1] * 60L + values[2],
            _ => -1,
        };
        if (seconds < 0) return TimeSpan.Zero;
        return TimeSpan.FromSeconds(Math.Min(seconds, MaxPlannedSeconds));
    }

    /// <summary>格式化主显示；向下取整到秒，负数按 0 处理。</summary>
    public static string FormatClock(TimeSpan value, bool showHours)
    {
        var total = (long)Math.Floor(Math.Max(0, value.TotalSeconds));
        var hours = total / 3600;
        var minutes = total % 3600 / 60;
        var seconds = total % 60;
        return showHours || hours > 0
            ? $"{hours:00}:{minutes:00}:{seconds:00}"
            : $"{minutes:00}:{seconds:00}";
    }

    /// <summary>记录表 / 副显示用的紧凑格式。</summary>
    public static string FormatDuration(TimeSpan value)
    {
        var total = (long)Math.Floor(Math.Max(0, value.TotalSeconds));
        var hours = total / 3600;
        var minutes = total % 3600 / 60;
        var seconds = total % 60;
        return hours > 0
            ? $"{hours:00}:{minutes:00}:{seconds:00}"
            : $"{minutes:00}:{seconds:00}";
    }

    /// <summary>超时显示：始终带 <c>+</c> 前缀，便于与正常计时区分。</summary>
    public static string FormatSignedOvertime(TimeSpan value)
        => "+" + FormatDuration(value);

    /// <summary>记录展示顺序：最新（Id 最大）在上。</summary>
    public static IReadOnlyList<TimerRecord> OrderForDisplay(IEnumerable<TimerRecord> records)
        => records.OrderByDescending(r => r.Id).ToList();

    /// <summary>生成 TSV（Excel 可直接粘贴）；序号按展示顺序从 1 开始。</summary>
    public static string BuildTsv(IEnumerable<TimerRecord> records)
    {
        var sb = new StringBuilder();
        sb.Append("序号\t开始时间\t模式\t计划时长\t实际用时\t超时\r\n");
        var ordered = OrderForDisplay(records);
        for (var i = 0; i < ordered.Count; i++)
        {
            var r = ordered[i];
            sb.Append(i + 1).Append('\t')
              .Append(Escape(r.StartedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))).Append('\t')
              .Append(r.Mode == TimerMode.CountDown ? "CountDown" : "CountUp").Append('\t')
              .Append(FormatDuration(TimeSpan.FromSeconds(r.PlannedSeconds))).Append('\t')
              .Append(FormatDuration(TimeSpan.FromSeconds(r.ActualSeconds))).Append('\t')
              .Append(r.OvertimeSeconds > 0
                  ? FormatSignedOvertime(TimeSpan.FromSeconds(r.OvertimeSeconds))
                  : "-")
              .Append("\r\n");
        }
        return sb.ToString();
    }

    /// <summary>生成 CSV（Excel 可直接打开）；序号按展示顺序从 1 开始。</summary>
    public static string BuildCsv(IEnumerable<TimerRecord> records)
    {
        var sb = new StringBuilder();
        sb.Append("序号,开始时间,模式,计划时长,实际用时,超时\r\n");
        var ordered = OrderForDisplay(records);
        for (var i = 0; i < ordered.Count; i++)
        {
            var r = ordered[i];
            sb.Append(i + 1).Append(',')
              .Append(CsvEscape(r.StartedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))).Append(',')
              .Append(r.Mode == TimerMode.CountDown ? "CountDown" : "CountUp").Append(',')
              .Append(CsvEscape(FormatDuration(TimeSpan.FromSeconds(r.PlannedSeconds)))).Append(',')
              .Append(CsvEscape(FormatDuration(TimeSpan.FromSeconds(r.ActualSeconds)))).Append(',')
              .Append(r.OvertimeSeconds > 0
                  ? CsvEscape(FormatSignedOvertime(TimeSpan.FromSeconds(r.OvertimeSeconds)))
                  : "-")
              .Append("\r\n");
        }
        return sb.ToString();
    }

    private static string CsvEscape(string value)
        => value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0
            ? value
            : "\"" + value.Replace("\"", "\"\"") + "\"";

    private static string Escape(string value)
        => value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    public static double ClampOpacity(double value)
        => double.IsNaN(value) ? DefaultOpacity : Math.Clamp(value, MinOpacity, MaxOpacity);

    public static double ClampFontScale(double value)
        => double.IsNaN(value) ? DefaultFontScale : Math.Clamp(value, MinFontScale, MaxFontScale);

    /// <summary>显示器索引越界时回退到主屏；没有主屏标记则回退 0。</summary>
    public static int NormalizeMonitorIndex(int index, IReadOnlyList<TimerScreen> screens)
    {
        if (screens.Count == 0) return 0;
        if (index >= 0 && index < screens.Count) return index;
        for (var i = 0; i < screens.Count; i++)
            if (screens[i].IsPrimary) return i;
        return 0;
    }

    /// <summary>把窗口夹进工作区；宽高不低于最小值，也不超过工作区。</summary>
    public static TimerRect FitToWorkArea(TimerRect window, TimerRect workArea)
    {
        if (workArea.IsEmpty) return window;

        var width = Math.Clamp(window.Width <= 0 ? DefaultWindowWidth : window.Width,
            Math.Min(MinWindowWidth, workArea.Width), workArea.Width);
        var height = Math.Clamp(window.Height <= 0 ? DefaultWindowHeight : window.Height,
            Math.Min(MinWindowHeight, workArea.Height), workArea.Height);

        var left = Math.Clamp(window.Left, workArea.Left, Math.Max(workArea.Left, workArea.Right - width));
        var top = Math.Clamp(window.Top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - height));
        return new TimerRect(left, top, width, height);
    }

    /// <summary>
    /// 计算计时窗最终位置：
    /// 1) 优先使用保存位置；2) 保存位置完全落在屏幕外（拔掉副屏）回主屏默认位；
    /// 3) 尺寸 / 位置统一夹进目标屏幕工作区。
    /// </summary>
    public static TimerRect ResolveBounds(
        TimerRect? saved, IReadOnlyList<TimerScreen> screens, int preferredIndex,
        double defaultWidth = DefaultWindowWidth, double defaultHeight = DefaultWindowHeight)
    {
        if (screens.Count == 0)
            return saved ?? new TimerRect(0, 0, defaultWidth, defaultHeight);

        var index = NormalizeMonitorIndex(preferredIndex, screens);
        var work = screens[index].WorkArea;

        var width = saved is { Width: > 0 } ? saved.Value.Width : defaultWidth;
        var height = saved is { Height: > 0 } ? saved.Value.Height : defaultHeight;

        if (saved is null)
        {
            return FitToWorkArea(
                new TimerRect(work.Left + 24, work.Top + 24, width, height), work);
        }

        var candidate = new TimerRect(saved.Value.Left, saved.Value.Top, width, height);
        var visible = screens.Any(s => candidate.Intersects(s.WorkArea));
        if (!visible)
        {
            // 拔掉副屏 / 分辨率变化后，保存位置可能完全在所有屏幕之外：
            // 按产品约定回主屏默认位，而不是回到「上一次选的副屏」。
            var primary = screens.FirstOrDefault(s => s.IsPrimary) ?? screens[0];
            var primaryWork = primary.WorkArea;
            return FitToWorkArea(
                new TimerRect(primaryWork.Left + 24, primaryWork.Top + 24, width, height), primaryWork);
        }

        return FitToWorkArea(candidate, work);
    }
}

/// <summary>
/// 计时状态机。所有方法都接收 <c>now</c>，不读系统时钟；线程安全由调用方（服务层）保证。
/// </summary>
public sealed class TimerMachine
{
    private DateTime _startedAt;
    private TimeSpan _accumulated = TimeSpan.Zero;
    private DateTime? _finishedAt;

    public TimerState State { get; private set; } = TimerState.Idle;
    public TimerMode Mode { get; set; } = TimerMode.CountDown;
    public TimeSpan Planned { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>开始新一轮；Finished 状态下再次开始视为新一轮。</summary>
    public void Start(DateTime now)
    {
        State = TimerState.Running;
        _startedAt = now;
        _accumulated = TimeSpan.Zero;
        _finishedAt = null;
    }

    public void Pause(DateTime now)
    {
        if (State != TimerState.Running) return;
        _accumulated = ElapsedAt(now);
        State = TimerState.Paused;
    }

    public void Resume(DateTime now)
    {
        if (State != TimerState.Paused) return;
        _startedAt = now;
        State = TimerState.Running;
    }

    /// <summary>倒计时归零时进入 Finished，并记下精确的归零墙钟时间。</summary>
    public void Tick(DateTime now)
    {
        if (State != TimerState.Running || Mode != TimerMode.CountDown || Planned <= TimeSpan.Zero) return;
        if (ElapsedAt(now) < Planned) return;

        // 精确归零时间：当前累计 + 本次运行还需要的时间
        var needed = Planned - _accumulated;
        if (needed < TimeSpan.Zero) needed = TimeSpan.Zero;
        _finishedAt = _startedAt + needed;
        _accumulated = Planned;
        State = TimerState.Finished;
    }

    /// <summary>结束当前一轮并生成记录；Idle 返回 null。</summary>
    public TimerRecord? Stop(DateTime now, int nextId)
    {
        if (State == TimerState.Idle) return null;

        var record = new TimerRecord
        {
            Id = nextId,
            StartedAt = _startedAt,
            Mode = Mode,
            PlannedSeconds = (int)Math.Floor(Planned.TotalSeconds),
            ActualSeconds = ActualSecondsAt(now),
            OvertimeSeconds = (int)Math.Floor(OvertimeAt(now).TotalSeconds),
        };
        Reset();
        return record;
    }

    public void Reset()
    {
        State = TimerState.Idle;
        _startedAt = default;
        _accumulated = TimeSpan.Zero;
        _finishedAt = null;
    }

    public TimeSpan ElapsedAt(DateTime now)
    {
        if (State == TimerState.Running)
        {
            var delta = now - _startedAt;
            if (delta < TimeSpan.Zero) delta = TimeSpan.Zero;
            return _accumulated + delta;
        }
        return _accumulated;
    }

    public TimeSpan OvertimeAt(DateTime now)
    {
        if (State == TimerState.Finished && _finishedAt is { } finished)
        {
            var delta = now - finished;
            return delta > TimeSpan.Zero ? delta : TimeSpan.Zero;
        }
        if (Mode == TimerMode.CountUp && Planned > TimeSpan.Zero)
        {
            var elapsed = ElapsedAt(now);
            var over = elapsed - Planned;
            return over > TimeSpan.Zero ? over : TimeSpan.Zero;
        }
        return TimeSpan.Zero;
    }

    public TimerSnapshot Snapshot(DateTime now, bool showHours = false)
    {
        var elapsed = ElapsedAt(now);
        var overtime = OvertimeAt(now);

        // 倒计时实际用时最多到计划时长；超时单独累计，避免记录里出现「实际 75 分、超时 15 分」的重复。
        var actual = State == TimerState.Finished && Mode == TimerMode.CountDown
            ? Planned
            : elapsed;

        var remaining = Planned - elapsed;
        if (Mode == TimerMode.CountDown && remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

        var display = Mode == TimerMode.CountDown
            ? TimerCore.FormatClock(remaining, showHours)
            : TimerCore.FormatClock(elapsed, showHours);

        var secondary = overtime > TimeSpan.Zero
            ? TimerCore.FormatSignedOvertime(overtime)
            : TimerCore.FormatDuration(Planned);

        return new TimerSnapshot(
            State,
            Mode,
            actual,
            Planned,
            remaining,
            overtime,
            overtime > TimeSpan.Zero,
            State == TimerState.Paused,
            display,
            secondary);
    }

    private int ActualSecondsAt(DateTime now)
    {
        var elapsed = ElapsedAt(now);
        if (State == TimerState.Finished && Mode == TimerMode.CountDown)
            elapsed = Planned;
        return (int)Math.Floor(Math.Max(0, elapsed.TotalSeconds));
    }
}
