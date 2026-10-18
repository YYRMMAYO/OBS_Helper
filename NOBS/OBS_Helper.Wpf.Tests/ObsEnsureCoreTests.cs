using System.Net.Sockets;
using System.Net.WebSockets;
using OBS_Helper.Wpf.Localization;
using OBS_Helper.Wpf.Services.Obs;
using OBS_Helper.Wpf.Services.ObsConfig;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// V3.1.0 一键录制：连接错误分类、重试策略、首次同意与倒计时归一化。
/// </summary>
public class ObsEnsureCoreTests
{
    [Fact]
    public void UnauthorizedMapsToAuthRequiredWhenNoPassword()
        => Assert.Equal(EnsureConnectionOutcome.AuthRequired,
            ObsEnsureCore.Classify(new UnauthorizedAccessException("need password"),
                authRequired: true, passwordProvided: false));

    [Fact]
    public void UnauthorizedMapsToAuthFailedWhenPasswordProvided()
        => Assert.Equal(EnsureConnectionOutcome.AuthFailed,
            ObsEnsureCore.Classify(new UnauthorizedAccessException("wrong"),
                authRequired: true, passwordProvided: true));

    [Fact]
    public void SocketExceptionMapsToPortClosedEvenWhenWrapped()
    {
        var wrapped = new WebSocketException("connect failed", new SocketException((int)SocketError.ConnectionRefused));
        Assert.Equal(EnsureConnectionOutcome.PortClosed,
            ObsEnsureCore.Classify(wrapped, authRequired: false, passwordProvided: false));
    }

    [Fact]
    public void WebSocketExceptionMapsToHandshakeFailed()
        => Assert.Equal(EnsureConnectionOutcome.HandshakeFailed,
            ObsEnsureCore.Classify(new WebSocketException("handshake"), false, false));

    [Fact]
    public void CancellationAndTimeoutAreDistinct()
    {
        Assert.Equal(EnsureConnectionOutcome.Cancelled,
            ObsEnsureCore.Classify(new OperationCanceledException(), false, false));
        Assert.Equal(EnsureConnectionOutcome.Timeout,
            ObsEnsureCore.Classify(new TimeoutException(), false, false));
        Assert.Equal(EnsureConnectionOutcome.Error, ObsEnsureCore.Classify(null, false, false));
    }

    [Theory]
    [InlineData(EnsureConnectionOutcome.PortClosed, true)]
    [InlineData(EnsureConnectionOutcome.HandshakeFailed, true)]
    [InlineData(EnsureConnectionOutcome.Error, true)]
    [InlineData(EnsureConnectionOutcome.AuthFailed, false)]
    [InlineData(EnsureConnectionOutcome.AuthRequired, false)]
    [InlineData(EnsureConnectionOutcome.ObsExited, false)]
    [InlineData(EnsureConnectionOutcome.Cancelled, false)]
    public void RetryPolicy(EnsureConnectionOutcome outcome, bool expected)
        => Assert.Equal(expected, ObsEnsureCore.ShouldRetry(outcome));

    [Theory]
    [InlineData(EnsureConnectionOutcome.AuthRequired, true)]
    [InlineData(EnsureConnectionOutcome.AuthFailed, true)]
    [InlineData(EnsureConnectionOutcome.ObsNotRunning, true)]
    [InlineData(EnsureConnectionOutcome.ObsExited, true)]
    [InlineData(EnsureConnectionOutcome.PortClosed, false)]
    [InlineData(EnsureConnectionOutcome.Timeout, false)]
    public void UserActionPolicy(EnsureConnectionOutcome outcome, bool expected)
        => Assert.Equal(expected, ObsEnsureCore.NeedsUserAction(outcome));

    [Fact]
    public void EveryFailureOutcomeHasAnErrorKey()
    {
        foreach (var outcome in new[]
                 {
                     EnsureConnectionOutcome.ObsNotRunning,
                     EnsureConnectionOutcome.ObsExited,
                     EnsureConnectionOutcome.PortClosed,
                     EnsureConnectionOutcome.HandshakeFailed,
                     EnsureConnectionOutcome.AuthRequired,
                     EnsureConnectionOutcome.AuthFailed,
                     EnsureConnectionOutcome.Timeout,
                     EnsureConnectionOutcome.Cancelled,
                     EnsureConnectionOutcome.Error
                 })
        {
            var key = ObsEnsureCore.ErrorKey(outcome);
            Assert.StartsWith("simple.error.", key);
            // 文案表里必须真的有这个键（缺键时 Strings.T 会原样返回键名）
            Assert.NotEqual(key, Strings.T(key));
        }
    }

    // ------------------------------------------------------------ 首次同意 / 倒计时

    [Theory]
    [InlineData("allow", SimpleRecordLaunchConsent.Allow)]
    [InlineData("ALLOW", SimpleRecordLaunchConsent.Allow)]
    [InlineData("deny", SimpleRecordLaunchConsent.Deny)]
    [InlineData("ask", SimpleRecordLaunchConsent.Ask)]
    [InlineData("", SimpleRecordLaunchConsent.Ask)]
    [InlineData("garbage", SimpleRecordLaunchConsent.Ask)]
    [InlineData(null, SimpleRecordLaunchConsent.Ask)]
    public void ConsentParsingIsDirtyProof(string? key, SimpleRecordLaunchConsent expected)
        => Assert.Equal(expected, SimpleRecordingCore.ParseConsent(key));

    [Fact]
    public void ConsentKeysRoundTrip()
    {
        foreach (var value in new[]
                 {
                     SimpleRecordLaunchConsent.Ask,
                     SimpleRecordLaunchConsent.Allow,
                     SimpleRecordLaunchConsent.Deny
                 })
        {
            Assert.Equal(value, SimpleRecordingCore.ParseConsent(SimpleRecordingCore.ConsentKey(value)));
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 3)]
    [InlineData(5, 5)]
    [InlineData(10, 10)]
    [InlineData(7, 0)]
    [InlineData(-3, 0)]
    [InlineData(999, 0)]
    public void CountdownNormalization(int input, int expected)
        => Assert.Equal(expected, SimpleRecordingCore.NormalizeCountdownSeconds(input));

    // ------------------------------------------------------------ 就绪判定新分支

    [Fact]
    public void CheckAllowsAutoConnectWhenObsRunsButNotConnected()
    {
        var check = SimpleRecordingCore.Check(
            viaWebSocket: false, canWriteFiles: false, obsProcessRunning: true,
            streaming: false, recording: false, failCount: 0, warnCount: 0,
            canAutoConnect: true);

        Assert.True(check.Ready);
        Assert.True(check.WillConnectObs);
        Assert.False(check.WillLaunchObs);
        Assert.False(string.IsNullOrWhiteSpace(check.Hint));
    }

    [Fact]
    public void CheckAllowsAutoLaunchWhenObsNotRunning()
    {
        var check = SimpleRecordingCore.Check(
            viaWebSocket: false, canWriteFiles: false, obsProcessRunning: false,
            streaming: false, recording: false, failCount: 0, warnCount: 0,
            canAutoLaunch: true, launchAllowed: true);

        Assert.True(check.Ready);
        Assert.True(check.WillLaunchObs);
        Assert.False(check.WillConnectObs);
    }

    [Fact]
    public void CheckStillBlocksWhenLaunchNotAllowed()
    {
        var check = SimpleRecordingCore.Check(
            viaWebSocket: false, canWriteFiles: false, obsProcessRunning: false,
            streaming: false, recording: false, failCount: 0, warnCount: 0,
            canAutoLaunch: true, launchAllowed: false);

        Assert.False(check.Ready);
        Assert.False(string.IsNullOrWhiteSpace(check.BlockReason));
    }

    [Fact]
    public void PreflightFailureStillBlocksAutoPaths()
    {
        var check = SimpleRecordingCore.Check(
            viaWebSocket: false, canWriteFiles: false, obsProcessRunning: false,
            streaming: false, recording: false, failCount: 2, warnCount: 0,
            canAutoLaunch: true, launchAllowed: true);

        Assert.False(check.Ready);
        Assert.Contains("2", check.BlockReason!);
    }

    [Fact]
    public void StreamingAlwaysBlocksEvenWithAutoConnect()
    {
        var check = SimpleRecordingCore.Check(
            viaWebSocket: false, canWriteFiles: true, obsProcessRunning: true,
            streaming: true, recording: false, failCount: 0, warnCount: 0,
            canAutoConnect: true);

        Assert.False(check.Ready);
        Assert.Equal(Strings.T("simple.blocked.streaming"), check.BlockReason);
    }

    [Fact]
    public void LegacyDefaultsPreserveOldBehaviour()
    {
        // 不传新参数时必须与 V2.9.4 完全一致：没通道就阻断，不会被「自动连接」放行
        var check = SimpleRecordingCore.Check(
            viaWebSocket: false, canWriteFiles: false, obsProcessRunning: true,
            streaming: false, recording: false, failCount: 0, warnCount: 0);

        Assert.False(check.Ready);
        Assert.False(check.WillConnectObs);
        Assert.False(check.WillLaunchObs);
    }
}
