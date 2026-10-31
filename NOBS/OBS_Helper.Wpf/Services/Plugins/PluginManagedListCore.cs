using System.IO;
using OBS_Helper.Wpf.Models.Plugins;

namespace OBS_Helper.Wpf.Services.Plugins;

/// <summary>插件来源：本应用管理 / 仅本机检测。</summary>
public enum ManagedPluginKind
{
    /// <summary>写进 installed.json，可更新 / 回滚 / 卸载。</summary>
    Managed,

    /// <summary>用户自己放进 OBS 的插件：只读展示，绝不由本应用卸载。</summary>
    Local,
}

/// <summary>「已安装」列表的一行（合并后的统一视图）。</summary>
public sealed record ManagedPluginRow(
    string Key,
    string Id,
    string Name,
    string Version,
    string Path,
    ManagedPluginKind Kind,
    bool Curated,
    bool DirectoryMissing,
    bool CanRollback,
    string SourceRepo,
    string Sha256,
    string FileName);

/// <summary>
/// 「已安装插件」列表的合并逻辑（V3.5.0 / 修复已安装不显示）。
///
/// 旧实现只读 <c>installed.json</c>，于是用户用 OBS 自己安装的插件完全不可见，
/// 页面会显示「还没有由本应用安装的插件」——与用户在 OBS 里看到的事实相反。
///
/// 这里把两个数据源合并成一张表：
/// <list type="number">
///   <item>本应用管理（可回滚 / 卸载，优先级最高）；</item>
///   <item>本机扫描（只读，标注「本机检测」）。</item>
/// </list>
/// 去重按「规范化安装目录」而不是文件名：同一个插件可能同时存在多个 DLL，
/// 文件名去重会把它们拆成多行。
/// </summary>
public static class PluginManagedListCore
{
    /// <summary>
    /// 合并两个来源。参数全部可注入，便于单测；默认使用真实文件系统。
    /// </summary>
    public static IReadOnlyList<ManagedPluginRow> Merge(
        IEnumerable<PluginInstallEntry> managed,
        IEnumerable<InstalledPluginFile> local,
        Func<string, bool>? directoryExists = null,
        Func<string, int>? backupCount = null)
    {
        directoryExists ??= Directory.Exists;
        backupCount ??= _ => 0;

        var rows = new List<ManagedPluginRow>();
        var managedDirs = new List<string>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in managed ?? Enumerable.Empty<PluginInstallEntry>())
        {
            var dir = Normalize(entry.InstallDirectory);
            if (dir.Length > 0) managedDirs.Add(dir);

            var missing = dir.Length == 0 || !directoryExists(dir);
            var row = new ManagedPluginRow(
                Key: dir.Length > 0 ? dir : "managed:" + (entry.Id ?? ""),
                Id: entry.Id ?? "",
                Name: string.IsNullOrWhiteSpace(entry.Name) ? (entry.Id ?? "") : entry.Name,
                Version: entry.Version ?? "",
                Path: entry.InstallDirectory ?? "",
                Kind: ManagedPluginKind.Managed,
                Curated: entry.Curated,
                DirectoryMissing: missing,
                CanRollback: !missing && backupCount(entry.Id ?? "") > 0,
                SourceRepo: entry.SourceRepo ?? "",
                Sha256: entry.Sha256 ?? "",
                FileName: entry.AssetName ?? "");

            rows.Add(row);
            if (dir.Length > 0) seenPaths.Add(dir);
        }

        foreach (var plugin in local ?? Enumerable.Empty<InstalledPluginFile>())
        {
            var path = Normalize(plugin.FullPath);
            if (path.Length == 0 || seenPaths.Contains(path)) continue;

            // 已由本应用管理的目录不再重复列出（管理记录优先）。
            if (managedDirs.Any(d => IsUnder(path, d))) continue;

            seenPaths.Add(path);
            rows.Add(new ManagedPluginRow(
                Key: path,
                Id: plugin.CatalogId ?? "",
                Name: string.IsNullOrWhiteSpace(plugin.ProductName) ? plugin.Stem : plugin.ProductName,
                Version: plugin.FileVersion ?? "",
                Path: plugin.FullPath ?? "",
                Kind: ManagedPluginKind.Local,
                Curated: false,
                DirectoryMissing: false,
                CanRollback: false,
                SourceRepo: "",
                Sha256: "",
                FileName: plugin.FileName ?? ""));
        }

        return rows
            .OrderBy(r => r.Kind)
            .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>规范化路径：去空白、GetFullPath（失败时退回原串）、去尾部分隔符、统一大小写不敏感比较。</summary>
    public static string Normalize(string? path)
    {
        var s = (path ?? "").Trim();
        if (s.Length == 0) return "";
        try { s = Path.GetFullPath(s); }
        catch (Exception) { /* 非法路径保持原样，交给调用方判定 */ }
        return s.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>path 是否位于 dir 之下（含 dir 本身的 DLL 文件）。</summary>
    public static bool IsUnder(string normalizedPath, string normalizedDir)
    {
        if (normalizedPath.Length == 0 || normalizedDir.Length == 0) return false;
        if (string.Equals(normalizedPath, normalizedDir, StringComparison.OrdinalIgnoreCase)) return true;
        return normalizedPath.StartsWith(normalizedDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(normalizedDir + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
