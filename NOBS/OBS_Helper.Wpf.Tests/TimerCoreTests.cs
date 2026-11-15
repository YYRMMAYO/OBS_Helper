using OBS_Helper.Wpf.Services.Timing;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// 计时器纯逻辑（V3.6.0）：状态机、倒计时/顺计时、超时、格式化、TSV、显示器与窗口夹取。
/// 时间全部由测试注入，不依赖 Thread.Sleep。
/// </summary>
public class TimerCoreTests
{
    private static readonly DateTime T0 = new(2026, 11, 15, 12, 0, 0, DateTimeKind.Local);

    // ------------------------------------------------------------ 解析 / 格式化

    [Theory]
    [InlineData("900", 900)]
    [InlineData("15:00", 900)]
    [InlineData("1:02:03", 3723)]
    [InlineData(" 05:30 ", 330)]
    [InlineData("99:59:59", 359999)]
    [InlineData("1:99", 0)]
    [InlineData("1:2:99", 0)]
    [InlineData("abc", 0)]
    [InlineData("", 0)]
    public void ParseDuration_Works(string input, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), TimerCore.ParseDuration(input));
    }

    [Fact]
    public void ParseDuration_ClampsToMax()
    {
        Assert.Equal(TimeSpan.FromSeconds(TimerCore.MaxPlannedSeconds),
            TimerCore.ParseDuration("999:00:00"));
    }

    [Theory]
    [InlineData(0, false, "00:00")]
    [InlineData(65, false, "01:05")]
    [InlineData(3665, false, "01:01:05")]
    [InlineData(3665, true, "01:01:05")]
    [InlineData(90000, true, "25:00:00")]
    [InlineData(-5, false, "00:00")]
    public void FormatClock_Works(int seconds, bool showHours, string expected)
    {
        Assert.Equal(expected, TimerCore.FormatClock(TimeSpan.FromSeconds(seconds), showHours));
    }

    [Fact]
    public void FormatDuration_AndSignedOvertime()
    {
        Assert.Equal("02:03", TimerCore.FormatDuration(TimeSpan.FromSeconds(123)));
        Assert.Equal("01:02:03", TimerCore.FormatDuration(TimeSpan.FromSeconds(3723)));
        Assert.Equal("+00:35", TimerCore.FormatSignedOvertime(TimeSpan.FromSeconds(35)));
    }

    // ------------------------------------------------------------ 倒计时

    [Fact]
    public void CountDown_ReachesZero_ThenAccumulatesOvertime()
    {
        var machine = new TimerMachine
        {
            Mode = TimerMode.CountDown,
            Planned = TimeSpan.FromMinutes(15),
        };

        machine.Start(T0);

        var half = machine.Snapshot(T0.AddSeconds(30));
        Assert.Equal(TimerState.Running, half.State);
        Assert.Equal("14:30", half.Display);
        Assert.False(half.IsOvertime);
        Assert.Equal(870, (int)half.Remaining.TotalSeconds);

        machine.Tick(T0.AddMinutes(15));
        var zero = machine.Snapshot(T0.AddMinutes(15));
        Assert.Equal(TimerState.Finished, zero.State);
        Assert.Equal("00:00", zero.Display);
        Assert.Equal(0, (int)zero.Overtime.TotalSeconds);

        machine.Tick(T0.AddMinutes(15).AddSeconds(35));
        var over = machine.Snapshot(T0.AddMinutes(15).AddSeconds(35));
        Assert.Equal(TimerState.Finished, over.State);
        Assert.True(over.IsOvertime);
        Assert.Equal(35, (int)over.Overtime.TotalSeconds);
        Assert.Equal("+00:35", over.Secondary);
    }

    [Fact]
    public void CountDown_StopAfterFinish_RecordsPlannedAndOvertime()
    {
        var machine = new TimerMachine
        {
            Mode = TimerMode.CountDown,
            Planned = TimeSpan.FromSeconds(60),
        };
        machine.Start(T0);
        machine.Tick(T0.AddSeconds(70));

        var record = machine.Stop(T0.AddSeconds(75), nextId: 1);

        Assert.NotNull(record);
        Assert.Equal(1, record!.Id);
        Assert.Equal(T0, record.StartedAt);
        Assert.Equal(60, record.PlannedSeconds);
        Assert.Equal(60, record.ActualSeconds);
        Assert.Equal(15, record.OvertimeSeconds);
        Assert.Equal(TimerState.Idle, machine.State);
    }

    [Fact]
    public void CountDown_StopEarly_RecordsElapsed()
    {
        var machine = new TimerMachine
        {
            Mode = TimerMode.CountDown,
            Planned = TimeSpan.FromSeconds(60),
        };
        machine.Start(T0);

        var record = machine.Stop(T0.AddSeconds(20), nextId: 2);

        Assert.NotNull(record);
        Assert.Equal(20, record!.ActualSeconds);
        Assert.Equal(0, record.OvertimeSeconds);
    }

    // ------------------------------------------------------------ 顺计时

    [Fact]
    public void CountUp_KeepsRunning_AndMarksOvertime()
    {
        var machine = new TimerMachine
        {
            Mode = TimerMode.CountUp,
            Planned = TimeSpan.FromSeconds(60),
        };
        machine.Start(T0);

        var snapshot = machine.Snapshot(T0.AddSeconds(90));

        Assert.Equal(TimerState.Running, snapshot.State);
        Assert.True(snapshot.IsOvertime);
        Assert.Equal(90, (int)snapshot.Elapsed.TotalSeconds);
        Assert.Equal(30, (int)snapshot.Overtime.TotalSeconds);
        Assert.Equal("01:30", snapshot.Display);

        var record = machine.Stop(T0.AddSeconds(90), nextId: 3);
        Assert.Equal(90, record!.ActualSeconds);
        Assert.Equal(30, record.OvertimeSeconds);
    }

    [Fact]
    public void CountUp_ZeroPlan_NoOvertime()
    {
        var machine = new TimerMachine
        {
            Mode = TimerMode.CountUp,
            Planned = TimeSpan.Zero,
        };
        machine.Start(T0);
        Assert.False(machine.Snapshot(T0.AddHours(2)).IsOvertime);
    }

    // ------------------------------------------------------------ 暂停 / 继续

    [Fact]
    public void Pause_And_Resume_DoNotCountPausedTime()
    {
        var machine = new TimerMachine
        {
            Mode = TimerMode.CountDown,
            Planned = TimeSpan.FromSeconds(60),
        };
        machine.Start(T0);
        machine.Pause(T0.AddSeconds(10));
        Assert.Equal(10, (int)machine.ElapsedAt(T0.AddSeconds(70)).TotalSeconds);
        Assert.Equal(TimerState.Paused, machine.State);
        Assert.True(machine.Snapshot(T0.AddSeconds(70)).IsPaused);

        machine.Resume(T0.AddSeconds(70));
        Assert.Equal(15, (int)machine.ElapsedAt(T0.AddSeconds(75)).TotalSeconds);
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var machine = new TimerMachine
        {
            Mode = TimerMode.CountDown,
            Planned = TimeSpan.FromSeconds(60),
        };
        machine.Start(T0);
        machine.Reset();

        Assert.Equal(TimerState.Idle, machine.State);
        Assert.Equal(TimeSpan.Zero, machine.ElapsedAt(T0.AddSeconds(10)));
        Assert.Null(machine.Stop(T0.AddSeconds(10), 1));
    }

    // ------------------------------------------------------------ 记录 / TSV

    [Fact]
    public void Tsv_OrdersNewestFirst_AndEscapes()
    {
        var records = new[]
        {
            new TimerRecord
            {
                Id = 1, StartedAt = T0, Mode = TimerMode.CountDown,
                PlannedSeconds = 60, ActualSeconds = 60, OvertimeSeconds = 0,
            },
            new TimerRecord
            {
                Id = 2, StartedAt = T0.AddMinutes(1), Mode = TimerMode.CountUp,
                PlannedSeconds = 30, ActualSeconds = 45, OvertimeSeconds = 15,
            },
        };

        var tsv = TimerCore.BuildTsv(records);
        var lines = tsv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("序号\t开始时间\t模式\t计划时长\t实际用时\t超时", lines[0]);
        Assert.StartsWith("1\t", lines[1]);
        Assert.Contains("CountUp", lines[1]);
        Assert.Contains("+00:15", lines[1]);
        Assert.StartsWith("2\t", lines[2]);
        Assert.Contains("CountDown", lines[2]);
        Assert.EndsWith("\t-", lines[2]);
    }

    [Fact]
    public void OrderForDisplay_IsDescendingById()
    {
        var ordered = TimerCore.OrderForDisplay(new[]
        {
            new TimerRecord { Id = 7 },
            new TimerRecord { Id = 12 },
            new TimerRecord { Id = 3 },
        });
        Assert.Equal(new[] { 12, 7, 3 }, ordered.Select(r => r.Id));
    }

    // ------------------------------------------------------------ clamp / 几何

    [Fact]
    public void ClampOpacity_And_FontScale()
    {
        Assert.Equal(TimerCore.MinOpacity, TimerCore.ClampOpacity(-1));
        Assert.Equal(TimerCore.MaxOpacity, TimerCore.ClampOpacity(5));
        Assert.Equal(TimerCore.DefaultOpacity, TimerCore.ClampOpacity(double.NaN));

        Assert.Equal(TimerCore.MinFontScale, TimerCore.ClampFontScale(0));
        Assert.Equal(TimerCore.MaxFontScale, TimerCore.ClampFontScale(99));
        Assert.Equal(TimerCore.DefaultFontScale, TimerCore.ClampFontScale(double.NaN));
    }

    private static readonly TimerScreen[] Screens =
    {
        new(0, new TimerRect(0, 0, 1920, 1080), IsPrimary: true),
        new(1, new TimerRect(1920, 0, 1920, 1080), IsPrimary: false),
    };

    [Fact]
    public void NormalizeMonitorIndex_FallsBackToPrimary()
    {
        Assert.Equal(0, TimerCore.NormalizeMonitorIndex(-1, Screens));
        Assert.Equal(1, TimerCore.NormalizeMonitorIndex(1, Screens));
        Assert.Equal(0, TimerCore.NormalizeMonitorIndex(9, Screens));
        Assert.Equal(0, TimerCore.NormalizeMonitorIndex(0, Array.Empty<TimerScreen>()));
    }

    [Fact]
    public void ResolveBounds_DefaultUsesWorkAreaOffset()
    {
        var bounds = TimerCore.ResolveBounds(null, Screens, preferredIndex: 1);
        Assert.Equal(1920 + 24, bounds.Left);
        Assert.Equal(24, bounds.Top);
        Assert.Equal(TimerCore.DefaultWindowWidth, bounds.Width);
    }

    [Fact]
    public void ResolveBounds_OffScreenFallsBackToPrimary()
    {
        var saved = new TimerRect(5000, 5000, 360, 160);
        var bounds = TimerCore.ResolveBounds(saved, Screens, preferredIndex: 1);
        Assert.Equal(24, bounds.Left);
        Assert.Equal(24, bounds.Top);
    }

    [Fact]
    public void ResolveBounds_ClampsOverflow()
    {
        var saved = new TimerRect(100, 100, 9999, 9999);
        var bounds = TimerCore.ResolveBounds(saved, Screens, preferredIndex: 0);
        Assert.Equal(1920, bounds.Width);
        Assert.Equal(1080, bounds.Height);
        Assert.Equal(0, bounds.Left);
        Assert.Equal(0, bounds.Top);
    }

    [Fact]
    public void FitToWorkArea_EnforcesMinimumSize()
    {
        var result = TimerCore.FitToWorkArea(new TimerRect(-50, -50, 10, 10),
            new TimerRect(0, 0, 1920, 1080));
        Assert.Equal(TimerCore.MinWindowWidth, result.Width);
        Assert.Equal(TimerCore.MinWindowHeight, result.Height);
        Assert.Equal(0, result.Left);
        Assert.Equal(0, result.Top);
    }
}
