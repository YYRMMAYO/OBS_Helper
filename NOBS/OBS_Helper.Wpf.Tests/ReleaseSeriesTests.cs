using System.Text.Json;
using OBS_Helper.Wpf.Services;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// X1031：X / V 双通道解析与过滤。
/// X 构建只能看到 X 标签，V 构建只能看到 V 标签；两边绝不互相提示。
/// </summary>
public class ReleaseSeriesTests
{
    [Theory]
    [InlineData(3, 5, 0, ReleaseSeries.V)]
    [InlineData(3, 4, 2, ReleaseSeries.V)]
    [InlineData(1031, 0, 0, ReleaseSeries.X)]
    [InlineData(1101, 0, 0, ReleaseSeries.X)]
    public void SeriesIsDecidedByMajorVersion(int major, int minor, int build, ReleaseSeries expected)
        => Assert.Equal(expected, ReleaseChannel.FromVersion(new Version(major, minor, build)));

    [Theory]
    [InlineData("X1031", ReleaseSeries.X, true)]
    [InlineData("x1031", ReleaseSeries.X, true)]
    [InlineData("X1101", ReleaseSeries.X, true)]
    [InlineData("X1031.2", ReleaseSeries.X, true)]
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
    [InlineData("X1101", 1101, 0, 0)]
    [InlineData("X1031.2", 1031, 2, 0)]
    public void XTagsParseToInternalVersions(string tag, int major, int minor, int build)
    {
        var v = ReleaseChannel.ParseTagForSeries(tag, ReleaseSeries.X);
        Assert.NotNull(v);
        Assert.Equal(new Version(major, minor, build), v);
    }

    [Fact]
    public void XSeriesOrderingIsChronological()
    {
        var a = ReleaseChannel.ParseTagForSeries("X1031", ReleaseSeries.X)!;
        var b = ReleaseChannel.ParseTagForSeries("X1101", ReleaseSeries.X)!;
        Assert.True(b > a);
    }

    [Fact]
    public void DisplayVersionUsesSeriesPrefix()
    {
        Assert.Equal("X1031", ReleaseChannel.FormatVersion(new Version(1031, 0, 0)));
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
    public void LatestVersionSkipsUnparsableTags()
    {
        const string json = """
        [
          { "name": "X-beta" },
          { "name": "X1031" },
          { "name": "latest" }
        ]
        """;
        Assert.Equal(new Version(1031, 0, 0), UpdateService.ParseLatestVersion(json, ReleaseSeries.X));
    }
}
