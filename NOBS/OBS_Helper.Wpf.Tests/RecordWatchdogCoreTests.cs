using OBS_Helper.Wpf.Services.Shell;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

public class RecordWatchdogCoreTests
{
    [Fact]
    public void NotRecording_NoAlerts()
    {
        var d = RecordWatchdogCore.Evaluate(false, connected: false, reconnectedNow: false,
            recordActiveNow: false, heartbeatFailures: 99);
        Assert.False(d.Alert);
    }

    [Fact]
    public void ConnectionLostWhileRecording_Alerts()
    {
        var d = RecordWatchdogCore.Evaluate(true, connected: false, reconnectedNow: false,
            recordActiveNow: true, heartbeatFailures: 0);
        Assert.True(d.Alert);
        Assert.Equal(WatchdogAlertKind.ConnectionLostWhileRecording, d.Kind);
        Assert.Contains("连接", d.Title);
    }

    [Fact]
    public void ConnectionLostWhileNotRecording_NoAlert()
    {
        var d = RecordWatchdogCore.Evaluate(false, connected: false, reconnectedNow: false,
            recordActiveNow: false, heartbeatFailures: 0);
        Assert.False(d.Alert);
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(10, true)]
    public void HeartbeatFailures_Threshold(int failures, bool expected)
    {
        var d = RecordWatchdogCore.Evaluate(true, connected: true, reconnectedNow: false,
            recordActiveNow: true, heartbeatFailures: failures);
        Assert.Equal(expected, d.Alert);
        if (expected)
            Assert.Equal(WatchdogAlertKind.HeartbeatTimeout, d.Kind);
    }

    [Fact]
    public void Reconnected_RecordingStillActive_NoAlert()
    {
        var d = RecordWatchdogCore.Evaluate(true, connected: true, reconnectedNow: true,
            recordActiveNow: true, heartbeatFailures: 0);
        Assert.False(d.Alert);
    }

    [Fact]
    public void Reconnected_RecordingLost_AlertsWithRecoveryHint()
    {
        var d = RecordWatchdogCore.Evaluate(true, connected: true, reconnectedNow: true,
            recordActiveNow: false, heartbeatFailures: 0);
        Assert.True(d.Alert);
        Assert.Equal(WatchdogAlertKind.RecordingLostAfterReconnect, d.Kind);
        Assert.Contains("重新开始", d.Message);
    }

    [Fact]
    public void ReconnectSignal_TakesPriorityOverHeartbeatTimeout()
    {
        // 重连瞬间心跳还没跑，失败计数可能残留；重连结论应优先
        var d = RecordWatchdogCore.Evaluate(true, connected: true, reconnectedNow: true,
            recordActiveNow: false, heartbeatFailures: 5);
        Assert.Equal(WatchdogAlertKind.RecordingLostAfterReconnect, d.Kind);
    }
}
