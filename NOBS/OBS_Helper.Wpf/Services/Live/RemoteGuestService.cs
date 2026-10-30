using System.IO;
using System.Net;
using System.Net.Sockets;
using OBS_Helper.Wpf.Services.Interop;
using OBS_Helper.Wpf.Services.Mobile;
using OBS_Helper.Wpf.Services.Security;

namespace OBS_Helper.Wpf.Services.Live;

/// <summary>
/// 远程嘉宾房间（M10）。
///
/// 边界（PLAN_V3.4.0 §4.7 / §6.5）：
/// <list type="bullet">
///   <item>默认只做局域网/自建；不自动开 TURN、不做公网穿透、不捆绑 SFU；</item>
///   <item>房间 token 只存哈希；踢人立即生效；录制需要所有在场嘉宾同意；</item>
///   <item><b>BLOCKED</b>：浏览器 <c>getUserMedia</c> 在非 localhost 的 HTTP 页面上不可用，
///     而本项目不自带 HTTPS 证书。因此本服务负责房间、权限、SFU 配置与混流接入，
///     实际“浏览器进房推流”需要用户自备 HTTPS 反向代理或 LiveKit/mediasoup 的 TLS 部署。
///     界面必须如实说明，不允许把“房间已创建”冒充成“嘉宾已能推流”。</item>
/// </list>
/// </summary>
public sealed class RemoteGuestService : IDisposable
{
    private readonly SidecarManager _sidecars;
    private readonly Func<Version> _osVersion;
    private readonly object _gate = new();
    private string? _configPath;
    private int _webrtcPort;
    private int _apiPort;

    public RemoteGuestService(SidecarManager sidecars, Func<Version>? osVersion = null)
    {
        _sidecars = sidecars;
        _osVersion = osVersion ?? (() => OsSupport.Current);
    }

    public RemoteGuestCore.Room? Room { get; private set; }
    public string GuestUrl { get; private set; } = "";
    public bool IsRunning => Room is not null && _configPath is not null;
    public bool MediaPathAvailable => false;
    public string MediaBlockedReason => "浏览器推流需要 HTTPS（安全上下文）；本项目不内置证书。请自备 HTTPS 反向代理或 LiveKit/mediasoup 的 TLS 部署。";

    public (bool Ok, string Code, string Invite) Start(string lanAddress)
    {
        if (!OsSupport.IsWindows10OrNewer(_osVersion())) return (false, FeatureErrors.CamUnsupportedWin7, "");
        if (!LanPolicy.IsPrivateAddress(lanAddress)) return (false, FeatureErrors.CamNoPrivateIp, "");
        var entry = _sidecars.Manifest.Find("mediamtx");
        if (entry is null || !_sidecars.IsInstalled(entry)) return (false, FeatureErrors.CamSidecarMissing, "");
        if (IsRunning) return (true, "", GuestUrl);

        try
        {
            _webrtcPort = FreePort();
            _apiPort = FreePort();
        }
        catch (SocketException) { return (false, FeatureErrors.CamPortInUse, ""); }

        var (token, room) = RemoteGuestCore.Create();
        var dir = Path.Combine(_sidecars.Root, "mediamtx", entry.Version, "remote");
        Directory.CreateDirectory(dir);
        var config = Path.Combine(dir, "mediamtx-webrtc.yml");
        File.WriteAllText(config, RemoteGuestCore.BuildMediamtxConfig(_webrtcPort, _apiPort, null));
        if (!_sidecars.Start(entry, new[] { config })) return (false, FeatureErrors.CamSidecarStartFailed, "");

        Room = room;
        _configPath = config;
        GuestUrl = $"http://{lanAddress}:{_webrtcPort}/";
        return (true, "", GuestUrl + "?room=" + room.RoomId + "&token=" + token);
    }

    public void Stop()
    {
        var entry = _sidecars.Manifest.Find("mediamtx");
        if (entry is not null) _sidecars.Stop(entry);
        var config = _configPath;
        Room = null;
        _configPath = null;
        GuestUrl = "";
        try { if (config is not null) File.Delete(config); } catch { }
    }

    public (bool Ok, string Code) Join(string token, string name, bool consent)
    {
        if (Room is null) return (false, FeatureErrors.RemoteRoomFull);
        return RemoteGuestCore.Join(Room, token, name, consent);
    }

    public bool Kick(string guestId) => Room is not null && RemoteGuestCore.Kick(Room, guestId);

    public void SetRecording(bool enabled)
    {
        if (Room is not null) Room.RecordingEnabled = enabled;
    }

    public bool CanRecord => Room is not null && RemoteGuestCore.CanRecord(Room);

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public void Dispose() => Stop();
}
