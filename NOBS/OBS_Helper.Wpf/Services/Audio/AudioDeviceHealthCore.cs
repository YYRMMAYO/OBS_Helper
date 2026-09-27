using OBS_Helper.Wpf.Localization;
namespace OBS_Helper.Wpf.Services.Audio;

/// <summary>
/// 音频设备深度体检核心（纯逻辑，供单元测试）。GAP-3。
///
/// 「OBS 没声音 / 麦克风无声」的根因常在系统侧：
/// 隐私权限未授权、通信 Ducking 压低音量、音频服务未运行、OBS 所选设备已漂移。
/// 本核心接收只读探测得到的快照做确定性判定。
/// </summary>
public static class AudioDeviceHealthCore
{
    /// <summary>
    /// 通信 Ducking 策略（HKCU\Software\Microsoft\Multimedia\Audio\UserDuckingPolicy）。
    /// 0 = 不执行任何操作；其余值都会在检测到通话时压低 / 静音其他声音；缺省 = 未显式设置。
    /// </summary>
    public const int DuckingDoNothing = 0;

    public static List<EnvCheckItem> Evaluate(AudioDeviceHealthSnapshot s)
    {
        var items = new List<EnvCheckItem>();

        // ---- 麦克风隐私权限 ----
        items.Add(s.MicGlobalConsent switch
        {
            false => new EnvCheckItem("error", Strings.T("audiohealth.mic.deniedTitle"),
                Strings.T("audiohealth.mic.deniedDetail")),
            true => new EnvCheckItem("ok", Strings.T("audiohealth.mic.allowedTitle"), Strings.T("audiohealth.mic.allowedDetail")),
            _ => new EnvCheckItem("info", Strings.T("audiohealth.mic.unknownTitle"),
                Strings.T("audiohealth.mic.unknownDetail"))
        });

        // ---- 通信 Ducking ----
        items.Add(s.UserDuckingPolicy switch
        {
            DuckingDoNothing => new EnvCheckItem("ok", Strings.T("audiohealth.ducking.offTitle"),
                Strings.T("audiohealth.ducking.offDetail")),
            null => new EnvCheckItem("info", Strings.T("audiohealth.ducking.defaultTitle"),
                Strings.T("audiohealth.ducking.defaultDetail")),
            var v => new EnvCheckItem("warn", Strings.T("audiohealth.ducking.reducedTitle", v),
                Strings.T("audiohealth.ducking.reducedDetail"))
        });

        // ---- 音频服务 ----
        if (!s.AudiosrvRunning || !s.AudioEndpointBuilderRunning)
        {
            var dead = new List<string>();
            if (!s.AudiosrvRunning) dead.Add("Windows Audio (Audiosrv)");
            if (!s.AudioEndpointBuilderRunning) dead.Add("Windows Audio Endpoint Builder");
            items.Add(new EnvCheckItem("error", Strings.T("audiohealth.service.deadTitle"),
                Strings.T("audiohealth.service.deadDetail", string.Join(", ", dead))));
        }
        else
        {
            items.Add(new EnvCheckItem("ok", Strings.T("audiohealth.service.okTitle"), Strings.T("audiohealth.service.okDetail")));
        }

        // ---- OBS 所选输入 vs 系统活动捕获设备 ----
        if (s.ObsAudioInputs.Count > 0 && s.CaptureDeviceNames.Count == 0)
        {
            items.Add(new EnvCheckItem("warn", Strings.T("audiohealth.drift.noneDevicesTitle"),
                Strings.T("audiohealth.drift.noneDevicesDetail", s.ObsAudioInputs.Count)));
        }
        else
        {
            var unmatched = MatchDrift(s.ObsAudioInputs, s.CaptureDeviceNames);
            if (unmatched.Count > 0)
            {
                items.Add(new EnvCheckItem("warn", Strings.T("audiohealth.drift.mismatchTitle"),
                    Strings.T("audiohealth.drift.mismatchDetail", string.Join(", ", unmatched))));
            }
            else
            {
                items.Add(new EnvCheckItem("ok", Strings.T("audiohealth.obsInputs.okTitle"),
                    s.ObsAudioInputs.Count == 0
                        ? Strings.T("audiohealth.obsInputs.none")
                        : Strings.T("audiohealth.obsInputs.matched", s.ObsAudioInputs.Count)));
            }
        }

        return items;
    }

    /// <summary>宽松名称匹配：双向包含即视为同一设备（忽略大小写与空白差异）。</summary>
    public static List<string> MatchDrift(IReadOnlyList<string> obsInputs, IReadOnlyList<string> systemDevices)
    {
        var unmatched = new List<string>();
        foreach (var input in obsInputs)
        {
            var nInput = Normalize(input);
            if (nInput.Length == 0) continue;
            var found = systemDevices.Any(d =>
            {
                var nDev = Normalize(d);
                return nDev.Length > 0 && (nDev.Contains(nInput, StringComparison.Ordinal) ||
                                           nInput.Contains(nDev, StringComparison.Ordinal));
            });
            if (!found) unmatched.Add(input);
        }
        return unmatched;
    }

    private static string Normalize(string s) => s.Trim().ToLowerInvariant().Replace(" ", "");
}

/// <summary>音频设备深度体检快照：全部由只读探测填充，未知项保持 null。</summary>
public sealed class AudioDeviceHealthSnapshot
{
    /// <summary>麦克风全局隐私开关：true=允许 false=拒绝 null=读取失败。</summary>
    public bool? MicGlobalConsent { get; init; }

    /// <summary>通信 Ducking 策略值；null = 未显式设置。</summary>
    public int? UserDuckingPolicy { get; init; }

    public bool AudiosrvRunning { get; init; }
    public bool AudioEndpointBuilderRunning { get; init; }

    /// <summary>OBS 中配置的音频输入名（来自 websocket GetInputList）。</summary>
    public List<string> ObsAudioInputs { get; init; } = new();

    /// <summary>系统活动录音设备友好名（来自 MMDevices 枚举）。</summary>
    public List<string> CaptureDeviceNames { get; init; } = new();
}
