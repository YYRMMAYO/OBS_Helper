using OBS_Helper.Wpf.Services.Diagnostics;
using OBS_Helper.Wpf.Services.ObsConfig;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// 「开播前体检 / 一键处方」（V3.0 / D4 后半段）的判定逻辑测试。
///
/// 这个功能的价值在于**排序**与**克制**：把五页检查压成一句结论、把必须先解决的排在最前，
/// 并且在没有依据时**不乱建议**（不乱改档位、不谎报通过）。
/// </summary>
public class MachineProfileCoreTests
{
    private static MachineProfileInputs Healthy() => new()
    {
        Elevated = true,
        GameDvrEnabled = false,
        HwSchMode = 2,
        GpuCount = 1,
        ObsGpuPreferenceSet = true,
        OnBattery = false,
        ActivePowerScheme = "平衡",
        EncoderNames = new[] { "jim_nvenc" },
        AudioSampleRateHz = 48000,
        StreamBitrateKbps = 6000,
        UplinkMbps = 20,
        IngestRttMs = 30,
        DiskWriteMbps = 500,
        DiskFreeGb = 200,
        RecordingFormat = "mkv",
    };

    [Fact]
    public void HealthyMachine_ReportsCleanConclusion()
    {
        var profile = MachineProfileCore.Build(Healthy());

        Assert.Equal(0, profile.BlockerCount);
        Assert.Equal(0, profile.RecommendCount);
        Assert.Contains("可以开始", profile.Conclusion);
        // 硬件编码 + 磁盘够快 → 建议 60fps 的游戏档
        Assert.Equal(SimplePresetId.Game, profile.SuggestedPreset);
    }

    /// <summary>磁盘写入跟不上是**必须先解决**的（录出来的文件会坏），排在最前。</summary>
    [Fact]
    public void SlowDisk_IsABlockerAndComesFirst()
    {
        var inputs = Healthy() with { DiskWriteMbps = 1 };
        var profile = MachineProfileCore.Build(inputs);

        Assert.True(profile.BlockerCount >= 1);
        Assert.Equal(MachineSeverity.Blocker, profile.Items[0].Severity);
        Assert.Equal(MachineArea.Disk, profile.Items[0].Area);
        Assert.Contains("必须先解决", profile.Conclusion);
    }

    [Fact]
    public void LowDiskSpace_IsABlocker()
    {
        var profile = MachineProfileCore.Build(Healthy() with { DiskFreeGb = 5 });
        Assert.Contains(profile.Items, i => i.Severity == MachineSeverity.Blocker && i.Area == MachineArea.Disk);
    }

    /// <summary>有阻塞问题时**不给档位建议** —— 先解决问题，再谈档位（否则等于建议往坏盘上录）。</summary>
    [Fact]
    public void WithBlockers_NoPresetSuggestion()
    {
        var profile = MachineProfileCore.Build(Healthy() with { DiskFreeGb = 3 });
        Assert.Null(profile.SuggestedPreset);
    }

    [Theory]
    [InlineData("obs_x264")]
    [InlineData("x264")]
    public void SoftwareEncoder_IsRecommendedAgainstAndSuggestsLowerPreset(string encoder)
    {
        var profile = MachineProfileCore.Build(Healthy() with { EncoderNames = new[] { encoder } });

        Assert.Contains(profile.Items, i => i.Area == MachineArea.Encoder && i.Severity == MachineSeverity.Recommend);
        // 软件编码：建议 30fps 的会议档，别用 60fps 去抢 CPU
        Assert.Equal(SimplePresetId.Meeting, profile.SuggestedPreset);
    }

    [Fact]
    public void GameDvrOn_IsRecommendedAgainst()
        => Assert.Contains(
            MachineProfileCore.Build(Healthy() with { GameDvrEnabled = true }).Items,
            i => i.Area == MachineArea.Graphics && i.Severity == MachineSeverity.Recommend);

    [Fact]
    public void MultiGpuWithoutPreference_IsRecommendedAgainst()
    {
        var profile = MachineProfileCore.Build(Healthy() with { GpuCount = 2, ObsGpuPreferenceSet = false });
        Assert.Contains(profile.Items, i => i.Area == MachineArea.Graphics && i.Severity == MachineSeverity.Recommend);
    }

    [Fact]
    public void OnBattery_AndPowerSaver_AreBothNoted()
    {
        var profile = MachineProfileCore.Build(Healthy() with { OnBattery = true, ActivePowerScheme = "Power saver" });
        Assert.Equal(2, profile.Items.Count(i => i.Area == MachineArea.Graphics && i.Severity == MachineSeverity.Recommend));
    }

    /// <summary>锁屏/节能计划名是本地化的，只认得出的关键词才提；认不出不该瞎提。</summary>
    [Fact]
    public void UnknownPowerScheme_IsNotFlagged()
        => Assert.DoesNotContain(
            MachineProfileCore.Build(Healthy() with { ActivePowerScheme = "自定义计划" }).Items,
            i => i.Title.Contains("节能"));

    /// <summary>码率超上行只给**建议**：推流参数不会被自动改（提案的边界）。</summary>
    [Fact]
    public void BitrateOverUplink_IsOnlyARecommendation()
    {
        var profile = MachineProfileCore.Build(Healthy() with { StreamBitrateKbps = 9000, UplinkMbps = 10 });
        var item = Assert.Single(profile.Items.Where(i => i.Area == MachineArea.Network && i.Severity == MachineSeverity.Recommend));
        Assert.Contains("9", item.Detail);          // 当前码率
        Assert.Contains("7.5", item.Detail);        // 建议上限
    }

    [Fact]
    public void HighIngestLatency_IsRecommendedAgainst()
    {
        var profile = MachineProfileCore.Build(Healthy() with { IngestRttMs = 260 });
        Assert.Contains(profile.Items, i => i.Area == MachineArea.Network && i.Severity == MachineSeverity.Recommend);
    }

    [Fact]
    public void Mp4RecordingFormat_IsRecommendedAgainst()
    {
        var profile = MachineProfileCore.Build(Healthy() with { RecordingFormat = "mp4" });
        Assert.Contains(profile.Items, i => i.Area == MachineArea.Recording && i.Severity == MachineSeverity.Recommend);
    }

    [Theory]
    [InlineData("mkv")]
    [InlineData("fragmented_mp4")]
    [InlineData("hybrid_mp4")]
    public void CrashSafeFormats_AreNotFlagged(string format)
        => Assert.DoesNotContain(
            MachineProfileCore.Build(Healthy() with { RecordingFormat = format }).Items,
            i => i.Area == MachineArea.Recording && i.Title.Contains("MKV"));

    [Fact]
    public void NonStandardSampleRate_IsRecommendedAgainst()
        => Assert.Contains(
            MachineProfileCore.Build(Healthy() with { AudioSampleRateHz = 44100 }).Items,
            i => i.Area == MachineArea.Recording && i.Severity == MachineSeverity.Recommend);

    /// <summary>完全没有证据时不报警、也不给档位建议 —— 不乱猜。</summary>
    [Fact]
    public void NoEvidence_ReportsNoBlockersAndNoPreset()
    {
        var profile = MachineProfileCore.Build(new MachineProfileInputs());

        Assert.Equal(0, profile.BlockerCount);
        Assert.Equal(0, profile.RecommendCount);
        Assert.Null(profile.SuggestedPreset);
        Assert.Contains(profile.Items, i => i.Area == MachineArea.Encoder && i.Severity == MachineSeverity.Info);
    }

    /// <summary>条目必须按严重度排序：阻塞 > 建议 > 信息。</summary>
    [Fact]
    public void Items_AreOrderedBySeverity()
    {
        var inputs = Healthy() with
        {
            GameDvrEnabled = true,          // Recommend
            DiskFreeGb = 2,                 // Blocker
            HwSchMode = 1,                  // Info
        };
        var profile = MachineProfileCore.Build(inputs);

        var severities = profile.Items.Select(i => (int)i.Severity).ToList();
        Assert.Equal(severities.OrderBy(s => s), severities);
    }

    [Fact]
    public void EveryItem_HasTitleAndDetail()
    {
        var profile = MachineProfileCore.Build(new MachineProfileInputs
        {
            Elevated = false, GameDvrEnabled = true, HwSchMode = 1, GpuCount = 2, ObsGpuPreferenceSet = false,
            OnBattery = true, ActivePowerScheme = "省电", EncoderNames = new[] { "obs_x264" },
            AudioSampleRateHz = 44100, StreamBitrateKbps = 20000, UplinkMbps = 5, IngestRttMs = 300,
            DiskWriteMbps = 2, DiskFreeGb = 1, RecordingFormat = "mp4",
        });

        Assert.All(profile.Items, i =>
        {
            Assert.False(string.IsNullOrWhiteSpace(i.Title));
            Assert.False(string.IsNullOrWhiteSpace(i.Detail));
        });
        Assert.True(profile.Items.Count >= 8);
    }
}
