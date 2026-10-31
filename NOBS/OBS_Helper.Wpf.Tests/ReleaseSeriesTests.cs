using System.Text.Json;
using OBS_Helper.Wpf.Services;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// X 系列版本规则：<c>X</c> + <c>MMdd</c> 日期码；同一天再版追加 <c>Y1</c>、<c>Y2</c>…
/// 更新检测先比日期，日期相同再比 <c>Yn</c> 序号；V / X 两个通道绝不互相提示。
/// </summary>
public class ReleaseSeriesTests
{
    [Theory]
    [InlineData(3, 5, 0, ReleaseSeries.V)]
    [InlineData(3, 4, 2, ReleaseSeries.V)]
    [InlineData(101, 0, 0, ReleaseSeries.X)]   // X0101
    [InlineData(930, 0, 0, ReleaseSeries.X)]   // X0930
    [InlineData(1031, 0, 0, ReleaseSeries.X)]  // X1031
    [InlineData(1101, 0, 0, ReleaseSeries.X)]  // X1101
    [InlineData(1031, 2, 0, ReleaseSeries.X)]  // X1031Y2
    public void SeriesIsDecidedByMajorVersion(int major, int minor, int build, ReleaseSeries expected)
        => Assert.Equal(expected, ReleaseChannel.FromVersion(new Version(major, minor, build)));

    [Theory]
    [InlineData("X1031", ReleaseSeries.X, true)]
    [InlineData("x1031", ReleaseSeries.X, true)]
    [InlineData("X0101", ReleaseSeries.X, true)]
    [InlineData("X1231Y1", ReleaseSeries.X, true)]
    [InlineData("X1031Y12", ReleaseSeries.X, true)]
    [InlineData("x1031y2", ReleaseSeries.X, true)]
    [InlineData("X1031A", ReleaseSeries.X, false)]
    [InlineData("X1031.2", ReleaseSeries.X, false)]
    [InlineData("X1031Y0", ReleaseSeries.X, false)]
    [InlineData("X1331", ReleaseSeries.X, false)]
    [InlineData("X0230", ReleaseSeries.X, false)]
    [InlineData("V3.5.0", ReleaseSeries.X, false)]
    [InlineData("X1031", ReleaseSeries.V, false)]
    [InlineData("V3.5.0", ReleaseSeries.V, true)]
    [InlineData("v3.5.0", ReleaseSeries.V, true)]
    [InlineData("3.5.0", ReleaseSeries.V, true)]
    [InlineData("1031.0.0", ReleaseSeries.V, false)]
    public void TagsBelongToOneSeriesOnly(string tag, ReleaseSeries series, bool expected)
        => Assert.Equal(expected, ReleaseChannel.IsTagForSeries(tag, series));

    [Theory]
    [InlineData("X1031", 1031, 0, 0)]
    [InlineData("X0101", 101, 0, 0)]
    [InlineData("X1101", 1101, 0, 0)]
    [InlineData("X1031Y2", 1031, 2, 0)]
    [InlineData("X1031Y10", 1031, 10, 0)]
    public void XTagsParseToInternalVersions(string tag, int major, int minor, int build)
    {
        var v = ReleaseChannel.ParseTagForSeries(tag, ReleaseSeries.X);
        Assert.NotNull(v);
        Assert.Equal(new Version(major, minor, build), v);
    }

    [Theory]
    [InlineData("X1331")]
    [InlineData("X0230")]
    [InlineData("X0000")]
    [InlineData("X1031Y0")]
    [InlineData("X1031Y")]
    [InlineData("X1031.2")]
    public void InvalidXTagsAreRejected(string tag)
        => Assert.Null(ReleaseChannel.ParseTagForSeries(tag, ReleaseSeries.X));

    [Fact]
    public void XSeriesOrderingIsDateFirstThenYRevision()
    {
        var x1031 = ReleaseChannel.ParseTagForSeries("X1031", ReleaseSeries.X)!;
        var x1031Y2 = ReleaseChannel.ParseTagForSeries("X1031Y2", ReleaseSeries.X)!;
        var x1031Y10 = ReleaseChannel.ParseTagForSeries("X1031Y10", ReleaseSeries.X)!;
        var x1101 = ReleaseChannel.ParseTagForSeries("X1101", ReleaseSeries.X)!;

        Assert.True(x1031Y2 > x1031);
        Assert.True(x1031Y10 > x1031Y2);
        Assert.True(x1101 > x1031Y10);
    }

    [Fact]
    public void DisplayVersionUsesSeriesPrefix()
    {
        Assert.Equal("X1031", ReleaseChannel.FormatVersion(new Version(1031, 0, 0)));
        Assert.Equal("X1031Y2", ReleaseChannel.FormatVersion(new Version(1031, 2, 0)));
        Assert.Equal("X0101", ReleaseChannel.FormatVersion(new Version(101, 0, 0)));
        Assert.Equal("v3.5.0", ReleaseChannel.FormatVersion(new Version(3, 5, 0)));
    }

    [Fact]
    public void LatestVersionIgnoresOtherSeries()
    {
        const string json = """
        [
          { "name": "V3.6.0" },
          { "name": "X1031" },
          { "name": "X1101" },
          { "name": "V3.5.0" }
        ]
        """;

        Assert.Equal(new Version(1101, 0, 0), UpdateService.ParseLatestVersion(json, ReleaseSeries.X));
        Assert.Equal(new Version(3, 6, 0), UpdateService.ParseLatestVersion(json, ReleaseSeries.V));
    }

    [Fact]
    public void LatestVersionOnSameDateUsesYRevision()
    {
        const string json = """
        [
          { "name": "X1031" },
          { "name": "X1031Y2" },
          { "name": "X1031Y10" }
        ]
        """;

        Assert.Equal(new Version(1031, 10, 0), UpdateService.ParseLatestVersion(json, ReleaseSeries.X));
    }

    [Fact]
    public void LatestVersionSkipsUnparsableTags()
    {
        const string json = """
        [
          { "name": "X-beta" },
          { "name": "X1031" },
          { "name": "latest" },
          { "name": "X1031A" },
          { "name": "X1331" }
        ]
        """;
        Assert.Equal(new Version(1031, 0, 0), UpdateService.ParseLatestVersion(json, ReleaseSeries.X));
    }

    [Fact]
    public void ReleaseBodyDropsAutoAppendedShaTable()
    {
        const string body = """
        # X1031 发布说明

        - 修复更新检测

        ---

        ### 资产 SHA-256

        | 资产 | 大小 (MB) | SHA-256 |
        | --- | --- | --- |
        """;

        var trimmed = UpdateService.TrimReleaseBodyForDisplay(body);
        Assert.Contains("修复更新检测", trimmed);
        Assert.DoesNotContain("资产 SHA-256", trimmed);
        Assert.DoesNotContain("SHA-256 |", trimmed);
    }

    [Fact]
    public void ReleaseBodyEmptyWhenFetchFailed()
        => Assert.Equal("", UpdateService.TrimReleaseBodyForDisplay(null));
}
