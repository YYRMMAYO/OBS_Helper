using System.Reflection;
using System.Text.RegularExpressions;

namespace OBS_Helper.Wpf.Services;

/// <summary>发布系列：V = 常规正式版，X = 独立大版本。</summary>
public enum ReleaseSeries
{
    V = 0,
    X = 1,
}

/// <summary>
/// 发布通道（V3.5.0 / X1031）。
///
/// 规则：
/// <list type="bullet">
///   <item>X 系列只检测 GitHub 上 <c>X</c> 开头的标签；V 系列只检测 <c>V</c> 开头 / 纯数字标签；</item>
///   <item>X 标签形如 <c>X1031</c>，转换成内部版本 <c>1031.0.0</c>，保证 X1101 &gt; X1031；</item>
///   <item>当前系列由程序集主版本号判定：&gt;= 1000 即 X 系列，否则 V 系列。这样同一份源码
///       在 X 构建（1031.0.0）与 V 构建（3.5.0）下会自动选中各自的通道，不需要额外的编译常量。</item>
/// </list>
/// 纯逻辑，可单测。
/// </summary>
public static class ReleaseChannel
{
    /// <summary>主版本号达到该值即视为 X 系列（X1031 → 1031.0.0）。</summary>
    public const int XMajorThreshold = 1000;

    private static readonly Regex XTagPattern = new(
        @"^\s*[xX]\s*(\d{3,4})(?:\.(\d+))?(?:\.(\d+))?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex VTagPattern = new(
        @"^\s*[vV]?\s*(\d+)\.(\d+)(?:\.(\d+))?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>由程序集版本号判断系列（&gt;= 1000 为 X）。</summary>
    public static ReleaseSeries FromVersion(Version? version)
        => version is not null && version.Major >= XMajorThreshold ? ReleaseSeries.X : ReleaseSeries.V;

    /// <summary>当前进程所属系列。</summary>
    public static ReleaseSeries Current
        => FromVersion(typeof(ReleaseChannel).Assembly.GetName().Version);

    public static bool IsXSeries => Current == ReleaseSeries.X;

    /// <summary>标签是否属于指定系列。</summary>
    public static bool IsTagForSeries(string? tag, ReleaseSeries series)
        => ParseTagForSeries(tag, series) is not null;

    /// <summary>
    /// 解析指定系列的标签；不属于该系列或格式非法返回 null。
    /// X1031 → 1031.0.0，X1031.2 → 1031.2.0，X1101 → 1101.0.0。
    /// </summary>
    public static Version? ParseTagForSeries(string? tag, ReleaseSeries series)
    {
        var raw = (tag ?? "").Trim();
        if (raw.Length == 0) return null;

        if (series == ReleaseSeries.X)
        {
            var x = XTagPattern.Match(raw);
            if (!x.Success) return null;
            var major = int.Parse(x.Groups[1].Value);
            var minor = x.Groups[2].Success ? int.Parse(x.Groups[2].Value) : 0;
            var build = x.Groups[3].Success ? int.Parse(x.Groups[3].Value) : 0;
            return new Version(major, minor, build);
        }

        // V 系列：明确拒绝 X 标签；允许 V1.2.3 / v1.2.3 / 1.2.3
        if (raw.StartsWith("X", StringComparison.OrdinalIgnoreCase)) return null;
        var v = VTagPattern.Match(raw);
        if (!v.Success) return null;
        var vMajor = int.Parse(v.Groups[1].Value);
        if (vMajor >= XMajorThreshold) return null; // 1000+ 只属于 X 系列
        var vMinor = int.Parse(v.Groups[2].Value);
        var vBuild = v.Groups[3].Success ? int.Parse(v.Groups[3].Value) : 0;
        return new Version(vMajor, vMinor, vBuild);
    }

    /// <summary>按当前系列解析标签。</summary>
    public static Version? ParseCurrentTag(string? tag) => ParseTagForSeries(tag, Current);

    /// <summary>界面显示的版本号：X1031 / v3.5.0。</summary>
    public static string FormatVersion(Version? version)
    {
        if (version is null) return "";
        if (version.Major >= XMajorThreshold)
            return version.Minor > 0 || version.Build > 0
                ? $"X{version.Major}.{version.Minor}"
                : $"X{version.Major}";
        return $"v{version.Major}.{version.Minor}.{version.Build}";
    }

    /// <summary>系列名称（用于日志 / 关于页）。</summary>
    public static string SeriesName(ReleaseSeries series)
        => series == ReleaseSeries.X ? "X" : "V";
}
