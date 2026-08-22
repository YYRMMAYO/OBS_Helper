using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using OBS_Helper.Wpf.Models.ObsConfig;
using OBS_Helper.Wpf.Services.Host;

namespace OBS_Helper.Wpf.Services.ObsConfig;

/// <summary>
/// OBS 配置目录定位 + 进程检测。
///
/// 定位优先级：手动覆盖（LocalStore <c>obs_config_override</c>）→ 便携模式
/// （安装目录下存在 <c>portable_mode.txt</c> → <c>&lt;安装目录&gt;\config\obs-studio</c>）
/// → <c>%AppData%\obs-studio</c>。
///
/// 安装目录探测：运行中进程 <c>MainModule.FileName</c> 上溯（OBS 提权运行时会抛
/// <see cref="System.ComponentModel.Win32Exception"/>，必须 try/catch）→ 注册表卸载项
/// （HKLM 双视图 + HKCU，InstallLocation 缺失时用 DisplayIcon 反推）→ 全盘固定驱动器上的
/// 常见安装布局（V2.3 起，不再只认 C 盘默认路径）。
///
/// <b>任何探测失败都返回空的合法结果，绝不抛异常</b>——本助手的目标机器不一定装了 OBS。
/// </summary>
public sealed class ObsPathService
{
    internal const string OverrideKey = "obs_config_override";
    /// <summary>卸载项候选：HKLM 64 位视图 / HKLM WOW6432Node（32 位安装器） / HKCU（用户级安装）。</summary>
    private static readonly string[] UninstallKeys =
    {
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\OBS Studio",
        @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\OBS Studio",
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall\OBS Studio",
    };

    private static readonly string[] ProcessNames = { "obs64", "obs32", "obs" };

    private readonly LocalStore _store;

    public ObsPathService(LocalStore store) => _store = store;

    /// <summary>应用私有数据下的备份目录（手动指定目录外的自动备份落这里）。</summary>
    public static string BackupsRoot => Path.Combine(HostBridge.AppDataDirectory, "backups");

    /// <summary>应用私有数据下的回收站目录（彻底重置 / 覆盖导入时把旧文件移到这里，供恢复）。</summary>
    public static string TrashRoot => Path.Combine(HostBridge.AppDataDirectory, "trash");

    /// <summary>清理回收站，只保留最近 keepGroups 组（对应「永不硬删，但回收站也不能无限增长」）。</summary>
    public static void CleanupTrash(int keepGroups = 5)
    {
        try
        {
            var root = TrashRoot;
            if (!Directory.Exists(root)) return;
            var groups = Directory.GetDirectories(root)
                .Where(d => Path.GetFileName(d).StartsWith("tx_", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(d => Directory.GetCreationTimeUtc(d))
                .Skip(keepGroups);
            foreach (var g in groups)
            {
                try { Directory.Delete(g, recursive: true); } catch (Exception) { }
            }
        }
        catch (Exception) { }
    }

    /// <summary>定位 OBS 配置目录。不存在时 <see cref="ObsConfigLocation.Exists"/> 为 false，不抛。</summary>
    public Task<ObsConfigLocation> LocateAsync() => Task.FromResult(ResolveLocation());

    private ObsConfigLocation ResolveLocation()
    {
        // 1) 手动覆盖优先（用户通过「手动指定目录」指过去）
        var overridePath = _store.GetItem(OverrideKey);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return new ObsConfigLocation(
                ConfigDir: overridePath,
                IsPortable: false,
                Exists: Directory.Exists(overridePath),
                Source: "manual");
        }

        // 2) 便携模式：安装目录下存在 portable_mode.txt
        var installDir = DetectInstallDir();
        if (!string.IsNullOrEmpty(installDir))
        {
            var portableFlag = Path.Combine(installDir, "portable_mode.txt");
            if (File.Exists(portableFlag))
            {
                var cfg = Path.Combine(installDir, "config", "obs-studio");
                return new ObsConfigLocation(
                    ConfigDir: cfg,
                    IsPortable: true,
                    Exists: Directory.Exists(cfg),
                    Source: "portable");
            }
        }

        // 3) 兜底：%AppData%\obs-studio
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "obs-studio");
        return new ObsConfigLocation(
            ConfigDir: appData,
            IsPortable: false,
            Exists: Directory.Exists(appData),
            Source: "appdata");
    }

    /// <summary>检测 OBS 是否在运行（双信号）。</summary>
    public bool IsObsRunning() => DetectProcess().IsRunning;

    /// <summary>双信号检测：① 进程名 obs*/obs64/obs32；② <c>global.ini</c> 排他锁（OBS 运行时会持写句柄）。</summary>
    public ObsProcessInfo DetectProcess()
    {
        var info = new ObsProcessInfo();

        // 信号①：进程名
        try
        {
            foreach (var name in ProcessNames)
            {
                var procs = Process.GetProcessesByName(name);
                if (procs.Length > 0)
                {
                    info.IsRunning = true;
                    info.ProcessName = name;
                    info.Pid = procs[0].Id;
                    info.Evidence = $"检测到进程 {name}（PID {procs[0].Id}）。";
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            // 进程枚举失败不应阻断：交给信号②兜底
            info.Evidence = $"进程枚举异常（已忽略）：{ex.Message}";
        }

        // 信号②：global.ini 独占锁（即便改了进程名也能抓到）
        var loc = ResolveLocation();
        if (loc.Exists)
        {
            var gi = Path.Combine(loc.ConfigDir, "global.ini");
            if (File.Exists(gi))
            {
                try
                {
                    using var fs = new FileStream(gi, FileMode.Open, FileAccess.Read, FileShare.None);
                    if (!info.IsRunning) info.Evidence = "global.ini 可被独占打开，OBS 未在运行。";
                }
                catch (IOException)
                {
                    info.IsRunning = true;
                    info.Evidence = "global.ini 被 OBS 独占占用，判定 OBS 正在运行。";
                }
                catch (UnauthorizedAccessException)
                {
                    info.IsRunning = true;
                    info.Evidence = "global.ini 无法以只读方式打开（被占用），判定 OBS 正在运行。";
                }
            }
        }

        return info;
    }

    /// <summary>枚举场景集合名（读 basic/scenes/*.json 内的 <c>name</c> 字段）。目录缺失返回空列表，不抛。</summary>
    public IReadOnlyList<string> EnumerateSceneCollections(string configDir)
    {
        var result = new List<string>();
        try
        {
            var dir = Path.Combine(configDir, "basic", "scenes");
            if (!Directory.Exists(dir)) return result;
            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                var name = ReadSceneCollectionName(file);
                if (!string.IsNullOrWhiteSpace(name)) result.Add(name);
            }
        }
        catch (Exception)
        {
            // 枚举失败：返回已收集到的部分
        }
        return result;
    }

    /// <summary>枚举 profile 名（basic/profiles 下的子目录名）。</summary>
    public IReadOnlyList<string> EnumerateProfiles(string configDir)
    {
        var result = new List<string>();
        try
        {
            var dir = Path.Combine(configDir, "basic", "profiles");
            if (!Directory.Exists(dir)) return result;
            foreach (var sub in Directory.GetDirectories(dir))
                result.Add(Path.GetFileName(sub.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
        }
        catch (Exception)
        {
        }
        return result;
    }

    /// <summary>估算配置目录总大小（字节）；失败返回 0。</summary>
    public long EstimateSize(string configDir)
    {
        try
        {
            if (!Directory.Exists(configDir)) return 0;
            return new DirectoryInfo(configDir)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static string? ReadSceneCollectionName(string file)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
            if (doc.RootElement.TryGetProperty("name", out var n) && n.ValueKind == System.Text.Json.JsonValueKind.String)
                return n.GetString();
        }
        catch (Exception)
        {
        }
        return null;
    }

    /// <summary>
    /// 探测 OBS 安装目录（进程 MainModule → 注册表卸载项 → 全盘常见路径）。
    /// V2.2 起对插件体检开放（只读使用）；探测失败返回 null。
    /// </summary>
    public static string? TryDetectInstallDir() => DetectInstallDir();

    /// <summary>
    /// 插件体检专用入口（V2.3）：优先尊重「设置 → OBS 配置管理」手动指定的配置目录——
    /// 若符合便携布局（<c>&lt;安装目录&gt;\config\obs-studio</c>）则反推安装目录；
    /// 否则回退自动探测。找不到返回 null。
    /// </summary>
    public string? TryDetectInstallDirForScan()
    {
        try
        {
            var overrideCfg = _store.GetItem(OverrideKey);
            if (!string.IsNullOrWhiteSpace(overrideCfg))
            {
                var parent = Directory.GetParent(overrideCfg);
                if (parent is not null &&
                    string.Equals(parent.Name, "config", StringComparison.OrdinalIgnoreCase))
                {
                    var install = parent.Parent?.FullName;
                    if (!string.IsNullOrEmpty(install) && Directory.Exists(install))
                        return install;
                }
            }
        }
        catch (Exception)
        {
        }
        return TryDetectInstallDir();
    }

    private static string? DetectInstallDir()
    {
        // 信号①：运行中进程的安装目录
        try
        {
            foreach (var name in ProcessNames)
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    try
                    {
                        var path = p.MainModule?.FileName;
                        if (!string.IsNullOrEmpty(path))
                            return Path.GetDirectoryName(path);
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        // 提权运行的 OBS：跨会话读 MainModule 会抛，忽略，继续走注册表
                    }
                    catch (Exception)
                    {
                        // 其它异常（已退出等）忽略
                    }
                }
            }
        }
        catch (Exception)
        {
        }

        // 信号②：注册表卸载项（HKLM 双视图 + HKCU；InstallLocation 缺失时用 DisplayIcon 反推）
        foreach (var key in UninstallKeys)
        {
            try
            {
                var loc = Registry.GetValue(key, "InstallLocation", null) as string;
                if (!string.IsNullOrWhiteSpace(loc))
                {
                    // 有的安装器把值写成带引号或带尾斜杠的形式，归一化后再验证
                    loc = loc.Trim().Trim('"').TrimEnd('\\', '/');
                    if (Directory.Exists(loc)) return loc;
                }

                var fromIcon = InstallDirFromDisplayIcon(
                    Registry.GetValue(key, "DisplayIcon", null) as string);
                if (!string.IsNullOrEmpty(fromIcon)) return fromIcon;
            }
            catch (Exception)
            {
            }
        }

        // 信号③：全盘固定驱动器上的常见安装布局（OBS 可能装在任意盘，C 盘默认位置自然覆盖其中）
        foreach (var root in GetStandardObsRoots(GetFixedDriveRoots()))
        {
            try { if (Directory.Exists(root)) return root; } catch (Exception) { }
        }
        return null;
    }

    /// <summary>DisplayIcon 值形如 <c>"C:\...\bin\64bit\obs64.exe",0</c>：取 exe 路径后向上找含 obs-plugins 的安装根。</summary>
    private static string? InstallDirFromDisplayIcon(string? displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon)) return null;
        try
        {
            var exe = displayIcon.Trim();
            // 去掉资源索引后缀（如 ",0"）
            var comma = exe.LastIndexOf(',');
            if (comma > 0 && int.TryParse(exe[(comma + 1)..].Trim(), out _)) exe = exe[..comma];
            exe = exe.Trim('"');
            if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;

            var dir = Path.GetDirectoryName(exe);
            for (var i = 0; i < 3 && !string.IsNullOrEmpty(dir); i++)
            {
                if (Directory.Exists(Path.Combine(dir, "obs-plugins"))) return dir;
                dir = Path.GetDirectoryName(dir);
            }
        }
        catch (Exception)
        {
        }
        return null;
    }

    /// <summary>每个固定驱动器上的常见 OBS 安装布局（与插件扫描的候选规则保持一致）。</summary>
    private static IEnumerable<string> GetStandardObsRoots(IEnumerable<string> driveRoots)
    {
        foreach (var drive in driveRoots)
        {
            yield return Path.Combine(drive, "Program Files", "obs-studio");
            yield return Path.Combine(drive, "Program Files (x86)", "obs-studio");
            yield return Path.Combine(drive, "obs-studio");
        }
    }

    private static List<string> GetFixedDriveRoots()
    {
        var roots = new List<string>();
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (d.DriveType == DriveType.Fixed && d.IsReady) roots.Add(d.Name);
                }
                catch (Exception) { }
            }
        }
        catch (Exception) { }
        return roots;
    }
}
