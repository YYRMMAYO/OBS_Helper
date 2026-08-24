using System.IO;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
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

    /// <summary>自定义背景模式："default"（跟随主题）/"color"（纯色）/"image"（图片）。</summary>
    [JsonPropertyName("bgMode")]
    public string BackgroundMode { get; set; } = "default";

    /// <summary>纯色背景的色值（仅 BackgroundMode=color 时生效）。</summary>
    [JsonPropertyName("bgColor")]
    public string BackgroundColor { get; set; } = "#f4f4fb";

    /// <summary>背景图片的本地路径（仅 BackgroundMode=image 时生效）。</summary>
    [JsonPropertyName("bgImage")]
    public string BackgroundImage { get; set; } = "";

    /// <summary>品牌强调色 key（见 AccentScheme.Catalog），默认青瓷绿。</summary>
    [JsonPropertyName("accent")]
    public string Accent { get; set; } = "teal";
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
    string Key, string Name, string Preview,
    string LightBase, string LightHover, string LightPressed, string LightSoft, string LightDark,
    string DarkBase, string DarkHover, string DarkPressed, string DarkSoft, string DarkDark,
    string DarkOn)
{
    public static readonly AccentScheme[] Catalog =
    [
        new("teal", "青瓷绿", "#157a70",
            "#157a70", "#12655d", "#0e5049", "#dcf0ec", "#105f57",
            "#45c6b6", "#5cd4c5", "#38b7a8", "#10312d", "#86dfd3", "#06231e"),
        new("blue", "海盐蓝", "#3d6da0",
            "#3d6da0", "#33608f", "#274c73", "#e3edf7", "#2e567e",
            "#6fa9dc", "#85b9e4", "#5b99cf", "#14273d", "#a3cdf0", "#0a2036"),
        new("sage", "鼠尾草绿", "#4b7061",
            "#4b7061", "#3f6355", "#305044", "#e4eee8", "#385648",
            "#93bfab", "#a7cdbb", "#82b19c", "#16281f", "#b4d8c6", "#12241b"),
        new("deepteal", "深海松石", "#0f766e",
            "#0f766e", "#0d6560", "#0a5350", "#d7f0ec", "#0b5b55",
            "#4ec6ba", "#64d2c7", "#41b5aa", "#0d2c29", "#90ddd4", "#06211e"),
        new("violet", "雾紫（经典）", "#7b2ff7",
            "#7b2ff7", "#6a1fd0", "#4d15ad", "#efe6ff", "#5a1fc4",
            "#a685f5", "#b79df9", "#a184f0", "#2a1f47", "#cbb6fb", "#170b33"),
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
        SaveAndApply();
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

    /// <summary>设置背景图片路径（同时切到 image 模式）；传 null/空串清除图片并回到默认。</summary>
    public void SetBackgroundImage(string? path)
    {
        Settings.BackgroundMode = string.IsNullOrWhiteSpace(path) ? "default" : "image";
        Settings.BackgroundImage = path?.Trim() ?? "";
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
        var hc = Settings.HighContrast;

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

        // ---- 【自定义背景 v1.10】内容区背景：默认跟随主题；可选纯色 / 本地图片。
        // 高对比模式强制回退默认底，保证可读性优先于个性化。
        ApplyBackgroundResources(res, dark, hc);
    }

    // ------------------------------------------------------------ 自定义背景

    private ImageSource? _bgImageCache;
    private string? _bgImageCachePath;

    /// <summary>按当前设置写入内容区背景资源：ContentBackgroundBrush / ContentBackgroundImage / ContentBackgroundDim。</summary>
    private void ApplyBackgroundResources(ResourceDictionary res, bool dark, bool hc)
    {
        if (hc || Settings.BackgroundMode == "default")
        {
            res["ContentBackgroundBrush"] = Frozen(dark ? "#14151d" : "#f4f4fb");
            res["ContentBackgroundImage"] = null;
            res["ContentBackgroundDim"] = Brushes.Transparent;
            return;
        }

        if (Settings.BackgroundMode == "color")
        {
            var brush = TryParseBrush(Settings.BackgroundColor)
                        ?? Frozen(dark ? "#14151d" : "#f4f4fb");
            res["ContentBackgroundBrush"] = brush;
            res["ContentBackgroundImage"] = null;
            res["ContentBackgroundDim"] = Brushes.Transparent;
            return;
        }

        // image 模式：图片加载失败（文件被移动 / 删除）时静默回退默认底，不弹错误。
        var img = LoadBackgroundImage(Settings.BackgroundImage);
        res["ContentBackgroundBrush"] = Frozen(dark ? "#14151d" : "#f4f4fb");
        res["ContentBackgroundImage"] = img;
        // 图片上方加一层柔和压暗遮罩，保证卡片与文字可读性（craft-floor：可读性优先）
        res["ContentBackgroundDim"] = img is null
            ? Brushes.Transparent
            : Frozen(dark ? "#66000000" : "#22000000");
    }

    /// <summary>加载本地背景图片；带路径缓存，文件缺失返回 null。</summary>
    private ImageSource? LoadBackgroundImage(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (string.Equals(_bgImageCachePath, path, StringComparison.OrdinalIgnoreCase))
            return _bgImageCache;

        ImageSource? loaded = null;
        try
        {
            if (File.Exists(path))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                loaded = bmp;
            }
        }
        catch (Exception)
        {
            loaded = null; // 文件损坏 / 权限不足：回退默认底
        }

        _bgImageCachePath = path;
        _bgImageCache = loaded;
        return loaded;
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
