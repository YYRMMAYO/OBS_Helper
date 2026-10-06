using System.Diagnostics;
using System.IO;

namespace OBS_Helper.Wpf.Services.Tools;

/// <summary>
/// 磁盘写入基准服务（V3.0 / D4）。
///
/// 原先这段实测逻辑放在工具箱**页面**里（<c>ToolboxPage.MeasureSequentialWrite</c>），
/// 于是「开播前体检」要用它就只能反向依赖一个 View —— 分层不对，也测不了。
/// 这里把它搬到服务层，页面与体检共用同一份实现（口径不会漂移）。
///
/// 只读性：向目标目录写一个临时文件并**立即删除**，不触碰任何用户数据。
/// </summary>
public static class DiskBenchmarkService
{
    /// <summary>向目录顺序写入临时文件并返回 MB/s，结束后立即删除。任何失败抛出由调用方降级。</summary>
    public static double MeasureSequentialWrite(string dir)
    {
        var file = Path.Combine(dir, $"obs_helper_disk_test_{Guid.NewGuid():N}.tmp");
        try
        {
            var buffer = new byte[4 * 1024 * 1024];
            var totalBytes = Math.Min(DiskBenchmarkCore.DefaultTestBytes,
                Math.Max(64L * 1024 * 1024, FreeBytesOf(dir) / 4)); // 盘面紧张时至少写 64MB

            using (var fs = new FileStream(file, FileMode.Create,
                       FileAccess.Write, FileShare.None, buffer.Length,
                       FileOptions.WriteThrough))
            {
                var sw = Stopwatch.StartNew();
                for (long written = 0; written < totalBytes; written += buffer.Length)
                {
                    fs.Write(buffer, 0, buffer.Length);
                }
                fs.Flush();
                sw.Stop();

                var mbps = totalBytes / 1024.0 / 1024.0 / sw.Elapsed.TotalSeconds;
                return mbps;
            }
        }
        finally
        {
            try { if (File.Exists(file)) File.Delete(file); } catch (Exception) { /* 清理失败无妨（临时文件） */ }
        }
    }

    /// <summary>目标目录所在盘的剩余字节数；读不到返回 0。</summary>
    public static long FreeBytesOf(string dir)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(dir));
            return string.IsNullOrEmpty(root) ? 0 : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception)
        {
            return 0;
        }
    }
}
