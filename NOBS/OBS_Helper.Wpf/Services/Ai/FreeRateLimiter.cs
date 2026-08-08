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
/// 免费内置 AI 的「本地端强制限额」：按通道分别统计——智谱通道每台机器每天最多
/// <see cref="ZhipuMaxPerDay"/> 次、Pollinations 通道每天最多 <see cref="PollinationsMaxPerDay"/> 次，
/// 且任意两次免费请求之间至少间隔 <see cref="MinIntervalSeconds"/> 秒（低频保护，防突发连打，两通道共用）。
///
/// 设计要点：
/// <list type="bullet">
///   <item>日计数落在 %LocalAppData%\OBS_Helper\prefs.json（<see cref="LocalStore"/>），跨会话、跨重启生效；
///         免费端点按 IP 也有自己的限流，本地这一层负责把单机用量压到「低频」档位，避免一个用户拖垮共享服务；
///         两通道各自独立计数（<see cref="StorageKeyFor"/>），互不挤占额度；</item>
///   <item>按本地日期（yyyyMMdd）统计，跨天自动清零恢复；间隔保护在内存里（重启即重置），两者互补；
///         日期基于 <see cref="DateTime.Now"/>：本限制是非机密的「荣誉制」防线（prefs.json 可手改），
///         不追求对抗时钟回拨 / 篡改，只负责把正常用户压到低频档；</item>
///   <item><see cref="TryConsumeAsync"/> 在发出请求前调用——每次发起免费 AI 请求计 1 次（失败重试也计数），
///         线程安全，同一时刻的并发诊断也只会放行限额内的请求。</item>
/// </list>
/// </summary>
public sealed class FreeRateLimiter
{
    /// <summary>智谱通道每日免费请求上限（强限制档：智谱是国内共享免费端点，压到最低频，避免单用户拖垮）。</summary>
    public const int ZhipuMaxPerDay = 10;

    /// <summary>Pollinations（国外免 Key）通道每日免费请求上限（维持原低频档不变）。</summary>
    public const int PollinationsMaxPerDay = 20;

    /// <summary>两次免费请求之间的最小间隔（秒），突发连打会触发本地低频保护（两通道共用）。</summary>
    public const int MinIntervalSeconds = 10;

    private const string StorageKeyPrefix = "obshelper.ai.freequota";

    private readonly LocalStore _store;
    private readonly object _gate = new();
    private DateTime _lastRequestUtc = DateTime.MinValue;

    public FreeRateLimiter(LocalStore store)
    {
        _store = store;
    }

    /// <summary>某通道的每日上限。</summary>
    public static int MaxPerDay(FreeAiProvider provider)
        => provider == FreeAiProvider.Pollinations ? PollinationsMaxPerDay : ZhipuMaxPerDay;

    private static string StorageKeyFor(FreeAiProvider provider)
        => provider == FreeAiProvider.Pollinations
            ? StorageKeyPrefix + ".pollinations"
            : StorageKeyPrefix + ".zhipu";

    /// <summary>读取指定通道的当前限额信息（自动处理跨天清零，不消耗额度）。</summary>
    public Task<FreeQuotaInfo> GetInfoAsync(FreeAiProvider provider)
    {
        lock (_gate)
        {
            var state = LoadState(provider);
            return Task.FromResult(new FreeQuotaInfo { Used = state.Used, Max = MaxPerDay(provider) });
        }
    }

    /// <summary>
    /// 尝试消耗一次指定通道的额度（含间隔保护）。返回 <see cref="FreeConsumeResult.Allowed"/> 才应发起请求。
    /// 落盘失败时 <see cref="LocalStore"/> 会静默保留内存值，本会话内限额仍然生效，不会误伤诊断。
    /// </summary>
    public Task<FreeConsumeResult> TryConsumeAsync(FreeAiProvider provider)
    {
        lock (_gate)
        {
            var state = LoadState(provider);

            // 低频保护：距上次请求不足 MinIntervalSeconds 秒直接拒绝（不扣日额度）
            var elapsed = DateTime.UtcNow - _lastRequestUtc;
            if (_lastRequestUtc != DateTime.MinValue && elapsed.TotalSeconds < MinIntervalSeconds)
                return Task.FromResult(FreeConsumeResult.TooSoon);

            if (state.Used >= MaxPerDay(provider)) return Task.FromResult(FreeConsumeResult.DailyQuotaExceeded);

            state.Used++;
            SaveState(provider, state);
            _lastRequestUtc = DateTime.UtcNow;
            return Task.FromResult(FreeConsumeResult.Allowed);
        }
    }

    private FreeQuotaState LoadState(FreeAiProvider provider)
    {
        var today = DateTime.Now.ToString("yyyyMMdd");
        var s = _store.GetObject<FreeQuotaState>(StorageKeyFor(provider));
        if (s is null || s.DateKey != today)
        {
            // 首次使用或跨天：重置为新的一天（不立刻落盘，首次消耗时才写）；
            // 同时清掉间隔保护的时间戳，避免前一天最后一笔请求压住新一天的第一笔
            _lastRequestUtc = DateTime.MinValue;
            return new FreeQuotaState { DateKey = today, Used = 0 };
        }
        return s;
    }

    private void SaveState(FreeAiProvider provider, FreeQuotaState state)
        => _store.SetObject(StorageKeyFor(provider), state);
}
