using System.IO;

namespace OBS_Helper.Wpf.Services.Plugins;

/// <summary>本机插件在磁盘上的摆放方式。</summary>
public enum LocalPluginUnitKind
{
    /// <summary>OBS 32.x 的一插件一目录：<c>&lt;根&gt;\&lt;插件&gt;\bin\64bit\&lt;插件&gt;.dll</c>。</summary>
    Nested,

    /// <summary>老式扁平摆放：DLL 直接放在插件根或 <c>obs-plugins\64bit</c> 下。</summary>
    Flat,
}

/// <summary>
/// 一个「可管理的本机插件单元」：卸载 / 迁移时到底该动哪个目录。
///
/// <see cref="PluginDirectory"/> 是插件自己的目录（Nested），Flat 时是 DLL 所在目录；
/// <see cref="RootDirectory"/> 是插件根（<c>plugins</c> / <c>obs-plugins</c>），
/// 迁移时只允许在两个已知插件根之间移动，绝不允许把任意路径当成目标。
/// </summary>
public sealed record LocalPluginUnit(
    LocalPluginUnitKind Kind,
    string PluginDirectory,
    string RootDirectory,
    string RootKind,
    string DllPath,
    string PluginName);

/// <summary>
/// 本机插件管理的纯逻辑（X1031Y2）：识别插件单元、计算迁移目标、校验可操作路径。
///
/// 为什么单独抽出来：这些判断全部是「字符串 + 路径」，一旦写错就是删错目录。
/// 放在这里可以逐条覆盖（扁平 / 一插件一目录 / 用户级 / 全局级 / 安装目录 / 非法路径），
/// 服务层只负责真正的文件操作与确认流程。
/// </summary>
public static class LocalPluginManageCore
{
    /// <summary>
    /// 从扫描到的 DLL 路径推断插件单元。无法安全判定时返回 null（界面不提供管理操作）。
    /// </summary>
    public static LocalPluginUnit? Describe(string? dllPath, string? sourceLabel)
    {
        var full = Normalize(dllPath);
        if (full.Length == 0) return null;

        var dir = Path.GetDirectoryName(full);
        if (string.IsNullOrWhiteSpace(dir)) return null;

        var parentName = Path.GetFileName(dir);
        var parentOfParent = Path.GetDirectoryName(dir);

        // 一插件一目录：<根>\<插件>\bin\64bit\<dll>
        if (string.Equals(parentName, "64bit", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(parentOfParent))
        {
            var binDir = parentOfParent!;
            if (string.Equals(Path.GetFileName(binDir), "bin", StringComparison.OrdinalIgnoreCase))
            {
                var pluginDir = Path.GetDirectoryName(binDir);
                var rootDir = string.IsNullOrWhiteSpace(pluginDir) ? null : Path.GetDirectoryName(pluginDir);
                if (!string.IsNullOrWhiteSpace(pluginDir) && !string.IsNullOrWhiteSpace(rootDir) &&
                    IsPluginRoot(rootDir!))
                {
                    var name = Path.GetFileName(pluginDir!);
                    if (IsSafeLeaf(name))
                    {
                        return new LocalPluginUnit(
                            LocalPluginUnitKind.Nested,
                            Normalize(pluginDir),
                            Normalize(rootDir),
                            ClassifyRoot(rootDir!),
                            full,
                            name);
                    }
                }
            }

            // 老布局：<插件根>\64bit\<dll>；也兼容 <插件根>\<插件>\64bit\<dll>
            if (IsPluginRoot(binDir))
            {
                return new LocalPluginUnit(
                    LocalPluginUnitKind.Flat,
                    Normalize(dir),
                    Normalize(binDir),
                    ClassifyRoot(binDir),
                    full,
                    PluginCatalogCore.NormalizeDllStem(Path.GetFileName(full)));
            }

            var grand = Path.GetDirectoryName(binDir);
            if (!string.IsNullOrWhiteSpace(grand) && IsPluginRoot(grand!))
            {
                return new LocalPluginUnit(
                    LocalPluginUnitKind.Flat,
                    Normalize(dir),
                    Normalize(grand),
                    ClassifyRoot(grand!),
                    full,
                    PluginCatalogCore.NormalizeDllStem(Path.GetFileName(full)));
            }
        }

        // 扁平：DLL 直接放在插件根
        var root = Path.GetDirectoryName(dir);
        if (!string.IsNullOrWhiteSpace(root) && IsPluginRoot(dir))
        {
            return new LocalPluginUnit(
                LocalPluginUnitKind.Flat,
                Normalize(dir),
                Normalize(dir),
                ClassifyRoot(dir),
                full,
                PluginCatalogCore.NormalizeDllStem(Path.GetFileName(full)));
        }

        // 兜底：只有来源标签明确是用户 / 全局插件根时才认
        if (sourceLabel is "user" or "global")
        {
            return new LocalPluginUnit(
                LocalPluginUnitKind.Flat,
                Normalize(dir),
                Normalize(dir),
                sourceLabel,
                full,
                PluginCatalogCore.NormalizeDllStem(Path.GetFileName(full)));
        }

        return null;
    }

    /// <summary>插件根名称只认 <c>plugins</c> 与 <c>obs-plugins</c>，避免把普通目录当插件根。</summary>
    public static bool IsPluginRoot(string? dir)
    {
        var leaf = Path.GetFileName((dir ?? "").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.Equals(leaf, "plugins", StringComparison.OrdinalIgnoreCase)
            || string.Equals(leaf, "obs-plugins", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>判断插件根属于用户级 / 全局级 / OBS 安装目录。</summary>
    public static string ClassifyRoot(string? root)
    {
        var full = Normalize(root);
        if (full.Length == 0) return "unknown";

        var leaf = Path.GetFileName(full);
        if (string.Equals(leaf, "obs-plugins", StringComparison.OrdinalIgnoreCase)) return "install";

        try
        {
            var roaming = Normalize(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obs-studio", "plugins"));
            if (string.Equals(full, roaming, StringComparison.OrdinalIgnoreCase)) return "user";

            var programData = Normalize(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "obs-studio", "plugins"));
            if (string.Equals(full, programData, StringComparison.OrdinalIgnoreCase)) return "global";
        }
        catch (Exception)
        {
            // 取不到特殊文件夹时按未知处理
        }

        return "unknown";
    }

    /// <summary>
    /// 计算迁移目标插件根：用户级 ⇄ 全局级；OBS 安装目录 → 用户级（不需要管理员权限）。
    /// 扁平摆放 / 未知根 / 目标与当前相同都返回 null。
    /// </summary>
    public static string? MigrationTargetRoot(LocalPluginUnit? unit, string? userPluginsRoot, string? globalPluginsRoot)
    {
        if (unit is null || unit.Kind != LocalPluginUnitKind.Nested) return null;

        var user = Normalize(userPluginsRoot);
        var global = Normalize(globalPluginsRoot);
        var current = Normalize(unit.RootDirectory);

        if (user.Length > 0 && string.Equals(current, user, StringComparison.OrdinalIgnoreCase))
            return global.Length > 0 ? global : null;

        if (global.Length > 0 && string.Equals(current, global, StringComparison.OrdinalIgnoreCase))
            return user.Length > 0 ? user : null;

        if (string.Equals(unit.RootKind, "install", StringComparison.OrdinalIgnoreCase))
            return user.Length > 0 ? user : null;

        return null;
    }

    /// <summary>目标插件根对应的界面文案键后缀：user / global。</summary>
    public static string? MigrationTargetScope(string? targetRoot)
    {
        if (string.IsNullOrWhiteSpace(targetRoot)) return null;
        return ClassifyRoot(targetRoot) switch
        {
            "user" => "user",
            "global" => "global",
            _ => null,
        };
    }

    /// <summary>
    /// 校验一个目录确实在允许的插件根之下（不包含根本身）。
    /// 用于卸载 / 迁移前的最后一道护栏：即使清单被改过，也不会把 <c>C:\Windows</c> 当成插件目录。
    /// </summary>
    public static bool IsSafePluginDirectory(string? directory, IEnumerable<string>? allowedRoots)
    {
        var dir = Normalize(directory);
        if (dir.Length == 0 || allowedRoots is null) return false;

        foreach (var root in allowedRoots)
        {
            var r = Normalize(root);
            if (r.Length == 0 || string.Equals(dir, r, StringComparison.OrdinalIgnoreCase)) continue;
            if (IsUnder(dir, r)) return true;
        }
        return false;
    }

    /// <summary>校验插件单元：目录确实在插件根之下，且 DLL 就在该单元里。</summary>
    public static bool IsSafeUnit(LocalPluginUnit unit)
    {
        if (unit is null || !IsPluginRoot(unit.RootDirectory)) return false;

        if (unit.Kind == LocalPluginUnitKind.Nested)
        {
            return IsSafePluginDirectory(unit.PluginDirectory, new[] { unit.RootDirectory })
                   && string.Equals(
                       Path.GetFileName(unit.PluginDirectory),
                       unit.PluginName,
                       StringComparison.OrdinalIgnoreCase)
                   && IsUnder(unit.DllPath, unit.PluginDirectory);
        }

        // 扁平：只允许动 DLL 本身，目录不参与删除
        return string.Equals(
            Normalize(Path.GetDirectoryName(unit.DllPath)),
            unit.PluginDirectory,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>path 是否位于 dir 之下（不含 dir 本身）。</summary>
    public static bool IsUnder(string? normalizedPath, string? normalizedDir)
    {
        var p = Normalize(normalizedPath);
        var d = Normalize(normalizedDir);
        if (p.Length == 0 || d.Length == 0) return false;
        if (string.Equals(p, d, StringComparison.OrdinalIgnoreCase)) return false;
        return p.StartsWith(d + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>备份目录名：时间戳 + 净化后的插件名，保证可读且不会拼出非法路径。</summary>
    public static string BackupName(string? pluginName, DateTime now)
        => now.ToString("yyyyMMddHHmmss") + "-" + SafeName(pluginName);

    /// <summary>把插件名净化为可安全用作目录名的一段。</summary>
    public static string SafeName(string? raw)
    {
        var s = (raw ?? "").Trim();
        if (s.Length == 0) return "plugin";
        var chars = s.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-').ToArray();
        var slug = new string(chars).Trim('-', '.', ' ');
        if (slug.Length == 0) return "plugin";
        return slug.Length > 80 ? slug[..80] : slug;
    }

    /// <summary>规范化路径：去空白、GetFullPath、去尾部分隔符。</summary>
    public static string Normalize(string? path)
    {
        var s = (path ?? "").Trim();
        if (s.Length == 0) return "";
        try { s = Path.GetFullPath(s); }
        catch (Exception) { /* 非法路径保持原样，交给调用方判定 */ }
        return s.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsSafeLeaf(string? name)
        => !string.IsNullOrWhiteSpace(name)
           && name is not ("." or "..")
           && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
}
