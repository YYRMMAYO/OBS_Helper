using System.Globalization;

namespace OBS_Helper.Wpf.Localization;

/// <summary>
/// 界面文案表的运行时入口（V2.9.2）。
///
/// 设计要点：
/// <list type="bullet">
///   <item><b>纯 BCL、零 WPF 依赖</b>：日志分析、录前自检、各类体检核心等「纯逻辑」文件
///     会被单测工程直接链接编译（见 <c>OBS_Helper.Wpf.Tests.csproj</c>），它们也要取文案，
///     因此文案表必须能在普通 net10.0 运行时里工作，不能碰 <c>System.Windows</c>。</item>
///   <item><b>键 = 逻辑标识，值 = 展示文案</b>：键一律用小写点分命名（<c>nav.home</c> /
///     <c>log.rule.LOG-ENC-OVERLOAD.title</c>），与语言无关；XAML 里用
///     <c>{DynamicResource Loc.nav.home}</c> 引同一批键（见 <see cref="ResourceKey"/>）。</item>
///   <item><b>缺键可见</b>：查不到时按「当前语言 → 中文 → 键本身」回退。回退到键本身意味着
///     界面上会直接出现 <c>nav.home</c> 这种字符串 —— 丑得一眼能看出来，好过静默空白。</item>
/// </list>
///
/// 文案表的实际内容在 <see cref="StringTableZhHans"/> / <see cref="StringTableEnUs"/>。
/// </summary>
public static class Strings
{
    /// <summary>简体中文（默认语言）。</summary>
    public const string ZhHans = "zh-Hans";

    /// <summary>英语（V2.9.2 新增）。</summary>
    public const string EnUs = "en-US";

    /// <summary>支持的语言（顺序即界面上的展示顺序，第一项为默认）。由语言注册表驱动。</summary>
    public static readonly string[] Supported = LanguageRegistry.Codes.ToArray();

    /// <summary>XAML 资源键前缀：<c>nav.home</c> 在 XAML 里写作 <c>{DynamicResource Loc.nav.home}</c>。</summary>
    public const string ResourceKeyPrefix = "Loc.";

    private static readonly object Gate = new();
    private static string _current = ZhHans;

    /// <summary>当前语言（<see cref="ZhHans"/> 或 <see cref="EnUs"/>）。</summary>
    public static string Current
    {
        get
        {
            lock (Gate) return _current;
        }
    }

    /// <summary>语言切换后触发（同语言重复设置不触发）。UI 侧据此刷新代码里拼出来的文案。</summary>
    public static event Action? LanguageChanged;

    /// <summary>语言是否受支持。</summary>
    public static bool IsSupported(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return false;
        foreach (var s in Supported)
        {
            if (string.Equals(s, language.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>
    /// 把任意输入归一化为受支持的语言标识。
    ///
    /// V3.0（D9）起**由 <see cref="LanguageRegistry"/> 的别名表驱动**，不再是「zh 或 en 二选一」——
    /// 加一门语言只需要注册它并写好别名，这里不需要再改一行判断。
    /// 认不出来的一律回退默认语言（简体中文）。
    /// </summary>
    public static string Normalize(string? language) => LanguageRegistry.Resolve(language).Code;

    /// <summary>
    /// 切换当前语言。返回是否发生实际变化（同语言重复设置返回 false）。
    /// 只改状态与广播事件，不碰 WPF —— 把文案写进 <c>Application.Resources</c> 是
    /// <c>Services/LocalizationService</c> 的事。
    /// </summary>
    public static bool SetLanguage(string? language)
    {
        var target = Normalize(language);
        lock (Gate)
        {
            if (string.Equals(_current, target, StringComparison.Ordinal)) return false;
            _current = target;
        }

        LanguageChanged?.Invoke();
        return true;
    }

    /// <summary>按 <paramref name="language"/> 取文案表（未支持的语言回退默认语言）。</summary>
    public static IReadOnlyDictionary<string, string> Table(string? language)
        => LanguageRegistry.Resolve(language).Table;

    /// <summary>当前语言的文案表。</summary>
    public static IReadOnlyDictionary<string, string> Table() => Table(Current);

    /// <summary>全部键（两种语言的并集，用于自检与单测）。</summary>
    public static IEnumerable<string> AllKeys
    {
        get
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var k in StringTableZhHans.Table.Keys) keys.Add(k);
            foreach (var k in StringTableEnUs.Table.Keys) keys.Add(k);
            return keys;
        }
    }

    /// <summary>取文案。缺失时按「当前语言 → 中文 → 键本身」回退。</summary>
    public static string T(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";

        var table = Table();
        if (table.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value)) return value;

        if (StringTableZhHans.Table.TryGetValue(key, out var zh) && !string.IsNullOrEmpty(zh)) return zh;

        // 回退到键本身：界面上会直接显示 'nav.home' 这类标识，一眼可见，便于发现问题
        return key;
    }

    /// <summary>
    /// 取带占位符的文案并格式化（<c>{0}</c> / <c>{1}</c>…）。
    ///
    /// 用**当前区域**（<see cref="CultureInfo.CurrentCulture"/>）格式化：数字与百分比的呈现
    /// 本来就该跟随系统区域（<c>{0:P0}</c> 在中文区域是 <c>10%</c>、不变区域是 <c>10 %</c>），
    /// 这也是改造前字符串插值的既有行为 —— 换文案表不该顺带改变数字的排版。
    /// </summary>
    public static string T(string key, params object?[] args)
    {
        var template = T(key);
        if (args is null || args.Length == 0) return template;
        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, args);
        }
        catch (FormatException)
        {
            // 占位符与参数对不上：不能让「少一个参数」把整个界面炸掉，原样返回模板
            return template;
        }
    }

    /// <summary>XAML 资源键：<c>nav.home</c> → <c>Loc.nav.home</c>。</summary>
    public static string ResourceKey(string key) => ResourceKeyPrefix + key;

    /// <summary>语言的展示名（各语言用各自的语言书写：简体中文 / English）。由注册表提供。</summary>
    public static string DisplayName(string? language) => LanguageRegistry.Resolve(language).DisplayName;
}
