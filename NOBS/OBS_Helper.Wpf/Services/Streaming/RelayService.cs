using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using OBS_Helper.Wpf.Models;
using OBS_Helper.Wpf.Services.Host;
using OBS_Helper.Wpf.Services.Interop;
using OBS_Helper.Wpf.Services.Mobile;
using OBS_Helper.Wpf.Services.Obs;
using OBS_Helper.Wpf.Services.Security;
using OBS_Helper.Wpf.Services.Streaming;

namespace OBS_Helper.Wpf.Services.Streaming;

/// <summary>
/// 一键多平台转推（Q01）：mediamtx 本地收流 + 每个目标一个 FFmpeg 拉流转发。
///
/// 为什么这样拆：OBS 只能推一路；让 OBS 推到本机 mediamtx，再由多个 FFmpeg 从回环 RTSP
/// 拉流并 `-c copy` 推到各平台，避免转码、避免把推流码写进日志。目标密钥只进 secrets.dat，
/// 界面只显示脱敏地址；失败时不回显密钥。
/// </summary>
public sealed class MultiPlatformRelayService : IDisposable
{
    private readonly SidecarManager _sidecars;
    private readonly ObsConnectionService _obs;
    private readonly LocalStore _store;
    private readonly HostBridge _host;
    private readonly ProcessGuard _guard = new();
    private string _ffmpegPath;
    private readonly object _gate = new();
    private string? _configPath;
    private int _rtmpPort, _rtspPort, _apiPort;
    private string _streamKey = "";

    public MultiPlatformRelayService(SidecarManager sidecars, ObsConnectionService obs, LocalStore store, HostBridge host, string? ffmpegPath = null)
    {
        _sidecars = sidecars;
        _obs = obs;
        _store = store;
        _host = host;
        _ffmpegPath = ffmpegPath ?? "";
    }

    public bool IsRunning { get; private set; }
    public string FfmpegPath => _ffmpegPath;
    public void SetFfmpegPath(string? path) => _ffmpegPath = path ?? "";
    public string IngestUrl => IsRunning ? $"rtmp://127.0.0.1:{_rtmpPort}/live" : "";
    public string StreamKey => IsRunning ? _streamKey : "";
    public string LastError { get; private set; } = "";
    public event Action? StateChanged;

    public IReadOnlyList<RelayCore.Target> LoadTargets()
        => _store.GetObject<List<RelayCore.Target>>("stream.relayTargets") ?? new List<RelayCore.Target>();

    public void SaveTargets(IEnumerable<RelayCore.Target> targets)
        => _store.SetObject("stream.relayTargets", targets.ToList());

    /// <summary>开始转推：启动 mediamtx 收流，并为每个目标启动一个 FFmpeg 拉流进程。</summary>
    public async Task<(bool Ok, string Code)> StartAsync(IEnumerable<RelayCore.Target> targets, CancellationToken ct = default)
    {
        if (IsRunning) return (true, "");
        if (!OsSupport.IsWindows10OrNewer(OsSupport.Current)) return (false, FeatureErrors.CamUnsupportedWin7);
        var entry = _sidecars.Manifest.Find("mediamtx");
        if (entry is null || !_sidecars.IsInstalled(entry)) return (false, FeatureErrors.CamSidecarMissing);
        if (string.IsNullOrWhiteSpace(_ffmpegPath) || !File.Exists(_ffmpegPath)) return (false, FeatureErrors.ClipFfmpegMissing);
        var enabled = targets.Where(t => t.Enabled).ToList();
        var check = RelayCore.Check(enabled);
        if (!check.Ok) return (false, FeatureErrors.StreamRelayFailed);

        try
        {
            _rtmpPort = FreePort();
            _rtspPort = FreePort();
            _apiPort = FreePort();
        }
        catch (SocketException) { return (false, FeatureErrors.CamPortInUse); }

        _streamKey = LanPolicy.NewToken(16);
        var dir = Path.Combine(_sidecars.Root, "mediamtx", entry.Version, "relay");
        Directory.CreateDirectory(dir);
        _configPath = Path.Combine(dir, "mediamtx-relay.yml");
        await File.WriteAllTextAsync(_configPath, $"""
logLevel: warn
rtmp: yes
rtmpAddress: 127.0.0.1:{_rtmpPort}
rtsp: yes
rtspAddress: 127.0.0.1:{_rtspPort}
srt: no
hls: no
webrtc: no
api: yes
apiAddress: 127.0.0.1:{_apiPort}
paths:
  live:
    source: publisher
""", ct).ConfigureAwait(false);

        if (!_sidecars.Start(entry, new[] { _configPath })) return (false, FeatureErrors.CamSidecarStartFailed);
        foreach (var target in enabled)
        {
            var key = await _host.GetSecretAsync("relay." + target.Id + ".key").ConfigureAwait(false) ?? target.StreamKey;
            var url = target.RtmpUrl.TrimEnd('/');
            var args = new[]
            {
                "-hide_banner", "-nostdin", "-loglevel", "warning",
                "-rtsp_transport", "tcp",
                "-i", $"rtsp://127.0.0.1:{_rtspPort}/live",
                "-c", "copy", "-f", "flv", $"{url}/{key}",
            };
            _guard.Start("relay-" + target.Id, _ffmpegPath, args);
        }
        lock (_gate) IsRunning = true;
        StateChanged?.Invoke();
        return (true, "");
    }

    public void Stop()
    {
        foreach (var target in LoadTargets()) _guard.Stop("relay-" + target.Id);
        var entry = _sidecars.Manifest.Find("mediamtx");
        if (entry is not null) _sidecars.Stop(entry);
        var config = _configPath;
        _configPath = null;
        try { if (config is not null) File.Delete(config); } catch { }
        lock (_gate) IsRunning = false;
        StateChanged?.Invoke();
    }

    /// <summary>把 OBS 推流设置指到本地收流；旧设置保存到 prefs，停止时可还原。</summary>
    public async Task<(bool Ok, string Code)> PointObsToRelayAsync()
    {
        if (!IsRunning) return (false, FeatureErrors.StreamRelayFailed);
        var current = await _obs.GetStreamServiceSettingsRawAsync().ConfigureAwait(false);
        if (current.Ok && current.Data is { } data)
            _store.SetItem("stream.relay.previousSettings", data.GetRawText());
        var result = await _obs.SetStreamServiceSettingsAsync(new
        {
            streamServiceType = "rtmp_custom",
            streamServiceSettings = new { server = IngestUrl, key = _streamKey, use_auth = false },
        }).ConfigureAwait(false);
        return result.Ok ? (true, "") : (false, FeatureErrors.StreamRelayFailed);
    }

    public async Task<(bool Ok, string Code)> RestoreObsAsync()
    {
        var raw = _store.GetItem("stream.relay.previousSettings");
        if (string.IsNullOrWhiteSpace(raw)) return (false, FeatureErrors.StreamRelayFailed);
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var result = await _obs.SetStreamServiceSettingsAsync(doc.RootElement.Clone()).ConfigureAwait(false);
            return result.Ok ? (true, "") : (false, FeatureErrors.StreamRelayFailed);
        }
        catch (JsonException) { return (false, FeatureErrors.StreamRelayFailed); }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public void Dispose()
    {
        Stop();
        _guard.Dispose();
    }
}
