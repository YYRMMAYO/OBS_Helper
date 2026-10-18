using System.Net.Sockets;
using System.Net.WebSockets;

namespace OBS_Helper.Wpf.Services.Obs;

/// <summary>主动建立连接的结果分类（V3.1.0 / D3）。</summary>
public enum EnsureConnectionOutcome
{
    AlreadyConnected = 0,
    Connected,
    ObsNotRunning,
    ObsExited,
    PortClosed,
    HandshakeFailed,
    AuthRequired,
    AuthFailed,
    Timeout,
    Cancelled,
    Error
}

/// <summary>一次 <c>EnsureConnectedAsync</c> 的结果。</summary>
public sealed record EnsureConnectionResult(
    EnsureConnectionOutcome Outcome,
    string? Message,
    int Attempts,
    TimeSpan Elapsed)
{
    public bool Ok => Outcome is EnsureConnectionOutcome.AlreadyConnected or EnsureConnectionOutcome.Connected;
}

/// <summary>
/// 连接错误分类与重试策略（V3.1.0 / D3、§4.7）。
///
/// 纯逻辑：把底层异常翻译成用户能照做的分类，决定「还能不能再试」。
/// 不弹窗、不写日志，界面与服务层据此给出不同出口。
/// </summary>
public static class ObsEnsureCore
{
    /// <summary>
    /// 异常 → 结果分类。
    /// <paramref name="authRequired"/> 来自服务端 Hello；<paramref name="passwordProvided"/> 是本次是否带了密码。
    /// </summary>
    public static EnsureConnectionOutcome Classify(
        Exception? exception, bool authRequired, bool passwordProvided)
    {
        if (exception is null) return EnsureConnectionOutcome.Error;

        if (exception is UnauthorizedAccessException)
            return authRequired && !passwordProvided
                ? EnsureConnectionOutcome.AuthRequired
                : EnsureConnectionOutcome.AuthFailed;

        if (exception is OperationCanceledException) return EnsureConnectionOutcome.Cancelled;
        if (exception is TimeoutException) return EnsureConnectionOutcome.Timeout;

        // 端口没开 / 被拒绝：SocketException 可能被包在 WebSocketException 里
        for (var e = exception; e is not null; e = e.InnerException)
        {
            if (e is SocketException) return EnsureConnectionOutcome.PortClosed;
        }

        if (exception is WebSocketException) return EnsureConnectionOutcome.HandshakeFailed;

        return EnsureConnectionOutcome.Error;
    }

    /// <summary>
    /// 这次失败是否值得在超时窗口内继续重试。
    /// 认证失败 / OBS 退出不重试：前者重试只会反复弹错，后者再等也没有意义。
    /// </summary>
    public static bool ShouldRetry(EnsureConnectionOutcome outcome) => outcome is
        EnsureConnectionOutcome.PortClosed or
        EnsureConnectionOutcome.HandshakeFailed or
        EnsureConnectionOutcome.Error;

    /// <summary>失败是否属于「需要用户输入 / 修改设置」而不是「再等等」。</summary>
    public static bool NeedsUserAction(EnsureConnectionOutcome outcome) => outcome is
        EnsureConnectionOutcome.AuthRequired or
        EnsureConnectionOutcome.AuthFailed or
        EnsureConnectionOutcome.ObsNotRunning or
        EnsureConnectionOutcome.ObsExited;

    /// <summary>把结果映射成 <c>simple.error.*</c> 文案键（缺键时由调用方兜底）。</summary>
    public static string ErrorKey(EnsureConnectionOutcome outcome) => outcome switch
    {
        EnsureConnectionOutcome.ObsNotRunning => "simple.error.obsNotRunning",
        EnsureConnectionOutcome.ObsExited => "simple.error.obsExited",
        EnsureConnectionOutcome.PortClosed => "simple.error.portClosed",
        EnsureConnectionOutcome.HandshakeFailed => "simple.error.handshakeFailed",
        EnsureConnectionOutcome.AuthRequired => "simple.error.authRequired",
        EnsureConnectionOutcome.AuthFailed => "simple.error.authFailed",
        EnsureConnectionOutcome.Timeout => "simple.error.timeout",
        EnsureConnectionOutcome.Cancelled => "simple.error.cancelled",
        _ => "simple.error.connectFailed"
    };
}
