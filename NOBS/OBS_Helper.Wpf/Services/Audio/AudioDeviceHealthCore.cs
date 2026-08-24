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
            false => new EnvCheckItem("error", "系统层面禁用了麦克风访问",
                "Windows 隐私设置把麦克风全局关掉了——这种状态下 OBS 里怎么选设备都收不到声音。" +
                "\n建议：打开 ms-settings:microphone，允许「桌面应用」访问麦克风。"),
            true => new EnvCheckItem("ok", "麦克风访问权限", "系统已允许桌面应用访问麦克风。"),
            _ => new EnvCheckItem("info", "麦克风权限状态未知",
                "未能读取隐私开关（注册表读取受限）。若麦克风无声，先到 设置 → 隐私和安全性 → 麦克风 确认已允许桌面应用。")
        });

        // ---- 通信 Ducking ----
        items.Add(s.UserDuckingPolicy switch
        {
            DuckingDoNothing => new EnvCheckItem("ok", "通信时音量策略：不执行任何操作",
                "微信 / QQ 来电话时不会压低直播 BGM，无需处理。"),
            null => new EnvCheckItem("info", "通信时音量策略：使用 Windows 默认",
                "默认行为是「通话时自动压低其他声音」——直播中收到消息语音会导致 BGM 忽然变小。" +
                "\n建议：声音设置 → 更多声音设置 → 通信选项卡 → 选「不执行任何操作」。"),
            var v => new EnvCheckItem("warn", $"通信时音量会被自动压低（策略值 {v}）",
                "只要电脑检测到通话（微信语音、腾讯会议等），系统就会压低甚至静音其他声音，直播 BGM 会跟着变小。" +
                "\n建议：声音设置 → 更多声音设置 → 通信选项卡 → 改为「不执行任何操作」。")
        });

        // ---- 音频服务 ----
        if (!s.AudiosrvRunning || !s.AudioEndpointBuilderRunning)
        {
            var dead = new List<string>();
            if (!s.AudiosrvRunning) dead.Add("Windows Audio (Audiosrv)");
            if (!s.AudioEndpointBuilderRunning) dead.Add("Windows Audio Endpoint Builder");
            items.Add(new EnvCheckItem("error", "音频服务未运行",
                $"{string.Join("、", dead)} 未在运行，所有录音 / 播放设备都会失效。" +
                "\n建议：Win+R 运行 services.msc，把上述两个服务设为「自动」并启动，然后重启 OBS。"));
        }
        else
        {
            items.Add(new EnvCheckItem("ok", "音频服务", "Audiosrv 与 AudioEndpoint Builder 都在运行。"));
        }

        // ---- OBS 所选输入 vs 系统活动捕获设备 ----
        if (s.ObsAudioInputs.Count > 0 && s.CaptureDeviceNames.Count == 0)
        {
            items.Add(new EnvCheckItem("warn", "没有枚举到活动的录音设备",
                $"OBS 配置了 {s.ObsAudioInputs.Count} 个音频输入，但系统当前没有任何活动录音设备（可能被拔出或被独占）。" +
                "\n建议：检查设备连接；声音设置里确认设备已启用后，回 OBS 重新选择一次。"));
        }
        else
        {
            var unmatched = MatchDrift(s.ObsAudioInputs, s.CaptureDeviceNames);
            if (unmatched.Count > 0)
            {
                items.Add(new EnvCheckItem("warn", "OBS 所选设备与系统活动设备对不上",
                    $"以下 OBS 输入找不到名字相近的系统录音设备：{string.Join("、", unmatched)}。" +
                    "常见于拔插过 USB 设备或蓝牙耳机重连后——Windows 会给设备换新名字，旧选择变成静默失效。" +
                    "\n建议：设置 → 音频 里重新选择一次对应设备。"));
            }
            else
            {
                items.Add(new EnvCheckItem("ok", "OBS 输入设备",
                    s.ObsAudioInputs.Count == 0
                        ? "OBS 当前没有配置麦克风 / 输入源（只用桌面音频时可忽略）。"
                        : $"OBS 的 {s.ObsAudioInputs.Count} 个音频输入都能匹配到系统活动设备。"));
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
