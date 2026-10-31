using System.Globalization;
using System.Text;

namespace OBS_Helper.Wpf.Services.Analytics;

/// <summary>一场直播的复盘输入（S08）。</summary>
public sealed class SessionMetrics
{
    public TimeSpan Duration { get; init; }
    public int PeakViewers { get; init; }
    public int ChatMessages { get; init; }
    public int NewFollowers { get; init; }
    public long GiftValue { get; init; }
    public double AvgBitrateKbps { get; init; }
    public double DroppedRatio { get; init; }
    public int ClipCount { get; init; }
}

/// <summary>直播复盘教练（S08）：评分、行动清单、可导出 Markdown；纯本地。</summary>
public static class SessionCoachCore
{
    public static (int Score, List<string> Actions, string Grade) Evaluate(SessionMetrics m)
    {
        var actions = new List<string>();
        var score = 50;

        if (m.Duration >= TimeSpan.FromHours(2)) score += 15;
        else if (m.Duration >= TimeSpan.FromHours(1)) score += 8;
        else actions.Add("时长不足 1 小时：固定开播时段更容易积累回访。");

        var chatPerHour = m.Duration.TotalHours > 0 ? m.ChatMessages / m.Duration.TotalHours : 0;
        if (chatPerHour >= 60) score += 15;
        else if (chatPerHour >= 20) score += 8;
        else actions.Add("互动偏少：准备 3 个固定话题，或开启弹幕投票。");

        if (m.NewFollowers >= 20) score += 10;
        else if (m.NewFollowers == 0) actions.Add("本场没有新增关注：结尾明确引导关注并说明下期内容。");

        if (m.DroppedRatio > 0.01) actions.Add("掉帧偏高：降低码率或改用硬件编码。");
        else score += 5;

        if (m.ClipCount == 0) actions.Add("本场没有切片：用高光切片导出 2~3 条短视频。");
        else score += 5;

        if (m.GiftValue > 0) score += 5;

        score = Math.Clamp(score, 0, 100);
        var grade = score >= 85 ? "A" : score >= 70 ? "B" : score >= 55 ? "C" : "D";
        if (actions.Count == 0) actions.Add("状态不错：保持节奏，下期尝试一个新的互动环节。");
        return (score, actions.Take(5).ToList(), grade);
    }

    public static string BuildReport(string title, DateTimeOffset at, SessionMetrics m)
    {
        var (score, actions, grade) = Evaluate(m);
        var sb = new StringBuilder();
        sb.AppendLine("# 直播复盘：" + (string.IsNullOrWhiteSpace(title) ? "未命名场次" : title));
        sb.AppendLine();
        sb.AppendLine("- 时间：" + at.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        sb.AppendLine($"- 时长：{m.Duration:hh\\:mm\\:ss}");
        sb.AppendLine($"- 峰值观看：{m.PeakViewers}");
        sb.AppendLine($"- 弹幕：{m.ChatMessages}");
        sb.AppendLine($"- 新增关注：{m.NewFollowers}");
        sb.AppendLine($"- 礼物价值：{m.GiftValue}");
        sb.AppendLine($"- 平均码率：{m.AvgBitrateKbps:0} kbps");
        sb.AppendLine($"- 掉帧率：{m.DroppedRatio:P2}");
        sb.AppendLine($"- 综合评分：**{score}/100（{grade}）**");
        sb.AppendLine();
        sb.AppendLine("## 下次改什么");
        foreach (var action in actions) sb.AppendLine("- " + action);
        sb.AppendLine();
        sb.AppendLine("> 报告只保存在本机，不上传任何平台。");
        return sb.ToString();
    }
}

/// <summary>智能封面 / 标题 / 话题（S07）：3 套候选、可编辑、不自动发布。</summary>
public static class CoverCopyCore
{
    public static readonly string[] BannedWords = { "最强", "第一", "百分百", "保证", "官方认证" };

    public static IReadOnlyList<(string Title, string Topics)> Suggest(string game, string keyword, string mood)
    {
        var g = Clean(game, 16);
        var k = Clean(keyword, 20);
        var m = Clean(mood, 8);
        var list = new List<(string, string)>
        {
            ($"{g} · {k} 直播中{m}", $"{g},直播,{k}"),
            ($"今晚只玩 {g}：{k}", $"{g},游戏直播,{k}"),
            ($"{m}{g}，一起来聊聊{k}", $"{g},聊天,{k}"),
        };
        return list.Select(x => (Trim(x.Item1, 60), x.Item2)).ToList();
    }

    public static string Clean(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return "直播";
        var cleaned = new string(text.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        return Trim(cleaned.Length == 0 ? "直播" : cleaned, max);
    }

    private static string Trim(string text, int max) => text.Length <= max ? text : text[..max];

    public static bool IsSafe(string? text)
        => !string.IsNullOrWhiteSpace(text) && !BannedWords.Any(w => text.Contains(w, StringComparison.Ordinal));
}

/// <summary>观众数据本地看板（E06）：按分钟聚合、留存/互动趋势、导出/清空；不上传。</summary>
public sealed class ViewerStatsCore
{
    public sealed class MinuteBucket
    {
        public DateTimeOffset Minute { get; init; }
        public int Viewers { get; set; }
        public int Messages { get; set; }
        public int Follows { get; set; }
    }

    private readonly SortedDictionary<long, MinuteBucket> _buckets = new();
    private readonly object _gate = new();
    private long _giftValue;
    private int _clipsExported;

    public int PeakViewers { get { lock (_gate) return _buckets.Count == 0 ? 0 : _buckets.Values.Max(b => b.Viewers); } }
    public int TotalMessages { get { lock (_gate) return _buckets.Values.Sum(b => b.Messages); } }
    public int TotalFollows { get { lock (_gate) return _buckets.Values.Sum(b => b.Follows); } }
    public long GiftValue { get { lock (_gate) return _giftValue; } }
    public int ClipsExported { get { lock (_gate) return _clipsExported; } }

    public void AddGift(long value)
    {
        if (value <= 0) return;
        lock (_gate) _giftValue += value;
    }

    public void AddClip()
    {
        lock (_gate) _clipsExported++;
    }

    public void Add(DateTimeOffset at, int viewers, int messages, int follows)
    {
        var key = at.ToUnixTimeSeconds() / 60;
        lock (_gate)
        {
            if (!_buckets.TryGetValue(key, out var bucket))
            {
                bucket = new MinuteBucket { Minute = DateTimeOffset.FromUnixTimeSeconds(key * 60) };
                _buckets[key] = bucket;
            }
            bucket.Viewers = Math.Max(bucket.Viewers, viewers);
            bucket.Messages += messages;
            bucket.Follows += follows;
        }
    }

    public IReadOnlyList<MinuteBucket> Snapshot()
    {
        lock (_gate) return _buckets.Values.ToList();
    }

    /// <summary>留存率：最后 5 分钟平均观看 / 峰值观看。</summary>
    public double Retention()
    {
        var list = Snapshot();
        if (list.Count == 0) return 0;
        var peak = list.Max(b => b.Viewers);
        if (peak <= 0) return 0;
        var tail = list.Skip(Math.Max(0, list.Count - 5));
        return Math.Clamp(tail.Average(b => b.Viewers) / peak, 0, 1);
    }

    public string ExportCsv()
    {
        var sb = new StringBuilder("minute,viewers,messages,follows\n");
        foreach (var b in Snapshot())
            sb.Append(b.Minute.ToString("O", CultureInfo.InvariantCulture)).Append(',')
              .Append(b.Viewers).Append(',').Append(b.Messages).Append(',').Append(b.Follows).Append('\n');
        return sb.ToString();
    }

    /// <summary>落盘状态（X1031Y3）：分钟桶 + 礼物 + 切片数，重启后恢复。</summary>
    public sealed class State
    {
        public List<MinuteBucket> Buckets { get; set; } = new();
        public long GiftValue { get; set; }
        public int ClipsExported { get; set; }
    }

    public State ExportState()
    {
        lock (_gate)
        {
            return new State
            {
                Buckets = _buckets.Values.ToList(),
                GiftValue = _giftValue,
                ClipsExported = _clipsExported,
            };
        }
    }

    public void ImportState(State? state)
    {
        lock (_gate)
        {
            _buckets.Clear();
            if (state?.Buckets is not null)
            {
                foreach (var bucket in state.Buckets)
                    _buckets[bucket.Minute.ToUnixTimeSeconds() / 60] = bucket;
            }
            _giftValue = state?.GiftValue ?? 0;
            _clipsExported = state?.ClipsExported ?? 0;
        }
    }

    public void Clear() { lock (_gate) { _buckets.Clear(); _giftValue = 0; _clipsExported = 0; } }
}
