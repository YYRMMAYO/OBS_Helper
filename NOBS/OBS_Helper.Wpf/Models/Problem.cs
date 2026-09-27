using OBS_Helper.Wpf.Localization;
namespace OBS_Helper.Wpf.Models;

/// <summary>
/// 单个解决方案步骤。
/// </summary>
public class Step
{
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    /// <summary>难度：基础 / 进阶</summary>
        /// <summary>步骤难度展示文案；数据缺省时按当前语言给出「基础」。</summary>
    public string Level
    {
        get => string.IsNullOrWhiteSpace(_level) ? Strings.T("model.level.basic") : _level;
        set => _level = value ?? "";
    }
    private string _level = "";
}

/// <summary>
/// 外部参考链接（官方文档 / 教程等），在问题详情页以「官方文档 / 参考链接」区块呈现。
/// </summary>
public class Link
{
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
}

/// <summary>
/// 一条 OBS 排障问题及其解决方案。
/// </summary>
public class Problem
{
    public string Id { get; set; } = "";
    public string Category { get; set; } = "";
    public string Title { get; set; } = "";

    /// <summary>适用平台，如 Windows / macOS</summary>
    public string[] Platforms { get; set; } = System.Array.Empty<string>();

    /// <summary>严重度：常见 / 一般 / 严重</summary>
    /// <summary>严重度展示文案；数据缺省时按当前语言给出「常见」。</summary>
    public string Severity
    {
        get => string.IsNullOrWhiteSpace(_severity) ? Strings.T("model.severity.common") : _severity;
        set => _severity = value ?? "";
    }
    private string _severity = "";

    public string[] Symptoms { get; set; } = System.Array.Empty<string>();
    public string[] Causes { get; set; } = System.Array.Empty<string>();
    public List<Step> Steps { get; set; } = new();
    public string[] Tips { get; set; } = System.Array.Empty<string>();

    /// <summary>相关问题 id 列表</summary>
    public string[] Related { get; set; } = System.Array.Empty<string>();

    /// <summary>官方文档 / 参考链接</summary>
    public List<Link> Links { get; set; } = new();
}
