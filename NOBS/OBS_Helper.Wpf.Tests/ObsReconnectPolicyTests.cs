using OBS_Helper.Wpf.Services.Obs;

namespace OBS_Helper.Wpf.Tests;

public class ObsReconnectPolicyTests
{
    [Fact]
    public void FirstAttempt_UsesBaseDelay()
    {
        var p = new ObsReconnectPolicy { BaseDelay = TimeSpan.FromSeconds(1) };
        Assert.Equal(TimeSpan.FromSeconds(1), p.DelayFor(1));
    }

    [Fact]
    public void Delay_DoublesExponentially()
    {
        var p = new ObsReconnectPolicy { BaseDelay = TimeSpan.FromSeconds(1) };
        Assert.Equal(TimeSpan.FromSeconds(2), p.DelayFor(2));
        Assert.Equal(TimeSpan.FromSeconds(4), p.DelayFor(3));
        Assert.Equal(TimeSpan.FromSeconds(8), p.DelayFor(4));
    }

    [Fact]
    public void Delay_CappedAtMaxDelay()
    {
        var p = new ObsReconnectPolicy { BaseDelay = TimeSpan.FromSeconds(1), MaxDelay = TimeSpan.FromSeconds(30) };
        Assert.Equal(TimeSpan.FromSeconds(30), p.DelayFor(20));
    }

    [Fact]
    public void ShouldRetry_RespectsMaxAttempts()
    {
        var p = new ObsReconnectPolicy { MaxAttempts = 8 };
        Assert.True(p.ShouldRetry(8));
        Assert.False(p.ShouldRetry(9));
    }
}
