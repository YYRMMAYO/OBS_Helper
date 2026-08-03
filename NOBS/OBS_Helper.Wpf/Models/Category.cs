namespace OBS_Helper.Wpf.Models;

/// <summary>一个问题分类（首页九宫格 / 分类页标题栏）。</summary>
public class Category
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Icon { get; set; } = "";
    public string Color { get; set; } = "#1abc9c";
    public string Description { get; set; } = "";
}
