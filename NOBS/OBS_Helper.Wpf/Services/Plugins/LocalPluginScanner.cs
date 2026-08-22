using System.IO;
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
/// 扫描目录候选（全部只读探测，存在才扫）：
/// <list type="bullet">
///   <item>&lt;OBS安装目录&gt;\obs-plugins\64bit —— 进程 / 注册表 / 默认路径三重定位（复用 ObsPathService）；</item>
///   <item>%ProgramFiles%\obs-studio\obs-plugins\64bit 与 x86 兜底；</item>
///   <item>Steam 版默认库目录；</item>
///   <item>%AppData%\obs-studio\plugins 用户级插件目录。</item>
/// </list>
///
/// 原则：只读不改——不提供任何安装 / 卸载 / 移动文件的能力；结果仅存本机。
/// </summary>
public sealed class LocalPluginScanner
{
    /// <summary>同步扫描。UI 层请用 <see cref="ScanAsync"/> 放到后台线程。</summary>
    public LocalPluginScanResult Scan()
    {
        var candidates = new List<(string Dir, string Label)>();

        var installDir = ObsPathService.TryDetectInstallDir();
        if (!string.IsNullOrEmpty(installDir))
            candidates.Add((Path.Combine(installDir, "obs-plugins", "64bit"), "install"));

        foreach (var root in GetProgramDirs())
        {
            candidates.Add((Path.Combine(root, "obs-studio", "obs-plugins", "64bit"), "install"));
        }

        // Steam 默认库（非默认库需解析 libraryfolders.vdf，投入产出比低，先不做）
        try
        {
            var steam = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam",
                "steamapps", "common", "OBSStudio");
            if (Directory.Exists(steam))
                candidates.Add((Path.Combine(steam, "obs-plugins", "64bit"), "install"));
        }
        catch (Exception) { }

        // 用户级插件目录
        try
        {
            var userPlugins = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obs-studio", "plugins");
            candidates.Add((userPlugins, "user"));
        }
        catch (Exception) { }

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

    private static List<string> GetProgramDirs()
    {
        var dirs = new List<string>();
        foreach (var special in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            try
            {
                var dir = Environment.GetFolderPath(special);
                if (!string.IsNullOrEmpty(dir)) dirs.Add(dir);
            }
            catch (Exception) { }
        }
        return dirs;
    }
}
