using System.Globalization;
using System.IO;
using OBS_Helper.Wpf.Services.Mobile;
using System.Text;

namespace OBS_Helper.Wpf.Services.Clips;

/// <summary>高光切片（S05）：按弹幕速率 + 音频能量 + 场景变化打分，去重并给出窗口。</summary>
public static class HighlightCore
{
    public sealed class Signal
    {
        public DateTimeOffset At { get; init; }
        public double ChatRate { get; init; }
        public double AudioEnergy { get; init; }
        public bool SceneChanged { get; init; }
    }

    public sealed record Clip(DateTimeOffset From, DateTimeOffset To, double Score, string Reason);

    public static IReadOnlyList<Clip> FindHighlights(IEnumerable<Signal> signals, int minSeconds = 15, int maxSeconds = 60, int top = 5)
    {
        var list = signals.OrderBy(s => s.At).ToList();
        if (list.Count == 0) return Array.Empty<Clip>();
        var scored = new List<Clip>();
        for (var i = 0; i < list.Count; i++)
        {
            var window = list.Skip(Math.Max(0, i - 2)).Take(5).ToList();
            var chat = window.Average(s => s.ChatRate);
            var audio = window.Average(s => s.AudioEnergy);
            var scene = window.Count(s => s.SceneChanged);
            var score = chat * 1.2 + audio * 2.0 + scene * 1.5;
            if (score < 1.0) continue;
            var from = list[i].At.AddSeconds(-5);
            var to = from.AddSeconds(Math.Clamp(minSeconds + chat * 20, minSeconds, maxSeconds));
            scored.Add(new Clip(from, to, score, $"弹幕 {chat:0.0}/s · 音量 {audio:0.00} · 切场景 {scene}"));
        }
        // 重叠去重：保留分数更高的窗口
        var result = new List<Clip>();
        foreach (var clip in scored.OrderByDescending(c => c.Score))
        {
            if (result.Any(r => clip.From < r.To && clip.To > r.From)) continue;
            result.Add(clip);
            if (result.Count >= top) break;
        }
        return result.OrderBy(c => c.From).ToList();
    }

    /// <summary>评分归一化，便于 UI 显示 0~100。</summary>
    public static int NormalizeScore(double raw, double max = 10) => (int)Math.Clamp(raw / max * 100, 0, 100);
}

/// <summary>竖屏自动重构（S06）：主体跟踪 → 9:16 安全区 → 裁切窗口；导出前预览。</summary>
public static class ReframeCore
{
    public sealed record Point(double X, double Y, double Confidence);

    public static (int X, int Y, int Width, int Height) CropWindow(
        int sourceWidth, int sourceHeight, IReadOnlyList<Point> track, int frameIndex, double smoothing = 0.7)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0) return (0, 0, 0, 0);
        var targetW = (int)Math.Round(sourceHeight * 9.0 / 16.0);
        if (targetW > sourceWidth) targetW = sourceWidth;
        var centerX = sourceWidth / 2.0;
        if (track.Count > 0)
        {
            var start = Math.Max(0, frameIndex - 5);
            var window = track.Skip(start).Take(6).Where(p => p.Confidence >= 0.3).ToList();
            if (window.Count > 0)
            {
                var avg = window.Average(p => p.X);
                centerX = centerX * smoothing + avg * (1 - smoothing);
            }
        }
        var x = (int)Math.Clamp(centerX - targetW / 2.0, 0, sourceWidth - targetW);
        return (x, 0, targetW, sourceHeight);
    }

    /// <summary>安全区：底部 12% 留给平台 UI，字幕放在上方。</summary>
    public static (double Top, double Bottom) SafeArea(int height)
        => (height * 0.06, height * 0.88);

    /// <summary>FFmpeg crop 参数（不含滤镜链其余部分）。</summary>
    public static string CropFilter(int x, int y, int w, int h)
        => string.Create(CultureInfo.InvariantCulture, $"crop={w}:{h}:{x}:{y},scale=1080:1920");
}

/// <summary>录制文件 AI 检索（S10）：转写索引、本地搜索、可删除索引；不联网。</summary>
public sealed class RecordingSearchCore
{
    public sealed class Segment
    {
        public string File { get; init; } = "";
        public string Text { get; init; } = "";
        public TimeSpan At { get; init; }
    }

    private readonly List<Segment> _segments = new();
    private readonly Dictionary<string, HashSet<int>> _index = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public int Count { get { lock (_gate) return _segments.Count; } }

    public void Add(Segment segment)
    {
        if (string.IsNullOrWhiteSpace(segment.Text)) return;
        lock (_gate)
        {
            var id = _segments.Count;
            _segments.Add(segment);
            foreach (var token in Tokenize(segment.Text))
            {
                if (!_index.TryGetValue(token, out var set))
                {
                    set = new HashSet<int>();
                    _index[token] = set;
                }
                set.Add(id);
            }
        }
    }

    public IReadOnlyList<Segment> Search(string query, int max = 20)
    {
        var tokens = Tokenize(query).ToList();
        if (tokens.Count == 0) return Array.Empty<Segment>();
        lock (_gate)
        {
            HashSet<int>? result = null;
            foreach (var token in tokens)
            {
                if (!_index.TryGetValue(token, out var set)) return Array.Empty<Segment>();
                if (result is null) result = new HashSet<int>(set);
                else result.IntersectWith(set);
            }
            return result!.Take(max).Select(i => _segments[i]).ToList();
        }
    }

    public void Clear() { lock (_gate) { _segments.Clear(); _index.Clear(); } }

    /// <summary>中英混排的粗粒度分词：中文按双字滑窗，英文按单词。</summary>
    public static IEnumerable<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        var lower = text.ToLowerInvariant();
        var word = new StringBuilder();
        foreach (var ch in lower)
        {
            if (char.IsLetterOrDigit(ch) && ch < 128) { word.Append(ch); continue; }
            if (word.Length > 0) { if (word.Length > 1) yield return word.ToString(); word.Clear(); }
            if (ch >= 0x4E00 && ch <= 0x9FFF) yield return ch.ToString();
        }
        if (word.Length > 1) yield return word.ToString();
    }
}

/// <summary>AI 导播（S09）：规则 → 建议；人工确认后才切场景；可回退。</summary>
public static class DirectorCore
{
    public sealed record Rule(string Id, string When, string Scene, int Priority, bool Enabled);

    public sealed record Suggestion(string RuleId, string Scene, string Reason, bool RequiresConfirm);

    public static readonly Rule[] Defaults =
    {
        new("game", "游戏窗口获得焦点", "游戏", 10, true),
        new("chat", "只开聊天/等待", "聊天", 5, true),
        new("brb", "离开超过 2 分钟", "BRB", 20, true),
    };

    public static Suggestion? Evaluate(IEnumerable<Rule> rules, string context)
        => rules.Where(r => r.Enabled && string.Equals(r.When, context, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.Priority)
                .Select(r => new Suggestion(r.Id, r.Scene, $"匹配规则：{r.When}", true))
                .FirstOrDefault();

    /// <summary>冲突检测：同一优先级指向不同场景时告警。</summary>
    public static bool HasConflict(IEnumerable<Rule> rules)
    {
        var groups = rules.Where(r => r.Enabled).GroupBy(r => r.Priority);
        return groups.Any(g => g.Select(r => r.Scene).Distinct().Count() > 1);
    }

    public static string Describe(Suggestion? suggestion)
        => suggestion is null ? "" : $"建议切到「{suggestion.Scene}」（{suggestion.Reason}），需要你确认。";
}

/// <summary>FFmpeg 参数白名单（S05/S06/Q03）：只允许受控参数，不拼接 shell 字符串。</summary>
public static class FfmpegArgs
{
    public static bool IsSafeInput(string? path)
        => !string.IsNullOrWhiteSpace(path) && path.IndexOfAny(new[] { '\0', '\r', '\n', '"' }) < 0;

    public static IReadOnlyList<string> BuildClip(string input, string output, TimeSpan from, TimeSpan duration)
    {
        var args = new List<string>
        {
            "-hide_banner", "-nostdin", "-y",
            "-ss", from.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture),
            "-i", input,
            "-t", duration.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture),
            "-c", "copy",
        };
        args.Add(output);
        return args;
    }

    public static IReadOnlyList<string> BuildVertical(string input, string output, int x, int y, int w, int h)
    {
        var args = new List<string> { "-hide_banner", "-nostdin", "-y", "-i", input, "-vf", ReframeCore.CropFilter(x, y, w, h), "-c:v", "libx264", "-preset", "veryfast", "-crf", "20", "-c:a", "aac" };
        args.Add(output);
        return args;
    }

    public static IReadOnlyList<string> BuildLoudnorm(string input, string output, string filter)
        => new[] { "-hide_banner", "-nostdin", "-y", "-i", input, "-af", filter, "-c:v", "copy", "-c:a", "aac", output };

    /// <summary>校验输出路径：必须是绝对路径、扩展名白名单、不覆盖输入。</summary>
    public static (bool Ok, string Code) ValidateOutput(string input, string output)
    {
        if (!IsSafeInput(output)) return (false, FeatureErrors.ClipOutputFailed);
        if (!Path.IsPathRooted(output)) return (false, FeatureErrors.ClipOutputFailed);
        var ext = Path.GetExtension(output).ToLowerInvariant();
        if (ext is not (".mp4" or ".mkv" or ".mov" or ".wav" or ".m4a")) return (false, FeatureErrors.ClipOutputFailed);
        if (string.Equals(Path.GetFullPath(input), Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase))
            return (false, FeatureErrors.ClipOutputFailed);
        return (true, "");
    }
}

/// <summary>FFmpeg 执行器：超时、输出限流、失败码映射。</summary>
public sealed class FfmpegRunner
{
    private readonly string _executable;
    private readonly TimeSpan _timeout;

    public FfmpegRunner(string executable, TimeSpan? timeout = null)
    {
        _executable = executable;
        _timeout = timeout ?? TimeSpan.FromMinutes(30);
    }

    public bool IsReady => File.Exists(_executable);

    public async Task<(bool Ok, string Code, string Output)> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
    {
        if (!IsReady) return (false, FeatureErrors.ClipFfmpegMissing, "");
        try
        {
            var info = new System.Diagnostics.ProcessStartInfo(_executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var arg in args) info.ArgumentList.Add(arg);
            using var process = System.Diagnostics.Process.Start(info);
            if (process is null) return (false, FeatureErrors.ClipFfmpegMissing, "");
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(_timeout);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var text = await stderr.ConfigureAwait(false);
            return process.ExitCode == 0
                ? (true, "", text)
                : (false, FeatureErrors.ClipOutputFailed, text.Length > 2000 ? text[^2000..] : text);
        }
        catch (OperationCanceledException) { return (false, FeatureErrors.ClipOutputFailed, "timeout"); }
        catch (Exception) { return (false, FeatureErrors.ClipFfmpegMissing, ""); }
    }
}
