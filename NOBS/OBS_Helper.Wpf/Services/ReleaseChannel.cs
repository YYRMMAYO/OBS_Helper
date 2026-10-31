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
///   <item>X 标签形如 <c>X1031</c>：<c>1031</c> 是 <c>MMdd</c> 日期码，转换成内部版本
///       <c>1031.0.0</c>；同一天再次发版时追加 <c>Y1</c>、<c>Y2</c>、<c>Y3</c>…，
///       转换成内部版本 <c>1031.1.0</c>、<c>1031.2.0</c>、<c>1031.3.0</c>…；</item>
///   <item>因此更新检测先比日期（主版本号），日期相同再比 <c>Yn</c> 的序号（次版本号），
///       例如 <c>X1031Y2 &gt; X1031</c>、<c>X1101 &gt; X1031Y99</c>；</item>
///   <item>当前系列由程序集主版本号判定：&gt;= 100 即 X 系列，否则 V 系列。这样同一份源码
///       在 X 构建（1031.0.0）与 V 构建（3.5.0）下会自动选中各自的通道，不需要额外的编译常量。</item>
/// </list>
/// 纯逻辑，可单测。
/// </summary>
public static class ReleaseChannel
{
    /// <summary>
    /// 主版本号达到该值即视为 X 系列。X 的主版本号是 <c>MMdd</c> 日期码，
    /// 最早的有效日期 <c>0101</c> 转成整数是 101；V 系列目前只有 3.x，不会被误判。
    /// </summary>
    public const int XMajorThreshold = 100;

    /// <summary>
    /// X 标签：<c>X</c> + 四位日期码 <c>MMdd</c>，同日再版追加 <c>Y</c> + 序号。
    /// 例：X1031、X1031Y1、X1031Y2、X1101。
    /// </summary>
    private static readonly Regex XTagPattern = new(
        @"^\s*[xX]\s*(\d{4})(?:[._-]?[yY]\s*(\d+))?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex VTagPattern = new(
        @"^\s*[vV]?\s*(\d+)\.(\d+)(?:\.(\d+))?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>由程序集版本号判断系列（主版本号 &gt;= 100 为 X）。</summary>
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
    /// X1031 → 1031.0.0，X1031Y2 → 1031.2.0，X1101 → 1101.0.0。
    /// </summary>
    public static Version? ParseTagForSeries(string? tag, ReleaseSeries series)
    {
        var raw = (tag ?? "").Trim();
        if (raw.Length == 0) return null;

        if (series == ReleaseSeries.X)
        {
            var x = XTagPattern.Match(raw);
            if (!x.Success) return null;

            var dateCode = int.Parse(x.Groups[1].Value);
            if (!IsValidMonthDay(dateCode)) return null;

            var revision = 0;
            if (x.Groups[2].Success)
            {
                // 只接受 Y1 / Y2 / Y3…；Y0 与「不带 Y」等价，按非法标签处理，避免两个标签映射到同一版本。
                if (!int.TryParse(x.Groups[2].Value, out revision) || revision <= 0) return null;
            }

            return new Version(dateCode, revision, 0);
        }

        // V 系列：明确拒绝 X 标签；允许 V1.2.3 / v1.2.3 / 1.2.3
        if (raw.StartsWith("X", StringComparison.OrdinalIgnoreCase)) return null;
        var v = VTagPattern.Match(raw);
        if (!v.Success) return null;
        var vMajor = int.Parse(v.Groups[1].Value);
        if (vMajor >= XMajorThreshold) return null; // 日期码范围只属于 X 系列
        var vMinor = int.Parse(v.Groups[2].Value);
        var vBuild = v.Groups[3].Success ? int.Parse(v.Groups[3].Value) : 0;
        return new Version(vMajor, vMinor, vBuild);
    }

    /// <summary>日期码 <c>MMdd</c> 是否是合法的「月-日」（2 月按 29 天放行，兼容闰年）。</summary>
    private static bool IsValidMonthDay(int dateCode)
    {
        var month = dateCode / 100;
        var day = dateCode % 100;
        if (month is < 1 or > 12) return false;
        var maxDay = month switch
        {
            2 => 29,
            4 or 6 or 9 or 11 => 30,
            _ => 31,
        };
        return day >= 1 && day <= maxDay;
    }

    /// <summary>按当前系列解析标签。</summary>
    public static Version? ParseCurrentTag(string? tag) => ParseTagForSeries(tag, Current);

    /// <summary>界面显示的版本号：X1031 / X1031Y2 / v3.5.0。</summary>
    public static string FormatVersion(Version? version)
    {
        if (version is null) return "";
        if (version.Major >= XMajorThreshold)
        {
            // 主版本 = MMdd 日期码，次版本 = 同日 Y 序号。日期固定补足四位，X0101 不会显示成 X101。
            var revision = version.Minor > 0 ? $"Y{version.Minor}" : "";
            return $"X{version.Major:D4}{revision}";
        }
        return $"v{version.Major}.{version.Minor}.{version.Build}";
    }

    /// <summary>系列名称（用于日志 / 关于页）。</summary>
    public static string SeriesName(ReleaseSeries series)
        => series == ReleaseSeries.X ? "X" : "V";
}
