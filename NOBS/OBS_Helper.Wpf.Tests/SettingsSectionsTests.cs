using System.Text.RegularExpressions;
using OBS_Helper.Wpf.Services.Shell;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// 设置页分类目录（V3.4.2）的一致性校验。
///
/// 设置页从「一条长滚动」改成「左栏目录 + 右侧单页」之后，目录与页面之间又出现了
/// 两份可能漂移的清单：<see cref="SettingsSections.All"/> 与 <c>SettingsPage.xaml</c> 里的卡片名。
/// 这里把它们钉在一起：
/// <list type="number">
///   <item>目录里的每一页，页面上都必须真有对应卡片；</item>
///   <item>页面上每一张设置卡片，也必须在目录里出现（否则用户永远切不到它）；</item>
///   <item>分组 / 页面 id 唯一，且每页的标题、指引、图标都真实存在；</item>
///   <item>中英文案键都存在，避免英文界面出现键名或空串。</item>
/// </list>
/// </summary>
public class SettingsSectionsTests
{
    private static readonly Regex CardNameRegex = new(
        @"x:Name=""(?<name>Card[A-Za-z0-9_]+)""", RegexOptions.Compiled);

    private static readonly Regex TableKeyRegex = new(
        @"\[""(?<key>[^""]+)""\]\s*=", RegexOptions.Compiled);

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

    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { SourceRoot() }.Concat(parts).ToArray()));

    private static HashSet<string> TableKeys(string fileName)
        => TableKeyRegex.Matches(Read("OBS_Helper.Wpf", "Localization", fileName))
            .Select(match => match.Groups["key"].Value)
            .ToHashSet(StringComparer.Ordinal);

    private static List<string> XamlCardNames()
        => CardNameRegex.Matches(Read("OBS_Helper.Wpf", "Views", "SettingsPage.xaml"))
            .Select(match => match.Groups["name"].Value)
            .ToList();

    private static HashSet<string> XamlIconKeys()
        => Regex.Matches(Read("OBS_Helper.Wpf", "Themes", "Icons.xaml"), @"x:Key=""(?<key>IconGeom[^""]+)""")
            .Select(match => match.Groups["key"].Value)
            .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void EverySectionHasACardOnThePage()
    {
        var cards = XamlCardNames();
        var missing = SettingsSections.All
            .Select(section => section.CardName)
            .Where(name => !cards.Contains(name))
            .ToList();

        Assert.True(missing.Count == 0, $"目录里有、页面上找不到的卡片：{string.Join(", ", missing)}");
    }

    [Fact]
    public void EveryCardOnThePageIsInTheDirectory()
    {
        var listed = SettingsSections.All.Select(section => section.CardName).ToHashSet(StringComparer.Ordinal);
        var missing = XamlCardNames().Where(name => !listed.Contains(name)).ToList();

        Assert.True(missing.Count == 0, $"页面上有、目录里漏掉的卡片：{string.Join(", ", missing)}");
    }

    [Fact]
    public void IdsAndCardsAreUnique()
    {
        Assert.Equal(SettingsSections.All.Length, SettingsSections.All.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(SettingsSections.All.Length, SettingsSections.All.Select(s => s.CardName).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(SettingsSections.Groups.Length, SettingsSections.Groups.Select(g => g.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EverySectionBelongsToAKnownAndNonEmptyGroup()
    {
        foreach (var group in SettingsSections.Groups)
            Assert.NotEmpty(SettingsSections.InGroup(group.Id));

        foreach (var section in SettingsSections.All)
            Assert.NotNull(SettingsSections.FindGroup(section.GroupId));

        Assert.Equal(
            SettingsSections.All.Length,
            SettingsSections.Groups.Sum(group => SettingsSections.InGroup(group.Id).Count()));
    }

    [Fact]
    public void DefaultSectionExists()
        => Assert.NotNull(SettingsSections.Find(SettingsSections.DefaultId));

    [Fact]
    public void TitleAndDescriptionKeysExistInBothLanguages()
    {
        var zh = TableKeys("StringTableZhHans.cs");
        var en = TableKeys("StringTableEnUs.cs");

        var keys = SettingsSections.Groups
            .SelectMany(group => new[] { group.TitleKey, group.DescKey })
            .Concat(SettingsSections.All.SelectMany(section => new[] { section.TitleKey, section.DescKey }))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var key in keys)
        {
            Assert.Contains(key, zh);
            Assert.Contains(key, en);
        }
    }

    [Fact]
    public void EverySectionIconExists()
    {
        var icons = XamlIconKeys();
        var missing = SettingsSections.All
            .Select(section => section.IconKey)
            .Where(key => !icons.Contains(key))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0, $"设置目录引用了不存在的图标：{string.Join(", ", missing)}");
    }
}
