using System.Collections.Generic;
using System.Linq;

namespace OBS_Helper.Wpf.Services.Update;

/// <summary>
/// 增量清单构建（纯逻辑、无 IO，可单测）：对比新旧两版「完整文件清单」，算出升级所需差异。
///
/// 规则：
/// <list type="bullet">
///   <item>新版本里新增的文件 → 进 <see cref="UpdateManifest.Files"/>（需要下载）；</item>
///   <item>新旧都有但大小 / 哈希不同 → 进 Files（内容变化，需要覆盖）；</item>
///   <item>旧版本有、新版本没有 → 进 <see cref="UpdateManifest.Remove"/>（需要删除）。</item>
/// </list>
/// 文件名与路径均按「相对应用目录、正斜杠分隔」归一化后比较，与 Windows 目录分隔符无关。
/// </summary>
public static class DeltaBuilder
{
    /// <summary>计算从旧版本到新版本的增量清单。</summary>
    public static UpdateManifest Build(
        string baseVersion,
        string targetVersion,
        IReadOnlyDictionary<string, ManifestFileEntry> oldFiles,
        IReadOnlyDictionary<string, ManifestFileEntry> newFiles)
    {
        var manifest = new UpdateManifest
        {
            BaseVersion = baseVersion,
            TargetVersion = targetVersion,
        };

        foreach (var (path, entry) in newFiles)
        {
            // 新增（旧版没有），或内容变化（大小或哈希不同）→ 需要下载覆盖
            if (!oldFiles.TryGetValue(path, out var old)
                || old.Size != entry.Size
                || !string.Equals(old.Sha256, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                manifest.Files.Add(entry);
            }
        }

        foreach (var path in oldFiles.Keys)
        {
            if (!newFiles.ContainsKey(path))
            {
                manifest.Remove.Add(path);
            }
        }

        return manifest;
    }

    /// <summary>把「相对路径 → 文件条目」字典转为按正斜杠排序的稳定列表（构建脚本落盘用）。</summary>
    public static List<ManifestFileEntry> SortEntries(IEnumerable<ManifestFileEntry> entries)
        => entries.OrderBy(e => e.Path, StringComparer.Ordinal).ToList();
}
