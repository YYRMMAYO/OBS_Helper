using System.IO;
using OBS_Helper.Wpf.Services.Dependencies;
using OBS_Helper.Wpf.Services.Interop;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>开源依赖中心（V3.4.1）的纯逻辑与一键配置回归测试。</summary>
public class DependencyCoreTests
{
    [Fact]
    public void ItemsPointToOfficialGitHub()
    {
        Assert.Equal(DependencyCore.Items.Length, DependencyCore.Items.Select(i => i.Id).Distinct().Count());
        foreach (var item in DependencyCore.Items)
        {
            Assert.True(DependencyCore.IsOfficialGitHubUrl(item.RepoUrl), item.Id + " repo");
            Assert.True(DependencyCore.IsOfficialGitHubUrl(item.ReleasesUrl), item.Id + " releases");
            Assert.EndsWith(".exe", item.Executable, StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(item.License), item.Id + " license");
            Assert.False(string.IsNullOrWhiteSpace(item.SidecarId), item.Id + " sidecar");
        }
    }

    [Theory]
    [InlineData("http://github.com/a/b")]
    [InlineData("https://example.com/a/b")]
    [InlineData("https://github.com.evil.example/a/b")]
    [InlineData("file:///C:/windows/system32/cmd.exe")]
    public void RejectsUnofficialDownloadUrl(string url)
    {
        Assert.False(DependencyCore.IsOfficialGitHubUrl(url));
    }

    [Fact]
    public void CandidateDirectoriesAreDeduplicatedAndSkipMissing()
    {
        var dirs = DependencyCore.CandidateDirectories(
            @"C:\Users\me", @"C:\Users\me\AppData\Local", @"C:\Program Files", "", @"C:\tools; C:\tools ;", new[] { @"C:\sidecar" });

        Assert.Contains(@"C:\sidecar", dirs);
        Assert.Contains(@"C:\Users\me\Downloads", dirs);
        Assert.Contains(@"C:\Program Files", dirs);
        Assert.Contains(@"C:\tools", dirs);
        Assert.Equal(dirs.Count, dirs.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain("", dirs);
    }

    [Fact]
    public void FindExecutableSearchesSubdirectories()
    {
        var root = TempDir();
        try
        {
            var nested = Path.Combine(root, "ffmpeg-7.0", "bin");
            Directory.CreateDirectory(nested);
            var exe = Path.Combine(nested, "ffmpeg.exe");
            File.WriteAllText(exe, "stub");

            Assert.Equal(exe, DependencyCore.FindExecutable("ffmpeg.exe", new[] { root }));
            Assert.Null(DependencyCore.FindExecutable("rclone.exe", new[] { root }));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void IsAcceptablePathRequiresMatchingFileName()
    {
        var root = TempDir();
        try
        {
            var spec = DependencyCore.Find("ffmpeg")!;
            var wrong = Path.Combine(root, "renamed.exe");
            File.WriteAllText(wrong, "stub");
            Assert.False(DependencyCore.IsAcceptablePath(wrong, spec));

            var right = Path.Combine(root, "ffmpeg.exe");
            File.WriteAllText(right, "stub");
            Assert.True(DependencyCore.IsAcceptablePath(right, spec));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ConfigurePersistsPathAndAppliesImmediately()
    {
        var root = TempDir();
        try
        {
            var exe = Path.Combine(root, "ffmpeg.exe");
            File.WriteAllText(exe, "stub");

            string? storedKey = null;
            string? storedValue = null;
            string? appliedId = null;
            string? appliedPath = null;
            var service = new DependencyService(
                new FakeSidecars(root),
                _ => null,
                (key, value) => { storedKey = key; storedValue = value; },
                (id, path) => { appliedId = id; appliedPath = path; },
                _ => Task.FromResult(true));

            var spec = DependencyCore.Find("ffmpeg")!;
            var (ok, code, path) = service.Configure(spec, exe);

            Assert.True(ok);
            Assert.Equal("", code);
            Assert.Equal(Path.GetFullPath(exe), path);
            Assert.Equal("dep.ffmpeg.path", storedKey);
            Assert.Equal(Path.GetFullPath(exe), storedValue);
            Assert.Equal("ffmpeg", appliedId);
            Assert.Equal(Path.GetFullPath(exe), appliedPath);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ConfigureCopiesMediamtxIntoSidecarDirectory()
    {
        var root = TempDir();
        try
        {
            var source = Path.Combine(root, "downloads");
            Directory.CreateDirectory(source);
            var exe = Path.Combine(source, "mediamtx.exe");
            File.WriteAllText(exe, "stub");

            var sidecars = new FakeSidecars(Path.Combine(root, "sidecars"));
            sidecars.Entries["mediamtx"] = new SidecarEntry
            {
                Id = "mediamtx",
                Version = "manual",
                Url = "https://github.com/bluenviron/mediamtx/releases",
                License = "MIT",
                Source = "https://github.com/bluenviron/mediamtx",
                Executable = "mediamtx.exe",
                Manual = true,
            };

            var service = new DependencyService(sidecars, _ => null, (_, _) => { }, (_, _) => { }, _ => Task.FromResult(true));
            var spec = DependencyCore.Find("mediamtx")!;
            var (ok, _, path) = service.Configure(spec, exe);

            Assert.True(ok);
            Assert.True(File.Exists(path));
            Assert.Equal(path, service.ResolvePath(spec));
            Assert.EndsWith(Path.Combine("mediamtx", "manual", "mediamtx.exe"), path, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task DownloadOpensOfficialReleasesWhenManifestIsManual()
    {
        var root = TempDir();
        try
        {
            var sidecars = new FakeSidecars(root);
            sidecars.Entries["ffmpeg"] = new SidecarEntry
            {
                Id = "ffmpeg",
                Version = "manual",
                Url = "https://github.com/BtbN/FFmpeg-Builds/releases",
                License = "LGPL-3.0-or-later",
                Source = "https://github.com/BtbN/FFmpeg-Builds",
                Executable = "ffmpeg.exe",
                Manual = true,
            };
            string? opened = null;
            var service = new DependencyService(sidecars, _ => null, (_, _) => { }, (_, _) => { },
                url => { opened = url; return Task.FromResult(true); });

            var (ok, code) = await service.DownloadAsync(DependencyCore.Find("ffmpeg")!);

            Assert.True(ok);
            Assert.Equal("dep.download.opened", code);
            Assert.Equal("https://github.com/BtbN/FFmpeg-Builds/releases", opened);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void DependencyStringsExistInBothLanguages()
    {
        var original = OBS_Helper.Wpf.Localization.Strings.Current;
        try
        {
            foreach (var language in new[] { OBS_Helper.Wpf.Localization.Strings.ZhHans, OBS_Helper.Wpf.Localization.Strings.EnUs })
            {
                OBS_Helper.Wpf.Localization.Strings.SetLanguage(language);
                Assert.False(string.IsNullOrWhiteSpace(OBS_Helper.Wpf.Localization.Strings.T("dep.title")));
                Assert.False(string.IsNullOrWhiteSpace(OBS_Helper.Wpf.Localization.Strings.T("dep.desc")));
                foreach (var item in DependencyCore.Items)
                {
                    Assert.NotEqual("dep.name." + item.Id, OBS_Helper.Wpf.Localization.Strings.T("dep.name." + item.Id));
                    Assert.NotEqual("dep.desc." + item.Id, OBS_Helper.Wpf.Localization.Strings.T("dep.desc." + item.Id));
                }
            }
        }
        finally
        {
            OBS_Helper.Wpf.Localization.Strings.SetLanguage(original);
        }
    }

    private sealed class FakeSidecars : IDependencySidecars
    {
        public FakeSidecars(string root) => Root = root;

        public string Root { get; }
        public Dictionary<string, SidecarEntry> Entries { get; } = new(StringComparer.OrdinalIgnoreCase);

        public SidecarEntry? Find(string id) => Entries.TryGetValue(id, out var entry) ? entry : null;

        public string? InstalledExecutable(SidecarEntry entry)
        {
            var dir = SidecarCore.InstallDirectory(Root, entry);
            if (dir is null) return null;
            var file = Path.Combine(dir, entry.Executable);
            return File.Exists(file) ? file : null;
        }

        public Task<(bool Ok, string Code, string Path)> InstallAsync(
            SidecarEntry entry, IProgress<(long Done, long Total)>? progress = null, CancellationToken ct = default)
            => Task.FromResult((false, "manifest.manual", ""));
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "obshelper-dep-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch (Exception) { }
    }
}
