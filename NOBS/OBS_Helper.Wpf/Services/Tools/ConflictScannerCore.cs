namespace OBS_Helper.Wpf.Services.Tools;

/// <summary>一条冲突软件命中。</summary>
public sealed class ConflictHit
{
    /// <summary>命中的进程名（小写，不含 .exe）。</summary>
    public required string ProcessName { get; init; }
    /// <summary>给人看的软件名。</summary>
    public required string DisplayName { get; init; }
    /// <summary>风险等级：高 / 中 / 提示。</summary>
    public required string Risk { get; init; }
    /// <summary>为什么冲突 + 怎么处理。</summary>
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
/// </summary>
public static class ConflictScannerCore
{
    // Key 用于与进程名做「包含」匹配（小写）；一个软件可能对应多个进程名片段
    private static readonly (string[] Keys, string Display, string Risk, string Detail, string ProblemId)[] Known =
    {
        (new[] { "nahimic" }, "Nahimic 音频服务", "高",
         "向进程注入 DLL，是 OBS 官方论坛确认的头号崩溃源（微星 / 联想等主板预装）。" +
         "建议：应用列表卸载 Nahimic / A-Volute，或在服务中禁用「Nahimic service」。",
         "cr-env-interference"),
        (new[] { "a-volute", "avolute" }, "A-Volute（Nahimic 同源）", "高",
         "与 Nahimic 同源的音频注入组件，同样会导致崩溃 / 黑屏。建议一并卸载。",
         "cr-env-interference"),
        (new[] { "rtss", "rivatuner" }, "RivaTuner (RTSS)", "中",
         "游戏内覆盖层与捕获钩子冲突，可能造成黑框 / 掉帧。" +
         "建议：RTSS 设置里关闭 On-Screen Display，或把 obs64.exe 从覆盖检测中排除。",
         "cr-env-interference"),
        (new[] { "afterburner" }, "MSI AfterBurner", "中",
         "常与 RTSS 搭配运行，覆盖层冲突同上；只用于监控时关闭其 OSD 即可。",
         "cr-env-interference"),
        (new[] { "overwolf" }, "Overwolf", "中",
         "游戏内应用平台会注入图形栈，偶发捕获黑屏与掉帧。建议直播 / 录制时退出。",
         "cr-env-interference"),
        (new[] { "voicemod" }, "Voicemod", "中",
         "虚拟音频驱动注入，可能与 OBS 音频采集互抢设备。异常时先退出 Voicemod 再重选设备。",
         "au-mute"),
        (new[] { "360tray", "360safe", "360sd" }, "360 安全卫士", "提示",
         "曾出现对 OBS 安装包 / 插件 DLL 的误报拦截。若插件装不上或文件消失，检查其隔离区并加白名单。",
         "cr-antivirus"),
        (new[] { "hipsdaemon", "wsctrl" }, "火绒安全", "提示",
         "行为防御可能拦截 OBS 的脚本 / 插件加载。异常时查看拦截日志并放行 obs64.exe。",
         "cr-antivirus"),
        (new[] { "qqpctray" }, "腾讯电脑管家", "提示",
         "误报拦截记录偶见。安装插件失败时检查其信任区设置。",
         "cr-antivirus"),
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

        foreach (var (keys, display, risk, detail, problemId) in Known)
        {
            var matched = names.Where(n => keys.Any(k => n.Contains(k))).ToList();
            if (matched.Count == 0) continue;

            hits.Add(new ConflictHit
            {
                ProcessName = string.Join(", ", matched),
                DisplayName = display,
                Risk = risk,
                Advice = detail,
                ProblemId = problemId
            });
        }

        return hits
            .OrderBy(h => h.Risk == "高" ? 0 : h.Risk == "中" ? 1 : 2)
            .ThenBy(h => h.DisplayName)
            .ToList();
    }
}
