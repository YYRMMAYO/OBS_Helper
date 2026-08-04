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
/// <see cref="System.ComponentModel.Win32Exception"/>，必须 try/catch）→ 注册表
/// <c>HKLM\...\Uninstall\OBS Studio\InstallLocation</c> → <c>C:\Program Files\obs-studio</c>。
///
/// <b>任何探测失败都返回空的合法结果，绝不抛异常</b>——本助手的目标机器不一定装了 OBS。
/// </summary>
public sealed class ObsPathService
{
    internal const string OverrideKey = "obs_config_override";
    private const string UninstallKey = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\OBS Studio";
    private const string DefaultInstall = @"C:\Program Files\obs-studio";

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

        // 信号②：注册表卸载项
        try
        {
            var loc = Registry.GetValue(UninstallKey, "InstallLocation", null) as string;
            if (!string.IsNullOrWhiteSpace(loc) && Directory.Exists(loc)) return loc;
        }
        catch (Exception)
        {
        }

        // 信号③：常见默认路径
        return Directory.Exists(DefaultInstall) ? DefaultInstall : null;
    }
}
