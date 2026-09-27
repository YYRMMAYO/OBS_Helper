using System.Text.RegularExpressions;
using OBS_Helper.Wpf.Localization;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// 文案表本身的体检（V2.9.2）。
///
/// 文案表是「编译器看不见的代码」：键写错、中英缺一、值写成空串、XAML 引用了不存在的键，
/// 都不会编译报错，而是直接以「界面上出现 nav.home 这种标识」或「英文界面里冒出一句中文」
/// 的形式露给用户。这里把这几类错法全部钉死：
/// <list type="number">
///   <item>两表键集完全一致、无重复键、值不为空（中英对等）；</item>
///   <item>英文表里不出现中日韩汉字（防止漏译后「看着像翻了」）；</item>
///   <item>占位符数量与内容在两表间一致（否则中英会各错各的参数个数）；</item>
///   <item>XAML 里 <c>{DynamicResource Loc.*}</c> 引用的键真实存在（与 scripts/check_resources.py 互补：
///     脚本查的是「非 Loc 前缀」的主题资源，这里查文案键）。</item>
/// </list>
/// </summary>
public class StringsTests
{
    private static readonly Regex KeyRegex = new(@"\[""(?<key>[^""]+)""\]\s*=", RegexOptions.Compiled);
    private static readonly Regex PlaceholderRegex = new(@"\{(?<index>\d+)[^}]*\}", RegexOptions.Compiled);
    private static readonly Regex XamlLocRefRegex = new(@"\{DynamicResource\s+Loc\.(?<key>[^\}\s,]+)\s*\}", RegexOptions.Compiled);

    private static Dictionary<string, string> ParseTable(string relativePath)
    {
        var path = Path.Combine(SourceRoot(), relativePath);
        Assert.True(File.Exists(path), $"文案表文件缺失：{path}");

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var duplicates = new List<string>();

        foreach (var line in File.ReadAllLines(path))
        {
            var m = KeyRegex.Match(line);
            if (!m.Success) continue;

            var key = m.Groups["key"].Value;
            var valueStart = line.IndexOf('=', m.Index) + 1;
            var value = line[valueStart..].Trim();
            if (value.StartsWith('"') && value.EndsWith('"') && value.Length >= 2)
                value = value[1..^1];

            if (!result.TryAdd(key, value)) duplicates.Add(key);
        }

        Assert.True(duplicates.Count == 0, $"文案表有重复键：{string.Join(", ", duplicates)}");
        return result;
    }

    /// <summary>从测试输出目录向上找到含 OBS_Helper.slnx 的目录（即 NOBS/）。</summary>
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

    [Fact]
    public void Tables_HaveIdenticalKeySets()
    {
        var zh = ParseTable(Path.Combine("OBS_Helper.Wpf", "Localization", "StringTableZhHans.cs"));
        var en = ParseTable(Path.Combine("OBS_Helper.Wpf", "Localization", "StringTableEnUs.cs"));

        Assert.True(zh.Count > 500, $"中文文案表条目过少（{zh.Count}），疑似被截断");

        var onlyZh = zh.Keys.Except(en.Keys, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var onlyEn = en.Keys.Except(zh.Keys, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList();

        Assert.True(onlyZh.Count == 0, $"只有中文、英文缺失的键：{string.Join(", ", onlyZh)}");
        Assert.True(onlyEn.Count == 0, $"只有英文、中文缺失的键：{string.Join(", ", onlyEn)}");
    }

    [Fact]
    public void Tables_HaveNoEmptyValues()
    {
        foreach (var (file, table) in new[]
                 {
                     ("zh-Hans", ParseTable(Path.Combine("OBS_Helper.Wpf", "Localization", "StringTableZhHans.cs"))),
                     ("en-US", ParseTable(Path.Combine("OBS_Helper.Wpf", "Localization", "StringTableEnUs.cs"))),
                 })
        {
            var empty = table.Where(kv => string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key).ToList();
            Assert.True(empty.Count == 0, $"{file} 表里存在空文案：{string.Join(", ", empty)}");
        }
    }

    /// <summary>
    /// 英文表里不允许出现汉字：漏译时最容易「复制中文顶上去」，那样英文界面会中英混排，
    /// 而人工翻页很难逐条发现。
    ///
    /// 例外：<c>language.*</c> 是语言选项本身，按惯例用**各自的语言**书写（英文界面里也显示
    /// 「简体中文」），否则看不懂当前界面语言的用户反而找不到自己的那一项。
    /// </summary>
    [Fact]
    public void EnglishTable_ContainsNoCjk()
    {
        var en = ParseTable(Path.Combine("OBS_Helper.Wpf", "Localization", "StringTableEnUs.cs"));
        var cjk = new Regex(@"[\u4e00-\u9fff]", RegexOptions.Compiled);

        var offenders = en
            .Where(kv => !kv.Key.StartsWith("language.", StringComparison.Ordinal))
            .Where(kv => cjk.IsMatch(kv.Value))
            .Select(kv => kv.Key)
            .ToList();
        Assert.True(offenders.Count == 0, $"英文表里仍有中文（漏译）：{string.Join(", ", offenders)}");
    }

    /// <summary>占位符集合必须一致：两表各错各的参数个数时，格式化会静默把 <c>{2}</c> 原样打出来。</summary>
    [Fact]
    public void Tables_HaveMatchingPlaceholders()
    {
        var zh = ParseTable(Path.Combine("OBS_Helper.Wpf", "Localization", "StringTableZhHans.cs"));
        var en = ParseTable(Path.Combine("OBS_Helper.Wpf", "Localization", "StringTableEnUs.cs"));

        foreach (var (key, zhValue) in zh)
        {
            if (!en.TryGetValue(key, out var enValue)) continue;

            static string Slots(string value)
            {
                var indexes = PlaceholderRegex.Matches(value)
                    .Select(m => m.Groups["index"].Value)
                    .Distinct()
                    .OrderBy(x => x, StringComparer.Ordinal);
                return string.Join(",", indexes);
            }

            Assert.Equal(Slots(zhValue), Slots(enValue));
        }
    }

    /// <summary>XAML 里引用的 <c>Loc.*</c> 键必须存在（拼错时界面只显示键名，编译期毫无信号）。</summary>
    [Fact]
    public void XamlDynamicResourceKeys_AllExist()
    {
        var keys = Strings.AllKeys.ToHashSet(StringComparer.Ordinal);
        var root = SourceRoot();
        var projectDir = Path.Combine(root, "OBS_Helper.Wpf");

        var xamls = Directory.EnumerateFiles(projectDir, "*.xaml", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();
        Assert.NotEmpty(xamls);

        var missing = new List<string>();
        var referenced = 0;
        foreach (var xaml in xamls)
        {
            foreach (Match m in XamlLocRefRegex.Matches(File.ReadAllText(xaml)))
            {
                referenced++;
                var key = m.Groups["key"].Value;
                if (!keys.Contains(key))
                    missing.Add($"{Path.GetFileName(xaml)}: Loc.{key}");
            }
        }

        Assert.True(referenced > 100, $"XAML 里的 Loc.* 引用只有 {referenced} 处，疑似正则失效");
        Assert.True(missing.Count == 0, $"XAML 引用了不存在的文案键：{string.Join("; ", missing)}");
    }

    [Theory]
    [InlineData("zh-Hans", Strings.ZhHans)]
    [InlineData("zh", Strings.ZhHans)]
    [InlineData("zh-CN", Strings.ZhHans)]
    [InlineData("chinese", Strings.ZhHans)]
    [InlineData("chinesesimplified", Strings.ZhHans)]
    [InlineData("en-US", Strings.EnUs)]
    [InlineData("en", Strings.EnUs)]
    [InlineData("english", Strings.EnUs)]
    [InlineData("de-DE", Strings.ZhHans)]
    [InlineData("", Strings.ZhHans)]
    [InlineData(null, Strings.ZhHans)]
    public void Normalize_MapsToSupportedLanguage(string? input, string expected)
        => Assert.Equal(expected, Strings.Normalize(input));

    [Fact]
    public void Normalize_IsCaseInsensitive()
    {
        Assert.Equal(Strings.EnUs, Strings.Normalize("EN-us"));
        Assert.Equal(Strings.EnUs, Strings.Normalize("English"));
        Assert.Equal(Strings.ZhHans, Strings.Normalize("ZH-HANS"));
    }

    [Fact]
    public void DisplayName_IsWrittenInItsOwnLanguage()
    {
        // 语言选项用各自的语言书写：用户看不懂当前界面语言时也能找到自己的那一项
        Assert.Equal("简体中文", Strings.DisplayName(Strings.ZhHans));
        Assert.Equal("English", Strings.DisplayName(Strings.EnUs));
        Assert.Equal("简体中文", Strings.DisplayName(null));
    }

    [Fact]
    public void T_FallsBackToChineseThenToTheKeyItself()
    {
        var original = Strings.Current;
        try
        {
            Strings.SetLanguage(Strings.EnUs);
            // 两个表都有：取英文
            Assert.Equal("Settings", Strings.T("nav.settings"));
            // 两个表都没有：回退成键名本身（界面上会显示键名，一眼可见，便于发现问题）
            Assert.Equal("no.such.key", Strings.T("no.such.key"));
            // 空键不抛异常
            Assert.Equal("", Strings.T(""));
        }
        finally
        {
            Strings.SetLanguage(original);
        }
    }

    [Fact]
    public void T_FormatsPlaceholders_AndSurvivesTooFewArguments()
    {
        // 正常格式化（注意中英的表括号样式不同：中文用全角括号）
        Assert.Equal("[OBS900] x（extra）", Strings.T("err.formatWithExtra", "OBS900", "x", "extra"));
        // 少传参数时返回模板本身，不抛异常（界面不能因为一句文案就炸）
        Assert.Equal("完成：{0}", Strings.T("recording.remux.done"));
        Assert.Equal("完成：a.mkv", Strings.T("recording.remux.done", "a.mkv"));
    }

    [Fact]
    public void SetLanguage_ReportsWhetherItChanged()
    {
        var original = Strings.Current;
        try
        {
            Strings.SetLanguage(original);
            Assert.False(Strings.SetLanguage(original), "同语言重复设置不应报告「已变化」");

            var target = original == Strings.EnUs ? Strings.ZhHans : Strings.EnUs;
            Assert.True(Strings.SetLanguage(target));
            Assert.Equal(target, Strings.Current);
        }
        finally
        {
            Strings.SetLanguage(original);
        }
    }

    [Fact]
    public void ResourceKey_PrefixesWithLoc()
    {
        Assert.Equal("Loc.nav.home", Strings.ResourceKey("nav.home"));
        Assert.StartsWith(Strings.ResourceKeyPrefix, Strings.ResourceKey("anything"));
    }

    /// <summary>
    /// 语言数量与默认语言：目前只有中文与英文两种，且默认必须是中文（安装向导同此约定）。
    /// 加语言时这条测试会失败，提醒同步补 .isl 与文案表。
    /// </summary>
    [Fact]
    public void SupportedLanguages_AreChineseAndEnglish_WithChineseFirst()
    {
        Assert.Equal(new[] { Strings.ZhHans, Strings.EnUs }, Strings.Supported);
        Assert.Equal(Strings.ZhHans, Strings.Normalize(null));
    }
}

/// <summary>
/// 数据驱动展示值的跨语言判定（V2.9.2）：知识库里的 severity / level 是展示文案，
/// 中英各一套；换语言后本地已下载的外部知识库可能是另一种语言写的，判定必须两种都认。
/// </summary>
public class DataValuesTests
{
    [Theory]
    [InlineData("严重", DataValues.SeverityKind.Critical)]
    [InlineData("Critical", DataValues.SeverityKind.Critical)]
    [InlineData("常见", DataValues.SeverityKind.Common)]
    [InlineData("Common", DataValues.SeverityKind.Common)]
    [InlineData("一般", DataValues.SeverityKind.Normal)]
    [InlineData("Occasional", DataValues.SeverityKind.Normal)]
    [InlineData("进阶", DataValues.SeverityKind.Advanced)]
    [InlineData("Advanced", DataValues.SeverityKind.Advanced)]
    [InlineData("罕见", DataValues.SeverityKind.Other)]
    [InlineData("", DataValues.SeverityKind.Other)]
    [InlineData(null, DataValues.SeverityKind.Other)]
    public void ClassifySeverity_AcceptsBothLanguages(string? input, DataValues.SeverityKind expected)
        => Assert.Equal(expected, DataValues.ClassifySeverity(input));

    [Theory]
    [InlineData("进阶", true)]
    [InlineData("Advanced", true)]
    [InlineData("基础", false)]
    [InlineData("Basic", false)]
    [InlineData(null, false)]
    public void IsAdvancedLevel_AcceptsBothLanguages(string? input, bool expected)
        => Assert.Equal(expected, DataValues.IsAdvancedLevel(input));

    [Theory]
    [InlineData("热门", true)]
    [InlineData("Popular", true)]
    [InlineData("推荐", false)]
    [InlineData("Recommended", false)]
    public void IsHotBadge_AcceptsBothLanguages(string? input, bool expected)
        => Assert.Equal(expected, DataValues.IsHotBadge(input));
}

/// <summary>
/// 严重度文案映射必须同时认中英两套（外部热更新数据可能是任一语言）。
/// </summary>
public class DiagnosticSeverityMapperLocalizationTests
{
    [Theory]
    [InlineData("严重", OBS_Helper.Wpf.Services.Ai.DiagnosticSeverity.Critical)]
    [InlineData("Critical", OBS_Helper.Wpf.Services.Ai.DiagnosticSeverity.Critical)]
    [InlineData("错误", OBS_Helper.Wpf.Services.Ai.DiagnosticSeverity.Error)]
    [InlineData("Error", OBS_Helper.Wpf.Services.Ai.DiagnosticSeverity.Error)]
    [InlineData("警告", OBS_Helper.Wpf.Services.Ai.DiagnosticSeverity.Warning)]
    [InlineData("Warning", OBS_Helper.Wpf.Services.Ai.DiagnosticSeverity.Warning)]
    [InlineData("一般", OBS_Helper.Wpf.Services.Ai.DiagnosticSeverity.Warning)]
    [InlineData("Occasional", OBS_Helper.Wpf.Services.Ai.DiagnosticSeverity.Warning)]
    [InlineData("常见", OBS_Helper.Wpf.Services.Ai.DiagnosticSeverity.Suggestion)]
    [InlineData("Common", OBS_Helper.Wpf.Services.Ai.DiagnosticSeverity.Suggestion)]
    [InlineData("unknown", OBS_Helper.Wpf.Services.Ai.DiagnosticSeverity.Suggestion)]
    public void Map_String_AcceptsChineseAndEnglish(string? input, OBS_Helper.Wpf.Services.Ai.DiagnosticSeverity expected)
        => Assert.Equal(expected, OBS_Helper.Wpf.Services.Ai.DiagnosticSeverityMapper.Map(input));
}

/// <summary>
/// 首启语言的解析次序：用户选择 &gt; 安装向导写的 language.ini &gt; 简体中文。
///
/// <c>LocalizationService.ReadInstallerLanguage</c> 依赖 <c>AppContext.BaseDirectory</c>（WPF 侧文件），
/// 不能链接进纯逻辑单测；这里钉住它最终依赖的那一步契约 —— Inno Setup 的 <c>{language}</c> 常量值
/// （<c>chinesesimplified</c> / <c>english</c>）必须能被归一化到受支持语言，否则安装向导选了也没用。
/// </summary>
public class LocalizationLanguageResolutionTests
{
    [Theory]
    [InlineData("chinesesimplified", Strings.ZhHans)]
    [InlineData("chinese", Strings.ZhHans)]
    [InlineData("english", Strings.EnUs)]
    [InlineData("en-US", Strings.EnUs)]
    [InlineData("zh-Hans", Strings.ZhHans)]
    [InlineData("de-DE", Strings.ZhHans)]
    public void InstallerLanguage_WouldResolveToSupportedLanguage(string installerLanguage, string expected)
        => Assert.Equal(expected, Strings.Normalize(installerLanguage));
}
