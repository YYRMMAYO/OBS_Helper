namespace OBS_Helper.Wpf.Models.Obs;

/// <summary>
/// obs-websocket 5.x 协议操作码（WebSocketOpCode）。
/// 参考：https://github.com/obsproject/obs-websocket/blob/master/docs/generated/protocol.md
/// </summary>
public static class ObsOpCode
{
    /// <summary>服务端 → 客户端：连接建立后的第一条消息，含鉴权挑战。</summary>
    public const int Hello = 0;
    /// <summary>客户端 → 服务端：应答鉴权并声明订阅。</summary>
    public const int Identify = 1;
    /// <summary>服务端 → 客户端：鉴权通过，握手完成。</summary>
    public const int Identified = 2;
    /// <summary>客户端 → 服务端：更新订阅（不重新鉴权）。</summary>
    public const int Reidentify = 3;
    /// <summary>服务端 → 客户端：事件推送。</summary>
    public const int Event = 5;
    /// <summary>客户端 → 服务端：单条请求。</summary>
    public const int Request = 6;
    /// <summary>服务端 → 客户端：单条请求的响应。</summary>
    public const int RequestResponse = 7;
}

/// <summary>
/// 事件订阅位掩码。Identify 时传入，决定服务端推送哪些类别的事件。
/// 高频类别（音量表 / 变换）默认不订阅，避免无谓的 CPU 与消息开销。
/// </summary>
[Flags]
public enum ObsEventSubscription
{
    None = 0,
    General = 1 << 0,
    Config = 1 << 1,
    Scenes = 1 << 2,
    Inputs = 1 << 3,
    Transitions = 1 << 4,
    Filters = 1 << 5,
    Outputs = 1 << 6,
    SceneItems = 1 << 7,
    MediaInputs = 1 << 8,
    Vendors = 1 << 9,
    Ui = 1 << 10,

    /// <summary>高频事件：输入音量表（每秒数十次），仅在监控页可见时按需开启。</summary>
    InputVolumeMeters = 1 << 16,
    InputActiveStateChanged = 1 << 17,
    InputShowStateChanged = 1 << 18,
    SceneItemTransformChanged = 1 << 19,

    /// <summary>助手默认订阅集合：足以驱动控制面板与状态监控，且不含高频事件。</summary>
    Default = General | Config | Scenes | Inputs | Outputs | SceneItems | Ui
}

/// <summary>obs-websocket 请求返回码（RequestStatus）中的常见取值。</summary>
public static class ObsRequestStatusCode
{
    public const int Success = 100;
    public const int MissingRequestType = 203;
    public const int UnknownRequestType = 204;
    public const int ResourceNotFound = 600;
    public const int InvalidResourceState = 604;
    public const int NotReady = 207;
}

/// <summary>连接状态机。</summary>
public enum ObsConnectionState
{
    Disconnected,
    Connecting,
    Authenticating,
    Connected,
    Reconnecting,
    Failed
}

/// <summary>一次请求的结果。<see cref="Ok"/> 为 false 时 <see cref="Comment"/> 含服务端说明。</summary>
public sealed class ObsRequestResult
{
    public bool Ok { get; init; }
    public int Code { get; init; }
    public string? Comment { get; init; }
    public System.Text.Json.JsonElement? Data { get; init; }

    public static ObsRequestResult Fail(int code, string comment) => new() { Ok = false, Code = code, Comment = comment };
}

/// <summary>CallBatch（Request Batch）的一条子请求。</summary>
public sealed class ObsBatchRequest
{
    public required string RequestType { get; init; }
    public object? RequestData { get; init; }
}

/// <summary>服务端推送的事件。</summary>
public sealed class ObsEventMessage
{
    public string EventType { get; init; } = "";
    public System.Text.Json.JsonElement Data { get; init; }
}

// ---------------------------------------------------------------------------
// 领域 DTO：仅保留 UI 与诊断实际需要的字段，避免过度建模。
// ---------------------------------------------------------------------------

/// <summary>一个 OBS 场景（名称 + 排序 + 是否当前场景）。</summary>
public sealed class ObsSceneInfo
{
    public string Name { get; set; } = "";
    public int Index { get; set; }
    public bool IsCurrent { get; set; }
}

/// <summary>一个 OBS 输入源（含静音 / 音量等 UI 所需字段）。</summary>
public sealed class ObsInputInfo
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public bool Muted { get; set; }
    /// <summary>音量（dB）。OBS 用 -100 表示静音下限。</summary>
    public float VolumeDb { get; set; }
    /// <summary>是否为音频类输入（可静音 / 可调音量）。</summary>
    public bool IsAudio { get; set; }
}

public sealed class ObsSceneItemInfo
{
    public int Id { get; set; }
    public string SourceName { get; set; } = "";
    public bool Enabled { get; set; }
    public bool Locked { get; set; }
}

/// <summary>OBS 实时性能统计（GetStats + GetVideoSettings 汇总）。</summary>
public sealed class ObsStats
{
    public double CpuUsage { get; set; }
    public double MemoryUsageMb { get; set; }
    public double AvailableDiskSpaceMb { get; set; }
    public double ActiveFps { get; set; }
    public double AverageFrameRenderTimeMs { get; set; }
    public long RenderSkippedFrames { get; set; }
    public long RenderTotalFrames { get; set; }
    public long OutputSkippedFrames { get; set; }
    public long OutputTotalFrames { get; set; }

    /// <summary>渲染丢帧率（GPU / 画面合成压力）。</summary>
    public double RenderSkipRatio => RenderTotalFrames > 0 ? (double)RenderSkippedFrames / RenderTotalFrames : 0;
    /// <summary>输出丢帧率（编码压力）。</summary>
    public double OutputSkipRatio => OutputTotalFrames > 0 ? (double)OutputSkippedFrames / OutputTotalFrames : 0;
}

/// <summary>录制 / 推流 / 虚拟摄像头的输出状态。</summary>
public sealed class ObsOutputStatus
{
    public bool Active { get; set; }
    public bool Paused { get; set; }
    public bool Reconnecting { get; set; }
    public string Timecode { get; set; } = "00:00:00.000";
    public long Bytes { get; set; }
    /// <summary>推流拥塞度 0~1，越高说明上行越吃紧（仅推流有效）。</summary>
    public double Congestion { get; set; }
    public long SkippedFrames { get; set; }
    public long TotalFrames { get; set; }

    public double DroppedRatio => TotalFrames > 0 ? (double)SkippedFrames / TotalFrames : 0;
}

/// <summary>OBS 版本与视频输出配置，用于诊断「分辨率 / 帧率 / 缩放」类问题。</summary>
public sealed class ObsProfileInfo
{
    public string ObsVersion { get; set; } = "";
    public string WebSocketVersion { get; set; } = "";
    public string Platform { get; set; } = "";
    public int BaseWidth { get; set; }
    public int BaseHeight { get; set; }
    public int OutputWidth { get; set; }
    public int OutputHeight { get; set; }
    public double Fps { get; set; }
}
