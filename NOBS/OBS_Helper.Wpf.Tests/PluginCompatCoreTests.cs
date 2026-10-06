using OBS_Helper.Wpf.Services.Plugins;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// 插件 × OBS 版本兼容性（V3.0 / D9）的判定测试。
///
/// 这个功能的全部风险在**误报**：给一个其实能用的插件打上「不兼容」黄标，
/// 用户就不敢装了；反过来漏报则等于没做。因此：
/// <list type="number">
///   <item>任何读不懂的声明一律判「未知」（不猜）；</item>
///   <item>本机版本读不到时也判「未知」；</item>
///   <item>「未知」与「兼容」都不显示提示（只有真的有问题才提示）。</item>
/// </list>
/// </summary>
public class PluginCompatCoreTests
{
    // ---------------------------------------------------------------- 版本解析

    [Theory]
    [InlineData("31.0.2", 31, 0)]
    [InlineData("30.1.2-rc1", 30, 1)]
    [InlineData("31", 31, 0)]
    [InlineData("OBS 31.1.0", 31, 1)]
    [InlineData("v29.0.0-beta4", 29, 0)]
    public void ParseObsVersion_HandlesRealWorldFormats(string raw, int major, int minor)
    {
        var parsed = PluginCompatCore.ParseObsVersion(raw);
        Assert.NotNull(parsed);
        Assert.Equal(major, parsed!.Value.Major);
        Assert.Equal(minor, parsed.Value.Minor);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("unknown")]
    [InlineData(null)]
    public void ParseObsVersion_ReturnsNullWhenUnparsable(string? raw)
        => Assert.Null(PluginCompatCore.ParseObsVersion(raw));

    // ---------------------------------------------------------------- 声明判定

    [Theory]
    [InlineData("30+", "31.0.2", PluginCompatStatus.Compatible)]
    [InlineData("30+", "30.0.0", PluginCompatStatus.Compatible)]
    [InlineData("30+", "29.1.2", PluginCompatStatus.TooOld)]
    [InlineData("28-32", "30.0.0", PluginCompatStatus.Compatible)]
    [InlineData("28-32", "27.0.0", PluginCompatStatus.TooOld)]
    [InlineData("28-32", "33.0.0", PluginCompatStatus.TooNew)]
    [InlineData("!33", "33.0.0", PluginCompatStatus.Broken)]
    [InlineData("!33", "32.0.0", PluginCompatStatus.Compatible)]
    [InlineData("30+,!33", "31.0.0", PluginCompatStatus.Compatible)]
    [InlineData("30+,!33", "33.0.0", PluginCompatStatus.Broken)]
    [InlineData("30+,!33", "29.0.0", PluginCompatStatus.TooOld)]
    [InlineData("31", "31.0.2", PluginCompatStatus.Compatible)]
    [InlineData("31", "30.0.0", PluginCompatStatus.TooOld)]
    [InlineData("31", "32.0.0", PluginCompatStatus.TooNew)]
    public void Evaluate_CoversDeclaredGrammar(string compat, string localVersion, PluginCompatStatus expected)
        => Assert.Equal(expected, PluginCompatCore.Evaluate(compat, localVersion));

    /// <summary>没声明兼容性 = 不做提示（旧目录是常态，不能因此给全部条目打标）。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Evaluate_NoDeclaration_IsUnknown(string? compat)
    {
        var status = PluginCompatCore.Evaluate(compat, "31.0.2");
        Assert.Equal(PluginCompatStatus.Unknown, status);
        Assert.False(PluginCompatCore.NeedsWarning(status));
    }

    /// <summary>本机版本读不到时判「未知」，绝不误报不兼容。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    public void Evaluate_NoLocalVersion_IsUnknown(string? localVersion)
    {
        var status = PluginCompatCore.Evaluate("30+", localVersion);
        Assert.Equal(PluginCompatStatus.Unknown, status);
        Assert.False(PluginCompatCore.NeedsWarning(status));
    }

    /// <summary>有一个 token 读不懂 → 整条声明作废（返回未知），而不是按读懂的部分下结论。</summary>
    [Theory]
    [InlineData("30+,bogus")]
    [InlineData("最近版本")]
    [InlineData("30+,-31")]
    [InlineData("!")]
    [InlineData(",")]
    public void Evaluate_UnparsableDeclaration_IsUnknown(string compat)
    {
        var status = PluginCompatCore.Evaluate(compat, "31.0.2");
        Assert.Equal(PluginCompatStatus.Unknown, status);
    }

    // ---------------------------------------------------------------- 提示口径

    [Theory]
    [InlineData(PluginCompatStatus.TooOld, true)]
    [InlineData(PluginCompatStatus.TooNew, true)]
    [InlineData(PluginCompatStatus.Broken, true)]
    [InlineData(PluginCompatStatus.Compatible, false)]
    [InlineData(PluginCompatStatus.Unknown, false)]
    public void NeedsWarning_OnlyWarnsOnRealProblems(PluginCompatStatus status, bool expected)
        => Assert.Equal(expected, PluginCompatCore.NeedsWarning(status));

    [Fact]
    public void DescribeRequirement_TrimsAndPassesThrough()
    {
        Assert.Equal("30+,!33", PluginCompatCore.DescribeRequirement("  30+,!33  "));
        Assert.Equal("", PluginCompatCore.DescribeRequirement(""));
        Assert.Equal("", PluginCompatCore.DescribeRequirement(null));
    }
}
