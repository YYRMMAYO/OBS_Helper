using System.IO;
using OBS_Helper.Wpf.Services.Compat;

namespace OBS_Helper.Wpf.Services.ObsConfig;

/// <summary>一个「可能装着 OBS」的候选可执行文件。</summary>
public sealed record ObsInstallCandidate(string ExePath, string Source, bool FromRegistry);

/// <summary>
/// 「把 OBS 拉起来」的纯逻辑（V2.9.4）。零 WPF / 零注册表依赖，单测工程直接链接编译。
///
/// 为什么单独抽出来：路径解析是这一环里唯一会「静默出错」的地方 ——
/// 注册表的 <c>DisplayIcon</c> 形如 <c>"C:\Program Files\obs-studio\bin\64bit\obs64.exe",0</c>，
/// 引号、逗号后缀、路径里本身带逗号的目录名，都是容易写错的形态。
/// 探测（注册表读取 / 目录枚举）留在 <see cref="ObsPathService"/>，这里只做字符串与路径推导。
///
/// 口径：<b>宁可返回 null 也不猜路径</b>。找不到就老老实实让用户自己打开 OBS，
/// 猜一个不存在的路径去 Process.Start 只会给出一句用户看不懂的系统错误。
/// </summary>
public static class ObsLaunchCore
{
    /// <summary>OBS 32.x 的主程序相对安装根的路径（其余为历史布局 / 便携布局）。</summary>
    private static readonly string[][] RelativeExePaths =
    {
        new[] { "bin", "64bit", "obs64.exe" },
        new[] { "bin", "64bit", "obs32.exe" },
        new[] { "obs64.exe" },
        new[] { "bin", "obs64.exe" },
    };

    /// <summary>
    /// 从注册表 <c>DisplayIcon</c> 的值解析出可执行文件路径。
    /// 认这几种形态（都实测存在于各家安装器写法里）：
    /// <c>"C:\...\obs64.exe",0</c> / <c>"C:\...\obs64.exe"</c> / <c>C:\...\obs64.exe,0</c> / <c>C:\...\obs64.exe</c>。
    /// 解析不出 .exe 一律返回 null。
    /// </summary>
    public static string? ExeFromDisplayIcon(string? displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon)) return null;

        var s = displayIcon.Trim();

        // 先砍掉引号之后的内容（",0 这类图标索引后缀都在引号外面）
        var closeQuote = s.IndexOf('"', 1);
        if (s.StartsWith('"') && closeQuote > 0)
            s = s[1..closeQuote];
        else
        {
            // 没有引号包裹：只按「末尾的 ,数字」剥图标索引。
            // 不能无脑取第一个逗号 —— 目录名里带逗号（"D:\OBS, backup\..."）会把路径截断。
            var comma = s.LastIndexOf(',');
            if (comma > 0)
            {
                var tail = s[(comma + 1)..].Trim();
                if (tail.Length > 0 && tail.All(Compat.Compat.IsAsciiDigit)) s = s[..comma];
            }
        }

        s = s.Trim().Trim('"').TrimEnd(',').Trim();
        return LooksLikeExe(s) ? s : null;
    }

    /// <summary>
    /// 从安装根目录推导可执行文件路径。找不到返回 null。
    /// 顺序即优先级：<c>bin\64bit\obs64.exe</c>（现行布局）→ <c>bin\64bit\obs32.exe</c> →
    /// <c>obs64.exe</c>（便携解压）→ <c>bin\obs64.exe</c>（历史布局）。
    /// </summary>
    public static string? ExeFromInstallDir(string? installDir)
    {
        if (string.IsNullOrWhiteSpace(installDir)) return null;

        var root = installDir.Trim().Trim('"').TrimEnd('\\', '/');
        if (root.Length == 0) return null;

        foreach (var rel in RelativeExePaths)
        {
            var path = root;
            foreach (var seg in rel) path = Path.Combine(path, seg);
            if (LooksLikeExe(path)) return path;
        }
        return null;
    }

    /// <summary>
    /// 汇总候选并按「注册表优先、其次安装根」排序，去重（大小写不敏感），
    /// 只保留 <paramref name="exists"/> 认账的项。
    ///
    /// 探测与判定分离是为了可测：单测直接注入一个假的 exists 就能覆盖「哪些路径会被试」。
    /// </summary>
    public static IReadOnlyList<ObsInstallCandidate> ResolveCandidates(
        IEnumerable<ObsInstallCandidate> raw, Func<string, bool> exists)
    {
        var result = new List<ObsInstallCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var c in raw)
        {
            if (c is null || string.IsNullOrWhiteSpace(c.ExePath)) continue;
            if (!LooksLikeExe(c.ExePath)) continue;
            if (!seen.Add(c.ExePath)) continue;

            bool ok;
            try { ok = exists(c.ExePath); }
            catch (Exception) { ok = false; }
            if (!ok) continue;

            result.Add(c);
        }

        return result;
    }

    /// <summary>候选来源标签：注册表卸载项。</summary>
    public const string SourceRegistry = "registry";

    /// <summary>候选来源标签：安装目录推导。</summary>
    public const string SourceInstallDir = "installDir";

    /// <summary>该路径看起来是个 exe 吗（扩展名 + 非空文件名 + 没有非法字符）。</summary>
    private static bool LooksLikeExe(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            var name = Path.GetFileName(path);
            return name.Length > 4 && path.IndexOfAny(Path.GetInvalidPathChars()) < 0;
        }
        catch (Exception)
        {
            // 非法路径（含 null 字符等）会让 Path 相关 API 抛异常：判定为「不是 exe」
            return false;
        }
    }
}
