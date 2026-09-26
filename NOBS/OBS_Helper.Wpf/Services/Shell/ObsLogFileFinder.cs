using System.IO;

namespace OBS_Helper.Wpf.Services.Shell;

/// <summary>
/// OBS 会话日志文件定位（纯逻辑，无 WPF / WinForms 依赖，可被单元测试工程直接链接编译）。
///
/// 之所以单独抽出来：OBS 的会话日志扩展名是 <c>.txt</c>（<c>logs\2026-08-27 19-41-42.txt</c>），
/// 不是 <c>.log</c>。V2.8 的实时日志尾随只扫了 <c>*.log</c>，导致它在任何真实 OBS 安装上都
/// 找不到日志文件、整条实时预警链路静默失效。把这个「按什么找」的规则收敛到一处并加测试，
/// 避免同一个坑再踩一次（同目录的 <c>HostBridge.ListObsLogsAsync</c> 一直是两者都认的）。
/// </summary>
public static class ObsLogFileFinder
{
    /// <summary>
    /// 认作 OBS 日志的扩展名。
    /// <c>.txt</c> 是 OBS 实际的会话日志命名；<c>.log</c> 作为兼容保留
    /// （部分用户会手动改名，或使用第三方日志工具）。
    /// </summary>
    public static readonly string[] SupportedExtensions = { ".txt", ".log" };

    /// <summary>按修改时间取最新的一个 OBS 会话日志；目录不存在 / 无匹配返回 null。</summary>
    public static string? FindNewest(string? directory)
        => FindNewest(directory, out _);

    /// <summary>
    /// 按修改时间取最新的一个 OBS 会话日志，并输出该目录下的候选总数。
    /// 枚举失败（目录不存在 / 权限不足）时返回 null 且 <paramref name="candidateCount"/> 为 0。
    /// </summary>
    public static string? FindNewest(string? directory, out int candidateCount)
    {
        candidateCount = 0;
        if (string.IsNullOrWhiteSpace(directory)) return null;

        try
        {
            if (!Directory.Exists(directory)) return null;

            var files = new DirectoryInfo(directory)
                .GetFiles()
                .Where(IsObsLogFile)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

            candidateCount = files.Count;
            return files.Count > 0 ? files[0].FullName : null;
        }
        catch (Exception)
        {
            // 目录不存在 / 权限不足 / 磁盘异常：一律静默，调用方按「没找到」处理
            return null;
        }
    }

    /// <summary>是否认作 OBS 日志文件：只看扩展名，大小写不敏感。</summary>
    public static bool IsObsLogFile(FileInfo file)
    {
        if (file is null) return false;
        var ext = file.Extension;
        foreach (var known in SupportedExtensions)
        {
            if (ext.Equals(known, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
