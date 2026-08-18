using System;

namespace OBS_Helper.Wpf.Services.Update;

/// <summary>知识库版本号比较（纯逻辑、可单测）。知识库版本形如 "1.4" / "1.5.2"，与程序集版本完全解耦。</summary>
public static class KbVersion
{
    /// <summary>解析知识库版本号；空串 / 非法一律视为 0.0，便于「远程比本地新」判定。</summary>
    public static Version Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new Version(0, 0);
        var t = text.Trim();
        // 去掉尾部修饰（"1.5-beta" / "1.5 (稳定)" 之类）
        var cut = t.IndexOfAny(new[] { ' ', '-', '+' });
        if (cut >= 0) t = t[..cut];
        // Version.TryParse 不接受单段版本串（"2"），补 .0 再解析
        if (!t.Contains('.')) t += ".0";
        return Version.TryParse(t, out var v) ? v : new Version(0, 0);
    }

    /// <summary>remote 比 current 新返回 true。</summary>
    public static bool IsNewer(string? current, string? remote)
        => Parse(remote) > Parse(current);
}
