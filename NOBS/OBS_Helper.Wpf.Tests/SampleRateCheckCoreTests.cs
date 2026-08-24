using OBS_Helper.Wpf.Services.Audio;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

public class SampleRateCheckCoreTests
{
    [Fact]
    public void Evaluate_Obs48k_AllDevices48k_Pass()
    {
        var devices = new List<AudioDeviceInfo>
        {
            new("扬声器", 48000, IsRender: true),
            new("麦克风", 48000, IsRender: false),
        };

        var items = SampleRateCheckCore.Evaluate(48000, devices);
        Assert.All(items, i => Assert.Equal("ok", i.Status));
    }

    [Fact]
    public void Evaluate_Obs44100_WarnsWithKbLink()
    {
        var items = SampleRateCheckCore.Evaluate(44100, new List<AudioDeviceInfo>());
        var obs = items.First(i => i.Title.Contains("OBS"));
        Assert.Equal("warn", obs.Status);
        Assert.Equal("au-sample-mismatch", obs.ProblemId);
    }

    [Fact]
    public void Evaluate_NullObsRate_TreatedAsDefault48k()
    {
        var items = SampleRateCheckCore.Evaluate(null, new List<AudioDeviceInfo>());
        Assert.Equal("ok", items.First(i => i.Title.Contains("OBS")).Status);
    }

    [Fact]
    public void Evaluate_MismatchedDevice_Warns()
    {
        var devices = new List<AudioDeviceInfo>
        {
            new("扬声器 (Realtek)", 48000, IsRender: true),
            new("USB Microphone", 44100, IsRender: false),
        };

        var items = SampleRateCheckCore.Evaluate(48000, devices);
        Assert.Equal(2, items.Count);

        var devItem = items.First(i => i.Title.Contains("设备"));
        Assert.Equal("warn", devItem.Status);
        Assert.Equal("au-sample-mismatch", devItem.ProblemId);
        Assert.Contains("USB Microphone", devItem.Detail);
    }

    [Fact]
    public void Evaluate_EmptyDevices_GivesManualHint()
    {
        var items = SampleRateCheckCore.Evaluate(48000, new List<AudioDeviceInfo>());
        Assert.Equal(2, items.Count);
        Assert.Equal("info", items[1].Status);
    }
}
