using OBS_Helper.Wpf.Models.Shell;
using OBS_Helper.Wpf.Services.Shell;

namespace OBS_Helper.Wpf.Tests;

/// <summary>提词器纯逻辑（V3.1.0 / §12.1）：归一化、滚动、镜像、脚本分段与场景匹配。</summary>
public class TeleprompterCoreTests
{
    // ------------------------------------------------------------ 设置归一化

    [Fact]
    public void NormalizeClampsDirtyValues()
    {
        var s = TeleprompterCore.Normalize(new TeleprompterSettings
        {
            FontSize = 999,
            LineHeight = 0.1,
            Speed = -20,
            SideMargin = 9999,
            BackgroundOpacity = 2,
            TextColor = "not-a-color",
            Background = "#12",
            Monitor = 99,
            Width = 10,
            Height = double.NaN
        });

        Assert.Equal(TeleprompterCore.MaxFontSize, s.FontSize);
        Assert.Equal(TeleprompterCore.MinLineHeight, s.LineHeight);
        Assert.Equal(TeleprompterCore.MinSpeed, s.Speed);
        Assert.Equal(TeleprompterCore.MaxSideMargin, s.SideMargin);
        Assert.Equal(TeleprompterCore.MaxBackgroundOpacity, s.BackgroundOpacity);
        Assert.Equal("#ffffff", s.TextColor);
        Assert.Equal("#000000", s.Background);
        Assert.Equal(-1, s.Monitor);
        Assert.True(s.Width >= 320);
        Assert.True(s.Height >= 160);
    }

    [Fact]
    public void NormalizeAcceptsValidColors()
    {
        Assert.Equal("#abc", TeleprompterCore.NormalizeColor("#ABC", "#ffffff"));
        Assert.Equal("#112233", TeleprompterCore.NormalizeColor("#112233", "#ffffff"));
        Assert.Equal("#ffffff", TeleprompterCore.NormalizeColor("#12345", "#ffffff"));
        Assert.Equal("#ffffff", TeleprompterCore.NormalizeColor("red", "#ffffff"));
        Assert.Equal("#000000", TeleprompterCore.NormalizeColor(null, "#000000"));
    }

    [Fact]
    public void NormalizeHandlesNull()
    {
        var s = TeleprompterCore.Normalize(null);
        Assert.Equal(TeleprompterCore.DefaultFontSize, s.FontSize);
        Assert.Equal(TeleprompterCore.DefaultSpeed, s.Speed);
    }

    // ------------------------------------------------------------ 滚动

    [Fact]
    public void ScrollStepUsesPixelsPerSecond()
    {
        Assert.Equal(40, TeleprompterCore.ScrollStep(40, 1.0), 3);
        Assert.Equal(20, TeleprompterCore.ScrollStep(40, 0.5), 3);
        Assert.Equal(0, TeleprompterCore.ScrollStep(40, 0));
        // 单帧最多按 1 秒算：卡顿后不能突然跳过一大段
        Assert.Equal(TeleprompterCore.MaxSpeed, TeleprompterCore.ScrollStep(200, 5), 3);
    }

    [Fact]
    public void AdvanceClampsAndRespectsPause()
    {
        Assert.Equal(10, TeleprompterCore.Advance(0, 100, 20, 0.5, paused: false), 3);
        Assert.Equal(0, TeleprompterCore.Advance(0, 100, 20, 0.5, paused: true), 3);
        Assert.Equal(100, TeleprompterCore.Advance(99, 100, 20, 1, paused: false), 3);
        Assert.Equal(15, TeleprompterCore.Advance(-5, 100, 20, 1, paused: false), 3);
    }

    [Fact]
    public void ClampOffsetHandlesNaNAndNegativeMax()
    {
        Assert.Equal(0, TeleprompterCore.ClampOffset(double.NaN, 100));
        Assert.Equal(0, TeleprompterCore.ClampOffset(50, double.NaN));
        Assert.Equal(0, TeleprompterCore.ClampOffset(50, -1));
        Assert.Equal(30, TeleprompterCore.ClampOffset(30, 100));
    }

    // ------------------------------------------------------------ 镜像 / 显示器

    [Fact]
    public void MirrorScaleFlipsOnlyRequestedAxis()
    {
        Assert.Equal((1.0, 1.0), TeleprompterCore.MirrorScale(false, false));
        Assert.Equal((-1.0, 1.0), TeleprompterCore.MirrorScale(true, false));
        Assert.Equal((1.0, -1.0), TeleprompterCore.MirrorScale(false, true));
        Assert.Equal((-1.0, -1.0), TeleprompterCore.MirrorScale(true, true));
    }

    [Theory]
    [InlineData(-1, 2, 0)]
    [InlineData(0, 2, 0)]
    [InlineData(1, 2, 1)]
    [InlineData(5, 2, 0)]
    [InlineData(0, 0, -1)]
    public void MonitorIndexNormalization(int index, int count, int expected)
        => Assert.Equal(expected, TeleprompterCore.NormalizeMonitor(index, count));

    [Fact]
    public void CenterOnUsesWorkingArea()
    {
        var (left, top) = TeleprompterCore.CenterOn(0, 0, 1920, 1080, 900, 320);
        Assert.Equal(510, left, 3);
        Assert.Equal(380, top, 3);

        var (l2, t2) = TeleprompterCore.CenterOn(100, 50, 800, 600, 900, 700);
        Assert.Equal(100, l2, 3);
        Assert.Equal(50, t2, 3);
    }

    // ------------------------------------------------------------ 脚本

    [Fact]
    public void NormalizeScriptUnifiesNewlinesAndBom()
    {
        Assert.Equal("a\nb\nc", TeleprompterCore.NormalizeScript("\uFEFFa\r\nb\rc"));
    }

    [Fact]
    public void NormalizeScriptFallsBackToDefaultWhenEmpty()
    {
        Assert.Equal(TeleprompterCore.DefaultScript, TeleprompterCore.NormalizeScript(""));
        Assert.Equal(TeleprompterCore.DefaultScript, TeleprompterCore.NormalizeScript("   \n  "));
    }

    [Fact]
    public void NormalizeScriptCapsLength()
    {
        var huge = new string('x', TeleprompterCore.MaxScriptChars + 100);
        Assert.Equal(TeleprompterCore.MaxScriptChars, TeleprompterCore.NormalizeScript(huge).Length);
    }

    [Fact]
    public void ParseSectionsSupportsThreeMarkerStyles()
    {
        var text = "intro\n## 开场\nhello\n# 中场\nmore\n[结尾]\nbye";
        var sections = TeleprompterCore.ParseSections(text);

        Assert.Equal(new[] { "开场", "中场", "结尾" }, sections.Select(s => s.Marker));
        Assert.Equal(6, sections[0].StartIndex);   // "intro\n" = 6
    }

    [Fact]
    public void FindSectionForSceneExactThenFuzzy()
    {
        var sections = TeleprompterCore.ParseSections("## 开场\n## 游戏时间\n## 结尾");

        Assert.Equal(1, TeleprompterCore.FindSectionForScene(sections, "游戏时间"));
        Assert.Equal(1, TeleprompterCore.FindSectionForScene(sections, "  游戏时间  "));
        Assert.Equal(1, TeleprompterCore.FindSectionForScene(sections, "游戏时间 Part 2"));
        Assert.Equal(-1, TeleprompterCore.FindSectionForScene(sections, "不存在的场景"));
        Assert.Equal(-1, TeleprompterCore.FindSectionForScene(sections, ""));
        Assert.Equal(-1, TeleprompterCore.FindSectionForScene(Array.Empty<TeleprompterSection>(), "x"));
    }

    [Fact]
    public void SceneBindingMissingDetectsRenamedScene()
    {
        var scenes = new[] { "开场", "结尾" };
        Assert.False(TeleprompterCore.SceneBindingMissing("开场", scenes));
        Assert.True(TeleprompterCore.SceneBindingMissing("旧场景", scenes));
        Assert.False(TeleprompterCore.SceneBindingMissing("", scenes));
        Assert.False(TeleprompterCore.SceneBindingMissing("x", Array.Empty<string>()));
    }

    [Fact]
    public void CountdownStepsCountsDown()
    {
        Assert.Equal(new[] { 3, 2, 1 }, TeleprompterCore.CountdownSteps(3));
        Assert.Empty(TeleprompterCore.CountdownSteps(0));
        Assert.Empty(TeleprompterCore.CountdownSteps(-2));
    }

    [Fact]
    public void LineHeightPixelsMultipliesAndClamps()
    {
        Assert.Equal(60.9, TeleprompterCore.LineHeightPixels(42, 1.45), 1);
        Assert.Equal(TeleprompterCore.MaxFontSize * TeleprompterCore.MaxLineHeight,
            TeleprompterCore.LineHeightPixels(999, 99), 1);
    }

    [Fact]
    public void SpeedFormattingRounds()
    {
        Assert.Equal("40", TeleprompterCore.FormatSpeed(39.6));
        Assert.Equal("10", TeleprompterCore.FormatSpeed(-100));
    }
}
