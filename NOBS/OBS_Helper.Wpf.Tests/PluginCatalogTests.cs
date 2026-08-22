using OBS_Helper.Wpf.Services.Plugins;

namespace OBS_Helper.Wpf.Tests;

public class PluginCatalogTests
{
    private const string SampleJson = """
    {
      "version": "1.0",
      "updated": "2026-08-22",
      "categories": [
        { "key": "auto", "label": "自动化", "icon": "A" },
        { "key": "ai", "label": "AI", "icon": "B" }
      ],
      "plugins": [
        {
          "id": "move-transition",
          "name": "Move Transition",
          "category": "auto",
          "desc": "d",
          "url": "https://github.com/exeldro/obs-move-transition",
          "repo": "exeldro/obs-move-transition",
          "dlls": ["move-transition"]
        },
        {
          "id": "localvocal",
          "name": "LocalVocal",
          "category": "ai",
          "badge": "AI",
          "desc": "d2",
          "url": "https://github.com/occ-ai/obs-localvocal",
          "repo": "occ-ai/obs-localvocal",
          "dlls": ["localvocal"],
          "aiCostCpu": "+5~10% CPU",
          "aiCostMem": "+200~500MB"
        },
        {
          "id": "mystery",
          "name": "Mystery",
          "category": "ghost",
          "desc": "orphan category",
          "url": "https://example.com"
        }
      ]
    }
    """;

    [Fact]
    public void Parse_ValidJson_ReturnsData()
    {
        var data = PluginCatalogCore.Parse(SampleJson);
        Assert.NotNull(data);
        Assert.Equal("1.0", data!.Version);
        Assert.Equal(3, data.Plugins.Count);
        Assert.Equal(2, data.Categories.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{ \"version\": \"1.0\", \"plugins\": [] }")]
    public void Parse_Invalid_ReturnsNull(string json)
    {
        Assert.Null(PluginCatalogCore.Parse(json));
    }

    [Fact]
    public void MatchByDll_NormalizesCaseAndPath()
    {
        var data = PluginCatalogCore.Parse(SampleJson)!;
        var hit = PluginCatalogCore.MatchByDll(data, @"C:\Program Files\OBS\obs-plugins\64bit\Move-Transition.DLL");
        Assert.NotNull(hit);
        Assert.Equal("move-transition", hit!.Id);

        Assert.Null(PluginCatalogCore.MatchByDll(data, "unknown.dll"));
        Assert.Null(PluginCatalogCore.MatchByDll(null, "move-transition.dll"));
    }

    [Fact]
    public void NormalizeDllStem_StripsPathExtensionAndCase()
    {
        Assert.Equal("foo-bar", PluginCatalogCore.NormalizeDllStem(@"D:\x\FOO-Bar.DLL"));
        Assert.Equal("tuna", PluginCatalogCore.NormalizeDllStem("/usr/lib/obs-plugins/tuna.so".Replace(".so", ".dll")));
        Assert.Equal("", PluginCatalogCore.NormalizeDllStem(""));
    }

    [Fact]
    public void FindById_CaseInsensitive()
    {
        var data = PluginCatalogCore.Parse(SampleJson)!;
        Assert.NotNull(PluginCatalogCore.FindById(data, "LocalVocal"));
        Assert.Null(PluginCatalogCore.FindById(data, ""));
        Assert.Null(PluginCatalogCore.FindById(null, "localvocal"));
    }

    [Fact]
    public void GroupByCategory_FollowsDeclaredOrder_AndBucketsOrphans()
    {
        var data = PluginCatalogCore.Parse(SampleJson)!;
        var groups = PluginCatalogCore.GroupByCategory(data);

        Assert.Equal(3, groups.Count);
        Assert.Equal("auto", groups[0].Category.Key);   // 声明顺序在前
        Assert.Equal("ai", groups[1].Category.Key);
        Assert.Equal("_other", groups[2].Category.Key); // 未声明分类兜底
        Assert.Single(groups[0].Items);
        Assert.Single(groups[1].Items);
        Assert.Single(groups[2].Items);
    }

    [Fact]
    public void AiCost_FlagWorks()
    {
        var data = PluginCatalogCore.Parse(SampleJson)!;
        Assert.True(data.Plugins.First(p => p.Id == "localvocal").HasAiCost);
        Assert.False(data.Plugins.First(p => p.Id == "move-transition").HasAiCost);
    }
}

public class PluginRepoNormalizationTests
{
    [Theory]
    [InlineData("exeldro/obs-move-transition", "exeldro/obs-move-transition")]
    [InlineData("https://github.com/occ-ai/obs-localvocal/", "occ-ai/obs-localvocal")]
    [InlineData("http://github.com/Aitum/obs-vertical-canvas/releases/latest", "Aitum/obs-vertical-canvas")]
    [InlineData("  WarmUpTill/SceneSwitcher  ", "WarmUpTill/SceneSwitcher")]
    public void NormalizeRepo_AcceptsCommonForms(string input, string expected)
    {
        Assert.Equal(expected, PluginCatalogCore.NormalizeRepo(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("just-a-name")]
    [InlineData("https://gitlab.com/a/b")]
    public void NormalizeRepo_RejectsInvalid(string input)
    {
        Assert.Equal("", PluginCatalogCore.NormalizeRepo(input));
    }
}
