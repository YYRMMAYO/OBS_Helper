using System.IO;
using OBS_Helper.Wpf.Services.Recording;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>X1031Y3 / Y4 并入：历史录像文件索引只扫常见视频、按时间倒序、不读内容。</summary>
public class RecordingLibraryServiceTests
{
    [Fact]
    public void Scan_FiltersSupportedFilesAndSortsByModifiedTime()
    {
        var root = Path.Combine(Path.GetTempPath(), "obshelper-recording-library", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var old = Path.Combine(root, "old.mp4");
            File.WriteAllText(old, "x");
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddHours(-2));

            var newer = Path.Combine(root, "new.mkv");
            File.WriteAllText(newer, "x");
            File.SetLastWriteTimeUtc(newer, DateTime.UtcNow);

            File.WriteAllText(Path.Combine(root, "notes.txt"), "x");
            Directory.CreateDirectory(Path.Combine(root, "sub"));
            File.WriteAllText(Path.Combine(root, "sub", "nested.mp4"), "x");

            var files = RecordingLibraryService.Scan(root);

            Assert.Equal(2, files.Count);
            Assert.Equal("new.mkv", files[0].Name);
            Assert.Equal("old.mp4", files[1].Name);
            Assert.Equal(root, RecordingLibraryService.DefaultRoot(old));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* 忽略 */ }
        }
    }
}
