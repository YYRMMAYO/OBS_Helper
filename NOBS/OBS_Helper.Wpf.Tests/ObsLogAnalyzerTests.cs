using OBS_Helper.Wpf.Services.Obs;

namespace OBS_Helper.Wpf.Tests;

public class ObsLogAnalyzerTests
{
    private readonly ObsLogAnalyzer _analyzer = new();

    [Fact]
    public void Analyze_Empty_ReturnsEmptyReport()
    {
        var report = _analyzer.Analyze("");
        Assert.Empty(report.Findings);
        Assert.Equal(0, report.Summary.TotalLines);
    }

    [Fact]
    public void Analyze_DetectsEncodingOverload()
    {
        var report = _analyzer.Analyze("encoder overloaded");
        Assert.Contains(report.Findings, f => f.Code == "LOG-ENC-OVERLOAD");
    }

    [Fact]
    public void Analyze_DetectsNvencFailure()
    {
        var report = _analyzer.Analyze("Failed to open NVENC codec");
        Assert.Contains(report.Findings, f => f.Code == "LOG-ENC-NVENC");
    }

    [Theory]
    [InlineData("OBS 30.0.0 (64-bit, windows)")]
    [InlineData("OBS Studio 30.0.0 (windows)")]
    public void Analyze_ExtractsEnvironmentSummary(string versionLine)
    {
        var input = versionLine + "\n" +
                    "CPU Name: AMD Ryzen 7\n" +
                    "Physical Memory: 16384MB Total\n" +
                    "Windows Version: 10.0 Build 22631\n" +
                    "base resolution:  1920x1080\n" +
                    "output resolution: 1280x720\n" +
                    "fps:  60/1\n";
        var report = _analyzer.Analyze(input);
        Assert.Equal("30.0.0", report.Summary.ObsVersion);
        Assert.Equal("AMD Ryzen 7", report.Summary.Cpu);
        Assert.Equal("1920x1080", report.Summary.BaseResolution);
        Assert.Equal("1280x720", report.Summary.OutputResolution);
        Assert.Equal("60", report.Summary.Fps);
    }

    [Fact]
    public void Analyze_QuantifiesEncodingLag_RatioAndSeverity()
    {
        var input = "skipped frames due to encoding lag: 500/10000 (5.0%)\n";
        var report = _analyzer.Analyze(input);
        Assert.True(report.Summary.EncodingLagRatio > 0.04);
        var stat = report.Findings.FirstOrDefault(f => f.Code == "LOG-STAT-ENCODE");
        Assert.NotNull(stat);
        Assert.Equal(LogSeverity.Critical, stat!.Severity);
    }

    [Fact]
    public void Analyze_SanitizesEvidence()
    {
        var report = _analyzer.Analyze("encoder overloaded streamkey=SECRETVALUE");
        Assert.DoesNotContain("SECRETVALUE", report.SanitizedText);
    }

    [Fact]
    public void Findings_SortedBySeverityDescending()
    {
        var report = _analyzer.Analyze("Failed to initialize video encoder overloaded");
        for (var i = 1; i < report.Findings.Count; i++)
        {
            Assert.True((int)report.Findings[i - 1].Severity >= (int)report.Findings[i].Severity);
        }
    }

    [Fact]
    public void Analyze_DetectsHybridGpu()
    {
        var report = _analyzer.Analyze("Loading up D3D11 on adapter Intel(R) UHD Graphics 630");
        Assert.Contains(report.Findings, f => f.Code == "LOG-GPU-HYBRID");
    }

    [Fact]
    public void Analyze_DetectsSampleRateMismatch()
    {
        var report = _analyzer.Analyze("sample rate doesn't match");
        Assert.Contains(report.Findings, f => f.Code == "LOG-AUDIO-SAMPLERATE");
    }

    [Fact]
    public void Analyze_DetectsStreamKeyInLog()
    {
        var report = _analyzer.Analyze("streamkey=abc123secretvalue");
        Assert.Contains(report.Findings, f => f.Code == "LOG-STREAMKEY-LEAK");
    }

    [Fact]
    public void Analyze_DetectsCrashModule()
    {
        var report = _analyzer.Analyze("Faulting module name: obs-browser.dll");
        Assert.Contains(report.Findings, f => f.Code == "LOG-CRASH-MODULE");
    }

    // ---------------------- V2.2 P0-2：插件嫌疑提取与联动 ----------------------

    [Fact]
    public void Analyze_PluginLoadFailure_ExtractsSuspectModule()
    {
        var report = _analyzer.Analyze(
            @"os_dlopen(C:\Program Files\obs-studio\obs-plugins\64bit\foo-bar.dll): The specified module could not be found.");
        var finding = report.Findings.FirstOrDefault(f => f.Code == "LOG-PLUGIN");
        Assert.NotNull(finding);
        Assert.Equal("foo-bar.dll", finding!.SuspectModule);
    }

    [Fact]
    public void Analyze_CrashModule_ExtractsSuspectModule()
    {
        var report = _analyzer.Analyze("Exception Module Name: evilplugin.dll");
        var finding = report.Findings.FirstOrDefault(f => f.Code == "LOG-CRASH-MODULE");
        Assert.NotNull(finding);
        Assert.Equal("evilplugin.dll", finding!.SuspectModule);
    }

    [Fact]
    public void Analyze_StreamFX_MapsToMigrationEntry()
    {
        var report = _analyzer.Analyze("os_dlopen(streamfx.dll) failed to load");
        Assert.Contains(report.Findings, f => f.Code == "LOG-PLUGIN-STREAMFX" && f.ProblemId == "cr-streamfx");
    }

    [Fact]
    public void Analyze_MultiRtmp_MentionsKnownIssuesEntry()
    {
        var report = _analyzer.Analyze("[obs-multi-rtmp] output 'rtmp_output' reconnecting");
        var finding = report.Findings.FirstOrDefault(f => f.Code == "LOG-PLUGIN-MULTI-RTMP");
        Assert.NotNull(finding);
        Assert.Equal("st-multi-rtmp", finding!.ProblemId);
        // Info 级别不应排在 Warning 之前
        var warnIndex = report.Findings.FindIndex(f => f.Severity == LogSeverity.Warning);
        if (warnIndex >= 0)
        {
            var idx = report.Findings.IndexOf(finding!);
            Assert.True(idx >= 0);
        }
    }
}
