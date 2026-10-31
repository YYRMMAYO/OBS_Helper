using System.IO;

namespace OBS_Helper.Wpf.Services.Dependencies;

/// <summary>
/// 一个开源依赖（V3.4.1）：桌面端按需调用的外部组件。
///
/// 只描述“官方来源 / 许可证 / 可执行文件名 / 配置落点”，不保存任何二进制。
/// 下载一律走官方 GitHub Releases；配置只接受用户本机已存在的 exe。
/// </summary>
public sealed record DependencySpec
{
    public required string Id { get; init; }
    /// <summary>可执行文件名（含扩展名），用于识别与校验。</summary>
    public required string Executable { get; init; }
    /// <summary>官方 GitHub 仓库（一键打开仓库用）。</summary>
    public required string RepoUrl { get; init; }
    /// <summary>官方 GitHub Releases（一键下载用）。</summary>
    public required string ReleasesUrl { get; init; }
    public required string License { get; init; }
    /// <summary>对应的 sidecar id；用于检测已安装位置与“复制进 sidecar 目录”。</summary>
    public string? SidecarId { get; init; }
    /// <summary>路径持久化键；为空表示该依赖只能放在 sidecar 目录（如 mediamtx）。</summary>
    public string? StoreKey { get; init; }
}

/// <summary>
/// 依赖清单与「本机查找」纯逻辑（V3.4.1）。
///
/// 边界：
/// <list type="bullet">
///   <item>下载页只允许 https + GitHub 域名，杜绝清单被改成任意地址；</item>
///   <item>“一键配置”只接受文件名匹配的可执行文件，不执行、不改系统 PATH；</item>
///   <item>查找范围固定为用户目录 / PATH / Program Files / sidecar 目录，不遍历全盘。</item>
/// </list>
/// 纯函数，便于单测。
/// </summary>
public static class DependencyCore
{
    public static readonly DependencySpec[] Items =
    {
        new()
        {
            Id = "ffmpeg",
            Executable = "ffmpeg.exe",
            RepoUrl = "https://github.com/BtbN/FFmpeg-Builds",
            ReleasesUrl = "https://github.com/BtbN/FFmpeg-Builds/releases",
            License = "LGPL-3.0-or-later",
            SidecarId = "ffmpeg",
            StoreKey = "dep.ffmpeg.path",
        },
        new()
        {
            Id = "rclone",
            Executable = "rclone.exe",
            RepoUrl = "https://github.com/rclone/rclone",
            ReleasesUrl = "https://github.com/rclone/rclone/releases",
            License = "MIT",
            SidecarId = "rclone",
            StoreKey = "dep.rclone.path",
        },
        new()
        {
            Id = "whisper",
            Executable = "whisper-cli.exe",
            RepoUrl = "https://github.com/ggml-org/whisper.cpp",
            ReleasesUrl = "https://github.com/ggml-org/whisper.cpp/releases",
            License = "MIT",
            SidecarId = "whisper",
            StoreKey = "dep.whisper.path",
        },
        new()
        {
            Id = "mediamtx",
            Executable = "mediamtx.exe",
            RepoUrl = "https://github.com/bluenviron/mediamtx",
            ReleasesUrl = "https://github.com/bluenviron/mediamtx/releases",
            License = "MIT",
            SidecarId = "mediamtx",
            StoreKey = null,
        },
    };

    public static DependencySpec? Find(string? id)
        => Items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>一键下载前校验：必须是 https 且落在 github.com（含 Releases 页面）。</summary>
    public static bool IsOfficialGitHubUrl(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
           && uri.Scheme == Uri.UriSchemeHttps
           && (string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
               || string.Equals(uri.Host, "www.github.com", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 一键配置的候选目录（按优先级）。<paramref name="extra"/> 通常放 sidecar 安装目录。
    /// 缺失的环境变量会被忽略，重复目录会去重。
    /// </summary>
    public static List<string> CandidateDirectories(
        string? userProfile,
        string? localAppData,
        string? programFiles,
        string? programFilesX86,
        string? pathVariable,
        IEnumerable<string>? extra = null)
    {
        var dirs = new List<string>();
        void Add(string? dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            var trimmed = dir.Trim().Trim('"');
            if (trimmed.Length == 0 || trimmed.Contains('\0')) return;
            if (!dirs.Contains(trimmed, StringComparer.OrdinalIgnoreCase)) dirs.Add(trimmed);
        }

        if (extra is not null) foreach (var dir in extra) Add(dir);

        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            Add(Path.Combine(userProfile, "Downloads"));
            Add(Path.Combine(userProfile, "Desktop"));
            Add(Path.Combine(userProfile, "Documents"));
        }
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            Add(Path.Combine(localAppData, "Programs"));
            Add(Path.Combine(localAppData, "Microsoft", "WinGet", "Links"));
        }
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            Add(programFiles);
            Add(Path.Combine(programFiles, "ffmpeg", "bin"));
            Add(Path.Combine(programFiles, "rclone"));
        }
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            Add(programFilesX86);
        }
        if (!string.IsNullOrWhiteSpace(pathVariable))
        {
            foreach (var part in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                Add(part);
        }
        return dirs;
    }

    /// <summary>
    /// 在候选目录（以及其下最多 <paramref name="maxDepth"/> 层子目录）里查找可执行文件。
    /// 只做有界遍历：每层最多看 <paramref name="maxEntries"/> 个子目录，避免在下载目录里卡住。
    /// </summary>
    public static string? FindExecutable(
        string executable, IEnumerable<string> directories, int maxDepth = 3, int maxEntries = 400)
    {
        if (string.IsNullOrWhiteSpace(executable)) return null;
        var seen = 0;
        foreach (var dir in directories)
        {
            if (seen > maxEntries) return null;
            var hit = Search(executable, dir, maxDepth, ref seen, maxEntries);
            if (hit is not null) return hit;
        }
        return null;
    }

    private static string? Search(string executable, string dir, int depth, ref int seen, int maxEntries)
    {
        if (seen++ > maxEntries) return null;
        try
        {
            if (!Directory.Exists(dir)) return null;
            var direct = Path.Combine(dir, executable);
            if (File.Exists(direct)) return direct;
            if (depth <= 0) return null;
            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                if (seen > maxEntries) return null;
                var hit = Search(executable, sub, depth - 1, ref seen, maxEntries);
                if (hit is not null) return hit;
            }
        }
        catch (Exception)
        {
            // 权限不足 / 路径消失都不应中断整体查找。
        }
        return null;
    }

    /// <summary>配置前校验：文件存在且文件名（不区分大小写）与依赖声明一致。</summary>
    public static bool IsAcceptablePath(string? path, DependencySpec spec)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (path.Contains('\0')) return false;
        var name = Path.GetFileName(path.Trim());
        return string.Equals(name, spec.Executable, StringComparison.OrdinalIgnoreCase)
               && File.Exists(path);
    }
}
