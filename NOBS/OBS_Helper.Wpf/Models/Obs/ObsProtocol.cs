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

    /// <summary>
    /// 客户端**本地**校验失败（请求根本没发出去）。
    ///
    /// 第三轮验证修正：原先用 400，而 obs-websocket 的 400 就是 <c>InvalidRequestField</c>
    /// （OBS 对非法 mediaAction 等回的正是这个码）—— 「刻意与 OBS 区分」根本没做到。
    /// 现在用负数：OBS 从不返回负码，因此 <c>code &lt; 0</c> 就能可靠地区分「本地拦截」与「服务端拒绝」。
    /// </summary>
    public const int ClientValidationFailed = -1;

    /// <summary>该返回码是否来自本地校验（请求未发出）。</summary>
    public static bool IsLocalValidation(int code) => code < 0;
}

/// <summary>来源上挂的一个滤镜（V3.0 / D7）。</summary>
public sealed class ObsFilterInfo
{
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "";
    public int Index { get; init; }
    public bool Enabled { get; init; }

    /// <summary>展示名：优先给已知滤镜种类一个中文名，未知则原样显示种类 id。</summary>
    public string KindLabel => Localization.DataValues.FilterKindLabel(Kind);
}

/// <summary>媒体源状态（V3.0 / D7）。</summary>
public sealed class ObsMediaStatus
{
    public string State { get; init; } = "";
    public long DurationMs { get; init; }
    public long CursorMs { get; init; }

    public bool IsPlaying => State.Contains("PLAYING", StringComparison.OrdinalIgnoreCase);
    public bool IsPaused => State.Contains("PAUSED", StringComparison.OrdinalIgnoreCase);
    public bool IsEnded => State.Contains("ENDED", StringComparison.OrdinalIgnoreCase);
    public bool IsStopped => State.Contains("STOPPED", StringComparison.OrdinalIgnoreCase) || State.Length == 0;
}

/// <summary>媒体源动作白名单（V3.0 / D7）。拼错取值时 OBS 的报错很难懂，因此本地先拦一道。</summary>
public static class ObsMediaActions
{
    public const string Play = "OBS_WEBSOCKET_MEDIA_INPUT_ACTION_PLAY";
    public const string Pause = "OBS_WEBSOCKET_MEDIA_INPUT_ACTION_PAUSE";
    public const string Restart = "OBS_WEBSOCKET_MEDIA_INPUT_ACTION_RESTART";
    public const string Stop = "OBS_WEBSOCKET_MEDIA_INPUT_ACTION_STOP";
    public const string Next = "OBS_WEBSOCKET_MEDIA_INPUT_ACTION_NEXT";
    public const string Previous = "OBS_WEBSOCKET_MEDIA_INPUT_ACTION_PREVIOUS";

    public static readonly string[] All = { Play, Pause, Restart, Stop, Next, Previous };

    public static bool IsValid(string? action) => action is not null && All.Contains(action);
}

/// <summary>音频监听类型（V3.0 / D7）。</summary>
public static class ObsMonitorTypes
{
    public const string None = "OBS_MONITORING_TYPE_NONE";
    public const string MonitorOnly = "OBS_MONITORING_TYPE_MONITOR_ONLY";
    public const string MonitorAndOutput = "OBS_MONITORING_TYPE_MONITOR_AND_OUTPUT";

    public static readonly string[] All = { None, MonitorOnly, MonitorAndOutput };

    /// <summary>
    /// 把常见写法归一化为官方取值；认不出来返回 null（调用方据此直接报错，不发无效请求）。
    /// 接受简写（<c>none</c> / <c>monitorOnly</c> / <c>monitorAndOutput</c>）与中文（<c>关闭</c>/<c>仅监听</c>/<c>监听并输出</c>）。
    /// </summary>
    public static string? Normalize(string? monitorType)
    {
        if (string.IsNullOrWhiteSpace(monitorType)) return null;
        var s = monitorType.Trim();

        foreach (var t in All)
        {
            if (string.Equals(t, s, StringComparison.OrdinalIgnoreCase)) return t;
        }

        var key = s.Replace("_", "").Replace("-", "").Replace(" ", "").ToLowerInvariant();
        return key switch
        {
            "none" or "off" or "关闭" or "关" => None,
            "monitoronly" or "only" or "仅监听" or "只听" => MonitorOnly,
            "monitorandoutput" or "both" or "监听并输出" or "同时输出" => MonitorAndOutput,
            _ => null
        };
    }
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
    /// <summary>OBS 自报的时长（<c>HH:MM:SS.mmm</c>）。录制暂停期间 OBS 不再累加，因此比本地计时更准。</summary>
    public string Timecode { get; set; } = "00:00:00.000";
    public long Bytes { get; set; }
    /// <summary>推流拥塞度 0~1，越高说明上行越吃紧（仅推流有效）。</summary>
    public double Congestion { get; set; }
    public long SkippedFrames { get; set; }
    public long TotalFrames { get; set; }

    /// <summary>
    /// OBS 自报的实时码率（kbps）；拿不到时为 0（V3.0 / D2）。
    ///
    /// 只有旧协议（obs-websocket 4.x 的 <c>kbits-per-sec</c>）会填它：v5 的 <c>GetStreamStatus</c>
    /// 不给实时码率，只能由「已发送字节 ÷ 已播秒数」估算。界面优先用自报值。
    /// </summary>
    public double Kbps { get; set; }

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
