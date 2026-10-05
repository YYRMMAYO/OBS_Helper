using OBS_Helper.Wpf.Localization;
namespace OBS_Helper.Wpf.Services.Tools;

/// <summary>编码器顾问的一次推荐结果。</summary>
public sealed class EncoderAdvice
{
    /// <summary>识别到的显卡厂商：NVIDIA / AMD / Intel / 未知。</summary>
    public string Vendor { get; init; } = Strings.T("encoder.vendor.unknown");
    /// <summary>识别出的显卡名（未识别时为空）。</summary>
    public string GpuName { get; init; } = "";
    /// <summary>是否检测到支持 AV1 编码的显卡代际。</summary>
    public bool Av1Capable { get; init; }
    /// <summary>给人看的参数组合与调整建议（多行）。</summary>
    public string Advice { get; init; } = "";
}

/// <summary>
/// 编码顾问核心（纯函数，供单元测试）。
///
/// 输入显卡名字符串与用途场景，输出具体的预设 / 速率控制建议：
/// - NVIDIA RTX 30 系及以上：NVENC P5；GTX / RTX 20 系及更早：P4；
/// - AV1 仅 RTX 40/50、RX 7000、Arc 等新硬件可用；
/// - 录像推荐 CQP 恒定质量（H.264 18~20 / HEVC +2 / AV1 22）；
/// - 「边播边录」双编码叠加约 10~15% GPU 占用。
/// </summary>
public static class EncoderAdvisorCore
{
    /// <summary>双编码相对单路推流的额外 GPU 占用（调研参考值下限）。</summary>
    public const double DualEncodeExtraRatio = 0.10;
    /// <summary>双编码相对单路推流的额外 GPU 占用（调研参考值上限）。</summary>
    public const double DualEncodeExtraMaxRatio = 0.15;

    /// <summary>用途场景。</summary>
    public enum Scenario
    {
        Stream,
        Record,
        Both
    }

    /// <summary>从显卡名推断厂商。无法识别返回 null。</summary>
    public static string? DetectVendor(string? gpuName)
    {
        var s = gpuName ?? "";
        if (s.Contains("nvidia", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("geforce", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("quadro", StringComparison.OrdinalIgnoreCase))
            return "NVIDIA";
        if (s.Contains("radeon", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("amd", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("ati ", StringComparison.OrdinalIgnoreCase))
            return "AMD";
        if (s.Contains("intel", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("arc", StringComparison.OrdinalIgnoreCase) &&
            !s.Contains("nvidia", StringComparison.OrdinalIgnoreCase))
            return "Intel";
        return null;
    }

    /// <summary>NVIDIA 显卡是否支持 AV1 编码（RTX 40 系 / 50 系起）。</summary>
    public static bool NvencAv1Capable(string? gpuName)
    {
        var s = gpuName ?? "";
        // 命中 "RTX 40" / "RTX 4090" / "5070" 等 40/50 系数字段
        foreach (var gen in new[] { "40", "50" })
        {
            var idx = s.IndexOf("rtx " + gen, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var tail = s[(idx + 4)..];
                if (tail.Length > 0 && char.IsDigit(tail[0])) return true;
                // "rtx 40" 后无数字也按命中处理（如营销写法）
                return true;
            }
        }
        return false;
    }

    /// <summary>生成参数组合建议。<paramref name="gpuName"/> 可为空（未知显卡给通用建议）。</summary>
    public static EncoderAdvice Recommend(string? gpuName, Scenario scenario, bool dualEncode)
    {
        var vendor = DetectVendor(gpuName) ?? Strings.T("encoder.vendor.unknown");
        var gpu = (gpuName ?? "").Trim();
        var av1 = vendor switch
        {
            "NVIDIA" => NvencAv1Capable(gpu),
            _ => false
        };

        var lines = new List<string>();

        lines.Add(vendor switch
        {
            "NVIDIA" when av1 => Strings.T("encoder.headline.nvidiaAv1", gpu),
            "NVIDIA" => Strings.T("encoder.headline.nvidia", gpu),
            "AMD" => Strings.T("encoder.headline.amd", FormatGpu(gpu)),
            "Intel" => Strings.T("encoder.headline.intel", FormatGpu(gpu)),
            _ => Strings.T("encoder.headline.unknown")
        });

        // 推流侧
        if (scenario != Scenario.Record)
        {
            lines.Add(vendor switch
            {
                "NVIDIA" => NvencStreamPreset(gpu),
                "AMD" => Strings.T("encoder.stream.amd"),
                "Intel" => Strings.T("encoder.stream.intel"),
                _ => Strings.T("encoder.stream.unknown")
            });
            lines.Add(Strings.T("encoder.stream.rateControl"));
        }

        // 录像侧
        if (scenario != Scenario.Stream)
        {
            lines.Add(vendor switch
            {
                "NVIDIA" when av1 => Strings.T("encoder.record.nvidiaAv1"),
                "NVIDIA" => Strings.T("encoder.record.nvidia"),
                "AMD" => Strings.T("encoder.record.amd"),
                "Intel" => Strings.T("encoder.record.intel"),
                _ => Strings.T("encoder.record.unknown")
            });
            lines.Add(Strings.T("encoder.record.note"));
        }

        // 双编码预算
        if (dualEncode || scenario == Scenario.Both)
        {
            lines.Add(Strings.T("encoder.dual", DualEncodeExtraRatio, DualEncodeExtraMaxRatio));
        }

        return new EncoderAdvice
        {
            Vendor = vendor,
            GpuName = gpu,
            Av1Capable = av1,
            Advice = string.Join("\n", lines)
        };
    }

    /// <summary>
    /// 显卡名后缀：NVIDIA 的模板句里已经带了显卡名（<c>检测到 {0}（…）</c>），所以不再重复拼；
    /// 别的厂商的模板句是 <c>检测到 AMD 显卡{0}：</c> 这种，需要把具体型号补进去。
    ///
    /// 这里**按厂商判定**，不去截本地化文案的前两个字符 ——
    /// 那种写法只在中文模板下碰巧成立（英文模板取到的是 <c>"{0"</c>），
    /// 而且随文案改动静默失效（V2.9.4 审查发现）。
    /// </summary>
    private static string FormatGpu(string gpu)
    {
        if (gpu.Length == 0) return "";
        var vendor = DetectVendor(gpu);
        return vendor == "NVIDIA" ? "" : $"（{gpu}）";
    }

    private static string NvencStreamPreset(string gpu)
    {
        var s = gpu.ToLowerInvariant();
        // RTX 30/40/50 系 → P5；GTX / RTX 20 及更早 → P4
        var modern = ContainsAny(s, "rtx 30", "rtx 40", "rtx 50") ||
                     (s.Contains("rtx") && !s.Contains("rtx 20"));
        return modern
            ? Strings.T("encoder.stream.nvenc.new")
            : Strings.T("encoder.stream.nvenc.old");
    }

    private static bool ContainsAny(string source, params string[] keys)
        => keys.Any(k => source.Contains(k));
}