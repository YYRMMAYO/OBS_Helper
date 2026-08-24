using System.Diagnostics;
using System.Globalization;

namespace OBS_Helper.Wpf.Services.Tools;

/// <summary>一个候选推流节点（ingest）。</summary>
public sealed record IngestTarget(string Label, string Host, int Port = 1935);

/// <summary>一次 TCP 连接探测的结果。RttMs 为 null 表示连接失败。</summary>
public sealed record IngestPingResult(IngestTarget Target, double? RttMs)
{
    /// <summary>展示用文本：失败时给出可读原因。</summary>
    public string RttText => RttMs is null
        ? "连接失败"
        : $"{RttMs.Value.ToString("0", CultureInfo.InvariantCulture)} ms";

    public bool Ok => RttMs is not null;
}

/// <summary>
/// 推流节点延迟探测（V2.7 工具箱）。
///
/// 对候选 ingest 域名做 TCP 三次握手计时（RTT 近似值）：
/// 只反映「到该节点端口的网络往返」，不代表实际推流质量——
/// 界面与结论中必须保留这一提示，避免用户把 ping 低当成唯一依据。
///
/// 全部探测失败时静默降级为逐条「连接失败」结果，绝不抛异常。
/// </summary>
public static class IngestPingService
{
    /// <summary>单次探测超时（毫秒）。</summary>
    public const int TimeoutMs = 800;

    /// <summary>内置候选节点快照：仅收录长期稳定的官方通用入口，随包数据可在后续版本更新。</summary>
    public static readonly IngestTarget[] DefaultTargets =
    {
        new("B站 · 主力推流入口", "live-push.bilivideo.com"),
        new("Twitch · 全球聚合入口", "ingest.global.contribute.live-video.net"),
        new("YouTube · RTMP 入口", "a.rtmp.youtube.com"),
        new("自定义 / 其他平台", "填写你的服务器地址"),
    };

    /// <summary>
    /// 排序纯逻辑：成功的按 RTT 升序在前，失败的按原顺序垫底。
    /// 返回新列表，不修改输入。
    /// </summary>
    public static List<IngestPingResult> Sort(IEnumerable<IngestPingResult> results)
    {
        var list = results.ToList();
        return list
            .Select((r, i) => (r, i))
            .OrderBy(x => x.r.Ok ? 0 : 1)
            .ThenBy(x => x.r.RttMs ?? double.MaxValue)
            .ThenBy(x => x.i)
            .Select(x => x.r)
            .ToList();
    }

    /// <summary>对单个目标做 TCP 握手测速。任何异常都折算为失败结果。</summary>
    public static async Task<IngestPingResult> MeasureAsync(IngestTarget target, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(target.Host) || target.Port is <= 0 or > 65535)
                return new IngestPingResult(target, null);

            using var client = new System.Net.Sockets.TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeoutMs);

            var sw = Stopwatch.StartNew();
            await client.ConnectAsync(target.Host, target.Port, timeout.Token).ConfigureAwait(false);
            sw.Stop();

            return ct.IsCancellationRequested
                ? new IngestPingResult(target, null)
                : new IngestPingResult(target, sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception)
        {
            return new IngestPingResult(target, null);
        }
    }

    /// <summary>并发探测全部候选并返回排序后的结果。</summary>
    public static async Task<List<IngestPingResult>> MeasureAllAsync(
        IEnumerable<IngestTarget> targets, CancellationToken ct = default)
    {
        var tasks = targets.Select(t => MeasureAsync(t, ct));
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return Sort(results);
    }
}
