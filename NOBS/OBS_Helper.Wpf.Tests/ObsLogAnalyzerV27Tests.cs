using OBS_Helper.Wpf.Services.Obs;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>V2.7 新增的 4 条日志分析规则。</summary>
public class ObsLogAnalyzerV27Tests
{
    private static LogFinding? Find(ObsLogReport report, string code)
        => report.Findings.FirstOrDefault(f => f.Code == code);

    [Fact]
    public void ColorRangeFull_HitsRule()
    {
        var log = "23:10:02 video settings: base 1920x1080\n" +
                  "color range: full\ncolor space: 709";
        var r = new ObsLogAnalyzer().Analyze(log);
        var f = Find(r, "LOG-COLOR-RANGE");
        Assert.NotNull(f);
        Assert.Equal("cf-colorrange", f!.ProblemId);
    }

    [Fact]
    public void DynamicBitrateDrop_HitsRule()
    {
        var log = "23:11:00 output 'adv_stream': bitrate reduced to 4500 due to congestion";
        var r = new ObsLogAnalyzer().Analyze(log);
        Assert.NotNull(Find(r, "LOG-BITRATE-DROP"));
    }

    [Fact]
    public void DecklinkFailure_HitsRule()
    {
        var log = "23:12:33 decklink output: failed to start capture (invalid mode)";
        var r = new ObsLogAnalyzer().Analyze(log);
        var f = Find(r, "LOG-CAPTURE-CARD");
        Assert.NotNull(f);
        Assert.Equal("bs-capturecard", f!.ProblemId);
    }

    [Fact]
    public void Resampling_HitsRule()
    {
        var log = "23:13:05 audio device resampling from 44100 Hz to 48000 Hz";
        var r = new ObsLogAnalyzer().Analyze(log);
        var f = Find(r, "LOG-AUDIO-RESAMPLE");
        Assert.NotNull(f);
        Assert.Equal("au-sample-mismatch", f!.ProblemId);
    }

    [Fact]
    public void NormalLog_DoesNotHitV27Rules()
    {
        var log = "OBS 32.1.2 (64-bit, windows)\n" +
                  "CPU Name: AMD Ryzen 9\n" +
                  "color range: limited";
        var r = new ObsLogAnalyzer().Analyze(log);
        Assert.Null(Find(r, "LOG-COLOR-RANGE"));
        Assert.Null(Find(r, "LOG-BITRATE-DROP"));
        Assert.Null(Find(r, "LOG-CAPTURE-CARD"));
        Assert.Null(Find(r, "LOG-AUDIO-RESAMPLE"));
    }
}
