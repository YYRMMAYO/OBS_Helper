using System.Globalization;
using System.IO;
using System.Text;

namespace OBS_Helper.Wpf.Services.Recording;

/// <summary>命名模板可用的上下文（全部来自本机，不联网）。</summary>
public sealed record RecordingNamingContext(
    DateTime Now,
    string Scene,
    string Profile,
    int Width,
    int Height,
    double Fps,
    string Extension);

/// <summary>
/// 录像文件命名模板（V3.1.0 / §12.5.1 第 2 项）。
///
/// 支持变量：<c>{date}</c> <c>{time}</c> <c>{datetime}</c> <c>{scene}</c>
/// <c>{profile}</c> <c>{resolution}</c> <c>{fps}</c> <c>{ext}</c>。
///
/// 安全口径：
/// <list type="bullet">
///   <item>非法字符一律替换成 <c>_</c>，不会拼出路径分隔符；</item>
///   <item>总长度上限 120 字符，避免超过 MAX_PATH；</item>
///   <item>重命名失败时保留原文件（由调用方负责），绝不删除；</item>
///   <item>模板为空表示「不改名」，保持 OBS 自己的文件名。</item>
/// </list>
/// </summary>
public static class RecordingNamingCore
{
    public const int MaxNameLength = 120;

    /// <summary>推荐模板（用户可在设置里改）。</summary>
    public const string RecommendedTemplate = "{date}_{time}_{scene}";

    /// <summary>支持的变量（供设置页说明与校验）。</summary>
    public static readonly string[] Variables =
        { "{date}", "{time}", "{datetime}", "{scene}", "{profile}", "{resolution}", "{fps}", "{ext}" };

    /// <summary>把模板套成文件名。模板为空返回空串（表示不改名）。</summary>
    public static string Apply(string? template, RecordingNamingContext context)
    {
        if (string.IsNullOrWhiteSpace(template)) return "";

        var ext = NormalizeExtension(context.Extension);
        var result = template
            .Replace("{date}", context.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Replace("{time}", context.Now.ToString("HH-mm-ss", CultureInfo.InvariantCulture))
            .Replace("{datetime}", context.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture))
            .Replace("{scene}", context.Scene ?? "")
            .Replace("{profile}", context.Profile ?? "")
            .Replace("{resolution}", context.Width > 0 && context.Height > 0
                ? context.Width + "x" + context.Height
                : "")
            .Replace("{fps}", context.Fps > 0
                ? Math.Round(context.Fps).ToString("0", CultureInfo.InvariantCulture)
                : "")
            .Replace("{ext}", ext.TrimStart('.'));

        result = Sanitize(result);
        if (result.Length == 0) return "";

        // 模板里没带扩展名时补上原扩展名，避免生成无后缀文件
        if (ext.Length > 0 && !result.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            result += ext;

        return result;
    }

    /// <summary>生成设置页的预览文件名（固定时间，避免预览每秒抖动）。</summary>
    public static string Preview(string? template)
        => Apply(template, new RecordingNamingContext(
            new DateTime(2026, 10, 18, 20, 30, 15),
            "游戏",
            "默认",
            1920,
            1080,
            60,
            ".mkv"));

    /// <summary>
    /// 清洗文件名：非法字符替换为 <c>_</c>，去掉首尾空白与点，压缩连续下划线，限制长度。
    /// 只处理文件名（不含目录）。
    /// </summary>
    public static string Sanitize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";

        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (c == '\r' || c == '\n' || c == '\t') sb.Append('_');
            else if (Array.IndexOf(invalid, c) >= 0 || c == '/' || c == '\\' || c == ':') sb.Append('_');
            else sb.Append(c);
        }

        var cleaned = sb.ToString().Trim().Trim('.', ' ');
        while (cleaned.Contains("__", StringComparison.Ordinal))
            cleaned = cleaned.Replace("__", "_", StringComparison.Ordinal);
        while (cleaned.Contains("..", StringComparison.Ordinal))
            cleaned = cleaned.Replace("..", "_", StringComparison.Ordinal);

        if (cleaned.Length > MaxNameLength)
        {
            var ext = Path.GetExtension(cleaned);
            var stemLength = Math.Max(1, MaxNameLength - ext.Length);
            cleaned = cleaned[..Math.Min(cleaned.Length, stemLength)].TrimEnd('.', ' ') + ext;
        }

        return cleaned;
    }

    /// <summary>模板是否至少包含一个已知变量；空模板视为「关闭」，返回 false 不报错。</summary>
    public static bool HasKnownVariable(string? template)
    {
        if (string.IsNullOrWhiteSpace(template)) return false;
        foreach (var v in Variables)
        {
            if (template.Contains(v, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>模板里未知的 <c>{...}</c> 变量（供设置页提示，不影响保存）。</summary>
    public static IReadOnlyList<string> UnknownVariables(string? template)
    {
        var unknown = new List<string>();
        if (string.IsNullOrWhiteSpace(template)) return unknown;

        var index = 0;
        while (index < template.Length)
        {
            var open = template.IndexOf('{', index);
            if (open < 0) break;
            var close = template.IndexOf('}', open + 1);
            if (close < 0) break;

            var token = template.Substring(open, close - open + 1);
            if (!Variables.Contains(token, StringComparer.OrdinalIgnoreCase)
                && !unknown.Contains(token, StringComparer.OrdinalIgnoreCase))
                unknown.Add(token);

            index = close + 1;
        }
        return unknown;
    }

    /// <summary>在目标目录里选一个不冲突的文件名（同名时追加 _2 / _3…）。</summary>
    public static string EnsureUniquePath(string directory, string fileName)
    {
        var safe = Sanitize(fileName);
        if (safe.Length == 0) safe = "recording";

        var candidate = Path.Combine(directory, safe);
        if (!File.Exists(candidate)) return candidate;

        var ext = Path.GetExtension(safe);
        var stem = Path.GetFileNameWithoutExtension(safe);
        for (var i = 2; i < 1000; i++)
        {
            var next = Path.Combine(directory, stem + "_" + i + ext);
            if (!File.Exists(next)) return next;
        }
        return Path.Combine(directory, Guid.NewGuid().ToString("N") + ext);
    }

    private static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return "";
        var ext = extension.Trim();
        if (!ext.StartsWith(".", StringComparison.Ordinal)) ext = "." + ext;
        return ext;
    }
}
