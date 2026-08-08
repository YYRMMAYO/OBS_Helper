using System.Text.Json.Serialization;
using OBS_Helper.Wpf.Services.Host;

namespace OBS_Helper.Wpf.Services.Ai;

/// <summary>诊断引擎模式。</summary>
public enum DiagnosticEngineMode
{
    /// <summary>本地离线规则引擎（默认，无需联网、不依赖密钥）。</summary>
    Local,
    /// <summary>免费内置 AI（无需注册与 API Key，本地强制每日限次，适合低频使用）。</summary>
    Free,
    /// <summary>云端大模型（由宿主转发，密钥仅存于 DPAPI 加密存储）。</summary>
    Cloud
}

/// <summary>持久化的 AI 设置（不含任何密钥本身）。</summary>
public sealed class AiSettings
{
    [JsonPropertyName("mode")] public string Mode { get; set; } = "local";
    [JsonPropertyName("cloudUrl")] public string CloudUrl { get; set; } = "";
    /// <summary>API Key 在机密存储中的「键名」，不是密钥值。</summary>
    [JsonPropertyName("cloudSecretKeyName")] public string CloudSecretKeyName { get; set; } = "obs_ai_apikey";
    [JsonPropertyName("cloudModel")] public string CloudModel { get; set; } = "gpt-4o-mini";
    /// <summary>免费内置 AI 使用的模型名（默认 openai，见 <see cref="AiSettingsService.FreeEndpointUrl"/>）。</summary>
    [JsonPropertyName("freeModel")] public string FreeModel { get; set; } = "openai";
}

/// <summary>
/// AI 诊断引擎的运行时设置（可切换的本地 / 云端引擎）。
///
/// 设计要点：
/// <list type="bullet">
///   <item>默认本地引擎，可一键切到云端大模型；切换状态持久化到 prefs.json。</item>
///   <item>云端只存「接口地址 + 密钥键名 + 模型名」，真正的 API Key 由 DPAPI 加密保存，
///         调用时才由宿主取出拼装请求头（见 <see cref="HostBridge.AiChatAsync"/>）。</item>
///   <item><see cref="IsCloudConfigured"/> 用于在不触碰密钥的前提下判断云端是否「可用」。</item>
/// </list>
/// </summary>
public sealed class AiSettingsService
{
    private const string StorageKey = "obshelper.ai";

    /// <summary>免费内置 AI 的接口地址（无需 API Key 的 OpenAI 兼容端点）。</summary>
    public const string FreeEndpointUrl = "https://text.pollinations.ai/openai";

    /// <summary>免费内置 AI 默认模型。</summary>
    public const string DefaultFreeModel = "openai";

    private readonly LocalStore _store;
    private readonly HostBridge _host;
    private bool _loaded;

    public AiSettingsService(LocalStore store, HostBridge host)
    {
        _store = store;
        _host = host;
    }

    public AiSettings Settings { get; private set; } = new();

    /// <summary>设置变更时触发，供设置页刷新。</summary>
    public event Action? Changed;

    public DiagnosticEngineMode Mode => Settings.Mode switch
    {
        "free" => DiagnosticEngineMode.Free,
        "cloud" => DiagnosticEngineMode.Cloud,
        _ => DiagnosticEngineMode.Local
    };

    /// <summary>免费内置 AI 是否「可用」：无需任何配置，选即用。模型名空时回退默认值。</summary>
    public bool IsFreeAvailable => Mode == DiagnosticEngineMode.Free;

    /// <summary>取免费模式实际使用的模型名（空值回退默认）。</summary>
    public string EffectiveFreeModel
        => string.IsNullOrWhiteSpace(Settings.FreeModel) ? DefaultFreeModel : Settings.FreeModel.Trim();

    /// <summary>云端是否「逻辑上可用」：模式为云端、地址为 https、密钥键名非空。</summary>
    public bool IsCloudConfigured
        => Mode == DiagnosticEngineMode.Cloud
           && Uri.TryCreate(Settings.CloudUrl, UriKind.Absolute, out var u)
           && u.Scheme == "https"
           && !string.IsNullOrWhiteSpace(Settings.CloudSecretKeyName);

    public Task LoadAsync()
    {
        if (_loaded) return Task.CompletedTask;
        _loaded = true;

        var s = _store.GetObject<AiSettings>(StorageKey);
        if (s is not null) Settings = s;
        return Task.CompletedTask;
    }

    public Task SetModeAsync(DiagnosticEngineMode mode)
    {
        Settings.Mode = mode switch
        {
            DiagnosticEngineMode.Free => "free",
            DiagnosticEngineMode.Cloud => "cloud",
            _ => "local"
        };
        return SaveAsync();
    }

    /// <summary>保存免费模式的模型名（空值会回退默认，与 EffectiveFreeModel 口径一致）。</summary>
    public Task SetFreeModelAsync(string model)
    {
        Settings.FreeModel = (model ?? "").Trim();
        return SaveAsync();
    }

    public Task SetCloudAsync(string url, string secretKeyName, string model)
    {
        var trimmed = (url ?? "").Trim();

        // 保存前即时校验：非法地址立刻报错，而不是等诊断时才被发现。
        // 空地址允许（用户可能想先清空，之后通过 IsCloudConfigured 感知「未配置」）。
        if (!string.IsNullOrEmpty(trimmed))
        {
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps)
                throw new ArgumentException("云端接口地址必须是 https:// 开头的完整 URL。");
            if (HostBridge.IsPrivateHost(u.Host))
                throw new ArgumentException("出于安全考虑，云端接口不能指向本机或内网地址。");
        }

        Settings.CloudUrl = trimmed;
        Settings.CloudSecretKeyName = string.IsNullOrWhiteSpace(secretKeyName) ? "obs_ai_apikey" : secretKeyName.Trim();
        Settings.CloudModel = (model ?? "").Trim();
        return SaveAsync();
    }

    /// <summary>保存 API Key（DPAPI 加密）。传空表示删除。</summary>
    public Task<bool> SetApiKeyAsync(string? apiKey)
        => _host.SetSecretAsync(Settings.CloudSecretKeyName, apiKey ?? "");

    /// <summary>是否已保存 API Key（不返回密钥内容，只回布尔）。</summary>
    public async Task<bool> HasApiKeyAsync()
        => !string.IsNullOrEmpty(await _host.GetSecretAsync(Settings.CloudSecretKeyName).ConfigureAwait(false));

    /// <summary>清除已保存的 API Key。</summary>
    public Task<bool> ClearApiKeyAsync()
        => _host.DeleteSecretAsync(Settings.CloudSecretKeyName);

    private Task SaveAsync()
    {
        _store.SetObject(StorageKey, Settings);
        Changed?.Invoke();
        return Task.CompletedTask;
    }
}
