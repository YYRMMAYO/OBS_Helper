using System.Globalization;
using System.Text.RegularExpressions;

namespace OBS_Helper.Wpf.Services.Update;

/// <summary>
/// 更新渠道定义 —— <b>本文件是 dev（测试）分支专属</b>，主分支没有它。
///
/// dev 分支与主分支的版本体系完全独立：
/// <list type="bullet">
///   <item><b>主分支</b>：tag 形如 <c>V3.1.0</c>，按 <see cref="Version"/> 比较，出安装包 + 增量包；</item>
///   <item><b>dev 分支</b>：tag 形如 <c>01</c> / <c>02</c> / <c>03</c>（<b>纯数字、不带字母 V</b>），
///     彼此按「构建序号」递增，更新只从 <b>dev 的 Release 页</b>获取。</item>
/// </list>
///
/// 两套编号没有可比性（<c>01</c> 和 <c>3.1.0</c> 谁新？），所以 dev 构建<b>只认 dev tag、只把
/// dev Release 当更新来源</b>；反过来主分支的解析器要求 <c>X.Y[.Z]</c>，纯数字 tag 解析不出，
/// 因此仓库里两类 tag 共存也不会互相串版本。
/// </summary>
public static class UpdateChannel
{
    /// <summary>本构建是否为 DEV（测试）通道。dev 分支恒为 <c>true</c>。</summary>
    public const bool IsDev = true;

    /// <summary>
    /// 当前 DEV 构建序号，对应 dev tag：1 → <c>01</c>、2 → <c>02</c> …
    /// <b>每次出 dev 构建都要 +1</b>，否则已经装了上一个 dev 版的用户收不到更新提示。
    /// </summary>
    public const int DevBuildNumber = 1;

    /// <summary>把构建序号格式化成 tag 形态（1 → <c>"01"</c>）。</summary>
    public static string FormatDevBuild(int build) => build.ToString("D2", CultureInfo.InvariantCulture);

    /// <summary>当前构建的 tag 形态（<c>"01"</c>）。</summary>
    public static string DevBuildLabel => FormatDevBuild(DevBuildNumber);

    /// <summary>当前构建的完整显示名（<c>"DEV 01"</c>）。</summary>
    public static string DevVersionLabel => "DEV " + DevBuildLabel;

    /// <summary>
    /// dev tag 只接受 1~3 位纯数字（<c>01</c> / <c>7</c> / <c>123</c>）：
    /// 带 <c>V</c> 前缀、带点、带后缀（<c>V01</c> / <c>1.3.0</c> / <c>01-beta</c>）一律不认 ——
    /// 这正是「dev 编号不带字母 V」这条约定的落地位置。
    /// </summary>
    private static readonly Regex DevTagPattern = new(
        @"^\d{1,3}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// 解析 dev tag → 构建序号。不是 dev tag 时返回 <c>false</c>（<paramref name="build"/> 为 0）。
    /// </summary>
    public static bool TryParseDevTag(string? tag, out int build)
    {
        build = 0;
        var s = (tag ?? "").Trim();
        if (!DevTagPattern.IsMatch(s)) return false;
        return int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out build);
    }
}
