using System.IO;

namespace OBS_Helper.Wpf.Services.ObsConfig;

/// <summary>路径护栏异常。任何试图越过安全边界的操作都会抛它，由上层转成「拒绝执行」提示。</summary>
public sealed class ObsSafePathException : Exception
{
    public ObsSafePathException(string message) : base(message) { }
}

/// <summary>
/// 路径护栏：所有对 OBS 配置目录的删 / 写都要先过这里。<b>绝不</b>直接拼路径就删。
///
/// 七道闸（全过才放行）：
/// 1. 路径可解析且非空；
/// 2. 落在允许的 root（obs-studio 配置目录）之内；
/// 3. root 目录名必须是 <c>obs-studio</c>，且 <c>basic\</c> 或 <c>global.ini</c> 至少存在一个（确认真的 OBS 配置）；
/// 4. target 不是盘符根；
/// 5. target 不是 root 自身；
/// 6. target 名不在 {logs, crashes, themes}（永不触碰），且不等于系统关键目录根（%WINDIR% / %ProgramFiles% / %UserProfile%）；
/// 7. target 不是符号链接 / junction（防逃逸）。
///
/// 所有判定都用 <see cref="Path.GetFullPath"/> 解析掉 <c>..</c> 之后再比较，杜绝路径穿越。
/// </summary>
public static class ObsSafePath
{
    private static readonly HashSet<string> ForbiddenSubdirNames =
        new(StringComparer.OrdinalIgnoreCase) { "logs", "crashes", "themes" };

    /// <summary>在允许的 root 内才可删除。</summary>
    public static void AssertDeletable(string fullPath, string allowedRoot)
    {
        var root = Resolve(allowedRoot);
        var target = Resolve(fullPath);
        if (target.Length == 0)
            throw new ObsSafePathException($"路径无法解析：{fullPath}");

        // 闸 2：必须落在 root 之下
        if (!IsUnder(root, target))
            throw new ObsSafePathException($"路径越界，不在 OBS 配置目录内：{fullPath}");

        // 闸 3：root 必须是 obs-studio，且确属 OBS 配置
        var rootDir = new DirectoryInfo(root);
        if (!string.Equals(rootDir.Name, "obs-studio", StringComparison.OrdinalIgnoreCase))
            throw new ObsSafePathException("只允许操作 obs-studio 配置目录。");
        if (!Directory.Exists(Path.Combine(root, "basic")) &&
            !File.Exists(Path.Combine(root, "global.ini")))
            throw new ObsSafePathException("不是有效的 OBS 配置目录（缺少 basic/ 与 global.ini）。");

        // 闸 4：不能是盘符根
        if (IsDriveRoot(target))
            throw new ObsSafePathException("不能删除磁盘根目录。");

        // 闸 5：不能是 root 自身
        if (string.Equals(target, root, StringComparison.OrdinalIgnoreCase))
            throw new ObsSafePathException("不能删除 OBS 配置根目录本身。");

        // 闸 6：名禁用集合 + 系统关键目录根
        var name = Path.GetFileName(target.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (ForbiddenSubdirNames.Contains(name))
            throw new ObsSafePathException($"永不触碰 {name} 目录。");
        if (IsSystemRoot(target))
            throw new ObsSafePathException("目标位于系统关键目录内，拒绝操作。");

        // 闸 7：拒绝符号链接 / junction
        if (IsReparsePoint(target))
            throw new ObsSafePathException("拒绝操作符号链接 / junction，防止路径逃逸。");
    }

    /// <summary>在允许的 root 内才可写入（用于导入落盘）。比删除更宽松：允许在 root 之下任意创建。</summary>
    public static void AssertWritable(string fullPath, string allowedRoot)
    {
        var root = Resolve(allowedRoot);
        var target = Resolve(fullPath);
        if (target.Length == 0)
            throw new ObsSafePathException($"路径无法解析：{fullPath}");

        if (!IsUnder(root, target))
            throw new ObsSafePathException($"写路径越界，不在 OBS 配置目录内：{fullPath}");

        var rootDir = new DirectoryInfo(root);
        if (!string.Equals(rootDir.Name, "obs-studio", StringComparison.OrdinalIgnoreCase))
            throw new ObsSafePathException("只允许写入 obs-studio 配置目录。");

        if (IsSystemRoot(target))
            throw new ObsSafePathException("目标位于系统关键目录内，拒绝写入。");

        if (IsReparsePoint(target))
            throw new ObsSafePathException("拒绝写入符号链接 / junction。");
    }

    /// <summary>判断路径是否为符号链接 / junction（reparse point）。无法判定时保守返回 false。</summary>
    public static bool IsReparsePoint(string fullPath)
    {
        try
        {
            var dir = new DirectoryInfo(fullPath);
            if (dir.Exists && dir.LinkTarget is not null) return true;
            var file = new FileInfo(fullPath);
            if (file.Exists && file.LinkTarget is not null) return true;
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsUnder(string root, string target)
    {
        root = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return target.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDriveRoot(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            return !string.IsNullOrEmpty(root) &&
                   string.Equals(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                                 path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                                 StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsSystemRoot(string path)
    {
        foreach (var folder in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        })
        {
            if (string.IsNullOrEmpty(folder)) continue;
            var f = Resolve(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(f, path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                              StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string Resolve(string path)
    {
        try { return Path.GetFullPath(path); }
        catch (Exception) { return ""; }
    }
}
