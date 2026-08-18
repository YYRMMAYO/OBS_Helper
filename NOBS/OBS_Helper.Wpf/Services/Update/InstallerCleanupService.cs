using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace OBS_Helper.Wpf.Services.Update;

/// <summary>
/// 安装包自动清理：扫描常见下载位置，删除本应用自己的旧安装包（每类保留最新一份）。
///
/// 触发时机：
/// <list type="bullet">
///   <item>应用启动后后台执行一次（延迟数秒，避免与启动流程抢 IO）；</item>
///   <item>应用内完成安装包 / 增量包下载并启动安装后执行一次（此时刚下载的是最新一份，不会被误删）。</item>
/// </list>
/// 安全性：只按 <see cref="InstallerCleanup.Classify"/> 的严格命名模式识别，绝不触碰其它文件；
/// 删除前保留每类最新一份；删除失败（占用 / 权限）静默跳过并记日志。
/// </summary>
public sealed class InstallerCleanupService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);

    /// <summary>启动后的后台清理（fire-and-forget 入口）。</summary>
    public void RunAtStartup()
    {
        Task.Run(async () =>
        {
            await Task.Delay(StartupDelay).ConfigureAwait(false);
            RunOnce();
        }).FireAndForget("Cleanup", "清理旧安装包");
    }

    /// <summary>立即执行一次清理（下载完成后调用）。同步阻塞但很快（仅删除匹配文件）。</summary>
    public void RunOnce()
    {
        try
        {
            var candidates = CollectCandidates();
            if (candidates.Count == 0) return;

            var toDelete = InstallerCleanup.SelectFilesToDelete(candidates);
            foreach (var path in toDelete)
            {
                try
                {
                    File.Delete(path);
                    FileLogger.Info("Cleanup", $"已删除旧安装包：{path}");
                }
                catch (Exception ex)
                {
                    // 占用 / 权限不足：跳过，下次启动再试
                    FileLogger.Warn("Cleanup", $"删除失败（跳过）：{path}（{ex.Message}）");
                }
            }
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Cleanup", "安装包清理异常：" + ex.Message);
        }
    }

    /// <summary>收集各候选目录中符合命名模式的安装包文件（路径 + 最后修改时间）。</summary>
    private static List<(string Path, DateTime LastWrite)> CollectCandidates()
    {
        var files = new List<(string Path, DateTime LastWrite)>();

        foreach (var dir in CandidateDirectories())
        {
            if (!Directory.Exists(dir)) continue;

            foreach (var file in Directory.EnumerateFiles(dir))
            {
                var name = Path.GetFileName(file);
                if (InstallerCleanup.Classify(name) == InstallerCleanup.Kind.None) continue;

                try
                {
                    var fi = new FileInfo(file);
                    files.Add((file, fi.LastWriteTime));
                }
                catch (Exception)
                {
                    // 单个文件读取失败跳过
                }
            }
        }

        return files;
    }

    /// <summary>扫描目录集合：临时目录、下载目录、桌面、应用数据目录（updates 暂存所在）。</summary>
    private static IEnumerable<string> CandidateDirectories()
    {
        var dirs = new List<string>
        {
            Path.GetTempPath(),
        };

        AddIfExists(dirs, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        AddIfExists(dirs, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));

        // 应用数据目录本身 + 其下的 updates（增量包下载解压可能留下 OBS_Helper_Update_*.zip 之外的文件）
        var dataDir = OBS_Helper.Wpf.Services.Host.HostBridge.AppDataDirectory;
        AddIfExists(dirs, dataDir);
        AddIfExists(dirs, dataDir, "updates");

        return dirs;
    }

    private static void AddIfExists(List<string> dirs, params string[] parts)
    {
        try
        {
            var path = Path.Combine(parts);
            if (Directory.Exists(path)) dirs.Add(path);
        }
        catch (Exception)
        {
            // 路径非法跳过
        }
    }
}
