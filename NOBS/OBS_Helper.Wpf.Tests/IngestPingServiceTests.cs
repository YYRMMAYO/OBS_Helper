using OBS_Helper.Wpf.Services.Tools;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

public class IngestPingServiceTests
{
    private static readonly IngestTarget A = new("A", "a.example.com");
    private static readonly IngestTarget B = new("B", "b.example.com");
    private static readonly IngestTarget C = new("C", "c.example.com");

    [Fact]
    public void Sort_ByRttAscending()
    {
        var results = new List<IngestPingResult>
        {
            new(A, 120),
            new(B, 30),
            new(C, 75),
        };

        var sorted = IngestPingService.Sort(results);
        Assert.Equal(new[] { B, C, A }, sorted.Select(r => r.Target));
    }

    [Fact]
    public void Sort_FailuresGoLast_StableOrder()
    {
        var results = new List<IngestPingResult>
        {
            new(A, null),   // 失败 1
            new(B, 200),
            new(C, null),   // 失败 2
        };

        var sorted = IngestPingService.Sort(results);
        Assert.True(sorted[0].Ok);
        Assert.Equal(200, sorted[0].RttMs);
        Assert.Equal(new[] { A, C }, sorted.Skip(1).Select(r => r.Target));
    }

    [Fact]
    public void Sort_DoesNotMutateInput()
    {
        var results = new List<IngestPingResult> { new(A, 100), new(B, 10) };
        _ = IngestPingService.Sort(results);
        Assert.Equal(100, results[0].RttMs);
    }

    [Fact]
    public async Task MeasureAsync_EmptyHost_ReturnsFailure()
    {
        var r = await IngestPingService.MeasureAsync(new IngestTarget("x", ""));
        Assert.False(r.Ok);
    }

    [Fact]
    public async Task MeasureAsync_UnreachableHost_ReturnsFailure()
    {
        // RFC 5737 保留地址，保证不可达；超时 800ms 内必然返回
        var r = await IngestPingService.MeasureAsync(new IngestTarget("x", "203.0.113.1", 1));
        Assert.False(r.Ok);
        Assert.Equal("连接失败", r.RttText);
    }

    [Fact]
    public async Task MeasureAllAsync_AllFailures_StillReturnsSortedResults()
    {
        var targets = new[]
        {
            new IngestTarget("x", "203.0.113.1", 1),
            new IngestTarget("", ""),
        };
        var results = await IngestPingService.MeasureAllAsync(targets);
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.False(r.Ok));
    }
}
