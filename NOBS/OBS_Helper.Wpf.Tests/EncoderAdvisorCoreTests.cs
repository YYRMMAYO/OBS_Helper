using OBS_Helper.Wpf.Services.Tools;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

public class EncoderAdvisorCoreTests
{
    [Theory]
    [InlineData("NVIDIA GeForce RTX 4070", "NVIDIA")]
    [InlineData("NVIDIA GeForce GTX 1660", "NVIDIA")]
    [InlineData("AMD Radeon RX 6700 XT", "AMD")]
    [InlineData("Intel(R) UHD Graphics 730", "Intel")]
    public void DetectVendor_KnownNames(string gpu, string expected)
        => Assert.Equal(expected, EncoderAdvisorCore.DetectVendor(gpu));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("某某显卡")]
    public void DetectVendor_Unknown_ReturnsNull(string? gpu)
        => Assert.Null(EncoderAdvisorCore.DetectVendor(gpu));

    [Theory]
    [InlineData("RTX 4090", true)]
    [InlineData("GeForce RTX 5070", true)]
    [InlineData("RTX 3090", false)]
    [InlineData("GTX 1080", false)]
    public void NvencAv1Capable_ByGeneration(string gpu, bool expected)
        => Assert.Equal(expected, EncoderAdvisorCore.NvencAv1Capable(gpu));

    [Fact]
    public void Recommend_Nvidia40xx_IncludesAv1RecordingAdvice()
    {
        var r = EncoderAdvisorCore.Recommend("NVIDIA GeForce RTX 4070", EncoderAdvisorCore.Scenario.Record, dualEncode: false);
        Assert.True(r.Av1Capable);
        Assert.Contains("AV1 CQP 22", r.Advice);
    }

    [Fact]
    public void Recommend_Rtx30_StreamUsesP5()
    {
        var r = EncoderAdvisorCore.Recommend("NVIDIA GeForce RTX 3070", EncoderAdvisorCore.Scenario.Stream, dualEncode: false);
        Assert.False(r.Av1Capable);
        Assert.Contains("P5", r.Advice);
    }

    [Fact]
    public void Recommend_Gtx_StreamUsesP4()
    {
        var r = EncoderAdvisorCore.Recommend("NVIDIA GeForce GTX 1660 Super", EncoderAdvisorCore.Scenario.Stream, dualEncode: false);
        Assert.Contains("P4", r.Advice);
        Assert.DoesNotContain("P5", r.Advice);
    }

    [Fact]
    public void Recommend_DualEncode_MentionsGpuBudget()
    {
        var r = EncoderAdvisorCore.Recommend("NVIDIA GeForce RTX 3060", EncoderAdvisorCore.Scenario.Both, dualEncode: true);
        Assert.Contains("10%", r.Advice);
        Assert.Contains("15%", r.Advice);
    }

    [Fact]
    public void Recommend_UnknownGpu_FallsBackToGenericAdvice()
    {
        var r = EncoderAdvisorCore.Recommend(null, EncoderAdvisorCore.Scenario.Both, dualEncode: false);
        Assert.Equal("未知", r.Vendor);
        Assert.False(string.IsNullOrEmpty(r.Advice));
    }
}
