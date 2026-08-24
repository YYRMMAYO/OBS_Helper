using OBS_Helper.Wpf.Services.Tools;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

public class BandwidthAdvisorCoreTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Recommend_NonPositive_IsNotViable(double upload)
    {
        var r = BandwidthAdvisorCore.Recommend(upload);
        Assert.False(r.Viable);
        Assert.Equal(0, r.BitrateKbps);
        Assert.False(string.IsNullOrEmpty(r.Advice));
    }

    [Fact]
    public void Recommend_TooLowBandwidth_IsNotViable()
    {
        // 2 Mbps * 0.65 = 1300kbps < 1500kbps 下限
        var r = BandwidthAdvisorCore.Recommend(2.0);
        Assert.False(r.Viable);
    }

    [Fact]
    public void Recommend_HighBandwidth_TopTier()
    {
        // 20 Mbps -> safe 12800kbps -> 8000kbps 1080p60
        var r = BandwidthAdvisorCore.Recommend(20);
        Assert.True(r.Viable);
        Assert.Equal(8000, r.BitrateKbps);
        Assert.Equal("1920x1080", r.Resolution);
        Assert.Equal(60, r.Fps);
    }

    [Fact]
    public void Recommend_MidBandwidth_1080p30()
    {
        // 8 Mbps -> safe 5200kbps -> 4500 档 1080p30
        var r = BandwidthAdvisorCore.Recommend(8);
        Assert.True(r.Viable);
        Assert.Equal(4500, r.BitrateKbps);
        Assert.Equal("1920x1080", r.Resolution);
        Assert.Equal(30, r.Fps);
    }

    [Fact]
    public void RequiredUpload_AppliesHeadroom()
    {
        // 3 路 × 6000kbps × 1.2 = 21600kbps = 21.6Mbps
        Assert.Equal(21.6, BandwidthAdvisorCore.RequiredUploadMbps(3, 6000), precision: 9);
        // 默认冗余系数常量
        Assert.Equal(1.2, BandwidthAdvisorCore.MultiStreamHeadroom, precision: 9);
    }

    [Fact]
    public void CanSustain_BoundaryIsInclusive()
    {
        // 恰好等于所需值：判定为可承载（含边界）
        Assert.True(BandwidthAdvisorCore.CanSustain(7.2, 1, 6000));   // 6*1.2=7.2
        Assert.False(BandwidthAdvisorCore.CanSustain(7.19, 1, 6000));
        Assert.False(BandwidthAdvisorCore.CanSustain(0, 1, 6000));     // 无效带宽不可承载
    }

    [Fact]
    public void DescribeMultiStream_InvalidInputs_ReturnsHint()
    {
        Assert.Contains("请填写", BandwidthAdvisorCore.DescribeMultiStream(10, 0, 6000));
        Assert.Contains("请填写", BandwidthAdvisorCore.DescribeMultiStream(10, 2, 0));
    }

    [Fact]
    public void DescribeMultiStream_SufficientAndInsufficient()
    {
        var ok = BandwidthAdvisorCore.DescribeMultiStream(15, 2, 6000);   // 需 14.4，余 0.6 → 够但偏小
        Assert.Contains("可以承载", ok);
        Assert.Contains("余量偏小", ok);

        var tight = BandwidthAdvisorCore.DescribeMultiStream(20, 2, 6000); // 余 5.6 → 正常
        Assert.Contains("可以承载", tight);
        Assert.DoesNotContain("余量偏小", tight);

        var bad = BandwidthAdvisorCore.DescribeMultiStream(10, 2, 6000);   // 需 14.4 > 10
        Assert.Contains("不够用", bad);
        Assert.Contains("Restream", bad);
    }
}
