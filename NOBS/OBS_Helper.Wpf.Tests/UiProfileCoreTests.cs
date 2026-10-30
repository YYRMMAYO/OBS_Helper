using OBS_Helper.Wpf.Services.Appearance;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// 无障碍 / 老年模式的组合规则（V3.3.0 / P0-1）。
///
/// 这些规则一旦写错，表现是「模式打开了但界面没变」或「老年模式把高级功能藏死」，
/// 都属于必须在代码层钉住、而不是靠人眼试两下的东西。
/// </summary>
public class UiProfileCoreTests
{
    [Fact]
    public void Defaults_AreCompactAndUnchanged()
    {
        var t = UiProfileCore.Compute(null);

        Assert.Equal(1.0, t.FontScale, 3);
        Assert.Equal(UiDensity.Compact, t.Density);
        Assert.Equal(UiLayoutMode.Full, t.Layout);
        Assert.False(t.ReduceMotion);
        Assert.False(t.ShowSkipLink);
        Assert.Equal(212, t.NavWidth);
        Assert.Equal(2, t.FocusThickness);
        Assert.Equal(0, t.ButtonMinSize);
    }

    [Fact]
    public void AccessibilityMode_RaisesFontAndDensity()
    {
        var t = UiProfileCore.Compute(new UiProfileInput { AccessibilityMode = true });

        Assert.True(t.FontScale >= 1.15, $"字号应至少 +15%，实际 {t.FontScale}");
        Assert.True(t.Density >= UiDensity.Comfortable);
        Assert.True(t.ReduceMotion);
        Assert.True(t.ShowSkipLink);
        Assert.Equal(44, t.ButtonMinSize);
        Assert.True(t.ControlMinHeight >= 44);
        Assert.Equal(3, t.FocusThickness);
    }

    [Fact]
    public void ElderMode_IsSpaciousSimplifiedAndLarger()
    {
        var t = UiProfileCore.Compute(new UiProfileInput { ElderMode = true });

        Assert.True(t.FontScale >= 1.3, $"老年模式字号应 ≥1.3，实际 {t.FontScale}");
        Assert.Equal(UiDensity.Spacious, t.Density);
        Assert.Equal(UiLayoutMode.Simplified, t.Layout);
        Assert.True(t.ReduceMotion);
        Assert.True(t.ShowSkipLink);
        Assert.Equal(48, t.ButtonMinSize);
        Assert.True(t.NavWidth >= 240, $"侧栏应加宽到 ≥240，实际 {t.NavWidth}");
    }

    [Fact]
    public void BothModes_TakeTheLargerValues()
    {
        var both = UiProfileCore.Compute(new UiProfileInput { AccessibilityMode = true, ElderMode = true });
        var elder = UiProfileCore.Compute(new UiProfileInput { ElderMode = true });
        var a11y = UiProfileCore.Compute(new UiProfileInput { AccessibilityMode = true });

        Assert.Equal(elder.FontScale, both.FontScale, 3);
        Assert.Equal(UiDensity.Spacious, both.Density);
        Assert.True(both.FontScale >= a11y.FontScale);
        Assert.True(both.AnyAssistMode);
    }

    [Fact]
    public void SystemTextScale_MultipliesFontSize()
    {
        var t = UiProfileCore.Compute(new UiProfileInput { SystemTextScale = 1.5 });

        Assert.Equal(1.5, t.FontScale, 3);
    }

    [Fact]
    public void FontScale_IsClampedAtBothEnds()
    {
        var max = UiProfileCore.Compute(new UiProfileInput
        {
            FontScaleKey = "xl",
            SystemTextScale = 2.0
        });
        Assert.Equal(2.56, max.FontScale, 3);

        // 系统缩放低于 1（脏值）按 1 处理，不能让界面比默认还小
        var min = UiProfileCore.Compute(new UiProfileInput { SystemTextScale = 0.5 });
        Assert.Equal(1.0, min.FontScale, 3);
    }

    [Fact]
    public void SystemReduceMotion_IsAFloor()
    {
        var t = UiProfileCore.Compute(new UiProfileInput
        {
            SystemReduceMotion = true,
            ReduceMotionOverride = false
        });

        Assert.True(t.ReduceMotion, "系统减少动画不能被应用关掉");
    }

    [Fact]
    public void ExplicitUserChoice_OverridesModePreset()
    {
        var t = UiProfileCore.Compute(new UiProfileInput
        {
            AccessibilityMode = true,
            ReduceMotionOverride = false,
            DensityKey = "compact",
            LayoutKey = "full"
        });

        Assert.False(t.ReduceMotion, "用户显式关闭减少动画时，预设不应重新打开");
        Assert.Equal(UiDensity.Comfortable, t.Density); // 无障碍模式仍有 44px 下限
        Assert.Equal(UiLayoutMode.Full, t.Layout);
    }

    [Fact]
    public void ElderMode_SimplifiesLayout_UnlessUserChoseFull()
    {
        var preset = UiProfileCore.Compute(new UiProfileInput { ElderMode = true });
        var explicitFull = UiProfileCore.Compute(new UiProfileInput { ElderMode = true, LayoutKey = "full" });

        Assert.Equal(UiLayoutMode.Simplified, preset.Layout);
        Assert.Equal(UiLayoutMode.Full, explicitFull.Layout);
    }

    [Fact]
    public void ModeKey_DescribesCombination()
    {
        Assert.Equal("off", UiProfileCore.ModeKey(false, false));
        Assert.Equal("a11y", UiProfileCore.ModeKey(true, false));
        Assert.Equal("elder", UiProfileCore.ModeKey(false, true));
        Assert.Equal("both", UiProfileCore.ModeKey(true, true));
    }

    [Fact]
    public void DensityRoundTrip_IsStable()
    {
        foreach (var d in new[] { UiDensity.Compact, UiDensity.Comfortable, UiDensity.Spacious })
        {
            Assert.Equal(d, UiProfileCore.ParseDensity(UiProfileCore.DensityKey(d)));
        }

        // 脏值不抛异常，回到默认
        Assert.Equal(UiDensity.Compact, UiProfileCore.ParseDensity("huge"));
        Assert.Equal(UiLayoutMode.Full, UiProfileCore.ParseLayout(null));
    }
}
