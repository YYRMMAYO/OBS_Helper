using OBS_Helper.Wpf.Services.Diagnostics;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// 自检清单自动回填（V3.0 / D4）的判定逻辑测试。
///
/// 这个功能的全部价值在于**三态**：通过 / 需注意 / 无法判定。
/// 把「无法判定」误判成两者之一都会造成实际伤害 ——
/// 判成失败是冤枉用户（他明明没做错），判成通过是撒谎（清单的全部意义就没了）。
/// </summary>
public class ChecklistAutoFillCoreTests
{
    private static ChecklistAutoResult Result(ChecklistEvidence e, string key)
        => ChecklistAutoFillCore.Evaluate(e).Single(r => r.ItemKey == key);

    private const string Item1 = "diagnostic.check.1";
    private const string Item2 = "diagnostic.check.2";
    private const string Item3 = "diagnostic.check.3";
    private const string Item5 = "diagnostic.check.5";
    private const string Item8 = "diagnostic.check.8";

    /// <summary>八条都在、顺序与界面一致、键名不重复。</summary>
    [Fact]
    public void Evaluate_ReturnsAllEightItemsInOrder()
    {
        var results = ChecklistAutoFillCore.Evaluate(new ChecklistEvidence());

        Assert.Equal(8, results.Count);
        for (var i = 0; i < 8; i++)
            Assert.Equal($"diagnostic.check.{i + 1}", results[i].ItemKey);
    }

    /// <summary>没有任何证据时：**全部是「无法判定」**，不能有任何一条被蒙成通过或失败。</summary>
    [Fact]
    public void Evaluate_WithoutEvidence_EverythingIsUnknown()
    {
        var results = ChecklistAutoFillCore.Evaluate(new ChecklistEvidence());

        Assert.All(results, r => Assert.Equal(ChecklistStatus.Unknown, r.Status));
        Assert.All(results, r => Assert.False(string.IsNullOrWhiteSpace(r.Evidence)));
        Assert.Equal(0, ChecklistAutoFillCore.DecidedCount(results));
    }

    // ---------------------------------------------------------------- 管理员权限

    [Theory]
    [InlineData(true, ChecklistStatus.Pass)]
    [InlineData(false, ChecklistStatus.Attention)]
    public void Item1_AdminRights(bool elevated, ChecklistStatus expected)
        => Assert.Equal(expected, Result(new ChecklistEvidence { Elevated = elevated }, Item1).Status);

    [Fact]
    public void Item1_UnknownWhenPrivilegeUnreadable()
        => Assert.Equal(ChecklistStatus.Unknown, Result(new ChecklistEvidence { Elevated = null }, Item1).Status);

    // ---------------------------------------------------------------- 编码器

    [Theory]
    [InlineData("obs_x264", ChecklistStatus.Attention)]
    [InlineData("jim_nvenc", ChecklistStatus.Pass)]
    [InlineData("obs_nvenc", ChecklistStatus.Pass)]
    [InlineData("h264_texture_amf", ChecklistStatus.Pass)]
    [InlineData("obs_qsv11_v2", ChecklistStatus.Pass)]
    [InlineData("av1_texture_amf", ChecklistStatus.Pass)]
    [InlineData("ffmpeg_aom_av1", ChecklistStatus.Pass)]
    public void Item2_ClassifiesEncoders(string encoder, ChecklistStatus expected)
        => Assert.Equal(expected, Result(new ChecklistEvidence { EncoderNames = new[] { encoder } }, Item2).Status);

    [Fact]
    public void Item2_MixedEncoders_ReportsAttentionAndNamesTheSoftwareOne()
    {
        var result = Result(new ChecklistEvidence { EncoderNames = new[] { "jim_nvenc", "obs_x264" } }, Item2);

        Assert.Equal(ChecklistStatus.Attention, result.Status);
        Assert.Contains("obs_x264", result.Evidence);          // 指出到底是哪一个
        Assert.DoesNotContain("jim_nvenc", result.Evidence);
    }

    [Fact]
    public void Item2_UnknownWhenNoEncoderReadable()
    {
        Assert.Equal(ChecklistStatus.Unknown, Result(new ChecklistEvidence(), Item2).Status);
        Assert.Equal(ChecklistStatus.Unknown,
            Result(new ChecklistEvidence { EncoderNames = Array.Empty<string>() }, Item2).Status);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("obs_x264", false)]
    [InlineData("x265", false)]
    [InlineData("nvenc", true)]
    [InlineData("h264_texture_amf", true)]
    [InlineData("obs_qsv11", true)]
    public void IsHardwareEncoder_MatchesKnownNames(string name, bool expected)
        => Assert.Equal(expected, ChecklistAutoFillCore.IsHardwareEncoder(name));

    // ---------------------------------------------------------------- 码率与上行

    [Fact]
    public void Item3_PassesWhenWithin75Percent()
    {
        // 6000 kbps 对 10 Mbps：预算 7500 kbps → 通过
        var result = Result(new ChecklistEvidence { VideoBitrateKbps = 6000, UplinkMbps = 10 }, Item3);
        Assert.Equal(ChecklistStatus.Pass, result.Status);
    }

    [Fact]
    public void Item3_AttentionWhenOverBudget()
    {
        // 8000 kbps 对 10 Mbps：预算 7500 → 需注意
        var result = Result(new ChecklistEvidence { VideoBitrateKbps = 8000, UplinkMbps = 10 }, Item3);
        Assert.Equal(ChecklistStatus.Attention, result.Status);
        Assert.Contains("7.5", result.Evidence);   // 明确给出建议上限
    }

    /// <summary>没填上行速度时**不能**判失败（那会冤枉用户），必须给「无法判定」并说明去做哪一步。</summary>
    [Fact]
    public void Item3_UnknownWithoutUplink_NotFailure()
    {
        var result = Result(new ChecklistEvidence { VideoBitrateKbps = 6000 }, Item3);
        Assert.Equal(ChecklistStatus.Unknown, result.Status);
        Assert.Contains("6000", result.Evidence);
    }

    [Fact]
    public void Item3_UnknownWithoutBitrate()
        => Assert.Equal(ChecklistStatus.Unknown,
            Result(new ChecklistEvidence { UplinkMbps = 100 }, Item3).Status);

    // ---------------------------------------------------------------- 采样率

    [Theory]
    [InlineData(48000, ChecklistStatus.Pass)]
    [InlineData(44100, ChecklistStatus.Attention)]
    public void Item5_SampleRate(int rate, ChecklistStatus expected)
        => Assert.Equal(expected, Result(new ChecklistEvidence { AudioSampleRateHz = rate }, Item5).Status);

    // ---------------------------------------------------------------- 推流服务

    [Fact]
    public void Item8_PassesOnlyWhenServerAndKeyAreBothSet()
    {
        Assert.Equal(ChecklistStatus.Pass,
            Result(new ChecklistEvidence { StreamHasServer = true, StreamHasKey = true }, Item8).Status);

        Assert.Equal(ChecklistStatus.Attention,
            Result(new ChecklistEvidence { StreamHasServer = true, StreamHasKey = false }, Item8).Status);

        Assert.Equal(ChecklistStatus.Attention,
            Result(new ChecklistEvidence { StreamHasServer = false, StreamHasKey = true }, Item8).Status);
    }

    [Fact]
    public void Item8_UnknownWhenSettingsUnreadable()
        => Assert.Equal(ChecklistStatus.Unknown, Result(new ChecklistEvidence(), Item8).Status);

    // ---------------------------------------------------------------- 网络与丢帧

    /// <summary>丢帧优先于「是否无线」：两者都不可时先报最要紧的那个。</summary>
    [Fact]
    public void Item7_DroppedFramesOutrankWiredCheck()
    {
        var result = Result(new ChecklistEvidence { OnWiredNetwork = true, DroppedRatio = 0.12 },
            "diagnostic.check.7");

        Assert.Equal(ChecklistStatus.Attention, result.Status);
        Assert.Contains("12", result.Evidence);
    }

    [Fact]
    public void Item7_WiredWithoutDrops_Passes()
        => Assert.Equal(ChecklistStatus.Pass,
            Result(new ChecklistEvidence { OnWiredNetwork = true, DroppedRatio = 0 }, "diagnostic.check.7").Status);

    // ---------------------------------------------------------------- 汇总

    [Fact]
    public void ShouldCheck_OnlyPassCountsAsSatisfied()
    {
        Assert.True(ChecklistAutoFillCore.ShouldCheck(ChecklistStatus.Pass));
        Assert.False(ChecklistAutoFillCore.ShouldCheck(ChecklistStatus.Attention));
        Assert.False(ChecklistAutoFillCore.ShouldCheck(ChecklistStatus.Unknown));
    }

    [Fact]
    public void DecidedCount_CountsPassAndAttention()
    {
        var evidence = new ChecklistEvidence
        {
            Elevated = true,                 // Pass
            EncoderNames = new[] { "obs_x264" },   // Attention
            // 其余未知
        };
        Assert.Equal(2, ChecklistAutoFillCore.DecidedCount(ChecklistAutoFillCore.Evaluate(evidence)));
    }
}
