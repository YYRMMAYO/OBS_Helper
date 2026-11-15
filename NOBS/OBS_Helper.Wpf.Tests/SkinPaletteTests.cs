using OBS_Helper.Wpf.Services;
using OBS_Helper.Wpf.Services.Appearance;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// V3.7.2 双皮肤：解析规则与对比度基线。
///
/// 为什么要有这组测试：A/C 的配色不是随手挑的 —— 技术方案 §5.3 给每个文字 / 交互角色定了
/// 对比度下限（正文 4.5:1、大字与 UI 边界 3:1）。配色改错时单看截图很难发现，
/// 这里用 WCAG 公式直接钉住，避免以后「为了好看」把可读性调回去。
/// </summary>
public class SkinPaletteTests
{
    [Fact]
    public void DarkResolvesToSignalDesk()
        => Assert.Equal(UiSkin.SignalDark, SkinRules.Resolve(dark: true));

    [Fact]
    public void LightResolvesToFieldManual()
        => Assert.Equal(UiSkin.ManualLight, SkinRules.Resolve(dark: false));

    [Fact]
    public void DefaultAccentFollowsSkin()
    {
        Assert.Equal("signal", SkinRules.DefaultAccentKey(UiSkin.SignalDark));
        Assert.Equal("cobalt", SkinRules.DefaultAccentKey(UiSkin.ManualLight));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BodyAndMutedTextMeetAa(bool dark)
    {
        var p = SkinPalette.For(SkinRules.Resolve(dark));
        Assert.True(Contrast(p.Text, p.Surface2) >= 4.5, "正文对比度不足");
        Assert.True(Contrast(p.Muted, p.Surface) >= 4.5, "次要文字对比度不足");
        Assert.True(Contrast(p.Dim, p.Surface2) >= 4.5, "功能性小字对比度不足");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InteractiveBoundaryMeetsNonTextContrast(bool dark)
    {
        var p = SkinPalette.For(SkinRules.Resolve(dark));
        Assert.True(Contrast(p.LineStrong, p.Surface) >= 3.0, "交互边界低于 3:1");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StatusColorsRemainReadable(bool dark)
    {
        var p = SkinPalette.For(SkinRules.Resolve(dark));
        Assert.True(Contrast(p.Danger, p.Surface2) >= 3.0, "危险色对比度不足");
        Assert.True(Contrast(p.Warn, p.Surface2) >= 3.0, "警告色对比度不足");
        Assert.True(Contrast(p.Ok, p.Surface2) >= 3.0, "正常色对比度不足");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HighContrastOverridesKeepAa(bool dark)
    {
        var p = SkinPalette.Signal.WithHighContrast(dark);
        Assert.True(Contrast(p.Text, p.Surface2) >= 7.0, "高对比正文低于 7:1");
        Assert.True(Contrast(p.LineStrong, p.Surface) >= 3.0, "高对比边界低于 3:1");
    }

    // ------------------------------------------------------------ WCAG 2.1 相对亮度

    private static double Contrast(string fg, string bg)
    {
        var a = RelativeLuminance(fg);
        var b = RelativeLuminance(bg);
        var (hi, lo) = a > b ? (a, b) : (b, a);
        return (hi + 0.05) / (lo + 0.05);
    }

    private static double RelativeLuminance(string hex)
    {
        var rgb = Parse(hex);
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(rgb.R) + 0.7152 * Channel(rgb.G) + 0.0722 * Channel(rgb.B);
    }

    private static (byte R, byte G, byte B) Parse(string hex)
    {
        var text = hex.TrimStart('#');
        if (text.Length == 8) text = text[2..];   // #AARRGGBB
        return (Convert.ToByte(text[..2], 16), Convert.ToByte(text[2..4], 16), Convert.ToByte(text[4..6], 16));
    }
}
