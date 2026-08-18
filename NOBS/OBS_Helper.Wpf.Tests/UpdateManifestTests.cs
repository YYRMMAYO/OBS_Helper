using System.Text.Json;
using OBS_Helper.Wpf.Services.Update;

namespace OBS_Helper.Wpf.Tests;

public class UpdateManifestTests
{
    [Fact]
    public void RoundTrip_SerializesAndDeserializes()
    {
        var manifest = new UpdateManifest
        {
            Format = 1,
            BaseVersion = "2.0.0",
            TargetVersion = "2.1.0",
            Files =
            {
                new ManifestFileEntry { Path = "OBS_Helper.dll", Size = 1234, Sha256 = "a".PadLeft(64, 'a') },
            },
            Remove = { "old.txt" },
        };

        var json = JsonSerializer.Serialize(manifest);
        var back = JsonSerializer.Deserialize<UpdateManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(back);
        Assert.Equal("2.0.0", back!.BaseVersion);
        Assert.Equal("2.1.0", back.TargetVersion);
        Assert.Single(back.Files);
        Assert.Equal("OBS_Helper.dll", back.Files[0].Path);
        Assert.Equal(1234, back.Files[0].Size);
        Assert.Equal("old.txt", Assert.Single(back.Remove));
    }

    [Fact]
    public void Deserialize_CaseInsensitiveKeys()
    {
        // 构建脚本可能产出小写 / 首字母大写混合的键，解析必须大小写不敏感
        var json = """{"format":1,"baseVersion":"1.9.0","targetVersion":"2.0.0","files":[{"path":"x.dll","size":5,"sha256":"abc"}],"remove":[]}""";
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(manifest);
        Assert.Equal("1.9.0", manifest!.BaseVersion);
        Assert.Single(manifest.Files);
    }
}

public class UpdatePathsTests
{
    [Theory]
    [InlineData("OBS_Helper.dll")]
    [InlineData("sub/OBS_Helper.dll")]
    [InlineData("a/b/c.txt")]
    public void NormalizeRel_Valid(string rel)
    {
        Assert.DoesNotContain("..", UpdatePaths.NormalizeRel(rel));
        Assert.False(Path.IsPathRooted(UpdatePaths.NormalizeRel(rel)));
    }

    [Theory]
    [InlineData("../evil.dll")]
    [InlineData("a/../../evil.dll")]
    [InlineData("/abs/path.dll")]
    [InlineData("C:\\Windows\\evil.dll")]
    public void NormalizeRel_Traversal_Throws(string rel)
    {
        Assert.Throws<InvalidDataException>(() => UpdatePaths.NormalizeRel(rel));
    }
}

public class KbVersionTests
{
    [Theory]
    [InlineData("1.4", "1.5", true)]
    [InlineData("1.5", "1.5", false)]
    [InlineData("1.5.2", "1.5.1", false)]
    [InlineData("", "1.0", true)]
    [InlineData("1.0", "", false)]
    [InlineData("1.5-beta", "1.5", false)]
    [InlineData("1.4", "1.5-beta", true)]
    public void IsNewer_ComparesNumericPrefix(string current, string remote, bool expected)
    {
        Assert.Equal(expected, KbVersion.IsNewer(current, remote));
    }

    [Theory]
    [InlineData("garbage", 0, 0)]
    [InlineData(null, 0, 0)]
    [InlineData("1.5", 1, 5)]
    [InlineData("1.5.2", 1, 5)]
    [InlineData("2", 2, 0)]
    public void Parse_Tolerant(string? input, int major, int minor)
    {
        var v = KbVersion.Parse(input);
        Assert.Equal(major, v.Major);
        Assert.Equal(minor, v.Minor);
    }
}
