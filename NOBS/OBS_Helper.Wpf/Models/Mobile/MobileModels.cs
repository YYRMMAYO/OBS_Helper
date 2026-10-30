using System.Text.Json;
using System.Text.Json.Serialization;

namespace OBS_Helper.Wpf.Models.Mobile;

/// <summary>手机设备角色。权限只升不自动降：配对默认 <see cref="Operator"/>，可由 PC 改为 viewer/admin。</summary>
public enum MobileRole
{
    Viewer = 0,
    Operator = 1,
    Admin = 2,
}

public static class MobileRoles
{
    public static string Name(MobileRole role) => role switch
    {
        MobileRole.Viewer => "viewer",
        MobileRole.Operator => "operator",
        MobileRole.Admin => "admin",
        _ => "viewer",
    };

    public static MobileRole Parse(string? value) => (value ?? "").Trim().ToLowerInvariant() switch
    {
        "admin" => MobileRole.Admin,
        "operator" => MobileRole.Operator,
        _ => MobileRole.Viewer,
    };

    /// <summary>角色是否满足最低要求（viewer &lt; operator &lt; admin）。</summary>
    public static bool Allows(MobileRole actual, MobileRole required) => actual >= required;
}

/// <summary>一次配对会话（内存态）。只保存 secret 的哈希。</summary>
public sealed class PairingSession
{
    public required string PairId { get; init; }
    public required string SecretHash { get; init; }
    public required string ShortCode { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset ExpiresUtc { get; init; }
    public bool Consumed { get; set; }
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresUtc;
}

/// <summary>已配对设备（落盘，不含明文 token）。</summary>
public sealed class MobileDevice
{
    public string DeviceId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Role { get; set; } = "operator";
    public string TokenHash { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }
    public DateTimeOffset ExpiresUtc { get; set; }
    public bool Revoked { get; set; }

    [JsonIgnore] public MobileRole ParsedRole => MobileRoles.Parse(Role);
}

/// <summary>配对结果（对手机只暴露成功/失败，失败原因不区分，避免探测）。</summary>
public enum PairOutcome
{
    Ok,
    Expired,
    Used,
    BadSecret,
    UnknownPair,
}

/// <summary>手机端展示用的状态快照（同时是 WS 推送的 payload）。</summary>
public sealed class MobileSnapshot
{
    public string Type { get; set; } = "state";
    public string CurrentScene { get; set; } = "";
    public List<string> Scenes { get; set; } = new();
    public List<MobileAudioState> Audio { get; set; } = new();
    public bool Recording { get; set; }
    public bool RecordPaused { get; set; }
    public string RecordTime { get; set; } = "00:00:00";
    public bool Streaming { get; set; }
    public string StreamTime { get; set; } = "00:00:00";
    public bool VirtualCam { get; set; }
    public bool ReplayBuffer { get; set; }
    public bool StudioMode { get; set; }
    public string Tally { get; set; } = "off";
    public string Role { get; set; } = "viewer";
    public string DeviceName { get; set; } = "";
    public long ObsTimestamp { get; set; }
    public MobileTeleprompterState Teleprompter { get; set; } = new();
    public MobileSystemState System { get; set; } = new();
}

public sealed class MobileAudioState
{
    public string Name { get; set; } = "";
    public bool Muted { get; set; }
    public double VolumeDb { get; set; }
}

public sealed class MobileTeleprompterState
{
    public bool Playing { get; set; }
    public int Speed { get; set; }
    public int FontSize { get; set; }
    public bool Mirror { get; set; }
    public double Offset { get; set; }
    public string Script { get; set; } = "";
}

public sealed class MobileSystemState
{
    public double Cpu { get; set; }
    public double MemoryMb { get; set; }
    public double DiskFreeMb { get; set; }
}

/// <summary>手机 → PC 的命令信封。</summary>
public sealed class MobileCommandRequest
{
    public string RequestId { get; set; } = "";
    public string Command { get; set; } = "";
    public Dictionary<string, JsonElement> Params { get; set; } = new();
}

/// <summary>PC → 手机的响应。永远带同一个 requestId，便于手机端做幂等与超时。</summary>
public sealed class MobileCommandResult
{
    public string Type { get; set; } = "result";
    public string RequestId { get; set; } = "";
    public bool Ok { get; set; }
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public JsonElement? Data { get; set; }

    public static MobileCommandResult Success(string requestId, JsonElement? data = null) => new()
    {
        RequestId = requestId,
        Ok = true,
        Data = data,
    };

    public static MobileCommandResult Fail(string requestId, string code, string message) => new()
    {
        RequestId = requestId,
        Ok = false,
        Code = code,
        Message = message,
    };
}

/// <summary>手机摄像头（Wi-Fi）的画质与布局选项。</summary>
public sealed record PhoneCameraQuality(string Id, int Width, int Height, int Fps, int BitrateKbps)
{
    public static readonly PhoneCameraQuality Hd720 = new("720p30", 1280, 720, 30, 2500);
    public static readonly PhoneCameraQuality Fhd1080 = new("1080p30", 1920, 1080, 30, 5000);
    public static readonly PhoneCameraQuality Portrait = new("portrait720", 720, 1280, 30, 3500);
    public static readonly PhoneCameraQuality Low = new("540p30", 960, 540, 30, 1200);

    public static readonly PhoneCameraQuality[] All = { Hd720, Fhd1080, Portrait, Low };

    public static PhoneCameraQuality Parse(string? id)
        => All.FirstOrDefault(q => string.Equals(q.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Hd720;
}

/// <summary>手机机位布局：全屏 / 右下角画中画 / 左侧三分 / 竖屏裁切。</summary>
public enum PhoneCameraLayout
{
    Fullscreen,
    PipBottomRight,
    SideLeftThird,
    PortraitCrop,
}

/// <summary>摄像头会话状态（推流地址、端口、路径 key、是否已连上）。</summary>
public sealed class PhoneCameraSession
{
    public string CameraId { get; init; } = "cam1";
    public string StreamKey { get; init; } = "";
    public int SrtPort { get; init; }
    public int RtmpPort { get; init; }
    public int RtspPort { get; init; }
    public string LanAddress { get; init; } = "";
    public PhoneCameraQuality Quality { get; init; } = PhoneCameraQuality.Hd720;
    public PhoneCameraLayout Layout { get; set; } = PhoneCameraLayout.Fullscreen;
    public bool WithAudio { get; init; } = true;
    public string SceneName { get; init; } = "";
    public string SourceName { get; init; } = "";
    public bool Ready { get; set; }
    /// <summary>OBS 场景项 id（用于布局变换）；0 表示尚未创建。</summary>
    public int SceneItemId { get; set; }
    public DateTimeOffset StartedUtc { get; init; }
}
