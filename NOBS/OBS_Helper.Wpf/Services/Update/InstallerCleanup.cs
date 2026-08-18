using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OBS_Helper.Wpf.Services.Update;

/// <summary>
/// 安装包识别与清理策略（纯逻辑、可单测）。
///
/// 只识别本应用自己的分发产物，绝不碰其它文件：
/// <list type="bullet">
///   <item><c>OBS_Helper_Setup_*.exe</c> —— Inno Setup 安装包（含应用内下载的临时安装包）；</item>
///   <item><c>OBS_Helper_Portable_*.exe / .zip</c> —— 便携版；</item>
///   <item><c>OBS_Helper_Update_*.zip</c> —— 增量更新包；</item>
///   <item><c>OBS_Helper_Manifest_*.json</c> —— 发布清单。</item>
/// </list>
/// 识别一律严格前缀 + 扩展名匹配，避免误删用户其它文件。
/// </summary>
public static class InstallerCleanup
{
    /// <summary>产物类别；不匹配任何本应用产物时为 <see cref="Kind.None"/>。</summary>
    public enum Kind
    {
        Setup,
        PortableExe,
        PortableZip,
        UpdateZip,
        Manifest,
        None,
    }

    /// <summary>按文件名识别产物类别（大小写不敏感）。</summary>
    public static Kind Classify(string fileName)
    {
        if (fileName.StartsWith("OBS_Helper_Setup_", StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return Kind.Setup;

        if (fileName.StartsWith("OBS_Helper_Portable_", StringComparison.OrdinalIgnoreCase))
        {
            if (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return Kind.PortableExe;
            if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return Kind.PortableZip;
            return Kind.None;
        }

        if (fileName.StartsWith("OBS_Helper_Update_", StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return Kind.UpdateZip;

        if (fileName.StartsWith("OBS_Helper_Manifest_", StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return Kind.Manifest;

        return Kind.None;
    }

    /// <summary>
    /// 从一批候选文件里选出「应删除的旧包」：同一类别只保留修改时间最新的一份，其余全部删除。
    /// <paramref name="files"/> 的每一项为 (完整路径, 最后修改时间)。
    /// </summary>
    public static List<string> SelectFilesToDelete(IEnumerable<(string Path, DateTime LastWrite)> files)
    {
        var result = new List<string>();
        var grouped = files
            .Where(f => Classify(Path.GetFileName(f.Path)) != Kind.None)
            .GroupBy(f => Classify(Path.GetFileName(f.Path)));

        foreach (var group in grouped)
        {
            var newest = group.OrderByDescending(f => f.LastWrite).First();
            foreach (var f in group)
            {
                if (!string.Equals(f.Path, newest.Path, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(f.Path);
                }
            }
        }

        return result;
    }
}
