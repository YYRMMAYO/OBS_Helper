using OBS_Helper.Wpf.Services.ObsConfig;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// 文件事务（<see cref="FileTx"/>）的回归测试，重点是 V3.0 修掉的那条数据丢失缺陷：
/// <b>回滚时若恢复失败，绝不能删除事务目录里那份唯一的恢复副本</b>。
/// </summary>
public class FileTxTests : IDisposable
{
    private readonly string _root;

    public FileTxTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "obshelper_filetx_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (Exception) { }
    }

    private string TrashBase => Path.Combine(_root, "trash");

    // ---------------------------------------------------------------- 恢复对应表（V3.0 / A4）

    /// <summary>
    /// 移动时必须记下「副本 ← 原始路径」的对应关系。
    /// 没有这份记录，用户在回收站里看到的就只是一堆同名文件，无从知道该放回哪里。
    /// </summary>
    [Fact]
    public void StageMove_WritesRestoreManifest()
    {
        var src = Path.Combine(_root, "basic.ini");
        File.WriteAllText(src, "内容");

        using var tx = new FileTx(TrashBase);
        tx.StageMove(src);

        var entries = FileTx.ReadManifest(tx.RecoveryPath);
        var entry = Assert.Single(entries);
        Assert.Equal(src, entry.OriginalPath);
        Assert.False(entry.IsDirectory);
        Assert.True(File.Exists(Path.Combine(tx.RecoveryPath, entry.CopyName)));
    }

    /// <summary>目录同样要记录（profile 目录整体移开是常见路径）。</summary>
    [Fact]
    public void StageMove_Directory_IsRecordedAsDirectory()
    {
        var dir = Path.Combine(_root, "profiles", "未命名");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "basic.ini"), "x");

        using var tx = new FileTx(TrashBase);
        tx.StageMove(dir);

        var entry = Assert.Single(FileTx.ReadManifest(tx.RecoveryPath));
        Assert.True(entry.IsDirectory);
        Assert.Equal(dir, entry.OriginalPath);
        Assert.True(Directory.Exists(Path.Combine(tx.RecoveryPath, entry.CopyName)));
    }

    /// <summary>多次移动要累积多条记录（追加而不是覆盖）。</summary>
    [Fact]
    public void StageMove_MultipleFiles_AppendsEntries()
    {
        var a = Path.Combine(_root, "a.ini");
        var b = Path.Combine(_root, "b.ini");
        File.WriteAllText(a, "a");
        File.WriteAllText(b, "b");

        using var tx = new FileTx(TrashBase);
        tx.StageMove(a);
        tx.StageMove(b);

        var entries = FileTx.ReadManifest(tx.RecoveryPath);
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.OriginalPath == a);
        Assert.Contains(entries, e => e.OriginalPath == b);
    }

    /// <summary>
    /// 没有对应表的旧事务目录（V3.0 之前产生的）必须优雅降级成空列表 ——
    /// 界面据此退回「只列出副本 + 打开文件夹」，而不是报错。
    /// </summary>
    [Fact]
    public void ReadManifest_WithoutManifestFile_ReturnsEmpty()
    {
        var dir = Path.Combine(_root, "tx_old");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "0000_basic.ini"), "x");

        Assert.Empty(FileTx.ReadManifest(dir));
    }

    /// <summary>对应表损坏（半截 JSON）时不能抛异常，能读出的部分照常返回。</summary>
    [Fact]
    public void ReadManifest_WithCorruptLine_IgnoresBadLine()
    {
        var src = Path.Combine(_root, "c.ini");
        File.WriteAllText(src, "c");

        using var tx = new FileTx(TrashBase);
        tx.StageMove(src);
        File.AppendAllText(Path.Combine(tx.RecoveryPath, FileTx.ManifestFileName), "{ 半截 JSON\n");

        var entry = Assert.Single(FileTx.ReadManifest(tx.RecoveryPath));
        Assert.Equal(src, entry.OriginalPath);
    }

    /// <summary>
    /// **中间**一行损坏时，其后的记录必须仍然读得出来。
    ///
    /// 审查实测过的缺陷：原先一个 try 包住整个循环，坏行之后全部静默丢失，
    /// 界面却按「全部恢复成功」报告 —— 用户以为还原了，其实没有。
    /// </summary>
    [Fact]
    public void ReadManifest_CorruptMiddleLine_KeepsLaterEntries()
    {
        var a = Path.Combine(_root, "a.ini");
        var b = Path.Combine(_root, "b.ini");
        var c = Path.Combine(_root, "c.ini");
        foreach (var f in new[] { a, b, c }) File.WriteAllText(f, "x");

        using var tx = new FileTx(TrashBase);
        tx.StageMove(a);
        tx.StageMove(b);
        tx.StageMove(c);

        // 把中间一行替换成半截 JSON
        var manifest = Path.Combine(tx.RecoveryPath, FileTx.ManifestFileName);
        var lines = File.ReadAllLines(manifest);
        Assert.Equal(3, lines.Length);
        lines[1] = "{ 半截 JSON";
        File.WriteAllLines(manifest, lines);

        var entries = FileTx.ReadManifest(tx.RecoveryPath);
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.OriginalPath == a);
        Assert.Contains(entries, e => e.OriginalPath == c);   // ← 坏行之后的记录必须还在
    }

    /// <summary>
    /// 对应表里的 <c>CopyName</c> 只允许「本事务目录内的相对文件名」。
    ///
    /// 审查实测过的缺陷：<c>Path.Combine</c> 遇到根化片段会丢弃前缀，被篡改的对应表
    /// 于是可以指向机器上任意路径 —— 既是任意读，也能把别处内容写进配置目录。
    /// </summary>
    [Theory]
    [InlineData(@"C:\Windows\win.ini", false)]
    [InlineData(@"\\server\share\x", false)]
    [InlineData("/etc/passwd", false)]
    [InlineData(@"..\..\secret.txt", false)]
    [InlineData(@"sub\x.ini", false)]
    [InlineData("0000_basic.ini", true)]
    [InlineData("0001_profiles", true)]
    [InlineData("", false)]
    [InlineData(" 0000_basic.ini", false)]
    public void IsSafeCopyName_OnlyAcceptsPlainRelativeNames(string copyName, bool expected)
        => Assert.Equal(expected, FileTx.IsSafeCopyName(copyName));

    /// <summary>对应表被篡改（含逃逸路径）时，读取阶段就要把它丢掉。</summary>
    [Fact]
    public void ReadManifest_DropsEntriesWithEscapingCopyName()
    {
        using var tx = new FileTx(TrashBase);
        var manifest = Path.Combine(tx.RecoveryPath, FileTx.ManifestFileName);
        File.WriteAllText(manifest,
            "{\"OriginalPath\":\"C:\\\\cfg\\\\basic\\\\a.ini\",\"CopyName\":\"C:\\\\Windows\\\\win.ini\",\"IsDirectory\":false}\n" +
            "{\"OriginalPath\":\"C:\\\\cfg\\\\basic\\\\b.ini\",\"CopyName\":\"0001_b.ini\",\"IsDirectory\":false}\n",
            new System.Text.UTF8Encoding(false));

        var entry = Assert.Single(FileTx.ReadManifest(tx.RecoveryPath));
        Assert.Equal("0001_b.ini", entry.CopyName);
    }

    /// <summary>事务目录在提交前必须带「在途」标记，避免被滚动清理误删（唯一副本）。</summary>
    [Fact]
    public void NewTransaction_HasInFlightMarker_RemovedOnCommit()
    {
        var src = Path.Combine(_root, "live.ini");
        File.WriteAllText(src, "x");

        using var tx = new FileTx(TrashBase);
        Assert.True(File.Exists(Path.Combine(tx.RecoveryPath, FileTx.InFlightMarkerFileName)));

        tx.StageMove(src);
        tx.Commit();
        Assert.False(File.Exists(Path.Combine(tx.RecoveryPath, FileTx.InFlightMarkerFileName)));
    }

    /// <summary>事务目录里的「恢复副本」文件（排除 .retained 标记与 restore-manifest.json 这类元数据）。</summary>
    private static string[] RecoveryCopies(string dir)
        => Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
            .Where(f => !IsMetadata(Path.GetFileName(f)))
            .ToArray();

    private static bool IsMetadata(string name)
        => string.Equals(name, FileTx.RetainedMarkerFileName, StringComparison.OrdinalIgnoreCase)
           || string.Equals(name, FileTx.InFlightMarkerFileName, StringComparison.OrdinalIgnoreCase)
           || string.Equals(name, FileTx.ManifestFileName, StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void StageMove_RemovesSourceAndKeepsRecoveryCopy()
    {
        var src = Path.Combine(_root, "global.ini");
        File.WriteAllText(src, "原始内容");

        using var tx = new FileTx(TrashBase);
        tx.StageMove(src);

        Assert.False(File.Exists(src));
        var copies = RecoveryCopies(tx.RecoveryPath);
        Assert.Single(copies);
        Assert.Equal("原始内容", File.ReadAllText(copies[0]));
    }

    [Fact]
    public void Rollback_RestoresContentAndCleansUpWhenSuccessful()
    {
        var src = Path.Combine(_root, "global.ini");
        File.WriteAllText(src, "原始内容");

        var tx = new FileTx(TrashBase);
        tx.StageMove(src);
        tx.Rollback();

        Assert.True(File.Exists(src));
        Assert.Equal("原始内容", File.ReadAllText(src));
        Assert.Empty(tx.RollbackFailures);
        Assert.False(tx.HasRetainedRecoveryCopies);
        Assert.False(Directory.Exists(tx.RecoveryPath));   // 全部恢复成功才清理事务目录
    }

    /// <summary>
    /// 这条是 V3.0 的核心回归：制造一次必然失败的恢复，断言
    /// ① 失败被如实记入 <see cref="FileTx.RollbackFailures"/>；
    /// ② 恢复副本仍然存在（文件内容可读）；
    /// ③ 事务目录未被删除。
    ///
    /// 制造失败的办法：StageMove 之后把原路径**建成一个目录** ——
    /// 把文件复制到一个已存在的目录路径上必然抛异常（Windows 上不允许以文件覆盖目录）。
    /// </summary>
    [Fact]
    public void Rollback_WhenRestoreFails_KeepsRecoveryCopyAndReportsFailure()
    {
        var src = Path.Combine(_root, "global.ini");
        File.WriteAllText(src, "唯一的配置内容");

        var tx = new FileTx(TrashBase);
        tx.StageMove(src);
        Directory.CreateDirectory(src);   // 让「复制回原位」必然失败

        tx.Rollback();

        Assert.Single(tx.RollbackFailures);
        Assert.Equal(src, tx.RollbackFailures[0]);
        Assert.True(tx.HasRetainedRecoveryCopies);

        // 恢复副本必须还在，且内容完整 —— 这就是用户的最后一份配置
        Assert.True(Directory.Exists(tx.RecoveryPath));
        var copies = RecoveryCopies(tx.RecoveryPath);
        Assert.Single(copies);
        Assert.Equal("唯一的配置内容", File.ReadAllText(copies[0]));
        // 并且打了「保留」标记：CleanupTrash 会跳过带标记的目录，不会把它当过期组清掉
        Assert.True(File.Exists(Path.Combine(tx.RecoveryPath, FileTx.RetainedMarkerFileName)));
    }

    /// <summary>部分失败：失败的那一项保留副本，成功的那一项照常还回去并清掉副本。</summary>
    [Fact]
    public void Rollback_PartialFailure_KeepsOnlyFailedCopy()
    {
        var okSrc = Path.Combine(_root, "a.ini");
        var badSrc = Path.Combine(_root, "b.ini");
        File.WriteAllText(okSrc, "A");
        File.WriteAllText(badSrc, "B");

        var tx = new FileTx(TrashBase);
        tx.StageMove(okSrc);
        tx.StageMove(badSrc);
        Directory.CreateDirectory(badSrc);   // 只让 b 的恢复失败

        tx.Rollback();

        Assert.Equal("A", File.ReadAllText(okSrc));
        Assert.Single(tx.RollbackFailures);
        Assert.Equal(badSrc, tx.RollbackFailures[0]);

        var copies = RecoveryCopies(tx.RecoveryPath);
        Assert.Single(copies);
        Assert.Equal("B", File.ReadAllText(copies[0]));
    }

    /// <summary>未提交就 Dispose（例如 using 中途抛异常）等价于回滚，同样不能删掉恢复副本。</summary>
    [Fact]
    public void Dispose_WithoutCommit_RollsBackAndKeepsFailedCopies()
    {
        var src = Path.Combine(_root, "global.ini");
        File.WriteAllText(src, "内容");

        FileTx tx;
        using (tx = new FileTx(TrashBase))
        {
            tx.StageMove(src);
            Directory.CreateDirectory(src);
        }   // Dispose → Rollback

        Assert.Single(tx.RollbackFailures);
        Assert.True(Directory.Exists(tx.RecoveryPath));
    }

    /// <summary>提交后恢复副本按设计保留（提交即「用户可随时找回」的语义）。</summary>
    [Fact]
    public void Commit_KeepsRecoveryCopyForManualRecovery()
    {
        var src = Path.Combine(_root, "global.ini");
        File.WriteAllText(src, "内容");

        var tx = new FileTx(TrashBase);
        tx.StageMove(src);
        tx.Commit();

        Assert.False(File.Exists(src));
        Assert.True(Directory.Exists(tx.RecoveryPath));
        Assert.Empty(tx.RollbackFailures);
    }

    [Fact]
    public void StageMove_MissingSource_IsNoOp()
    {
        var tx = new FileTx(TrashBase);
        tx.StageMove(Path.Combine(_root, "does-not-exist.ini"));
        tx.Rollback();
        Assert.Empty(tx.RollbackFailures);
    }

    [Fact]
    public void StageCreate_WritesFileAndRollbackDeletesIt()
    {
        var dst = Path.Combine(_root, "new.ini");
        var tx = new FileTx(TrashBase);
        tx.StageCreate(dst, System.Text.Encoding.UTF8.GetBytes("新文件"));
        Assert.True(File.Exists(dst));

        tx.Rollback();
        Assert.False(File.Exists(dst));
    }
}
