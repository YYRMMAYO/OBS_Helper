using System.IO;

namespace OBS_Helper.Wpf.Services.Update;

/// <summary>增量包清单路径归一化（纯逻辑、可单测）：把「正斜杠相对路径」转为本地分隔符路径并拒绝路径穿越。</summary>
public static class UpdatePaths
{
    /// <summary>把清单里的相对路径（正斜杠）转为本地路径；含 .. 或为根路径时抛 <see cref="InvalidDataException"/>。</summary>
    public static string NormalizeRel(string rel)
    {
        var normalized = rel.Replace('/', Path.DirectorySeparatorChar);
        if (normalized.Contains("..") || Path.IsPathRooted(normalized))
        {
            throw new InvalidDataException($"非法路径：{rel}");
        }
        return normalized;
    }
}
