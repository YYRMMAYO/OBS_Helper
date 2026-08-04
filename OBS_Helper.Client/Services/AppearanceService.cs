using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace OBS_Helper.Client.Services;

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

/// <summary>持久化到 localStorage 的外观设置。</summary>
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
/// 外观与无障碍设置（技术计划 §9「可访问性」）。
///
/// 设计要点：
/// <list type="bullet">
///   <item>所有设置通过 <c>&lt;html&gt;</c> 上的 <c>data-*</c> 属性驱动 CSS 变量，
///         不写内联样式，方便主题整体切换。</item>
///   <item>存储键 <c>obshelper.appearance</c> 与 index.html 里的首屏内联脚本一致，
///         使刷新时不会先闪一下默认主题再切换（FOUC）。</item>
///   <item>不依赖宿主，纯浏览器能力，浏览器里直接打开也能用。</item>
/// </list>
/// </summary>
public sealed class AppearanceService
{
    private const string StorageKey = "obshelper.appearance";

    private readonly IJSRuntime _js;
    private bool _loaded;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AppearanceService(IJSRuntime js) => _js = js;

    public AppearanceSettings Settings { get; private set; } = new();

    /// <summary>设置变更时触发，供页面重绘。</summary>
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

    /// <summary>从 localStorage 载入设置并套用到文档根元素。只会真正执行一次。</summary>
    public async Task InitializeAsync()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            var raw = await _js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (!string.IsNullOrWhiteSpace(raw))
                Settings = JsonSerializer.Deserialize<AppearanceSettings>(raw, JsonOpts) ?? new();
        }
        catch (Exception)
        {
            // 存储被禁用或内容损坏：用默认外观，不影响主流程
            Settings = new AppearanceSettings();
        }
        await ApplyAsync();
    }

    public Task SetThemeAsync(AppTheme theme)
    {
        Settings.Theme = theme switch
        {
            AppTheme.Light => "light",
            AppTheme.Dark => "dark",
            _ => "system"
        };
        return SaveAndApplyAsync();
    }

    public Task SetFontScaleAsync(AppFontScale scale)
    {
        Settings.FontScale = scale switch
        {
            AppFontScale.Sm => "sm",
            AppFontScale.Lg => "lg",
            AppFontScale.Xl => "xl",
            _ => "md"
        };
        return SaveAndApplyAsync();
    }

    public Task SetHighContrastAsync(bool on)
    {
        Settings.HighContrast = on;
        return SaveAndApplyAsync();
    }

    public Task SetReduceMotionAsync(bool on)
    {
        Settings.ReduceMotion = on;
        return SaveAndApplyAsync();
    }

    private async Task SaveAndApplyAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(Settings, JsonOpts);
            await _js.InvokeVoidAsync("localStorage.setItem", StorageKey, json);
        }
        catch (Exception)
        {
            // 无痕模式下 localStorage 可能抛异常：本次会话内仍然生效
        }
        await ApplyAsync();
        Changed?.Invoke();
    }

    private async Task ApplyAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("obsHelperUi.applyAppearance",
                Settings.Theme, Settings.FontScale, Settings.HighContrast, Settings.ReduceMotion);
        }
        catch (Exception)
        {
            // JS 尚未就绪（极早期调用）：下一次设置变更时会重新套用
        }
    }
}
