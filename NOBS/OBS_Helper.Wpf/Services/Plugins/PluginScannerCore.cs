using System.Diagnostics;
using System.IO;

namespace OBS_Helper.Wpf.Services.Plugins;

/// <summary>本机已安装的一个插件 DLL（只读体检结果）。</summary>
public sealed class InstalledPluginFile
{
    /// <summary>文件名，如 advanced-scene-switcher.dll。</summary>
    public string FileName { get; init; } = "";
    /// <summary>匹配用主干（小写、无扩展名）。</summary>
    public string Stem { get; init; } = "";
    /// <summary>文件版本（读不到时为空串）。</summary>
    public string FileVersion { get; init; } = "";
    /// <summary>产品名（读不到时为空串），用于展示兜底。</summary>
    public string ProductName { get; init; } = "";
    public string FullPath { get; init; } = "";
    public long SizeBytes { get; init; }
    public DateTime ModifiedAt { get; init; }
    /// <summary>所在目录的友好标签：install=系统安装目录，user=用户插件目录。</summary>
    public string SourceLabel { get; init; } = "install";
    /// <summary>命中的广场条目 id；未收录为 null。由调用方借助目录数据回填。</summary>
    public string? CatalogId { get; set; }
}

/// <summary>
/// 本地插件扫描的纯逻辑部分：给定若干 obs-plugins/64bit 目录，枚举其中的 DLL 并读取版本信息。
/// 只读、绝不抛异常；单目录失败跳过并记录。
/// </summary>
public static class PluginScannerCore
{
    /// <summary>扫描多个目录，返回按文件名排序的去重列表（同一路径只出现一次）。</summary>
    public static List<InstalledPluginFile> ScanDirectories(IEnumerable<(string Dir, string SourceLabel)> dirs)
    {
        var result = new List<InstalledPluginFile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (dir, label) in dirs)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) continue;

            string[] files;
            try
            {
                files = Directory.GetFiles(dir, "*.dll", SearchOption.TopDirectoryOnly);
            }
            catch (Exception)
            {
                continue; // 无权限等：跳过该目录
            }

            foreach (var file in files)
            {
                if (!seen.Add(file)) continue;

                FileInfo info;
                try { info = new FileInfo(file); }
                catch (Exception) { continue; }

                string version = "", product = "";
                try
                {
                    var vi = FileVersionInfo.GetVersionInfo(file);
                    version = PickVersion(vi);
                    product = vi.ProductName ?? "";
                }
                catch (Exception)
                {
                    // 个别 DLL 无版本信息：字段留空即可
                }

                result.Add(new InstalledPluginFile
                {
                    FileName = info.Name,
                    Stem = PluginCatalogCore.NormalizeDllStem(info.Name),
                    FileVersion = version,
                    ProductName = product,
                    FullPath = info.FullName,
                    SizeBytes = info.Length,
                    ModifiedAt = info.LastWriteTime,
                    SourceLabel = label
                });
            }
        }

        return result.OrderBy(p => p.Stem, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>优先取 ProductVersion（语义更完整），回退 FileVersion；都空则空串。</summary>
    private static string PickVersion(FileVersionInfo vi)
    {
        if (!string.IsNullOrWhiteSpace(vi.ProductVersion)) return vi.ProductVersion!;
        if (!string.IsNullOrWhiteSpace(vi.FileVersion)) return vi.FileVersion!;
        return "";
    }
}

/// <summary>
/// 扫描候选目录构建的纯逻辑部分（V2.3，可单测）：把「实际 OBS 安装目录 / 常见安装根 /
/// Steam 多库目录 / 用户级插件目录」合并成有序去重的候选清单。不做任何 IO 与注册表访问。
///
/// 背景：V2.2 只探测 %ProgramFiles%（通常在 C 盘）——OBS 装在 D/E 盘或 Steam 非默认库时
/// 本机体检会漏扫。V2.3 改为多信号定位，具体探测由 <see cref="LocalPluginScanner"/> 完成。
/// </summary>
public static class PluginScanLocations
{
    /// <summary>
    /// 合并各来源候选并按优先级排序去重：
    /// 实际安装目录 → 各安装根下的 obs-plugins\64bit → Steam 库目录 → 用户级插件目录。
    /// 空白输入直接忽略；存在性检查交给调用方（ScanDirectories 会跳过不存在的目录）。
    /// </summary>
    public static List<(string Dir, string Label)> BuildCandidates(
        string? installDir,
        IEnumerable<string> obsInstallRoots,
        IEnumerable<string> steamObsPluginDirs,
        string? userPluginsDir)
    {
        var list = new List<(string Dir, string Label)>();

        if (!string.IsNullOrWhiteSpace(installDir))
            list.Add((Path.Combine(installDir, "obs-plugins", "64bit"), "install"));

        foreach (var root in obsInstallRoots)
        {
            if (!string.IsNullOrWhiteSpace(root))
                list.Add((Path.Combine(root, "obs-plugins", "64bit"), "install"));
        }

        foreach (var steamDir in steamObsPluginDirs)
        {
            if (!string.IsNullOrWhiteSpace(steamDir))
                list.Add((steamDir, "install"));
        }

        if (!string.IsNullOrWhiteSpace(userPluginsDir))
            list.Add((userPluginsDir, "user"));

        // 大小写不敏感去重，保留首次出现顺序（优先级语义）
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return list.Where(c => seen.Add(c.Dir)).ToList();
    }

    /// <summary>
    /// 给定一组盘符根（如 C:\、D:\），产出每个盘上的常见 OBS 安装根候选
    /// （Program Files / Program Files (x86) / 盘根目录三种布局）。只拼路径，不做存在性检查。
    /// </summary>
    public static List<string> GetStandardObsRoots(IEnumerable<string> driveRoots)
    {
        var roots = new List<string>();
        foreach (var drive in driveRoots)
        {
            if (string.IsNullOrWhiteSpace(drive)) continue;
            roots.Add(Path.Combine(drive, "Program Files", "obs-studio"));
            roots.Add(Path.Combine(drive, "Program Files (x86)", "obs-studio"));
            roots.Add(Path.Combine(drive, "obs-studio"));
        }
        return roots;
    }

    /// <summary>
    /// 解析 Steam 的 <c>steamapps/libraryfolders.vdf</c>，返回全部库路径（含默认库）。
    /// VDF 里路径以 <c>\\</c> 转义（如 <c>"D:\\SteamLibrary"</c>），统一还原为 Windows 路径。
    /// </summary>
    public static List<string> ParseSteamLibraryPaths(string? vdfContent)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(vdfContent)) return result;

        try
        {
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(
                         vdfContent, "\"path\"\\s+\"([^\"]+)\"",
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                // 还原 VDF 转义：\\ → \；顺带把可能的正斜杠统一为反斜杠
                var p = m.Groups[1].Value.Replace("\\\\", "\\").Replace('/', '\\');
                if (!string.IsNullOrWhiteSpace(p) && !result.Contains(p, StringComparer.OrdinalIgnoreCase))
                    result.Add(p);
            }
        }
        catch (Exception)
        {
            // 解析失败按无库处理
        }
        return result;
    }
}
