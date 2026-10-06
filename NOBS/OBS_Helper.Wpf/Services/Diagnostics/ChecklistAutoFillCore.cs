using OBS_Helper.Wpf.Localization;

namespace OBS_Helper.Wpf.Services.Diagnostics;

/// <summary>自检清单项的本机判定结论。</summary>
public enum ChecklistStatus
{
    /// <summary>本机实测符合这一条。</summary>
    Pass,
    /// <summary>本机实测**不符合**或存在风险，值得处理。</summary>
    Attention,
    /// <summary>本机无法判定（缺少样本 / 未连接 OBS）—— 如实说「不知道」，不假装通过。</summary>
    Unknown
}

/// <summary>一条自动回填结果。</summary>
public sealed record ChecklistAutoResult(string ItemKey, ChecklistStatus Status, string Evidence);

/// <summary>
/// 自动回填所需的**本机事实**（V3.0 / D4）。全部可空：探测不到就保持 null，判定为「无法判定」。
///
/// 为什么不直接传一堆检查服务的报告：那些报告带的是本地化文案，拿文案反推结论既脆弱又不可测。
/// 这里只放**原始事实**，「事实 → 结论」的映射集中在 <see cref="ChecklistAutoFillCore.Evaluate"/> 里，可单测。
/// </summary>
public sealed record ChecklistEvidence
{
    // ① 管理员权限
    public bool? Elevated { get; init; }

    // ② 编码器
    public IReadOnlyList<string>? EncoderNames { get; init; }

    // ③ 码率 ≤ 上行 75%
    public int? VideoBitrateKbps { get; init; }
    public double? UplinkMbps { get; init; }

    // ④ 捕获方式与双显卡
    public int? GpuCount { get; init; }
    public bool? ObsGpuPreferenceSet { get; init; }

    // ⑤ 48kHz
    public int? AudioSampleRateHz { get; init; }

    // ⑥ 冲突软件硬件加速
    public int? HardwareAccelConflicts { get; init; }

    // ⑦ 有线网络与丢帧
    public bool? OnWiredNetwork { get; init; }
    public double? DroppedRatio { get; init; }

    // ⑧ 推流服务
    public bool? StreamHasServer { get; init; }
    public bool? StreamHasKey { get; init; }
}

/// <summary>
/// 自检清单自动回填核心（V3.0 / D4）。纯逻辑、零 IO，单测工程直接链接编译。
///
/// 背景（提案原文）：诊断页那 8 条清单全靠用户自己勾，而其中 6~7 条本机就能判定 ——
/// 让用户对着「OBS 是否以管理员身份运行」这种问题自查，等于把可以自动化的排查手工化。
///
/// 三态而不是两态是关键：<see cref="ChecklistStatus.Unknown"/> 与「未通过」完全不同 ——
/// 本机没跑过带宽测算时，把「码率不超过上行 75%」判成失败是**冤枉**用户；
/// 判成通过又是撒谎。这里一律如实给 Unknown 并说明为什么。
/// </summary>
public static class ChecklistAutoFillCore
{
    /// <summary>上行余量：码率不应超过实测上行的 75%。</summary>
    public const double UplinkHeadroom = 0.75;

    /// <summary>丢帧率超过这个比例就值得提醒。</summary>
    public const double DroppedAttentionRatio = 0.05;

    /// <summary>判定这 8 条清单（顺序与界面一致）。</summary>
    public static IReadOnlyList<ChecklistAutoResult> Evaluate(ChecklistEvidence e) => new[]
    {
        Item1(e), Item2(e), Item3(e), Item4(e), Item5(e), Item6(e), Item7(e), Item8(e)
    };

    // ① 管理员权限
    private static ChecklistAutoResult Item1(ChecklistEvidence e)
    {
        if (e.Elevated is not { } elevated)
            return Unknown("diagnostic.check.1", "check.auto.admin.unknown");

        return elevated
            ? Pass("diagnostic.check.1", "check.auto.admin.yes")
            : Attention("diagnostic.check.1", "check.auto.admin.no");
    }

    // ② 硬件编码
    private static ChecklistAutoResult Item2(ChecklistEvidence e)
    {
        var names = e.EncoderNames?.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToList();
        if (names is null || names.Count == 0)
            return Unknown("diagnostic.check.2", "check.auto.encoder.unknown");

        var software = names.Where(n => !IsHardwareEncoder(n)).ToList();
        return software.Count == 0
            ? Pass("diagnostic.check.2", "check.auto.encoder.hardware", string.Join(" / ", names))
            : Attention("diagnostic.check.2", "check.auto.encoder.software", string.Join(" / ", software));
    }

    /// <summary>编码器标识是否属于硬件编码（NVENC / AMF / QSV / Apple / VAAPI 等）。</summary>
    public static bool IsHardwareEncoder(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var n = name.ToLowerInvariant();
        if (n.Contains("x264") || n.Contains("x265")) return false;   // 软件编码
        return n.Contains("nvenc") || n.Contains("jim") || n.Contains("amf")
            || n.Contains("qsv") || n.Contains("videotoolbox") || n.Contains("vaapi")
            || n.Contains("av1") || n.Contains("aom") || n.Contains("svt");
    }

    // ③ 码率 ≤ 上行 75%
    private static ChecklistAutoResult Item3(ChecklistEvidence e)
    {
        if (e.VideoBitrateKbps is not { } bitrate || bitrate <= 0)
            return Unknown("diagnostic.check.3", "check.auto.bitrate.noBitrate");

        if (e.UplinkMbps is not { } uplink || uplink <= 0)
            return Unknown("diagnostic.check.3", "check.auto.bitrate.noUplink", bitrate);

        var budgetKbps = uplink * 1000 * UplinkHeadroom;
        return bitrate <= budgetKbps
            ? Pass("diagnostic.check.3", "check.auto.bitrate.ok", bitrate, uplink.ToString("0.#"))
            : Attention("diagnostic.check.3", "check.auto.bitrate.over", bitrate, uplink.ToString("0.#"),
                (budgetKbps / 1000).ToString("0.#"));
    }

    // ④ 捕获方式与双显卡
    private static ChecklistAutoResult Item4(ChecklistEvidence e)
    {
        if (e.GpuCount is not { } gpus || gpus <= 0)
            return Unknown("diagnostic.check.4", "check.auto.capture.unknown");

        if (gpus == 1)
            return Pass("diagnostic.check.4", "check.auto.capture.singleGpu");

        // 多显卡：真正的坑是「游戏跑在独显、OBS 跑在核显」，而这个只能用 GPU 首选项来降低概率
        return e.ObsGpuPreferenceSet == true
            ? Pass("diagnostic.check.4", "check.auto.capture.multiGpuPreferred", gpus)
            : Attention("diagnostic.check.4", "check.auto.capture.multiGpu", gpus);
    }

    // ⑤ 48kHz
    private static ChecklistAutoResult Item5(ChecklistEvidence e)
    {
        if (e.AudioSampleRateHz is not { } rate || rate <= 0)
            return Unknown("diagnostic.check.5", "check.auto.samplerate.unknown");

        return rate == 48000
            ? Pass("diagnostic.check.5", "check.auto.samplerate.ok")
            : Attention("diagnostic.check.5", "check.auto.samplerate.bad", rate);
    }

    // ⑥ 冲突软件的硬件加速
    private static ChecklistAutoResult Item6(ChecklistEvidence e)
    {
        if (e.HardwareAccelConflicts is not { } hits)
            return Unknown("diagnostic.check.6", "check.auto.conflict.unknown");

        return hits == 0
            ? Pass("diagnostic.check.6", "check.auto.conflict.none")
            : Attention("diagnostic.check.6", "check.auto.conflict.found", hits);
    }

    // ⑦ 有线网络 / 丢帧
    private static ChecklistAutoResult Item7(ChecklistEvidence e)
    {
        if (e.DroppedRatio is { } dropped && dropped > DroppedAttentionRatio)
            return Attention("diagnostic.check.7", "check.auto.network.dropped", (dropped * 100).ToString("0.#"));

        if (e.OnWiredNetwork is not { } wired)
            return Unknown("diagnostic.check.7", "check.auto.network.unknown");

        return wired
            ? Pass("diagnostic.check.7", "check.auto.network.wired")
            : Attention("diagnostic.check.7", "check.auto.network.wireless");
    }

    // ⑧ 推流服务与密钥
    private static ChecklistAutoResult Item8(ChecklistEvidence e)
    {
        if (e.StreamHasServer is not { } server)
            return Unknown("diagnostic.check.8", "check.auto.stream.unknown");

        if (!server)
            return Attention("diagnostic.check.8", "check.auto.stream.noServer");
        if (e.StreamHasKey == false)
            return Attention("diagnostic.check.8", "check.auto.stream.noKey");

        return Pass("diagnostic.check.8", "check.auto.stream.ok");
    }

    // ---------------------------------------------------------------- 构造与汇总

    private static ChecklistAutoResult Pass(string key, string evidenceKey, params object[] args)
        => new(key, ChecklistStatus.Pass, Strings.T(evidenceKey, args));

    private static ChecklistAutoResult Attention(string key, string evidenceKey, params object[] args)
        => new(key, ChecklistStatus.Attention, Strings.T(evidenceKey, args));

    private static ChecklistAutoResult Unknown(string key, string evidenceKey, params object[] args)
        => new(key, ChecklistStatus.Unknown, Strings.T(evidenceKey, args));

    /// <summary>可以自动判定的条数（用于界面提示「8 条里 N 条已按本机实测判定」）。</summary>
    public static int DecidedCount(IReadOnlyList<ChecklistAutoResult> results)
        => results.Count(r => r.Status != ChecklistStatus.Unknown);

    /// <summary>状态 → 清单勾选：只有「通过」才代表这一条已满足。</summary>
    public static bool ShouldCheck(ChecklistStatus status) => status == ChecklistStatus.Pass;
}
