namespace OBS_Helper.Wpf.Navigation;

/// <summary>
/// 应用内路由名。集中定义，避免各页面拼字符串拼错。
///
/// 单独成文件（V2.9.1）：这里只有字符串常量、不依赖 WPF，因此可以被单测工程直接链接编译，
/// 用来校验「新手引导里写的路由名真的存在」（见 OnboardingGuideTests）。
/// </summary>
public static class Routes
{
    public const string Home = "home";
    public const string Search = "search";
    public const string Assistant = "assistant";
    public const string Diagnostic = "diagnostic";
    public const string Setup = "setup";
    public const string Console = "console";
    public const string Guide = "guide";
    public const string Settings = "settings";

    /// <summary>分类页，参数为分类 id（string）。</summary>
    public const string Category = "category";
    /// <summary>问题详情页，参数为问题 id（string）。</summary>
    public const string Problem = "problem";
    /// <summary>日志分析页（从诊断页 / 设置页进入，无独立导航项）。</summary>
    public const string Logs = "logs";

    /// <summary>直播间场景模板页（一级导航）。</summary>
    public const string Templates = "templates";
    /// <summary>OBS 配置管理页：备份 / 导入导出 / 重置（从设置页进入，无独立导航项）。</summary>
    public const string ObsConfig = "obsconfig";

    /// <summary>系统资源监控页（一级导航）。</summary>
    public const string Performance = "performance";

    /// <summary>OBS 插件广场：常用插件的分类导航与官方下载跳转（一级导航）。</summary>
    public const string Plugins = "plugins";

    /// <summary>工具箱：录像工具 / 参数处方 / 隐私清单 / 冲突扫描 / 带宽计算 / 版本情报（一级导航，V2.6）。</summary>
    public const string Toolbox = "toolbox";
}
