using System.Text.Json;
using System.Text.RegularExpressions;

namespace OBS_Helper.Wpf.Services.Interop;

/// <summary>sidecar 清单的一项（PLAN_V3.4.0 附录 C）。</summary>
public sealed class SidecarEntry
{
    public string Id { get; init; } = "";
    public string Version { get; init; } = "";
    public string Url { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public long Size { get; init; }
    public string License { get; init; } = "";
    public string Source { get; init; } = "";
    /// <summary>最低系统版本（如 <c>10.0</c>）；空表示不限制。</summary>
    public string MinOs { get; init; } = "";
    public string Arch { get; init; } = "x64";
    /// <summary>解压后要启动的可执行文件名（相对安装目录）。</summary>
    public string Executable { get; init; } = "";
    public string[] Args { get; init; } = Array.Empty<string>();
    public string Notes { get; init; } = "";
    /// <summary>下载后的解压子目录（默认与 id 相同）。</summary>
    public string InstallSubdir { get; init; } = "";
    /// <summary>
    /// 手动安装项：清单只提供官方来源与放置路径，应用不代下载。
    /// 用于本轮无法完成供应链核验（哈希/许可证）的 sidecar；正式自动下载需要补全 sha256 后再置为 false。
    /// </summary>
    public bool Manual { get; init; }

    public bool IsUsable => Id.Length > 0 && Version.Length > 0;
}

/// <summary>整份清单。</summary>
public sealed class SidecarManifest
{
    public int Schema { get; init; } = 1;
    public string Updated { get; init; } = "";
    public List<SidecarEntry> Entries { get; init; } = new();

    public SidecarEntry? Find(string id)
        => Entries.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// sidecar 清单的解析与安全校验（V3.4.0 / 批次 0）。
///
/// 边界（PLAN_V3.4.0 §6.3 / 附录 C）：
/// <list type="bullet">
///   <item>只允许 HTTPS 与固定域名白名单（GitHub 官方下载域 / FFmpeg 官方推荐构建域）；</item>
///   <item>SHA-256 必填且必须是 64 位十六进制；体积必填；版本固定；许可证与来源必填；</item>
///   <item>id / version 不得含路径分隔符或 <c>..</c>，解压条目名同样校验（防目录穿越）；</item>
///   <item>二进制不入库、不静默安装、不静默更新 —— 这些是调用方的责任，这里只保证「不合格清单不可用」。</item>
/// </list>
/// 纯逻辑，可单测。
/// </summary>
public static class SidecarCore
{
    /// <summary>允许的下载域名（含 GitHub Release 重定向后的 CDN）。</summary>
    public static readonly string[] AllowedHosts =
    {
        "github.com",
        "objects.githubusercontent.com",
        "github-releases.githubusercontent.com",
        "raw.githubusercontent.com",
        "www.gyan.dev",
        "rclone.org",
    };

    /// <summary>允许声明的许可证（GPL 只允许作为外部进程）。</summary>
    public static readonly string[] AllowedLicenses =
    {
        "MIT", "Apache-2.0", "BSD-2-Clause", "BSD-3-Clause", "ISC",
        "LGPL-2.1-only", "LGPL-2.1-or-later", "LGPL-3.0-only", "LGPL-3.0-or-later",
        "GPL-2.0-only", "GPL-2.0-or-later", "GPL-3.0-only", "GPL-3.0-or-later",
        "MPL-2.0", "Unlicense", "Proprietary",
    };

    private static readonly Regex IdPattern = new("^[a-z0-9][a-z0-9._-]{0,39}$", RegexOptions.CultureInvariant);
    private static readonly Regex VersionPattern = new("^[0-9A-Za-z][0-9A-Za-z._+-]{0,31}$", RegexOptions.CultureInvariant);
    private static readonly Regex ShaPattern = new("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant);

    /// <summary>
    /// 解析清单 JSON 并逐项校验。返回值里 <c>Errors</c> 非空表示清单不可用（调用方必须拒绝启用）。
    /// </summary>
    public static (SidecarManifest Manifest, List<string> Errors) Parse(string? json)
    {
        var errors = new List<string>();
        var manifest = new SidecarManifest();
        if (string.IsNullOrWhiteSpace(json)) return (manifest, new List<string> { "manifest.empty" });

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return (manifest, new List<string> { "manifest.json: " + ex.Message });
        }

        using (doc)
        {
            var root = doc.RootElement;
            var schema = root.TryGetProperty("schema", out var s) && s.TryGetInt32(out var sv) ? sv : 1;
            var updated = root.TryGetProperty("updated", out var u) ? u.GetString() ?? "" : "";
            var entries = new List<SidecarEntry>();
            if (root.TryGetProperty("sidecars", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var item in arr.EnumerateArray())
                {
                    index++;
                    var entry = new SidecarEntry
                    {
                        Id = Str(item, "id"),
                        Version = Str(item, "version"),
                        Url = Str(item, "url"),
                        Sha256 = Str(item, "sha256"),
                        Size = item.TryGetProperty("size", out var size) && size.TryGetInt64(out var sz) ? sz : 0,
                        License = Str(item, "license"),
                        Source = Str(item, "source"),
                        MinOs = Str(item, "minOs"),
                        Arch = Str(item, "arch") is { Length: > 0 } arch ? arch : "x64",
                        Executable = Str(item, "executable"),
                        InstallSubdir = Str(item, "installSubdir"),
                        Notes = Str(item, "notes"),
                        Manual = item.TryGetProperty("manual", out var manual) && manual.ValueKind == JsonValueKind.True,
                        Args = item.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Array
                            ? args.EnumerateArray().Select(a => a.GetString() ?? "").ToArray()
                            : Array.Empty<string>(),
                    };
                    errors.AddRange(Validate(entry).Select(e => $"sidecars[{index}]: {e}"));
                    entries.Add(entry);
                }
            }
            else
            {
                errors.Add("manifest.sidecars: missing array");
            }

            if (entries.Count == 0) errors.Add("manifest.sidecars: empty");
            var duplicates = entries.GroupBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            foreach (var dup in duplicates) errors.Add($"manifest.sidecars: duplicate id {dup}");

            manifest = new SidecarManifest { Schema = schema, Updated = updated, Entries = entries };
        }
        return (manifest, errors);
    }

    /// <summary>单项校验（解析后独立可用，便于单测逐条覆盖）。</summary>
    public static List<string> Validate(SidecarEntry entry)
    {
        var errors = new List<string>();
        if (!IdPattern.IsMatch(entry.Id)) errors.Add("id: must match [a-z0-9._-], 1~40 chars");
        if (!VersionPattern.IsMatch(entry.Version)) errors.Add("version: invalid or empty");
        if (!Uri.TryCreate(entry.Url, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            errors.Add("url: must be absolute https");
        else if (!AllowedHosts.Any(h => string.Equals(h, uri.Host, StringComparison.OrdinalIgnoreCase)))
            errors.Add("url: host not in allowlist");
        else if (!string.IsNullOrEmpty(uri.Query) && uri.Query.Length > 0 && !uri.Query.StartsWith("?"))
            errors.Add("url: suspicious query");

        if (!entry.Manual)
        {
            if (!ShaPattern.IsMatch(entry.Sha256)) errors.Add("sha256: must be 64 hex chars");
            if (entry.Size <= 0) errors.Add("size: must be > 0");
        }
        if (!AllowedLicenses.Contains(entry.License)) errors.Add("license: not in allowlist");
        if (!Uri.TryCreate(entry.Source, UriKind.Absolute, out var source) ||
            !string.Equals(source.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            errors.Add("source: must be absolute https");
        if (entry.MinOs.Length > 0 && !Version.TryParse(entry.MinOs.TrimEnd('.'), out _))
            errors.Add("minOs: invalid version");
        if (entry.Arch is not ("x64" or "arm64" or "any")) errors.Add("arch: must be x64/arm64/any");
        if (entry.Executable.Length > 0 && SafeRelativePath(entry.Executable) is null)
            errors.Add("executable: unsafe relative path");
        if (entry.InstallSubdir.Length > 0 && SafeRelativePath(entry.InstallSubdir) is null)
            errors.Add("installSubdir: unsafe relative path");
        if (entry.Args.Any(a => a.Contains('\0'))) errors.Add("args: contains NUL");
        return errors;
    }

    /// <summary>把用户输入 / 解压条目名归一化成安全的相对路径；不安全返回 null。</summary>
    public static string? SafeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (path.Contains('\0')) return null;
        var normalized = path.Replace('\\', '/').Trim();
        if (normalized.Length == 0) return null;
        if (normalized.StartsWith('/')) return null;
        if (normalized.Length >= 2 && normalized[1] == ':') return null; // C:\...
        var segments = normalized.Split('/');
        if (segments.Any(s => s.Length == 0 || s == "." || s == "..")) return null;
        if (segments.Any(s => s.IndexOfAny(new[] { ':', '*', '?', '"', '<', '>', '|' }) >= 0)) return null;
        return string.Join('/', segments);
    }

    /// <summary>安装目录：<c>&lt;root&gt;\&lt;id&gt;\&lt;version&gt;</c>，id/version 必须已通过校验。</summary>
    public static string? InstallDirectory(string root, SidecarEntry entry)
    {
        if (!IdPattern.IsMatch(entry.Id) || !VersionPattern.IsMatch(entry.Version)) return null;
        return System.IO.Path.Combine(root, entry.Id, entry.Version);
    }

    /// <summary>比较两个版本号（数字段按数值、缺失段视为 0；非数字后缀按字符串）。新版本在前返回正数。</summary>
    public static int CompareVersion(string? left, string? right)
    {
        static string[] Parts(string? v) => (v ?? "").Trim().TrimStart('v', 'V').Split('.', '-', '+');
        var a = Parts(left);
        var b = Parts(right);
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var x = i < a.Length ? a[i] : "0";
            var y = i < b.Length ? b[i] : "0";
            var cmp = long.TryParse(x, out var nx) && long.TryParse(y, out var ny)
                ? nx.CompareTo(ny)
                : string.CompareOrdinal(x, y);
            if (cmp != 0) return cmp;
        }
        return 0;
    }

    /// <summary>系统主版本是否满足 sidecar 的最低系统要求（Win7 = 6.1，Win10 = 10.0）。</summary>
    public static bool MeetsMinOs(string? minOs, Version current)
    {
        if (string.IsNullOrWhiteSpace(minOs)) return true;
        return Version.TryParse(minOs.TrimEnd('.'), out var required) && current >= required;
    }

    /// <summary>当前进程架构对应的清单架构标签。</summary>
    public static string CurrentArch => System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
    {
        System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
        System.Runtime.InteropServices.Architecture.X64 => "x64",
        _ => "x86",
    };

    private static string Str(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}
