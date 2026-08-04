using OBS_Helper.Client.Models.Obs;
using OBS_Helper.Client.Services.Host;
using OBS_Helper.Client.Services.Obs;

namespace OBS_Helper.Client.Services.Ai;

/// <summary>
/// 诊断编排器（技术计划 §4.5 总入口）。
///
/// 对 UI 只暴露一个 <see cref="DiagnoseAsync"/>，内部按 <see cref="AiSettingsService.Mode"/>
/// 在「本地离线引擎」与「云端大模型」之间切换：
/// <list type="bullet">
///   <item>默认本地：无需联网、不依赖密钥，零成本；</item>
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
    private readonly SystemHealthService _system;
    private readonly ObsConfigScanner _config;

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
        SystemHealthService system,
        ObsConfigScanner config)
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
        _system = system;
        _config = config;
    }

    /// <summary>最近一次日志分析报告；由日志分析页写入。</summary>
    public ObsLogReport? LatestReport { get; set; }

    /// <summary>最近一次系统体检结果，供诊断页直接展示（避免重复扫描）。</summary>
    public SystemHealthReport? LatestSystem { get; private set; }

    /// <summary>最近一次配置体检结果。</summary>
    public ObsConfigReport? LatestConfig { get; private set; }

    public DiagnosticEngineMode CurrentMode => _ai.Mode;

    public bool CanUseCloud => _ai.IsCloudConfigured && _host.IsAvailable;

    /// <summary>
    /// 采集系统 + 配置体检。
    ///
    /// 单独暴露出来是因为诊断页需要在「用户还没提问」时就把这两块展示出来——
    /// 打开页面就能看到「你的机器现在什么状态」，比等用户描述问题再分析要主动得多。
    /// 两者互相独立，任一失败都不影响另一个。
    /// </summary>
    public async Task<(SystemHealthReport System, ObsConfigReport Config)> ScanEnvironmentAsync(bool allowNetwork = true)
    {
        SystemHealthReport sys;
        try { sys = await _system.CheckAsync(allowNetwork); }
        catch { sys = new SystemHealthReport(); }

        ObsConfigReport cfg;
        try { cfg = await _config.ScanAsync(); }
        catch { cfg = new ObsConfigReport(); }

        LatestSystem = sys;
        LatestConfig = cfg;
        return (sys, cfg);
    }

    /// <summary>执行一次诊断。本地引擎始终可用；云端仅在配置完整且有宿主时启用，否则回退。</summary>
    public async Task<DiagnosticResult> DiagnoseAsync(string? query = null)
    {
        // 环境体检结果如果还没采集过就现采一次；已有则直接复用，避免每轮对话都去读注册表。
        if (LatestSystem is null || LatestConfig is null)
        {
            await ScanEnvironmentAsync();
        }

        var ctx = new DiagnosticContext(
            _conn, _analyzer, _problems, _assistant, _host, LatestReport, LatestSystem, LatestConfig);

        if (_ai.Mode == DiagnosticEngineMode.Cloud && _ai.IsCloudConfigured && _host.IsAvailable)
        {
            var cloud = await _cloud.DiagnoseAsync(ctx, query);
            if (cloud.Success) return cloud;

            // 云端失败：回退本地，并在结果上保留云端失败原因，方便 UI 提示。
            var fallback = await _local.DiagnoseAsync(ctx, query);
            fallback.FellBackToLocal = true;
            fallback.Error = cloud.Error;
            return fallback;
        }

        return await _local.DiagnoseAsync(ctx, query);
    }
}
