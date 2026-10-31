namespace OBS_Helper.Wpf.Services.Obs;

/// <summary>
/// 输出状态（录制 / 推流）对账规则：**事件是权威来源，查询只做补充**。
///
/// 背景（V3.5.2 实测复现）：OBS 在 <c>RecordStateChanged</c> 已经发出
/// <c>OBS_WEBSOCKET_OUTPUT_STOPPING</c> / <c>STOPPED</c>（<c>outputActive=false</c>）之后，
/// 仍会在一小段时间内让 <c>GetRecordStatus</c> 返回 <c>outputActive=true</c>（输出收尾阶段）。
/// 旧实现把这次查询结果直接写回 <c>Active</c>，于是「停止录制」后界面又翻回「进行中」，
/// 而此后不会再有事件来纠正 —— 桌面端与手机端都会一直显示在录制。
///
/// 规则：查询结果与最近一次事件冲突时，在 <see cref="EventAuthorityWindow"/> 内以事件为准；
/// 事件已过期（可能丢失）时才采信查询，保证漏事件也能在下一轮对账自愈。
/// </summary>
public static class ObsOutputStateCore
{
    /// <summary>事件之后、查询结果冲突时仍信任事件的时间窗口。</summary>
    public static readonly TimeSpan EventAuthorityWindow = TimeSpan.FromSeconds(3);

    /// <summary>
    /// 解析「当前是否真的在运行」。
    /// </summary>
    /// <param name="queryActive"><c>GetRecordStatus</c> / <c>GetStreamStatus</c> 的 <c>outputActive</c>。</param>
    /// <param name="eventActive">最近一次状态事件给出的 <c>outputActive</c>。</param>
    /// <param name="sinceEvent">距最近一次状态事件的时间；从未收到事件时传 <see cref="TimeSpan.MaxValue"/>。</param>
    /// <param name="window">信任事件的时间窗口。</param>
    public static bool ResolveActive(bool queryActive, bool eventActive, TimeSpan sinceEvent, TimeSpan window)
        => queryActive == eventActive || sinceEvent > window ? queryActive : eventActive;

    /// <summary>使用默认窗口（3 秒）解析。</summary>
    public static bool ResolveActive(bool queryActive, bool eventActive, TimeSpan sinceEvent)
        => ResolveActive(queryActive, eventActive, sinceEvent, EventAuthorityWindow);
}
