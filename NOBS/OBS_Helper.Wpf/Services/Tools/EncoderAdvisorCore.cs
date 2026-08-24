namespace OBS_Helper.Wpf.Services.Tools;

/// <summary>编码器顾问的一次推荐结果。</summary>
public sealed class EncoderAdvice
{
    /// <summary>识别到的显卡厂商：NVIDIA / AMD / Intel / 未知。</summary>
    public string Vendor { get; init; } = "未知";
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
        var vendor = DetectVendor(gpuName) ?? "未知";
        var gpu = (gpuName ?? "").Trim();
        var av1 = vendor switch
        {
            "NVIDIA" => NvencAv1Capable(gpu),
            _ => false
        };

        var lines = new List<string>();

        lines.Add(vendor switch
        {
            "NVIDIA" when av1 => $"检测到 {gpu}（支持 AV1 编码）：",
            "NVIDIA" => $"检测到 {gpu}（不支持 AV1，用 H.264/HEVC）：",
            "AMD" => $"检测到 AMD 显卡{FormatGpu(gpu)}：",
            "Intel" => $"检测到 Intel 核显 / Arc{FormatGpu(gpu)}：",
            _ => "未能识别显卡型号，以下为通用建议（可在设备管理器或日志分析里确认型号）："
        });

        // 推流侧
        if (scenario != Scenario.Record)
        {
            lines.Add(vendor switch
            {
                "NVIDIA" => NvencStreamPreset(gpu),
                "AMD" => "推流：AMF H.264，预设 Quality；过载先降一档预设。",
                "Intel" => "推流：QuickSync (QSV) H.264，预设 Balanced 起步，驱动较新可试 Quality。",
                _ => "推流：有独显优先对应硬件编码（NVENC / AMF / QSV）；核显机器用 QSV 或 x264 veryfast。"
            });
            lines.Add("速率控制 CBR · 关键帧间隔固定 2 秒（不要设 0）。");
        }

        // 录像侧
        if (scenario != Scenario.Stream)
        {
            lines.Add(vendor switch
            {
                "NVIDIA" when av1 => "录像：AV1 CQP 22（约比 HEVC 同观感小 30%）；剪辑软件不支持 AV1 时改 HEVC CQP 18。",
                "NVIDIA" => "录像：HEVC CQP 18（基本无损档）或 H.264 CQP 18~20；速率控制在「输出 → 录像」里改为 CQP。",
                "AMD" => "录像：AMF HEVC，速率控制选 CQP，数值 18~20。",
                "Intel" => "录像：QSV HEVC，速率控制选 ICQ（质量值 18~22）。",
                _ => "录像：速率控制改 CQP / CRF，H.264 取 18~23（越小质量越高体积越大）。"
            });
            lines.Add("静态画面多可再放宽 2 个点；磁盘紧张优先上调 CQP 而不是换 CBR。");
        }

        // 双编码预算
        if (dualEncode || scenario == Scenario.Both)
        {
            lines.Add($"注意：边播边录是两路独立编码，GPU 额外增加约 {DualEncodeExtraRatio:P0}~{DualEncodeExtraMaxRatio:P0}。" +
                      "\n开播前在吃配置的场景同时跑 10 分钟压测；过载时先降录像档位，最后才动推流参数。");
        }

        return new EncoderAdvice
        {
            Vendor = vendor,
            GpuName = gpu,
            Av1Capable = av1,
            Advice = string.Join("\n", lines)
        };
    }

    private static string FormatGpu(string gpu)
        => gpu.Length > 0 && !gpu.StartsWith("检测", StringComparison.Ordinal) ? $"（{gpu}）" : "";

    private static string NvencStreamPreset(string gpu)
    {
        var s = gpu.ToLowerInvariant();
        // RTX 30/40/50 系 → P5；GTX / RTX 20 及更早 → P4
        var modern = ContainsAny(s, "rtx 30", "rtx 40", "rtx 50") ||
                     (s.Contains("rtx") && !s.Contains("rtx 20"));
        return modern
            ? "推流：NVENC H.264，预设 P5（质量与性能平衡点），多帧质量选「两遍（四分）」。"
            : "推流：NVENC H.264，预设 P4（老一代 NVENC 的平衡档）。";
    }

    private static bool ContainsAny(string source, params string[] keys)
        => keys.Any(k => source.Contains(k));
}
