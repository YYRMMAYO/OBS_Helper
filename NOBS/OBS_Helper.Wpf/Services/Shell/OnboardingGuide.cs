namespace OBS_Helper.Wpf.Services.Shell;

/// <summary>新手引导的一步。</summary>
public sealed record OnboardingStep(string Title, string Description);

/// <summary>
/// 新手引导（V2.9.0）的纯逻辑部分：步骤清单 + 步骤游标的状态机 + 是否展示的判定。
/// 不依赖 WPF，可被单元测试工程直接链接编译。
///
/// 为什么把文案和游标逻辑单独抽出来：引导覆盖层是首启的第一印象，文案写错、步数越界
/// 这类问题在 UI 上很难自动发现；抽成纯逻辑后可以像其它 Core 一样被单测钉死。
///
/// 与设置的约定：只在 <c>prefs.json</c> 里记一个「已完成」标记（<see cref="PrefKey"/>），
/// 不存进度 —— 中途关掉窗口下次仍从第一步开始，符合「新手引导」的语义。
/// </summary>
public static class OnboardingGuide
{
    /// <summary>「已完成新手引导」的偏好键（LocalStore，值 "1" 表示已完成 / 已跳过）。</summary>
    public const string PrefKey = "onboarding.completed";

    /// <summary>
    /// 引导步骤。文案对应真实导航项与实际能力（导航名与页面卡片名必须与界面一致，
    /// 否则用户按着引导找不到入口 —— 这是这类引导最常见的失效方式）。
    /// </summary>
    public static IReadOnlyList<OnboardingStep> Steps { get; } = new OnboardingStep[]
    {
        new(
            "第一步 · 连上 OBS",
            "在左侧「控制台」填地址与密码即可连接（默认本机 4455 端口），连上后能远程切换场景、控制录制与推流，顶栏与左下角的连接徽章会实时显示状态。" +
            "连不上也不影响使用——查问题、看日志、做体检都是离线可用的。"),

        new(
            "第二步 · 出问题先来这里",
            "「首页」按分类翻常见问题，「搜索」敲关键词直接找，说不清现象就交给「助手」用一句话描述。" +
            "手上已经有 OBS 日志时，走「设置 → 排障指引 → 打开日志分析」，会自动脱敏并逐条定位异常。"),

        new(
            "第三步 · 一键体检",
            "连上 OBS 后到「诊断」跑自检清单与智能诊断（云端 / 免费 / 本地三通道，没网也能用）。" +
            "「工具箱」另有八个只读体检卡——色彩范围、采样率、黑屏专项、音频设备、虚拟摄像头、磁盘写入、编码顾问、推流节点探测，外加录像工具、冲突扫描、带宽计算器等实用小工具。"),

        new(
            "第四步 · 开播、装修与调教",
            "「搭建」是零基础到开播的分步向导，「模板」一键落地整套场景与来源，「插件」按分类直达官方下载。" +
            "「监控」实时看 CPU / 内存 / 磁盘；「设置」里能换主题色与字号、配全局热键、决定关闭窗口时是否缩到托盘继续守护。"),
    };

    /// <summary>步骤总数。</summary>
    public static int StepCount => Steps.Count;

    /// <summary>把任意下标夹到合法范围（负数取首步，越界取末步）。</summary>
    public static int Clamp(int index)
    {
        if (index < 0) return 0;
        return index >= StepCount ? StepCount - 1 : index;
    }

    /// <summary>取某一步（下标自动夹取）。</summary>
    public static OnboardingStep Step(int index) => Steps[Clamp(index)];

    /// <summary>是否为最后一步（此时主按钮文案应变成「开始使用」、隐藏「跳过」）。</summary>
    public static bool IsLast(int index) => Clamp(index) == StepCount - 1;

    /// <summary>是否为第一步（第一步隐藏「上一步」）。</summary>
    public static bool IsFirst(int index) => Clamp(index) == 0;

    /// <summary>下一步的下标；已在最后一步时保持不动。</summary>
    public static int Next(int index) => Clamp(Clamp(index) + 1);

    /// <summary>上一步的下标；已在第一步时保持不动。</summary>
    public static int Back(int index) => Clamp(Clamp(index) - 1);

    /// <summary>
    /// 是否需要在启动时展示引导：只有明确记过「已完成」才不展示。
    /// 值不是 "1"（包括 null / 空 / 脏数据）一律视为未完成 —— 宁可多给一次引导，
    /// 也不要因为偏好文件损坏而让新用户直接迷路。
    /// </summary>
    public static bool ShouldShow(string? storedValue) => !string.Equals(storedValue, "1", StringComparison.Ordinal);

    /// <summary>完成后写入的标记值。</summary>
    public const string CompletedValue = "1";
}
