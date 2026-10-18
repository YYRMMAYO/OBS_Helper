using System.IO;
using OBS_Helper.Wpf.Services.Recording;

namespace OBS_Helper.Wpf.Tests;

/// <summary>录像命名模板（V3.1.0 / §12.5.1）：变量、净化、冲突与非法字符。</summary>
public class RecordingNamingCoreTests
{
    private static RecordingNamingContext Context(string scene = "游戏", string extension = ".mkv")
        => new(new DateTime(2026, 10, 18, 20, 30, 15), scene, "默认", 1920, 1080, 60, extension);

    [Fact]
    public void ApplyReplacesAllKnownVariables()
    {
        var name = RecordingNamingCore.Apply(
            "{date}_{time}_{datetime}_{scene}_{profile}_{resolution}_{fps}",
            Context());

        Assert.Equal("2026-10-18_20-30-15_2026-10-18_20-30-15_游戏_默认_1920x1080_60.mkv", name);
    }

    [Fact]
    public void ApplyAppendsOriginalExtensionWhenMissing()
    {
        Assert.EndsWith(".mkv", RecordingNamingCore.Apply("{date}", Context()));
        Assert.EndsWith(".mkv", RecordingNamingCore.Apply("{date}.mkv", Context()));
    }

    [Fact]
    public void ApplySupportsExtVariable()
    {
        Assert.Equal("2026-10-18.mkv", RecordingNamingCore.Apply("{date}.{ext}", Context()));
    }

    [Fact]
    public void EmptyTemplateMeansNoRename()
    {
        Assert.Equal("", RecordingNamingCore.Apply("", Context()));
        Assert.Equal("", RecordingNamingCore.Apply("   ", Context()));
    }

    [Fact]
    public void SanitizeRemovesIllegalCharacters()
    {
        var name = RecordingNamingCore.Sanitize("a/b\\c:d*e?f\"g<h>i|j");
        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain('\\', name);
        Assert.DoesNotContain(':', name);
        Assert.DoesNotContain('*', name);
        Assert.DoesNotContain('?', name);
        Assert.DoesNotContain('"', name);
        Assert.DoesNotContain('<', name);
        Assert.DoesNotContain('>', name);
        Assert.DoesNotContain('|', name);
    }

    [Fact]
    public void SanitizeTrimsDotsAndSpacesAndCapsLength()
    {
        Assert.Equal("abc", RecordingNamingCore.Sanitize("  .abc.  "));
        var longName = new string('x', 300) + ".mkv";
        Assert.True(RecordingNamingCore.Sanitize(longName).Length <= RecordingNamingCore.MaxNameLength);
    }

    [Fact]
    public void SanitizeDoesNotEscapeDirectory()
    {
        var name = RecordingNamingCore.Sanitize("..\\..\\evil");
        Assert.DoesNotContain("..", name);
        Assert.DoesNotContain("\\", name);
        Assert.DoesNotContain("/", name);
    }

    [Fact]
    public void UnknownVariablesAreReported()
    {
        Assert.Empty(RecordingNamingCore.UnknownVariables("{date}_{time}"));
        var unknown = RecordingNamingCore.UnknownVariables("{date}_{game}");
        Assert.Single(unknown);
        Assert.Equal("{game}", unknown[0]);
    }

    [Fact]
    public void HasKnownVariableRecognizesTemplates()
    {
        Assert.True(RecordingNamingCore.HasKnownVariable("{date}"));
        Assert.True(RecordingNamingCore.HasKnownVariable("{SCENE}"));
        Assert.False(RecordingNamingCore.HasKnownVariable("plain name"));
        Assert.False(RecordingNamingCore.HasKnownVariable(""));
    }

    [Fact]
    public void PreviewIsStable()
    {
        Assert.Equal("2026-10-18_20-30-15_游戏.mkv",
            RecordingNamingCore.Preview("{date}_{time}_{scene}"));
        Assert.Equal("", RecordingNamingCore.Preview(""));
    }

    [Fact]
    public void EnsureUniquePathAvoidsCollisions()
    {
        var dir = Path.Combine(Path.GetTempPath(), "obshelper-naming-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var first = RecordingNamingCore.EnsureUniquePath(dir, "a.mkv");
            Assert.Equal(Path.Combine(dir, "a.mkv"), first);

            File.WriteAllText(first, "x");
            var second = RecordingNamingCore.EnsureUniquePath(dir, "a.mkv");
            Assert.Equal(Path.Combine(dir, "a_2.mkv"), second);

            File.WriteAllText(second, "x");
            var third = RecordingNamingCore.EnsureUniquePath(dir, "a.mkv");
            Assert.Equal(Path.Combine(dir, "a_3.mkv"), third);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (Exception) { /* 清理失败无妨 */ }
        }
    }
}
