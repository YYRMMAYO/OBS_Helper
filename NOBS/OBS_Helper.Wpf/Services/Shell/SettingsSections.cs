namespace OBS_Helper.Wpf.Services.Shell;

/// <summary>设置页的分组（左栏的小标题）。顺序即左栏自上而下的顺序。</summary>
public sealed record SettingsGroup(string Id, string TitleKey, string DescKey);

/// <summary>
/// 一个设置页：对应右侧的一张卡片。
/// <para>
/// <see cref="CardName"/> 是 <c>SettingsPage.xaml</c> 里卡片的 <c>x:Name</c>；
/// 单测会拿它核对页面里真的有这张卡片，避免「导航里有、页面上没有」的静默漂移。
/// </para>
/// </summary>
public sealed record SettingsSection(
    string Id,
    string GroupId,
    string CardName,
    string TitleKey,
    string DescKey,
    string IconKey,
    string[] Keywords);

/// <summary>
/// 设置页目录（V3.4.2）。
///
/// 为什么单独放一个纯 BCL 文件：左栏导航、页面卡片、搜索关键词、单测必须共用同一份真源。
/// 以前所有设置卡片都堆在同一个纵向 StackPanel 里，新增设置只能继续往下追加，
/// 于是「设置页越来越长、找不到东西」是结构性问题，而不是文案问题。
/// 现在一个设置页 = 一张卡片；分组只是目录上的层级，不改变卡片里的业务逻辑。
/// </summary>
public static class SettingsSections
{
    public static readonly SettingsGroup[] Groups =
    {
        new("basics", "settings.group.basics", "settings.group.basics.desc"),
        new("recording", "settings.group.recording", "settings.group.recording.desc"),
        new("ai", "settings.group.ai", "settings.group.ai.desc"),
        new("data", "settings.group.data", "settings.group.data.desc"),
        new("about", "settings.group.about", "settings.group.about.desc"),
    };

    public static readonly SettingsSection[] All =
    {
        // ------------------------------------------------------------ 基础设置
        new("language", "basics", "CardLanguage",
            "settings.language.title", "language.hint", "IconGeomLanguage",
            new[] { "语言", "language", "locale", "中文", "english", "界面语言" }),
        new("appearance", "basics", "CardAppearance",
            "settings.appearance.title", "settings.appearance.desc", "IconGeomPalette",
            new[] { "外观", "主题", "theme", "颜色", "accent", "字体", "字号", "font", "背景", "壁纸", "wallpaper", "无障碍", "高对比", "动画", "老年模式" }),
        new("shell", "basics", "CardShell",
            "settings.shell.title", "settings.shell.desc", "IconGeomTray",
            new[] { "后台", "托盘", "tray", "通知", "notification", "守护", "watchdog", "日志预警", "静音提醒" }),

        // ------------------------------------------------------------ 录制与自动化
        new("record", "recording", "CardRecord",
            "settings.record.title", "settings.record.desc", "IconGeomVideo",
            new[] { "录制", "录像", "record", "预设", "preset", "命名", "naming", "倒计时", "countdown" }),
        new("autoswitch", "recording", "CardAutoSwitch",
            "settings.autoSwitch.title", "settings.autoSwitch.desc", "IconGeomArrowsSwitch",
            new[] { "场景", "scene", "自动切换", "窗口标题", "规则", "rule", "正则", "regex" }),
        new("hotkey", "recording", "CardHotkey",
            "settings.hotkey.title", "settings.hotkey.desc", "IconGeomKeyboard",
            new[] { "热键", "快捷键", "hotkey", "shortcut", "组合键", "全局" }),

        // ------------------------------------------------------------ AI 与诊断
        new("ai", "ai", "CardAi",
            "settings.ai.title", "settings.ai.desc", "IconGeomCpu",
            new[] { "ai", "模型", "model", "api", "密钥", "key", "诊断引擎", "免费", "云端", "本地" }),
        new("guide", "ai", "CardGuide",
            "settings.guide.title", "settings.guide.desc", "IconGeomDiagnostic",
            new[] { "排障", "diagnose", "日志", "log", "黑屏", "掉帧", "推流失败", "指引" }),

        // ------------------------------------------------------------ 数据与存储
        new("data", "data", "CardData",
            "settings.data.title", "settings.data.desc", "IconGeomDatabase",
            new[] { "数据", "data", "收藏", "bookmark", "步骤", "steps", "清理", "重置", "本机" }),
        new("obsconfig", "data", "CardObsConfig",
            "settings.obsconfig.title", "settings.obsconfig.desc", "IconGeomArchive",
            new[] { "obs配置", "obs config", "备份", "backup", "导入", "导出", "重置", "场景模板" }),

        // ------------------------------------------------------------ 关于与帮助
        new("onboarding", "about", "CardOnboarding",
            "settings.onboarding.title", "settings.onboarding.desc", "IconGeomCompass",
            new[] { "新手", "引导", "onboarding", "教程", "重看" }),
        new("about", "about", "CardAbout",
            "settings.about.title", "settings.about.desc", "IconGeomInfo",
            new[] { "关于", "about", "版本", "version", "更新", "update", "开源", "许可", "license", "隐私" }),
    };

    /// <summary>按 id 找设置页；找不到返回 <c>null</c>（页面会回退到第一页）。</summary>
    public static SettingsSection? Find(string? id)
        => string.IsNullOrWhiteSpace(id)
            ? null
            : All.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal));

    /// <summary>按分组取设置页，保持 <see cref="All"/> 的顺序。</summary>
    public static IEnumerable<SettingsSection> InGroup(string groupId)
        => All.Where(s => string.Equals(s.GroupId, groupId, StringComparison.Ordinal));

    public static SettingsGroup? FindGroup(string? id)
        => string.IsNullOrWhiteSpace(id)
            ? null
            : Groups.FirstOrDefault(g => string.Equals(g.Id, id, StringComparison.Ordinal));

    /// <summary>默认设置页：语言（最常改、也最适合作为“设置从哪里开始”的落点）。</summary>
    public const string DefaultId = "language";
}
