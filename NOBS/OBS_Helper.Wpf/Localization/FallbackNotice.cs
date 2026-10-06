namespace OBS_Helper.Wpf.Localization;

/// <summary>
/// 内容资产「回退成中文」的界面提示登记处（V3.0 / E5）。
///
/// 背景：四份内容资产（知识库 / 插件目录 / 场景模板 / 排障指引）都有英文并列文件。
/// 一旦英文资产缺失，<see cref="ContentAssets.Resolve"/> 会**静默**退回中文随包内容 ——
/// 以前只在日志里留一条 WARN，**界面上什么都不说**，英文用户只会觉得「这软件怎么一半是中文」。
/// 这与 V2.9.1 raw 404 那类「静默且合法」的坑是同一个形状：功能没坏，体验错了，且没人知道。
///
/// 设计取舍：
/// <list type="bullet">
///   <item>登记点放在 <see cref="ContentAssets"/> 内部（唯一的回退出口），
///     这样**调用方不可能忘记上报** —— 少写一行日志是小事，少一条界面提示是这一项的全部意义；</item>
///   <item>纯静态、零 WPF 依赖：单测工程直接链接编译；界面由 MainWindow 读它决定显不显示提示条；</item>
///   <item>「知道了」只在**本次运行**内生效：下次启动如果仍然缺失，用户应该再看到一次 ——
///     这不是打扰，而是提醒他「英文内容确实没随包发出去」。</item>
/// </list>
/// </summary>
public static class FallbackNotice
{
    private static readonly object Gate = new();
    private static readonly List<string> Assets = new();

    /// <summary>回退集合或「已忽略」状态变化时触发（界面据此刷新提示条）。</summary>
    public static event Action? Changed;

    /// <summary>已经发生回退的资产基名（去重、保持首次出现的顺序）。</summary>
    public static IReadOnlyList<string> FallbackAssets
    {
        get { lock (Gate) return Assets.ToList(); }
    }

    /// <summary>本次运行内是否发生过回退。</summary>
    public static bool HasFallback
    {
        get { lock (Gate) return Assets.Count > 0; }
    }

    /// <summary>用户本次运行内是否点过「知道了」。</summary>
    public static bool Dismissed { get; private set; }

    /// <summary>登记一次回退（由 <see cref="ContentAssets"/> 在发现回退时调用）。</summary>
    public static void Report(string baseName)
    {
        if (string.IsNullOrWhiteSpace(baseName)) return;

        var changed = false;
        lock (Gate)
        {
            if (!Assets.Contains(baseName, StringComparer.Ordinal))
            {
                Assets.Add(baseName);
                changed = true;
            }
        }

        if (changed) Raise();
    }

    /// <summary>
    /// 撤销一次回退登记（V3.0 第三轮验证修复）：资产后来补齐 / 热更新下载到本地缓存后，
    /// 由 <see cref="ContentAssets"/> 在**成功取到当前语言资产**时调用。
    ///
    /// 没有这个对称操作的话提示条只增不减：后台把英文资产下载下来、界面已经是英文内容了，
    /// 提示条还在肯定地说「这块内容缺失」，直到用户点「知道了」或重启 —— 那就是一条假消息。
    /// </summary>
    public static void Clear(string baseName)
    {
        if (string.IsNullOrWhiteSpace(baseName)) return;

        var changed = false;
        lock (Gate) changed = Assets.Remove(baseName);

        if (changed) Raise();
    }

    /// <summary>用户点「知道了」：本次运行内不再显示。</summary>
    public static void Dismiss()
    {
        if (Dismissed) return;
        Dismissed = true;
        Raise();
    }

    /// <summary>清空状态（切换语言后重新评估；单测也用它隔离用例）。</summary>
    public static void Reset()
    {
        lock (Gate) Assets.Clear();
        Dismissed = false;
        Raise();
    }

    private static void Raise()
    {
        try { Changed?.Invoke(); }
        catch (Exception) { /* 提示条刷新失败绝不能影响主流程 */ }
    }
}

/// <summary>
/// 回退提示条的判定与文案（V3.0 / E5）。纯逻辑，便于单测把「什么时候该提示」钉死。
/// </summary>
public static class FallbackNoticeCore
{
    /// <summary>
    /// 现在该不该显示提示条。
    ///
    /// 三个条件缺一不可：**发生过回退**、**当前语言不是中文**（中文界面下回退到中文本来就是对的，
    /// 提示只会让人困惑）、**用户没点过「知道了」**。
    /// </summary>
    public static bool ShouldShow(string? language, bool hasFallback, bool dismissed)
        => hasFallback
           && !dismissed
           // 用 Normalize 而不是直接比字符串：空串 / "zh" / "zh-CN" 都归属默认的简体中文，
           // 直接比会让「语言还没定下来」的时刻误报一条提示。
           && !string.Equals(Strings.Normalize(language), Strings.ZhHans, StringComparison.Ordinal);

    /// <summary>提示条正文：列出受影响的内容名称，让用户知道「具体是哪几块」。</summary>
    public static string BuildMessage(string? language, IReadOnlyList<string> fallbackAssets, bool hasFallback, bool dismissed)
    {
        if (!ShouldShow(language, hasFallback, dismissed)) return "";

        var names = fallbackAssets
            .Select(AssetLabel)
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return names.Count == 0
            ? Strings.T("i18n.fallback.message")
            : Strings.T("i18n.fallback.messageList", string.Join(Strings.T("i18n.fallback.separator"), names));
    }

    /// <summary>资产基名 → 用户看得懂的界面文案。</summary>
    public static string AssetLabel(string? baseName) => baseName switch
    {
        ContentAssets.Problems => Strings.T("i18n.fallback.asset.problems"),
        ContentAssets.Plugins => Strings.T("i18n.fallback.asset.plugins"),
        ContentAssets.SceneTemplates => Strings.T("i18n.fallback.asset.templates"),
        ContentAssets.Troubleshooting => Strings.T("i18n.fallback.asset.guide"),
        _ => baseName ?? ""
    };
}
