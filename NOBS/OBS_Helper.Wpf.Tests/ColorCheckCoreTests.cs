using OBS_Helper.Wpf.Services.Obs;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

public class ColorCheckCoreTests
{
    private static Dictionary<string, string> Ini(string text)
    {
        // 复用 PreflightCheckCore 的 INI 解析（同程序集 internal 可见）
        return PreflightCheckCore.ParseIni(text);
    }

    [Fact]
    public void Evaluate_EmptyIni_AllOkDefaults()
    {
        var items = ColorCheckCore.Evaluate(new Dictionary<string, string>());
        Assert.Equal(3, items.Count);
        Assert.All(items, i => Assert.Equal("ok", i.Status));
    }

    [Fact]
    public void Evaluate_NullIni_AllOkDefaults()
    {
        var items = ColorCheckCore.Evaluate(null);
        Assert.Equal(3, items.Count);
        Assert.All(items, i => Assert.Equal("ok", i.Status));
    }

    [Fact]
    public void Evaluate_FullRange_WarnsWithKbLink()
    {
        var ini = Ini("[AdvOut]\nColorRange=Full\n");
        var items = ColorCheckCore.Evaluate(ini);

        var range = items.First(i => i.Title.Contains("色彩范围"));
        Assert.Equal("warn", range.Status);
        Assert.Equal("cf-colorrange", range.ProblemId);
    }

    [Fact]
    public void Evaluate_PartialRange_Ok()
    {
        var ini = Ini("[AdvOut]\nColorRange=Partial\n");
        var items = ColorCheckCore.Evaluate(ini);

        Assert.Equal("ok", items.First(i => i.Title.Contains("色彩范围")).Status);
    }

    [Fact]
    public void Evaluate_HdrSpace_InfoNotWarn()
    {
        var ini = Ini("[AdvOut]\nColorSpace=Rec.2100 PQ\n");
        var items = ColorCheckCore.Evaluate(ini);

        var space = items.First(i => i.Title.Contains("色彩空间"));
        Assert.Equal("info", space.Status);
    }

    [Fact]
    public void Evaluate_WeirdSpace_Warns()
    {
        var ini = Ini("[AdvOut]\nColorSpace=170m\n");
        var items = ColorCheckCore.Evaluate(ini);

        var space = items.First(i => i.Title.Contains("色彩空间"));
        Assert.Equal("warn", space.Status);
    }

    [Fact]
    public void Evaluate_RgbFormat_Warns()
    {
        var ini = Ini("[AdvOut]\nColorFormat=RGBA\n");
        var items = ColorCheckCore.Evaluate(ini);

        var format = items.First(i => i.Title.Contains("色彩格式"));
        Assert.Equal("warn", format.Status);
    }

    [Fact]
    public void Evaluate_P010Format_Info()
    {
        var ini = Ini("[AdvOut]\nColorFormat=P010\n");
        var items = ColorCheckCore.Evaluate(ini);

        Assert.Equal("info", items.First(i => i.Title.Contains("色彩格式")).Status);
    }
}
