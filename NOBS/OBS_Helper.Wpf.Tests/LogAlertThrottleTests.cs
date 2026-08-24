using OBS_Helper.Wpf.Services.Shell;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

public class LogAlertThrottleTests
{
    private static DateTime T(int addSeconds) =>
        new DateTime(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc).AddSeconds(addSeconds);

    [Fact]
    public void FirstHit_Passes()
    {
        var t = new LogAlertThrottle();
        Assert.True(t.ShouldNotify("LOG-ENC-OVERLOAD", T(0)));
    }

    [Fact]
    public void SameCode_WithinWindow_Suppressed()
    {
        var t = new LogAlertThrottle();
        Assert.True(t.ShouldNotify("A", T(0)));
        Assert.False(t.ShouldNotify("A", T(30)));
        Assert.False(t.ShouldNotify("A", T(89)));
    }

    [Fact]
    public void SameCode_AfterWindow_PassesAgain()
    {
        var t = new LogAlertThrottle();
        Assert.True(t.ShouldNotify("A", T(0)));
        Assert.True(t.ShouldNotify("A", T(91)));
    }

    [Fact]
    public void DifferentCodes_IndependentWindows_ButShareHourCap()
    {
        var t = new LogAlertThrottle();
        Assert.True(t.ShouldNotify("A", T(0)));
        Assert.True(t.ShouldNotify("B", T(1)));
        // 不同码互不抑制
        for (var i = 0; i < LogAlertThrottle.MaxPerHour - 2; i++)
            Assert.True(t.ShouldNotify($"C{i}", T(2 + i)));
        // 达到每小时上限后，即使新码也拦截
        Assert.False(t.ShouldNotify("Z", T(100)));
    }

    [Fact]
    public void HourWindow_SlidesOut_AllowsAgain()
    {
        var t = new LogAlertThrottle();
        for (var i = 0; i < LogAlertThrottle.MaxPerHour; i++)
            Assert.True(t.ShouldNotify($"C{i}", T(i)));
        Assert.False(t.ShouldNotify("NEW", T(LogAlertThrottle.MaxPerHour)));
        // 一小时窗口滑出最早一批后恢复放行
        Assert.True(t.ShouldNotify("NEW", T(3600 + LogAlertThrottle.MaxPerHour)));
    }

    [Fact]
    public void Reset_ClearsAllState()
    {
        var t = new LogAlertThrottle();
        Assert.True(t.ShouldNotify("A", T(0)));
        t.Reset();
        Assert.True(t.ShouldNotify("A", T(1)));
    }
}
