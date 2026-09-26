using System.Reflection;
using OBS_Helper.Wpf.Navigation;
using OBS_Helper.Wpf.Services.Shell;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// 新手引导纯逻辑（V2.9.0）的测试：步骤清单的完整性 + 步骤游标的状态机 + 展示判定。
///
/// 引导是首启的第一印象，文案写空、步数越界、标记判断反了这类问题在 UI 上很难自动发现，
/// 因此在纯逻辑层钉死。
///
/// V2.9.1 追加：**跳转目标**（步骤对应的页面与卡片上的跳转按钮）也在这里校验 ——
/// 引导会带着界面自动翻页，路由名写错的话用户看到的是「文案讲 A 页、界面停在 B 页」，
/// 比纯文字引导更糟。
/// </summary>
public class OnboardingGuideTests
{
    /// <summary>Routes 里定义的全部路由名（反射取常量，避免手工维护第二份清单）。</summary>
    private static HashSet<string> AllRoutes() => typeof(Routes)
        .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
        .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Steps_ArePresentAndWellFormed()
    {
        Assert.True(OnboardingGuide.StepCount >= 3, "新手引导至少要有 3 步才够覆盖主要功能面");

        foreach (var step in OnboardingGuide.Steps)
        {
            Assert.False(string.IsNullOrWhiteSpace(step.Title), "步骤标题不能为空");
            Assert.False(string.IsNullOrWhiteSpace(step.Description), "步骤说明不能为空");
            Assert.True(step.Description.Length >= 20, $"步骤说明过短，讲不清引导意图：{step.Title}");
        }
    }

    [Fact]
    public void Steps_HaveUniqueTitles()
    {
        var titles = OnboardingGuide.Steps.Select(s => s.Title).ToList();
        Assert.Equal(titles.Count, titles.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void StepRoutes_AreRegisteredRouteNames()
    {
        var routes = AllRoutes();
        Assert.NotEmpty(routes);

        foreach (var step in OnboardingGuide.Steps)
        {
            Assert.False(string.IsNullOrWhiteSpace(step.Route), $"步骤「{step.Title}」没有配置跳转页面");
            Assert.Contains(step.Route, routes);
        }
    }

    [Fact]
    public void StepRoutes_AreDistinct()
    {
        // 每步自动跳到同一个页面等于没跳，这里钉住「一步一个页面」
        var routes = OnboardingGuide.Steps.Select(s => s.Route).ToList();
        Assert.Equal(routes.Count, routes.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void StepLinks_AreWellFormedAndResolvable()
    {
        var routes = AllRoutes();

        foreach (var step in OnboardingGuide.Steps)
        {
            Assert.NotNull(step.Links);

            foreach (var link in step.Links)
            {
                Assert.False(string.IsNullOrWhiteSpace(link.Label), $"步骤「{step.Title}」有跳转按钮没写文案");
                Assert.False(string.IsNullOrWhiteSpace(link.Target), $"步骤「{step.Title}」的「{link.Label}」没写目标");

                if (link.External)
                {
                    // 站外链接只允许官方域名：引导里的外链最容易变成「点了直接中招」的入口
                    Assert.True(Services.Update.ObsDownloadLinks.IsOfficialDownloadUrl(link.Target),
                        $"站外跳转不在官方域名下：{link.Label} → {link.Target}");
                }
                else
                {
                    Assert.Contains(link.Target, routes);
                }
            }
        }
    }

    [Fact]
    public void StepLinks_DoNotContradictStepRoute()
    {
        // 卡片上的「站内跳转按钮」不该指向本步已经自动打开的页面（点了没反应，像坏了）
        foreach (var step in OnboardingGuide.Steps)
        {
            foreach (var link in step.Links.Where(l => !l.External))
            {
                Assert.False(string.Equals(step.Route, link.Target, StringComparison.OrdinalIgnoreCase),
                    $"步骤「{step.Title}」的跳转按钮「{link.Label}」指向了本步已经自动打开的页面");
            }
        }
    }

    [Fact]
    public void StepCount_MatchesList()
        => Assert.Equal(OnboardingGuide.Steps.Count, OnboardingGuide.StepCount);

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(99, 3)]
    public void Clamp_KeepsIndexInRange(int input, int expected)
    {
        // 末步下标随步骤数变化，用例里直接取末位，避免把步数写死
        var last = OnboardingGuide.StepCount - 1;
        Assert.Equal(Math.Min(expected, last), OnboardingGuide.Clamp(input));
    }

    [Fact]
    public void Step_ClampsAndNeverThrows()
    {
        Assert.Equal(OnboardingGuide.Steps[0].Title, OnboardingGuide.Step(-10).Title);
        Assert.Equal(OnboardingGuide.Steps[^1].Title, OnboardingGuide.Step(1000).Title);
    }

    [Fact]
    public void IsFirst_And_IsLast_TrackBoundaries()
    {
        Assert.True(OnboardingGuide.IsFirst(0));
        Assert.False(OnboardingGuide.IsLast(0));
        Assert.False(OnboardingGuide.IsFirst(OnboardingGuide.StepCount - 1));
        Assert.True(OnboardingGuide.IsLast(OnboardingGuide.StepCount - 1));

        // 越界输入按夹取后的值判断
        Assert.True(OnboardingGuide.IsFirst(-3));
        Assert.True(OnboardingGuide.IsLast(999));
    }

    [Fact]
    public void Next_AdvancesAndStopsAtLastStep()
    {
        var index = 0;
        for (var i = 1; i < OnboardingGuide.StepCount; i++)
        {
            index = OnboardingGuide.Next(index);
            Assert.Equal(i, index);
        }

        // 已在最后一步：保持不动，不会越界
        Assert.Equal(index, OnboardingGuide.Next(index));
        Assert.Equal(OnboardingGuide.StepCount - 1, OnboardingGuide.Next(OnboardingGuide.StepCount - 1));
    }

    [Fact]
    public void Back_ReturnsToStartAndStopsThere()
    {
        var last = OnboardingGuide.StepCount - 1;
        for (var i = last; i > 0; i--)
            Assert.Equal(i - 1, OnboardingGuide.Back(i));

        Assert.Equal(0, OnboardingGuide.Back(0));
        Assert.Equal(0, OnboardingGuide.Back(-4));
    }

    [Fact]
    public void WalkThroughAllSteps_TerminatesExactly()
    {
        var index = 0;
        var visited = 1;
        while (!OnboardingGuide.IsLast(index))
        {
            index = OnboardingGuide.Next(index);
            visited++;
            Assert.True(visited <= OnboardingGuide.StepCount + 1, "步骤游标出现死循环");
        }
        Assert.Equal(OnboardingGuide.StepCount, visited);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("垃圾数据")]
    public void ShouldShow_WithoutCompletedFlag_IsTrue(string? stored)
        => Assert.True(OnboardingGuide.ShouldShow(stored));

    [Fact]
    public void ShouldShow_AfterCompletedFlag_IsFalse()
    {
        Assert.False(OnboardingGuide.ShouldShow(OnboardingGuide.CompletedValue));
        Assert.Equal("1", OnboardingGuide.CompletedValue);
    }

    [Fact]
    public void PrefKey_IsStable()
    {
        // 偏好键改名会让老用户重新看到引导，这里显式钉住
        Assert.Equal("onboarding.completed", OnboardingGuide.PrefKey);
    }
}
