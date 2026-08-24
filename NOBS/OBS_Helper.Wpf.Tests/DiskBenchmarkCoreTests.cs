using OBS_Helper.Wpf.Services.Tools;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

public class DiskBenchmarkCoreTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Verdict_InvalidWriteSpeed_Fails(double writeMbps)
    {
        var v = DiskBenchmarkCore.Verdict(writeMbps, 20000);
        Assert.False(v.Pass);
        Assert.Equal("fail", v.Status);
        Assert.False(string.IsNullOrEmpty(v.Advice));
    }

    [Fact]
    public void RequiredMbps_AppliesHeadroom()
    {
        // 20000kbps = 2.5MB/s，×1.5 冗余 = 3.75MB/s
        Assert.Equal(3.75, DiskBenchmarkCore.RequiredMbps(20_000), precision: 9);
        // 异常大码率被截断
        Assert.Equal(DiskBenchmarkInput.MaxBitrateKbps / 8000.0 * 1.5,
            DiskBenchmarkCore.RequiredMbps(int.MaxValue), precision: 6);
    }

    [Fact]
    public void Verdict_PlentyOfMargin_Passes()
    {
        // 需要 3.75MB/s，实测 500MB/s（>2 倍）→ 通过
        var v = DiskBenchmarkCore.Verdict(500, 20_000);
        Assert.True(v.Pass);
        Assert.Equal("ok", v.Status);
    }

    [Fact]
    public void Verdict_Marginal_Warns()
    {
        // 需要 3.75MB/s，实测 5MB/s（1~2 倍之间）→ 警告
        var v = DiskBenchmarkCore.Verdict(5, 20_000);
        Assert.False(v.Pass);
        Assert.Equal("warn", v.Status);
        Assert.Contains("余量偏小", v.Advice);
    }

    [Fact]
    public void Verdict_BelowRequirement_Fails()
    {
        // 需要 3.75MB/s，实测 2MB/s → 不通过
        var v = DiskBenchmarkCore.Verdict(2, 20_000);
        Assert.False(v.Pass);
        Assert.Equal("fail", v.Status);
        Assert.Contains("SSD", v.Advice);
    }
}
