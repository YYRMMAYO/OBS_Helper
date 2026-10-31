using OBS_Helper.Wpf.Models.Plugins;
using OBS_Helper.Wpf.Services.Plugins;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// V3.5.0：已安装插件列表必须同时覆盖「本应用管理」与「本机检测」。
/// 旧实现只读 installed.json，用户用 OBS 自己安装的插件完全不可见。
/// </summary>
public class PluginManagedListCoreTests
{
    private static PluginInstallEntry Managed(string id, string dir, string version = "1.2.3") => new()
    {
        Id = id,
        Name = id,
        Version = version,
        InstallDirectory = dir,
        AssetName = id + ".zip",
        Sha256 = "abcdef0123456789",
        Curated = true,
    };

    private static InstalledPluginFile Local(string path, string version = "2.0") => new()
    {
        FileName = Path.GetFileName(path),
        Stem = Path.GetFileNameWithoutExtension(path),
        ProductName = "本机插件",
        FileVersion = version,
        FullPath = path,
    };

    [Fact]
    public void ManagedAndLocalAreBothListed()
    {
        var managed = new[] { Managed("a", @"C:\obs\obs-plugins\64bit\a") };
        var local = new[] { Local(@"C:\obs\obs-plugins\64bit\other.dll") };

        var rows = PluginManagedListCore.Merge(managed, local, _ => true);

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.Id == "a" && r.Kind == ManagedPluginKind.Managed);
        Assert.Contains(rows, r => r.FileName == "other.dll" && r.Kind == ManagedPluginKind.Local);
    }

    [Fact]
    public void LocalDllInsideManagedDirectoryIsNotListedTwice()
    {
        var managed = new[] { Managed("a", @"C:\obs\obs-plugins\64bit\a") };
        var local = new[] { Local(@"C:\obs\obs-plugins\64bit\a\a.dll") };

        var rows = PluginManagedListCore.Merge(managed, local, _ => true);

        Assert.Single(rows);
        Assert.Equal(ManagedPluginKind.Managed, rows[0].Kind);
    }

    [Fact]
    public void MissingManagedDirectoryIsMarkedAndCannotRollback()
    {
        var managed = new[] { Managed("a", @"C:\gone\a") };
        var rows = PluginManagedListCore.Merge(managed, Array.Empty<InstalledPluginFile>(),
            _ => false, _ => 2);

        var row = Assert.Single(rows);
        Assert.True(row.DirectoryMissing);
        Assert.False(row.CanRollback);
    }

    [Fact]
    public void ManagedPrefersRollbackOnlyWhenBackupsExist()
    {
        var managed = new[] { Managed("a", @"C:\obs\a") };
        var rows = PluginManagedListCore.Merge(managed, Array.Empty<InstalledPluginFile>(),
            _ => true, _ => 1);
        Assert.True(rows[0].CanRollback);
    }

    [Fact]
    public void NormalizeRemovesTrailingSeparators()
    {
        var normalized = PluginManagedListCore.Normalize(@"C:\obs\a\");
        Assert.False(normalized.EndsWith(Path.DirectorySeparatorChar));
        Assert.True(PluginManagedListCore.IsUnder(normalized + Path.DirectorySeparatorChar + "x.dll", normalized));
    }
}

/// <summary>GitHub 连通性探测的异常分类。</summary>
public class GitHubReachabilityTests
{
    [Fact]
    public void SuccessIsReachable()
        => Assert.Equal(GitHubReachabilityStatus.Reachable, GitHubReachability.Classify(null, true));

    [Fact]
    public void TimeoutIsTimeout()
    {
        Assert.Equal(GitHubReachabilityStatus.Timeout,
            GitHubReachability.Classify(new TaskCanceledException(), false));
        Assert.Equal(GitHubReachabilityStatus.Timeout,
            GitHubReachability.Classify(new TimeoutException(), false));
    }

    [Fact]
    public void OtherErrorsAreFailed()
        => Assert.Equal(GitHubReachabilityStatus.Failed,
            GitHubReachability.Classify(new HttpRequestException("dns"), false));

    [Fact]
    public void ProbeUrlIsHttps()
        => Assert.StartsWith("https://api.github.com/", GitHubReachability.ProbeUrl);
}
