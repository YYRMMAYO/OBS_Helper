namespace OBS_Helper.Wpf.Models;

/// <summary>一个问题分类（首页九宫格 / 分类页标题栏）。</summary>
public class Category
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Icon { get; set; } = "";
    /// <summary>
    /// 语义色键（red/orange/yellow/purple/blue/teal/green/azure/violet/crimson）。
    /// 主题资源里定义浅/深两套值，深色模式自动柔和化；不再是数据文件里的硬编码 hex（P2-1）。
    /// </summary>
    public string Semantic { get; set; } = "teal";
    public string Description { get; set; } = "";
}
