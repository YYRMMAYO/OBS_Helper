namespace OBS_Helper.Wpf.Services.Appearance;

/// <summary>
/// V3.7.2 双皮肤的一套语义色板（A · 导播台 / C · 排版手册）。
///
/// 为什么单独成文件：颜色角色与「主题明暗」不是一一对应 —— A/C 是两种排版语言，
/// 同一个角色在两套皮肤下取值完全不同（例如 C 的卡片是透明 + 发丝线，A 是填充卡片）。
/// 把这些值集中在一处，<c>AppearanceService.Apply()</c> 只负责写资源，不再散落一堆三元表达式。
///
/// 取值来源：<c>NOBS/docs/V3.7.2_AC_THEME_TECH.md</c> §5.3 的对比度修正值。
/// 所有正文 / 次要文字组合按 WCAG 2.1 AA（4.5:1）取色；装饰线（Line）不承载文字与交互边界。
/// </summary>
public sealed record SkinPalette(
    string Surface,
    string Surface2,
    string Surface3,
    string Nav,
    string TopBar,
    string Text,
    string Muted,
    string Dim,
    string Line,
    string LineStrong,
    string Danger,
    string DangerSoft,
    string DangerHover,
    string DangerPressed,
    string Warn,
    string WarnSoft,
    string Ok,
    string OkSoft,
    string Info,
    string InfoSoft,
    string SemanticRed,
    string SemanticOrange,
    string SemanticYellow,
    string SemanticPurple,
    string SemanticBlue,
    string SemanticTeal,
    string SemanticGreen,
    string SemanticAzure,
    string SemanticViolet,
    string SemanticCrimson)
{
    /// <summary>A · 导播台（黑夜）：机柜黑底、信号青、填充面板。</summary>
    public static readonly SkinPalette Signal = new(
        Surface: "#15181D",
        Surface2: "#0B0D10",
        Surface3: "#1E232B",
        Nav: "#0B0D10",
        TopBar: "#0B0D10",
        Text: "#EAF0FF",
        Muted: "#8B95A5",
        // §5.3：设计板的 #5C6675 只有 3.35:1，功能性小字统一提到 #7F8B9A（5.14:1）。
        Dim: "#7F8B9A",
        Line: "#262C35",
        // 交互边界（按钮 / 输入框）用 #6B768C：面板上 3.89:1，达非文本对比 3:1。
        LineStrong: "#6B768C",
        Danger: "#FF4D4D",
        DangerSoft: "#2A1214",
        DangerHover: "#FF6B6B",
        DangerPressed: "#FF8585",
        Warn: "#FFB547",
        WarnSoft: "#33280F",
        Ok: "#3BE08F",
        OkSoft: "#10291A",
        Info: "#7FB2FF",
        InfoSoft: "#152740",
        SemanticRed: "#E56A5C",
        SemanticOrange: "#F0A35E",
        SemanticYellow: "#E8CF6F",
        SemanticPurple: "#C39BD3",
        SemanticBlue: "#7FB8E8",
        SemanticTeal: "#63C9B5",
        SemanticGreen: "#4FBFA4",
        SemanticAzure: "#6FA8DC",
        SemanticViolet: "#B08CD6",
        SemanticCrimson: "#D96A63");

    /// <summary>C · 排版手册（白天）：纸白底、信号蓝、透明分节 + 发丝线。</summary>
    public static readonly SkinPalette Manual = new(
        Surface: "#FFFFFF",
        Surface2: "#FCFCFA",
        Surface3: "#F0F0EB",
        Nav: "#FCFCFA",
        TopBar: "#FCFCFA",
        Text: "#111111",
        Muted: "#5E5E58",
        // §5.3：目录序号 #A6A69F 只有 2.38:1，改用 #6B6B66（5.22:1）。
        Dim: "#6B6B66",
        Line: "#DCDCD6",
        LineStrong: "#111111",
        Danger: "#C2410C",
        DangerSoft: "#FCEBE1",
        DangerHover: "#A8370A",
        DangerPressed: "#8F2F08",
        Warn: "#A63A00",
        WarnSoft: "#FDF1E5",
        Ok: "#3F6B4B",
        OkSoft: "#EAF2EC",
        Info: "#1F3BFF",
        InfoSoft: "#E9EDFF",
        SemanticRed: "#B23A2E",
        SemanticOrange: "#B35A12",
        SemanticYellow: "#8A6D00",
        SemanticPurple: "#6C3FA0",
        SemanticBlue: "#1F6FA8",
        SemanticTeal: "#0F766E",
        SemanticGreen: "#166534",
        SemanticAzure: "#1D5F91",
        SemanticViolet: "#6D28D9",
        SemanticCrimson: "#A61B1B");

    /// <summary>取皮肤对应的色板。</summary>
    public static SkinPalette For(UiSkin skin) =>
        skin == UiSkin.SignalDark ? Signal : Manual;

    /// <summary>
    /// 高对比覆盖（V3.7.2 / §5.7）：只改文字、背景、边框，不改皮肤的结构令牌。
    /// 高对比优先级高于皮肤：A 的青、C 的蓝都不允许绕过。
    /// </summary>
    public SkinPalette WithHighContrast(bool dark) => this with
    {
        Surface = dark ? "#000000" : "#FFFFFF",
        Surface2 = dark ? "#000000" : "#FFFFFF",
        Surface3 = dark ? "#1A1A1A" : "#F2F2F2",
        Nav = dark ? "#000000" : "#FFFFFF",
        TopBar = dark ? "#000000" : "#FFFFFF",
        Text = dark ? "#FFFFFF" : "#000000",
        Muted = dark ? "#E8E8E8" : "#1A1A1A",
        Dim = dark ? "#E8E8E8" : "#1A1A1A",
        Line = dark ? "#FFFFFF" : "#000000",
        LineStrong = dark ? "#FFFFFF" : "#000000",
        Danger = dark ? "#FF8A8A" : "#9A1B12",
        DangerSoft = dark ? "#2A0A0A" : "#FBE9E7",
        Warn = dark ? "#FFD166" : "#7A2E00",
        WarnSoft = dark ? "#2A2105" : "#FDF1E5",
        Ok = dark ? "#7CFFB2" : "#14532D",
        OkSoft = dark ? "#06210F" : "#E7F6EE",
        Info = dark ? "#9CC7FF" : "#0B3EA8",
        InfoSoft = dark ? "#06152B" : "#E8F0FE",
    };
}
