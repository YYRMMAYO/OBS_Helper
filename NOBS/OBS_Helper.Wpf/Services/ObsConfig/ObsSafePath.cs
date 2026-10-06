using System.IO;
using OBS_Helper.Wpf.Localization;

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
/// 3. root 必须是<b>受信任的</b> OBS 配置目录 —— 由 <see cref="ObsPathService"/> 在定位成功时登记
///    （自动探测到的 <c>%AppData%\obs-studio</c> / 便携目录，或用户在设置里手动确认过的目录）。
///    早期实现只看「目录名是不是 obs-studio」，两头都不安全：手动指到别的名字 → 彻底重置必被拒（功能不可用）；
///    手动指到任意一个恰好叫 obs-studio 的目录 → 护栏放行。现在按登记记录判定（见 <see cref="RegisterTrustedRoot"/>）；
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

    /// <summary>已确认可信的 OBS 配置根（由 <see cref="ObsPathService"/> 登记）。</summary>
    private static readonly HashSet<string> TrustedRoots = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object TrustedGate = new();

    /// <summary>
    /// 登记一个「可信」的 OBS 配置根目录。
    ///
    /// 调用者必须已经确认过它确实是 OBS 配置目录（自动探测或用户显式指定），
    /// 因为登记之后它的子路径就允许被删 / 写。
    ///
    /// V3.0 审查补正：<b>盘符根与系统关键目录永远不可登记</b>。
    /// 否则「手动把配置目录指到 D:\」之后，整个 D: 盘都会被当成可信写区 ——
    /// 而手动目录来自用户零校验输入的文件夹选择器。
    /// </summary>
    public static void RegisterTrustedRoot(string? root)
    {
        var r = Trim(Resolve(root ?? ""));
        if (r.Length == 0) return;
        if (IsDriveRoot(r) || IsSystemRoot(r)) return;
        lock (TrustedGate) TrustedRoots.Add(r);
    }

    /// <summary>
    /// 该根是否受信任（已登记，或「目录名就是 obs-studio 且确实含 OBS 配置标记」这一约定位置）。
    ///
    /// 名字兜底加了**配置标记**要求：只有目录名不足以证明它是 OBS 配置目录，
    /// 否则随便建一个叫 <c>obs-studio</c> 的空目录就能让护栏放行。
    /// </summary>
    public static bool IsTrustedRoot(string? root)
    {
        var r = Trim(Resolve(root ?? ""));
        if (r.Length == 0) return false;
        if (IsDriveRoot(r) || IsSystemRoot(r)) return false;

        lock (TrustedGate)
        {
            if (TrustedRoots.Contains(r)) return true;
        }
        try
        {
            if (!string.Equals(new DirectoryInfo(r).Name, "obs-studio", StringComparison.OrdinalIgnoreCase))
                return false;
            return Directory.Exists(Path.Combine(r, "basic")) || File.Exists(Path.Combine(r, "global.ini"));
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 净化 <c>global.ini</c> 里读到的 <c>basic.profiledir</c>：只接受**单个目录名**。
    ///
    /// 为什么必须净化：这个值来自本机文件，而「导入备份包」可以整份写入 <c>global.ini</c>。
    /// 原样拼接时 <c>Path.Combine</c> 遇到根化段会丢弃前面的前缀（<c>C:\Windows\System32</c> → 写到系统目录），
    /// <c>..\..\..</c> 则会逃出配置目录。取 <see cref="Path.GetFileName(string)"/> 之后
    /// 只剩最后一段名字，两类逃逸都从源头消失。
    /// </summary>
    public static string? SafeProfileDir(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string name;
        try { name = Path.GetFileName(raw.Trim()); }
        catch (Exception) { return null; }
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (name is "." or "..") return null;
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        return name;
    }

    private static string Trim(string path)
        => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>在允许的 root 内才可删除。</summary>
    public static void AssertDeletable(string fullPath, string allowedRoot)
    {
        var root = Resolve(allowedRoot);
        var target = Resolve(fullPath);
        if (target.Length == 0)
            throw new ObsSafePathException(Strings.T("safepath.unresolvable", fullPath));

        // 闸 2：必须落在 root 之下
        if (!IsUnder(root, target))
            throw new ObsSafePathException(Strings.T("safepath.outOfRoot", fullPath));

        // 闸 3：root 必须是已登记的受信任 OBS 配置目录，且确属 OBS 配置
        if (!IsTrustedRoot(root))
            throw new ObsSafePathException(Strings.T("safepath.onlyObsStudio"));
        if (!Directory.Exists(Path.Combine(root, "basic")) &&
            !File.Exists(Path.Combine(root, "global.ini")))
            throw new ObsSafePathException(Strings.T("safepath.notObsConfig"));

        // 闸 4：不能是盘符根
        if (IsDriveRoot(target))
            throw new ObsSafePathException(Strings.T("safepath.driveRoot"));

        // 闸 5：不能是 root 自身
        if (string.Equals(target, root, StringComparison.OrdinalIgnoreCase))
            throw new ObsSafePathException(Strings.T("safepath.rootItself"));

        // 闸 6：名禁用集合 + 系统关键目录根
        var name = Path.GetFileName(target.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (ForbiddenSubdirNames.Contains(name))
            throw new ObsSafePathException(Strings.T("safepath.forbiddenDir", name));
        if (IsSystemRoot(target))
            throw new ObsSafePathException(Strings.T("safepath.systemDir"));

        // 闸 7：拒绝符号链接 / junction
        if (IsReparsePoint(target))
            throw new ObsSafePathException(Strings.T("safepath.reparsePoint"));
    }

    /// <summary>在允许的 root 内才可写入（用于导入落盘）。比删除更宽松：允许在 root 之下任意创建。</summary>
    public static void AssertWritable(string fullPath, string allowedRoot)
    {
        var root = Resolve(allowedRoot);
        var target = Resolve(fullPath);
        if (target.Length == 0)
            throw new ObsSafePathException(Strings.T("safepath.unresolvable", fullPath));

        if (!IsUnder(root, target))
            throw new ObsSafePathException(Strings.T("safepath.writeOutOfRoot", fullPath));

        // 闸 3：root 必须是已登记的受信任 OBS 配置目录
        if (!IsTrustedRoot(root))
            throw new ObsSafePathException(Strings.T("safepath.onlyWriteObsStudio"));

        // 闸 4（与删除侧对齐）：不能写盘符根
        if (IsDriveRoot(target))
            throw new ObsSafePathException(Strings.T("safepath.driveRoot"));

        if (IsSystemRoot(target))
            throw new ObsSafePathException(Strings.T("safepath.writeSystemDir"));

        if (IsReparsePoint(target))
            throw new ObsSafePathException(Strings.T("safepath.writeReparsePoint"));

        // 闸 5（V3.0 审查修正）：**已存在的祖先**不得是重解析点（junction / 符号链接）。
        //
        // 闸 4 上面那条查的是目标自身，而写目标在写入前必然不存在 —— 那个检查恒为假。
        // 真正能被利用的是中间层级的 junction：可信根之下挂一个指向别处的链接，
        // 写入就会越过边界落到根之外。这里只检查已存在的祖先（不存在的无从检查，也无从利用）。
        AssertNoReparseAncestors(fullPath);
    }

    /// <summary>
    /// 从目标路径向上逐级检查已存在的祖先目录是否为重解析点；发现即拒绝。
    /// 只向上走到盘符根，不越过（避免把系统级链接也算进来造成误伤）。
    /// </summary>
    private static void AssertNoReparseAncestors(string fullPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(fullPath);
            var guard = 0;
            while (!string.IsNullOrEmpty(dir) && guard++ < 64)
            {
                if (!Directory.Exists(dir)) { dir = Path.GetDirectoryName(dir); continue; }
                if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
                    throw new ObsSafePathException(Strings.T("safepath.reparseAncestor", dir));
                dir = Path.GetDirectoryName(dir);
            }
        }
        catch (ObsSafePathException)
        {
            throw;
        }
        catch (Exception)
        {
            // 路径形态异常（超长等）：按「检查不了就不放行」处理
            throw new ObsSafePathException(Strings.T("safepath.reparseAncestor", fullPath));
        }
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
