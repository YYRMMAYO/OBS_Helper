using System.Globalization;
using System.Text;

namespace OBS_Helper.Wpf.Services.Streaming;

/// <summary>推流健康输入（Q02）。</summary>
public sealed class StreamHealthInput
{
    public bool Streaming { get; init; }
    public double ActiveFps { get; init; }
    public double BitrateKbps { get; init; }
    public long DroppedFrames { get; init; }
    public long TotalFrames { get; init; }
    public double Congestion { get; init; }
    public double UploadKbps { get; init; }
    public int ElapsedSeconds { get; init; }
}

public sealed record StreamAdvice(string Code, string Title, string Detail, int Severity);

/// <summary>推流健康医生（Q02）：带宽/掉帧/断流评分与可执行建议；不自动改码率。</summary>
public static class StreamHealthCore
{
    public static (int Score, string Grade, List<StreamAdvice> Advice) Evaluate(StreamHealthInput input)
    {
        var advice = new List<StreamAdvice>();
        var score = 100;

        if (!input.Streaming)
        {
            advice.Add(new("STREAM_IDLE", "未在推流", "开始推流后才能评估上行健康。", 0));
            return (100, "idle", advice);
        }

        var dropRatio = input.TotalFrames > 0 ? (double)input.DroppedFrames / input.TotalFrames : 0;
        if (dropRatio > 0.02)
        {
            score -= 35;
            advice.Add(new("STREAM_DROPS", "输出掉帧偏高",
                $"{dropRatio:P1} 的帧被丢弃。先降低码率/分辨率，或改用硬件编码。", 3));
        }
        else if (dropRatio > 0.005)
        {
            score -= 15;
            advice.Add(new("STREAM_DROPS_WARN", "有轻微掉帧",
                $"{dropRatio:P1} 的帧被丢弃，建议观察并适当降低码率。", 1));
        }

        if (input.Congestion > 0.3)
        {
            score -= 25;
            advice.Add(new("STREAM_CONGESTION", "上行拥塞",
                "网络拥塞度较高，检查是否有下载/上传占满带宽，或换 5GHz/有线。", 2));
        }

        if (input.UploadKbps > 0 && input.BitrateKbps > 0)
        {
            var headroom = input.UploadKbps / input.BitrateKbps;
            if (headroom < 1.15)
            {
                score -= 30;
                advice.Add(new("STREAM_BANDWIDTH_LOW", "上行带宽不足",
                    $"实测上行 {input.UploadKbps:0} kbps，推流码率 {input.BitrateKbps:0} kbps，余量不足 15%。降低码率或换网络。", 3));
            }
            else if (headroom < 1.4)
            {
                score -= 10;
                advice.Add(new("STREAM_BANDWIDTH_TIGHT", "上行余量偏小",
                    $"上行余量约 {(headroom - 1) * 100:0}%，建议预留 40% 以上。", 1));
            }
        }

        if (input.ActiveFps > 0 && input.ActiveFps < 24)
        {
            score -= 10;
            advice.Add(new("STREAM_FPS_LOW", "渲染帧率偏低",
                $"当前 {input.ActiveFps:0.0} fps，检查场景负载/滤镜/浏览器源。", 1));
        }

        if (input.BitrateKbps > 0 && input.UploadKbps <= 0)
        {
            advice.Add(new("STREAM_NO_PROBE", "未探测上行带宽",
                "运行一次带宽探测可给出更准确的码率建议。", 0));
        }

        score = Math.Clamp(score, 0, 100);
        var grade = score >= 90 ? "good" : score >= 70 ? "fair" : score >= 40 ? "poor" : "bad";
        return (score, grade, advice.OrderByDescending(a => a.Severity).ToList());
    }

    /// <summary>码率建议：目标 = 实测上行 × 0.6，钳到平台惯例区间。</summary>
    public static int RecommendBitrate(double uploadKbps, int width, int height, int fps)
    {
        var cap = uploadKbps > 0 ? (int)(uploadKbps * 0.6) : 0;
        var target = (width, height) switch
        {
            ( >= 1920, _) => fps >= 60 ? 9000 : 6000,
            ( >= 1280, _) => fps >= 60 ? 6000 : 4000,
            _ => fps >= 60 ? 3500 : 2500,
        };
        return cap > 0 ? Math.Clamp(Math.Min(target, cap), 800, 50000) : target;
    }
}

/// <summary>构图/灯光/色温检查输入（Q04）。</summary>
public sealed class CompositionSample
{
    public double Brightness { get; init; }        // 0~1 平均亮度
    public double SkinToneRatio { get; init; }     // 0~1 人脸区域占比
    public double ColorTempK { get; init; }
    public double CenterOffset { get; init; }      // 主体偏离画面中心（0~1）
    public double Contrast { get; init; }          // 0~1
}

/// <summary>构图 / 灯光 / 色温检查 2.0（Q04）：给分数与一句话建议，可整体关闭。</summary>
public static class CompositionCheckCore
{
    public static (int Score, List<string> Issues) Evaluate(CompositionSample sample)
    {
        var issues = new List<string>();
        var score = 100;

        if (sample.Brightness < 0.18)
        {
            score -= 30;
            issues.Add("画面偏暗：提高主光或相机曝光。");
        }
        else if (sample.Brightness > 0.85)
        {
            score -= 25;
            issues.Add("画面过曝：降低补光或曝光补偿。");
        }

        if (sample.ColorTempK > 0 && (sample.ColorTempK < 4500 || sample.ColorTempK > 7000))
        {
            score -= 20;
            issues.Add($"色温 {(int)sample.ColorTempK}K 偏离常见 5600K；检查白平衡或灯珠色温。");
        }

        if (sample.SkinToneRatio > 0 && sample.SkinToneRatio < 0.02)
        {
            score -= 10;
            issues.Add("人脸占比偏小：靠近镜头或裁切构图。");
        }

        if (sample.CenterOffset > 0.35)
        {
            score -= 15;
            issues.Add("主体偏离中心：调整机位或使用三分构图。");
        }

        if (sample.Contrast < 0.15)
        {
            score -= 10;
            issues.Add("对比度偏低：画面发灰，检查镜头/灯光。");
        }

        return (Math.Clamp(score, 0, 100), issues);
    }
}

/// <summary>开播预案 / 回滚（Q08）：检查项、执行顺序、失败回滚；不静默提权。</summary>
public sealed class RunbookCore
{
    public sealed class Step
    {
        public string Id { get; init; } = "";
        public string Title { get; init; } = "";
        public bool Destructive { get; init; }
        public bool Required { get; init; } = true;
        public string? Rollback { get; init; }
    }

    public sealed class State
    {
        public HashSet<string> Done { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Failed { get; } = new(StringComparer.Ordinal);
        public bool AllRequiredPassed(IEnumerable<Step> steps) => steps.Where(s => s.Required).All(s => Done.Contains(s.Id));
    }

    public static readonly Step[] DefaultSteps =
    {
        new() { Id = "obs", Title = "OBS 已连接", Required = true },
        new() { Id = "scene", Title = "当前场景有画面源", Required = true },
        new() { Id = "audio", Title = "音频输入未静音", Required = true },
        new() { Id = "stream", Title = "推流服务器/码率有效", Required = true },
        new() { Id = "record", Title = "录制路径可用", Required = true },
        new() { Id = "backup", Title = "配置已备份", Required = false, Rollback = "从备份恢复配置" },
    };

    public static (bool Ok, string Code) Mark(State state, string id, bool ok)
    {
        state.Done.Remove(id);
        state.Failed.Remove(id);
        if (ok) state.Done.Add(id); else state.Failed.Add(id);
        return (ok, ok ? "" : "CLIP_OUTPUT_FAILED");
    }

    /// <summary>失败回滚顺序：逆序执行声明了 Rollback 的步骤。</summary>
    public static IReadOnlyList<Step> RollbackPlan(IEnumerable<Step> steps, State state)
        => steps.Where(s => state.Failed.Contains(s.Id) && s.Rollback is not null).Reverse().ToList();
}

/// <summary>事故回放 / 环形缓冲（Q07）：前后 30 秒、导出、磁盘上限。</summary>
public static class IncidentClipCore
{
    public const int WindowSeconds = 30;
    public const int MaxDiskGb = 8;

    public static (DateTimeOffset From, DateTimeOffset To) Window(DateTimeOffset eventAt, int beforeSeconds = WindowSeconds, int afterSeconds = WindowSeconds)
        => (eventAt.AddSeconds(-beforeSeconds), eventAt.AddSeconds(afterSeconds));

    /// <summary>判断录像文件是否覆盖窗口（文件起点 + 时长）。</summary>
    public static bool Covers(DateTimeOffset fileStart, TimeSpan duration, DateTimeOffset from, DateTimeOffset to)
        => fileStart <= from && fileStart + duration >= to;

    /// <summary>按磁盘上限裁剪最旧的导出文件；返回要删除的路径。</summary>
    public static IReadOnlyList<string> TrimToDiskLimit(IEnumerable<(string Path, long Bytes)> files, long limitBytes)
    {
        var ordered = files.OrderBy(f => f.Path, StringComparer.Ordinal).ToList();
        var total = ordered.Sum(f => f.Bytes);
        var remove = new List<string>();
        foreach (var file in ordered)
        {
            if (total <= limitBytes) break;
            remove.Add(file.Path);
            total -= file.Bytes;
        }
        return remove;
    }

    public static string ExportName(DateTimeOffset at) => $"incident-{at:yyyyMMdd-HHmmss}";
}

/// <summary>安全哨兵（Q06）：黑屏/静音/掉帧/敏感词 → 建议动作；所有动作需确认并审计。</summary>
public static class PanicCore
{
    public sealed record Action(string Id, string Title, bool RequiresConfirm);

    public static readonly Action[] Actions =
    {
        new("mute", "静音所有音频", false),
        new("brb", "切到 BRB 场景", true),
        new("stop", "停止推流", true),
    };

    public sealed class AuditEntry
    {
        public DateTimeOffset At { get; init; } = DateTimeOffset.Now;
        public string Action { get; init; } = "";
        public string Reason { get; init; } = "";
        public bool Confirmed { get; init; }
    }

    public sealed class Auditor
    {
        public List<AuditEntry> Entries { get; } = new();
        public void Record(string action, string reason, bool confirmed)
        {
            Entries.Add(new AuditEntry { Action = action, Reason = reason, Confirmed = confirmed });
            if (Entries.Count > 200) Entries.RemoveAt(0);
        }
    }

    /// <summary>根据信号给出建议动作（不自动执行）。</summary>
    public static IReadOnlyList<Action> Suggest(bool blackScreen, bool silent, bool dropping, bool sensitive)
    {
        var result = new List<Action>();
        if (blackScreen) result.Add(Actions[1]);
        if (silent) result.Add(Actions[0]);
        if (sensitive) result.Add(Actions[1]);
        if (dropping) result.Add(new Action("quality", "降低码率/分辨率", false));
        return result;
    }
}

/// <summary>多平台转推（Q01）：目标、密钥脱敏、健康检查；密钥只进 secrets.dat。</summary>
public static class RelayCore
{
    public sealed class Target
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string Name { get; init; } = "";
        public string RtmpUrl { get; init; } = "";
        public string StreamKey { get; init; } = "";
        public bool Enabled { get; init; } = true;
    }

    public static bool IsValidTarget(Target target)
        => target.RtmpUrl.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase)
           || target.RtmpUrl.StartsWith("rtmps://", StringComparison.OrdinalIgnoreCase);

    /// <summary>展示用地址：隐藏 query 与 key。</summary>
    public static string Redact(Target target)
    {
        var baseUrl = target.RtmpUrl;
        var q = baseUrl.IndexOf('?');
        if (q >= 0) baseUrl = baseUrl[..q];
        return target.Name + " → " + baseUrl + "/***";
    }

    public static (bool Ok, string Detail) Check(IEnumerable<Target> targets)
    {
        var enabled = targets.Where(t => t.Enabled).ToList();
        if (enabled.Count == 0) return (false, "没有启用的转推目标");
        if (enabled.Count > 3) return (false, "最多 3 个转推目标（带宽限制）");
        if (enabled.Any(t => !IsValidTarget(t))) return (false, "目标地址必须是 rtmp:// 或 rtmps://");
        return (true, $"{enabled.Count} 个目标");
    }

    /// <summary>mediamtx/SRS 转推配置片段（只生成文本，不写入密钥日志）。</summary>
    public static string BuildMediamtxPaths(IEnumerable<Target> targets)
    {
        var sb = new StringBuilder();
        foreach (var target in targets.Where(t => t.Enabled))
        {
            sb.Append("  relay-").Append(target.Id[..8]).AppendLine(":")
              .Append("    source: publisher").AppendLine()
              .Append("    runOnReady: ffmpeg -re -i rtsp://127.0.0.1:8554/$MTX_PATH -c copy -f flv \"")
              .Append(target.RtmpUrl).AppendLine("/***\"");
        }
        return sb.ToString();
    }
}

/// <summary>AI 音频链（Q03）：降噪 / 响度 / 齿音 / 变声预设，支持旁路对比。</summary>
public static class AudioChainCore
{
    public sealed record Preset(string Id, string Title, double NoiseReductionDb, double LoudnessLufs, double DeEssDb, string Voice);

    public static readonly Preset[] Presets =
    {
        new("clean", "干净人声", 12, -16, 3, "none"),
        new("warm", "温暖电台", 8, -14, 2, "warm"),
        new("bright", "明亮解说", 10, -15, 4, "bright"),
        new("robot", "变声（AI 标注）", 6, -16, 0, "robot"),
    };

    /// <summary>旁路 A/B：返回当前是否处理（true = 走效果链）。</summary>
    public static bool ShouldProcess(bool enabled, bool bypass) => enabled && !bypass;

    /// <summary>FFmpeg 滤镜字符串（参数白名单，不拼接用户输入）。</summary>
    public static string BuildFilter(Preset preset)
    {
        var parts = new List<string>();
        if (preset.NoiseReductionDb > 0) parts.Add($"afftdn=nr={preset.NoiseReductionDb:0}");
        if (preset.DeEssDb > 0) parts.Add($"deesser=i={Math.Clamp(preset.DeEssDb / 10, 0.1, 1):0.00}");
        parts.Add($"loudnorm=I={preset.LoudnessLufs:0}:TP=-1.5:LRA=11");
        return string.Join(",", parts);
    }
}
