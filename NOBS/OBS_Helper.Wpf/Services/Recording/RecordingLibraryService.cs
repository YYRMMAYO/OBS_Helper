using System.IO;

namespace OBS_Helper.Wpf.Services.Recording;

/// <summary>一条本地录像文件索引（X1031Y3 / Y4 并入：历史录像索引）。</summary>
public sealed record RecordingFileInfo(string Path, string Name, long SizeBytes, DateTimeOffset ModifiedUtc);

/// <summary>
/// 历史录像文件索引：扫描录像目录的常见视频文件，按修改时间倒序返回。
///
/// 边界：只扫一层目录、只认常见扩展名、最多 200 条；不读取视频内容、不上传。
/// 字幕/转写内容的检索仍由 <c>RecordingSearchCore</c> 负责。
/// </summary>
public static class RecordingLibraryService
{
    private static readonly string[] Extensions = { ".mp4", ".mkv", ".mov", ".flv", ".ts" };

    /// <summary>录像目录：优先使用最近一次录制文件所在目录，否则用系统「视频」目录。</summary>
    public static string? DefaultRoot(string? lastRecordFile)
    {
        if (!string.IsNullOrWhiteSpace(lastRecordFile))
        {
            try
            {
                var dir = Path.GetDirectoryName(lastRecordFile);
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir)) return dir;
            }
            catch (Exception) { /* 路径异常时回退 */ }
        }
        var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        return Directory.Exists(videos) ? videos : null;
    }

    public static IReadOnlyList<RecordingFileInfo> Scan(string? root = null, int max = 200)
    {
        var dir = string.IsNullOrWhiteSpace(root) ? DefaultRoot(null) : root;
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return Array.Empty<RecordingFileInfo>();
        try
        {
            return Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly)
                .Where(path => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(Math.Clamp(max, 1, 1000))
                .Select(file => new RecordingFileInfo(
                    file.FullName, file.Name, file.Length, file.LastWriteTimeUtc))
                .ToList();
        }
        catch (Exception)
        {
            return Array.Empty<RecordingFileInfo>();
        }
    }
}
