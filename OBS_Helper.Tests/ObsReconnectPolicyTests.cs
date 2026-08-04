using OBS_Helper.Client.Services.Obs;

namespace OBS_Helper.Tests;

public class ObsReconnectPolicyTests
{
    private readonly ObsReconnectPolicy _policy = new();

    [Fact]
    public void DelayFor_FirstAttempt_IsBaseDelay()
        => Assert.Equal(_policy.BaseDelay, _policy.DelayFor(1));

    [Fact]
    public void DelayFor_ExponentialBackoff()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), _policy.DelayFor(2));
        Assert.Equal(TimeSpan.FromSeconds(4), _policy.DelayFor(3));
        Assert.Equal(TimeSpan.FromSeconds(8), _policy.DelayFor(4));
    }

    [Fact]
    public void DelayFor_CappedAtMaxDelay()
    {
        Assert.Equal(_policy.MaxDelay, _policy.DelayFor(20));
        Assert.Equal(_policy.MaxDelay, _policy.DelayFor(100));
    }

    [Fact]
    public void ShouldRetry_WithinMax()
    {
        Assert.True(_policy.ShouldRetry(1));
        Assert.True(_policy.ShouldRetry(_policy.MaxAttempts));
    }

    [Fact]
    public void ShouldRetry_ExceedsMax()
        => Assert.False(_policy.ShouldRetry(_policy.MaxAttempts + 1));
}
