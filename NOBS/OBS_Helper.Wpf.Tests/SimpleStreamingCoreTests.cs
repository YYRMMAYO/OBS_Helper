using OBS_Helper.Wpf.Services.ObsConfig;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// 「简单开播」（V3.0 / D2）判定逻辑的回归测试。
///
/// 重点锁三件事：
/// ① 判定顺序（已经播了 → 没连上 → 没填服务 → 没填密钥 → 才能播）；
/// ② 「读不到推流设置」与「没配置」必须给**不同**的文案（否则用户按错的提示去排查）；
/// ③ 提醒不阻断（拥塞/丢帧/同时录制都只是警告，不能把「能播」判成「不能播」）。
/// </summary>
public class SimpleStreamingCoreTests
{
    private static readonly SimpleStreamServiceInfo Configured = new(HasServer: true, HasKey: true, "rtmp_custom");
    private static readonly SimpleStreamServiceInfo NoKey = new(HasServer: true, HasKey: false, "rtmp_custom");
    private static readonly SimpleStreamServiceInfo NoServer = new(HasServer: false, HasKey: true, "rtmp_custom");

    [Fact]
    public void Check_ConfigComplete_IsReadyWithoutWarning()
    {
        var check = SimpleStreamingCore.Check(connected: true, streaming: false, recording: false, service: Configured);
        Assert.True(check.Ready);
        Assert.Null(check.BlockReason);
        Assert.Null(check.Warning);
    }

    [Fact]
    public void Check_AlreadyStreaming_BlocksFirst()
    {
        // 即使一切就绪，已经在推流也不能再推 —— 且这条优先级最高
        var check = SimpleStreamingCore.Check(true, streaming: true, false, service: Configured);
        Assert.False(check.Ready);
        Assert.Contains("已经在推流", check.BlockReason!);
    }

    [Fact]
    public void Check_NotConnected_Blocks()
    {
        var check = SimpleStreamingCore.Check(connected: false, streaming: false, recording: false, service: Configured);
        Assert.False(check.Ready);
        Assert.Contains("没有连上", check.BlockReason!);
    }

    [Fact]
    public void Check_MissingStreamKey_BlocksWithActionableText()
    {
        var check = SimpleStreamingCore.Check(true, false, false, NoKey);
        Assert.False(check.Ready);
        Assert.Contains("串流密钥", check.BlockReason!);
    }

    [Fact]
    public void Check_MissingServer_Blocks()
    {
        var check = SimpleStreamingCore.Check(true, false, false, NoServer);
        Assert.False(check.Ready);
        Assert.Contains("服务器", check.BlockReason!);
    }

    /// <summary>
    /// 「读不到设置」与「没配置」必须给**不同**的处理：
    /// 读不到只是**提醒**（不阻断 —— D2 的红线是「只读取 + 校验 + 提示 + 调 StartStream」，
    /// 而「读不到」不等于「没配置」，真有问题时 OBS 自己会给出准确错误）；
    /// 明确的「没填密钥」才阻断。
    /// </summary>
    [Fact]
    public void Check_UnknownServiceInfo_WarnsButDoesNotBlock()
    {
        var unknown = SimpleStreamingCore.Check(true, false, false, service: null);
        var missing = SimpleStreamingCore.Check(true, false, false, NoKey);

        Assert.True(unknown.Ready);
        Assert.Null(unknown.BlockReason);
        Assert.Contains("读不到", unknown.Warning!);

        Assert.False(missing.Ready);
        Assert.NotNull(missing.BlockReason);
    }

    [Theory]
    [InlineData(0.9, 0.0, "拥塞")]
    [InlineData(0.0, 0.2, "丢帧")]
    public void Check_Risks_WarnButStillReady(double congestion, double dropped, string expectedHint)
    {
        var check = SimpleStreamingCore.Check(true, false, false, Configured, congestion, dropped);
        Assert.True(check.Ready);
        Assert.Null(check.BlockReason);
        Assert.Contains(expectedHint, check.Warning!);
    }

    [Fact]
    public void Check_RecordingRunning_WarnsButStillReady()
    {
        var check = SimpleStreamingCore.Check(true, false, recording: true, Configured);
        Assert.True(check.Ready);
        Assert.NotNull(check.Warning);
        Assert.Contains("录制", check.Warning!);
    }

    /// <summary>阈值以下不该报警（否则警告会变成噪音，用户就学会无视它了）。</summary>
    [Fact]
    public void Check_BelowThresholds_NoWarning()
    {
        var check = SimpleStreamingCore.Check(true, false, false, Configured, congestion: 0.05, droppedRatio: 0.001);
        Assert.True(check.Ready);
        Assert.Null(check.Warning);
    }

    [Theory]
    [InlineData(SimpleStreamStep.Streaming, true)]
    [InlineData(SimpleStreamStep.Starting, true)]
    [InlineData(SimpleStreamStep.Ready, false)]
    [InlineData(SimpleStreamStep.Stopped, false)]
    [InlineData(SimpleStreamStep.Blocked, false)]
    public void IsStopAction_MatchesState(SimpleStreamStep step, bool expected)
        => Assert.Equal(expected, SimpleStreamingCore.IsStopAction(step));

    /// <summary>码率估算：样本不足时返回 0（界面显示「—」），而不是拿两三秒的数据装成稳定值。</summary>
    [Fact]
    public void EstimateKbps_RequiresEnoughSample()
    {
        Assert.Equal(0, SimpleStreamingCore.EstimateKbps(bytesSent: 10_000_000, elapsed: TimeSpan.FromSeconds(1)));
        Assert.Equal(0, SimpleStreamingCore.EstimateKbps(bytesSent: 0, elapsed: TimeSpan.FromMinutes(1)));

        // 10 秒发了 7.5MB → 7_500_000 × 8 ÷ 1024 ÷ 10 ≈ 5859 kbps
        // （与「剩余可录」估算同一口径：这里的 kbps 按 1024 进制算，全项目统一）
        var kbps = SimpleStreamingCore.EstimateKbps(7_500_000, TimeSpan.FromSeconds(10));
        Assert.InRange(kbps, 5800, 5900);
    }
}
