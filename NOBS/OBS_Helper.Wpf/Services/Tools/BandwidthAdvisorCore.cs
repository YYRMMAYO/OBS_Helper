using OBS_Helper.Wpf.Localization;
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
            return BandwidthRecommendation.NotViable(Strings.T("bandwidth.invalid"));

        // 钳制异常大的输入，避免后续整型换算溢出
        uploadMbps = Clamp(uploadMbps, MaxUploadMbps);

        // 安全码率 = 上行 × 65%，向下取整到 100kbps，避免贴线
        var safeKbps = (int)(uploadMbps * 650 / 100) * 100;

        if (safeKbps < 1500)
        {
            return BandwidthRecommendation.NotViable(
                Strings.T("bandwidth.notViable", uploadMbps, safeKbps));
        }

        // 档位从高到低匹配
        var (bitrate, resolution, fps, extra) = safeKbps switch
        {
            >= 8000 => (8000, "1920x1080", 60, Strings.T("bandwidth.tier.8000")),
            >= 6000 => (6000, "1920x1080", 60, Strings.T("bandwidth.tier.6000")),
            >= 4500 => (4500, "1920x1080", 30, Strings.T("bandwidth.tier.4500")),
            >= 3000 => (3000, "1280x720", 60, Strings.T("bandwidth.tier.3000")),
            >= 2000 => (2000, "1280x720", 30, Strings.T("bandwidth.tier.2000")),
            _ => (1500, Strings.T("bandwidth.tier.lowRes"), 30, Strings.T("bandwidth.tier.low"))
        };

        return new BandwidthRecommendation
        {
            Viable = true,
            BitrateKbps = bitrate,
            Resolution = resolution,
            Fps = fps,
            Advice =
                Strings.T("bandwidth.advice", uploadMbps, safeKbps, bitrate, resolution, fps, extra)
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
            return Strings.T("bandwidth.multiInvalid");

        streams = Math.Clamp(streams, 1, MaxStreams);
        singleBitrateKbps = Math.Clamp(singleBitrateKbps, 1, MaxSingleBitrateKbps);
        uploadMbps = Clamp(uploadMbps, MaxUploadMbps);

        var required = RequiredUploadMbps(streams, singleBitrateKbps);
        var total = streams * singleBitrateKbps;
        var ok = CanSustain(uploadMbps, streams, singleBitrateKbps);

        var head = Strings.T("bandwidth.multiHead", streams, singleBitrateKbps, total, required.ToString("0.##", CultureInfo.InvariantCulture));
        if (!ok)
        {
            return head + Strings.T("bandwidth.multiNotEnough", uploadMbps);
        }
        var margin = uploadMbps - required;
        return head + Strings.T("bandwidth.multiOk", uploadMbps, margin) +
            (margin < 1 ? Strings.T("bandwidth.multiTight") : "");
    }
}