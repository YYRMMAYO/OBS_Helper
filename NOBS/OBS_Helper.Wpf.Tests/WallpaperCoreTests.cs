using System.IO;
using System.Text;
using OBS_Helper.Wpf.Services.Appearance;

namespace OBS_Helper.Wpf.Tests;

/// <summary>壁纸纯逻辑（V3.1.0 / W1~W9）：校验、归一化、轮播顺序、迁移与 manifest 容错。</summary>
public class WallpaperCoreTests
{
    // ------------------------------------------------------------ 格式 / 大小 / 尺寸

    [Theory]
    [InlineData("a.jpg", true)]
    [InlineData("a.JPEG", true)]
    [InlineData("a.png", true)]
    [InlineData("a.bmp", true)]
    [InlineData("a.tif", true)]
    [InlineData("a.tiff", true)]
    [InlineData("a.webp", false)]
    [InlineData("a.avif", false)]
    [InlineData("a.heic", false)]
    [InlineData("a.gif", false)]
    [InlineData("", false)]
    public void ExtensionWhitelist(string name, bool expected)
        => Assert.Equal(expected, WallpaperCore.IsSupportedExtension(name));

    [Fact]
    public void ValidateRejectsOversizeAndUnsupported()
    {
        Assert.Equal(WallpaperImportError.Unsupported,
            WallpaperCore.Validate("a.webp", 1024));
        Assert.Equal(WallpaperImportError.TooLarge,
            WallpaperCore.Validate("a.jpg", WallpaperCore.MaxFileBytes + 1));
        Assert.Equal(WallpaperImportError.TooManyPixels,
            WallpaperCore.Validate("a.jpg", 1024, WallpaperCore.MaxDimension + 1, 100));
        Assert.Equal(WallpaperImportError.Missing,
            WallpaperCore.Validate("a.jpg", 1024, exists: false));
        Assert.Equal(WallpaperImportError.Corrupt,
            WallpaperCore.Validate("a.jpg", 0));
        Assert.Equal(WallpaperImportError.None,
            WallpaperCore.Validate("a.jpg", 1024, 3840, 2160));
    }

    [Fact]
    public void HashIsStableAndContentBased()
    {
        var dir = TempDir();
        try
        {
            var a = Path.Combine(dir, "a.jpg");
            var b = Path.Combine(dir, "b.jpg");
            File.WriteAllBytes(a, Encoding.ASCII.GetBytes("same"));
            File.WriteAllBytes(b, Encoding.ASCII.GetBytes("same"));
            File.WriteAllBytes(Path.Combine(dir, "c.jpg"), Encoding.ASCII.GetBytes("other"));

            Assert.Equal(WallpaperCore.HashFile(a), WallpaperCore.HashFile(b));
            Assert.NotEqual(WallpaperCore.HashFile(a), WallpaperCore.HashFile(Path.Combine(dir, "c.jpg")));
            Assert.True(WallpaperCore.IsValidId(WallpaperCore.HashFile(a)));
            Assert.False(WallpaperCore.IsValidId("../../evil"));
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void FileNameKeepsKnownExtension()
    {
        var id = new string('a', 64);
        Assert.Equal(id + ".png", WallpaperCore.FileNameFor(id, ".png"));
        Assert.Equal(id + ".tiff", WallpaperCore.FileNameFor(id, ".TIFF"));
    }

    // ------------------------------------------------------------ 归一化

    [Theory]
    [InlineData("fill", WallpaperFit.Fill)]
    [InlineData("contain", WallpaperFit.Contain)]
    [InlineData("fit", WallpaperFit.Contain)]
    [InlineData("center", WallpaperFit.Center)]
    [InlineData("tile", WallpaperFit.Tile)]
    [InlineData("something-else", WallpaperFit.Fill)]
    [InlineData(null, WallpaperFit.Fill)]
    public void FitNormalization(string? key, WallpaperFit expected)
        => Assert.Equal(expected, WallpaperCore.NormalizeFit(key));

    [Fact]
    public void DimUsesThemeDefaultAndClamps()
    {
        Assert.Equal(WallpaperCore.DefaultDimLight, WallpaperCore.NormalizeDim(-1, dark: false));
        Assert.Equal(WallpaperCore.DefaultDimDark, WallpaperCore.NormalizeDim(-1, dark: true));
        Assert.Equal(WallpaperCore.DefaultDimDark, WallpaperCore.NormalizeDim(-5, dark: true));
        Assert.Equal(WallpaperCore.MaxDim, WallpaperCore.NormalizeDim(5, dark: false));
        Assert.Equal(0.25, WallpaperCore.NormalizeDim(0.25, dark: false));
        Assert.False(WallpaperCore.IsCustomDim(-1));
        Assert.True(WallpaperCore.IsCustomDim(0));
    }

    [Theory]
    [InlineData(-3, 0)]
    [InlineData(0, 0)]
    [InlineData(12, 12)]
    [InlineData(100, 24)]
    public void BlurNormalization(double input, double expected)
        => Assert.Equal(expected, WallpaperCore.NormalizeBlur(input));

    [Theory]
    [InlineData(30, 30)]
    [InlineData(60, 60)]
    [InlineData(300, 300)]
    [InlineData(900, 900)]
    [InlineData(1800, 1800)]
    [InlineData(3600, 3600)]
    [InlineData(42, WallpaperCore.DefaultSlideshowSeconds)]
    [InlineData(0, WallpaperCore.DefaultSlideshowSeconds)]
    public void SlideshowIntervalWhitelist(int input, int expected)
        => Assert.Equal(expected, WallpaperCore.NormalizeSlideshowSeconds(input));

    [Fact]
    public void SlideshowNeedsTwoItems()
    {
        Assert.False(WallpaperCore.CanSlideshow(true, 1));
        Assert.False(WallpaperCore.CanSlideshow(false, 5));
        Assert.True(WallpaperCore.CanSlideshow(true, 2));
    }

    [Fact]
    public void DecodeSizeOnlyShrinksAndCapsAt3840()
    {
        // 小图不放大
        Assert.Equal((800, 600), WallpaperCore.DecodeSize(1920, 1080, 800, 600));
        // 4K 图按窗口缩小
        var (w, h) = WallpaperCore.DecodeSize(1200, 800, 3840, 2160);
        Assert.True(w <= WallpaperCore.DecodeMaxEdge && h <= WallpaperCore.DecodeMaxEdge);
        Assert.True(w > 0 && h > 0);
        // 8K 图：最长边不超过渲染上限
        var (w8, h8) = WallpaperCore.DecodeSize(3840, 2160, 7680, 4320);
        Assert.True(Math.Max(w8, h8) <= WallpaperCore.DecodeMaxEdge);
    }

    // ------------------------------------------------------------ 轮播

    [Fact]
    public void OrderWithoutShuffleKeepsInputOrder()
    {
        var ids = new[] { "a", "b", "c" };
        Assert.Equal(ids, WallpaperCore.BuildOrder(ids, shuffle: false));
    }

    [Fact]
    public void ShuffleKeepsAllItemsWithoutRepeats()
    {
        var ids = Enumerable.Range(0, 20).Select(i => i.ToString()).ToArray();
        var order = WallpaperCore.BuildOrder(ids, shuffle: true, new Random(42));
        Assert.Equal(ids.Length, order.Count);
        Assert.Equal(ids.OrderBy(x => x), order.OrderBy(x => x));
        Assert.Equal(order.Count, order.Distinct().Count());
    }

    [Fact]
    public void SingleItemOrderIsStable()
        => Assert.Equal(new[] { "only" }, WallpaperCore.BuildOrder(new[] { "only" }, shuffle: true));

    [Fact]
    public void NextIndexWrapsAndHandlesEmpty()
    {
        Assert.Equal(1, WallpaperCore.NextIndex(0, 2));
        Assert.Equal(0, WallpaperCore.NextIndex(1, 2));
        Assert.Equal(0, WallpaperCore.NextIndex(-1, 3));
        Assert.Equal(-1, WallpaperCore.NextIndex(0, 0));
    }

    [Fact]
    public void RemoveFromOrderKeepsNeighbouringIndex()
    {
        var (order, index) = WallpaperCore.RemoveFromOrder(new[] { "a", "b", "c" }, "a", 0);
        Assert.Equal(new[] { "b", "c" }, order);
        Assert.Equal(0, index);

        var (last, lastIndex) = WallpaperCore.RemoveFromOrder(new[] { "a", "b" }, "b", 1);
        Assert.Equal(new[] { "a" }, last);
        Assert.Equal(0, lastIndex);

        var (empty, emptyIndex) = WallpaperCore.RemoveFromOrder(new[] { "a" }, "a", 0);
        Assert.Empty(empty);
        Assert.Equal(-1, emptyIndex);
    }

    // ------------------------------------------------------------ 迁移

    [Theory]
    [InlineData(1, "c:\\old.jpg", true, "", false, WallpaperMigrationAction.None)]
    [InlineData(0, "", true, "", false, WallpaperMigrationAction.None)]
    [InlineData(0, "c:\\old.jpg", false, "", false, WallpaperMigrationAction.FallbackDefault)]
    [InlineData(0, "c:\\old.jpg", true, "", false, WallpaperMigrationAction.ImportLegacy)]
    [InlineData(0, "c:\\old.jpg", true, "abc", true, WallpaperMigrationAction.None)]
    public void MigrationDecision(
        int version, string path, bool exists, string id, bool idExists, WallpaperMigrationAction expected)
        => Assert.Equal(expected, WallpaperCore.DecideMigration(version, path, exists, id, idExists));

    [Fact]
    public void ResolveCurrentIdFallsBackToFirstItem()
    {
        var library = new List<WallpaperItem>
        {
            new() { Id = "a", File = "a.jpg" },
            new() { Id = "b", File = "b.jpg" }
        };
        Assert.Equal("b", WallpaperCore.ResolveCurrentId("b", library));
        Assert.Equal("a", WallpaperCore.ResolveCurrentId("missing", library));
        Assert.Equal("", WallpaperCore.ResolveCurrentId("", new List<WallpaperItem>()));
    }

    // ------------------------------------------------------------ manifest

    [Fact]
    public void ManifestRoundTrip()
    {
        var manifest = new WallpaperManifest
        {
            Items =
            {
                new WallpaperItem
                {
                    Id = new string('a', 64), File = new string('a', 64) + ".jpg",
                    Name = "夜景.jpg", Width = 3840, Height = 2160, Bytes = 1234,
                    AddedAt = DateTimeOffset.Parse("2026-10-18T12:00:00Z")
                }
            }
        };

        var json = WallpaperCore.SerializeManifest(manifest);
        var parsed = WallpaperCore.TryParseManifest(json);
        Assert.NotNull(parsed);
        var item = Assert.Single(parsed!.Items);
        Assert.Equal("夜景.jpg", item.Name);
        Assert.Equal(3840, item.Width);
    }

    [Fact]
    public void CorruptManifestReturnsNull()
    {
        Assert.Null(WallpaperCore.TryParseManifest("not json"));
        Assert.Null(WallpaperCore.TryParseManifest(""));
        Assert.Null(WallpaperCore.TryParseManifest("null"));
    }

    [Fact]
    public void ManifestDropsInvalidAndDuplicateIds()
    {
        var valid = new string('b', 64);
        var json = $$"""
        {
          "version": 1,
          "items": [
            { "id": "{{valid}}", "file": "{{valid}}.png", "name": "ok" },
            { "id": "../evil", "file": "evil.png" },
            { "id": "{{valid}}", "file": "{{valid}}.jpg", "name": "dup" },
            { "id": "", "file": "empty.png" }
          ]
        }
        """;

        var parsed = WallpaperCore.TryParseManifest(json);
        Assert.NotNull(parsed);
        var item = Assert.Single(parsed!.Items);
        Assert.Equal(valid, item.Id);
    }

    // ------------------------------------------------------------ 文件库

    [Fact]
    public void StoreImportsDeduplicatesAndDeletes()
    {
        var root = TempDir();
        var srcDir = TempDir();
        try
        {
            var source = Path.Combine(srcDir, "夜景.png");
            File.WriteAllBytes(source, new byte[] { 1, 2, 3, 4, 5 });

            var store = new WallpaperStore(root);
            var first = store.Import(source, 1920, 1080);
            Assert.True(first.Ok);
            Assert.False(first.Reused);
            Assert.True(File.Exists(first.Item!.FullPath(root)));

            var second = store.Import(source, 1920, 1080);
            Assert.True(second.Ok);
            Assert.True(second.Reused);
            Assert.Single(store.Load());

            // 原文件移走不影响库内副本
            File.Delete(source);
            var library = store.Load();
            Assert.Single(library);
            Assert.True(File.Exists(library[0].FullPath(root)));

            Assert.True(store.Delete(library[0].Id));
            Assert.Empty(store.Load());
            Assert.False(File.Exists(library[0].FullPath(root)));
        }
        finally
        {
            Cleanup(root);
            Cleanup(srcDir);
        }
    }

    [Fact]
    public void StoreRebuildsCorruptManifestFromDirectory()
    {
        var root = TempDir();
        var srcDir = TempDir();
        try
        {
            var source = Path.Combine(srcDir, "a.jpg");
            File.WriteAllBytes(source, new byte[] { 9, 8, 7 });
            var store = new WallpaperStore(root);
            var imported = store.Import(source);
            Assert.True(imported.Ok);

            File.WriteAllText(store.ManifestPath, "{ this is broken");
            var library = store.Load();

            Assert.Single(library);
            Assert.Equal(imported.Item!.Id, library[0].Id);
            // 重建后 manifest 已重新可读
            Assert.NotNull(WallpaperCore.TryParseManifest(File.ReadAllText(store.ManifestPath)));
        }
        finally
        {
            Cleanup(root);
            Cleanup(srcDir);
        }
    }

    [Fact]
    public void StoreRejectsUnsupportedAndOversized()
    {
        var root = TempDir();
        var srcDir = TempDir();
        try
        {
            var webp = Path.Combine(srcDir, "a.webp");
            File.WriteAllBytes(webp, new byte[] { 1 });
            var store = new WallpaperStore(root);
            Assert.Equal(WallpaperImportError.Unsupported, store.Import(webp).Error);

            var huge = Path.Combine(srcDir, "huge.jpg");
            using (var fs = File.Create(huge))
            {
                fs.SetLength(WallpaperCore.MaxFileBytes + 1);
            }
            Assert.Equal(WallpaperImportError.TooLarge, store.Import(huge).Error);
        }
        finally
        {
            Cleanup(root);
            Cleanup(srcDir);
        }
    }

    private static string TempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "obshelper-wallpaper-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Cleanup(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch (Exception) { /* 测试清理失败无妨 */ }
    }
}
