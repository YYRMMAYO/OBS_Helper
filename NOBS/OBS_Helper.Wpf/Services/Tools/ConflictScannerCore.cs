using OBS_Helper.Wpf.Localization;
namespace OBS_Helper.Wpf.Services.Tools;

/// <summary>一条冲突软件命中。</summary>
public sealed class ConflictHit
{
    /// <summary>命中的进程名（小写，不含 .exe）。</summary>
    public required string ProcessName { get; init; }
    /// <summary>给人看的软件名（已按当前语言本地化）。</summary>
    public required string DisplayName { get; init; }
    /// <summary>风险等级的展示文案（高 / 中 / 提示，已本地化）。</summary>
    public required string Risk { get; init; }
    /// <summary>
    /// 风险等级的逻辑标识：<c>high</c> / <c>medium</c> / <c>info</c>。
    ///
    /// 与 <see cref="Risk"/> 分开（V2.9.2）：展示文案要跟着语言走，而排序与卡片配色必须与语言无关 ——
    /// 否则中英切换后 XAML 里的 <c>DataTrigger</c> 会全部落空、风险色塌成默认值。
    /// </summary>
    public required string RiskLevel { get; init; }
    /// <summary>为什么冲突 + 怎么处理（已本地化）。</summary>
    public required string Advice { get; init; }
    /// <summary>关联知识库条目 id。</summary>
    public string? ProblemId { get; init; }
}

/// <summary>
/// 冲突软件识别（纯逻辑，进程名列表注入，供单元测试）。
///
/// 已知会对 OBS 注入 DLL / 挂钩图形音频栈、或拦截其文件的软件清单。
/// 依据 OBS 官方论坛长期反馈整理（Nahimic 为头号崩溃源，RTSS 覆盖层次之）。
/// 只做提示，不代用户结束任何进程。
///
/// 文案（软件名 / 处置建议）走文案表，键在 <c>toolbox.conflict.*</c>：
/// 清单本身与语言无关，因此新增语言不必再改这张表。
/// </summary>
public static class ConflictScannerCore
{
    /// <summary>风险等级：高。</summary>
    public const string RiskHigh = "high";
    /// <summary>风险等级：中。</summary>
    public const string RiskMedium = "medium";
    /// <summary>风险等级：提示。</summary>
    public const string RiskInfo = "info";

    // 第一列用于与进程名做「包含」匹配（小写）；一个软件可能对应多个进程名片段
    private static readonly (string[] Keys, string NameKey, string RiskLevel, string AdviceKey, string ProblemId)[] Known =
    {
        (new[] { "nahimic" }, "toolbox.conflict.nahimic.name", RiskHigh,
         "toolbox.conflict.nahimic.advice", "cr-env-interference"),
        (new[] { "a-volute", "avolute" }, "toolbox.conflict.avolute.name", RiskHigh,
         "toolbox.conflict.avolute.advice", "cr-env-interference"),
        (new[] { "rtss", "rivatuner" }, "toolbox.conflict.rtss.name", RiskMedium,
         "toolbox.conflict.rtss.advice", "cr-env-interference"),
        (new[] { "afterburner" }, "toolbox.conflict.afterburner.name", RiskMedium,
         "toolbox.conflict.afterburner.advice", "cr-env-interference"),
        (new[] { "overwolf" }, "toolbox.conflict.overwolf.name", RiskMedium,
         "toolbox.conflict.overwolf.advice", "cr-env-interference"),
        (new[] { "voicemod" }, "toolbox.conflict.voicemod.name", RiskMedium,
         "toolbox.conflict.voicemod.advice", "au-mute"),
        (new[] { "360tray", "360safe", "360sd" }, "toolbox.conflict.360.name", RiskInfo,
         "toolbox.conflict.360.advice", "cr-antivirus"),
        (new[] { "hipsdaemon", "wsctrl" }, "toolbox.conflict.huorong.name", RiskInfo,
         "toolbox.conflict.huorong.advice", "cr-antivirus"),
        (new[] { "qqpctray" }, "toolbox.conflict.qqpc.name", RiskInfo,
         "toolbox.conflict.qqpc.advice", "cr-antivirus"),
    };

    /// <summary>
    /// 对给定进程名集合做匹配。<paramref name="processNames"/> 应为不含扩展名的进程名（大小写不限）。
    /// </summary>
    public static List<ConflictHit> Scan(IEnumerable<string> processNames)
    {
        var hits = new List<ConflictHit>();
        if (processNames is null) return hits;

        var names = processNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();

        foreach (var (keys, nameKey, riskLevel, adviceKey, problemId) in Known)
        {
            var matched = names.Where(n => keys.Any(k => n.Contains(k))).ToList();
            if (matched.Count == 0) continue;

            hits.Add(new ConflictHit
            {
                ProcessName = string.Join(", ", matched),
                DisplayName = Localization.Strings.T(nameKey),
                Risk = Localization.Strings.T(RiskLabelKey(riskLevel)),
                RiskLevel = riskLevel,
                Advice = Localization.Strings.T(adviceKey),
                ProblemId = problemId
            });
        }

        return hits
            .OrderBy(h => Rank(h.RiskLevel))
            .ThenBy(h => h.DisplayName)
            .ToList();
    }

    /// <summary>风险等级 → 展示文案键。</summary>
    private static string RiskLabelKey(string riskLevel) => riskLevel switch
    {
        RiskHigh => "toolbox.risk.high",
        RiskMedium => "toolbox.risk.medium",
        _ => "toolbox.risk.info"
    };

    private static int Rank(string riskLevel) => riskLevel switch
    {
        RiskHigh => 0,
        RiskMedium => 1,
        _ => 2
    };
}
