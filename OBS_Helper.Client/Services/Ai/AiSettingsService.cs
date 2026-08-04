using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;
using OBS_Helper.Client.Services.Host;

namespace OBS_Helper.Client.Services.Ai;

/// <summary>诊断引擎模式。</summary>
public enum DiagnosticEngineMode
{
    /// <summary>本地离线规则引擎（默认，无需联网、不依赖密钥）。</summary>
    Local,
    /// <summary>云端大模型（通过桌面宿主转发，密钥不进 WebAssembly）。</summary>
    Cloud
}

/// <summary>持久化的 AI 设置（不含任何密钥本身）。</summary>
public sealed class AiSettings
{
    [JsonPropertyName("mode")] public string Mode { get; set; } = "local";
    [JsonPropertyName("cloudUrl")] public string CloudUrl { get; set; } = "";
    /// <summary>API Key 在桌面宿主密钥存储中的「键名」，不是密钥值。</summary>
    [JsonPropertyName("cloudSecretKeyName")] public string CloudSecretKeyName { get; set; } = "obs_ai_apikey";
    [JsonPropertyName("cloudModel")] public string CloudModel { get; set; } = "gpt-4o-mini";
}

/// <summary>
/// AI 诊断引擎的运行时设置（技术计划 §4.5「可切换的本地/云端 AI」）。
///
/// 设计要点：
/// <list type="bullet">
///   <item>默认本地引擎，企业/个人可一键切到云端大模型；切换状态持久化到 localStorage。</item>
///   <item>云端只存「接口地址 + 密钥键名 + 模型名」，真正的 API Key 由桌面宿主加密保存，
///         前端只传键名，密钥全程不进入 WebAssembly 内存（见 <see cref="HostBridge.AiChatAsync"/>）。</item>
///   <item><see cref="IsCloudConfigured"/> 用于在不触碰密钥的前提下判断云端是否「可用」，
///         避免把未配置状态误当成已配置去发请求。</item>
/// </list>
/// </summary>
public sealed class AiSettingsService
{
    private const string StorageKey = "obshelper.ai";
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly IJSRuntime _js;
    private readonly HostBridge _host;
    private bool _loaded;

    public AiSettingsService(IJSRuntime js, HostBridge host)
    {
        _js = js;
        _host = host;
    }

    public AiSettings Settings { get; private set; } = new();

    /// <summary>设置变更时触发，供设置页刷新。</summary>
    public event Action? Changed;

    public DiagnosticEngineMode Mode => Settings.Mode == "cloud" ? DiagnosticEngineMode.Cloud : DiagnosticEngineMode.Local;

    /// <summary>云端是否「逻辑上可用」：模式为云端、地址为 https、密钥键名非空。</summary>
    public bool IsCloudConfigured
        => Mode == DiagnosticEngineMode.Cloud
           && Uri.TryCreate(Settings.CloudUrl, UriKind.Absolute, out var u)
           && u.Scheme == "https"
           && !string.IsNullOrWhiteSpace(Settings.CloudSecretKeyName);

    public async Task LoadAsync()
    {
        if (_loaded) return;
        _loaded = true;
        await _host.ProbeAsync();
        try
        {
            var raw = await _js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (!string.IsNullOrWhiteSpace(raw))
                Settings = JsonSerializer.Deserialize<AiSettings>(raw, JsonOpts) ?? new();
        }
        catch (Exception)
        {
            // 存储不可用：用默认（本地引擎），不影响主流程。
            Settings = new AiSettings();
        }
    }

    public async Task SetModeAsync(DiagnosticEngineMode mode)
    {
        Settings.Mode = mode == DiagnosticEngineMode.Cloud ? "cloud" : "local";
        await SaveAsync();
    }

    public async Task SetCloudAsync(string url, string secretKeyName, string model)
    {
        Settings.CloudUrl = (url ?? "").Trim();
        Settings.CloudSecretKeyName = string.IsNullOrWhiteSpace(secretKeyName) ? "obs_ai_apikey" : secretKeyName.Trim();
        Settings.CloudModel = (model ?? "").Trim();
        await SaveAsync();
    }

    private async Task SaveAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("localStorage.setItem", StorageKey, JsonSerializer.Serialize(Settings, JsonOpts));
        }
        catch (Exception)
        {
            // 无痕模式可能抛异常：本次会话内仍然生效。
        }
        Changed?.Invoke();
    }
}
