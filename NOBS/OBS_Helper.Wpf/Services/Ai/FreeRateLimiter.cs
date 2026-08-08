using OBS_Helper.Wpf.Services.Host;

namespace OBS_Helper.Wpf.Services.Ai;

/// <summary>免费内置 AI 的本地限额状态（持久化在 prefs.json，非机密）。</summary>
public sealed class FreeQuotaState
{
    /// <summary>统计所属的本地日期（yyyyMMdd），跨天后清零。</summary>
    public string DateKey { get; set; } = "";

    /// <summary>当天已消耗的次数。</summary>
    public int Used { get; set; }
}

/// <summary>免费内置 AI 的限额信息（供设置页展示）。</summary>
public sealed class FreeQuotaInfo
{
    public int Used { get; init; }
    public int Max { get; init; }
    public int Remaining => Math.Max(0, Max - Used);
}

/// <summary>一次消耗请求的结果。</summary>
public enum FreeConsumeResult
{
    /// <summary>放行，可发起请求。</summary>
    Allowed,
    /// <summary>今日额度已用完。</summary>
    DailyQuotaExceeded,
    /// <summary>距上次请求过近，触发低频保护。</summary>
    TooSoon,
}

/// <summary>
/// 免费内置 AI 的「本地端强制限额」：每台机器每天最多 <see cref="MaxFreePerDay"/> 次请求，
/// 且任意两次请求之间至少间隔 <see cref="MinIntervalSeconds"/> 秒（低频保护，防突发连打）。
///
/// 设计要点：
/// <list type="bullet">
///   <item>日计数落在 %LocalAppData%\OBS_Helper\prefs.json（<see cref="LocalStore"/>），跨会话、跨重启生效；
///         免费端点按 IP 也有自己的限流，本地这一层负责把单机用量压到「低频」档位，避免一个用户拖垮共享服务；</item>
///   <item>按本地日期（yyyyMMdd）统计，跨天自动清零恢复；间隔保护在内存里（重启即重置），两者互补；
///         日期基于 <see cref="DateTime.Now"/>：本限制是非机密的「荣誉制」防线（prefs.json 可手改），
///         不追求对抗时钟回拨 / 篡改，只负责把正常用户压到低频档；</item>
///   <item><see cref="TryConsumeAsync"/> 在发出请求前调用——每次发起免费 AI 请求计 1 次（失败重试也计数），
///         线程安全，同一时刻的并发诊断也只会放行限额内的请求。</item>
/// </list>
/// </summary>
public sealed class FreeRateLimiter
{
    /// <summary>每日免费请求上限（低频使用档位；免费共享端点不可承受高频，也不建议放开）。</summary>
    public const int MaxFreePerDay = 20;

    /// <summary>两次免费请求之间的最小间隔（秒），突发连打会触发本地低频保护。</summary>
    public const int MinIntervalSeconds = 10;

    private const string StorageKey = "obshelper.ai.freequota";

    private readonly LocalStore _store;
    private readonly object _gate = new();
    private DateTime _lastRequestUtc = DateTime.MinValue;

    public FreeRateLimiter(LocalStore store)
    {
        _store = store;
    }

    /// <summary>读取当前限额信息（自动处理跨天清零，不消耗额度）。</summary>
    public Task<FreeQuotaInfo> GetInfoAsync()
    {
        lock (_gate)
        {
            var state = LoadState();
            return Task.FromResult(new FreeQuotaInfo { Used = state.Used, Max = MaxFreePerDay });
        }
    }

    /// <summary>
    /// 尝试消耗一次额度（含间隔保护）。返回 <see cref="FreeConsumeResult.Allowed"/> 才应发起请求。
    /// 落盘失败时 <see cref="LocalStore"/> 会静默保留内存值，本会话内限额仍然生效，不会误伤诊断。
    /// </summary>
    public Task<FreeConsumeResult> TryConsumeAsync()
    {
        lock (_gate)
        {
            var state = LoadState();

            // 低频保护：距上次请求不足 MinIntervalSeconds 秒直接拒绝（不扣日额度）
            var elapsed = DateTime.UtcNow - _lastRequestUtc;
            if (_lastRequestUtc != DateTime.MinValue && elapsed.TotalSeconds < MinIntervalSeconds)
                return Task.FromResult(FreeConsumeResult.TooSoon);

            if (state.Used >= MaxFreePerDay) return Task.FromResult(FreeConsumeResult.DailyQuotaExceeded);

            state.Used++;
            SaveState(state);
            _lastRequestUtc = DateTime.UtcNow;
            return Task.FromResult(FreeConsumeResult.Allowed);
        }
    }

    private FreeQuotaState LoadState()
    {
        var today = DateTime.Now.ToString("yyyyMMdd");
        var s = _store.GetObject<FreeQuotaState>(StorageKey);
        if (s is null || s.DateKey != today)
        {
            // 首次使用或跨天：重置为新的一天（不立刻落盘，首次消耗时才写）；
            // 同时清掉间隔保护的时间戳，避免前一天最后一笔请求压住新一天的第一笔
            _lastRequestUtc = DateTime.MinValue;
            return new FreeQuotaState { DateKey = today, Used = 0 };
        }
        return s;
    }

    private void SaveState(FreeQuotaState state)
        => _store.SetObject(StorageKey, state);
}
