using System.Globalization;

namespace OBS_Helper.Wpf.Services.Tools;

/// <summary>一次上行带宽 → 推流参数的推荐结果。</summary>
public sealed class BandwidthRecommendation
{
    /// <summary>是否具备直播条件（带宽过低时为 false）。</summary>
    public bool Viable { get; init; }
    public int BitrateKbps { get; init; }
    public string Resolution { get; init; } = "";
    public int Fps { get; init; }
    /// <summary>给人看的结论与调整建议（多行）。</summary>
    public string Advice { get; init; } = "";

    public static BandwidthRecommendation NotViable(string reason) => new()
    {
        Viable = false,
        BitrateKbps = 0,
        Resolution = "",
        Fps = 0,
        Advice = reason
    };
}

/// <summary>
/// 推流带宽顾问（纯函数，供单元测试）。
///
/// 经验规则：推流码率取实测上行的 60~70%（留出网络抖动、语音通话等余量），
/// 再按码率档位映射到分辨率 / 帧率组合。档位参考 Twitch / B站 / YouTube 的公开推荐值。
/// </summary>
public static class BandwidthAdvisorCore
{
    /// <summary>多路推流的冗余系数：编码器开销 + 各平台连接波动。</summary>
    public const double MultiStreamHeadroom = 1.2;

    // ---- 输入安全上限：防止异常大数值导致溢出或荒谬结论 ----
    /// <summary>上行带宽输入上限（10Gbps），超出按此值截断。</summary>
    public const double MaxUploadMbps = 10_000;
    /// <summary>多路推流路数上限，超出按此值截断。</summary>
    public const int MaxStreams = 32;
    /// <summary>单路码率输入上限（kbps，约等于 OBS 编码器可设置的最大值量级）。</summary>
    public const int MaxSingleBitrateKbps = 100_000;

    private static double Clamp(double v, double max) => v > max ? max : v;
    public static int ClampToInt(double v, int max)
        => double.IsNaN(v) || v <= 0 ? 0 : (int)(v > max ? max : v);

    /// <summary>
    /// 按实测上行带宽推荐单路推流参数。<paramref name="uploadMbps"/> 为测速得到的上行速率（Mbps）。
    /// </summary>
    public static BandwidthRecommendation Recommend(double uploadMbps)
    {
        if (double.IsNaN(uploadMbps) || uploadMbps <= 0)
            return BandwidthRecommendation.NotViable("请输入有效的上行带宽数值。");

        // 钳制异常大的输入，避免后续整型换算溢出
        uploadMbps = Clamp(uploadMbps, MaxUploadMbps);

        // 安全码率 = 上行 × 65%，向下取整到 100kbps，避免贴线
        var safeKbps = (int)(uploadMbps * 650 / 100) * 100;

        if (safeKbps < 1500)
        {
            return BandwidthRecommendation.NotViable(
                $"上行 {uploadMbps:0.#}Mbps 不足以稳定直播（安全码率仅约 {safeKbps}kbps）。" +
                "\n建议：改用有线网络；关闭占用上行的程序（网盘同步 / 下载）；或降低需求后再试。");
        }

        // 档位从高到低匹配
        var (bitrate, resolution, fps, extra) = safeKbps switch
        {
            >= 8000 => (8000, "1920x1080", 60, "1080p60 高画质档，适合游戏 / 高动态内容。"),
            >= 6000 => (6000, "1920x1080", 60, "1080p60 主流档；若编码过载可降到 30fps 保画质。"),
            >= 4500 => (4500, "1920x1080", 30, "1080p30 稳妥档，人像 / 桌面类内容足够清晰。"),
            >= 3000 => (3000, "1280x720", 60, "720p60 流畅档，优先保帧率。"),
            >= 2000 => (2000, "1280x720", 30, "720p30 入门档，静态画面场景可用。"),
            _ => (1500, "960x540 或更低", 30, "勉强可播档，强烈建议先改善网络。")
        };

        return new BandwidthRecommendation
        {
            Viable = true,
            BitrateKbps = bitrate,
            Resolution = resolution,
            Fps = fps,
            Advice =
                $"实测上行 {uploadMbps:0.##}Mbps，按 65% 安全系数 ≈ {safeKbps}kbps 可用。\n" +
                $"推荐：码率 {bitrate}kbps · 输出分辨率 {resolution} · {fps}fps。\n" +
                $"{extra}\n" +
                "提示：开启动态码率（设置 → 推流 → 网络相关）可在波动时自动降码；WiFi 不稳时换有线。"
        };
    }

    /// <summary>
    /// 多路推流所需上行（Mbps）：路数 × 单路码率 × 冗余系数。
    /// 路数与单路码率会先按安全上限截断，防止异常输入导致溢出。
    /// </summary>
    public static double RequiredUploadMbps(int streams, int singleBitrateKbps, double headroom = MultiStreamHeadroom)
        => Math.Clamp(streams, 0, MaxStreams) * (double)Math.Clamp(singleBitrateKbps, 0, MaxSingleBitrateKbps) * headroom / 1000.0;

    /// <summary>判断当前上行能否承载多路推流。</summary>
    public static bool CanSustain(double uploadMbps, int streams, int singleBitrateKbps)
        => uploadMbps > 0 && uploadMbps + 1e-9 >= RequiredUploadMbps(streams, singleBitrateKbps);

    /// <summary>多路推流的结论文案（含判定），供界面直接展示。</summary>
    public static string DescribeMultiStream(double uploadMbps, int streams, int singleBitrateKbps)
    {
        if (streams <= 0 || singleBitrateKbps <= 0)
            return "请填写有效的路数与单路码率。";

        streams = Math.Clamp(streams, 1, MaxStreams);
        singleBitrateKbps = Math.Clamp(singleBitrateKbps, 1, MaxSingleBitrateKbps);
        uploadMbps = Clamp(uploadMbps, MaxUploadMbps);

        var required = RequiredUploadMbps(streams, singleBitrateKbps);
        var total = streams * singleBitrateKbps;
        var ok = CanSustain(uploadMbps, streams, singleBitrateKbps);

        var head = $"{streams} 路 × {singleBitrateKbps}kbps = 总码率 {total}kbps，" +
                   $"按 20% 冗余需要上行 ≥ {required.ToString("0.##", CultureInfo.InvariantCulture)}Mbps。";
        if (!ok)
        {
            return head + $"\n当前上行 {uploadMbps:0.##}Mbps 不够用。" +
                "\n建议：降低单路码率、减少路数；或使用支持转发的多播服务（如 Restream）把上行压力交给服务端。";
        }
        var margin = uploadMbps - required;
        return head + $"\n当前上行 {uploadMbps:0.##}Mbps 可以承载，余量 {margin:0.##}Mbps。" +
            (margin < 1 ? "\n注意：余量偏小，直播中避免其他设备占用上行（网盘 / 下载 / 其他主播）。" : "");
    }
}
