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
