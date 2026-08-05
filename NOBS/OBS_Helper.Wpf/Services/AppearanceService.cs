using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using OBS_Helper.Wpf.Services.Host;

namespace OBS_Helper.Wpf.Services;

/// <summary>外观主题。</summary>
public enum AppTheme
{
    /// <summary>跟随系统。</summary>
    System,
    Light,
    Dark
}

/// <summary>字号档位。</summary>
public enum AppFontScale
{
    /// <summary>小。</summary>
    Sm,
    /// <summary>标准。</summary>
    Md,
    /// <summary>大。</summary>
    Lg,
    /// <summary>特大（无障碍）。</summary>
    Xl
}

/// <summary>持久化的外观设置。</summary>
public sealed class AppearanceSettings
{
    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "system";

    [JsonPropertyName("fontScale")]
    public string FontScale { get; set; } = "md";

    [JsonPropertyName("highContrast")]
    public bool HighContrast { get; set; }

    /// <summary>减少动画（尊重前庭功能障碍用户）。</summary>
    [JsonPropertyName("reduceMotion")]
    public bool ReduceMotion { get; set; }
}

/// <summary>
/// 外观与无障碍设置。
///
/// 与 Blazor 版的对应关系：原来通过 <c>&lt;html data-theme&gt;</c> 驱动 CSS 变量，
/// WPF 版改为把同一套调色板写进 <see cref="Application.Resources"/>，
/// 所有控件样式用 <c>DynamicResource</c> 引用，从而实现运行时整体换肤。
///
/// 设计要点：
/// <list type="bullet">
///   <item>色值与 Blazor 版 app.css 完全一致，保证两版视觉统一。</item>
///   <item>「跟随系统」读注册表 AppsUseLightTheme，并监听系统主题变更事件实时切换。</item>
///   <item>字号档位换算成一组 FontSize 资源，而不是缩放整个窗口，避免图标与边框糊掉。</item>
///   <item>高对比在基础主题之上叠加覆盖，只改文字 / 边框，不动品牌色。</item>
/// </list>
/// </summary>
public sealed class AppearanceService : IDisposable
{
    private const string StorageKey = "obshelper.appearance";

    private readonly LocalStore _store;
    private bool _loaded;
    private bool _hookedSystemEvents;

    public AppearanceService(LocalStore store)
    {
        _store = store;
    }

    public AppearanceSettings Settings { get; private set; } = new();

    /// <summary>设置变更时触发，供设置页刷新选中态。</summary>
    public event Action? Changed;

    public AppTheme Theme => Settings.Theme switch
    {
        "light" => AppTheme.Light,
        "dark" => AppTheme.Dark,
        _ => AppTheme.System
    };

    public AppFontScale FontScale => Settings.FontScale switch
    {
        "sm" => AppFontScale.Sm,
        "lg" => AppFontScale.Lg,
        "xl" => AppFontScale.Xl,
        _ => AppFontScale.Md
    };

    /// <summary>当前实际生效的是否为深色（把「跟随系统」解析成具体值）。</summary>
    public bool IsDarkEffective => Theme switch
    {
        AppTheme.Dark => true,
        AppTheme.Light => false,
        _ => IsSystemDark()
    };

    /// <summary>载入设置并套用。只会真正执行一次。</summary>
    public void Initialize()
    {
        if (_loaded) return;
        _loaded = true;

        var s = _store.GetObject<AppearanceSettings>(StorageKey);
        if (s is not null) Settings = s;

        if (!_hookedSystemEvents)
        {
            _hookedSystemEvents = true;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }

        Apply();
    }

    /// <summary>系统在浅色/深色切换时会广播 UserPreferenceChanged(General)。</summary>
    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        if (Theme != AppTheme.System) return;
        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            Apply();
            Changed?.Invoke();
        }));
    }

    /// <summary>退出时摘掉系统事件挂钩（SystemEvents 是静态事件，不解绑会拖住对象）。</summary>
    public void Dispose()
    {
        if (!_hookedSystemEvents) return;
        _hookedSystemEvents = false;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }

    public void SetTheme(AppTheme theme)
    {
        Settings.Theme = theme switch
        {
            AppTheme.Light => "light",
            AppTheme.Dark => "dark",
            _ => "system"
        };
        SaveAndApply();
    }

    public void SetFontScale(AppFontScale scale)
    {
        Settings.FontScale = scale switch
        {
            AppFontScale.Sm => "sm",
            AppFontScale.Lg => "lg",
            AppFontScale.Xl => "xl",
            _ => "md"
        };
        SaveAndApply();
    }

    public void SetHighContrast(bool on)
    {
        Settings.HighContrast = on;
        SaveAndApply();
    }

    public void SetReduceMotion(bool on)
    {
        Settings.ReduceMotion = on;
        SaveAndApply();
    }

    private void SaveAndApply()
    {
        _store.SetObject(StorageKey, Settings);
        Apply();
        Changed?.Invoke();
    }

    // ------------------------------------------------------------ 系统主题

    /// <summary>读注册表判断系统是否为深色模式；读不到时按浅色处理。</summary>
    public static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var v = key?.GetValue("AppsUseLightTheme");
            if (v is int i) return i == 0;
        }
        catch (Exception)
        {
            // 注册表不可读（策略限制）：按浅色。
        }
        return false;
    }

    // ------------------------------------------------------------ 调色板

    private static SolidColorBrush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    /// <summary>把当前设置写进应用级资源字典。控件样式通过 DynamicResource 感知变化。</summary>
    public void Apply()
    {
        var app = Application.Current;
        if (app is null) return;

        var res = app.Resources;
        var dark = IsDarkEffective;
        var hc = Settings.HighContrast;

        // ---- 品牌色（深浅一致，仅 soft 底色随主题变化）
        res["BrandBrush"] = Frozen("#7b2ff7");
        res["BrandDarkBrush"] = Frozen("#5a1fc4");
        res["BrandSoftBrush"] = Frozen(dark ? "#2a1f47" : "#efe6ff");

        // ---- 表面 / 文字 / 边框
        res["SurfaceBrush"] = Frozen(dark ? "#1b1c26" : "#ffffff");
        res["Surface2Brush"] = Frozen(dark ? "#14151d" : "#f4f4fb");
        res["Surface3Brush"] = Frozen(dark ? "#20212c" : "#ececf6");
        res["TextBrush"] = Frozen(hc ? (dark ? "#ffffff" : "#000000") : (dark ? "#e9e9f2" : "#1c1d2b"));
        res["MutedBrush"] = Frozen(hc ? (dark ? "#dddddd" : "#222222") : (dark ? "#9a9bb0" : "#6b6c80"));
        res["LineBrush"] = Frozen(hc ? (dark ? "#ffffff" : "#000000") : (dark ? "#2c2d3a" : "#e6e6f0"));

        // ---- 语义色（严重度 / 状态）
        // 浅色 Danger 由 #d92d20 加深为 #c62828（P2-3）：保证 Danger 文字放在 DangerSoft 底
        // 上的对比度 ≥4.5:1（原 4.23:1 不达标），白底 / Surface2 上同步达标。
        res["DangerBrush"] = Frozen(dark ? "#ff6b6b" : "#c62828");
        res["DangerSoftBrush"] = Frozen(dark ? "#3a1e1e" : "#fdeceb");
        res["WarnBrush"] = Frozen(dark ? "#f5a524" : "#b54708");
        res["WarnSoftBrush"] = Frozen(dark ? "#3a2e14" : "#fef4e6");
        res["OkBrush"] = Frozen(dark ? "#3ecf8e" : "#067647");
        res["OkSoftBrush"] = Frozen(dark ? "#12301f" : "#e7f6ee");
        res["InfoBrush"] = Frozen(dark ? "#63a8ff" : "#175cd3");
        res["InfoSoftBrush"] = Frozen(dark ? "#152740" : "#e8f0fe");

        // ---- 分类语义色（P2-1）：浅色与原 problems.json 硬编码一致，深色换柔和值避免刺眼
        res["SemanticRedBrush"] = Frozen(dark ? "#e56a5c" : "#e74c3c");
        res["SemanticOrangeBrush"] = Frozen(dark ? "#f0a35e" : "#e67e22");
        res["SemanticYellowBrush"] = Frozen(dark ? "#e8cf6f" : "#f1c40f");
        res["SemanticPurpleBrush"] = Frozen(dark ? "#c39bd3" : "#9b59b6");
        res["SemanticBlueBrush"] = Frozen(dark ? "#7fb8e8" : "#3498db");
        res["SemanticTealBrush"] = Frozen(dark ? "#63c9b5" : "#1abc9c");
        res["SemanticGreenBrush"] = Frozen(dark ? "#4fbfa4" : "#16a085");
        res["SemanticAzureBrush"] = Frozen(dark ? "#6fa8dc" : "#2980b9");
        res["SemanticVioletBrush"] = Frozen(dark ? "#b08cd6" : "#8e44ad");
        res["SemanticCrimsonBrush"] = Frozen(dark ? "#d96a63" : "#c0392b");

        // ---- 阴影强度（用不透明度模拟 CSS 的 shadow-sm / md）
        res["ShadowOpacity"] = dark ? 0.45 : 0.10;

        // ---- 圆角（与 CSS --radius / --radius-sm 对齐）
        res["CornerRadiusLg"] = new CornerRadius(14);
        res["CornerRadiusMd"] = new CornerRadius(10);
        res["CornerRadiusSm"] = new CornerRadius(6);

        // ---- 字号档位
        var f = FontScale switch
        {
            AppFontScale.Sm => 0.92,
            AppFontScale.Lg => 1.12,
            AppFontScale.Xl => 1.28,
            _ => 1.0
        };
        res["FontScaleFactor"] = f;
        res["FontSizeXs"] = Math.Round(11 * f, 1);
        res["FontSizeSm"] = Math.Round(12.5 * f, 1);
        res["FontSizeBase"] = Math.Round(14 * f, 1);
        res["FontSizeMd"] = Math.Round(15.5 * f, 1);
        res["FontSizeLg"] = Math.Round(18 * f, 1);
        res["FontSizeXl"] = Math.Round(22 * f, 1);
        res["FontSizeH1"] = Math.Round(28 * f, 1);

        // ---- 动画时长：减少动画时直接归零，样式里的 Duration 统一引用它
        res["MotionDuration"] = Settings.ReduceMotion
            ? new Duration(TimeSpan.Zero)
            : new Duration(TimeSpan.FromMilliseconds(160));
        res["ReduceMotion"] = Settings.ReduceMotion;
    }
}
