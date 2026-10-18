namespace OBS_Helper.Wpf.Services.Audio;

/// <summary>音频提醒的种类。</summary>
public enum AudioAlertKind
{
    None = 0,

    /// <summary>麦克风 / 音频输入处于静音。</summary>
    Muted,

    /// <summary>音量长期过低（可能选错了设备或推子拉到底）。</summary>
    TooQuiet
}

/// <summary>
/// 录制中静音 / 音量过低提醒的纯逻辑（V3.1.0 / §12.5.1 第 5 项）。
///
/// 口径：
/// <list type="bullet">
///   <item>只在录制 / 推流中生效；</item>
///   <item>静音持续 8 秒才提醒（避免用户主动短暂静音就被打断）；</item>
///   <item>音量低于 -55 dB 持续 10 秒才提醒（防止单帧抖动）；</item>
///   <item>只提醒，不自动取消静音、不自动改音量；</item>
///   <item>同一个输入在一次会话内只提醒一次（防告警疲劳）。</item>
/// </list>
/// </summary>
public static class AudioAlertCore
{
    /// <summary>静音持续多久才提醒。</summary>
    public static readonly TimeSpan MutedThreshold = TimeSpan.FromSeconds(8);

    /// <summary>低音量持续多久才提醒。</summary>
    public static readonly TimeSpan QuietThreshold = TimeSpan.FromSeconds(10);

    /// <summary>低于这个音量（dB）视为「过低」。0 dB 是正常满电平。</summary>
    public const double QuietDb = -55;

    /// <summary>
    /// 判断当前是否应该提醒。
    /// <paramref name="sustained"/> 是当前状态已持续的时间；<paramref name="alreadyAlerted"/> 表示本次会话已提醒过。
    /// </summary>
    public static AudioAlertKind Evaluate(
        bool outputActive,
        bool paused,
        bool muted,
        double volumeDb,
        TimeSpan sustained,
        bool alreadyAlerted)
    {
        if (!outputActive || paused || alreadyAlerted) return AudioAlertKind.None;

        if (muted)
            return sustained >= MutedThreshold ? AudioAlertKind.Muted : AudioAlertKind.None;

        if (volumeDb < QuietDb)
            return sustained >= QuietThreshold ? AudioAlertKind.TooQuiet : AudioAlertKind.None;

        return AudioAlertKind.None;
    }

    /// <summary>提醒文案键（标题 / 正文）。</summary>
    public static (string TitleKey, string MessageKey) KeysFor(AudioAlertKind kind) => kind switch
    {
        AudioAlertKind.Muted => ("audio.alert.mutedTitle", "audio.alert.mutedMessage"),
        AudioAlertKind.TooQuiet => ("audio.alert.quietTitle", "audio.alert.quietMessage"),
        _ => ("audio.alert.title", "audio.alert.message")
    };
}
