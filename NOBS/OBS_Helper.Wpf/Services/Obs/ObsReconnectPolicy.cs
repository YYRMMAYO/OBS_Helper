namespace OBS_Helper.Wpf.Services.Obs;

/// <summary>
/// 断线重连的指数退避策略。纯函数式，便于单元测试。
/// </summary>
public sealed class ObsReconnectPolicy
{
    /// <summary>首次重连前的等待时间。</summary>
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>退避上限，避免长时间不可用时把间隔拉到分钟级。</summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>最大重连次数；超过后进入 Failed 状态，等待用户手动重连。</summary>
    public int MaxAttempts { get; init; } = 8;

    /// <summary>第 <paramref name="attempt"/> 次重连（从 1 开始）之前应等待的时长。</summary>
    public TimeSpan DelayFor(int attempt)
    {
        if (attempt <= 1) return BaseDelay;

        // 2^(attempt-1) 倍基准值，用 double 计算避免长时间运行时的整数溢出
        var factor = Math.Pow(2, Math.Min(attempt - 1, 16));
        var ms = BaseDelay.TotalMilliseconds * factor;
        return ms >= MaxDelay.TotalMilliseconds ? MaxDelay : TimeSpan.FromMilliseconds(ms);
    }

    /// <summary>是否还应继续尝试重连。</summary>
    public bool ShouldRetry(int attempt) => attempt <= MaxAttempts;
}
