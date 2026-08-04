using System.Text;
using System.Text.RegularExpressions;

namespace OBS_Helper.Client.Services.Markdown;

/// <summary>
/// 极简、安全的 Markdown → HTML 渲染器，仅供应用内渲染自带的《排障指引》。
/// 不依赖任何第三方库；原始文本先做 HTML 转义，仅输出受控标签，杜绝注入。
/// 支持语法：标题(#~######)、段落、无序列表(-/*/+)、有序列表(1.)、
/// 代码块(```)、引用(&gt;)、分隔线(---/***)、以及行内 **粗体** *斜体* `代码` [链接](url)。
/// </summary>
public static class MarkdownRenderer
{
    private static readonly Regex HeadingRegex = new(@"^([#]{1,6})\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex CodeSpanRegex = new(@"`([^`]+)`", RegexOptions.Compiled);
    private static readonly Regex BoldRegex = new(@"\*\*([^*]+)\*\*", RegexOptions.Compiled);
    private static readonly Regex ItalicRegex = new(@"\*([^*]+)\*", RegexOptions.Compiled);
    private static readonly Regex LinkRegex = new(@"\[([^\]]+)\]\(([^)]+)\)", RegexOptions.Compiled);

    public static string RenderToHtml(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return string.Empty;

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        var i = 0;

        while (i < lines.Length)
        {
            var line = lines[i];

            // 代码块
            if (line.TrimStart().StartsWith("```"))
            {
                var code = new StringBuilder();
                i++;
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("```"))
                {
                    code.Append(lines[i]).Append('\n');
                    i++;
                }
                i++; // 跳过结束的 ```
                sb.Append("<pre><code>").Append(Escape(code.ToString())).Append("</code></pre>");
                continue;
            }

            // 空行
            if (string.IsNullOrWhiteSpace(line)) { i++; continue; }

            var trimmed = line.Trim();

            // 分隔线
            if (trimmed is "---" or "***" or "___")
            {
                sb.Append("<hr/>");
                i++;
                continue;
            }

            // 标题
            var hm = HeadingRegex.Match(line);
            if (hm.Success)
            {
                var level = hm.Groups[1].Value.Length;
                sb.Append('<').Append('h').Append(level).Append('>')
                  .Append(Inline(hm.Groups[2].Value))
                  .Append("</h").Append(level).Append('>');
                i++;
                continue;
            }

            // 引用
            if (trimmed.StartsWith(">"))
            {
                var quote = new StringBuilder();
                while (i < lines.Length && lines[i].TrimStart().StartsWith(">"))
                {
                    var content = lines[i].TrimStart();
                    if (content.StartsWith(">")) content = content.Substring(1);
                    quote.Append(content.Trim()).Append('\n');
                    i++;
                }
                sb.Append("<blockquote>").Append(Inline(quote.ToString())).Append("</blockquote>");
                continue;
            }

            // 列表（有序 / 无序）
            if (IsListStart(trimmed))
            {
                var ordered = char.IsDigit(trimmed[0]);
                var tag = ordered ? "ol" : "ul";
                var items = new StringBuilder();
                while (i < lines.Length && IsListStart(lines[i].Trim()))
                {
                    var item = lines[i].Trim();
                    string content;
                    if (item[0] == '*' || item[0] == '-' || item[0] == '+')
                        content = item.Substring(1).Trim();
                    else
                        content = item.Substring(item.IndexOf('.') + 1).Trim();
                    items.Append("<li>").Append(Inline(content)).Append("</li>");
                    i++;
                }
                sb.Append('<').Append(tag).Append('>').Append(items).Append("</").Append(tag).Append('>');
                continue;
            }

            // 段落：收集连续非空、非块起始的行
            var para = new StringBuilder();
            while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) && !IsBlockStart(lines[i]))
            {
                if (para.Length > 0) para.Append("<br/>");
                para.Append(Inline(lines[i]));
                i++;
            }
            sb.Append("<p>").Append(para).Append("</p>");
        }

        return sb.ToString();
    }

    private static bool IsListStart(string trimmed)
    {
        if (trimmed.Length == 0) return false;
        if (trimmed[0] == '*' || trimmed[0] == '-' || trimmed[0] == '+')
            return trimmed.Length > 1 && char.IsWhiteSpace(trimmed[1]);
        // 有序列表：数字 + '.' + 空格
        var dot = trimmed.IndexOf('.');
        return dot > 0 && dot <= 3 && trimmed[dot + 1] == ' '
            && int.TryParse(trimmed.Substring(0, dot), out _);
    }

    private static bool IsBlockStart(string line)
    {
        var t = line.TrimStart();
        if (t.StartsWith("```")) return true;
        if (t.StartsWith(">")) return true;
        if (IsListStart(t)) return true;
        if (HeadingRegex.IsMatch(line)) return true;
        var tr = t.Trim();
        return tr is "---" or "***" or "___";
    }

    private static string Inline(string text)
    {
        var s = Escape(text);
        s = CodeSpanRegex.Replace(s, m => "<code>" + m.Groups[1].Value + "</code>");
        s = BoldRegex.Replace(s, "<strong>$1</strong>");
        s = ItalicRegex.Replace(s, "<em>$1</em>");
        s = LinkRegex.Replace(s, m =>
        {
            var label = m.Groups[1].Value;
            var url = m.Groups[2].Value;
            return IsSafeUrl(url)
                ? $"<a href=\"{url}\" target=\"_blank\" rel=\"noopener noreferrer\">{label}</a>"
                : label;
        });
        return s;
    }

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary>仅允许 http/https/mailto 与站内相对地址；含引号或空白视为不安全，直接丢弃链接。</summary>
    private static bool IsSafeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (url.Contains('"') || url.Contains('<') || url.Contains('>') || url.Contains(' '))
            return false;
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            return true;
        return url.StartsWith("/") || url.StartsWith("#") || url.StartsWith("./") || url.StartsWith("../");
    }
}
