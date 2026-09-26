using OBS_Helper.Wpf.Services.Shell;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// OBS 会话日志定位的回归测试（V2.9.0）。
///
/// 背景：OBS 的会话日志扩展名是 <c>.txt</c>（<c>logs\2026-08-27 19-41-42.txt</c>），
/// 而 V2.8 的实时日志尾随只扫 <c>*.log</c>，结果在真实 OBS 上永远找不到日志文件、
/// 整条实时预警链路静默失效。这里把「按什么扩展名找、按什么排序」钉死。
/// </summary>
public class ObsLogFileFinderTests : IDisposable
{
    private readonly string _dir;

    public ObsLogFileFinderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "obs_helper_logfinder_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 清理失败不影响断言 */ }
    }

    private void Write(string name, DateTime modifiedUtc, string content = "line")
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, modifiedUtc);
    }

    [Fact]
    public void FindsNewestTxtLog_ObsRealNaming()
    {
        // OBS 真实命名：带时间戳的 .txt
        Write("2026-08-26 12-51-12.txt", new DateTime(2026, 8, 26, 4, 51, 12, DateTimeKind.Utc));
        Write("2026-08-27 19-41-42.txt", new DateTime(2026, 8, 27, 11, 41, 42, DateTimeKind.Utc));

        var newest = ObsLogFileFinder.FindNewest(_dir, out var count);

        Assert.Equal(2, count);
        Assert.Equal("2026-08-27 19-41-42.txt", Path.GetFileName(newest));
    }

    [Fact]
    public void AlsoAcceptsDotLogFiles()
    {
        Write("session.log", new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc));
        Assert.Equal("session.log", Path.GetFileName(ObsLogFileFinder.FindNewest(_dir)));
    }

    [Fact]
    public void PicksNewestAcrossBothExtensions()
    {
        Write("older.log", new DateTime(2026, 8, 26, 0, 0, 0, DateTimeKind.Utc));
        Write("newer.txt", new DateTime(2026, 8, 27, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal("newer.txt", Path.GetFileName(ObsLogFileFinder.FindNewest(_dir)));
    }

    [Fact]
    public void IgnoresUnrelatedFiles()
    {
        Write("crash-20260806.txt.bak", new DateTime(2026, 8, 27, 23, 0, 0, DateTimeKind.Utc));
        Write("notes.md", new DateTime(2026, 8, 27, 23, 0, 0, DateTimeKind.Utc));
        Write("real.txt", new DateTime(2026, 8, 26, 0, 0, 0, DateTimeKind.Utc));

        var newest = ObsLogFileFinder.FindNewest(_dir, out var count);

        Assert.Equal(1, count);
        Assert.Equal("real.txt", Path.GetFileName(newest));
    }

    [Fact]
    public void EmptyDirectory_ReturnsNull()
    {
        Assert.Null(ObsLogFileFinder.FindNewest(_dir, out var count));
        Assert.Equal(0, count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankDirectory_ReturnsNull(string? dir)
        => Assert.Null(ObsLogFileFinder.FindNewest(dir));

    [Fact]
    public void MissingDirectory_ReturnsNull()
        => Assert.Null(ObsLogFileFinder.FindNewest(Path.Combine(_dir, "does-not-exist")));

    [Fact]
    public void ExtensionMatching_IsCaseInsensitive()
    {
        Write("SESSION.TXT", new DateTime(2026, 8, 27, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal("SESSION.TXT", Path.GetFileName(ObsLogFileFinder.FindNewest(_dir)));

        Assert.True(ObsLogFileFinder.IsObsLogFile(new FileInfo(Path.Combine(_dir, "SESSION.TXT"))));
        Assert.False(ObsLogFileFinder.IsObsLogFile(new FileInfo(Path.Combine(_dir, "notes.md"))));
    }
}
