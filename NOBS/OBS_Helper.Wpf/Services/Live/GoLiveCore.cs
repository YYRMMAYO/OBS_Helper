using System.Text.Json;
using System.Text.Json.Nodes;
using OBS_Helper.Wpf.Models.Live;

namespace OBS_Helper.Wpf.Services.Live;

/// <summary>开播方式（对应用户能在界面上做的动作）。</summary>
public enum GoLiveMode
{
    /// <summary>OBS 直推 RTMP / RTMPS。</summary>
    Direct,
    /// <summary>平台客户端 + OBS 虚拟摄像头。</summary>
    VirtualCamera,
    /// <summary>只提供图文引导（不自动化）。</summary>
    Manual
}

/// <summary>推流状态快照。</summary>
public sealed record GoLiveStreamStatus(
    bool Active, bool Reconnecting, string Timecode, double Congestion, long SkippedFrames, long TotalFrames);

/// <summary>
/// 开播引导的纯逻辑（V3.3.0 / P0-4）：模式判定、推流码规范化 / 遮罩、OBS 请求体、状态解析、错误分类。
///
/// 安全约定（PLAN_V3.3.0 §3.5）：
/// <list type="bullet">
///   <item>推流码只在内存里出现；<see cref="MaskKey"/> 永远不返回任何明文字符；</item>
///   <item>日志 / 诊断里只允许出现平台 id 与「有没有填」，不允许出现 server 的查询串或 key；</item>
///   <item>v4（Win7）不支持 <c>SetStreamServiceSettings</c>，不假装支持。</item>
/// </list>
/// </summary>
public static class GoLiveCore
{
    /// <summary>推流码长度上限（正常 20~100 字符；超长基本是粘贴错了整段文本）。</summary>
    public const int MaxKeyLength = 512;

    /// <summary>服务器地址长度上限。</summary>
    public const int MaxServerLength = 512;

    /// <summary>平台模式字符串 → 枚举。</summary>
    public static GoLiveMode ParseMode(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        "direct" => GoLiveMode.Direct,
        "virtualcam" => GoLiveMode.VirtualCamera,
        _ => GoLiveMode.Manual
    };

    /// <summary>平台是否支持 OBS 直推。</summary>
    public static bool SupportsDirect(LivePlatform? platform)
        => platform is not null && ParseMode(platform.Mode) == GoLiveMode.Direct;

    /// <summary>规范化服务器地址：去空白、补协议检查；不合法返回 null。</summary>
    public static string? NormalizeServer(string? server)
    {
        if (string.IsNullOrWhiteSpace(server)) return null;
        var s = server.Trim();
        if (s.Length > MaxServerLength) return null;
        if (s.Any(char.IsControl)) return null;
        if (!s.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase)
            && !s.StartsWith("rtmps://", StringComparison.OrdinalIgnoreCase)) return null;
        return s;
    }

    /// <summary>规范化推流码：去掉粘贴时带进来的空白 / 换行；空串或超长返回 null。</summary>
    public static string? NormalizeKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        var cleaned = new string(key.Where(c => !char.IsWhiteSpace(c) && !char.IsControl(c)).ToArray());
        if (cleaned.Length == 0 || cleaned.Length > MaxKeyLength) return null;
        return cleaned;
    }

    /// <summary>
    /// 推流码遮罩：只返回固定长度的圆点与长度提示，不透露任何原字符。
    /// 连「首尾各留两位」都不做 —— 那足以让旁观者对上一个已知前缀。
    /// </summary>
    public static string MaskKey(string? key)
    {
        var normalized = NormalizeKey(key);
        if (normalized is null) return "";
        return new string('•', Math.Min(12, Math.Max(4, normalized.Length))) + $" ({normalized.Length})";
    }

    /// <summary>服务器地址的日志 / 界面展示：只保留协议与主机，丢弃路径与查询串。</summary>
    public static string MaskServer(string? server)
    {
        var s = NormalizeServer(server);
        if (s is null) return "";
        try
        {
            var uri = new Uri(s);
            return $"{uri.Scheme}://{uri.Host}{(uri.Port is > 0 and not 1935 and not 443 and not 80 ? ":" + uri.Port : "")}";
        }
        catch (Exception)
        {
            return "";
        }
    }

    /// <summary>直推前的输入校验；返回稳定错误码（空串 = 通过）。</summary>
    public static string ValidateDirectInput(string? server, string? key)
    {
        if (NormalizeServer(server) is null) return "GOLIVE_BAD_SERVER";
        if (NormalizeKey(key) is null)
            return string.IsNullOrWhiteSpace(key) ? "GOLIVE_NO_KEY" : "GOLIVE_KEY_TOO_LONG";
        return "";
    }

    /// <summary>构造 <c>SetStreamServiceSettings</c> 的请求体（简单 RTMP：rtmp_custom + server/key）。</summary>
    public static JsonObject BuildStreamServiceSettings(string server, string key)
    {
        return new JsonObject
        {
            ["streamServiceType"] = "rtmp_custom",
            ["streamServiceSettings"] = new JsonObject
            {
                ["server"] = server,
                ["key"] = key
            }
        };
    }

    /// <summary>解析 <c>GetStreamStatus</c> 的响应。</summary>
    public static GoLiveStreamStatus ParseStreamStatus(JsonElement data)
    {
        bool Bool(string name) => data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

        var timecode = data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("outputTimecode", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString() ?? ""
            : "";

        var congestion = data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("outputCongestion", out var c) && c.ValueKind == JsonValueKind.Number
            ? c.GetDouble()
            : 0;

        long Long(string name) => data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            && v.TryGetInt64(out var n) ? n : 0;

        return new GoLiveStreamStatus(
            data.ValueKind == JsonValueKind.Object && Bool("outputActive"),
            data.ValueKind == JsonValueKind.Object && data.TryGetProperty("outputState", out var st)
                && st.ValueKind == JsonValueKind.String
                && (st.GetString() ?? "").Contains("RECONNECT", StringComparison.OrdinalIgnoreCase),
            timecode,
            congestion,
            Long("outputSkippedFrames"),
            Long("outputTotalFrames"));
    }

    /// <summary>
    /// 推流失败分类：把 OBS 的错误文本 / 日志片段归到用户能照做的几类。
    /// 不做自动重试：推流码错了重试一百次也还是错，只会刷爆平台的风控。
    /// </summary>
    public static string ClassifyStreamError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "GOLIVE_STREAM_FAILED";
        var m = message.ToLowerInvariant();

        if (m.Contains("invalid") || m.Contains("authentication") || m.Contains("key") || m.Contains("unauthorized"))
            return "GOLIVE_KEY_INVALID";
        if (m.Contains("resolve") || m.Contains("connect") || m.Contains("unreachable") || m.Contains("refused") || m.Contains("timeout"))
            return "GOLIVE_SERVER_UNREACHABLE";
        if (m.Contains("encoder") || m.Contains("nvenc") || m.Contains("x264") || m.Contains("qsv"))
            return "GOLIVE_ENCODER_FAILED";
        if (m.Contains("bandwidth") || m.Contains("network") || m.Contains("dropped") || m.Contains("congestion"))
            return "GOLIVE_NETWORK";
        if (m.Contains("already") && m.Contains("stream"))
            return "GOLIVE_ALREADY_STREAMING";
        return "GOLIVE_STREAM_FAILED";
    }

    /// <summary>开播步骤 id（界面按 id 取文案，顺序即执行顺序）。</summary>
    public static readonly string[] DirectSteps =
    {
        "GOLIVE_STEP_VALIDATE", "GOLIVE_STEP_CONNECT", "GOLIVE_STEP_WRITE",
        "GOLIVE_STEP_VERIFY", "GOLIVE_STEP_START", "GOLIVE_STEP_CONFIRM"
    };

    /// <summary>虚拟摄像头步骤 id。</summary>
    public static readonly string[] VirtualCamSteps =
    {
        "GOLIVE_STEP_CHECK_VCAM", "GOLIVE_STEP_START_VCAM", "GOLIVE_STEP_CONFIRM_VCAM"
    };
}
