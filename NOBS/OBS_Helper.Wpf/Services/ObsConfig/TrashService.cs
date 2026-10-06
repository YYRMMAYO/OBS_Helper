using System.IO;
using System.Text;

namespace OBS_Helper.Wpf.Services.ObsConfig;

/// <summary>回收站里的一组事务目录（每个目录来自一次 <see cref="FileTx"/>）。</summary>
public sealed record TrashGroup(
    /// <summary>事务目录的完整路径。</summary>
    string Dir,
    /// <summary>创建时间（本地时间，供界面显示）。</summary>
    DateTime CreatedLocal,
    /// <summary>副本文件数。</summary>
    int FileCount,
    /// <summary>副本总字节数。</summary>
    long TotalBytes,
    /// <summary>是否打了「回滚未竟、不得清理」的标记。</summary>
    bool Retained,
    /// <summary>标记文件里的说明（无标记时为 null）。</summary>
    string? Note,
    /// <summary>「副本 ← 原始路径」对应表；旧事务目录没有该文件时为空。</summary>
    IReadOnlyList<FileTx.RecoveryEntry> Entries)
{
    /// <summary>
    /// 能否一键恢复。
    ///
    /// 口径要诚实（审查指出）：这只表示「对应表里还有至少一条可用于恢复的记录」，
    /// **不保证每一条副本都还在** —— 丢失的那些会在恢复结果里逐条报告为失败。
    /// </summary>
    public bool CanRestore => Entries.Count > 0;
}

/// <summary>恢复结果。</summary>
public sealed record TrashRestoreResult(
    /// <summary>恢复到原位的项数。</summary>
    int Restored,
    /// <summary>原位已存在、改放旁边的项数。</summary>
    int PlacedAside,
    /// <summary>失败的项数。</summary>
    int Failed,
    /// <summary>给用户看的逐条说明（成功与原位改放也记，方便核对）。</summary>
    IReadOnlyList<string> Messages);

/// <summary>
/// 配置回收站的读取与恢复（V3.0 / A4）。
///
/// 为什么需要它：本产品对 OBS 配置「永不硬删」—— 平台内每一次移动/覆盖都会在
/// <c>%LocalAppData%\OBS_Helper\trash\tx_*</c> 留一份恢复副本。但在此之前，
/// 这些副本**只有写没有读**：用户在界面上根本看不到它们，出了事也无从下手。
/// 这个服务把回收站变成用户真的能用的东西：看得到、认得出（原始路径）、能放回去。
///
/// 安全口径（V3.0 审查后收紧）：
/// <list type="bullet">
///   <item>恢复**绝不覆盖**已存在的文件 —— 原位有新文件时改放到旁边并如实报告；</item>
///   <item>落点必须在本机**当前真实的** OBS 配置目录之下。不再「从对应表里的原始路径反推配置根」——
///         那等于让落点自己给自己发通行证：篡改对应表就能把内容写到任意叫 <c>obs-studio</c> 的目录里；</item>
///   <item>副本路径必须真的在事务目录内（对应表可能被篡改，见 <see cref="FileTx.IsSafeCopyName"/>）；</item>
///   <item>恢复成功的条目会从对应表里**销账**，因此重复点击不会不断产生 <c>.restoredN</c>；</item>
///   <item>本服务**不提供**「永久删除」——回收站只由 <see cref="ObsPathService.CleanupTrash"/> 按组数滚动清理，
///         且带在途/保留标记的目录永不被清理。</item>
/// </list>
/// </summary>
public sealed class TrashService
{
    private readonly ObsPathService _paths;

    public TrashService(ObsPathService paths) => _paths = paths;

    /// <summary>列出全部事务目录，最新的在前。</summary>
    public IReadOnlyList<TrashGroup> List()
    {
        var list = new List<TrashGroup>();
        try
        {
            var root = ObsPathService.TrashRoot;
            if (!Directory.Exists(root)) return list;

            foreach (var dir in Directory.GetDirectories(root))
            {
                try
                {
                    var info = new DirectoryInfo(dir);
                    var files = info.GetFiles("*", SearchOption.AllDirectories);
                    var retainedPath = Path.Combine(dir, FileTx.RetainedMarkerFileName);
                    var retained = File.Exists(retainedPath);

                    list.Add(new TrashGroup(
                        Dir: dir,
                        CreatedLocal: info.CreationTime,
                        // 只把「事务目录根下」的标记/对应表算作元数据：递归里恰好同名的**真副本**
                        // 不该被漏计（审查实测：子目录里一个叫 restore-manifest.json 的副本被当成元数据）
                        FileCount: files.Count(f => !IsRootMetadata(dir, f.FullName)),
                        TotalBytes: files.Where(f => !IsRootMetadata(dir, f.FullName)).Sum(f => f.Length),
                        Retained: retained,
                        Note: retained ? ReadNote(retainedPath) : null,
                        Entries: FileTx.ReadManifest(dir)));
                }
                catch (Exception)
                {
                    // 单个目录读不了（被占用等）不影响列出其它组
                }
            }
        }
        catch (Exception)
        {
            // 回收站不可读时返回空列表：这是「查看历史」功能，不能因此让页面报错
        }

        return list.OrderByDescending(g => g.CreatedLocal).ToList();
    }

    /// <summary>
    /// 把一组副本恢复回原路径。
    ///
    /// 逐条独立处理：一条失败不影响其它条（部分恢复也比全不恢复强），并逐条给出说明。
    /// 成功的条目会从对应表销账，避免重复点击产生一堆 <c>.restoredN</c>。
    /// </summary>
    public async Task<TrashRestoreResult> RestoreAsync(TrashGroup group)
    {
        var messages = new List<string>();
        var done = new List<string>();
        int restored = 0, placedAside = 0, failed = 0;

        // 落点护栏用的是**本机当前真实**的配置目录，而不是对应表里写的路径
        string? realRoot = null;
        try
        {
            var loc = await _paths.LocateAsync().ConfigureAwait(false);
            if (loc.Exists) realRoot = loc.ConfigDir;
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Trash", $"定位 OBS 配置目录失败，无法恢复：{ex.Message}");
        }

        if (string.IsNullOrEmpty(realRoot))
        {
            return new TrashRestoreResult(0, 0, group.Entries.Count,
                new[] { Strings.T("trash.restore.noConfigRoot") });
        }

        foreach (var entry in group.Entries)
        {
            // 双保险：即使对应表被改过，副本路径也必须真的落在事务目录内
            if (!FileTx.IsSafeCopyName(entry.CopyName))
            {
                failed++;
                messages.Add(Strings.T("trash.restore.badCopyName", entry.CopyName));
                continue;
            }

            var copy = Path.Combine(group.Dir, entry.CopyName);
            var exists = entry.IsDirectory ? Directory.Exists(copy) : File.Exists(copy);
            if (!exists)
            {
                failed++;
                messages.Add(Strings.T("trash.restore.copyMissing", entry.OriginalPath));
                continue;
            }

            try
            {
                if (!IsAllowedRestoreTarget(realRoot, entry.OriginalPath))
                {
                    failed++;
                    messages.Add(Strings.T("trash.restore.outsideConfig", entry.OriginalPath, realRoot));
                    continue;
                }

                var originalExists = File.Exists(entry.OriginalPath) || Directory.Exists(entry.OriginalPath);
                var dest = originalExists ? AsidePathFor(entry.OriginalPath) : entry.OriginalPath;

                ObsSafePath.AssertWritable(dest, realRoot);
                var parent = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

                if (entry.IsDirectory) CopyDirectory(copy, dest);
                else File.Copy(copy, dest, overwrite: false);

                done.Add(entry.CopyName);
                if (originalExists)
                {
                    placedAside++;
                    messages.Add(Strings.T("trash.restore.placedAside", entry.OriginalPath, dest));
                }
                else
                {
                    restored++;
                    messages.Add(Strings.T("trash.restore.restored", entry.OriginalPath));
                }
            }
            catch (Exception ex)
            {
                failed++;
                messages.Add(Strings.T("trash.restore.failed", entry.OriginalPath, ex.Message));
            }
        }

        // 销账：把已恢复的条目从对应表移除（失败/未处理的留在表里，下次还能再试）
        if (done.Count > 0) RewriteManifestWithout(group.Dir, done);

        return new TrashRestoreResult(restored, placedAside, failed, messages);
    }

    /// <summary>
    /// 恢复落点是否在真实配置目录的合法区域内（<c>basic\**</c> / <c>plugin_config\**</c> /
    /// <c>global.ini</c> / <c>user.ini</c>）。
    /// </summary>
    private static bool IsAllowedRestoreTarget(string configRoot, string target)
    {
        try
        {
            var full = Path.GetFullPath(target);
            var root = Path.GetFullPath(configRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (Under(Path.Combine(root, "basic"), full)) return true;
            if (Under(Path.Combine(root, "plugin_config"), full)) return true;
            if (string.Equals(full, Path.Combine(root, "global.ini"), StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(full, Path.Combine(root, "user.ini"), StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>target 是否等于 root 或位于其下（按完整路径比较，防 <c>..</c> 与大小写差异）。</summary>
    private static bool Under(string root, string target)
    {
        var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var t = Path.GetFullPath(target);
        return t.Equals(r, StringComparison.OrdinalIgnoreCase)
            || t.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void RewriteManifestWithout(string txDir, IReadOnlyCollection<string> restoredCopyNames)
    {
        try
        {
            var path = Path.Combine(txDir, FileTx.ManifestFileName);
            if (!File.Exists(path)) return;

            var set = restoredCopyNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var remaining = FileTx.ReadManifest(txDir).Where(e => !set.Contains(e.CopyName)).ToList();

            if (remaining.Count == 0)
            {
                // 全部恢复完成：目录里只剩已经放回去的副本，清掉对应表让界面显示「无对应表」，
                // 同时失效化（不再提供「放回原位」）。副本本身仍保留（永不硬删）。
                File.Delete(path);
                return;
            }

            var sb = new StringBuilder();
            foreach (var e in remaining)
                sb.AppendLine(System.Text.Json.JsonSerializer.Serialize(e));
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Trash", $"更新恢复对应表失败（重复点击可能产生额外副本）：{ex.Message}");
        }
    }

    /// <summary>只有事务目录**根下**的这两个文件名算元数据（递归里同名的真副本不算）。</summary>
    private static bool IsRootMetadata(string txDir, string filePath)
    {
        try
        {
            if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(filePath)),
                    Path.GetFullPath(txDir).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                return false;

            var name = Path.GetFileName(filePath);
            return string.Equals(name, FileTx.RetainedMarkerFileName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, FileTx.InFlightMarkerFileName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, FileTx.ManifestFileName, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string? ReadNote(string retainedPath)
    {
        try { return File.ReadAllText(retainedPath).Trim(); }
        catch (Exception) { return null; }
    }

    /// <summary>原位已有文件时的落点：<c>名字.restored</c>，必要时再加序号。</summary>
    private static string AsidePathFor(string original)
    {
        var dir = Path.GetDirectoryName(original) ?? "";
        var name = Path.GetFileName(original);
        var candidate = Path.Combine(dir, name + ".restored");
        var n = 2;
        while (File.Exists(candidate) || Directory.Exists(candidate))
            candidate = Path.Combine(dir, $"{name}.restored{n++}");
        return candidate;
    }

    /// <summary>
    /// 递归复制。**跳过重解析点（junction / 符号链接）**：这类条目指向目录树之外，
    /// 自引用的 junction 会让递归无限下去（<c>StackOverflowException</c> 不可捕获，进程直接死），
    /// 跨目录的则会把外部内容整棵搬进配置目录（审查指出）。
    /// </summary>
    private static void CopyDirectory(string src, string dest)
    {
        Directory.CreateDirectory(dest);

        foreach (var file in Directory.GetFiles(src))
        {
            if (IsReparsePoint(file)) continue;
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: false);
        }

        foreach (var sub in Directory.GetDirectories(src))
        {
            if (IsReparsePoint(sub)) continue;
            CopyDirectory(sub, Path.Combine(dest, Path.GetFileName(sub)));
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (Exception) { return true; }   // 判断不了就跳过（宁可少恢复，也不要递归进未知目标）
    }

    /// <summary>把某一组所在位置用资源管理器打开（不想冒险手动恢复时看个明白）。</summary>
    public static void OpenInExplorer(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{dir}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Trash", $"打开回收站目录失败：{ex.Message}");
        }
    }
}
