using OBS_Helper.Wpf.Localization;

namespace OBS_Helper.Wpf.Services.ObsConfig;

/// <summary>简单开播的界面状态。</summary>
public enum SimpleStreamStep
{
    /// <summary>空闲（还没检查过 / 已停止）。</summary>
    Idle,
    /// <summary>正在检查能不能播。</summary>
    Checking,
    /// <summary>可以播，等用户点开始。</summary>
    Ready,
    /// <summary>正在开始。</summary>
    Starting,
    /// <summary>正在推流。</summary>
    Streaming,
    /// <summary>正在停止。</summary>
    Stopping,
    /// <summary>已停止（本次会话播过）。</summary>
    Stopped,
    /// <summary>不可播（原因见 StatusText）。</summary>
    Blocked
}

/// <summary>
/// 推流服务配置的**存在性**摘要（不含任何密钥内容）。
///
/// 刻意的设计：只带「有没有填服务器 / 有没有填密钥」两个布尔，**不携带密钥本身** ——
/// 于是密钥不会进入本应用的任何数据模型、日志或界面状态，也就没有泄漏面。
/// </summary>
public sealed record SimpleStreamServiceInfo(bool HasServer, bool HasKey, string ServiceType = "")
{
    public static readonly SimpleStreamServiceInfo Unknown = new(false, false, "");
}

/// <summary>「现在能不能播」的结论。</summary>
public sealed record SimpleStreamCheck(
    bool Ready,
    /// <summary>不能播的原因（可播时为 null）。</summary>
    string? BlockReason,
    /// <summary>能播但要先知道的提醒（可播且无风险时为 null）。</summary>
    string? Warning);

/// <summary>
/// 「简单开播」的核心逻辑（V3.0 / D2）。纯 BCL、零 WPF 依赖，单测工程直接链接编译。
///
/// 与「简单录像」同一套取舍：**只做用户真正会卡住的那几个判断**，不替用户决定编码与码率
/// （推流参数与平台强相关，猜错比不猜更糟）。因此这里只回答三件事：
/// <list type="number">
///   <item>连上 OBS 了吗；</item>
///   <item>OBS 里的推流服务填了吗（服务器 + 串流密钥）—— 这是「点了没反应」的第一大真原因；</item>
///   <item>现在有没有风险（已在下行丢帧 / 拥塞、或正在同时录制）。</item>
/// </list>
/// </summary>
public static class SimpleStreamingCore
{
    /// <summary>丢帧率超过这个比例就提醒（与性能页的「警告」阈值一致）。</summary>
    public const double DroppedWarnRatio = 0.05;

    /// <summary>拥塞比例超过这个值就提醒。</summary>
    public const double CongestionWarnRatio = 0.3;

    /// <summary>
    /// 判定现在能不能开播。
    ///
    /// 判定顺序即用户的心智顺序：已经播了 → 没连上 → 没填服务 → 没填密钥；
    /// 这些都不满足时才谈「能播，但要注意什么」。
    /// </summary>
    public static SimpleStreamCheck Check(
        bool connected,
        bool streaming,
        bool recording,
        SimpleStreamServiceInfo? service,
        double congestion = 0,
        double droppedRatio = 0)
    {
        if (streaming)
            return new SimpleStreamCheck(false, Strings.T("simple.stream.blocked.already"), null);

        if (!connected)
            return new SimpleStreamCheck(false, Strings.T("simple.stream.blocked.notConnected"), null);

        if (service is null)
        {
            // 连上了但读不到推流设置（旧协议 / 权限）：**不阻断**，只提醒。
            //
            // 为什么不当硬阻断：D2 的红线是「只读取 + 校验 + 提示 + 调 StartStream」，
            // 而「读不到」并不等于「没配置」—— 真有问题时 OBS 自己会给出准确错误。
            // 把它当阻断，反而会让 Win7 旧协议用户完全无法开播（审查指出这条越界了）。
            return new SimpleStreamCheck(true, null, Strings.T("simple.stream.warn.unknownService"));
        }

        if (!service.HasServer)
            return new SimpleStreamCheck(false, Strings.T("simple.stream.blocked.noServer"), null);

        if (!service.HasKey)
            return new SimpleStreamCheck(false, Strings.T("simple.stream.blocked.noKey"), null);

        // 能播了：下面只给提醒，不阻断
        if (congestion > CongestionWarnRatio)
            return new SimpleStreamCheck(true, null, Strings.T("simple.stream.warn.congestion", (congestion * 100).ToString("0")));

        if (droppedRatio > DroppedWarnRatio)
            return new SimpleStreamCheck(true, null, Strings.T("simple.stream.warn.dropped", (droppedRatio * 100).ToString("0.#")));

        if (recording)
            return new SimpleStreamCheck(true, null, Strings.T("simple.stream.warn.recording"));

        return new SimpleStreamCheck(true, null, null);
    }

    /// <summary>状态 → 按钮文案的语义（true = 当前应显示「停止推流」）。</summary>
    public static bool IsStopAction(SimpleStreamStep step)
        => step is SimpleStreamStep.Streaming or SimpleStreamStep.Starting or SimpleStreamStep.Stopping;

    /// <summary>
    /// 推流停止（或已不在推流）之后该进入哪个状态。
    ///
    /// 抽成纯函数是因为审查发现的状态机缺陷都在这里：停播请求失败 / 超时并不代表**仍然在推流**，
    /// 唯一可信的判据是 OBS 自报的 <paramref name="actuallyStreaming"/>。
    /// </summary>
    public static SimpleStreamStep StepAfterStopAttempt(bool actuallyStreaming)
        => actuallyStreaming ? SimpleStreamStep.Streaming : SimpleStreamStep.Stopped;

    /// <summary>
    /// 收到「推流已停止」这类外部变化时，是否应当同步本服务状态。
    ///
    /// 关键在于不能只认 <see cref="SimpleStreamStep.Streaming"/>：用户点停止后状态会先进入
    /// <see cref="SimpleStreamStep.Stopping"/>，此时事件到达若被忽略，就会永久卡在「停止推流」上
    /// （审查发现的不可自愈缺陷）。
    /// </summary>
    public static bool ShouldSyncExternalStop(SimpleStreamStep step)
        => step is SimpleStreamStep.Streaming or SimpleStreamStep.Starting or SimpleStreamStep.Stopping;

    /// <summary>已播时长的展示格式（复用录制那边的口径，避免两个页面各写一套）。</summary>
    public static string FormatDuration(TimeSpan t) => SimpleRecordingCore.FormatDuration(t);

    /// <summary>
    /// 推流码率的近似值（kbps）：用 OBS 自报的已发送字节 ÷ 已播秒数。
    ///
    /// 为什么需要：推流的「卡不卡」取决于实时带宽，而 OBS 只在状态里给出累计字节数；
    /// 换算成 kbps 才能让用户一眼看出「现在到底跑多少」。
    /// 样本不足（小于 <paramref name="minimumSeconds"/> 秒）时返回 0，由界面显示「—」而不是假装有个数。
    /// </summary>
    public static double EstimateKbps(long bytesSent, TimeSpan elapsed, double minimumSeconds = 3)
    {
        if (bytesSent <= 0 || elapsed.TotalSeconds < minimumSeconds) return 0;
        return bytesSent * 8.0 / 1024.0 / elapsed.TotalSeconds;
    }
}
