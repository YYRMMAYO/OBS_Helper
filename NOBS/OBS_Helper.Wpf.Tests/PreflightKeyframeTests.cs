using OBS_Helper.Wpf.Services.Obs;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>V2.7 新增：录前自检的关键帧间隔检查项。</summary>
public class PreflightKeyframeTests
{
    private static Dictionary<string, string> Parse(string ini) => PreflightCheckCore.ParseIni(ini);

    private static (PreflightReport Report, PreflightItem Item) RunAndGetItem(string basicIni)
    {
        var report = new PreflightReport();
        PreflightCheckCore.Run(report, true,
            Parse("[Basic]\nProfileDir=Untitled\n"), basicIni);
        var item = report.Items.FirstOrDefault(i => i.Title.Contains("关键帧"));
        Assert.NotNull(item);
        return (report, item!);
    }

    [Fact]
    public void KeyintZero_WarnsWithKbLink()
    {
        var (_, item) = RunAndGetItem("[jim-nvenc]\nkeyint_sec=0\n");
        Assert.Equal(PreflightStatus.Warn, item.Status);
        Assert.Equal("lag-keyint", item.ProblemId);
    }

    [Fact]
    public void KeyintTwo_Ok()
    {
        var (_, item) = RunAndGetItem("[jim-nvenc]\nkeyint_sec=2\n");
        Assert.Equal(PreflightStatus.Ok, item.Status);
        Assert.Null(item.ProblemId);
    }

    [Fact]
    public void KeyintTooLarge_Warns()
    {
        var (_, item) = RunAndGetItem("[obs-x264]\nkeyint_sec=10\n");
        Assert.Equal(PreflightStatus.Warn, item.Status);
        Assert.Equal("lag-keyint", item.ProblemId);
    }

    [Fact]
    public void KeyintMissing_InfoNotWarn()
    {
        var (_, item) = RunAndGetItem("[AdvOut]\nRecFormat2=mkv\n");
        Assert.Equal(PreflightStatus.Info, item.Status);
    }
}
