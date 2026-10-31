using System.Text;

namespace OBS_Helper.Wpf.Services.Shell;

/// <summary>
/// 页面级自检输出：把「这个模块现在能不能用 / 缺什么」直接列给用户，
/// 避免用户点了一圈按钮却不知道是没配置、没连接还是功能坏了。
/// </summary>
public static class SelfCheckResult
{
    public static string Ok(string label, string? detail = null)
        => Line("✓", label, Strings.T("selfcheck.ok"), detail);

    public static string Warn(string label, string? detail = null)
        => Line("!", label, Strings.T("selfcheck.warn"), detail);

    public static string Fail(string label, string? detail = null)
        => Line("✗", label, Strings.T("selfcheck.fail"), detail);

    private static string Line(string mark, string label, string status, string? detail)
    {
        var sb = new StringBuilder();
        sb.Append(mark).Append(' ').Append(label).Append(": ").Append(status);
        if (!string.IsNullOrWhiteSpace(detail)) sb.Append(" · ").Append(detail);
        return sb.ToString();
    }

    public static string Join(IEnumerable<string> lines)
        => string.Join(Environment.NewLine, lines);
}
