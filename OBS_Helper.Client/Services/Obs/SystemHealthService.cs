using OBS_Helper.Client.Services.Host;

namespace OBS_Helper.Client.Services.Obs;

/// <summary>系统体检发现的一条线索（与 <see cref="ConfigFinding"/> 同构，便于诊断页统一展示）。</summary>
public sealed class SystemFinding
{
    public string Code { get; init; } = "";
    public LogSeverity Severity { get; init; }
    public string Title { get; init; } = "";
    public string Detail { get; set; } = "";
    public string Suggestion { get; init; } = "";
    public string? ProblemId { get; init; }
}

/// <summary>一次系统体检的结果。</summary>
public sealed class SystemHealthReport
{
    /// <summary>宿主是否可用（浏览器里单独跑 WebAssembly 时为 false，此时整块降级隐藏）。</summary>
    public bool Available { get; set; }

    /// <summary>宿主上报的原始环境信息，供 UI 直接展示。</summary>
    public HostSystemInfo? Info { get; set; }

    /// <summary>联网查到的 OBS 最新版本号；离线或查询失败时为空。</summary>
    public string LatestObsVersion { get; set; } = "";

    public List<SystemFinding> Findings { get; set; } = new();

    public bool HasIssues => Findings.Count > 0;
}

/// <summary>
/// 系统体检服务（方向 A）。
///
/// 日志分析只能看到「OBS 自己愿意写下来的东西」，而真正拖垮直播的往往是 OBS 看不见的系统设置：
/// 硬件加速 GPU 调度、游戏模式、双显卡分配、录制盘快满了、OBS 没提权……
/// 这些必须由宿主进程去读注册表 / 系统接口，因此本服务只是 <c>system.info</c> 命令的
/// 客户端封装 + 规则判定层。
///
/// 设计取舍：
/// <list type="bullet">
///   <item>宿主不可用（纯浏览器调试）时返回 <c>Available = false</c>，UI 整块隐藏而不是报错；</item>
///   <item>版本比对需要联网，失败时静默跳过——「查不到最新版」不该让整个体检失败；</item>
///   <item>阈值刻意保守（磁盘 &lt; 20 GB 才告警），避免动不动就报红消耗用户信任。</item>
/// </list>
/// </summary>
public sealed class SystemHealthService
{
    /// <summary>录制盘剩余空间告警阈值（GB）。1080p60 高码率录制大约 1 GB/分钟。</summary>
    private const double DiskWarnGb = 20;
    /// <summary>录制盘剩余空间严重告警阈值（GB）。</summary>
    private const double DiskCriticalGb = 5;
    /// <summary>OBS 内存占用告警阈值（MB）。正常 1~2 GB，超过说明可能有源泄漏。</summary>
    private const double MemoryWarnMb = 4096;

    private readonly HostBridge _host;

    public SystemHealthService(HostBridge host) => _host = host;

    /// <summary>执行一次系统体检。<paramref name="allowNetwork"/> 为 false 时跳过版本查询。</summary>
    public async Task<SystemHealthReport> CheckAsync(bool allowNetwork = true)
    {
        var report = new SystemHealthReport();

        HostSystemInfo? info;
        try
        {
            info = await _host.GetSystemInfoAsync();
        }
        catch
        {
            // 宿主命令不可用（旧版桌面壳 / 浏览器直开）——降级为「没有这块信息」。
            return report;
        }

        if (info is null) return report;

        report.Available = true;
        report.Info = info;

        if (allowNetwork)
        {
            try
            {
                report.LatestObsVersion = await _host.GetObsLatestVersionAsync() ?? "";
            }
            catch
            {
                // 离线可用是硬要求：查不到最新版不影响其余体检项。
            }
        }

        Evaluate(report, info);
        return report;
    }

    // ------------------------------------------------------------------ 规则判定

    private static void Evaluate(SystemHealthReport report, HostSystemInfo info)
    {
        var f = report.Findings;

        // —— HAGS：NVENC 掉帧的头号嫌疑人 ——
        if (info.HagsEnabled)
        {
            f.Add(new SystemFinding
            {
                Code = "SYS-HAGS",
                Severity = LogSeverity.Warning,
                Title = "已开启「硬件加速 GPU 计划」（HAGS）",
                Detail = "系统正在让 GPU 自行调度任务队列。",
                Suggestion = "HAGS 会打乱 GPU 任务顺序，是 NVENC 编码掉帧、画面卡顿的常见诱因。到 设置→系统→显示→图形→更改默认图形设置 关闭它，重启后生效。",
                ProblemId = "lag-skip"
            });
        }

        // —— 游戏模式：优先级只是「可能」有影响，所以只给提示级 ——
        if (info.GameModeEnabled)
        {
            f.Add(new SystemFinding
            {
                Code = "SYS-GAMEMODE",
                Severity = LogSeverity.Info,
                Title = "已开启 Windows 游戏模式",
                Detail = "系统会优先把资源让给前台全屏游戏。",
                Suggestion = "如果出现无规律掉帧，可以关闭游戏模式做一次对比测试；没有掉帧则无需理会。"
            });
        }

        // —— 双显卡：不是「有两块卡」就报警，而是提示要确认分配 ——
        var realGpus = info.Gpus
            .Where(g => !string.IsNullOrWhiteSpace(g.Name) && !IsVirtualGpu(g.Name))
            .ToList();
        if (realGpus.Count > 1)
        {
            var names = string.Join("、", realGpus.Select(g => g.Name));
            f.Add(new SystemFinding
            {
                Code = "SYS-DUALGPU",
                Severity = LogSeverity.Info,
                Title = $"检测到 {realGpus.Count} 块显卡",
                Detail = names,
                Suggestion = "双显卡机器（尤其是笔记本）必须让 OBS 与游戏跑在同一块卡上，否则会出现捕获黑屏或额外性能损耗。到 设置→系统→显示→图形 把 OBS 指定为「高性能」。",
                ProblemId = "bs-black"
            });
        }

        // —— 录制盘余量 ——
        if (info.RecordingDiskTotalGb > 0)
        {
            if (info.RecordingDiskFreeGb < DiskCriticalGb)
            {
                f.Add(new SystemFinding
                {
                    Code = "SYS-DISK-CRIT",
                    Severity = LogSeverity.Critical,
                    Title = $"录制盘仅剩 {info.RecordingDiskFreeGb:0.#} GB",
                    Detail = $"总容量 {info.RecordingDiskTotalGb:0.#} GB。",
                    Suggestion = "按 1080p60 常用码率估算，这点空间撑不过几分钟录制，且写满后录像文件会直接损坏。请立即清理磁盘或改到其它盘录制。",
                    ProblemId = "rc-diskfull"
                });
            }
            else if (info.RecordingDiskFreeGb < DiskWarnGb)
            {
                f.Add(new SystemFinding
                {
                    Code = "SYS-DISK-LOW",
                    Severity = LogSeverity.Warning,
                    Title = $"录制盘剩余 {info.RecordingDiskFreeGb:0.#} GB",
                    Detail = $"总容量 {info.RecordingDiskTotalGb:0.#} GB。",
                    Suggestion = "长时间录制建议至少预留 50 GB。开播前先清理，避免录到一半空间耗尽。",
                    ProblemId = "rc-diskfull"
                });
            }
        }

        // —— OBS 进程相关 ——
        if (info.Obs.Running)
        {
            if (!info.Obs.Elevated)
            {
                f.Add(new SystemFinding
                {
                    Code = "SYS-OBS-NOADMIN",
                    Severity = LogSeverity.Info,
                    Title = "OBS 未以管理员身份运行",
                    Detail = "当前 OBS 进程权限为标准用户。",
                    Suggestion = "捕获以管理员权限运行的游戏（多数反作弊游戏）时会黑屏。右键 OBS 快捷方式→属性→兼容性→勾选「以管理员身份运行此程序」。",
                    ProblemId = "bs-game"
                });
            }

            if (info.Obs.MemoryMb > MemoryWarnMb)
            {
                f.Add(new SystemFinding
                {
                    Code = "SYS-OBS-MEM",
                    Severity = LogSeverity.Warning,
                    Title = $"OBS 内存占用 {info.Obs.MemoryMb:0} MB 偏高",
                    Detail = "正常情况下 OBS 占用约 1~2 GB。",
                    Suggestion = "常见原因是浏览器源过多、场景集合里堆积了大量未使用的源。清理无用源，或给浏览器源勾选「不可见时关闭」。"
                });
            }
        }
        else
        {
            f.Add(new SystemFinding
            {
                Code = "SYS-OBS-NOTRUNNING",
                Severity = LogSeverity.Info,
                Title = "未检测到 OBS 正在运行",
                Detail = "部分实时检查项需要 OBS 处于运行状态。",
                Suggestion = "启动 OBS 后重新体检，可获得更完整的结果（进程占用、WebSocket 实时数据等）。"
            });
        }

        // —— 版本比对：只在两边都拿到版本号时才比 ——
        if (!string.IsNullOrWhiteSpace(report.LatestObsVersion) &&
            !string.IsNullOrWhiteSpace(info.Obs.Version) &&
            CompareVersion(info.Obs.Version, report.LatestObsVersion) < 0)
        {
            f.Add(new SystemFinding
            {
                Code = "SYS-OBS-OUTDATED",
                Severity = LogSeverity.Info,
                Title = $"OBS 有新版本（当前 {info.Obs.Version} → 最新 {report.LatestObsVersion}）",
                Detail = "版本信息来自 OBS 官方发布接口。",
                Suggestion = "新版通常修复了大量编码器与捕获相关的问题。升级前请先备份配置目录，避免插件不兼容。",
                ProblemId = "cr-update"
            });
        }
    }

    /// <summary>虚拟/远程显示适配器不该被当成「第二块显卡」，否则会误报双显卡。</summary>
    private static bool IsVirtualGpu(string name)
    {
        string[] keywords =
        {
            "Microsoft Basic", "Remote Display", "Virtual", "Parsec", "IDD",
            "Meta Virtual", "DisplayLink", "Citrix", "VMware", "VirtualBox"
        };
        return keywords.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 语义化版本比较（只比数字段）。
    /// OBS 的版本号可能带 "-rc1"、"-beta2" 后缀，这里一律截断后比较主体数字，
    /// 宁可漏报也不要把预览版误判成「落后」。
    /// </summary>
    internal static int CompareVersion(string a, string b)
    {
        var pa = ParseParts(a);
        var pb = ParseParts(b);
        int len = Math.Max(pa.Length, pb.Length);
        for (int i = 0; i < len; i++)
        {
            int va = i < pa.Length ? pa[i] : 0;
            int vb = i < pb.Length ? pb[i] : 0;
            if (va != vb) return va.CompareTo(vb);
        }
        return 0;
    }

    private static int[] ParseParts(string version)
    {
        var body = version.Trim().TrimStart('v', 'V');
        int cut = body.IndexOfAny(new[] { '-', '+', ' ' });
        if (cut >= 0) body = body[..cut];

        return body.Split('.')
            .Select(seg => int.TryParse(seg, out var n) ? n : 0)
            .ToArray();
    }
}
