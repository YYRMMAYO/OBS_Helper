using OBS_Helper.Wpf.Localization;
using OBS_Helper.Wpf.Services.ObsConfig;

namespace OBS_Helper.Wpf.Services.Diagnostics;

/// <summary>体检条目的领域（决定图标与分组）。</summary>
public enum MachineArea
{
    Graphics,
    Encoder,
    Network,
    Disk,
    Recording
}

/// <summary>体检条目的严重度。</summary>
public enum MachineSeverity
{
    /// <summary>必须先解决，否则录/播会出问题。</summary>
    Blocker,
    /// <summary>建议优化，不做也能用。</summary>
    Recommend,
    /// <summary>仅供参考的信息。</summary>
    Info
}

/// <summary>一条体检结论。<paramref name="FloorKb"/> 等具体数字由调用方拼进文案。</summary>
public sealed record MachineItem(MachineSeverity Severity, MachineArea Area, string Title, string Detail);

/// <summary>体检输入：全部可空，探测不到就保持 null（判成「未知」，不猜）。</summary>
public sealed record MachineProfileInputs
{
    // 图形环境
    public bool? Elevated { get; init; }
    public bool? GameDvrEnabled { get; init; }
    public int? HwSchMode { get; init; }
    public int? GpuCount { get; init; }
    public bool? ObsGpuPreferenceSet { get; init; }
    public bool? OnBattery { get; init; }
    public string? ActivePowerScheme { get; init; }

    // 编码与音频
    public IReadOnlyList<string>? EncoderNames { get; init; }
    public int? AudioSampleRateHz { get; init; }

    // 输出与网络
    public int? StreamBitrateKbps { get; init; }
    public double? UplinkMbps { get; init; }
    public double? IngestRttMs { get; init; }

    // 磁盘
    public double? DiskWriteMbps { get; init; }
    public double? DiskFreeGb { get; init; }

    /// <summary>录制格式（<c>mkv</c> / <c>mp4</c> / <c>fragmented_mp4</c> / <c>hybrid_mp4</c> 等）。</summary>
    public string? RecordingFormat { get; init; }
}

/// <summary>体检报告：一句结论 + 按严重度排序的条目 + 可落地的录制档位建议。</summary>
public sealed record MachineProfile(
    string Conclusion,
    IReadOnlyList<MachineItem> Items,
    /// <summary>建议的「简单录像」档位；null = 不建议改动（例如已在录制 / 机器信息不足）。</summary>
    SimplePresetId? SuggestedPreset)
{
    public int BlockerCount => Items.Count(i => i.Severity == MachineSeverity.Blocker);
    public int RecommendCount => Items.Count(i => i.Severity == MachineSeverity.Recommend);
}

/// <summary>
/// 「开播前体检」核心（V3.0 / D4 后半段）。纯逻辑、零 IO，单测工程直接链接编译。
///
/// 为什么值得做：黑屏体检、编码顾问、带宽计算、磁盘测速、节点探测**各自都是一页**，
/// 用户要跑五遍才知道「我这台机器到底行不行」。这里把它们合成**一个结论**：
/// 先按严重度排好，再给一句人话总结，并在录制侧给出可一键落地的档位。
///
/// 边界（与提案一致）：**推流侧只给建议，绝不自动改**。推流参数与平台/网络强相关，
/// 自动改的风险远大于收益；而录制侧本来就是本产品既有的可回滚落地路径。
/// </summary>
public static class MachineProfileCore
{
    /// <summary>丢包/延迟参考线：RTT 超过这个值说明到节点的链路不理想。</summary>
    public const double RttWarnMs = 150;

    /// <summary>录制盘剩余空间的警戒线（GB）。</summary>
    public const double DiskFreeWarnGb = 20;

    /// <summary>码率不应超过上行的 75%（与清单自动回填同一口径）。</summary>
    public const double UplinkHeadroom = ChecklistAutoFillCore.UplinkHeadroom;

    public static MachineProfile Build(MachineProfileInputs i)
    {
        var items = new List<MachineItem>();

        AddGraphics(items, i);
        AddEncoder(items, i);
        AddNetwork(items, i);
        AddDisk(items, i);
        AddRecording(items, i);

        var ordered = items
            .OrderBy(x => x.Severity)
            .ThenBy(x => x.Area)
            .ToList();

        var suggested = SuggestPreset(i, ordered);
        return new MachineProfile(BuildConclusion(ordered, suggested), ordered, suggested);
    }

    // ---------------------------------------------------------------- 各领域规则

    private static void AddGraphics(List<MachineItem> items, MachineProfileInputs i)
    {
        if (i.GameDvrEnabled == true)
            items.Add(Recommend(MachineArea.Graphics, "machine.graphics.gameDvr.title", "machine.graphics.gameDvr.detail"));

        if (i.GpuCount is > 1 && i.ObsGpuPreferenceSet == false)
            items.Add(Recommend(MachineArea.Graphics, "machine.graphics.gpuPref.title", "machine.graphics.gpuPref.detail"));

        if (i.OnBattery == true)
            items.Add(Recommend(MachineArea.Graphics, "machine.graphics.battery.title", "machine.graphics.battery.detail"));

        if (i.ActivePowerScheme is { Length: > 0 } scheme && LooksLikePowerSaver(scheme))
            items.Add(Recommend(MachineArea.Graphics, "machine.graphics.powerScheme.title",
                "machine.graphics.powerScheme.detail", scheme));

        if (i.Elevated == false)
            items.Add(Info(MachineArea.Graphics, "machine.graphics.elevated.title", "machine.graphics.elevated.detail"));

        if (i.HwSchMode == 1)
            items.Add(Info(MachineArea.Graphics, "machine.graphics.hags.title", "machine.graphics.hags.detail"));
    }

    /// <summary>电源计划名可能是中英文本地化的，按关键词粗判（判不出来就不提这一条）。</summary>
    private static bool LooksLikePowerSaver(string scheme)
    {
        var s = scheme.ToLowerInvariant();
        return s.Contains("saver") || s.Contains("power saving") || s.Contains("节能") || s.Contains("省电");
    }

    private static void AddEncoder(List<MachineItem> items, MachineProfileInputs i)
    {
        var names = i.EncoderNames?.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToList();
        if (names is null || names.Count == 0)
        {
            items.Add(Info(MachineArea.Encoder, "machine.encoder.unknown.title", "machine.encoder.unknown.detail"));
            return;
        }

        var software = names.Where(n => !ChecklistAutoFillCore.IsHardwareEncoder(n)).ToList();
        if (software.Count > 0)
            items.Add(Recommend(MachineArea.Encoder, "machine.encoder.software.title",
                "machine.encoder.software.detail", string.Join(" / ", software)));
        else
            items.Add(Info(MachineArea.Encoder, "machine.encoder.hardware.title",
                "machine.encoder.hardware.detail", string.Join(" / ", names)));
    }

    private static void AddNetwork(List<MachineItem> items, MachineProfileInputs i)
    {
        if (i.IngestRttMs is { } rtt && rtt > RttWarnMs)
            items.Add(Recommend(MachineArea.Network, "machine.network.rtt.title",
                "machine.network.rtt.detail", rtt.ToString("0")));

        if (i.StreamBitrateKbps is { } bitrate && bitrate > 0)
        {
            if (i.UplinkMbps is { } uplink && uplink > 0)
            {
                var budget = uplink * 1000 * UplinkHeadroom;
                if (bitrate > budget)
                    items.Add(Recommend(MachineArea.Network, "machine.network.bitrate.title",
                        "machine.network.bitrate.detail", bitrate, uplink.ToString("0.#"), (budget / 1000).ToString("0.#")));
            }
            else
            {
                items.Add(Info(MachineArea.Network, "machine.network.noUplink.title",
                    "machine.network.noUplink.detail", bitrate));
            }
        }
    }

    private static void AddDisk(List<MachineItem> items, MachineProfileInputs i)
    {
        if (i.DiskWriteMbps is { } write && i.StreamBitrateKbps is { } bitrate && bitrate > 0)
        {
            var required = DiskBenchmarkRequirement(bitrate);
            if (write < required)
                items.Add(Blocker(MachineArea.Disk, "machine.disk.slow.title",
                    "machine.disk.slow.detail", write.ToString("0"), required.ToString("0")));
        }

        if (i.DiskFreeGb is { } free && free < DiskFreeWarnGb)
            items.Add(Blocker(MachineArea.Disk, "machine.disk.full.title",
                "machine.disk.full.detail", free.ToString("0.#")));
    }

    /// <summary>与工具箱磁盘基准同一换算（kbps → 所需 MB/s，含冗余）。</summary>
    private static double DiskBenchmarkRequirement(int bitrateKbps)
        => Math.Round(bitrateKbps / 8000.0 * 1.5, 2);

    private static void AddRecording(List<MachineItem> items, MachineProfileInputs i)
    {
        // 录制格式：MKV / 分片 MP4 才抗崩溃；普通 MP4 崩了就整段没了
        if (i.RecordingFormat is { Length: > 0 } format)
        {
            var f = format.ToLowerInvariant();
            var safe = f.Contains("mkv") || f.Contains("fragmented") || f.Contains("hybrid");
            if (!safe)
                items.Add(Recommend(MachineArea.Recording, "machine.recording.format.title",
                    "machine.recording.format.detail", format));
        }

        if (i.AudioSampleRateHz is { } rate && rate > 0 && rate != 48000)
            items.Add(Recommend(MachineArea.Recording, "machine.recording.sampleRate.title",
                "machine.recording.sampleRate.detail", rate));
    }

    // ---------------------------------------------------------------- 档位建议

    /// <summary>
    /// 录制档位建议。
    ///
    /// 口径：**先看有没有硬件编码**（软件编码时 60fps 会明显吃 CPU，建议 30fps 的会议档），
    /// **再看磁盘**（写入跟不上 60fps 的码率时退到 30fps）。磁盘与编码都未知时不建议改动。
    /// </summary>
    private static SimplePresetId? SuggestPreset(MachineProfileInputs i, IReadOnlyList<MachineItem> items)
    {
        if (items.Any(x => x.Severity == MachineSeverity.Blocker)) return null;   // 先解决问题，再谈档位

        var names = i.EncoderNames?.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        if (names is null || names.Count == 0) return null;                        // 机器信息不足，不乱建议

        var hasHardware = names.All(ChecklistAutoFillCore.IsHardwareEncoder);
        if (!hasHardware) return SimplePresetId.Meeting;                           // 软件编码：30fps 省 CPU

        if (i.DiskWriteMbps is { } write && i.StreamBitrateKbps is { } bitrate && bitrate > 0)
        {
            // 60fps 的「快速」档按 1.25 倍码率估写入压力
            if (write < DiskBenchmarkRequirement((int)(bitrate * 1.25))) return SimplePresetId.Meeting;
        }

        return SimplePresetId.Game;   // 硬件编码 + 磁盘够快：60fps + 自动分段
    }

    // ---------------------------------------------------------------- 结论

    private static string BuildConclusion(IReadOnlyList<MachineItem> items, SimplePresetId? preset)
    {
        var blockers = items.Count(x => x.Severity == MachineSeverity.Blocker);
        var recommends = items.Count(x => x.Severity == MachineSeverity.Recommend);

        var head = blockers > 0
            ? Strings.T("machine.conclusion.blockers", blockers, recommends)
            : recommends > 0
                ? Strings.T("machine.conclusion.recommends", recommends)
                : Strings.T("machine.conclusion.clean");

        if (preset is { } p)
            head += " " + Strings.T("machine.conclusion.preset", SimpleRecordingCore.Get(p).Key);

        return head;
    }

    // ---------------------------------------------------------------- 构造辅助

    private static MachineItem Blocker(MachineArea area, string titleKey, string detailKey, params object[] args)
        => new(MachineSeverity.Blocker, area, Strings.T(titleKey), Strings.T(detailKey, args));

    private static MachineItem Recommend(MachineArea area, string titleKey, string detailKey, params object[] args)
        => new(MachineSeverity.Recommend, area, Strings.T(titleKey), Strings.T(detailKey, args));

    private static MachineItem Info(MachineArea area, string titleKey, string detailKey, params object[] args)
        => new(MachineSeverity.Info, area, Strings.T(titleKey), Strings.T(detailKey, args));

    /// <summary>领域 → 前缀符号（不靠颜色单独传达信息，见 ACCESSIBILITY.md 的无障碍口径）。</summary>
    public static string AreaSymbol(MachineArea area) => area switch
    {
        MachineArea.Graphics => "🖥",
        MachineArea.Encoder => "🎛",
        MachineArea.Network => "🌐",
        MachineArea.Disk => "💾",
        MachineArea.Recording => "🎬",
        _ => "•"
    };

    /// <summary>严重度 → 前缀符号。</summary>
    public static string SeveritySymbol(MachineSeverity severity) => severity switch
    {
        MachineSeverity.Blocker => "⛔",
        MachineSeverity.Recommend => "⚠️",
        _ => "ℹ️"
    };
}
