using System.Text.RegularExpressions;
using OBS_Helper.Wpf.Localization;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// 文案**调用点**的体检（V2.9.4）。
///
/// 为什么需要它：<see cref="StringsTests"/> 管的是「文案表自己是否自洽」（键集一致、非空、英文无汉字、
/// 中英占位符一致），但**管不到调用点**。而调用点有两种错误是编译器完全看不见的：
///
/// <list type="number">
///   <item><b>调用了不存在的键</b>：<c>Strings.T("no.such.key")</c> 不报错，
///         界面直接显示键名本身（例如 <c>simple.state.stopping</c> 这样一串英文点号）。</item>
///   <item><b>占位符个数对不上</b>：少传时 <c>Strings.T</c> 会保留 <c>{0}</c> 字面量（界面出现 <c>{0}</c>），
///         多传时参数被静默丢弃（用户看不到真正的原因）。</item>
/// </list>
///
/// V2.9.4 就是靠这条检查发现了 3 个真实缺陷：<c>simple.state.stopping</c> 与 <c>main.watchedMore</c>
/// 两个键根本不存在、<c>env.writeFailed</c> 的异常原因被静默丢掉。所以它值得成为常驻测试，
/// 而不是一次性脚本。
///
/// 跳过的情况：键是**拼接**出来的（<c>Strings.T("env.item." + key + ".label")</c>）——
/// 静态分析看不到结果，这类由各页面自己保证（实现里都带「取不到就退回键名」的兜底逻辑）。
/// </summary>
public class StringsCallSiteTests
{
    private static readonly Regex CallForward = new(@"Strings\.T\(""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex Placeholder = new(@"\{(?<index>\d+)[^}]*\}", RegexOptions.Compiled);

    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "OBS_Helper.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("找不到源码根目录（OBS_Helper.slnx）");
    }

    /// <summary>读文案表：键 → 该键值里出现的占位符序号集合。</summary>
    private static Dictionary<string, HashSet<string>> ReadTable(string file)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var keyRegex = new Regex(@"\[""(?<key>[^""]+)""\]\s*=", RegexOptions.Compiled);

        foreach (var line in File.ReadAllLines(file))
        {
            var m = keyRegex.Match(line);
            if (!m.Success) continue;
            var key = m.Groups["key"].Value;
            if (!result.TryGetValue(key, out var slots))
            {
                slots = new HashSet<string>(StringComparer.Ordinal);
                result[key] = slots;
            }
            foreach (Match p in Placeholder.Matches(line))
                slots.Add(p.Groups["index"].Value);
        }
        return result;
    }

    /// <summary>从 <paramref name="open"/>（指向左括号）开始，切出这一层括号内的实参文本。</summary>
    private static string? SliceArguments(string text, int open)
    {
        var depth = 0;
        var inString = false;
        for (var i = open; i < text.Length; i++)
        {
            var ch = text[i];
            if (inString)
            {
                if (ch == '\\') { i++; continue; }
                if (ch == '"') inString = false;
                continue;
            }
            switch (ch)
            {
                case '"': inString = true; break;
                case '(': depth++; break;
                case ')':
                    depth--;
                    if (depth == 0) return text[(open + 1)..i];
                    break;
            }
        }
        return null;   // 括号不闭合：源码里不该出现，交给编译期
    }

    /// <summary>
    /// 数 <c>Strings.T(...)</c> 的实参里，**除第一个（键字面量）之外**还有几个。
    ///
    /// 注意：<paramref name="argsText"/> 拿到的第一个「参数」就是键字面量本身
    /// （键既可以是普通字面量 "a{0}"，也可以是 <c>$"page.{route}.subtitle"</c> 插值串），
    /// 所以参数个数要减 1 —— 少减这一下会让「没有实参的调用」被判成传了 1 个，
    /// 于是全仓所有使用占位符的键都会误报。
    /// </summary>
    private static int CountArguments(string argsText)
    {
        var trimmed = argsText.Trim();
        if (trimmed.Length == 0) return 0;

        var total = 1;
        var depth = 0;
        var inString = false;
        var i = 0;
        while (i < trimmed.Length)
        {
            var ch = trimmed[i];
            if (inString)
            {
                if (ch == '\\') { i += 2; continue; }
                if (ch == '"') inString = false;
                i++;
                continue;
            }

            switch (ch)
            {
                case '@':
                case '$':
                    // 逐字串 / 插值串：修饰符后紧跟引号时，下一个字符就是串的开头
                    if (i + 1 < trimmed.Length && trimmed[i + 1] == '"') { inString = true; i += 2; continue; }
                    break;
                case '"': inString = true; break;
                case '(':
                case '[':
                case '{': depth++; break;
                case ')':
                case ']':
                case '}': depth--; break;
                case ',' when depth == 0: total++; break;
            }
            i++;
        }
        return Math.Max(0, total - 1);
    }

    private static IEnumerable<(string File, string Text)> ProductionSources()
    {
        var projectDir = Path.Combine(SourceRoot(), "OBS_Helper.Wpf");
        return Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(p => (p, File.ReadAllText(p)));
    }

    /// <summary>调用点引用的键必须存在于文案表（不存在时界面会显示键名本身）。</summary>
    [Fact]
    public void EveryLiteralKeyCall_HasATranslation()
    {
        var table = ReadTable(Path.Combine(SourceRoot(), "OBS_Helper.Wpf", "Localization", "StringTableZhHans.cs"));
        var missing = new List<string>();
        var checkedCalls = 0;

        foreach (var (file, text) in ProductionSources())
        {
            foreach (Match m in CallForward.Matches(text))
            {
                checkedCalls++;
                var key = m.Groups[1].Value;
                // 拼接键：字面量以点结尾（"env.item." + ...），静态看不到最终键，跳过
                if (key.EndsWith(".", StringComparison.Ordinal)) continue;
                if (!table.ContainsKey(key))
                    missing.Add($"{Path.GetFileName(file)}: {key}");
            }
        }

        Assert.True(checkedCalls > 300, $"扫描到的 Strings.T 调用只有 {checkedCalls} 处，疑似扫描失效");
        Assert.True(missing.Count == 0,
            $"调用了文案表里不存在的键（界面上会显示键名本身）：{string.Join("; ", missing)}");
    }

    /// <summary>占位符个数必须与调用处实参个数一致（少传 → 界面出现 {0}；多传 → 参数被静默丢弃）。</summary>
    [Fact]
    public void EveryLiteralKeyCall_PassesTheRightArgumentCount()
    {
        var table = ReadTable(Path.Combine(SourceRoot(), "OBS_Helper.Wpf", "Localization", "StringTableZhHans.cs"));
        var problems = new List<string>();
        var checkedCalls = 0;

        foreach (var (file, text) in ProductionSources())
        {
            foreach (Match m in CallForward.Matches(text))
            {
                var key = m.Groups[1].Value;
                if (key.EndsWith(".", StringComparison.Ordinal)) continue;   // 拼接键，跳过
                if (!table.TryGetValue(key, out var slots)) continue;       // 缺键由上一个测试报告

                var open = text.IndexOf('(', m.Index);
                if (open < 0) continue;
                var args = SliceArguments(text, open);
                if (args is null) continue;

                checkedCalls++;
                var got = CountArguments(args);
                var want = slots.Count;

                if (got != want)
                {
                    var shown = args.Length > 70 ? args[..70] + "..." : args;
                    problems.Add($"{Path.GetFileName(file)}: {key} 需要 {want} 个占位符，实际传了 {got} 个（{shown}）");
                }
            }
        }

        Assert.True(checkedCalls > 300, $"核对到的带键调用只有 {checkedCalls} 处，疑似扫描失效");
        Assert.True(problems.Count == 0, "占位符个数与实参个数不一致：" + string.Join("; ", problems));
    }
}
