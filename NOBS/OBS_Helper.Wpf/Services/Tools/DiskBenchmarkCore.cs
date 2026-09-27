using OBS_Helper.Wpf.Localization;
namespace OBS_Helper.Wpf.Services.Tools;

/// <summary>磁盘写入基准的一次判定结论。</summary>
public sealed class DiskVerdict
{
    /// <summary>true=通过；false=有风险（Warn 或 Fail）。</summary>
    public bool Pass { get; init; }
    /// <summary>"ok" / "warn" / "fail"。</summary>
    public string Status { get; init; } = "ok";
    /// <summary>给人看的结论与建议（多行）。</summary>
    public string Advice { get; init; } = "";
}

/// <summary>
/// 磁盘写入基准核心（纯函数，供单元测试）。
///
/// 判定规则：高码率录像要求「持续顺序写 ≥ 码率换算吞吐 × 1.5 冗余」。
/// 码率 kbps → MB/s：除以 8000（1000 进制 × 8 bit）。
/// 实测达到需求 2 倍以上 → 通过；1~2 倍 → 勉强够用（警告）；不足 → 不通过。
/// </summary>
public static class DiskBenchmarkCore
{
    /// <summary>写入冗余系数：留出系统与其他进程的 IO 波动空间。</summary>
    public const double RequiredHeadroom = 1.5;
    /// <summary>单次测试默认写入量（字节）：256MB，兼顾速度与代表性。</summary>
    public const long DefaultTestBytes = 256L * 1024 * 1024;

    /// <summary>码率（kbps）换算为所需持续写吞吐（MB/s，含冗余系数）。</summary>
    public static double RequiredMbps(int bitrateKbps, double headroom = RequiredHeadroom)
        => Math.Clamp(bitrateKbps, 0, DiskBenchmarkInput.MaxBitrateKbps) / 8000.0 * headroom;

    /// <summary>
    /// 按实测写吞吐与计划录像码率给出结论。
    /// <paramref name="writeMbps"/> 为无效值（≤0 或 NaN）时返回失败结论。
    /// </summary>
    public static DiskVerdict Verdict(double writeMbps, int bitrateKbps)
    {
        if (double.IsNaN(writeMbps) || writeMbps <= 0)
        {
            return new DiskVerdict
            {
                Pass = false,
                Status = "fail",
                Advice = Strings.T("diskbench.invalid")
            };
        }

        var required = RequiredMbps(bitrateKbps);
        var ratio = writeMbps / required;

        var head = Strings.T("diskbench.head", FormatSpeed(writeMbps), bitrateKbps, FormatSpeed(required));

        if (ratio >= 2.0)
        {
            return new DiskVerdict
            {
                Pass = true,
                Status = "ok",
                Advice = head + Strings.T("diskbench.ok")
            };
        }

        if (ratio >= 1.0)
        {
            return new DiskVerdict
            {
                Pass = false,
                Status = "warn",
                Advice = head + Strings.T("diskbench.tight")
            };
        }

        return new DiskVerdict
        {
            Pass = false,
            Status = "fail",
            Advice = head + Strings.T("diskbench.fail")
        };
    }

    internal static string FormatSpeed(double mbps)
        => mbps >= 100 ? $"{mbps:0}MB/s" : $"{mbps:0.#}MB/s";
}

/// <summary>测速输入的安全上限集合。</summary>
public static class DiskBenchmarkInput
{
    /// <summary>计划码率输入上限（kbps），超出按此值截断。</summary>
    public const int MaxBitrateKbps = 1_000_000;
}