using OBS_Helper.Wpf.Services.Interop;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>sidecar 清单校验：URL 白名单 / 哈希 / 路径穿越 / 版本 / 许可证。</summary>
public class SidecarCoreTests
{
    private const string GoodSha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static string ManifestJson(params string[] entries)
        => "{\"schema\":1,\"updated\":\"2026-10-30\",\"sidecars\":[" + string.Join(",", entries) + "]}";

    private static string Entry(
        string id = "mediamtx",
        string version = "1.9.0",
        string url = "https://github.com/bluenviron/mediamtx/releases/download/v1.9.0/mediamtx.zip",
        string sha = GoodSha,
        long size = 12345678,
        string license = "MIT",
        string source = "https://github.com/bluenviron/mediamtx",
        string executable = "mediamtx.exe",
        string installSubdir = "")
        => "{\"id\":\"" + id + "\",\"version\":\"" + version + "\",\"url\":\"" + url +
           "\",\"sha256\":\"" + sha + "\",\"size\":" + size + ",\"license\":\"" + license +
           "\",\"source\":\"" + source + "\",\"executable\":\"" + executable +
           "\",\"installSubdir\":\"" + installSubdir + "\",\"minOs\":\"10.0\",\"arch\":\"x64\",\"args\":[\"cfg.yml\"]}";

    [Fact]
    public void ValidManifestParses()
    {
        var (manifest, errors) = SidecarCore.Parse(ManifestJson(Entry()));
        Assert.Empty(errors);
        Assert.Single(manifest.Entries);
        var entry = manifest.Find("MediaMTX");
        Assert.NotNull(entry);
        Assert.Equal("MIT", entry!.License);
        Assert.Equal("x64", entry.Arch);
        Assert.Equal("cfg.yml", entry.Args[0]);
    }

    [Theory]
    [InlineData("http://github.com/a.zip", "url: must be absolute https")]
    [InlineData("https://evil.example.com/a.zip", "url: host not in allowlist")]
    [InlineData("file:///C:/a.zip", "url: must be absolute https")]
    public void UnsafeUrlsAreRejected(string url, string expected)
    {
        var (_, errors) = SidecarCore.Parse(ManifestJson(Entry(url: url)));
        Assert.Contains(errors, e => e.Contains(expected));
    }

    [Fact]
    public void GitHubReleaseAssetCdnIsAllowed()
    {
        var (_, errors) = SidecarCore.Parse(ManifestJson(
            Entry(url: "https://release-assets.githubusercontent.com/github-production-release-asset/1/2")));
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    [InlineData("")]
    public void BadHashIsRejected(string sha)
    {
        var (_, errors) = SidecarCore.Parse(ManifestJson(Entry(sha: sha)));
        Assert.Contains(errors, e => e.Contains("sha256"));
    }

    [Theory]
    [InlineData("../mediamtx", "id")]
    [InlineData("MediaMTX", "id")]
    [InlineData("a/b", "id")]
    public void BadIdIsRejected(string id, string expected)
    {
        var (_, errors) = SidecarCore.Parse(ManifestJson(Entry(id: id)));
        Assert.Contains(errors, e => e.Contains(expected));
    }

    [Theory]
    [InlineData("../1.9.0")]
    [InlineData("1.9/0")]
    [InlineData("")]
    public void BadVersionIsRejected(string version)
    {
        var (_, errors) = SidecarCore.Parse(ManifestJson(Entry(version: version)));
        Assert.Contains(errors, e => e.Contains("version"));
    }

    [Fact]
    public void UnknownLicenseIsRejected()
    {
        var (_, errors) = SidecarCore.Parse(ManifestJson(Entry(license: "WTFPL")));
        Assert.Contains(errors, e => e.Contains("license"));
    }

    [Fact]
    public void NonHttpsSourceIsRejected()
    {
        var (_, errors) = SidecarCore.Parse(ManifestJson(Entry(source: "http://example.com")));
        Assert.Contains(errors, e => e.Contains("source"));
    }

    [Fact]
    public void TraversingExecutableIsRejected()
    {
        var (_, errors) = SidecarCore.Parse(ManifestJson(Entry(executable: "../windows/system32/cmd.exe")));
        Assert.Contains(errors, e => e.Contains("executable"));
    }

    [Fact]
    public void DuplicateIdsAreRejected()
    {
        var (_, errors) = SidecarCore.Parse(ManifestJson(Entry(), Entry()));
        Assert.Contains(errors, e => e.Contains("duplicate"));
    }

    [Fact]
    public void EmptyManifestIsRejected()
    {
        var (_, errors) = SidecarCore.Parse("{\"sidecars\":[]}");
        Assert.Contains(errors, e => e.Contains("empty"));
        var (_, errors2) = SidecarCore.Parse("not json");
        Assert.NotEmpty(errors2);
    }

    [Fact]
    public void SizeMustBePositive()
    {
        var (_, errors) = SidecarCore.Parse(ManifestJson(Entry(size: 0)));
        Assert.Contains(errors, e => e.Contains("size"));
    }

    [Theory]
    [InlineData("mediamtx.exe", "mediamtx.exe")]
    [InlineData("bin\\mediamtx.exe", "bin/mediamtx.exe")]
    [InlineData("../evil.exe", null)]
    [InlineData("/etc/passwd", null)]
    [InlineData("C:\\Windows\\cmd.exe", null)]
    [InlineData("a/./b", null)]
    [InlineData("a//b", null)]
    [InlineData("a:b", null)]
    [InlineData("", null)]
    public void SafeRelativePathRules(string input, string? expected)
        => Assert.Equal(expected, SidecarCore.SafeRelativePath(input));

    [Theory]
    [InlineData("1.9.0", "1.8.9", 1)]
    [InlineData("v1.9.0", "1.9.0", 0)]
    [InlineData("1.10.0", "1.9.9", 1)]
    [InlineData("1.9", "1.9.0", 0)]
    [InlineData("2.0.0-beta1", "2.0.0", 1)]
    public void VersionComparison(string left, string right, int sign)
    {
        var cmp = SidecarCore.CompareVersion(left, right);
        Assert.Equal(sign, Math.Sign(cmp));
    }

    [Fact]
    public void MinOsGating()
    {
        Assert.True(SidecarCore.MeetsMinOs("10.0", new Version(10, 0)));
        Assert.False(SidecarCore.MeetsMinOs("10.0", new Version(6, 1)));
        Assert.True(SidecarCore.MeetsMinOs("", new Version(6, 1)));
    }

    [Fact]
    public void ManualEntriesMayOmitHashAndSize()
    {
        const string json = "{\"sidecars\":[{\"id\":\"mediamtx\",\"version\":\"manual\",\"url\":\"https://github.com/bluenviron/mediamtx/releases\",\"sha256\":\"\",\"size\":0,\"license\":\"MIT\",\"source\":\"https://github.com/bluenviron/mediamtx\",\"executable\":\"mediamtx.exe\",\"manual\":true}]}";
        var (manifest, errors) = SidecarCore.Parse(json);
        Assert.Empty(errors);
        Assert.True(manifest.Entries[0].Manual);
    }

    [Fact]
    public void ShippedManifestIsValidAndEveryEntryIsManualOrHashed()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "sidecars.json");
        Assert.True(File.Exists(path), path);
        var (manifest, errors) = SidecarCore.Parse(File.ReadAllText(path));
        Assert.Empty(errors);
        Assert.NotEmpty(manifest.Entries);
        Assert.All(manifest.Entries, e => Assert.True(e.Manual || e.Sha256.Length == 64));
    }

    [Fact]
    public void InstallDirectoryIsContained()
    {
        var (manifest, _) = SidecarCore.Parse(ManifestJson(Entry()));
        var path = SidecarCore.InstallDirectory(@"C:\data\sidecars", manifest.Entries[0]);
        Assert.NotNull(path);
        Assert.StartsWith(@"C:\data\sidecars\mediamtx\1.9.0", path);
    }
}
