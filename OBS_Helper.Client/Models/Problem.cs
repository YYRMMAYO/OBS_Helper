namespace OBS_Helper.Client.Models;

public class Step
{
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Level { get; set; } = "基础"; // 基础 / 进阶
}

public class Problem
{
    public string Id { get; set; } = "";
    public string Category { get; set; } = "";
    public string Title { get; set; } = "";
    public string[] Platforms { get; set; } = Array.Empty<string>();
    public string Severity { get; set; } = "";
    public string[] Symptoms { get; set; } = Array.Empty<string>();
    public string[] Causes { get; set; } = Array.Empty<string>();
    public List<Step> Steps { get; set; } = new();
    public string[] Tips { get; set; } = Array.Empty<string>();
    public string[] Related { get; set; } = Array.Empty<string>();
}
