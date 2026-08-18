using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace OBS_Helper.Wpf.Services.Update;

/// <summary>
/// 自举更新器（<c>OBS_Helper.exe --apply-update &lt;pendingDir&gt; &lt;parentPid&gt;</c>）。
///
/// 由增量更新流程在「应用即将重启」前拉起（安装版会先弹一次 UAC）。它本身仍是 OBS_Helper.exe，
/// 但处于无窗口模式：等待旧进程退出 → 用「重命名换位」技巧替换被占用的 exe / DLL →
/// 写入 DONE 标记 → 拉起新版本 → 退出。新版本启动时顺带清理残留（pending 目录 / *.old）。
///
/// 为什么必须重启自举而不是直接覆盖：运行中的进程会锁定自己的 exe 和已加载的 DLL，
/// 直接覆盖必然失败；先让旧进程退出、再改名换位，Windows 允许对「已锁定但改名」的文件操作。
/// </summary>
public static class UpdaterBootstrap
{
    /// <summary>自举模式命令行参数。</summary>
    public const string ArgFlag = "--apply-update";

    /// <summary>等待旧进程退出（含用户确认 UAC 的时间）的上限。</summary>
    private static readonly TimeSpan WaitParentTimeout = TimeSpan.FromSeconds(90);

    /// <summary>自举完成后在 pending 目录写入的标记文件名（内容为目标版本号）。</summary>
    public const string DoneMarker = "DONE";

    private const string ErrorMarker = "ERROR";

    /// <summary>
    /// 执行自举更新，返回进程退出码（0 = 成功）。此方法同步阻塞，不创建任何窗口。
    /// </summary>
    public static int Run(string[] args)
    {
        if (args.Length < 3)
        {
            FileLogger.Error("Updater", "自举参数不足，退出。");
            return 1;
        }

        var pendingDir = args[1];
        var parentPid = int.TryParse(args[2], out var pid) ? pid : 0;
        var manifestPath = Path.Combine(pendingDir, "update_manifest.json");
        var filesDir = Path.Combine(pendingDir, "files");
        var appDir = AppContext.BaseDirectory;
        var selfExe = Process.GetCurrentProcess().MainModule?.FileName ?? Path.Combine(appDir, "OBS_Helper.exe");
        var selfOld = selfExe + ".old";

        FileLogger.Info("Updater", $"自举更新开始：pending={pendingDir} parentPid={parentPid}");

        try
        {
            // 1) 等旧进程退出（释放文件锁）。用户可能在看 UAC 弹窗，耐心等。
            if (parentPid > 0)
            {
                try
                {
                    using var parent = Process.GetProcessById(parentPid);
                    if (!parent.WaitForExit((int)WaitParentTimeout.TotalMilliseconds))
                    {
                        FileLogger.Warn("Updater", "等待旧进程退出超时，仍继续尝试替换（可能失败于占用文件）。");
                    }
                }
                catch (ArgumentException)
                {
                    // 旧进程已退出（GetProcessById 抛异常），正是期望状态
                }
                catch (Exception ex)
                {
                    FileLogger.Warn("Updater", "等待旧进程退出异常：" + ex.Message);
                }
            }

            // 2) 读取清单
            if (!File.Exists(manifestPath))
            {
                FileLogger.Error("Updater", "pending 目录缺少 update_manifest.json，放弃。");
                return 1;
            }
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(
                File.ReadAllText(manifestPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (manifest is null || manifest.Format != 1)
            {
                FileLogger.Error("Updater", "清单解析失败，放弃。");
                return 1;
            }

            // 3) 先把自己的 exe 改名换位（本进程锁定着自己），再复制新 exe 进来
            try
            {
                File.Delete(selfOld);
                File.Move(selfExe, selfOld, overwrite: true);
            }
            catch (Exception ex)
            {
                FileLogger.Error("Updater", "自身 exe 改名失败：" + ex.Message);
                return 1;
            }

            // 4) 逐个复制新文件；被锁定的旧文件先改名再复制（Windows 允许重命名已加载的 DLL）
            var copied = 0;
            foreach (var entry in manifest.Files)
            {
                try
                {
                    var rel = IncrementalUpdateService.ToLocalPath(entry.Path);
                    var src = Path.Combine(filesDir, rel);
                    var dst = Path.Combine(appDir, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);

                    CopyWithRenameSwap(src, dst);
                    copied++;
                }
                catch (Exception ex)
                {
                    FileLogger.Error("Updater", $"复制 {entry.Path} 失败：" + ex.Message);
                    TryRestoreSelf(selfExe, selfOld);
                    return 1;
                }
            }

            // 5) 删除应移除的旧文件（尽力而为，失败不致命）
            foreach (var rel in manifest.Remove)
            {
                try
                {
                    var dst = Path.Combine(appDir, IncrementalUpdateService.ToLocalPath(rel));
                    if (File.Exists(dst)) File.Delete(dst);
                }
                catch (Exception)
                {
                    // 被占用等：留下，新版本启动时忽略即可
                }
            }

            // 6) 写 DONE 标记 → 新版本启动时据此清理 pending 目录
            try
            {
                File.WriteAllText(Path.Combine(pendingDir, DoneMarker), manifest.TargetVersion);
            }
            catch (Exception)
            {
                // 标记写失败不致命（新版本启动时也会尝试清理）
            }

            FileLogger.Info("Updater", $"自举完成：{copied}/{manifest.Files.Count} 个文件已就位，拉起新版本。");

            // 7) 拉起新版本（正常模式）后退出。新版本启动时清理 *.old 与 pending。
            try
            {
                Process.Start(new ProcessStartInfo(selfExe) { UseShellExecute = true, WorkingDirectory = appDir });
            }
            catch (Exception ex)
            {
                FileLogger.Error("Updater", "拉起新版本失败：" + ex.Message);
                TryRestoreSelf(selfExe, selfOld);
                return 1;
            }

            return 0;
        }
        catch (Exception ex)
        {
            FileLogger.Error("Updater", "自举更新异常：" + ex);
            TryRestoreSelf(selfExe, selfOld);
            return 1;
        }
        finally
        {
            FileLogger.Flush();
        }
    }

    /// <summary>复制源文件到目标；目标被占用（IOException）时先改名换位再复制。</summary>
    private static void CopyWithRenameSwap(string src, string dst)
    {
        try
        {
            File.Copy(src, dst, overwrite: true);
        }
        catch (IOException)
        {
            var old = dst + ".old";
            try { File.Delete(old); } catch (Exception) { }
            File.Move(dst, old, overwrite: true);
            File.Copy(src, dst, overwrite: true);
        }
    }

    /// <summary>应用失败时尽量把旧 exe 换回来，保证应用仍可启动。</summary>
    private static void TryRestoreSelf(string selfExe, string selfOld)
    {
        try
        {
            if (File.Exists(selfOld) && !File.Exists(selfExe))
            {
                File.Move(selfOld, selfExe, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Updater", "恢复旧 exe 失败：" + ex.Message);
        }
    }

    /// <summary>
    /// 新版本正常启动时的残留清理：删除更新遗留的 pending 目录（含 DONE 标记时）
    /// 与本目录下的 *.old（上次自举的改名换位残留）。尽力而为，失败不影响启动。
    /// </summary>
    public static void CleanupResidue()
    {
        try
        {
            var pending = IncrementalUpdateService.PendingDir;
            if (Directory.Exists(pending) && File.Exists(Path.Combine(pending, DoneMarker)))
            {
                Directory.Delete(pending, recursive: true);
                FileLogger.Info("Updater", "已清理更新暂存目录。");
            }
        }
        catch (Exception)
        {
            // 个别文件仍被占用（自举进程刚退出）：下次启动再试
        }

        try
        {
            var appDir = AppContext.BaseDirectory;
            foreach (var f in Directory.GetFiles(appDir, "*.old"))
            {
                try { File.Delete(f); } catch (Exception) { }
            }
        }
        catch (Exception)
        {
            // 目录枚举失败等：忽略
        }
    }
}
