namespace OBS_Helper.Wpf.Services.Tools;

/// <summary>
/// 工具箱页的分节顺序表（V3.0 / C6）。
///
/// 为什么单独放一个纯文件：页面里那张「元素 → 文案键」的表如果只写在 XAML 代码后置里，
/// 就**无法被单测覆盖**，于是「加了新分节却忘了加导航项」这种漂移迟早会发生。
/// 这里只放文案键（顺序即页面顺序），对应的元素名由 <see cref="ElementNameOf"/> 约定推出，
/// 单测再拿它对 <c>ToolboxPage.xaml</c> 里真实存在的 <c>x:Name</c> 做一致性校验。
/// </summary>
public static class ToolboxSections
{
    /// <summary>分节文案键，顺序与页面自上而下一致。</summary>
    public static readonly string[] Keys =
    {
        "toolbox.recordingTools",
        "toolbox.prescription",
        "toolbox.privacy",
        "toolbox.conflicts",
        "toolbox.bandwidth",
        "toolbox.colorCheck",
        "toolbox.sampleRate",
        "toolbox.graphics",
        "toolbox.audioHealth",
        "toolbox.vcam",
        "toolbox.disk",
        "toolbox.encoder",
        "toolbox.ingest",
        "toolbox.browserSource",
        "toolbox.release",
        "setup.officialObs",
        "toolbox.shortcuts"
    };

    /// <summary>文案键 → 页面里分节标题的 <c>x:Name</c>（约定：<c>Sec_</c> 前缀 + 键中的点换成下划线）。</summary>
    public static string ElementNameOf(string key) => "Sec_" + key.Replace('.', '_');
}
