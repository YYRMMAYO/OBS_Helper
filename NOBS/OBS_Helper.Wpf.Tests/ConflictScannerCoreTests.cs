using OBS_Helper.Wpf.Services.Tools;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

public class ConflictScannerCoreTests
{
    [Fact]
    public void Scan_EmptyOrNull_ReturnsEmpty()
    {
        Assert.Empty(ConflictScannerCore.Scan(Array.Empty<string>()));
        Assert.Empty(ConflictScannerCore.Scan((IEnumerable<string>)null!));
    }

    [Fact]
    public void Scan_MatchesBySubstring_CaseInsensitive()
    {
        var hits = ConflictScannerCore.Scan(new[] { "NahimicSvc64", "explorer", "chrome" });
        var hit = Assert.Single(hits);
        Assert.Equal("Nahimic 音频服务", hit.DisplayName);
        Assert.Equal("高", hit.Risk);
        Assert.Equal("cr-env-interference", hit.ProblemId);
    }

    [Fact]
    public void Scan_RtssAndOverwolf_AreMediumRisk()
    {
        var hits = ConflictScannerCore.Scan(new[] { "RTSSHooksLoader64", "OverwolfLauncher" });
        Assert.All(hits, h => Assert.Equal("中", h.Risk));
        Assert.Contains(hits, h => h.DisplayName.Contains("RivaTuner"));
        Assert.Contains(hits, h => h.DisplayName == "Overwolf");
    }

    [Fact]
    public void Scan_SortedHighFirst_ThenMediumThenHint()
    {
        var hits = ConflictScannerCore.Scan(new[]
        {
            "qqpctray",       // 提示
            "rtss",           // 中
            "nahimicsvc",     // 高
        });

        Assert.Equal(3, hits.Count);
        Assert.Equal("高", hits[0].Risk);
        Assert.Equal("中", hits[1].Risk);
        Assert.Equal("提示", hits[2].Risk);
    }

    [Fact]
    public void Scan_DuplicatesAndNullsAreIgnored()
    {
        var hits = ConflictScannerCore.Scan(new[] { "nahimic", "NAHIMIC", "", null!, "  " });
        var hit = Assert.Single(hits);
        // 去重后进程名列表只出现一次
        Assert.DoesNotContain(", ", hit.ProcessName);
    }

    [Fact]
    public void EveryKnownEntry_HasAdviceAndProblemId()
    {
        // 全量回归：任何已知条目都不允许缺建议或知识库关联
        var all = ConflictScannerCore.Scan(KnownProbeNames());
        Assert.All(all, h =>
        {
            Assert.False(string.IsNullOrWhiteSpace(h.Advice));
            Assert.False(string.IsNullOrEmpty(h.ProblemId));
            Assert.False(string.IsNullOrWhiteSpace(h.DisplayName));
        });
    }

    /// <summary>覆盖已知清单里每一类软件的探针进程名（新增条目时同步补一行）。</summary>
    private static IEnumerable<string> KnownProbeNames() => new[]
    {
        "nahimic", "avolute", "rtss", "afterburner",
        "overwolf", "voicemod", "360tray", "hipsdaemon", "qqpctray",
    };
}
