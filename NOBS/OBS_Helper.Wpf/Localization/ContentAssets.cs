using System.IO;
using System.Reflection;
using System.Text;

namespace OBS_Helper.Wpf.Localization;

/// <summary>
/// 随包离线内容的「按语言取资产」统一入口（V2.9.3）。
///
/// V2.9.2 把**界面与代码内文案**做成了中英双份；本版把剩下的**数据资产**也补齐：
/// <list type="bullet">
///   <item><c>problems.json</c> / <c>problems.en-US.json</c> —— 知识库 212 条；</item>
///   <item><c>plugins.json</c> / <c>plugins.en-US.json</c> —— 插件广场 57 条；</item>
///   <item><c>scene_templates.json</c> / <c>scene_templates.en-US.json</c> —— 场景模板 12 套；</item>
///   <item><c>troubleshooting.md</c> / <c>troubleshooting.en-US.md</c> —— 排障指引。</item>
/// </list>
///
/// <b>中文文件名刻意保持不变</b>：现有中文热更新地址（GitHub raw）、Release 资产名、
/// 以及用户本机已下载的缓存文件都依赖 <c>problems.json</c> 这个名字，改了就是静默回归。
/// 英文资产是新文件，后缀 <c>.en-US</c>。
///
/// 解析次序固定为：**当前语言的外部缓存 → 当前语言的随包内嵌 → 中文随包内嵌**。
/// 最后一级兜底**必须写 WARN 日志** —— 否则英文用户会「安静地看到中文内容」，
/// 正是 V2.9.1 修 raw 404 时踩过的那个坑（静默失败能潜伏很久）。
///
/// 本文件是纯逻辑（只依赖 BCL），单测工程直接链接编译；真正的内嵌资源读取在
/// <see cref="ReadEmbedded"/>，非 WPF 依赖，同样可在测试运行时里调用（只是测试程序集里没有这些资源）。
/// </summary>
public static class ContentAssets
{
    /// <summary>内嵌资源前缀（与 csproj 的 LogicalName 一一对应）。</summary>
    public const string ResourcePrefix = "OBS_Helper.Wpf.Assets.";

    /// <summary>英文资产的文件名/资源名后缀：<c>problems</c> → <c>problems.en-US.json</c>。</summary>
    public const string EnglishSuffix = ".en-US";

    /// <summary>知识库资产基名（无扩展名）。</summary>
    public const string Problems = "problems";
    /// <summary>插件目录资产基名。</summary>
    public const string Plugins = "plugins";
    /// <summary>场景模板资产基名。</summary>
    public const string SceneTemplates = "scene_templates";
    /// <summary>排障指引资产基名。</summary>
    public const string Troubleshooting = "troubleshooting";

    /// <summary>资产基名 → 扩展名（只有指引是 Markdown）。</summary>
    public static string Extension(string baseName)
        => baseName == Troubleshooting ? ".md" : ".json";

    /// <summary>
    /// 「基名 + 语言后缀 + 扩展名」的通用拼法，供缓存文件名 / 状态文件等复用。
    ///
    /// V3.0（D9）：后缀取自 <see cref="LanguageRegistry"/>，不再是「英文？加 .en-US : 不加」——
    /// 加一门语言时只需在注册表里填它的后缀，这里不必再改。
    /// </summary>
    public static string SuffixedFileName(string stem, string extension, string? language)
        => stem + LanguageRegistry.ContentSuffixOf(language) + extension;

    /// <summary>资产基名 → 带语言后缀的文件名（<c>problems</c> + en-US → <c>problems.en-US.json</c>）。</summary>
    public static string FileName(string baseName, string? language)
        => SuffixedFileName(baseName, Extension(baseName), language);

    /// <summary>资产基名 → 内嵌资源名。</summary>
    public static string ResourceName(string baseName, string? language)
        => ResourcePrefix + FileName(baseName, language);

    /// <summary>
    /// 给定语言是否使用英文后缀。保留此 API 是为了兼容既有调用点；
    /// 新代码请直接用 <see cref="LanguageRegistry.ContentSuffixOf"/>（它支持任意多种语言）。
    /// </summary>
    public static bool IsEnglish(string? language)
        => string.Equals(LanguageRegistry.ContentSuffixOf(language), EnglishSuffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 读出某个资产的文本。返回 null 表示当前语言与中文内嵌都缺失（调用方据此报错）。
    /// <paramref name="reader"/> 由调用方提供（生产环境是内嵌资源读取器，单测里是假数据），
    /// 这样「回退次序」这段逻辑可以脱离真实资源被钉在单测里。
    /// </summary>
    /// <param name="fellBackToChinese">
    /// 是否退回到了中文内嵌（英文界面下拿到中文内容）。为 true 时调用方**必须**写一条 WARN。
    /// </param>
    public static string? Resolve(
        Func<string, string?> reader,
        string baseName,
        string? language,
        out bool fellBackToChinese)
    {
        fellBackToChinese = false;
        if (reader is null) return null;

        var wanted = ResourceName(baseName, language);
        var text = reader(wanted);
        if (!string.IsNullOrWhiteSpace(text))
        {
            // V3.0 第三轮验证：取到了当前语言的资产 → 撤销可能存在的旧回退登记。
            // 没有这一步，提示条只增不减：后台补齐资产后界面明明已是英文，提示条仍在说「缺失」。
            FallbackNotice.Clear(baseName);
            return text;
        }

        if (!IsEnglish(language)) return null;

        // 英文资产缺失：退回中文内嵌，并显式标记，让调用方写日志。
        var zh = reader(ResourceName(baseName, Strings.ZhHans));
        if (!string.IsNullOrWhiteSpace(zh))
        {
            fellBackToChinese = true;
            // V3.0（E5）：登记到界面提示条 —— 只写日志的话，英文用户只会觉得「怎么一半是中文」。
            // 放在这里（唯一的回退出口）而不是各调用点，是为了不可能漏报。
            FallbackNotice.Report(baseName);
        }
        return zh;
    }

    /// <summary>
    /// 从当前程序集读内嵌资源文本；资源不存在返回 null。
    ///
    /// 刻意**不在这里写日志**：本文件会被单测工程直接链接编译，而 FileLogger 不在那个
    /// 编译单元里。缺资源 / 回退中文这类事由调用方（各 Service，它们本来就持有 FileLogger）
    /// 记到日志里，回退标记由 <see cref="Resolve"/> 的 out 参数给出。
    /// </summary>
    public static string? ReadEmbedded(string resourceName)
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream(resourceName);
            if (stream is null) return null;
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 「随包内嵌 + 按语言回退」的一步到位封装：<see cref="Resolve"/> 的读取器固定为
    /// <see cref="ReadEmbedded"/>。缺英文资产时 <paramref name="fellBackToChinese"/> 为 true，
    /// 调用方据此写一条 WARN。
    /// </summary>
    public static string? ReadEmbeddedWithFallback(
        string baseName, string? language, out bool fellBackToChinese)
        => Resolve(ReadEmbedded, baseName, language, out fellBackToChinese);
}
