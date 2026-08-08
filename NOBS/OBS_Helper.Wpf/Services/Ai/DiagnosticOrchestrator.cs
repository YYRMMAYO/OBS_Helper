using OBS_Helper.Wpf.Models.Obs;
using OBS_Helper.Wpf.Services.Host;
using OBS_Helper.Wpf.Services.Obs;

namespace OBS_Helper.Wpf.Services.Ai;

/// <summary>
/// 诊断编排器（技术计划 §4.5 总入口）。
///
/// 对 UI 只暴露一个 <see cref="DiagnoseAsync"/>，内部按 <see cref="AiSettingsService.Mode"/>
/// 在「本地离线引擎」「免费内置 AI」「云端大模型」之间切换：
/// <list type="bullet">
///   <item>默认本地：无需联网、不依赖密钥，零成本；</item>
///   <item>免费内置 AI：无需注册与 API Key，本地端强制每日限次（<see cref="FreeRateLimiter"/>），
///         适合低频使用；超出限额或请求失败时自动回退本地并标注原因；</item>
///   <item>云端：经桌面宿主转发（密钥不进 WebAssembly），支持 function-calling 深度排查；</item>
///   <item>云端若失败（无宿主 / 未配置 / 请求异常），自动回退本地并在结果上标注，
///         保证「诊断」这一核心能力永远可用。</item>
/// </list>
///
/// <see cref="LatestReport"/> 由「日志分析」页在分析完成后写入，供引擎与工具读取。
/// </summary>
public sealed class DiagnosticOrchestrator
{
    private readonly AiSettingsService _ai;
    private readonly ObsConnectionService _conn;
    private readonly ObsLogAnalyzer _analyzer;
    private readonly ProblemService _problems;
    private readonly AssistantService _assistant;
    private readonly HostBridge _host;
    private readonly ObsToolRegistry _tools;
    private readonly LocalDiagnosticEngine _local;
    private readonly CloudDiagnosticEngine _cloud;
    private readonly FreeDiagnosticEngine _free;
    private readonly FreeRateLimiter _freeLimiter;

    public DiagnosticOrchestrator(
        AiSettingsService ai,
        ObsConnectionService conn,
        ObsLogAnalyzer analyzer,
        ProblemService problems,
        AssistantService assistant,
        HostBridge host,
        ObsToolRegistry tools,
        LocalDiagnosticEngine local,
        CloudDiagnosticEngine cloud,
        FreeDiagnosticEngine free,
        FreeRateLimiter freeLimiter)
    {
        _ai = ai;
        _conn = conn;
        _analyzer = analyzer;
        _problems = problems;
        _assistant = assistant;
        _host = host;
        _tools = tools;
        _local = local;
        _cloud = cloud;
        _free = free;
        _freeLimiter = freeLimiter;
    }

    /// <summary>最近一次日志分析报告；由日志分析页写入。</summary>
    public ObsLogReport? LatestReport { get; set; }

    public DiagnosticEngineMode CurrentMode => _ai.Mode;

    public bool CanUseCloud => _ai.IsCloudConfigured && _host.IsAvailable;

    /// <summary>免费内置 AI 是否可用（选中即用，无需任何配置）。</summary>
    public bool CanUseFree => _ai.IsFreeAvailable && _host.IsAvailable;

    /// <summary>执行一次诊断。本地引擎始终可用；免费/云端仅在可用时启用，否则回退。</summary>
    public async Task<DiagnosticResult> DiagnoseAsync(string? query = null)
    {
        var ctx = new DiagnosticContext(_conn, _analyzer, _problems, _assistant, _host, LatestReport);

        if (_ai.Mode == DiagnosticEngineMode.Free)
        {
            return await DiagnoseFreeAsync(ctx, query).ConfigureAwait(false);
        }

        if (_ai.Mode == DiagnosticEngineMode.Cloud && _ai.IsCloudConfigured && _host.IsAvailable)
        {
            var cloud = await _cloud.DiagnoseAsync(ctx, query);
            if (cloud.Success) return cloud;

            // 云端失败：回退本地，并在结果上保留云端失败原因，方便 UI 提示。
            return await Fallback(_local, ctx, query, cloud.Error).ConfigureAwait(false);
        }

        return await _local.DiagnoseAsync(ctx, query);
    }

    /// <summary>
    /// 免费 AI 路径：先强制限额（发出请求前消耗 1 次，失败重试也计数），失败或超额一律回退本地。
    /// 两通道各自独立限额（智谱 10 次/天、Pollinations 20 次/天），按当前选中通道计费。
    /// 这样即便免费端点不可用，诊断能力也不中断，只是结论来自本地引擎。
    /// </summary>
    private async Task<DiagnosticResult> DiagnoseFreeAsync(DiagnosticContext ctx, string? query)
    {
        var provider = _ai.FreeProviderMode;
        var quota = await _freeLimiter.GetInfoAsync(provider).ConfigureAwait(false);
        if (quota.Remaining <= 0)
        {
            return await Fallback(_local, ctx, query, FreeQuotaExhaustedMessage(provider)).ConfigureAwait(false);
        }

        var consume = await _freeLimiter.TryConsumeAsync(provider).ConfigureAwait(false);
        switch (consume)
        {
            case FreeConsumeResult.Allowed:
                break; // 放行，继续走免费引擎
            case FreeConsumeResult.DailyQuotaExceeded:
                return await Fallback(_local, ctx, query, FreeQuotaExhaustedMessage(provider)).ConfigureAwait(false);
            case FreeConsumeResult.TooSoon:
                return await Fallback(_local, ctx, query,
                    $"免费 AI 触发本地低频保护：两次请求之间至少间隔 {FreeRateLimiter.MinIntervalSeconds} 秒，请稍后再试；本次已改用本地的搜索助手。").ConfigureAwait(false);
            default:
                // 未来新增的枚举值：按「未放行」保守处理，避免意外直通免费端点
                return await Fallback(_local, ctx, query, "免费 AI 暂不可用，本次改用本地的搜索助手。").ConfigureAwait(false);
        }

        var free = await _free.DiagnoseAsync(ctx, query);
        if (free.Success) return free;

        return await Fallback(_local, ctx, query, free.Error).ConfigureAwait(false);
    }

    private static string FreeQuotaExhaustedMessage(FreeAiProvider provider)
        => $"今日{provider switch
        {
            FreeAiProvider.Pollinations => "Pollinations（国外免 Key）",
            _ => "智谱免费 AI",
        }}额度（{FreeRateLimiter.MaxPerDay(provider)} 次/天）已用完，本次改用本地的搜索助手；每天 0 点自动恢复，或切换到「云端大模型」使用自己的 API。";

    private async Task<DiagnosticResult> Fallback(LocalDiagnosticEngine local, DiagnosticContext ctx, string? query, string? error)
    {
        var fallback = await local.DiagnoseAsync(ctx, query);
        fallback.FellBackToLocal = true;
        fallback.Error = error;
        return fallback;
    }
}
