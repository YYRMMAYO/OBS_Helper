using System.IO;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using OBS_Helper.Wpf.Services.Appearance;
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

    /// <summary>自定义背景模式："default"（跟随主题）/"color"（纯色）/"image"（图片）。</summary>
    [JsonPropertyName("bgMode")]
    public string BackgroundMode { get; set; } = "default";

    /// <summary>纯色背景的色值（仅 BackgroundMode=color 时生效）。</summary>
    [JsonPropertyName("bgColor")]
    public string BackgroundColor { get; set; } = "#f4f4fb";

    /// <summary>
    /// 旧版背景图片的绝对路径（仅 BackgroundMode=image 时生效）。
    /// V3.1.0 起作为兼容字段保留：首次启动会把它导入壁纸库，之后以 <see cref="BackgroundWallpaperId"/> 为准。
    /// </summary>
    [JsonPropertyName("bgImage")]
    public string BackgroundImage { get; set; } = "";

    /// <summary>当前选中的壁纸 id（壁纸库 manifest 中的 SHA-256）。</summary>
    [JsonPropertyName("bgWallpaperId")]
    public string BackgroundWallpaperId { get; set; } = "";

    /// <summary>壁纸铺放方式："fill" / "contain" / "center" / "tile"。</summary>
    [JsonPropertyName("bgFit")]
    public string BackgroundFit { get; set; } = "fill";

    /// <summary>压暗强度 0~0.8；-1 表示跟随主题（浅色 15%、深色 40%）。</summary>
    [JsonPropertyName("bgDim")]
    public double BackgroundDim { get; set; } = -1;

    /// <summary>模糊半径 0~24 px（P1，默认关闭）。</summary>
    [JsonPropertyName("bgBlur")]
    public double BackgroundBlur { get; set; }

    /// <summary>是否启用壁纸轮播。</summary>
    [JsonPropertyName("bgSlideshow")]
    public bool BackgroundSlideshow { get; set; }

    /// <summary>轮播间隔（秒，只接受 30/60/300/900/1800/3600）。</summary>
    [JsonPropertyName("bgSlideshowSeconds")]
    public int BackgroundSlideshowSeconds { get; set; } = 300;

    /// <summary>轮播是否随机顺序（一轮内不重复）。</summary>
    [JsonPropertyName("bgShuffle")]
    public bool BackgroundShuffle { get; set; }

    /// <summary>壁纸作用范围：3.1.0 固定 content（整窗为后续版本）。</summary>
    [JsonPropertyName("bgScope")]
    public string BackgroundScope { get; set; } = "content";

    /// <summary>旧设置迁移标记（1 = 已尝试过迁移）。</summary>
    [JsonPropertyName("bgMigrationVersion")]
    public int BackgroundMigrationVersion { get; set; }

    /// <summary>是否允许彩蛋（V3.1.0 / §12.3）：默认开启，可关闭；无声音、不干扰录制。</summary>
    [JsonPropertyName("easterEggEnabled")]
    public bool EasterEggEnabled { get; set; } = true;

    /// <summary>隐藏强调色是否已解锁（彩蛋触发后置 true）。</summary>
    [JsonPropertyName("hiddenAccentUnlocked")]
    public bool HiddenAccentUnlocked { get; set; }

    /// <summary>品牌强调色 key（见 AccentScheme.Catalog），默认青瓷绿。</summary>
    [JsonPropertyName("accent")]
    public string Accent { get; set; } = "teal";

    // ------------------------------------------------------------ 无障碍 / 老年模式（V3.3.0）

    /// <summary>无障碍模式：字号下限 +15%、焦点始终可见、减少动画、显示跳转链接等。</summary>
    [JsonPropertyName("a11yMode")]
    public bool AccessibilityMode { get; set; }

    /// <summary>老年模式：1.3× 字号 + 宽大密度 + 简化布局。可选预设，不推断年龄。</summary>
    [JsonPropertyName("elderMode")]
    public bool ElderMode { get; set; }

    /// <summary>用户显式选择的密度（"compact" / "comfortable" / "spacious"）；空串表示跟随模式预设。</summary>
    [JsonPropertyName("density")]
    public string Density { get; set; } = "";

    /// <summary>用户显式选择的减少动画（"on" / "off"）；空串表示跟随模式预设与系统设置。</summary>
    [JsonPropertyName("reduceMotionOverride")]
    public string ReduceMotionOverride { get; set; } = "";

    /// <summary>用户显式选择的布局（"full" / "simplified"）；空串表示跟随模式预设。</summary>
    [JsonPropertyName("layoutMode")]
    public string LayoutMode { get; set; } = "";
}

/// <summary>
/// 品牌强调色方案（v2.7.1）。
///
/// 每套方案提供浅色 / 深色两整套色阶（基础 / 悬停 / 按下 / 柔和底 / 深变体 / 反衬文字）：
///   - 浅色：基础色较深，与白色反衬文字对比度 ≥4.5:1；
///   - 深色：基础色用「亮品牌色」，既保证按钮内深色反衬文字（DarkOn）可读，
///     也让 BrandBrush 作为前景文字压在深底上时对比度 ≥4.5:1
///     （交叉检验 P1：若沿用深基础色，Ghost/Link 等以品牌色作前景的场景会看不清）。
/// Catalog 顺序即设置页色板展示顺序；Preview 是浅色基础色，用作色板圆点。
/// </summary>
public sealed record AccentScheme(
    string Key, string Preview,
    string LightBase, string LightHover, string LightPressed, string LightSoft, string LightDark,
    string DarkBase, string DarkHover, string DarkPressed, string DarkSoft, string DarkDark,
    string DarkOn,
    /// <summary>隐藏配色（彩蛋解锁后才在设置页出现）。</summary>
    bool Hidden = false)
{
    public static readonly AccentScheme[] Catalog =
    [
        new("teal", "#1a6b5a",
            "#1a6b5a", "#165c4d", "#114a3e", "#ddeee6", "#145244",
            "#4cc9a8", "#5fd4b8", "#3fbb9a", "#0f2e25", "#82dcc8", "#071e17"),
        new("blue", "#3d6da0",
            "#3d6da0", "#33608f", "#274c73", "#e3edf7", "#2e567e",
            "#6fa9dc", "#85b9e4", "#5b99cf", "#14273d", "#a3cdf0", "#0a2036"),
        new("sage", "#4b7061",
            "#4b7061", "#3f6355", "#305044", "#e4eee8", "#385648",
            "#93bfab", "#a7cdbb", "#82b19c", "#16281f", "#b4d8c6", "#12241b"),
        new("deepteal", "#0f766e",
            "#0f766e", "#0d6560", "#0a5350", "#d7f0ec", "#0b5b55",
            "#4ec6ba", "#64d2c7", "#41b5aa", "#0d2c29", "#90ddd4", "#06211e"),
        new("violet", "#6c42c9",
            "#6c42c9", "#5c36b0", "#472a8e", "#ede5fc", "#4e2fa0",
            "#9b7ef0", "#ad94f5", "#9070e8", "#251c3f", "#c4aff8", "#140a30"),
        // 彩蛋配色：解锁后才出现（默认隐藏，不影响现有 5 套）
        new("midnight", "#1f6f8b",
            "#155e78", "#125066", "#0e4254", "#d9eef5", "#114657",
            "#4cb8d6", "#67c6e0", "#3aa6c4", "#0c2833", "#8fd8ea", "#04161d",
            Hidden: true),
    ];

    public static readonly AccentScheme Default = Catalog[0];

    /// <summary>按 key 取方案；未知值（旧配置损坏 / 未来版本删除）回退默认。</summary>
    public static AccentScheme Resolve(string? key) =>
        Catalog.FirstOrDefault(a => a.Key == key) ?? Default;
}

/// <summary>
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

    /// <summary>
    /// 壁纸服务（V3.1.0）。图片模式的解码 / 缓存 / 轮播由它负责，
    /// 外观服务只保留「默认底 / 纯色 / 高对比回退」与主题调色板。
    /// 由组合根在 <see cref="Initialize"/> 之前注入，避免首次 Apply 时图片丢失。
    /// </summary>
    public Services.Appearance.WallpaperService? Wallpapers { get; set; }

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

    /// <summary>
    /// 实际生效的高对比（V3.0 无障碍）= 应用内开关 <b>或</b> Windows 的「高对比主题」。
    ///
    /// 为什么要读系统设置：以前只认应用内开关，系统级高对比用户在深色应用里得不到应有的配色 ——
    /// 而高对比是低视力用户最主要的辅助手段。这里只在系统确实开启时才改变配色，
    /// 且不改写用户自己的设置（关闭系统高对比后自动恢复原样）。
    /// </summary>
    public bool HighContrastEffective => Settings.HighContrast || IsSystemHighContrast;

    /// <summary>系统级「高对比」是否开启（取不到时按未开启处理）。</summary>
    public static bool IsSystemHighContrast
    {
        get
        {
            try { return SystemParameters.HighContrast; }
            catch (Exception) { return false; }
        }
    }

    /// <summary>
    /// Windows「辅助功能 → 文本大小」的缩放系数（1.0 = 100%）。
    ///
    /// WPF 不直接暴露这个值，只能读注册表：
    /// <c>HKCU\Software\Microsoft\Accessibility\TextScaleFactor</c>（DWORD，100 即 100%）。
    /// 读不到按 1.0 处理；钳到 1.0~2.0，避免极端值把界面撑破。
    /// </summary>
    public static double SystemTextScaleFactor
    {
        get
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Accessibility");
                if (k?.GetValue("TextScaleFactor") is int v && v > 0)
                    return Math.Clamp(v / 100.0, 1.0, 2.0);
            }
            catch (Exception)
            {
                // 读不到就当没有系统缩放
            }
            return 1.0;
        }
    }

    /// <summary>系统在浅色/深色切换时会广播 UserPreferenceChanged(General)。</summary>
    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;

        // V3.0：高对比开关也走这条事件。无论用户选的是哪种主题，系统高对比一变都要重刷配色。
        if (Theme != AppTheme.System)
        {
            if (!Settings.HighContrast && (IsSystemHighContrast || _lastSystemHighContrast))
            {
                _lastSystemHighContrast = IsSystemHighContrast;
                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    Apply();
                    Changed?.Invoke();
                }));
            }
            return;
        }

        _lastSystemHighContrast = IsSystemHighContrast;
        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            Apply();
            Changed?.Invoke();
        }));
    }

    /// <summary>上一次观察到的系统高对比状态（用于判断「系统高对比刚变化」）。</summary>
    private bool _lastSystemHighContrast;

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

    /// <summary>当前生效的强调色方案（未知 key 回退默认）。</summary>
    public AccentScheme CurrentAccent => AccentScheme.Resolve(Settings.Accent);

    /// <summary>切换品牌强调色（按钮 / 选中态 / 小标等全局品牌色）。</summary>
    public void SetAccent(string key)
    {
        Settings.Accent = AccentScheme.Resolve(key).Key;
        SaveAndApply();
    }

    public void SetReduceMotion(bool on)
    {
        Settings.ReduceMotion = on;
        // V3.3.0：显式设置优先于「无障碍 / 老年模式」的预设（预设只改默认值）
        Settings.ReduceMotionOverride = on ? "on" : "off";
        SaveAndApply();
    }

    // ------------------------------------------------------------ 无障碍 / 老年模式（V3.3.0）

    /// <summary>无障碍模式是否开启。</summary>
    public bool AccessibilityMode => Settings.AccessibilityMode;

    /// <summary>老年模式是否开启。</summary>
    public bool ElderMode => Settings.ElderMode;

    /// <summary>当前生效的界面令牌（字号 / 密度 / 焦点 / 导航宽度…）。</summary>
    public UiProfileTokens CurrentUiProfile => UiProfileCore.Compute(BuildUiProfileInput());

    /// <summary>实际生效的「减少动画」（系统设置与模式预设都算下限）。</summary>
    public bool ReduceMotionEffective => CurrentUiProfile.ReduceMotion;

    private UiProfileInput BuildUiProfileInput() => new()
    {
        AccessibilityMode = Settings.AccessibilityMode,
        ElderMode = Settings.ElderMode,
        SystemTextScale = SystemTextScaleFactor,
        SystemReduceMotion = IsSystemReduceMotion,
        FontScaleKey = Settings.FontScale,
        DensityKey = Settings.Density,
        ReduceMotionOverride = Settings.ReduceMotionOverride switch
        {
            "on" => true,
            "off" => false,
            // 老配置没有 override 字段：ReduceMotion=true 视为显式开启，false 视为未设置
            _ => Settings.ReduceMotion ? true : null
        },
        LayoutKey = Settings.LayoutMode
    };

    /// <summary>
    /// 系统「显示动画」开关的反面。WPF 不直接暴露「减少动画」，只能读 ClientAreaAnimation：
    /// 系统关闭动画时它为 false。读不到按「不减少」处理，不让异常把外观初始化打断。
    /// </summary>
    public static bool IsSystemReduceMotion
    {
        get
        {
            try { return !SystemParameters.ClientAreaAnimation; }
            catch (Exception) { return false; }
        }
    }

    /// <summary>切换无障碍模式。</summary>
    public void SetAccessibilityMode(bool on)
    {
        Settings.AccessibilityMode = on;
        SaveAndApply();
    }

    /// <summary>切换老年模式（可选预设，不推断年龄）。</summary>
    public void SetElderMode(bool on)
    {
        Settings.ElderMode = on;
        SaveAndApply();
    }

    /// <summary>设置密度（显式选择优先于模式预设）。</summary>
    public void SetDensity(UiDensity density)
    {
        Settings.Density = UiProfileCore.DensityKey(density);
        SaveAndApply();
    }

    /// <summary>设置布局形态（显式选择优先于模式预设）。</summary>
    public void SetLayoutMode(UiLayoutMode mode)
    {
        Settings.LayoutMode = UiProfileCore.LayoutKey(mode);
        SaveAndApply();
    }

    /// <summary>恢复默认：关闭两个模式，清空所有单项覆盖（字号 / 高对比等仍保留用户原值）。</summary>
    public void ResetUiProfile()
    {
        Settings.AccessibilityMode = false;
        Settings.ElderMode = false;
        Settings.Density = "";
        Settings.LayoutMode = "";
        Settings.ReduceMotionOverride = "";
        Settings.ReduceMotion = false;
        SaveAndApply();
    }

    // ------------------------------------------------------------ 彩蛋（V3.1.0 / §12.3）

    /// <summary>是否允许彩蛋（默认开启；设置页可关闭）。</summary>
    public bool EasterEggEnabled => Settings.EasterEggEnabled;

    public void SetEasterEggEnabled(bool on)
    {
        Settings.EasterEggEnabled = on;
        SaveAndApply();
    }

    /// <summary>设置页可见的强调色：隐藏配色只在解锁后出现。</summary>
    public IReadOnlyList<AccentScheme> VisibleAccents => Settings.HiddenAccentUnlocked
        ? AccentScheme.Catalog
        : AccentScheme.Catalog.Where(a => !a.Hidden).ToArray();

    /// <summary>
    /// 解锁隐藏强调色（KONAMI 彩蛋）。返回 true 表示本次是新解锁。
    /// 纯本地、无声音、不弹置顶窗口；「减少动画」下也不会有动画。
    /// </summary>
    public bool UnlockHiddenAccent()
    {
        var isNew = !Settings.HiddenAccentUnlocked;
        Settings.HiddenAccentUnlocked = true;
        Settings.Accent = "midnight";
        SaveAndApply();
        return isNew;
    }

    /// <summary>设置自定义背景模式：default / color / image。</summary>
    public void SetBackgroundMode(string mode)
    {
        Settings.BackgroundMode = mode switch
        {
            "color" => "color",
            "image" => "image",
            _ => "default"
        };
        SaveAndApply();
    }

    /// <summary>设置纯色背景（同时切到 color 模式）。非法色值会被 Apply 阶段回退到默认底。</summary>
    public void SetBackgroundColor(string hex)
    {
        Settings.BackgroundMode = "color";
        Settings.BackgroundColor = string.IsNullOrWhiteSpace(hex) ? "#f4f4fb" : hex.Trim();
        SaveAndApply();
    }

    /// <summary>
    /// 设置旧版背景图片路径（同时切到 image 模式）；传 null/空串清除图片并回到默认。
    /// V3.1.0 起界面不再直接调用它，保留给旧配置与导入流程。
    /// </summary>
    public void SetBackgroundImage(string? path)
    {
        Settings.BackgroundMode = string.IsNullOrWhiteSpace(path) ? "default" : "image";
        Settings.BackgroundImage = path?.Trim() ?? "";
        SaveAndApply();
    }

    /// <summary>选中壁纸库中的一张（同时切到 image 模式）；未知 id / 空串回退默认底。</summary>
    public void SetWallpaper(string? wallpaperId)
    {
        var id = wallpaperId?.Trim() ?? "";
        Settings.BackgroundWallpaperId = id;
        Settings.BackgroundMode = id.Length == 0 ? "default" : "image";
        SaveAndApply();
    }

    /// <summary>清除当前壁纸选择，回到主题默认底（不删除壁纸库文件）。</summary>
    public void RestoreDefaultBackground()
    {
        Settings.BackgroundWallpaperId = "";
        Settings.BackgroundImage = "";
        Settings.BackgroundMode = "default";
        SaveAndApply();
    }

    /// <summary>壁纸铺放方式（fill / contain / center / tile）。</summary>
    public void SetWallpaperFit(Services.Appearance.WallpaperFit fit)
    {
        Settings.BackgroundFit = Services.Appearance.WallpaperCore.FitKey(fit);
        SaveAndApply();
    }

    /// <summary>压暗强度 0~0.8；传负数恢复「跟随主题」。</summary>
    public void SetWallpaperDim(double dim)
    {
        Settings.BackgroundDim = dim < 0
            ? Services.Appearance.WallpaperCore.AutoDim
            : Math.Clamp(dim, Services.Appearance.WallpaperCore.MinDim, Services.Appearance.WallpaperCore.MaxDim);
        SaveAndApply();
    }

    /// <summary>模糊半径 0~24 px。</summary>
    public void SetWallpaperBlur(double blur)
    {
        Settings.BackgroundBlur = Services.Appearance.WallpaperCore.NormalizeBlur(blur);
        SaveAndApply();
    }

    /// <summary>轮播开关 / 间隔 / 随机顺序。</summary>
    public void SetWallpaperSlideshow(bool enabled, int seconds, bool shuffle)
    {
        Settings.BackgroundSlideshow = enabled;
        Settings.BackgroundSlideshowSeconds = Services.Appearance.WallpaperCore.NormalizeSlideshowSeconds(seconds);
        Settings.BackgroundShuffle = shuffle;
        SaveAndApply();
    }

    /// <summary>
    /// 标记旧壁纸设置已完成一次迁移，不重新应用外观。
    /// 迁移失败但文件已确定丢失时也要标记，避免每次启动重复提示。
    /// </summary>
    public void MarkWallpaperMigrationDone()
    {
        Settings.BackgroundMigrationVersion = Services.Appearance.WallpaperCore.MigrationVersion;
        _store.SetObject(StorageKey, Settings);
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

    /// <summary>生成主题化的 DropShadowEffect（偏移 + 柔和模糊），并冻结以便跨控件共享。</summary>
    private static DropShadowEffect NewShadow(double opacity, double blur, double depth)
    {
        var effect = new DropShadowEffect
        {
            Color = Colors.Black,
            Opacity = opacity,
            BlurRadius = blur,
            ShadowDepth = depth,
            Direction = 270
        };
        effect.Freeze();
        return effect;
    }

    /// <summary>把当前设置写进应用级资源字典。控件样式通过 DynamicResource 感知变化。</summary>
    public void Apply()
    {
        var app = Application.Current;
        if (app is null) return;

        var res = app.Resources;
        var dark = IsDarkEffective;
        // V3.0：高对比 = 应用内开关 OR 系统高对比主题（见 HighContrastEffective 的注释）
        var hc = HighContrastEffective;
        _lastSystemHighContrast = IsSystemHighContrast;
        // V3.3.0：无障碍 / 老年模式的组合结果 —— 字号、密度、焦点、导航宽度都从这里取，
        // 页面不再各自判断模式（避免「模式打架」与两套互相矛盾的规则）。
        var profile = CurrentUiProfile;

        // ---- 品牌色（v2.7.1 强调色体系）：由用户在设置页选择，浅 / 深色各一套色阶。
        // BrandBrush 既作主按钮背景（配 BrandForegroundBrush 反衬文字），也大量用作
        // 前景文字 / 描边，因此深色模式用「亮品牌色」保证两类用途都可读（交叉检验 P1）。
        var accent = AccentScheme.Resolve(Settings.Accent);
        res["BrandBrush"] = Frozen(dark ? accent.DarkBase : accent.LightBase);
        res["BrandDarkBrush"] = Frozen(dark ? accent.DarkDark : accent.LightDark);
        res["BrandSoftBrush"] = Frozen(dark ? accent.DarkSoft : accent.LightSoft);
        // 【UI 优化 v1.10】主按钮悬停 / 按下色阶
        res["BrandHoverBrush"] = Frozen(dark ? accent.DarkHover : accent.LightHover);
        res["BrandPressedBrush"] = Frozen(dark ? accent.DarkPressed : accent.LightPressed);
        // 品牌底上的反衬文字：浅色=白字压深品牌底；深色=深字压亮品牌底
        res["BrandForegroundBrush"] = dark ? Frozen(accent.DarkOn) : Frozen("#ffffff");

        // ---- 表面 / 文字 / 边框
        res["SurfaceBrush"] = Frozen(dark ? "#1b1c26" : "#ffffff");
        res["Surface2Brush"] = Frozen(dark ? "#14151d" : "#f4f4fb");
        res["Surface3Brush"] = Frozen(dark ? "#20212c" : "#ececf6");
        res["TextBrush"] = Frozen(hc ? (dark ? "#ffffff" : "#000000") : (dark ? "#ececf5" : "#1c1d2b"));
        // 【深色对比度 v2.7.1】MutedBrush 深色 #9a9bb0→#a9abbe：在 Surface(#1b1c26) 上
        // 对比度约 6:1 → 7.5:1，弱光 / 低亮度屏幕下的次要文字不再发灰难读。
        // 浅色同步对齐 Palette.xaml 的 #5c5d70（P3-1 结论），消除静态默认与运行时的漂移。
        res["MutedBrush"] = Frozen(hc ? (dark ? "#e2e2e8" : "#222222") : (dark ? "#a9abbe" : "#5c5d70"));
        // 【深色对比度 v2.7.1】LineBrush 深色 #2c2d3a→#3b3d50：边框与卡片底拉开层次
        // （对 Surface 对比约 1.25:1 → 1.6:1），输入框 / 分隔线在深色下可辨。
        res["LineBrush"] = Frozen(hc ? (dark ? "#ffffff" : "#000000") : (dark ? "#3b3d50" : "#e6e6f0"));

        // ---- 语义色（严重度 / 状态）
        // 浅色 Danger 由 #d92d20 加深为 #c62828（P2-3）：保证 Danger 文字放在 DangerSoft 底
        // 上的对比度 ≥4.5:1（原 4.23:1 不达标），白底 / Surface2 上同步达标。
        res["DangerBrush"] = Frozen(dark ? "#ff6b6b" : "#c62828");
        res["DangerSoftBrush"] = Frozen(dark ? "#3a1e1e" : "#fdeceb");
        // 【UI 优化 v1.10】危险按钮悬停 / 按下色阶
        res["DangerHoverBrush"] = Frozen(dark ? "#ff8080" : "#ab2121");
        res["DangerPressedBrush"] = Frozen(dark ? "#ff9696" : "#8f1a1a");
        // 【深色对比度 v2.7.1】危险底反衬文字：深色模式 #ff6b6b 偏亮，白字仅 2.8:1，
        // 改用深红字压亮红底（约 6:1）；浅色维持白字压深红。
        res["DangerForegroundBrush"] = dark ? Frozen("#340a0a") : Frozen("#ffffff");

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

        // ---- 【深色对比度 v2.7.1】彩色小标（徽标 / 色板圆点 / 分类 pill）反衬文字：
        // 深色模式语义色整体提亮后，白字普遍掉到 3:1 以下；统一改深色字压亮底，
        // 浅色模式维持白字（浅色语义色都偏深，白字达标）。
        res["PillForegroundBrush"] = dark ? Frozen("#11131c") : Frozen("#ffffff");

        // ---- 阴影强度（用不透明度模拟 CSS 的 shadow-sm / md）
        res["ShadowOpacity"] = dark ? 0.45 : 0.10;

        // ---- 【UI 优化 v1.10】卡片投影：带偏移 + 柔和的真实阴影（craft-floor：深度必须 offset+blur）
        // DropShadowEffect 是 Freezable，按主题生成同名资源，卡片 / 卡片按钮 / Toast 统一引用。
        res["CardShadow"] = NewShadow(dark ? 0.35 : 0.10, blur: 18, depth: 2);
        res["CardShadowStrong"] = NewShadow(dark ? 0.50 : 0.16, blur: 24, depth: 4);

        // ---- 圆角（紧凑风格，减少 AI 感）
        res["CornerRadiusLg"] = new CornerRadius(8);
        res["CornerRadiusMd"] = new CornerRadius(6);
        res["CornerRadiusSm"] = new CornerRadius(4);
        // V3.1.0 UI 视觉刷新 1.0：设计令牌（间距 / 圆角 / 动效），新页面优先引用它们，
        // 避免每处硬编码 8 / 12 / 16 造成视觉漂移；19 页的全量重设计留到 3.2。
        res["CornerRadiusXl"] = new CornerRadius(12);
        res["SpacingXs"] = 4.0;
        res["SpacingSm"] = 8.0;
        res["SpacingMd"] = 12.0;
        res["SpacingLg"] = 16.0;
        res["SpacingXl"] = 24.0;
        res["MotionFast"] = profile.ReduceMotion
            ? new Duration(TimeSpan.Zero)
            : new Duration(TimeSpan.FromMilliseconds(100));
        res["MotionSlow"] = profile.ReduceMotion
            ? new Duration(TimeSpan.Zero)
            : new Duration(TimeSpan.FromMilliseconds(300));

        // ---- 字号档位
        //
        // V3.3.0：字号由 UiProfileCore 统一算出 —— 用户档位 → 无障碍 / 老年模式下限 →
        // Windows「辅助功能 → 文本大小」的缩放（系统是下限，不是上限）。
        // 应用内档位最大 1.28×，对低视力用户常常不够；模式打开后至少 1.15× / 1.30×。
        var f = profile.FontScale;
        res["FontScaleFactor"] = f;
        res["FontSizeXs"] = Math.Round(11 * f, 1);
        res["FontSizeSm"] = Math.Round(12.5 * f, 1);
        res["FontSizeBase"] = Math.Round(14 * f, 1);
        res["FontSizeMd"] = Math.Round(15.5 * f, 1);
        res["FontSizeLg"] = Math.Round(18 * f, 1);
        res["FontSizeXl"] = Math.Round(22 * f, 1);
        res["FontSizeH1"] = Math.Round(28 * f, 1);

        // ---- 动画时长：减少动画时直接归零，样式里的 Duration 统一引用它
        res["MotionDuration"] = profile.ReduceMotion
            ? new Duration(TimeSpan.Zero)
            : new Duration(TimeSpan.FromMilliseconds(160));
        res["ReduceMotion"] = profile.ReduceMotion;

        // ---- 无障碍 / 老年模式的布局令牌（V3.3.0 / P0-1）
        // 页面与样式只引用这些键，不再各写一套 if；切换模式后 DynamicResource 即时生效。
        res["NavWidth"] = new GridLength(profile.NavWidth);
        res["NavItemMinHeight"] = profile.NavItemMinHeight;
        res["NavItemPadding"] = new Thickness(
            profile.NavItemPaddingX, profile.NavItemPaddingY,
            profile.NavItemPaddingX, profile.NavItemPaddingY);
        res["ControlMinHeight"] = profile.ControlMinHeight;
        res["ButtonMinSize"] = profile.ButtonMinSize;
        res["CardPadding"] = new Thickness(profile.CardPadding);
        res["FocusThickness"] = new Thickness(profile.FocusThickness);
        res["FocusOffset"] = profile.FocusOffset;
        res["LineHeightFactor"] = profile.LineHeightFactor;
        res["ShowSkipLink"] = profile.ShowSkipLink;
        res["UiSimplifyLayout"] = profile.Layout == UiLayoutMode.Simplified;
        res["UiAssistMode"] = profile.AnyAssistMode;
        res["UiDensity"] = UiProfileCore.DensityKey(profile.Density);

        // ---- 【自定义背景 v1.10】内容区背景：默认跟随主题；可选纯色 / 本地图片。
        // 高对比模式强制回退默认底，保证可读性优先于个性化。
        ApplyBackgroundResources(res, dark, hc);
    }

    // ------------------------------------------------------------ 自定义背景

    /// <summary>按当前设置写入内容区背景资源：ContentBackgroundBrush / ContentBackgroundImage / ContentBackgroundDim。</summary>
    private void ApplyBackgroundResources(ResourceDictionary res, bool dark, bool hc)
    {
        if (hc || Settings.BackgroundMode == "default")
        {
            res["ContentBackgroundBrush"] = Frozen(dark ? "#14151d" : "#f4f4fb");
            ClearWallpaperResources(res);
            return;
        }

        if (Settings.BackgroundMode == "color")
        {
            var brush = TryParseBrush(Settings.BackgroundColor)
                        ?? Frozen(dark ? "#14151d" : "#f4f4fb");
            res["ContentBackgroundBrush"] = brush;
            ClearWallpaperResources(res);
            return;
        }

        // 图片模式（V3.1.0）：底仍是主题默认底，图片、压暗、铺放方式由 WallpaperService 写入。
        // 高对比已在上面强制回退；文件丢失 / 损坏时服务层回退默认底并给出一次可见提示。
        res["ContentBackgroundBrush"] = Frozen(dark ? "#14151d" : "#f4f4fb");
        res["ContentBackgroundDim"] = Brushes.Transparent;
        res["ContentBackgroundImage"] = null;
        res["ContentBackgroundImageA"] = null;
        res["ContentBackgroundImageB"] = null;
        Wallpapers?.ApplyResources(res, dark);
    }

    /// <summary>清空壁纸相关资源（默认 / 纯色 / 高对比共用）。</summary>
    private static void ClearWallpaperResources(ResourceDictionary res)
    {
        res["ContentBackgroundImage"] = null;
        res["ContentBackgroundImageA"] = null;
        res["ContentBackgroundImageB"] = null;
        res["ContentBackgroundDim"] = Brushes.Transparent;
        res["ContentBackgroundOpacity"] = 1.0;
        res["ContentBackgroundBlurRadius"] = 0.0;
        res["ContentBackgroundFadeDuration"] = new Duration(TimeSpan.Zero);
    }

    private static SolidColorBrush? TryParseBrush(string hex)
    {
        try
        {
            if (ColorConverter.ConvertFromString(hex) is Color c)
            {
                var b = new SolidColorBrush(c);
                b.Freeze();
                return b;
            }
        }
        catch (Exception)
        {
            // 非法色值
        }
        return null;
    }
}
