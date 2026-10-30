using System.IO;
using System.Globalization;
using System.Text;

namespace OBS_Helper.Wpf.Services.Subtitles;

/// <summary>一条字幕（S01/S02）。时间戳为本地采集时刻。</summary>
public sealed class CaptionLine
{
    public string Text { get; init; } = "";
    public string Translation { get; init; } = "";
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public double Confidence { get; init; }
}

/// <summary>环形缓冲：采集回调只写入，不做分配/IO（S01）。</summary>
public sealed class AudioRingBuffer
{
    private readonly float[] _buffer;
    private int _write;
    private int _count;
    private readonly object _gate = new();

    public AudioRingBuffer(int capacitySamples = 16000 * 8) => _buffer = new float[Math.Max(1024, capacitySamples)];

    public int Count { get { lock (_gate) return _count; } }

    public void Write(ReadOnlySpan<float> samples)
    {
        lock (_gate)
        {
            foreach (var sample in samples)
            {
                _buffer[_write] = sample;
                _write = (_write + 1) % _buffer.Length;
                if (_count < _buffer.Length) _count++;
            }
        }
    }

    /// <summary>取最多 <paramref name="max"/> 个样本（按时间顺序）。</summary>
    public float[] Read(int max)
    {
        lock (_gate)
        {
            var take = Math.Min(max, _count);
            var result = new float[take];
            var start = (_write - take + _buffer.Length) % _buffer.Length;
            for (var i = 0; i < take; i++) result[i] = _buffer[(start + i) % _buffer.Length];
            _count -= take;
            return result;
        }
    }

    public void Clear() { lock (_gate) { _count = 0; _write = 0; } }
}

/// <summary>
/// VAD + 分段（纯逻辑）：能量阈值判静音；静音 400 ms 或最长 4 s 切段。
/// 采样率固定 16 kHz、单声道 float。
/// </summary>
public sealed class SpeechSegmenter
{
    public const int SampleRate = 16000;
    private readonly float _threshold;
    private readonly int _silenceMs;
    private readonly int _maxMs;
    private readonly List<float> _current = new();
    private int _silentMs;
    private readonly Func<DateTimeOffset> _clock;

    public SpeechSegmenter(float threshold = 0.012f, int silenceMs = 400, int maxMs = 4000, Func<DateTimeOffset>? clock = null)
    {
        _threshold = threshold;
        _silenceMs = silenceMs;
        _maxMs = maxMs;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>传入 20 ms 一帧的样本；返回切出的片段（没有切出时为空）。</summary>
    public IReadOnlyList<(float[] Samples, DateTimeOffset From, DateTimeOffset To)> Push(float[] frame, DateTimeOffset now)
    {
        var rms = Rms(frame);
        var voiced = rms >= _threshold;
        if (voiced || _current.Count > 0)
        {
            _current.AddRange(frame);
            _silentMs = voiced ? 0 : _silentMs + 20;
        }
        var lengthMs = _current.Count * 1000 / SampleRate;
        if (_current.Count == 0) return Array.Empty<(float[], DateTimeOffset, DateTimeOffset)>();
        if (_silentMs >= _silenceMs || lengthMs >= _maxMs)
        {
            var samples = _current.ToArray();
            var from = now.AddMilliseconds(-lengthMs);
            _current.Clear();
            _silentMs = 0;
            // 纯静音不送 ASR
            return Rms(samples) < _threshold ? Array.Empty<(float[], DateTimeOffset, DateTimeOffset)>()
                : new[] { (samples, from, now) };
        }
        return Array.Empty<(float[], DateTimeOffset, DateTimeOffset)>();
    }

    public static double Rms(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0) return 0;
        double sum = 0;
        foreach (var sample in samples) sum += sample * sample;
        return Math.Sqrt(sum / samples.Length);
    }

    public static float[] DownmixToMono(ReadOnlySpan<float> interleaved, int channels)
    {
        if (channels <= 1) return interleaved.ToArray();
        var result = new float[interleaved.Length / channels];
        for (var i = 0; i < result.Length; i++)
        {
            double sum = 0;
            for (var c = 0; c < channels; c++) sum += interleaved[i * channels + c];
            result[i] = (float)(sum / channels);
        }
        return result;
    }

    /// <summary>线性重采样到 16 kHz（纯 BCL，够 16 kHz ASR 用）。</summary>
    public static float[] ResampleTo16k(ReadOnlySpan<float> input, int sourceRate)
    {
        if (sourceRate == SampleRate) return input.ToArray();
        if (input.Length == 0 || sourceRate <= 0) return Array.Empty<float>();
        var ratio = (double)sourceRate / SampleRate;
        var outputLength = (int)(input.Length / ratio);
        var output = new float[outputLength];
        for (var i = 0; i < outputLength; i++)
        {
            var pos = i * ratio;
            var index = (int)pos;
            var frac = (float)(pos - index);
            var a = input[Math.Min(index, input.Length - 1)];
            var b = input[Math.Min(index + 1, input.Length - 1)];
            output[i] = a + (b - a) * frac;
        }
        return output;
    }

    /// <summary>把 16 kHz float 单声道样本写成 16-bit PCM WAV（用于 whisper.cpp）。</summary>
    public static byte[] ToWav(ReadOnlySpan<float> samples)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var dataBytes = samples.Length * 2;
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataBytes);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataBytes);
        foreach (var sample in samples)
            writer.Write((short)Math.Clamp((int)(sample * short.MaxValue), short.MinValue, short.MaxValue));
        writer.Flush();
        return stream.ToArray();
    }
}

/// <summary>
/// 字幕合并 / 断句 / 限长（S01/S02）：把 ASR 的分段结果整理成适合 OBS 文本源与 overlay 的短句。
/// </summary>
public static class SubtitleCore
{
    public const int MaxLineLength = 80;
    public const int MaxLines = 2;

    /// <summary>合并两段文本：同一句未结束时用空格/中文直接拼接，避免字幕抖动。</summary>
    public static string Merge(string previous, string next)
    {
        if (string.IsNullOrWhiteSpace(previous)) return next ?? "";
        if (string.IsNullOrWhiteSpace(next)) return previous;
        var left = previous.TrimEnd();
        var right = next.TrimStart();
        if (left.Length == 0) return right;
        var last = left[^1];
        var first = right[0];
        var needsSpace = char.IsLetterOrDigit(last) && char.IsLetterOrDigit(first) && first < 128;
        return left + (needsSpace ? " " : "") + right;
    }

    /// <summary>按标点与长度切句；保留标点，超长句强制在空格处断开。</summary>
    public static IReadOnlyList<string> Split(string text, int maxLength = MaxLineLength)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return result;
        var buffer = new StringBuilder();
        foreach (var ch in text)
        {
            buffer.Append(ch);
            var boundary = ch is '。' or '！' or '？' or '!' or '?' or ';' or '；' or '\n';
            if (boundary || buffer.Length >= maxLength)
            {
                var line = buffer.ToString().Trim();
                if (line.Length > 0) result.Add(line);
                buffer.Clear();
            }
        }
        var tail = buffer.ToString().Trim();
        if (tail.Length > 0) result.Add(tail);
        return result;
    }

    /// <summary>取最近 N 行（OBS 文本源只显示少量行，避免挡住画面）。</summary>
    public static string Tail(IEnumerable<string> lines, int maxLines = MaxLines)
    {
        var list = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        return string.Join("\n", list.Skip(Math.Max(0, list.Count - maxLines)));
    }

    /// <summary>字幕节流：300~500 ms 内最多更新一次 OBS 文本源。</summary>
    public sealed class Throttle
    {
        private readonly TimeSpan _interval;
        private readonly Func<DateTimeOffset> _clock;
        private DateTimeOffset _last = DateTimeOffset.MinValue;
        public Throttle(int intervalMs = 400, Func<DateTimeOffset>? clock = null)
        {
            _interval = TimeSpan.FromMilliseconds(Math.Clamp(intervalMs, 300, 500));
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
        }

        public bool TryAcquire()
        {
            var now = _clock();
            if (now - _last < _interval) return false;
            _last = now;
            return true;
        }
    }
}

/// <summary>whisper.cpp 参数构造（S01）：参数数组传参、不经过 shell、路径校验。</summary>
public static class WhisperArgs
{
    public static IReadOnlyList<string> Build(string modelPath, string wavPath, string language = "auto", int threads = 2, bool translate = false)
    {
        var args = new List<string>
        {
            "-m", modelPath,
            "-f", wavPath,
            "-l", string.IsNullOrWhiteSpace(language) ? "auto" : language,
            "-t", Math.Clamp(threads, 1, 16).ToString(CultureInfo.InvariantCulture),
            "-otxt",
            "-nt",
        };
        if (translate) args.Add("-tr");
        return args;
    }

    public static bool IsSafePath(string? path)
        => !string.IsNullOrWhiteSpace(path) && path.IndexOfAny(new[] { '\0', '\r', '\n', '|', '&', '>' }) < 0;

    /// <summary>把 whisper.cpp 的 stdout 行（[00:00:00.000 --&gt; ...] 文本）解析成纯文本。</summary>
    public static string ParseOutput(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout)) return "";
        var lines = new List<string>();
        foreach (var raw in stdout.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            // 去掉时间轴前缀与方括号
            var end = line.IndexOf(']');
            if (line.StartsWith('[') && end > 0) line = line[(end + 1)..].Trim();
            if (line.StartsWith("whisper_", StringComparison.Ordinal)) continue;
            if (line.Length == 0) continue;
            lines.Add(line);
        }
        return string.Join(" ", lines).Trim();
    }
}
