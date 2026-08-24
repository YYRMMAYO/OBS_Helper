using OBS_Helper.Wpf.Services.Audio;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

public class AudioDeviceHealthCoreTests
{
    private static AudioDeviceHealthSnapshot Base() => new()
    {
        MicGlobalConsent = true,
        UserDuckingPolicy = AudioDeviceHealthCore.DuckingDoNothing,
        AudiosrvRunning = true,
        AudioEndpointBuilderRunning = true,
        ObsAudioInputs = new List<string> { "麦克风 (USB Microphone)" },
        CaptureDeviceNames = new List<string> { "麦克风 (USB Microphone)", "扬声器 (Realtek)" }
    };

    [Fact]
    public void HealthySetup_NoErrorsNoWarns()
    {
        var items = AudioDeviceHealthCore.Evaluate(Base());
        Assert.DoesNotContain(items, i => i.Status is "error" or "warn");
    }

    [Fact]
    public void MicDenied_ErrorWithSettingsHint()
    {
        var s = Base();
        var snapshot = Clone(s, micConsent: false);
        var items = AudioDeviceHealthCore.Evaluate(snapshot);
        Assert.Contains(items, i => i.Status == "error" && i.Title.Contains("麦克风"));
    }

    [Fact]
    public void DuckingDefault_WarnsOrInfo_DoNothing_Passes()
    {
        // 未设置（Windows 默认压低）→ 提示
        Assert.Contains(AudioDeviceHealthCore.Evaluate(Clone(Base(), duckingSet: false)),
            i => i.Title.Contains("通信时音量策略") && i.Status == "info");

        // 显式设为压低 → warn
        Assert.Contains(AudioDeviceHealthCore.Evaluate(Clone(Base(), ducking: 2)),
            i => i.Status == "warn" && i.Title.Contains("压低"));

        // 不执行任何操作 → ok
        Assert.Contains(AudioDeviceHealthCore.Evaluate(Clone(Base(), ducking: 0)),
            i => i.Status == "ok" && i.Title.Contains("不执行任何操作"));
    }

    [Fact]
    public void AudioServiceDown_Errors()
    {
        var items = AudioDeviceHealthCore.Evaluate(Clone(Base(), audiosrv: false, endpointBuilder: false));
        Assert.Contains(items, i => i.Status == "error" && i.Title.Contains("音频服务"));
    }

    [Fact]
    public void DeviceDrift_Warns_WithUnmatchedNames()
    {
        var items = AudioDeviceHealthCore.Evaluate(Clone(Base(),
            captureDevices: new List<string> { "麦克风 (Realtek Audio)" }));
        var drift = items.First(i => i.Title.Contains("对不上"));
        Assert.Equal("warn", drift.Status);
        Assert.Contains("USB Microphone", drift.Detail);
    }

    [Fact]
    public void NoCaptureDevicesButObsInputs_Configured_Warns()
    {
        var items = AudioDeviceHealthCore.Evaluate(Clone(Base(), captureDevices: new List<string>()));
        Assert.Contains(items, i => i.Status == "warn" && i.Title.Contains("没有枚举到活动的录音设备"));
    }

    [Fact]
    public void MatchDrift_LooseMatching_IgnoresCaseAndSpaces()
    {
        var unmatched = AudioDeviceHealthCore.MatchDrift(
            new List<string> { "Microphone (USB Mic)" },
            new List<string> { "microphone(usb  mic) " });
        Assert.Empty(unmatched);

        unmatched = AudioDeviceHealthCore.MatchDrift(
            new List<string> { "完全不同的设备" },
            new List<string> { "扬声器 (Realtek)" });
        Assert.Single(unmatched);
    }

    private static AudioDeviceHealthSnapshot Clone(
        AudioDeviceHealthSnapshot b,
        bool? micConsent = null,
        int? ducking = null,
        bool duckingSet = true,
        bool? audiosrv = null,
        bool? endpointBuilder = null,
        List<string>? captureDevices = null)
        => new()
        {
            MicGlobalConsent = micConsent ?? b.MicGlobalConsent,
            UserDuckingPolicy = duckingSet ? ducking : null,
            AudiosrvRunning = audiosrv ?? b.AudiosrvRunning,
            AudioEndpointBuilderRunning = endpointBuilder ?? b.AudioEndpointBuilderRunning,
            ObsAudioInputs = b.ObsAudioInputs,
            CaptureDeviceNames = captureDevices ?? b.CaptureDeviceNames
        };
}
