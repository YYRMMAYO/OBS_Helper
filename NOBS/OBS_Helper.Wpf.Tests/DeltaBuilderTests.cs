using OBS_Helper.Wpf.Services.Update;

namespace OBS_Helper.Wpf.Tests;

public class DeltaBuilderTests
{
    private static ManifestFileEntry E(string path, long size, string sha = "abc")
        => new() { Path = path, Size = size, Sha256 = sha };

    private static Dictionary<string, ManifestFileEntry> Map(params ManifestFileEntry[] entries)
        => entries.ToDictionary(e => e.Path, e => e);

    [Fact]
    public void Build_NewFile_GoesToFiles()
    {
        var manifest = DeltaBuilder.Build("2.0.0", "2.1.0",
            Map(E("a.dll", 100)),
            Map(E("a.dll", 100), E("b.dll", 200)));

        Assert.Equal("2.0.0", manifest.BaseVersion);
        Assert.Equal("2.1.0", manifest.TargetVersion);
        Assert.Contains(manifest.Files, f => f.Path == "b.dll");
        Assert.DoesNotContain(manifest.Files, f => f.Path == "a.dll");
        Assert.Empty(manifest.Remove);
    }

    [Fact]
    public void Build_ChangedFile_GoesToFiles()
    {
        var manifest = DeltaBuilder.Build("2.0.0", "2.1.0",
            Map(E("a.dll", 100, "oldhash")),
            Map(E("a.dll", 120, "newhash")));

        Assert.Contains(manifest.Files, f => f.Path == "a.dll" && f.Size == 120);
    }

    [Fact]
    public void Build_RemovedFile_GoesToRemove()
    {
        var manifest = DeltaBuilder.Build("2.0.0", "2.1.0",
            Map(E("a.dll", 100), E("gone.dll", 50)),
            Map(E("a.dll", 100)));

        Assert.Contains(manifest.Remove, p => p == "gone.dll");
        Assert.DoesNotContain(manifest.Remove, p => p == "a.dll");
        Assert.Empty(manifest.Files);
    }

    [Fact]
    public void Build_SameFiles_EmptyDelta()
    {
        var manifest = DeltaBuilder.Build("2.0.0", "2.1.0",
            Map(E("a.dll", 100), E("b.dll", 200)),
            Map(E("b.dll", 200), E("a.dll", 100)));

        Assert.Empty(manifest.Files);
        Assert.Empty(manifest.Remove);
    }

    [Fact]
    public void Build_SameHashDifferentOrder_NoChange()
    {
        // 内容一致、只是字典顺序不同 → 不产生增量
        var manifest = DeltaBuilder.Build("2.0.0", "2.1.0",
            Map(E("a.dll", 100), E("b.dll", 200)),
            Map(E("b.dll", 200), E("a.dll", 100)));

        Assert.Empty(manifest.Files);
    }

    [Fact]
    public void SortEntries_OrdersByPathOrdinal()
    {
        var sorted = DeltaBuilder.SortEntries(new[]
        {
            E("z.dll", 1),
            E("a\\b.dll", 2),
            E("a.dll", 3),
        });

        Assert.Equal(new[] { "a.dll", "a\\b.dll", "z.dll" }, sorted.Select(e => e.Path));
    }
}
