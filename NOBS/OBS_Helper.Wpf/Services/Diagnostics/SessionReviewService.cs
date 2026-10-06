using OBS_Helper.Wpf.Services.Host;
using OBS_Helper.Wpf.Services.Shell;

namespace OBS_Helper.Wpf.Services.Diagnostics;

/// <summary>
/// 会话复盘服务（V3.0 / D5）：把实时日志的命中累计变成一份可读、可导出、可跨会话比较的报告。
///
/// 数据来源全部是既有的只读通道：
/// <list type="bullet">
///   <item><see cref="LogTailerService.SessionHits"/>：本次会话按规则码去重的命中计数；</item>
///   <item>日志页的分析结果条数（由界面传入，服务不反向依赖界面）；</item>
///   <item><see cref="LocalStore"/> 里上一次会话的摘要 —— 这才回答了「这场比上场差在哪」。</item>
/// </list>
/// </summary>
public sealed class SessionReviewService
{
    private const string LastSnapshotKey = "session_review_last";

    private readonly LogTailerService _tailer;
    private readonly LocalStore _store;

    public SessionReviewService(LogTailerService tailer, LocalStore store)
    {
        _tailer = tailer;
        _store = store;
    }

    /// <summary>构造当前会话的复盘报告（不写任何东西）。</summary>
    public SessionReview BuildCurrent(int analyzedProblemCount = 0, DateTime? now = null)
        => SessionReviewCore.Build(
            startedLocal: _tailer.SessionStartedLocal,
            endedLocal: now ?? DateTime.Now,
            hits: _tailer.SessionHits,
            analyzedProblemCount: analyzedProblemCount,
            previous: LoadPrevious());

    /// <summary>把当前会话存为「上次」，并重置计数（用户点「结束本次统计」时调用）。</summary>
    public void EndSessionAndReset(int analyzedProblemCount = 0)
    {
        var review = BuildCurrent(analyzedProblemCount);
        SaveSnapshot(SessionReviewCore.ToSnapshot(review));
        _tailer.ResetSession();
    }

    /// <summary>读取上次会话的摘要；没有 / 损坏时返回 null。</summary>
    public SessionSnapshot? LoadPrevious()
    {
        try
        {
            var raw = _store.GetItem(LastSnapshotKey);
            if (string.IsNullOrWhiteSpace(raw)) return null;
            return System.Text.Json.JsonSerializer.Deserialize<SessionSnapshot>(raw);
        }
        catch (Exception ex)
        {
            FileLogger.Warn("SessionReview", $"读取上次会话摘要失败（按无历史处理）：{ex.Message}");
            return null;
        }
    }

    private void SaveSnapshot(SessionSnapshot snapshot)
    {
        try
        {
            _store.SetItem(LastSnapshotKey, System.Text.Json.JsonSerializer.Serialize(snapshot));
        }
        catch (Exception ex)
        {
            FileLogger.Warn("SessionReview", $"保存会话摘要失败：{ex.Message}");
        }
    }
}
