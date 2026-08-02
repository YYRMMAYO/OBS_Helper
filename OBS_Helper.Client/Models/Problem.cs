namespace OBS_Helper.Client.Models;

/// <summary>
/// 单个解决方案步骤。
/// </summary>
public class Step
{
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    /// <summary>难度：基础 / 进阶</summary>
    public string Level { get; set; } = "基础";
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
    public string Severity { get; set; } = "常见";

    public string[] Symptoms { get; set; } = System.Array.Empty<string>();
    public string[] Causes { get; set; } = System.Array.Empty<string>();
    public List<Step> Steps { get; set; } = new();
    public string[] Tips { get; set; } = System.Array.Empty<string>();

    /// <summary>相关问题 id 列表</summary>
    public string[] Related { get; set; } = System.Array.Empty<string>();

    /// <summary>官方文档 / 参考链接</summary>
    public List<Link> Links { get; set; } = new();
}
