using System.IO;

namespace OBS_Helper.Wpf.Services.ObsConfig;

/// <summary>
/// 文件事务：把一系列「移动 / 创建」聚合成一个可回滚的操作。
///
/// 设计原则（与项目「永不硬删」铁律一致）：
/// <list type="bullet">
///   <item><b>StageMove</b>：先把源<b>复制</b>到事务专属的回收子目录（即恢复副本），再从原位删除。
///         回滚时把副本复制回原位并删掉副本；<b>恢复失败时副本必须保留</b>（见 <see cref="RollbackFailures"/>）。
///         提交后副本留在回收目录，用户可随时找回。</item>
///   <item><b>StageCreate</b>：直接向目标写字节；回滚时删除该文件。</item>
///   <item>跨卷时 <see cref="Directory.Move"/> 会抛 <see cref="IOException"/>，已降级为「递归复制 + 删除」。</item>
/// </list>
///
/// 用法：构造时传入回收根目录（通常是 <see cref="ObsPathService.TrashRoot"/>），提交成功即保留恢复副本；
/// 任一中间步骤失败则 <see cref="Rollback"/> 逆序还原。实现 <see cref="IDisposable"/>，未提交即销毁会自动回滚。
/// </summary>
public sealed class FileTx : IDisposable
{
    private readonly string _txDir;
    private readonly List<(string Src, string Trash)> _moves = new();
    private readonly List<string> _creates = new();
    private bool _finalized;

    /// <summary>
    /// 最近一次 <see cref="Rollback"/> 中「恢复失败」的原始路径列表（空表示全部恢复成功）。
    ///
    /// 这些路径的恢复副本**被刻意保留**在 <see cref="RecoveryPath"/> 里，等人工处理 ——
    /// 上层据此给用户明确提示，而不是让用户以为回滚成功了。
    /// </summary>
    public IReadOnlyList<string> RollbackFailures { get; private set; } = Array.Empty<string>();

    /// <summary>恢复副本是否仍保留在事务目录里（回滚未竟时为 true，此时不得删除该目录）。</summary>
    public bool HasRetainedRecoveryCopies => RollbackFailures.Count > 0;

    public FileTx(string trashBase)
    {
        _txDir = Path.Combine(trashBase, "tx_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(_txDir);

        // V3.0 审查修正（P2-4）：**构造时**就写「在途」标记。
        //
        // 原先只有「回滚失败」才写保留标记，于是「没标记」既可能是「已提交」也可能是
        // 「回滚未竟但标记没写成功 / 进程在中途被杀」—— 而后一种情况下
        // `CleanupTrash` 会把里面**唯一的原始副本**当作过期组硬删（与「永不硬删」直接冲突）。
        // 现在三态明确：在途（新目录）→ 提交后删除标记 / 回滚成功删除标记 / 回滚失败改写成保留标记。
        TryWriteMarker(InFlightMarkerFileName, "事务进行中：此时目录内的副本是本次操作的唯一备份。\n");
    }

    /// <summary>「事务进行中」标记：`CleanupTrash` 见到它一律跳过（不能把在途事务的备份清掉）。</summary>
    public const string InFlightMarkerFileName = ".obshelper-inflight";

    private void TryWriteMarker(string fileName, string content)
    {
        try
        {
            File.WriteAllText(Path.Combine(_txDir, fileName), content, new System.Text.UTF8Encoding(false));
        }
        catch (Exception)
        {
            // 标记写不出来不改变「副本已保留」这一事实；CleanupTrash 对读不到标记的目录也已按「保留」处理
        }
    }

    private void TryDeleteMarker(string fileName)
    {
        try { File.Delete(Path.Combine(_txDir, fileName)); }
        catch (Exception) { /* 删不掉只影响滚动清理，不影响数据 */ }
    }

    /// <summary>事务的回收 / 恢复目录（提交后即为恢复副本所在位置）。</summary>
    public string RecoveryPath => _txDir;

    /// <summary>把源（文件或目录）移入回收目录，原位删除。回滚时复原。</summary>
    public void StageMove(string src)
    {
        if (!File.Exists(src) && !Directory.Exists(src)) return;

        var trashDest = Path.Combine(_txDir, _moves.Count.ToString("D4") + "_" + Path.GetFileName(src.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
        Directory.CreateDirectory(Path.GetDirectoryName(trashDest)!);
        try
        {
            CopyItem(src, trashDest);
        }
        catch (Exception)
        {
            SafeDelete(trashDest);
            throw;
        }

        _moves.Add((src, trashDest));
        // V3.0（A4）：记下「副本 ← 原始路径」的对应关系。
        // 副本文件名只保留原名（加序号前缀），不含原目录 —— 没有这份记录，
        // 用户在回收站里看到的就只是一堆同名文件，无从知道该放回哪里。
        AppendManifest(src, Path.GetFileName(trashDest), isDirectory: Directory.Exists(trashDest));

        // 原位删除（恢复副本已留存）
        SafeDelete(src);
    }

    /// <summary>在目标路径写入字节。回滚时删除。</summary>
    public void StageCreate(string dst, byte[] data)
    {
        var parent = Path.GetDirectoryName(dst);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        File.WriteAllBytes(dst, data);
        _creates.Add(dst);
    }

    /// <summary>提交：保留回收目录中的恢复副本（不再回滚）。</summary>
    public void Commit()
    {
        _finalized = true;
        // 提交成功：事务不再「在途」，去掉标记即可被 `CleanupTrash` 正常滚动清理
        // （副本仍然保留在目录里，用户随时可找回 —— 见 TrashService）。
        TryDeleteMarker(InFlightMarkerFileName);
    }

    /// <summary>
    /// 回滚：逆序复原所有操作；**只有全部恢复成功**才清理事务目录。
    ///
    /// 为什么这么写：<see cref="StageMove"/> 已经把原文件从原位删掉了，事务目录里的副本就是**唯一**副本。
    /// 早期实现在恢复失败后仍然删副本，等于把用户唯一的配置一起删掉 —— 恢复失败必须保留副本并上报。
    /// </summary>
    public void Rollback()
    {
        if (_finalized) return;

        var failures = new List<string>();

        foreach (var (src, trash) in ((IEnumerable<(string, string)>)_moves).Reverse())
        {
            try
            {
                CopyItem(trash, src);
            }
            catch (Exception)
            {
                // 恢复失败：保留副本（下面不再删它），记进失败清单交给上层提示
                failures.Add(src);
                continue;
            }
            try { SafeDelete(trash); } catch (Exception) { /* 副本清理失败无害 */ }
        }

        foreach (var f in _creates)
        {
            // 同一事务里既 StageMove 又 StageCreate 的路径：**恢复优先**。
            // 否则会「先恢复、后删除」，把刚恢复的文件连同唯一的副本一起弄丢（审查发现的潜伏陷阱）。
            if (_moves.Any(m => string.Equals(m.Src, f, StringComparison.OrdinalIgnoreCase))) continue;
            try { SafeDelete(f); } catch (Exception) { }
        }

        RollbackFailures = failures;

        // 有任何一项没恢复成功，就保留整个事务目录（里面是恢复副本），绝不静默删除
        if (failures.Count == 0)
        {
            SafeDelete(_txDir);
        }
        else
        {
            // 标记从「在途」改成「保留」：`ObsPathService.CleanupTrash` 对两种标记都跳过，
            // 否则用户的最后一份配置会被下一次彻底重置顺手删掉（审查发现的缺陷）。
            TryDeleteMarker(InFlightMarkerFileName);
            TryWriteRetainedMarker();
        }

        // 让上层能把这个消息带给用户（未注入时只静默，行为与旧版一致）
        if (failures.Count > 0) RollbackFailureReporter?.Invoke(_txDir, failures.Count);

        _finalized = true;
    }

    /// <summary>
    /// 回滚失败的上报出口（由组合根在启动时接到 Toast / 日志上）。
    ///
    /// 为什么需要：副本保住了，但如果没有任何出口，用户只知道「操作失败了」，
    /// 不知道自己的配置还在 <c>trash\tx_xxxxxxxx</c> 里 —— 那等于没救回来。
    /// </summary>
    public static Action<string, int>? RollbackFailureReporter { get; set; }

    private void TryWriteRetainedMarker()
    {
        try
        {
            File.WriteAllText(Path.Combine(_txDir, RetainedMarkerFileName),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n保留原因：回滚时有 {RollbackFailures.Count} 项未能恢复。\n",
                new System.Text.UTF8Encoding(false));
        }
        catch (Exception)
        {
            // 标记写不出来不改变「副本已保留」这一事实
        }
    }

    /// <summary>「回滚未竟、副本必须保留」的标记文件名（<c>CleanupTrash</c> 据此跳过清理）。</summary>
    public const string RetainedMarkerFileName = ".obshelper-retained";

    /// <summary>「副本 ← 原始路径」的对应表文件名（回收站浏览与恢复据此工作）。</summary>
    public const string ManifestFileName = "restore-manifest.json";

    /// <summary>恢复副本中的一条记录。</summary>
    public sealed record RecoveryEntry(string OriginalPath, string CopyName, bool IsDirectory);

    /// <summary>
    /// 追加一条对应关系。失败只记日志：**记录失败不能反过来让清理动作失败**
    /// （此时副本已经复制完成，若在这里抛异常，调用方会以为整步没做）。
    /// </summary>
    private void AppendManifest(string originalPath, string copyName, bool isDirectory)
    {
        try
        {
            var entry = new RecoveryEntry(originalPath, copyName, isDirectory);
            File.AppendAllText(Path.Combine(_txDir, ManifestFileName),
                System.Text.Json.JsonSerializer.Serialize(entry) + "\n",
                new System.Text.UTF8Encoding(false));
        }
        catch (Exception)
        {
            // 记录失败不阻断（代价只是这一条无法一键恢复，副本仍在）
        }
    }

    /// <summary>
    /// 读取某个回收目录里的对应表（V3.0 / A4）。目录不存在或没有对应表时返回空列表 ——
    /// V3.0 之前的旧事务目录没有这个文件，界面要能优雅降级成「只列出副本」。
    ///
    /// V3.0 审查修正两点：
    /// <list type="number">
    ///   <item><b>逐行容错</b>：原先一个 try 包住整个循环，中间一行损坏会让其后的记录**全部静默丢失**，
    ///         界面却按「全部恢复成功」报告（用户以为还原了，其实没有）；</item>
    ///   <item><b>收口 <c>CopyName</c></b>：只接受「本事务目录内的相对文件名」。
    ///         <c>Path.Combine</c> 遇到根化片段会丢弃前缀，于是被篡改的对应表可以指向机器上任意路径 ——
    ///         既是任意读，也能把别处的内容写进配置目录。</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<RecoveryEntry> ReadManifest(string txDir)
    {
        var list = new List<RecoveryEntry>();
        try
        {
            var path = Path.Combine(txDir, ManifestFileName);
            if (!File.Exists(path)) return list;

            foreach (var line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = System.Text.Json.JsonSerializer.Deserialize<RecoveryEntry>(line);
                    if (entry is null || !IsSafeCopyName(entry.CopyName)) continue;
                    list.Add(entry);
                }
                catch (Exception)
                {
                    // 单行损坏（截断 / 半截 JSON）：跳过这一行，继续读后面的 —— 绝不整份丢弃
                }
            }
        }
        catch (Exception)
        {
            // 文件读不了按「无对应表」处理（界面退化为纯列表）
        }
        return list;
    }

    /// <summary>
    /// <c>CopyName</c> 是否只能是「本事务目录内的相对文件名」。
    ///
    /// 拒绝：空、根化路径（<c>C:\…</c> / <c>\\server\share</c> / <c>/etc</c>）、
    /// 带目录分隔符或 <c>..</c> 的路径、非法字符。
    /// 这一层是**读取侧**的收口；<see cref="TrashService"/> 在拼接时还会再校验一次解析结果。
    /// </summary>
    internal static bool IsSafeCopyName(string? copyName)
    {
        if (string.IsNullOrWhiteSpace(copyName)) return false;
        var name = copyName.Trim();
        if (name.Length != copyName.Length) return false;                 // 前后空格也算异常
        if (Path.IsPathRooted(name)) return false;
        if (name.Contains('/') || name.Contains('\\')) return false;
        if (name.Contains("..", StringComparison.Ordinal)) return false;
        return name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    public void Dispose()
    {
        if (!_finalized)
        {
            try { Rollback(); } catch (Exception) { }
        }
        // 已提交：_txDir 里的恢复副本是用户「找回」的唯一来源（UI 承诺「保留在回收站可恢复」），
        // 必须保留，由 ObsPathService.CleanupTrash 按保留组数限量清理。
    }

    // ------------------------------------------------------------ 内部工具

    /// <summary>
    /// 复制文件或目录（回滚恢复与移入回收都用它）。
    ///
    /// 递归时**跳过重解析点**（junction / 符号链接）：自引用的 junction 会让递归无限下去
    /// （<c>StackOverflowException</c> 不可捕获，进程直接死），而它发生在原文件已被移走之后
    /// —— 用户的配置就真的没了（V3.0 审查指出：原先第 7 闸查的是「目标自身是否链接」，
    /// 而写目标在写入前必然不存在，那个检查恒为假，防不住中间层级的链接）。
    /// </summary>
    private static void CopyItem(string src, string dst)
    {
        if (Directory.Exists(src))
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src))
            {
                if (IsReparsePoint(f)) continue;
                File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), overwrite: true);
            }
            foreach (var d in Directory.GetDirectories(src))
            {
                if (IsReparsePoint(d)) continue;
                CopyItem(d, Path.Combine(dst, Path.GetFileName(d)));
            }
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(src, dst, overwrite: true);
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (Exception) { return true; }   // 判断不了就跳过（宁可少拷一个，也不要递归进未知目标）
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            else if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception)
        {
            // 回收目录内的清理失败不应影响主流程；恢复副本多留一份无害
        }
    }
}
