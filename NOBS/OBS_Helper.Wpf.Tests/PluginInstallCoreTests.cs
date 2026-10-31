using System.IO.Compression;
using System.Text;
using OBS_Helper.Wpf.Models.Plugins;
using OBS_Helper.Wpf.Services.Plugins;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// 插件安装的纯逻辑（V3.3.0 / P0-5）：版本、资产选择、zip 安全、PE 架构、GitHub 解析。
/// 这里每一条失败都对应一种真实事故：装错架构、zip slip、把源码包当安装包、限流刷屏。
/// </summary>
public class PluginInstallCoreTests
{
    [Theory]
    [InlineData("3.3.0", "3.2.9", 1)]
    [InlineData("v3.3", "3.3.0", 0)]
    [InlineData("3.3.0-beta.2", "3.3.0", -1)]
    [InlineData("1.2", "1.10", -1)]
    [InlineData("", "0.1", -1)]
    public void CompareVersions_IsNumericAware(string a, string b, int expected)
        => Assert.Equal(expected, Math.Sign(PluginInstallCore.CompareVersions(a, b)));

    [Fact]
    public void SelectAsset_PrefersWindowsX64Zip_AndSkipsSourceCode()
    {
        var assets = new[]
        {
            new PluginReleaseAsset { Name = "plugin-src.zip", Url = "https://github.com/x/y", Size = 10 },
            new PluginReleaseAsset { Name = "plugin-linux.tar.gz", Url = "https://github.com/x/y", Size = 20 },
            new PluginReleaseAsset { Name = "plugin-1.0.7z", Url = "https://github.com/x/y", Size = 30 },
            new PluginReleaseAsset { Name = "plugin-1.0-windows-x64.zip", Url = "https://github.com/x/y", Size = 40 },
            new PluginReleaseAsset { Name = "plugin-1.0-portable.zip", Url = "https://github.com/x/y", Size = 50 }
        };

        var selected = PluginInstallCore.SelectAsset(assets);
        Assert.NotNull(selected);
        Assert.Equal("plugin-1.0-windows-x64.zip", selected!.Name);
    }

    [Fact]
    public void SelectAsset_ReturnsNullWhenOnlyUnsupportedArchives()
    {
        Assert.Null(PluginInstallCore.SelectAsset(new[]
        {
            new PluginReleaseAsset { Name = "plugin.7z", Url = "https://github.com/x/y" },
            new PluginReleaseAsset { Name = "Source code (zip)", Url = "https://github.com/x/y" }
        }));
        Assert.Null(PluginInstallCore.SelectAsset(null));
    }

    [Theory]
    [InlineData("bin/64bit/plugin.dll", true)]
    [InlineData("obs-plugins/64bit/plugin.dll", true)]
    [InlineData("../escape.dll", false)]
    [InlineData("/rooted/plugin.dll", false)]
    [InlineData(@"C:\plugin.dll", false)]
    [InlineData("bin/64bit/..", false)]
    [InlineData("bin/64bit/evil.exe", true)] // 路径本身安全，扩展名由 IsAllowedExtension 拒绝
    public void IsSafeEntryPath_RejectsTraversal(string entry, bool expected)
        => Assert.Equal(expected, PluginInstallCore.IsSafeEntryPath(entry, out _));

    [Theory]
    [InlineData("plugin.dll", true)]
    [InlineData("data/locale/en-US.ini", true)]
    [InlineData("payload.exe", false)]
    [InlineData("install.bat", false)]
    [InlineData("run.ps1", false)]
    [InlineData("setup.msi", false)]
    public void IsAllowedExtension_RejectsExecutables(string path, bool expected)
        => Assert.Equal(expected, PluginInstallCore.IsAllowedExtension(path));

    [Fact]
    public void CommonRoot_IsStrippedOnlyForWrapperFolder()
    {
        var entries = new[] { "plugin-1.2/bin/64bit/a.dll", "plugin-1.2/data/locale/en-US.ini" };
        var root = PluginInstallCore.CommonRootPrefix(entries);

        Assert.Equal("plugin-1.2", root);
        Assert.Equal("bin/64bit/a.dll", PluginInstallCore.MapEntry(entries[0], root));

        // 顶层是 bin/ 时不能剥（那是真实结构）
        Assert.Equal("", PluginInstallCore.CommonRootPrefix(new[] { "bin/64bit/a.dll", "data/x.ini" }));
    }

    [Fact]
    public void ValidateArchive_AcceptsNormalPluginZip()
    {
        using var zip = BuildZip(new[]
        {
            ("plugin/bin/64bit/plugin.dll", 16),
            ("plugin/data/locale/en-US.ini", 8)
        });

        var error = PluginInstallCore.ValidateArchive(zip, out var total, out var paths);
        Assert.Equal("", error);
        Assert.True(total > 0);
        Assert.Equal(2, paths.Count);
    }

    [Fact]
    public void ValidateArchive_AcceptsDirectoryEntries()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            zip.CreateEntry("plugin/");
            zip.CreateEntry("plugin/bin/");
            zip.CreateEntry("plugin/bin/64bit/");
            var e = zip.CreateEntry("plugin/bin/64bit/plugin.dll");
            using var s = e.Open();
            s.Write(new byte[16]);
        }
        ms.Position = 0;
        using var read = new ZipArchive(ms, ZipArchiveMode.Read);

        var error = PluginInstallCore.ValidateArchive(read, out _, out var paths);
        Assert.Equal("", error);
        Assert.Single(paths);
    }

    [Fact]
    public void ValidateArchive_RejectsZipSlip()
    {
        using var zip = BuildZip(new[] { ("../../evil.dll", 16) });
        Assert.Equal("PLUGIN_ZIP_UNSAFE", PluginInstallCore.ValidateArchive(zip, out _, out _));
    }

    [Fact]
    public void ValidateArchive_RejectsExecutableEntries()
    {
        using var zip = BuildZip(new[] { ("bin/64bit/plugin.dll", 16), ("installer.exe", 16) });
        Assert.Equal("PLUGIN_ZIP_UNSAFE_FILE", PluginInstallCore.ValidateArchive(zip, out _, out _));
    }

    [Fact]
    public void ValidateArchive_RejectsSymlinkEntries()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var e = zip.CreateEntry("plugin/bin/64bit/plugin.dll");
            e.ExternalAttributes = 0xA000 << 16; // S_IFLNK
            using var s = e.Open();
            s.Write(new byte[8]);
        }
        ms.Position = 0;
        using var read = new ZipArchive(ms, ZipArchiveMode.Read);

        Assert.Equal("PLUGIN_ZIP_UNSAFE", PluginInstallCore.ValidateArchive(read, out _, out _));
    }

    [Fact]
    public void DllLayout_Requires64Bit()
    {
        Assert.Equal("", PluginInstallCore.ValidateDllLayout(new[] { "bin/64bit/plugin.dll" }));
        Assert.Equal("", PluginInstallCore.ValidateDllLayout(new[] { "obs-plugins/64bit/plugin.dll" }));
        Assert.Equal("PLUGIN_NO_DLL", PluginInstallCore.ValidateDllLayout(new[] { "readme.md" }));
        Assert.Equal("PLUGIN_NO_X64_DLL", PluginInstallCore.ValidateDllLayout(new[] { "bin/32bit/plugin.dll" }));
    }

    [Fact]
    public void PeCheck_AcceptsX64_AndManagedX86_ButNotNativeX86()
    {
        Assert.True(PluginInstallCore.IsX64OrManaged(BuildPe(0x8664, managed: false)));
        Assert.True(PluginInstallCore.IsX64OrManaged(BuildPe(0x014C, managed: true)));
        Assert.False(PluginInstallCore.IsX64OrManaged(BuildPe(0x014C, managed: false)));
        Assert.False(PluginInstallCore.IsX64OrManaged(Array.Empty<byte>()));
        Assert.False(PluginInstallCore.IsX64OrManaged(Encoding.ASCII.GetBytes("not a pe file")));
    }

    [Theory]
    [InlineData("exeldro/obs-move-transition", true)]
    [InlineData("https://github.com/exeldro/obs-move-transition", true)]
    [InlineData("https://github.com/exeldro/obs-move-transition.git", true)]
    [InlineData("exeldro", false)]
    [InlineData("a/b/c", false)]
    [InlineData("../evil/repo", false)]
    [InlineData("", false)]
    public void TryParseOwnerRepo_RejectsUnexpectedInput(string input, bool expected)
    {
        Assert.Equal(expected, PluginInstallCore.TryParseOwnerRepo(input, out var slug));
        if (expected) Assert.Contains('/', slug);
    }

    [Fact]
    public void SearchUrl_EscapesQueryAndPinsTopic()
    {
        var url = PluginInstallCore.BuildSearchUrl("audio & video");

        Assert.Contains("topic%3Aobs-plugin", url);
        Assert.Contains("audio%20%26%20video", url);
        Assert.Contains("sort=stars", url);
    }

    [Theory]
    [InlineData("github.com", true)]
    [InlineData("objects.githubusercontent.com", true)]
    [InlineData("github-releases.githubusercontent.com", true)]
    [InlineData("release-assets.githubusercontent.com", true)]
    [InlineData("evil.example.com", false)]
    [InlineData(null, false)]
    public void DownloadHost_IsWhitelisted(string? host, bool expected)
        => Assert.Equal(expected, PluginInstallCore.IsAllowedDownloadHost(host));

    [Fact]
    public void SafeSlug_And_InstallRoot_ArePredictable()
    {
        Assert.Equal("obs-move-transition", PluginInstallCore.SafeSlug("OBS Move Transition"));
        Assert.Equal("plugin", PluginInstallCore.SafeSlug("///"));

        var userRoot = PluginInstallCore.DefaultInstallRoot("user", @"C:\Users\a\AppData\Local", @"C:\ProgramData");
        Assert.Equal(Path.Combine(@"C:\Users\a\AppData\Local", "obs-studio", "plugins"), userRoot);

        var globalRoot = PluginInstallCore.DefaultInstallRoot("global", @"C:\Users\a\AppData\Local", @"C:\ProgramData");
        Assert.Equal(Path.Combine(@"C:\ProgramData", "obs-studio", "plugins"), globalRoot);
    }

    // ---------------------------------------------------------------- GitHub 响应解析

    [Fact]
    public void ParseSearch_ReadsReposAndLicense()
    {
        const string json = """
        { "items": [
          { "full_name": "exeldro/obs-move-transition", "description": "Move transition",
            "stargazers_count": 900, "license": { "spdx_id": "GPL-2.0" },
            "pushed_at": "2026-10-01T00:00:00Z", "html_url": "https://github.com/exeldro/obs-move-transition" }
        ] }
        """;

        var repos = GitHubSearchCore.ParseSearch(json, out var error);
        Assert.Equal("", error);
        var repo = Assert.Single(repos);
        Assert.Equal("exeldro/obs-move-transition", repo.FullName);
        Assert.Equal("GPL-2.0", repo.License);
        Assert.Equal(900, repo.Stars);
    }

    [Fact]
    public void ParseSearch_ReportsBadJson()
    {
        var bad = GitHubSearchCore.ParseSearch("{oops", out var badError);
        Assert.Equal("NET_BAD_RESPONSE", badError);
        Assert.Empty(bad);

        var empty = GitHubSearchCore.ParseSearch("", out var emptyError);
        Assert.Equal("NET_EMPTY_RESPONSE", emptyError);
        Assert.Empty(empty);
    }

    [Fact]
    public void ParseLatestRelease_ReadsVersionAndAssets()
    {
        const string json = """
        { "tag_name": "v1.2.3", "assets": [
            { "name": "plugin-1.2.3-windows-x64.zip", "browser_download_url": "https://github.com/x/y/releases/download/v1.2.3/p.zip",
              "size": 1234, "digest": "sha256:abcd" }
        ] }
        """;

        var (version, assets, error) = GitHubSearchCore.ParseLatestRelease(json);
        Assert.Equal("", error);
        Assert.Equal("1.2.3", version);
        var asset = Assert.Single(assets);
        Assert.Equal("sha256:abcd", asset.Digest);
        Assert.Equal(1234, asset.Size);
    }

    // ---------------------------------------------------------------- helpers

    private static ZipArchive BuildZip(IEnumerable<(string Name, int Bytes)> entries)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, size) in entries)
            {
                var e = zip.CreateEntry(name, CompressionLevel.Fastest);
                using var s = e.Open();
                s.Write(new byte[size]);
            }
        }
        ms.Position = 0;
        return new ZipArchive(ms, ZipArchiveMode.Read);
    }

    /// <summary>构造最小 PE 头：MZ + PE + machine +（可选）CLR 数据目录。</summary>
    private static byte[] BuildPe(ushort machine, bool managed)
    {
        var bytes = new byte[4096];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        const int pe = 0x80;
        BitConverter.GetBytes(pe).CopyTo(bytes, 0x3C);
        bytes[pe] = (byte)'P';
        bytes[pe + 1] = (byte)'E';
        BitConverter.GetBytes(machine).CopyTo(bytes, pe + 4);

        var optional = pe + 24;
        BitConverter.GetBytes((ushort)0x10B).CopyTo(bytes, optional); // PE32
        var clrDir = optional + 208;                                  // data directory #14
        if (managed)
        {
            BitConverter.GetBytes(0x2000).CopyTo(bytes, clrDir);
            BitConverter.GetBytes(0x48).CopyTo(bytes, clrDir + 4);
        }
        return bytes;
    }
}
