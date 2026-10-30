namespace OBS_Helper.Wpf.Services.Appearance;

/// <summary>界面密度档位。数值越大，控件越高、留白越多。</summary>
public enum UiDensity
{
    /// <summary>紧凑（默认）。</summary>
    Compact,
    /// <summary>舒适（无障碍模式的默认值）。</summary>
    Comfortable,
    /// <summary>宽大（老年模式的默认值）。</summary>
    Spacious
}

/// <summary>布局形态：完整 / 简化（老年模式把工具箱等页面收成一层）。</summary>
public enum UiLayoutMode
{
    Full,
    Simplified
}

/// <summary>
/// 模式组合的输入（V3.3.0）。
///
/// 全部是**原始事实**而不是结论：系统文字缩放、系统减少动画、应用内开关、用户单项覆盖。
/// 纯逻辑算一遍再写进资源字典，避免「模式打架」散落在 XAML 与各页面的 if 里。
/// </summary>
public sealed class UiProfileInput
{
    /// <summary>无障碍模式（低视力 / 读屏 / 键盘 / 前庭障碍 / 听障 / 认知障碍）。</summary>
    public bool AccessibilityMode { get; init; }

    /// <summary>老年模式（可选预设，不推断年龄）。</summary>
    public bool ElderMode { get; init; }

    /// <summary>Windows「辅助功能 → 文本大小」系数（1.0 = 100%）。</summary>
    public double SystemTextScale { get; init; } = 1.0;

    /// <summary>系统「减少动画」是否开启（SystemParameters.ClientAreaAnimation 的反面）。</summary>
    public bool SystemReduceMotion { get; init; }

    /// <summary>应用内字号档位："sm" / "md" / "lg" / "xl"。</summary>
    public string? FontScaleKey { get; init; } = "md";

    /// <summary>用户显式选择的密度；为空表示跟随模式预设。</summary>
    public string? DensityKey { get; init; } = "";

    /// <summary>用户显式设置的减少动画；null 表示跟随模式预设。</summary>
    public bool? ReduceMotionOverride { get; init; }

    /// <summary>用户显式选择的布局；为空表示跟随模式预设。</summary>
    public string? LayoutKey { get; init; } = "";
}

/// <summary>
/// 一份算好的界面令牌（V3.3.0）。
///
/// 这些值由 <see cref="UiProfileCore.Compute"/> 统一算出，再由 <c>AppearanceService.Apply</c>
/// 写进 <see cref="System.Windows.Application.Resources"/>；页面只通过 DynamicResource 引用，
/// 不在页面里再判断一次「是不是老年模式」。这样模式切换能即时生效，也不会出现两套互相矛盾的规则。
/// </summary>
public sealed record UiProfileTokens
{
    /// <summary>字号缩放（已含用户档位与系统文本缩放）。</summary>
    public double FontScale { get; init; } = 1.0;

    /// <summary>生效的密度。</summary>
    public UiDensity Density { get; init; } = UiDensity.Compact;

    /// <summary>生效的布局形态。</summary>
    public UiLayoutMode Layout { get; init; } = UiLayoutMode.Full;

    /// <summary>是否减少动画（系统 / 用户 / 模式取并集，显式用户值优先）。</summary>
    public bool ReduceMotion { get; init; }

    /// <summary>侧栏宽度（DIP）。</summary>
    public double NavWidth { get; init; } = 212;

    /// <summary>导航项最小高度（DIP）。</summary>
    public double NavItemMinHeight { get; init; } = 34;

    /// <summary>导航项内边距（DIP，左右 / 上下）。</summary>
    public double NavItemPaddingX { get; init; } = 9;
    public double NavItemPaddingY { get; init; } = 7;

    /// <summary>普通控件最小高度（DIP）。</summary>
    public double ControlMinHeight { get; init; } = 30;

    /// <summary>主要按钮最小尺寸（DIP；无障碍 / 老年模式要求 ≥44）。</summary>
    public double ButtonMinSize { get; init; }

    /// <summary>卡片内边距（DIP）。</summary>
    public double CardPadding { get; init; } = 16;

    /// <summary>键盘焦点框粗细 / 外扩（DIP）。</summary>
    public double FocusThickness { get; init; } = 2;
    public double FocusOffset { get; init; } = 1;

    /// <summary>是否显示「跳到主内容」链接。</summary>
    public bool ShowSkipLink { get; init; }

    /// <summary>行高系数（无障碍 + 老年模式都放大，便于阅读）。</summary>
    public double LineHeightFactor { get; init; } = 1.35;

    /// <summary>无障碍 / 老年模式是否任一开启（供文案与诊断显示）。</summary>
    public bool AnyAssistMode { get; init; }
}

/// <summary>
/// 无障碍模式与老年模式的纯逻辑（V3.3.0 / P0-1）。
///
/// 设计约定（与 PLAN_V3.3.0 §7.2 一致）：
/// <list type="bullet">
///   <item><b>系统设置是下限</b>：系统文本缩放、系统减少动画不能被应用关掉。</item>
///   <item><b>两个模式可叠加</b>：字号 / 密度取较大值，无障碍特性取并集。</item>
///   <item><b>预设只改默认值</b>：用户显式改过的单项（密度 / 减少动画 / 布局）优先于预设。</item>
///   <item><b>高对比不在这里强制</b>：由用户开关与系统主题决定（见 AppearanceService）。</item>
/// </list>
///
/// 纯逻辑、零 WPF 依赖 —— 被单测工程直接链接编译。
/// </summary>
public static class UiProfileCore
{
    /// <summary>字号档位 → 系数。与旧版 AppearanceService 的换算保持一致。</summary>
    public static double FontFactorOf(string? key) => key switch
    {
        "sm" => 0.92,
        "lg" => 1.12,
        "xl" => 1.28,
        _ => 1.0
    };

    /// <summary>密度 key → 枚举；未知值回到紧凑。</summary>
    public static UiDensity ParseDensity(string? key) => key switch
    {
        "comfortable" => UiDensity.Comfortable,
        "spacious" => UiDensity.Spacious,
        _ => UiDensity.Compact
    };

    /// <summary>密度枚举 → 持久化 key。</summary>
    public static string DensityKey(UiDensity density) => density switch
    {
        UiDensity.Comfortable => "comfortable",
        UiDensity.Spacious => "spacious",
        _ => "compact"
    };

    /// <summary>布局 key → 枚举。</summary>
    public static UiLayoutMode ParseLayout(string? key)
        => string.Equals(key, "simplified", StringComparison.OrdinalIgnoreCase)
            ? UiLayoutMode.Simplified
            : UiLayoutMode.Full;

    /// <summary>布局枚举 → 持久化 key。</summary>
    public static string LayoutKey(UiLayoutMode mode)
        => mode == UiLayoutMode.Simplified ? "simplified" : "full";

    /// <summary>算出当前生效的令牌。任何输入都不会抛异常（旧配置 / 脏值都按默认处理）。</summary>
    public static UiProfileTokens Compute(UiProfileInput? input)
    {
        input ??= new UiProfileInput();

        var a11y = input.AccessibilityMode;
        var elder = input.ElderMode;
        var anyAssist = a11y || elder;

        // ---- 字号：用户档位 → 模式下限 → 系统缩放（系统是下限，不是上限）
        var font = FontFactorOf(input.FontScaleKey);
        if (a11y) font = Math.Max(font, 1.15);   // 无障碍：至少 +15%
        if (elder) font = Math.Max(font, 1.30);  // 老年：1.3× 起
        var system = double.IsFinite(input.SystemTextScale)
            ? Math.Clamp(input.SystemTextScale, 1.0, 2.0)
            : 1.0;
        font = Math.Clamp(font * system, 0.92, 2.56);

        // ---- 密度：取较大值；用户显式选择优先于模式预设
        var presetDensity = elder ? UiDensity.Spacious
            : a11y ? UiDensity.Comfortable
            : UiDensity.Compact;
        var density = (UiDensity)Math.Max((int)presetDensity, (int)ParseDensity(input.DensityKey));
        if (a11y) density = (UiDensity)Math.Max((int)density, (int)UiDensity.Comfortable);
        if (elder) density = (UiDensity)Math.Max((int)density, (int)UiDensity.Spacious);

        // ---- 布局：用户显式选择优先，否则老年模式简化
        var layout = !string.IsNullOrWhiteSpace(input.LayoutKey)
            ? ParseLayout(input.LayoutKey)
            : elder ? UiLayoutMode.Simplified : UiLayoutMode.Full;

        // ---- 减少动画：系统是下限；用户显式设置优先于模式预设
        var reduceMotion = input.SystemReduceMotion
            || input.ReduceMotionOverride == true
            || (!input.ReduceMotionOverride.HasValue && (a11y || elder));

        // ---- 尺寸令牌（按密度 + 字号放大；老人 / 无障碍另有硬下限）
        var (navH, ctrlH, cardPad, navPadX, navPadY) = density switch
        {
            UiDensity.Spacious => (56.0, 44.0, 22.0, 14.0, 12.0),
            UiDensity.Comfortable => (42.0, 38.0, 18.0, 11.0, 9.0),
            _ => (34.0, 30.0, 16.0, 9.0, 7.0)
        };

        // 字号变大后，固定高度必须跟着涨，否则文字会被裁掉
        navH = Math.Max(navH, Math.Round(26 * font, 1));
        ctrlH = Math.Max(ctrlH, Math.Round(22 * font, 1));
        cardPad = Math.Max(cardPad, Math.Round(12 * font, 1));

        // 硬下限：无障碍 44×44、老年 48×48（计划 §7.2）
        var buttonMin = elder ? 48.0 : a11y ? 44.0 : 0.0;
        ctrlH = Math.Max(ctrlH, buttonMin);

        var navWidth = Math.Clamp(Math.Round(212 * Math.Max(1.0, font * 0.92), 0), 212, 320);
        if (elder) navWidth = Math.Max(navWidth, 240);

        return new UiProfileTokens
        {
            FontScale = Math.Round(font, 3),
            Density = density,
            Layout = layout,
            ReduceMotion = reduceMotion,
            NavWidth = navWidth,
            NavItemMinHeight = Math.Round(navH, 1),
            NavItemPaddingX = navPadX,
            NavItemPaddingY = navPadY,
            ControlMinHeight = Math.Round(ctrlH, 1),
            ButtonMinSize = buttonMin,
            CardPadding = Math.Round(cardPad, 1),
            FocusThickness = anyAssist ? 3 : 2,
            FocusOffset = anyAssist ? 2 : 1,
            ShowSkipLink = anyAssist,
            LineHeightFactor = elder ? 1.5 : a11y ? 1.45 : 1.35,
            AnyAssistMode = anyAssist
        };
    }

    /// <summary>
    /// 模式的展示 key（设置页 / 状态行用）：<c>off</c> / <c>a11y</c> / <c>elder</c> / <c>both</c>。
    /// 返回 key 而不是文案，方便调用方按当前语言取字符串。
    /// </summary>
    public static string ModeKey(bool accessibility, bool elder)
        => (accessibility, elder) switch
        {
            (true, true) => "both",
            (true, false) => "a11y",
            (false, true) => "elder",
            _ => "off"
        };
}
