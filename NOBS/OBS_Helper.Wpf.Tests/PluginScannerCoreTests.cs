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
