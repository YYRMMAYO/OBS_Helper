using OBS_Helper.Wpf.Services.Obs;

namespace OBS_Helper.Wpf.Tests;

public class PreflightCheckTests
{
    private static Dictionary<string, string> Parse(string ini) => PreflightCheckCore.ParseIni(ini);

    [Fact]
    public void ParseIni_ReadsSectionsAndKeys()
    {
        var ini = Parse("[SimpleOutput]\nRecFormat2=mkv\nFilePath=C:\\rec\n\n[Audio]\nSampleRate=44100\n");
        Assert.Equal("mkv", ini["simpleoutput.recformat2"]);
        Assert.Equal("C:\\rec", ini["simpleoutput.filepath"]);
        Assert.Equal("44100", ini["audio.samplerate"]);
    }

    [Fact]
    public void Run_MissingConfigDir_AddsSingleFailItem()
    {
        var report = new PreflightReport();
        PreflightCheckCore.Run(report, configDirExists: false, globalIni: null, basicIniText: null);
        var item = Assert.Single(report.Items);
        Assert.Equal(PreflightStatus.Fail, item.Status);
    }

    private static (PreflightReport Report, string Global) NewReport() =>
        (new PreflightReport(), "[Basic]\nProfileDir=Untitled\n");

    [Fact]
    public void Run_MkvFormat_Passes()
    {
        var (report, global) = NewReport();
        PreflightCheckCore.Run(report, true, Parse(global), "[AdvOut]\nRecFormat2=mkv\n");

        var fmt = report.Items.First(i => i.Title.Contains("录制格式"));
        Assert.Equal(PreflightStatus.Ok, fmt.Status);
    }

    [Fact]
    public void Run_Mp4Format_WarnsWithKbLink()
    {
        var (report, global) = NewReport();
        PreflightCheckCore.Run(report, true, Parse(global), "[AdvOut]\nRecFormat2=mp4\n");

        var fmt = report.Items.First(i => i.Title.Contains("录制格式"));
        Assert.Equal(PreflightStatus.Warn, fmt.Status);
        Assert.Equal("rc-mkv", fmt.ProblemId);
    }

    [Fact]
    public void Run_MissingRecordingPath_FailsWithProblemLink()
    {
        var (report, global) = NewReport();
        PreflightCheckCore.Run(report, true, Parse(global),
            "[SimpleOutput]\nFilePath=Z:\\not-exist-dir\\rec\n");

        var path = report.Items.First(i => i.Title.Contains("录制保存路径"));
        Assert.Equal(PreflightStatus.Fail, path.Status);
        Assert.Equal("rc-nofile", path.ProblemId);
    }

    [Fact]
    public void Run_SoftwareEncoder_Warns()
    {
        var (report, global) = NewReport();
        PreflightCheckCore.Run(report, true, Parse(global),
            "[SimpleOutput]\nStreamEncoder=obs_x264\nRecFormat2=mkv\n");

        var enc = report.Items.First(i => i.Title.Contains("编码器"));
        Assert.Equal(PreflightStatus.Warn, enc.Status);
        Assert.Equal("enc-overload", enc.ProblemId);
    }

    [Fact]
    public void Run_HardwareEncoder_Passes()
    {
        var (report, global) = NewReport();
        PreflightCheckCore.Run(report, true, Parse(global),
            "[SimpleOutput]\nStreamEncoder=jim_nvenc\nRecFormat2=mkv\n");

        var enc = report.Items.First(i => i.Title.Contains("编码器"));
        Assert.Equal(PreflightStatus.Ok, enc.Status);
    }

    [Fact]
    public void Run_WrongSampleRate_Warns()
    {
        var (report, global) = NewReport();
        PreflightCheckCore.Run(report, true, Parse(global),
            "[Audio]\nSampleRate=44100\nMicDevice=device-guid\n");

        var rate = report.Items.First(i => i.Title.Contains("采样率"));
        Assert.Equal(PreflightStatus.Warn, rate.Status);
        Assert.Equal("av-sample", rate.ProblemId);
    }

    [Fact]
    public void Run_NoMicEnabled_InformationalOnly()
    {
        var (report, global) = NewReport();
        PreflightCheckCore.Run(report, true, Parse(global),
            "[Audio]\nSampleRate=48000\nMicDevice=disabled\nAuxDevice1=disabled\n");

        var mic = report.Items.First(i => i.Title.Contains("麦克风"));
        Assert.Equal(PreflightStatus.Info, mic.Status);
    }

    [Fact]
    public void Run_MicEnabled_Passes()
    {
        var (report, global) = NewReport();
        PreflightCheckCore.Run(report, true, Parse(global),
            "[Audio]\nSampleRate=48000\nMicDevice={0x25DB46.0x0}\n");

        var mic = report.Items.First(i => i.Title.Contains("麦克风"));
        Assert.Equal(PreflightStatus.Ok, mic.Status);
    }

    [Fact]
    public void Run_FreeSpaceBelow10GB_Warns()
    {
        var (report, global) = NewReport();
        var dir = System.IO.Directory.CreateTempSubdirectory("preflight").FullName;
        try
        {
            var basic = $"[SimpleOutput]\nFilePath={dir}\nRecFormat2=mkv\n";

            // 模拟 5GB 剩余
            PreflightCheckCore.Run(report, true, Parse(global), basic,
                freeBytesOf: _ => 5L * 1024 * 1024 * 1024);
        }
        finally
        {
            try { System.IO.Directory.Delete(dir, true); } catch (Exception) { }
        }

        var space = report.Items.FirstOrDefault(i => i.Title.Contains("剩余空间"));
        Assert.NotNull(space);
        Assert.Equal(PreflightStatus.Warn, space!.Status);
        Assert.Equal("rc-disk-space", space.ProblemId);
    }
}
