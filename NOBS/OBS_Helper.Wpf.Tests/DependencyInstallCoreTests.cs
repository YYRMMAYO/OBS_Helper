using System.IO.Compression;
using OBS_Helper.Wpf.Models.Plugins;
using OBS_Helper.Wpf.Services.Dependencies;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// X1031Y2：开源依赖「一键配置」的纯逻辑。
///
/// 资产选择决定用户最终装到的是不是 Windows x64 包；解压边界决定恶意 zip 能不能写出目标目录。
/// 这两类错误都不会在编译期暴露，只能靠逐条断言。
/// </summary>
public class DependencyInstallCoreTests
{
    private static DependencySpec Spec(string id) => DependencyCore.Find(id)!;

    private static PluginReleaseAsset Asset(string name, long size = 1024)
        => new() { Name = name, Url = "https://github.com/example/repo/releases/download/v1/" + name, Size = size };

    [Fact]
    public void SelectAsset_FFmpeg_PrefersWin64LgplStatic()
    {
        var assets = new[]
        {
            Asset("ffmpeg-master-latest-win64-gpl.zip", 200),
            Asset("ffmpeg-master-latest-win64-lgpl-shared.zip", 90),
            Asset("ffmpeg-master-latest-win64-lgpl.zip", 170),
            Asset("ffmpeg-master-latest-winarm64-lgpl.zip", 120),
        };

        var picked = DependencyInstallCore.SelectAsset(Spec("ffmpeg"), assets);

        Assert.NotNull(picked);
        Assert.Equal("ffmpeg-master-latest-win64-lgpl.zip", picked!.Name);
    }

    [Fact]
    public void SelectAsset_Rclone_PicksWindowsAmd64()
    {
        var assets = new[]
        {
            Asset("rclone-v1.75.2-linux-amd64.zip"),
            Asset("rclone-v1.75.2-windows-arm64.zip"),
            Asset("rclone-v1.75.2-windows-amd64.zip"),
        };

        var picked = DependencyInstallCore.SelectAsset(Spec("rclone"), assets);

        Assert.NotNull(picked);
        Assert.Equal("rclone-v1.75.2-windows-amd64.zip", picked!.Name);
    }

    [Fact]
    public void SelectAsset_Whisper_NoWindowsBinary_ReturnsNull()
    {
        var assets = new[]
        {
            Asset("whisper-source.zip"),
            Asset("whisper-linux-amd64.zip"),
        };

        Assert.Null(DependencyInstallCore.SelectAsset(Spec("whisper"), assets));
    }

    [Fact]
    public void SelectAsset_Mediamtx_PicksWindowsAmd64()
    {
        var assets = new[]
        {
            Asset("mediamtx_v1.21.3_linux_amd64.zip"),
            Asset("mediamtx_v1.21.3_windows_amd64.zip"),
        };

        var picked = DependencyInstallCore.SelectAsset(Spec("mediamtx"), assets);

        Assert.NotNull(picked);
        Assert.Equal("mediamtx_v1.21.3_windows_amd64.zip", picked!.Name);
    }

    [Fact]
    public void NormalizeDigest_AcceptsOnlySha256()
    {
        var hex = new string('a', 64);
        Assert.Equal(hex, DependencyInstallCore.NormalizeDigest("sha256:" + hex.ToUpperInvariant()));
        Assert.Equal("", DependencyInstallCore.NormalizeDigest("sha256:abc"));
        Assert.Equal("", DependencyInstallCore.NormalizeDigest("md5:" + hex));
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("C:/Windows/evil.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("dir/../../evil.txt")]
    public void SafeEntryPath_RejectsTraversal(string entry)
        => Assert.Null(DependencyInstallCore.SafeEntryPath(entry));

    [Fact]
    public void SafeEntryPath_AllowsNormalNestedPath()
        => Assert.Equal("bin/ffmpeg.exe", DependencyInstallCore.SafeEntryPath("bin\\ffmpeg.exe"));

    [Fact]
    public void ExtractZip_RejectsZipSlipAndWritesNothingOutside()
    {
        var root = Path.Combine(Path.GetTempPath(), "obs-helper-dep-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var zipPath = Path.Combine(root, "evil.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("../escaped.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("bad");
        }

        var destination = Path.Combine(root, "out");
        Directory.CreateDirectory(destination);
        var error = DependencyInstallCore.ExtractZip(zipPath, destination);

        Assert.Equal("dep.error.extract", error);
        Assert.False(File.Exists(Path.Combine(root, "escaped.txt")));

        try { Directory.Delete(root, recursive: true); } catch (Exception) { }
    }

    [Fact]
    public void FindExecutable_FindsNestedBinary()
    {
        var root = Path.Combine(Path.GetTempPath(), "obs-helper-dep-tests", Guid.NewGuid().ToString("N"));
        var bin = Path.Combine(root, "ffmpeg-1.0", "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "ffmpeg.exe"), "x");

        Assert.Equal(Path.Combine(bin, "ffmpeg.exe"),
            DependencyInstallCore.FindExecutable(root, "ffmpeg.exe"));

        try { Directory.Delete(root, recursive: true); } catch (Exception) { }
    }

    [Fact]
    public void InstallDirectory_RejectsUnsafeSegments()
    {
        Assert.Null(DependencyInstallCore.InstallDirectory(@"C:\root", "..", "1.0"));
        Assert.NotNull(DependencyInstallCore.InstallDirectory(@"C:\root", "ffmpeg", "v1.2.3"));
    }
}
