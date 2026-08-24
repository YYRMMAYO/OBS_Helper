using System.IO;
using Microsoft.Win32;

namespace OBS_Helper.Wpf.Services.Audio;

/// <summary>一次音频采样率体检的结果。</summary>
public sealed class SampleRateCheckResult
{
    public List<SampleRateCheckItem> Items { get; init; } = new();
}

/// <summary>
/// 音频采样率体检服务（V2.7 工具箱，只读）。
///
/// 1) 从当前 Profile 的 basic.ini 读 OBS 采样率（audio.samplerate，缺省 48k）；
/// 2) 从注册表 MMDevices 枚举活动音频设备的共享模式采样率
///    （PKEY_AudioEngine_DeviceFormat 的 WAVEFORMATEX 头部）；
/// 3) 交给 <see cref="SampleRateCheckCore"/> 评估。
///
/// 注册表读取全程 try/catch：枚举失败降级为「手动核对」指引，绝不抛异常，
/// 也绝不修改任何设备或 OBS 配置。
/// </summary>
public sealed class SampleRateCheckService
{
    private const string MMDevicesRoot =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio";

    // PKEY_AudioEngine_DeviceFormat：二进制 WAVEFORMATEX，offset 4 起 4 字节为 nSamplesPerSec
    private const string DeviceFormatValue = @"{f19f064d-082c-4e27-bc73-6882a1bb8e4c},0";
    // PKEY_Device_FriendlyName
    private const string FriendlyNameValue = @"{b3f8fa53-0004-438e-9003-51a46e139bfc},2";

    private readonly ObsConfig.ObsPathService _paths;

    public SampleRateCheckService(ObsConfig.ObsPathService paths) => _paths = paths;

    public async Task<SampleRateCheckResult> RunAsync()
    {
        var obsRate = await ReadObsSampleRateAsync().ConfigureAwait(false);
        var devices = await Task.Run(EnumerateDevices).ConfigureAwait(false);
        return new SampleRateCheckResult
        {
            Items = SampleRateCheckCore.Evaluate(obsRate, devices)
        };
    }

    /// <summary>从 basic.ini 读 audio.samplerate；读不到返回 null（默认 48k）。</summary>
    private async Task<int?> ReadObsSampleRateAsync()
    {
        try
        {
            var loc = await _paths.LocateAsync().ConfigureAwait(false);
            if (!loc.Exists) return null;

            var globalIniText = File.ReadAllText(Path.Combine(loc.ConfigDir, "global.ini"));
            var profileDir = Services.Obs.PreflightCheckCore.ParseIni(globalIniText)
                .TryGetValue("basic.profiledir", out var dir) ? dir : null;
            if (string.IsNullOrWhiteSpace(profileDir)) return null;

            var basicPath = Path.Combine(loc.ConfigDir, "basic", "profiles", profileDir!, "basic.ini");
            if (!File.Exists(basicPath)) return null;
            var ini = Services.Obs.PreflightCheckCore.ParseIni(File.ReadAllText(basicPath));

            return ini.TryGetValue("audio.samplerate", out var raw) &&
                   int.TryParse(raw, out var rate)
                ? rate
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>枚举活动的播放与录音设备的共享模式采样率。失败返回空列表。</summary>
    internal static List<AudioDeviceInfo> EnumerateDevices()
    {
        var result = new List<AudioDeviceInfo>();
        try
        {
            foreach (var (subKey, isRender) in new[] { ("Render", true), ("Capture", false) })
            {
                using var root = Registry.LocalMachine.OpenSubKey($@"{MMDevicesRoot}\{subKey}");
                if (root is null) continue;

                foreach (var deviceId in root.GetSubKeyNames())
                {
                    try
                    {
                        using var dev = root.OpenSubKey(deviceId);
                        // DeviceState == 1 (DEVICE_STATE_ACTIVE) 才参与体检
                        if (dev?.GetValue("DeviceState") is not int state || state != 1) continue;

                        using var props = dev!.OpenSubKey("Properties");
                        if (props is null) continue;

                        var name = props.GetValue(FriendlyNameValue) as string ?? deviceId[..Math.Min(8, deviceId.Length)];
                        var rate = ParseSharedModeRate(props.GetValue(DeviceFormatValue));
                        if (rate is > 0)
                            result.Add(new AudioDeviceInfo(name, rate.Value, isRender));
                    }
                    catch (Exception) { }
                }
            }
        }
        catch (Exception)
        {
            return result; // 部分枚举结果仍可用；全失败则为空列表 → 纯逻辑侧给「手动核对」提示
        }
        return result;
    }

    private static int? ParseSharedModeRate(object? raw)
    {
        if (raw is not byte[] bytes || bytes.Length < 8) return null;
        // WAVEFORMATEX: wFormatTag(2) nChannels(2) nSamplesPerSec(4, LE, offset 4)
        return BitConverter.ToInt32(bytes, 4);
    }
}
