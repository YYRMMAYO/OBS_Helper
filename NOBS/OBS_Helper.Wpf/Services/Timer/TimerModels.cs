using System.Text.Json.Serialization;

namespace OBS_Helper.Wpf.Services.Timing;

/// <summary>计时模式：倒计时归零后停表并累计超时；顺计时到计划时长后继续累计并标红。</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TimerMode
{
    CountDown = 0,
    CountUp = 1,
}

/// <summary>计时状态。Finished 只用于倒计时归零；此时仍继续累计超时，直到用户停止。</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TimerState
{
    Idle = 0,
    Running = 1,
    Paused = 2,
    Finished = 3,
}

/// <summary>
/// 计时器设置。全部存 prefs.json 的 <c>timer.settings</c>，不含敏感信息。
/// 窗口位置用可空值：未保存时为 null，避免 System.Text.Json 拒绝序列化 NaN。
/// </summary>
public sealed class TimerSettings
{
    public TimerMode Mode { get; set; } = TimerMode.CountDown;

    /// <summary>计划时长（秒）。0 表示不限时（顺计时）。上限 99:59:59。</summary>
    public int PlannedSeconds { get; set; } = 900;

    public bool ShowHours { get; set; }

    /// <summary>计时窗整体透明度（0.2–1.0）。</summary>
    public double Opacity { get; set; } = 0.92;

    /// <summary>计时窗字号倍率（0.6–3.0）。</summary>
    public double FontScale { get; set; } = 1.0;

    public bool ShowTimerWindow { get; set; }

    /// <summary>目标显示器索引；-1 表示自动（主屏）。</summary>
    public int MonitorIndex { get; set; } = -1;

    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double WindowWidth { get; set; } = 360;
    public double WindowHeight { get; set; } = 160;

    /// <summary>录制 / 捕获时排除计时窗（Win10 2004+；Win7 降级）。</summary>
    public bool ExcludeFromCapture { get; set; } = true;

    public bool ClickThrough { get; set; }

    /// <summary>OBS 开始录制或推流时自动开始计时（默认关闭）。</summary>
    public bool AutoStartWithRecording { get; set; }

    /// <summary>OBS 录制与推流都停止时自动停止计时（默认关闭）。</summary>
    public bool AutoStopWithRecording { get; set; }

    public bool SoundOnOvertime { get; set; }

    /// <summary>托盘显示剩余/累计时间（默认开启，可关）。</summary>
    public bool TrayStatusEnabled { get; set; } = true;
}

/// <summary>一条计时记录。Id 递增，展示与 TSV 均按 Id 倒序（最新为 1）。</summary>
public sealed class TimerRecord
{
    public int Id { get; set; }
    public DateTime StartedAt { get; set; }
    public TimerMode Mode { get; set; }
    public int PlannedSeconds { get; set; }
    public int ActualSeconds { get; set; }
    public int OvertimeSeconds { get; set; }
}

/// <summary>某一时刻的只读快照，供页面、托盘、手机与叠加层共用。</summary>
public sealed record TimerSnapshot(
    TimerState State,
    TimerMode Mode,
    TimeSpan Elapsed,
    TimeSpan Planned,
    TimeSpan Remaining,
    TimeSpan Overtime,
    bool IsOvertime,
    bool IsPaused,
    string Display,
    string Secondary);

/// <summary>与 WPF 无关的矩形，便于纯逻辑单测。</summary>
public readonly record struct TimerRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Intersects(TimerRect other)
        => !IsEmpty && !other.IsEmpty
           && Left < other.Right && Right > other.Left
           && Top < other.Bottom && Bottom > other.Top;
}

/// <summary>显示器工作区（设备无关单位由调用方换算）。</summary>
public sealed record TimerScreen(int Index, TimerRect WorkArea, bool IsPrimary);
