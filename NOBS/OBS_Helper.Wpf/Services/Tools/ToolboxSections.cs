namespace OBS_Helper.Wpf.Services.Tools;

/// <summary>
/// 工具箱的一个分组（页面上的一块「章」）。
/// <para>
/// <see cref="SectionKeys"/> 的顺序就是组内小节自上而下的顺序；
/// 页面上对应的容器名为 <c>Group_section_&lt;Id&gt;</c>（见 <see cref="GroupElementNameOf"/>），
/// 单测会核对它真实存在。
/// </para>
/// </summary>
public sealed record ToolboxGroup(string Id, string TitleKey, string DescKey, string[] SectionKeys);

/// <summary>
/// 工具箱页的分组与分节顺序表（V3.0 / C6，V3.4.2 重构为分组）。
///
/// 为什么单独放一个纯文件：页面里那张「元素 → 文案键」的表如果只写在 XAML 代码后置里，
/// 就**无法被单测覆盖**，于是「加了新分节却忘了加导航项」这种漂移迟早会发生。
/// 这里只放文案键（顺序即页面顺序），对应的元素名由 <see cref="ElementNameOf"/> 约定推出，
/// 单测再拿它对 <c>ToolboxPage.xaml</c> 里真实存在的 <c>x:Name</c> 做一致性校验。
///
/// V3.4.2：17 个小节不再按开发时间平铺，而是按「录制 / 画面 / 音频 / 网络 / 参数 / 版本 / 反馈」
/// 七组重排；分组同时是页面筛选与目录层级的唯一真源，避免 XAML、代码、测试各写一份。
/// </summary>
public static class ToolboxSections
{
    /// <summary>分组顺序 = 页面自上而下的顺序。</summary>
    public static readonly ToolboxGroup[] Groups =
    {
        new("capture", "toolbox.group.capture", "toolbox.group.capture.desc",
            new[] { "toolbox.recordingTools", "toolbox.disk" }),
        new("video", "toolbox.group.video", "toolbox.group.video.desc",
            new[] { "toolbox.colorCheck", "toolbox.graphics", "toolbox.vcam", "toolbox.encoder" }),
        new("audio", "toolbox.group.audio", "toolbox.group.audio.desc",
            new[] { "toolbox.sampleRate", "toolbox.audioHealth" }),
        new("network", "toolbox.group.network", "toolbox.group.network.desc",
            new[] { "toolbox.bandwidth", "toolbox.ingest", "toolbox.browserSource" }),
        new("params", "toolbox.group.params", "toolbox.group.params.desc",
            new[] { "toolbox.prescription", "toolbox.privacy", "toolbox.shortcuts" }),
        new("updates", "toolbox.group.updates", "toolbox.group.updates.desc",
            new[] { "toolbox.conflicts", "toolbox.release", "setup.officialObs" }),
        new("support", "toolbox.group.support", "toolbox.group.support.desc",
            new[] { "toolbox.feedback" }),
    };

    /// <summary>分节文案键，顺序与页面自上而下一致（由分组展开而来，避免两处各写一份）。</summary>
    public static readonly string[] Keys = Groups.SelectMany(group => group.SectionKeys).ToArray();

    /// <summary>所有分组 id。</summary>
    public static IEnumerable<string> GroupIds => Groups.Select(group => group.Id);

    /// <summary>文案键 → 所属分组 id；找不到返回 <c>null</c>。</summary>
    public static string? GroupOf(string sectionKey)
        => Groups.FirstOrDefault(group =>
                group.SectionKeys.Contains(sectionKey, StringComparer.Ordinal))
            ?.Id;

    /// <summary>文案键 → 页面里分节标题的 <c>x:Name</c>（约定：<c>Sec_</c> 前缀 + 键中的点换成下划线）。</summary>
    public static string ElementNameOf(string key) => "Sec_" + key.Replace('.', '_');

    /// <summary>分组 id → 页面里分组容器的 <c>x:Name</c>。</summary>
    public static string GroupElementNameOf(string groupId) => "Group_section_" + groupId;
}
