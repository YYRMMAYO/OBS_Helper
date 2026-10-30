using System.Globalization;
using OBS_Helper.Wpf.Services.Mobile;
using System.Text.Json;

namespace OBS_Helper.Wpf.Services.Live;

/// <summary>多机位轻量导播（M09）：2~4 路、布局槽位、切换与画中画、带宽估算。</summary>
public static class MultiCamCore
{
    public const int MaxCameras = 4;

    public enum Layout { Grid2x2, Pip, SideBySide, Single }

    public sealed class Slot
    {
        public string CameraId { get; init; } = "";
        public string SceneName { get; init; } = "";
        public bool Enabled { get; init; } = true;
        public int BitrateKbps { get; init; }
    }

    public static (int X, int Y, int Width, int Height) SlotBounds(Layout layout, int index, int canvasWidth, int canvasHeight)
    {
        if (canvasWidth <= 0 || canvasHeight <= 0) return (0, 0, 0, 0);
        var halfW = canvasWidth / 2;
        var halfH = canvasHeight / 2;
        return layout switch
        {
            Layout.Grid2x2 => (index % 2 * halfW, index / 2 * halfH, halfW, halfH),
            Layout.Pip => index == 0 ? (0, 0, canvasWidth, canvasHeight)
                : (canvasWidth * 3 / 4, canvasHeight * 3 / 4, canvasWidth / 4, canvasHeight / 4),
            Layout.SideBySide => index == 0 ? (0, 0, halfW, canvasHeight) : (halfW, 0, halfW, canvasHeight),
            _ => index == 0 ? (0, 0, canvasWidth, canvasHeight) : (0, 0, 0, 0),
        };
    }

    public static bool CanAdd(IReadOnlyCollection<Slot> slots) => slots.Count(s => s.Enabled) < MaxCameras;

    public static (bool Ok, string Code) Validate(IEnumerable<Slot> slots, double uploadKbps)
    {
        var enabled = slots.Where(s => s.Enabled).ToList();
        if (enabled.Count == 0) return (false, FeatureErrors.MultiSourceFailed);
        if (enabled.Count > MaxCameras) return (false, FeatureErrors.MultiLimit);
        var total = enabled.Sum(s => s.BitrateKbps);
        if (uploadKbps > 0 && total > uploadKbps * 0.7) return (false, FeatureErrors.StreamBandwidthLow);
        return (true, "");
    }

    /// <summary>切换机位：返回新顺序（目标排到第一，其余保持相对顺序）。</summary>
    public static IReadOnlyList<Slot> Switch(IReadOnlyList<Slot> slots, string cameraId)
        => slots.OrderBy(s => s.CameraId == cameraId ? 0 : 1).ToList();
}

/// <summary>远程嘉宾房间（M10）：房间 token、角色、踢人、录制同意、人数上限。</summary>
public sealed class RemoteGuestCore
{
    public const int MaxGuests = 4;

    public sealed class Guest
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string Name { get; init; } = "";
        public string Role { get; init; } = "guest"; // host / guest / viewer
        public bool RecordingConsent { get; set; }
        public DateTimeOffset JoinedUtc { get; init; } = DateTimeOffset.UtcNow;
    }

    public sealed class Room
    {
        public string RoomId { get; init; } = Guid.NewGuid().ToString("N")[..12];
        public string TokenHash { get; init; } = "";
        public string TurnUrl { get; init; } = "";
        public List<Guest> Guests { get; } = new();
        public List<string> Kicked { get; } = new();
        public bool RecordingEnabled { get; set; }
        public bool TurnEnabled { get; init; }
    }

    public static (string Token, Room Room) Create(string? turnUrl = null)
    {
        var token = Services.Security.LanPolicy.NewToken(32);
        return (token, new Room
        {
            TokenHash = Services.Security.LanPolicy.HashToken(token),
            TurnUrl = turnUrl ?? "",
            TurnEnabled = !string.IsNullOrWhiteSpace(turnUrl),
        });
    }

    public static (bool Ok, string Code) Join(Room room, string token, string name, bool recordingConsent)
    {
        if (!Services.Security.LanPolicy.FixedTimeEquals(room.TokenHash, Services.Security.LanPolicy.HashToken(token)))
            return (false, FeatureErrors.RemoteRoomFull);
        if (room.Guests.Count >= MaxGuests) return (false, FeatureErrors.RemoteRoomFull);
        room.Guests.Add(new Guest { Name = name, RecordingConsent = recordingConsent });
        return (true, "");
    }

    public static bool Kick(Room room, string guestId)
    {
        var guest = room.Guests.FirstOrDefault(g => g.Id == guestId);
        if (guest is null) return false;
        room.Guests.Remove(guest);
        room.Kicked.Add(guestId);
        return true;
    }

    public static bool CanRecord(Room room) => room.RecordingEnabled && room.Guests.All(g => g.RecordingConsent);

    /// <summary>mediamtx WebRTC 配置片段（只在启用远程嘉宾时开启 webrtc）。</summary>
    public static string BuildMediamtxConfig(int webrtcPort, int apiPort, string? turnUrl)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("webrtc: yes");
        sb.AppendLine("webrtcAddress: 127.0.0.1:" + webrtcPort.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine("webrtcEncryption: no");
        sb.AppendLine("webrtcAllowOrigins: []");
        if (!string.IsNullOrWhiteSpace(turnUrl)) sb.AppendLine("webrtcICEServers2: [" + JsonSerializer.Serialize(new { url = turnUrl }) + "]");
        sb.AppendLine("api: yes");
        sb.AppendLine("apiAddress: 127.0.0.1:" + apiPort.ToString(CultureInfo.InvariantCulture));
        return sb.ToString();
    }
}

/// <summary>多手机监看（M12）：viewer 只读、3 并发、互不干扰。</summary>
public static class MultiViewerCore
{
    public const int MaxSessions = 3;

    public sealed class Session
    {
        public string DeviceId { get; init; } = "";
        public string Role { get; init; } = "viewer";
        public DateTimeOffset StartedUtc { get; init; } = DateTimeOffset.UtcNow;
    }

    public static bool CanAdd(IReadOnlyCollection<Session> sessions) => sessions.Count < MaxSessions;

    public static (bool Ok, string Code) Register(List<Session> sessions, string deviceId, string role)
    {
        if (sessions.Any(s => s.DeviceId == deviceId)) return (true, "");
        if (!CanAdd(sessions)) return (false, FeatureErrors.MobRateLimited);
        sessions.Add(new Session { DeviceId = deviceId, Role = role });
        return (true, "");
    }

    public static bool Remove(List<Session> sessions, string deviceId) => sessions.RemoveAll(s => s.DeviceId == deviceId) > 0;
}
