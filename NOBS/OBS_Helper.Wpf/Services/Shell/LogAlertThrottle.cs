namespace OBS_Helper.Wpf.Services.Shell;

/// <summary>
/// 实时日志告警节流器（纯逻辑，供单元测试）。GAP-4。
///
/// 两条抑制规则：
/// ① 同一规则码在 <see cref="SuppressionWindow"/> 内只提醒一次（掉帧日志往往连续几十行）；
/// ② 每小时全局上限 <see cref="MaxPerHour"/> 条，防止异常刷屏把托盘通知打爆。
/// </summary>
public sealed class LogAlertThrottle
{
    /// <summary>同类告警的抑制窗口。</summary>
    public static readonly TimeSpan SuppressionWindow = TimeSpan.FromSeconds(90);

    /// <summary>每小时最多弹出的告警条数。</summary>
    public const int MaxPerHour = 12;

    private readonly Dictionary<string, DateTime> _lastByCode = new(StringComparer.Ordinal);
    private readonly Queue<DateTime> _hourWindow = new();

    /// <summary>判定是否允许弹出该规则的告警。<paramref name="nowUtc"/> 由调用方注入便于测试。</summary>
    public bool ShouldNotify(string code, DateTime nowUtc)
    {
        if (_lastByCode.TryGetValue(code, out var last) && nowUtc - last < SuppressionWindow)
        {
            return false;
        }

        // 全局限流：滑动一小时窗口
        while (_hourWindow.Count > 0 && nowUtc - _hourWindow.Peek() >= TimeSpan.FromHours(1))
        {
            _hourWindow.Dequeue();
        }
        if (_hourWindow.Count >= MaxPerHour)
        {
            return false;
        }

        _lastByCode[code] = nowUtc;
        _hourWindow.Enqueue(nowUtc);
        return true;
    }

    /// <summary>清空状态（新日志会话开始时调用）。</summary>
    public void Reset()
    {
        _lastByCode.Clear();
        _hourWindow.Clear();
    }
}
