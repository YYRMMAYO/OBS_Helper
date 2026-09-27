using OBS_Helper.Wpf.Localization;
namespace OBS_Helper.Wpf.Services.Shell;

/// <summary>录制守护的一次告警判定结论。</summary>
public sealed class WatchdogDecision
{
    /// <summary>true 表示需要向用户发出强提醒。</summary>
    public bool Alert { get; init; }

    /// <summary>告警类型；Alert 为 false 时无意义。</summary>
    public WatchdogAlertKind Kind { get; init; }

    /// <summary>通知标题。</summary>
    public string Title { get; init; } = "";

    /// <summary>通知正文（含建议动作）。</summary>
    public string Message { get; init; } = "";
}

/// <summary>录制守护的告警类型。</summary>
public enum WatchdogAlertKind
{
    /// <summary>不告警。</summary>
    None = 0,

    /// <summary>OBS 连接断开时正处于录制状态：录制可能已中断而用户在游戏中毫无察觉。</summary>
    ConnectionLostWhileRecording,

    /// <summary>录制中连续多次心跳查询失败：OBS 可能已卡死 / 假死。</summary>
    HeartbeatTimeout,

    /// <summary>断线重连成功后，发现录制已经不在进行：确认丢失并引导一键重启录制。</summary>
    RecordingLostAfterReconnect
}

/// <summary>
/// 录制守护核心（纯逻辑，供单元测试）。
///
/// 背景：OBS 故障的本质是「静默失败」——全屏游戏里录制中断 / 卡死，用户毫不知情，
/// 直到录完才发现空文件。守护监控三层信号：
/// ① WebSocket 断连且当时正在录制；
/// ② 录制中连续 N 次心跳（GetRecordStatus）失败；
/// ③ 断线重连成功后发现录制已停止。
/// </summary>
public static class RecordWatchdogCore
{
    /// <summary>心跳失败次数达到该值判定为超时（服务侧每 2s 轮询一次，约 6 秒内告警）。</summary>
    public const int HeartbeatFailureThreshold = 3;

    /// <param name="recordingActive">当前是否处于录制状态（本工具视角的「应然」状态）。</param>
    /// <param name="connected">WebSocket 当前是否已连接。</param>
    /// <param name="reconnectedNow">本次事件是否为「重连成功」这一跳变（Reconnecting → Connected）。</param>
    /// <param name="recordActiveNow">重连成功后从 OBS 查到的实际录制状态；非重连场景可传 true。</param>
    /// <param name="heartbeatFailures">连续心跳失败次数（连接正常时才累计）。</param>
    public static WatchdogDecision Evaluate(
        bool recordingActive,
        bool connected,
        bool reconnectedNow,
        bool recordActiveNow,
        int heartbeatFailures)
    {
        // ③ 重连成功的瞬间：确认录制是否还在。优先于其他信号（此刻心跳必然还没跑起来）。
        if (recordingActive && reconnectedNow)
        {
            return recordActiveNow
                ? NoAlert()
                : new WatchdogDecision
                {
                    Alert = true,
                    Kind = WatchdogAlertKind.RecordingLostAfterReconnect,
                    Title = Strings.T("watchdog.reconnect.title"),
                    Message = Strings.T("watchdog.reconnect.message")
                };
        }

        // ① 连接断开且正在录制：立刻提醒（自动重连会尝试恢复，但用户应当知情）。
        if (recordingActive && !connected)
        {
            return new WatchdogDecision
            {
                Alert = true,
                Kind = WatchdogAlertKind.ConnectionLostWhileRecording,
                Title = Strings.T("watchdog.disconnect.title"),
                Message = Strings.T("watchdog.disconnect.message"),
            };
        }

        // ② 心跳超时：连接还在但录制状态查不到，OBS 大概率假死 / 卡在 Stopping。
        if (recordingActive && connected && heartbeatFailures >= HeartbeatFailureThreshold)
        {
            return new WatchdogDecision
            {
                Alert = true,
                Kind = WatchdogAlertKind.HeartbeatTimeout,
                Title = Strings.T("watchdog.hang.title"),
                Message = Strings.T("watchdog.hang.message", heartbeatFailures)
            };
        }

        return NoAlert();
    }

    private static WatchdogDecision NoAlert() => new() { Alert = false, Kind = WatchdogAlertKind.None };
}
