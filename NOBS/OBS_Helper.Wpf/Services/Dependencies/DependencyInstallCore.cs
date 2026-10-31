using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using OBS_Helper.Wpf.Models.Plugins;

namespace OBS_Helper.Wpf.Services.Dependencies;

/// <summary>
/// 开源依赖「一键配置」的纯逻辑（X1031Y2）：挑资产、校验摘要、安全解压、定位可执行文件。
///
/// 背景：V3.4.1 的依赖中心只有 mediamtx 能在应用内安装，FFmpeg / rclone / whisper
/// 的「一键下载」只是打开浏览器。用户反馈「点完还是要去 GitHub 自己找包」——
/// 本类把「从最新 Release 里挑对 Windows x64 包」的判断固定下来，服务层只负责网络与落盘。
///
/// 安全边界：
/// <list type="bullet">
///   <item>只接受 HTTPS + GitHub 官方资产域；</item>
///   <item>体积 / 条目数 / 总解压量都有上限，拒绝 zip slip 与符号链接；</item>
///   <item>GitHub 提供 <c>sha256:</c> 摘要时必须匹配；没有摘要也只在 HTTPS 官方域下载。</item>
/// </list>
/// </summary>
public static class DependencyInstallCore
{
    public const long MaxAssetBytes = 512L * 1024 * 1024;
    public const long MaxExtractedBytes = 2L * 1024 * 1024 * 1024;
    public const int MaxEntries = 20_000;

    private static readonly Regex Sha256Pattern = new("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant);

    /// <summary>允许下载的 GitHub 官方域名（含 Release 资产重定向域）。</summary>
    public static readonly string[] AllowedHosts =
    {
        "github.com",
        "www.github.com",
        "api.github.com",
        "objects.githubusercontent.com",
        "github-releases.githubusercontent.com",
        "codeload.github.com",
        "raw.githubusercontent.com",
    };

    /// <summary>
    /// 从最新 Release 资产里挑出适合本机 Windows x64 的 zip。
    /// 找不到返回 null（界面提示「上游没有成品包，去下载页手动安装」）。
    /// </summary>
    public static PluginReleaseAsset? SelectAsset(DependencySpec spec, IEnumerable<PluginReleaseAsset>? assets)
    {
        if (spec is null || assets is null) return null;

        var zips = assets
            .Where(a => !string.IsNullOrWhiteSpace(a.Name) && !string.IsNullOrWhiteSpace(a.Url))
            .Where(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .Where(a => !a.Name.Contains("source code", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (zips.Count == 0) return null;

        var id = spec.Id.ToLowerInvariant();
        var scored = zips
            .Select(a => (Asset: a, Score: Score(id, a.Name.ToLowerInvariant())))
            .Where(x => x.Score > int.MinValue)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Asset.Size)
            .ToList();

        if (scored.Count > 0) return scored[0].Asset;

        // 已知依赖的规则全部拒绝：说明上游没有符合要求的 Windows 包，不能拿 Linux 包凑数。
        if (id is "ffmpeg" or "rclone" or "whisper" or "mediamtx") return null;

        // 未知依赖：只有一个 zip 就用它；多个则优先 Windows / x64 命名
        if (zips.Count == 1) return zips[0];
        return zips
            .OrderByDescending(a =>
            {
                var n = a.Name.ToLowerInvariant();
                var s = 0;
                if (n.Contains("windows") || n.Contains("win64") || n.Contains("win-")) s += 3;
                if (n.Contains("x64") || n.Contains("amd64") || n.Contains("64bit")) s += 2;
                return s;
            })
            .ThenByDescending(a => a.Size)
            .FirstOrDefault();
    }

    private static int Score(string id, string name)
    {
        // 明确排除的包：源码、调试符号、非 Windows 平台
        if (name.Contains("source code") || name.EndsWith(".tar.gz") || name.EndsWith(".7z")) return int.MinValue;

        static bool Has(string n, params string[] parts) => parts.Any(p => n.Contains(p, StringComparison.Ordinal));

        switch (id)
        {
            case "ffmpeg":
            {
                // 只用 LGPL 构建；优先静态版（shared 需要额外 DLL，解压后也能用但更脆）
                if (!Has(name, "lgpl")) return int.MinValue;
                if (!Has(name, "win64", "windows", "win-")) return int.MinValue;
                var score = 10;
                if (name.Contains("win64")) score += 4;
                if (name.Contains("shared")) score -= 2;
                if (name.Contains("master")) score += 1;
                return score;
            }

            case "rclone":
            {
                if (!Has(name, "windows")) return int.MinValue;
                if (Has(name, "arm", "386", "osx", "linux", "freebsd", "openbsd", "netbsd", "solaris", "plan9", "aix"))
                    return int.MinValue;
                if (!Has(name, "amd64", "x64", "64bit")) return int.MinValue;
                return 20;
            }

            case "whisper":
            {
                if (!Has(name, "win", "windows")) return int.MinValue;
                if (Has(name, "arm", "x86", "32bit")) return int.MinValue;
                if (!Has(name, "x64", "amd64", "64bit", "bin")) return int.MinValue;
                return 15;
            }

            case "mediamtx":
            {
                if (!Has(name, "windows")) return int.MinValue;
                if (!Has(name, "amd64", "x64", "64bit")) return int.MinValue;
                return 20;
            }

            default:
                return 0;
        }
    }

    /// <summary>资产地址必须是 HTTPS + GitHub 官方域。</summary>
    public static bool IsAllowedAssetUrl(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
           && uri.Scheme == Uri.UriSchemeHttps
           && AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);

    /// <summary>把 GitHub 的 <c>sha256:...</c> 归一化成 64 位十六进制；无效返回空串。</summary>
    public static string NormalizeDigest(string? digest)
    {
        var s = (digest ?? "").Trim();
        if (s.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) s = s["sha256:".Length..];
        return Sha256Pattern.IsMatch(s) ? s.ToLowerInvariant() : "";
    }

    /// <summary>安装目录：<c>&lt;root&gt;\&lt;id&gt;\&lt;tag&gt;</c>；id / tag 不合法返回 null。</summary>
    public static string? InstallDirectory(string root, string id, string tag)
    {
        var safeId = SafeSegment(id);
        var safeTag = SafeSegment(tag);
        if (safeId.Length == 0 || safeTag.Length == 0 || string.IsNullOrWhiteSpace(root)) return null;
        return Path.Combine(root, safeId, safeTag);
    }

    /// <summary>把版本号 / id 净化为单段目录名。</summary>
    public static string SafeSegment(string? raw)
    {
        var s = (raw ?? "").Trim().TrimStart('v', 'V');
        if (s.Length == 0) return "";
        var chars = s.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' or '+' ? c : '-').ToArray();
        var result = new string(chars).Trim('.', '-', '_');
        if (result.Length == 0 || result is "." or "..") return "";
        return result.Length > 64 ? result[..64] : result;
    }

    /// <summary>
    /// 安全解压：拒绝绝对路径 / <c>..</c> / 符号链接 / 超出体积或条目数上限。
    /// 返回错误码（空串表示成功）。
    /// </summary>
    public static string ExtractZip(string zipPath, string destination)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            if (archive.Entries.Count > MaxEntries) return "dep.error.extract";

            var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
            long total = 0;
            foreach (var entry in archive.Entries)
            {
                var relative = SafeEntryPath(entry.FullName);
                if (relative is null) return "dep.error.extract";

                // 符号链接（Unix external attributes 的 S_IFLNK）
                if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) return "dep.error.extract";

                var target = Path.GetFullPath(Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return "dep.error.extract";

                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }

                total += entry.Length;
                if (total > MaxExtractedBytes || entry.Length > MaxExtractedBytes) return "dep.error.extract";

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }
            return "";
        }
        catch (Exception)
        {
            return "dep.error.extract";
        }
    }

    /// <summary>解压条目路径归一化；不安全返回 null。</summary>
    public static string? SafeEntryPath(string? entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName)) return null;
        var raw = entryName.Replace('\\', '/').Trim();
        if (raw.Length == 0 || raw.Length > 600) return null;
        if (raw.StartsWith('/')) return null;
        if (raw.Any(char.IsControl)) return null;
        if (raw.Length >= 2 && raw[1] == ':') return null;

        var parts = raw.Split('/');
        if (parts.Any(p => p == "..")) return null;
        var cleaned = string.Join('/', parts.Where(p => p.Length > 0 && p != "."));
        return cleaned.Length == 0 ? null : cleaned;
    }

    /// <summary>在解压目录里找可执行文件（有界深度，避免在全盘乱找）。</summary>
    public static string? FindExecutable(string root, string executable, int maxDepth = 5)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(executable)) return null;
        if (!Directory.Exists(root)) return null;

        try
        {
            var direct = Path.Combine(root, executable);
            if (File.Exists(direct)) return direct;

            var queue = new Queue<(string Dir, int Depth)>();
            queue.Enqueue((root, 0));
            while (queue.Count > 0)
            {
                var (dir, depth) = queue.Dequeue();
                if (depth >= maxDepth) continue;

                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    var candidate = Path.Combine(sub, executable);
                    if (File.Exists(candidate)) return candidate;
                    queue.Enqueue((sub, depth + 1));
                }
            }
        }
        catch (Exception)
        {
            // 权限不足 / 目录消失：当作没找到
        }
        return null;
    }
}
