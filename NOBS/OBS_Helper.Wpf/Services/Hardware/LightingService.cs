using System.Text.Json;
using OBS_Helper.Wpf.Services.Automation;
using OBS_Helper.Wpf.Services.Chat;
using OBS_Helper.Wpf.Services.Host;
using OBS_Helper.Wpf.Services.Obs;

namespace OBS_Helper.Wpf.Services.Hardware;

/// <summary>
/// 灯光联动（E02）：WLED HTTP JSON，开播/礼物/待机触发预设；本地 API、超时降级、不依赖云。
/// 设置存 prefs.json（不含密钥）；OpenRGB 只做外部 SDK 引导，不静态链接。
/// </summary>
public sealed class LightingService : IDisposable
{
    private const string SettingsKey = "hardware.wled";
    private readonly WledClient _client;
    private readonly ChatHub _chat;
    private readonly ObsConnectionService _obs;
    private readonly LocalStore _store;
    private readonly object _gate = new();

    public LightingService(WledClient client, ChatHub chat, ObsConnectionService obs, LocalStore store)
    {
        _client = client;
        _chat = chat;
        _obs = obs;
        _store = store;
        Config = store.GetObject<Settings>(SettingsKey) ?? new Settings();
    }

    public sealed class Settings
    {
        public bool Enabled { get; set; }
        public string BaseUrl { get; set; } = "";
        public string GiftPreset { get; set; } = "gift";
        public string LivePreset { get; set; } = "live";
        public string IdlePreset { get; set; } = "idle";
    }

    public Settings Current => Config;
    private Settings Config { get; set; }

    public string? LastError => _client.LastError;
    public event Action<string>? Applied;

    public void ApplySettings(Settings settings)
    {
        Config = settings;
        _store.SetObject(SettingsKey, settings);
        if (settings.Enabled) Start();
        else Stop();
    }

    public void Start()
    {
        _chat.MessageAccepted -= OnChat;
        _obs.StateChanged -= OnObs;
        if (!Config.Enabled) return;
        _chat.MessageAccepted += OnChat;
        _obs.StateChanged += OnObs;
        _ = ApplyAsync(_obs.StreamStatus.Active ? Config.LivePreset : Config.IdlePreset);
    }

    public void Stop()
    {
        _chat.MessageAccepted -= OnChat;
        _obs.StateChanged -= OnObs;
    }

    private void OnChat(ChatMessage message)
    {
        if (message.Kind is "gift" or "superchat") _ = ApplyAsync(Config.GiftPreset);
    }

    private void OnObs()
    {
        var preset = _obs.StreamStatus.Active ? Config.LivePreset : Config.IdlePreset;
        _ = ApplyAsync(preset);
    }

    public async Task<bool> ApplyAsync(string presetId)
    {
        if (!Config.Enabled || string.IsNullOrWhiteSpace(Config.BaseUrl)) return false;
        var preset = WledCore.Presets.FirstOrDefault(p => p.Id == presetId) ?? WledCore.Presets[0];
        var ok = await _client.ApplyAsync(Config.BaseUrl, preset).ConfigureAwait(false);
        if (ok) Applied?.Invoke(preset.Id);
        return ok;
    }

    public void Dispose() => Stop();
}
