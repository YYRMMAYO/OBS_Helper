namespace OBS_Helper.Wpf.Models;

/// <summary>离线知识库根对象（problems.json 的反序列化目标）。</summary>
public class ProblemData
{
    public string Version { get; set; } = "";
    public string Updated { get; set; } = "";
    public List<Category> Categories { get; set; } = new();
    public List<Problem> Problems { get; set; } = new();
}
