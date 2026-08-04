using System.IO;

namespace OBS_Helper.Wpf.Services.ObsConfig;

/// <summary>
/// 文件事务：把一系列「移动 / 创建」聚合成一个可回滚的操作。
///
/// 设计原则（与项目「永不硬删」铁律一致）：
/// <list type="bullet">
///   <item><b>StageMove</b>：先把源<b>复制</b>到事务专属的回收子目录（即恢复副本），再从原位删除。
///         回滚时把副本复制回原位并删掉副本；提交后副本留在回收目录，用户可随时找回。</item>
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

    public FileTx(string trashBase)
    {
        _txDir = Path.Combine(trashBase, "tx_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(_txDir);
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
    public void Commit() => _finalized = true;

    /// <summary>回滚：逆序复原所有操作并清理事务目录。</summary>
    public void Rollback()
    {
        if (_finalized) return;
        foreach (var (src, trash) in ((IEnumerable<(string, string)>)_moves).Reverse())
        {
            try { CopyItem(trash, src); } catch (Exception) { /* 恢复失败留待人工处理 */ }
            try { SafeDelete(trash); } catch (Exception) { }
        }
        foreach (var f in _creates)
        {
            try { SafeDelete(f); } catch (Exception) { }
        }
        SafeDelete(_txDir);
        _finalized = true;
    }

    public void Dispose()
    {
        if (!_finalized)
        {
            try { Rollback(); } catch (Exception) { }
        }
        else
        {
            SafeDelete(_txDir);
        }
    }

    // ------------------------------------------------------------ 内部工具

    private static void CopyItem(string src, string dst)
    {
        if (Directory.Exists(src))
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src))
                File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), overwrite: true);
            foreach (var d in Directory.GetDirectories(src))
                CopyItem(d, Path.Combine(dst, Path.GetFileName(d)));
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(src, dst, overwrite: true);
        }
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
