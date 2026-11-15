namespace OBS_Helper.Wpf.Services;

/// <summary>
/// 界面皮肤（V3.7.2）。由 <see cref="AppTheme"/> 的唯一解析结果决定，不单独暴露给用户：
/// 深色 → <see cref="SignalDark"/>（A · 导播台），浅色 → <see cref="ManualLight"/>（C · 排版手册）。
/// 两套皮肤共用同一棵视觉树与同一份页面逻辑，只切换令牌与模板；切换不重建页面、不丢焦点。
/// </summary>
public enum UiSkin
{
    /// <summary>A · 导播台：机柜黑底、信号青强调色、圆角卡片、图标导航、tally / 时间码。</summary>
    SignalDark,

    /// <summary>C · 排版手册：纸白底、信号蓝强调色、发丝线、编号目录、细体大标题。</summary>
    ManualLight
}

/// <summary>皮肤解析与默认强调色（纯 BCL，供单测直接链接）。</summary>
public static class SkinRules
{
    /// <summary>明暗 → 皮肤。只允许这两种组合，避免出现「黑夜 + 排版手册」。</summary>
    public static UiSkin Resolve(bool dark) => dark ? UiSkin.SignalDark : UiSkin.ManualLight;

    /// <summary>皮肤默认强调色：A 信号青 / C 信号蓝。</summary>
    public static string DefaultAccentKey(UiSkin skin) =>
        skin == UiSkin.SignalDark ? "signal" : "cobalt";
}
