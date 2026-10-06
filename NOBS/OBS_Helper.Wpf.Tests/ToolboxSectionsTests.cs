using System.Text.RegularExpressions;
using OBS_Helper.Wpf.Services.Tools;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// 工具箱分节导航（V3.0 / C6）与页面实际分节的一致性校验。
///
/// 这类「两处各写一份清单」的结构最容易漂移：加了新分节却忘了加导航项，用户就永远看不到它，
/// 而且**不会有任何报错**。这里把两个真源钉在一起：
/// <see cref="ToolboxSections.Keys"/> ↔ <c>ToolboxPage.xaml</c> 里真实存在的 <c>x:Name="Sec_*"</c>。
/// </summary>
public class ToolboxSectionsTests
{
    private static readonly Regex SectionTitleRegex = new(
        @"<TextBlock\s+x:Name=""(?<name>Sec_[^""]+)""[^>]*SectionTitle", RegexOptions.Compiled);

    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "OBS_Helper.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("找不到源码根目录（OBS_Helper.slnx）");
    }

    private static List<string> XamlSectionNames()
    {
        var path = Path.Combine(SourceRoot(), "OBS_Helper.Wpf", "Views", "ToolboxPage.xaml");
        Assert.True(File.Exists(path), $"工具箱页面缺失：{path}");
        return SectionTitleRegex.Matches(File.ReadAllText(path))
            .Select(m => m.Groups["name"].Value)
            .ToList();
    }

    /// <summary>导航清单里的每一节，页面上都必须真有对应的标题元素。</summary>
    [Fact]
    public void EveryNavSection_HasAMatchingTitleElement()
    {
        var xaml = XamlSectionNames();
        var missing = ToolboxSections.Keys
            .Select(ToolboxSections.ElementNameOf)
            .Where(name => !xaml.Contains(name))
            .ToList();

        Assert.True(missing.Count == 0, $"导航里有、页面上找不到的分节元素：{string.Join(", ", missing)}");
    }

    /// <summary>页面上每一个分节标题，都必须出现在导航清单里（否则用户不知道它存在）。</summary>
    [Fact]
    public void EveryTitleElement_IsListedInNav()
    {
        var expected = ToolboxSections.Keys.Select(ToolboxSections.ElementNameOf).ToHashSet(StringComparer.Ordinal);
        var missing = XamlSectionNames().Where(name => !expected.Contains(name)).ToList();

        Assert.True(missing.Count == 0, $"页面上有、导航里漏掉的分节：{string.Join(", ", missing)}");
    }

    /// <summary>顺序必须与页面一致：导航是「目录」，顺序错了会让人以为跳错了地方。</summary>
    [Fact]
    public void NavOrder_MatchesPageOrder()
    {
        var xaml = XamlSectionNames();
        var listed = ToolboxSections.Keys.Select(ToolboxSections.ElementNameOf).ToList();

        Assert.Equal(xaml, listed);
    }

    [Fact]
    public void ElementNameOf_UsesUnderscoreConvention()
    {
        Assert.Equal("Sec_toolbox_recordingTools", ToolboxSections.ElementNameOf("toolbox.recordingTools"));
        Assert.Equal("Sec_setup_officialObs", ToolboxSections.ElementNameOf("setup.officialObs"));
    }

    /// <summary>不得出现重复键（重复会让两个按钮跳同一个地方，其中一个分节永远进不去）。</summary>
    [Fact]
    public void Keys_AreUnique()
    {
        Assert.Equal(ToolboxSections.Keys.Length, ToolboxSections.Keys.Distinct(StringComparer.Ordinal).Count());
    }
}
