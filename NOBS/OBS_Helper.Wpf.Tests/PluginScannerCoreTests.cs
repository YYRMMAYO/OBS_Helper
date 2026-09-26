using OBS_Helper.Wpf.Services.Plugins;

namespace OBS_Helper.Wpf.Tests;

public class PluginScannerCoreTests : IDisposable
{
    private readonly string _dir;

    public PluginScannerCoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "obshelper_scan_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);

        foreach (var name in new[] { "Move-Transition.dll", "TUNA.dll", "readme.txt" })
            File.WriteAllText(Path.Combine(_dir, name), "dummy");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 清理失败无妨 */ }
    }

    [Fact]
    public void ScanDirectories_ListsOnlyDlls_AndNormalizesStems()
    {
        var result = PluginScannerCore.ScanDirectories(new[] { (_dir, "install") });

        Assert.Equal(2, result.Count);
        // 按主干排序：move-transition < tuna
        Assert.Equal("move-transition", result[0].Stem);
        Assert.Equal("tuna", result[1].Stem);
        Assert.All(result, p => Assert.Equal("install", p.SourceLabel));
        Assert.All(result, p => Assert.EndsWith(".dll", p.FileName, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ScanDirectories_MissingOrEmptyDir_IsSkipped()
    {
        var ghost = Path.Combine(_dir, "ghost");
        var empty = Path.Combine(_dir, "empty");
        Directory.CreateDirectory(empty);

        var result = PluginScannerCore.ScanDirectories(new[]
        {
            (ghost, "install"),
            (empty, "user"),
        });

        Assert.Empty(result);
    }

    [Fact]
    public void ScanDirectories_DuplicatePaths_AreDeduped()
    {
        var result = PluginScannerCore.ScanDirectories(new[] { (_dir, "install"), (_dir, "user") });
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void CatalogId_MatchesBackToCatalogEntry()
    {
        const string catalogJson = """
        {
          "version": "1.0",
          "categories": [{ "key": "audio", "label": "音频", "icon": "x" }],
          "plugins": [
            {
              "id": "tuna",
              "name": "Tuna",
              "category": "audio",
              "desc": "d",
              "url": "https://github.com/univrsal/tuna",
              "dlls": ["tuna"]
            }
          ]
        }
        """;
        var catalog = PluginCatalogCore.Parse(catalogJson)!;
        var result = PluginScannerCore.ScanDirectories(new[] { (_dir, "install") });

        var tuna = result.First(p => p.Stem == "tuna");
        tuna.CatalogId = PluginCatalogCore.MatchByDll(catalog, tuna.FileName)?.Id;
        Assert.Equal("tuna", tuna.CatalogId);
    }
}

public class PluginScanLocationsTests
{
    [Fact]
    public void BuildCandidates_FollowsPriority_LabelsSources_AndDedupes()
    {
        var candidates = PluginScanLocations.BuildCandidates(
            installDir: @"D:\obs-studio",
            obsInstallRoots: new[] { @"C:\Program Files", @"D:\tools", @"D:\TOOLS" },
            steamObsPluginDirs: new[] { @"E:\SteamLibrary\steamapps\common\OBSStudio\obs-plugins\64bit" },
            userPluginsDir: @"C:\Users\u\AppData\Roaming\obs-studio\plugins");

        Assert.Equal(new[]
        {
            @"D:\obs-studio\obs-plugins\64bit",
            @"C:\Program Files\obs-plugins\64bit",
            @"D:\tools\obs-plugins\64bit",
            @"E:\SteamLibrary\steamapps\common\OBSStudio\obs-plugins\64bit",
            @"C:\Users\u\AppData\Roaming\obs-studio\plugins",
        }, candidates.Select(c => c.Dir).ToArray());

        // 实际安装目录与安装根都算 install；用户目录单独标注
        Assert.Equal("install", candidates[0].Label);
        Assert.Equal("user", candidates[^1].Label);

        // 大小写不敏感去重：重复的 D:\TOOLS 只出现一次
        Assert.Equal(5, candidates.Count);
    }

    [Fact]
    public void BuildCandidates_BlankOrNullInputs_AreIgnored()
    {
        var candidates = PluginScanLocations.BuildCandidates(
            installDir: "", obsInstallRoots: new[] { "", null! }, steamObsPluginDirs: Array.Empty<string>(),
            userPluginsDir: null);

        Assert.Empty(candidates);
    }

    [Fact]
    public void GetStandardObsRoots_CoversThreeLayoutsPerDrive()
    {
        var roots = PluginScanLocations.GetStandardObsRoots(new[] { @"C:\", @"D:\" });

        Assert.Equal(new[]
        {
            @"C:\Program Files\obs-studio",
            @"C:\Program Files (x86)\obs-studio",
            @"C:\obs-studio",
            @"D:\Program Files\obs-studio",
            @"D:\Program Files (x86)\obs-studio",
            @"D:\obs-studio",
        }, roots);
    }

    [Fact]
    public void ParseSteamLibraryPaths_UnescapesAndNormalizes()
    {
        const string vdf = """
        "libraryfolders"
        {
        	"0"
        	{
        		"path"		"C:\\Program Files (x86)\\Steam"
        		"label"		""
        	}
        	"1"
        	{
        		"path"		"D:\\SteamLibrary"
        	}
        	"2"
        	{
        		"path"		"E:/Games/Steam"
        	}
        }
        """;

        var paths = PluginScanLocations.ParseSteamLibraryPaths(vdf);

        Assert.Equal(new[]
        {
            @"C:\Program Files (x86)\Steam",
            @"D:\SteamLibrary",
            @"E:\Games\Steam",
        }, paths);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no path entries here")]
    public void ParseSteamLibraryPaths_EmptyOrInvalid_ReturnsEmpty(string? content)
    {
        Assert.Empty(PluginScanLocations.ParseSteamLibraryPaths(content));
    }

    // ---------------------------------------------------------- OBS 32.x 新插件布局（V2.9）

    [Fact]
    public void BuildCandidates_IncludesGlobalPluginsDir_AfterUserDir()
    {
        var candidates = PluginScanLocations.BuildCandidates(
            installDir: null,
            obsInstallRoots: Array.Empty<string>(),
            steamObsPluginDirs: Array.Empty<string>(),
            userPluginsDir: @"C:\Users\u\AppData\Roaming\obs-studio\plugins",
            globalPluginsDir: @"C:\ProgramData\obs-studio\plugins");

        Assert.Equal(new[]
        {
            @"C:\Users\u\AppData\Roaming\obs-studio\plugins",
            @"C:\ProgramData\obs-studio\plugins",
        }, candidates.Select(c => c.Dir).ToArray());
        Assert.Equal("user", candidates[0].Label);
        Assert.Equal("global", candidates[1].Label);
    }

    [Fact]
    public void BuildCandidates_GlobalDirIsOptional()
    {
        // 不传全局目录时行为与旧版完全一致（保持向后兼容）
        var candidates = PluginScanLocations.BuildCandidates(
            installDir: null,
            obsInstallRoots: Array.Empty<string>(),
            steamObsPluginDirs: Array.Empty<string>(),
            userPluginsDir: @"C:\Users\u\AppData\Roaming\obs-studio\plugins");

        Assert.Single(candidates);
    }

    [Fact]
    public void NestedPluginDirs_BuildsObs322Layout()
    {
        // OBS 32.x 布局：<插件根>\<插件名>\bin\64bit\<插件>.dll
        var dirs = PluginScanLocations.NestedPluginDirs(
            @"C:\ProgramData\obs-studio\plugins",
            new[] { "obs-helper-dock", "advanced-scene-switcher" });

        Assert.Equal(new[]
        {
            @"C:\ProgramData\obs-studio\plugins\obs-helper-dock\bin\64bit",
            @"C:\ProgramData\obs-studio\plugins\advanced-scene-switcher\bin\64bit",
        }, dirs);
    }

    [Fact]
    public void NestedPluginDirs_AcceptsFullPathsAndDedupes()
    {
        var dirs = PluginScanLocations.NestedPluginDirs(
            @"C:\ProgramData\obs-studio\plugins",
            new[]
            {
                @"C:\ProgramData\obs-studio\plugins\obs-helper-dock",
                @"C:\ProgramData\obs-studio\plugins\obs-helper-dock\",
                @"C:\ProgramData\obs-studio\plugins\OBS-Helper-Dock",
            });

        Assert.Single(dirs);
        Assert.EndsWith(@"obs-helper-dock\bin\64bit", dirs[0]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NestedPluginDirs_BlankRoot_ReturnsEmpty(string? root)
        => Assert.Empty(PluginScanLocations.NestedPluginDirs(root, new[] { "some-plugin" }));

    [Fact]
    public void NestedPluginDirs_SkipsBlankAndTraversalEntries()
    {
        var dirs = PluginScanLocations.NestedPluginDirs(
            @"C:\ProgramData\obs-studio\plugins",
            new[] { null, "", "   ", "..", "." });

        Assert.Empty(dirs);
    }
}
