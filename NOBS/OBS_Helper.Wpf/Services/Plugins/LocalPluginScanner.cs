using System.IO;
using Microsoft.Win32;
using OBS_Helper.Wpf.Services.ObsConfig;

namespace OBS_Helper.Wpf.Services.Plugins;

/// <summary>一次本机插件体检的完整结果。</summary>
public sealed class LocalPluginScanResult
{
    public List<InstalledPluginFile> Plugins { get; init; } = new();
    /// <summary>实际扫描到且存在的目录（供 UI 展示「检测来源」）。</summary>
    public List<string> ScannedDirs { get; init; } = new();
    /// <summary>OBS 安装目录是否找到（找不到时 UI 提示「未检测到 OBS」）。</summary>
    public bool ObsInstallFound { get; init; }

    /// <summary>已收录进插件广场的数量。</summary>
    public int CataloguedCount => Plugins.Count(p => p.CatalogId is not null);
}

/// <summary>
/// 本机 OBS 插件体检（路线图 P0-1，只读）：枚举已装插件的 DLL 名称与文件版本。
///
/// 扫描目录多信号定位（V2.3 重写，不再只看 C 盘；候选合并逻辑见
/// <see cref="PluginScanLocations"/>，纯逻辑部分有单测覆盖）：
/// <list type="bullet">
///   <item>实际安装目录：手动指定（便携布局反推，见 ObsPathService）→ 运行中进程 →
///         注册表卸载项（HKLM 双视图 + HKCU）→ 全盘固定驱动器常见布局；</item>
///   <item>%ProgramFiles%（及 x86 / ProgramW6432）下的 obs-studio；</item>
///   <item>Steam 版：解析各 Steam 根的 steamapps/libraryfolders.vdf，
///         覆盖安装在非默认盘的库（D:\SteamLibrary 等）；</item>
///   <item>用户级插件目录 %AppData%\obs-studio\plugins。</item>
/// </list>
///
/// 原则：只读不改——不提供任何安装 / 卸载 / 移动文件的能力；结果仅存本机。
/// </summary>
public sealed class LocalPluginScanner
{
    /// <summary>安装目录探测回调（组合根注入，优先尊重手动指定）；为空时走静态自动探测。</summary>
    private readonly Func<string?>? _detectInstallDir;

    public LocalPluginScanner(Func<string?>? detectInstallDir = null) => _detectInstallDir = detectInstallDir;

    /// <summary>同步扫描。UI 层请用 <see cref="ScanAsync"/> 放到后台线程。</summary>
    public LocalPluginScanResult Scan()
    {
        var installDir = _detectInstallDir?.Invoke() ?? ObsPathService.TryDetectInstallDir();

        var candidates = PluginScanLocations.BuildCandidates(
            installDir,
            GetObsInstallRoots(),
            GetSteamObsPluginDirs(),
            GetUserPluginsDir());

        var scanned = candidates
            .Where(c => !string.IsNullOrWhiteSpace(c.Dir) && Directory.Exists(c.Dir))
            .Select(c => c.Dir)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var plugins = PluginScannerCore.ScanDirectories(
            scanned.Select(d => (d, candidates.First(c => string.Equals(c.Dir, d, StringComparison.OrdinalIgnoreCase)).Label)));

        return new LocalPluginScanResult
        {
            Plugins = plugins,
            ScannedDirs = scanned,
            ObsInstallFound = !string.IsNullOrEmpty(installDir) || scanned.Count > 0
        };
    }

    /// <summary>
    /// 安装根候选：%ProgramFiles% 系特殊文件夹 + 所有固定驱动器上的常见布局
    /// （{盘}:\Program Files\obs-studio、{盘}:\Program Files (x86)\obs-studio、{盘}:\obs-studio）。
    /// 存在性过滤交给候选清单的使用方，这里只负责把网撒够宽。
    /// </summary>
    private static List<string> GetObsInstallRoots()
    {
        var roots = new List<string>();

        foreach (var special in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            try
            {
                var dir = Environment.GetFolderPath(special);
                if (!string.IsNullOrEmpty(dir)) roots.Add(dir);
            }
            catch (Exception) { }
        }

        // 64 位系统上 32 位进程读到的 ProgramFiles 会指向 x86；补一个明确的 64 位变量兜底
        try
        {
            var pf64 = Environment.GetEnvironmentVariable("ProgramW6432");
            if (!string.IsNullOrWhiteSpace(pf64)) roots.Add(pf64);
        }
        catch (Exception) { }

        foreach (var root in PluginScanLocations.GetStandardObsRoots(GetFixedDriveRoots()))
            roots.Add(root);

        return roots;
    }

    /// <summary>
    /// Steam 版 OBS 的插件目录：从每个可达的 Steam 根出发，解析 libraryfolders.vdf
    /// 拿到全部库（含跨盘库），再逐库探测 steamapps/common/OBSStudio/obs-plugins/64bit。
    /// </summary>
    private static List<string> GetSteamObsPluginDirs()
    {
        var result = new List<string>();
        try
        {
            foreach (var root in GetSteamRoots())
            {
                if (!Directory.Exists(root)) continue;

                var libraries = new List<string>();
                var steamapps = Path.Combine(root, "steamapps");
                if (Directory.Exists(steamapps)) libraries.Add(root);

                try
                {
                    var vdf = Path.Combine(steamapps, "libraryfolders.vdf");
                    if (File.Exists(vdf))
                        libraries.AddRange(PluginScanLocations.ParseSteamLibraryPaths(File.ReadAllText(vdf)));
                }
                catch (Exception) { }

                foreach (var lib in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var obs = Path.Combine(lib, "steamapps", "common", "OBSStudio", "obs-plugins", "64bit");
                    if (!result.Contains(obs, StringComparer.OrdinalIgnoreCase)) result.Add(obs);
                }
            }
        }
        catch (Exception) { }
        return result;
    }

    /// <summary>Steam 根候选：注册表 SteamPath（最可靠，任意盘）→ 默认安装位置 → 各固定盘常见目录名。</summary>
    private static List<string> GetSteamRoots()
    {
        var roots = new List<string>();

        try
        {
            var reg = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
            if (!string.IsNullOrWhiteSpace(reg))
            {
                // 注册表里可能是正斜杠写法，统一成 Windows 路径并去掉结尾分隔符
                var p = reg.Replace('/', '\\').TrimEnd('\\');
                if (p.Length > 0) roots.Add(p);
            }
        }
        catch (Exception) { }

        try
        {
            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrEmpty(pf86)) roots.Add(Path.Combine(pf86, "Steam"));
        }
        catch (Exception) { }

        foreach (var drive in GetFixedDriveRoots())
        {
            roots.Add(Path.Combine(drive, "Steam"));
            roots.Add(Path.Combine(drive, "SteamLibrary"));
        }
        return roots;
    }

    private static string? GetUserPluginsDir()
    {
        try
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obs-studio", "plugins");
        }
        catch (Exception)
        {
            return null;
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
