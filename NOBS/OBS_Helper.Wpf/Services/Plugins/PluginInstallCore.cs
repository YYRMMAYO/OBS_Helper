using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using OBS_Helper.Wpf.Models.Plugins;

namespace OBS_Helper.Wpf.Services.Plugins;

/// <summary>
/// 插件安装的纯逻辑（V3.3.0 / P0-5）：版本比较、资产选择、zip 路径安全、目录映射、PE 架构、清单。
///
/// 插件是原生 DLL，装进去就等于执行第三方代码。所以这里的每一条限制都不是「防御性编程」的客套：
/// <list type="bullet">
///   <item><b>zip slip</b>：绝对路径 / <c>..</c> / 符号链接一律拒绝；</item>
///   <item><b>文件类型</b>：只允许插件常见的静态资源，可执行脚本 / 安装包一律拒绝；</item>
///   <item><b>体积</b>：条目数 / 单文件 / 总体积都有上限，避免把磁盘塞满；</item>
///   <item><b>架构</b>：必须能找到 64 位 DLL（或托管 AnyCPU），否则 OBS 64 位加载不了。</item>
/// </list>
/// </summary>
public static class PluginInstallCore
{
    public const int MaxEntries = 5000;
    public const long MaxTotalBytes = 512L * 1024 * 1024;
    public const long MaxEntryBytes = 256L * 1024 * 1024;
    public const long MaxAssetBytes = 200L * 1024 * 1024;
    public const int MaxPathLength = 512;

    /// <summary>允许解压的扩展名（小写，含点）。刻意不含 exe / bat / ps1 / msi / lnk。</summary>
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dll", ".pdb",
        ".json", ".ini", ".cfg", ".xml", ".yaml", ".yml", ".txt", ".md", ".license", ".toml",
        ".png", ".jpg", ".jpeg", ".gif", ".svg", ".ico", ".webp", ".bmp",
        ".html", ".htm", ".css", ".js",
        ".woff", ".woff2", ".ttf", ".otf",
        ".lua", ".effect", ".shader", ".hlsl", ".glsl",
        ".qm", ".mo", ".dat", ".bin"
    };

    private static readonly Regex OwnerRepoRegex = new(
        @"^[A-Za-z0-9](?:[A-Za-z0-9_.-]{0,98})/[A-Za-z0-9_.-]{1,100}$", RegexOptions.Compiled);

    // ---------------------------------------------------------------- 版本

    /// <summary>
    /// 宽松版本比较（&gt;0 = a 更新）。支持 <c>3.3.0</c> / <c>v3.3</c> / <c>1.2.3-beta.1</c>；
    /// 数字段逐个比较，缺失按 0；无法解析的段按 0。相等返回 0。
    /// </summary>
    public static int CompareVersions(string? a, string? b)
    {
        var (coreA, preA) = SplitVersion(a);
        var (coreB, preB) = SplitVersion(b);

        var len = Math.Max(coreA.Length, coreB.Length);
        for (var i = 0; i < len; i++)
        {
            var x = i < coreA.Length ? coreA[i] : 0;
            var y = i < coreB.Length ? coreB[i] : 0;
            if (x != y) return x > y ? 1 : -1;
        }

        // 主版本相同：正式版 > 预发布版（3.3.0 > 3.3.0-beta.2）
        if (preA.Length == 0 && preB.Length == 0) return 0;
        if (preA.Length == 0) return 1;
        if (preB.Length == 0) return -1;

        var pa = preA.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var pb = preB.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var plen = Math.Max(pa.Length, pb.Length);
        for (var i = 0; i < plen; i++)
        {
            var x = i < pa.Length ? pa[i] : "";
            var y = i < pb.Length ? pb[i] : "";
            var nx = int.TryParse(x, out var xi);
            var ny = int.TryParse(y, out var yi);
            if (nx && ny)
            {
                if (xi != yi) return xi > yi ? 1 : -1;
            }
            else
            {
                var cmp = string.CompareOrdinal(x, y);
                if (cmp != 0) return cmp > 0 ? 1 : -1;
            }
        }
        return 0;
    }

    private static (int[] Core, string Pre) SplitVersion(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return (new[] { 0 }, "");
        var text = v.Trim().TrimStart('v', 'V');

        var plus = text.IndexOf('+');
        if (plus >= 0) text = text[..plus];

        var dash = text.IndexOf('-');
        var pre = "";
        if (dash >= 0)
        {
            pre = text[(dash + 1)..];
            text = text[..dash];
        }

        var core = text
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => int.TryParse(new string(p.TakeWhile(char.IsDigit).ToArray()), out var n)
                ? Math.Min(n, 1_000_000)
                : 0)
            .ToArray();

        if (core.Length == 0) core = new[] { 0 };
        return (core, pre);
    }

    // ---------------------------------------------------------------- 资产选择

    /// <summary>GitHub 自动生成的源码包（不能当插件安装）。</summary>
    public static bool IsSourceCodeAsset(string? name)
        => (name ?? "").Contains("source code", StringComparison.OrdinalIgnoreCase)
           || (name ?? "").EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 从 Release 资产里挑安装包：只接受 .zip（.7z 无 BCL 支持，不做），
    /// 优先 Windows / x64 命名的包。找不到返回 null。
    /// </summary>
    public static PluginReleaseAsset? SelectAsset(IEnumerable<PluginReleaseAsset>? assets)
    {
        if (assets is null) return null;

        var zips = assets
            .Where(a => !string.IsNullOrWhiteSpace(a.Url))
            .Where(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .Where(a => !IsSourceCodeAsset(a.Name))
            .ToList();

        if (zips.Count == 0) return null;
        if (zips.Count == 1) return zips[0];

        static int Score(PluginReleaseAsset a)
        {
            var n = a.Name.ToLowerInvariant();
            var score = 0;
            if (n.Contains("windows") || n.Contains("win64") || n.Contains("win-")) score += 4;
            if (n.Contains("x64") || n.Contains("64bit") || n.Contains("64-bit")) score += 2;
            if (n.Contains("installer") || n.Contains("setup")) score += 1; // 打包版通常更完整
            return score;
        }

        return zips.OrderByDescending(Score).ThenByDescending(a => a.Size).First();
    }

    // ---------------------------------------------------------------- zip 安全

    /// <summary>
    /// 规范化 zip 条目路径。返回 false 表示绝对路径、含 <c>..</c>、盘符、控制字符或超长。
    /// </summary>
    public static bool IsSafeEntryPath(string? entryName, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(entryName)) return false;

        var raw = entryName.Replace('\\', '/').Trim();
        if (raw.Length == 0 || raw.Length > MaxPathLength) return false;
        // 绝对路径一律拒绝（不能「悄悄去掉前导斜杠」当成相对路径）
        if (raw.StartsWith('/')) return false;
        if (raw.StartsWith("./")) raw = raw[2..];
        if (raw.Length == 0) return false;
        if (raw.Contains(':')) return false;
        if (raw.Any(char.IsControl)) return false;

        foreach (var seg in raw.Split('/'))
        {
            if (seg.Length == 0) continue;
            if (seg == "." || seg == "..") return false;
            if (seg.EndsWith(' ') || seg.EndsWith('.')) return false; // Windows 会静默去掉，导致路径不一致
        }

        normalized = raw.Trim('/');
        return normalized.Length > 0;
    }

    /// <summary>扩展名白名单（目录条目返回 true，交给调用方跳过）。</summary>
    public static bool IsAllowedExtension(string? normalizedPath)
    {
        if (string.IsNullOrWhiteSpace(normalizedPath)) return false;
        if (normalizedPath.EndsWith('/')) return true;
        return AllowedExtensions.Contains(Path.GetExtension(normalizedPath));
    }

    /// <summary>zip 里的符号链接（Unix external attributes 的 S_IFLNK）。</summary>
    public static bool IsSymlinkEntry(ZipArchiveEntry entry)
        => entry is not null && ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000;

    /// <summary>
    /// 所有条目共享的顶层目录（GitHub 打包常见 <c>plugin-1.2.3/</c>）；没有则返回空串。
    /// 只剥离「不是文件」的顶层目录，避免把 <c>bin/...</c> 这种真实结构拆掉。
    /// </summary>
    public static string CommonRootPrefix(IEnumerable<string>? normalizedEntries)
    {
        if (normalizedEntries is null) return "";
        var list = normalizedEntries
            .Where(e => !string.IsNullOrWhiteSpace(e) && !e.EndsWith('/'))
            .ToList();
        if (list.Count == 0) return "";

        // 只要有条目直接就落在顶层（如 plugin.dll / readme.md），就不存在「包装目录」，不剥
        if (list.Any(e => !e.Contains('/'))) return "";

        var first = list[0].Split('/');
        if (first.Length < 2) return "";
        var root = first[0];
        if (root.Length == 0 || root == ".") return "";

        foreach (var e in list)
        {
            if (!e.StartsWith(root + "/", StringComparison.Ordinal)) return "";
        }
        return root;
    }

    /// <summary>去掉公共顶层目录后的相对路径。</summary>
    public static string MapEntry(string normalizedEntry, string? rootPrefix)
    {
        var e = normalizedEntry.TrimStart('/');
        if (!string.IsNullOrEmpty(rootPrefix) && e.StartsWith(rootPrefix + "/", StringComparison.Ordinal))
            e = e[(rootPrefix.Length + 1)..];
        return e;
    }

    /// <summary>
    /// 校验整个压缩包；返回错误码（空串 = 通过）。不写磁盘。
    /// </summary>
    public static string ValidateArchive(ZipArchive? archive, out long totalBytes, out List<string> normalizedPaths)
    {
        totalBytes = 0;
        normalizedPaths = new List<string>();
        if (archive is null) return "PLUGIN_ZIP_INVALID";
        if (archive.Entries.Count > MaxEntries) return "PLUGIN_ZIP_TOO_LARGE";

        foreach (var entry in archive.Entries)
        {
            if (IsSymlinkEntry(entry)) return "PLUGIN_ZIP_UNSAFE";
            if (!IsSafeEntryPath(entry.FullName, out var normalized)) return "PLUGIN_ZIP_UNSAFE";
            if (!IsAllowedExtension(normalized)) return "PLUGIN_ZIP_UNSAFE_FILE";
            if (entry.Length > MaxEntryBytes) return "PLUGIN_ZIP_TOO_LARGE";

            totalBytes += entry.Length;
            if (totalBytes > MaxTotalBytes) return "PLUGIN_ZIP_TOO_LARGE";

            if (!entry.FullName.EndsWith('/')) normalizedPaths.Add(normalized);
        }

        if (normalizedPaths.Count == 0) return "PLUGIN_ZIP_EMPTY";
        return "";
    }

    /// <summary>去掉公共目录后，判断是否至少有一个 64 位（或托管）DLL；返回错误码。</summary>
    public static string ValidateDllLayout(IReadOnlyList<string>? mappedPaths)
    {
        if (mappedPaths is null || mappedPaths.Count == 0) return "PLUGIN_ZIP_EMPTY";

        var dlls = mappedPaths
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (dlls.Count == 0) return "PLUGIN_NO_DLL";

        // OBS 32+：<plugin>/bin/64bit/*.dll；旧布局：obs-plugins/64bit/*.dll
        var has64 = dlls.Any(p =>
            p.Split('/').Any(seg => seg.Equals("64bit", StringComparison.OrdinalIgnoreCase))
            || p.Contains("/64/", StringComparison.OrdinalIgnoreCase)
            || p.Contains("x64", StringComparison.OrdinalIgnoreCase));

        return has64 ? "" : "PLUGIN_NO_X64_DLL";
    }

    /// <summary>
    /// PE 头判断：是否 x64 或托管 AnyCPU。
    /// <paramref name="head"/> 只需文件前 4 KB（读不到 PE 头按不安全处理）。
    /// </summary>
    public static bool IsX64OrManaged(ReadOnlySpan<byte> head)
    {
        if (head.Length < 0x40) return false;
        if (head[0] != 'M' || head[1] != 'Z') return false;

        var peOffset = BitConverter.ToInt32(head.Slice(0x3C, 4));
        if (peOffset <= 0 || peOffset + 24 >= head.Length) return false;
        if (head[peOffset] != 'P' || head[peOffset + 1] != 'E' || head[peOffset + 2] != 0 || head[peOffset + 3] != 0)
            return false;

        var machine = BitConverter.ToUInt16(head.Slice(peOffset + 4, 2));
        if (machine == 0x8664) return true; // AMD64

        if (machine != 0x014C) return false; // 非 x86 也不是 x64

        // x86 机器码：只有「纯托管」的 AnyCPU DLL 才能在 64 位 OBS 里加载
        var optional = peOffset + 24;
        if (optional + 2 > head.Length) return false;
        var magic = BitConverter.ToUInt16(head.Slice(optional, 2));
        var clrDirIndex = optional + (magic == 0x20B ? 224 : 208);
        if (clrDirIndex + 8 > head.Length) return false;
        var clrRva = BitConverter.ToInt32(head.Slice(clrDirIndex, 4));
        var clrSize = BitConverter.ToInt32(head.Slice(clrDirIndex + 4, 4));
        return clrRva != 0 && clrSize != 0;
    }

    // ---------------------------------------------------------------- GitHub

    /// <summary>解析 <c>owner/repo</c>（也接受完整 GitHub URL）。</summary>
    public static bool TryParseOwnerRepo(string? input, out string ownerRepo)
    {
        ownerRepo = "";
        if (string.IsNullOrWhiteSpace(input)) return false;

        var text = input.Trim();
        if (text.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
            text = text["https://github.com/".Length..];
        text = text.Trim('/');
        if (text.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) text = text[..^4];

        var parts = text.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return false;
        var candidate = parts[0] + "/" + parts[1];
        if (!OwnerRepoRegex.IsMatch(candidate)) return false;
        if (parts[0].Contains("..") || parts[1].Contains("..")) return false;

        ownerRepo = candidate;
        return true;
    }

    /// <summary>构造 GitHub 搜索 API 地址（只搜 OBS 插件 topic）。</summary>
    public static string BuildSearchUrl(string? query, int perPage = 20)
    {
        var q = "topic:obs-plugin";
        if (!string.IsNullOrWhiteSpace(query))
        {
            var cleaned = new string(query.Trim().Where(c => !char.IsControl(c)).ToArray());
            q += " " + cleaned;
        }
        perPage = Math.Clamp(perPage, 1, 50);
        return "https://api.github.com/search/repositories?q=" + Uri.EscapeDataString(q)
               + "&sort=stars&order=desc&per_page=" + perPage;
    }

    /// <summary>latest release API 地址。</summary>
    public static string BuildLatestReleaseUrl(string ownerRepo)
        => "https://api.github.com/repos/" + ownerRepo + "/releases/latest";

    /// <summary>允许下载的主机（GitHub 官方域名及其资产重定向域）。</summary>
    public static bool IsAllowedDownloadHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        var h = host.Trim().ToLowerInvariant();
        return h is "github.com" or "api.github.com" or "objects.githubusercontent.com"
            or "codeload.github.com" or "raw.githubusercontent.com";
    }

    /// <summary>安装目录名净化（同 Live2D 的 slug 规则，避免拼出非法路径）。</summary>
    public static string SafeSlug(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "plugin";
        var chars = raw.Trim().Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray();
        var slug = new string(chars).Trim('-').ToLowerInvariant();
        if (slug.Length == 0) return "plugin";
        return slug.Length > 64 ? slug[..64] : slug;
    }

    /// <summary>插件安装根目录：用户级优先（无需 UAC）。</summary>
    public static string DefaultInstallRoot(string scope, string localAppData, string programData)
        => string.Equals(scope, "global", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(programData, "obs-studio", "plugins")
            : Path.Combine(localAppData, "obs-studio", "plugins");
}
