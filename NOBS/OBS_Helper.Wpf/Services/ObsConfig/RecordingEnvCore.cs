using System.Text;

namespace OBS_Helper.Wpf.Services.ObsConfig;

/// <summary>一条「推荐设置」要写进去的目标：参数分类 + 参数名 + 目标值。</summary>
public sealed record RecordingEnvTarget(string Category, string Parameter, string Value);

/// <summary>
/// 一键部署录制环境的**推荐项**（纯逻辑）。每一项都给出「当前值 → 推荐值」，
/// 由调用方决定要不要勾选、怎么落地（obs-websocket 或 basic.ini）。
/// </summary>
public sealed class RecordingEnvItem
{
    /// <summary>文案键后缀：<c>env.item.&lt;Key&gt;.label</c> / <c>.reason</c>。</summary>
    public required string Key { get; init; }

    /// <summary>落地时必须走 obs-websocket（文件层写不了，例如音频采样率）。</summary>
    public bool WebSocketOnly { get; init; }

    /// <summary>当前值（读取失败 / 缺省时为空串）。</summary>
    public required string Current { get; init; }

    /// <summary>目标键值对。同一个参数在多个分类下都要写时给多条。</summary>
    public required IReadOnlyList<RecordingEnvTarget> Targets { get; init; }

    /// <summary>给用户看的推荐值（可能是多个键的合成，例如画布 1920x1080）。</summary>
    public required string Recommended { get; init; }

    /// <summary>当前是否已经是推荐值（已经是就不需要改，仍列出来让用户看到现状）。</summary>
    public bool AlreadyOk => string.Equals(Current, Recommended, StringComparison.OrdinalIgnoreCase);
}

/// <summary>读取到的「当前录制环境」快照（缺项用空串表示）。</summary>
public sealed record RecordingEnvSnapshot
{
    /// <summary>输出模式：Simple / Advanced（为空表示未知）。</summary>
    public string OutputMode { get; init; } = "";
    /// <summary>录像格式（简单模式 RecFormat2/RecFormat，高级模式同）。</summary>
    public string RecFormat { get; init; } = "";
    /// <summary>简单模式的录像质量档。</summary>
    public string RecQuality { get; init; } = "";
    /// <summary>录像保存目录。</summary>
    public string RecPath { get; init; } = "";
    public string BaseCx { get; init; } = "";
    public string BaseCy { get; init; } = "";
    public string OutCx { get; init; } = "";
    public string OutCy { get; init; } = "";
    /// <summary>Common FPS Values 档位（30/60）。</summary>
    public string FpsCommon { get; init; } = "";
    /// <summary>音频采样率。</summary>
    public string SampleRate { get; init; } = "";
}

/// <summary>
/// 一键部署录制环境的核心逻辑（V2.9.3）。纯 BCL、零 WPF 依赖，单测工程直接链接编译。
///
/// 设计口径：
/// <list type="bullet">
///   <item><b>只做「录制向」的推荐</b>：不碰推流参数、不碰输出模式（模式一改会连带影响推流，
///     属于用户自己的选择）；每一项都能单独取消。</item>
///   <item><b>双通道</b>：OBS 在跑就走 obs-websocket 的 <c>SetProfileParameter</c>；OBS 没跑
///     就直接改当前配置集下的 <c>basic.ini</c>（先备份）。采样率只有 WebSocket 通道能改，
///     文件层刻意不写（INI 里音频设置的位置随版本变动，赌错就是把用户配置搞乱）。</item>
///   <item><b>可回滚</b>：应用前把每个参数的旧值原样记下来，回滚就是把旧值写回去。</item>
/// </list>
/// </summary>
public static class RecordingEnvCore
{
    /// <summary>崩溃安全的录像容器：录到一半崩溃 / 断电也能救回文件，且主流剪辑软件都认。</summary>
    public const string HybridMp4 = "hybrid_mp4";

    /// <summary>简单模式的录像质量档（「高质量」档，画质与体积的平衡点）。</summary>
    public const string HighQuality = "HQ";

    /// <summary>推荐画布 / 输出分辨率。</summary>
    public const int Width = 1920;
    public const int Height = 1080;

    /// <summary>推荐帧率（录制向；机器吃力时用户可以取消勾选）。</summary>
    public const int Fps = 60;

    /// <summary>推荐音频采样率。</summary>
    public const int SampleRate = 48000;

    /// <summary>构建推荐项清单。<paramref name="recPath"/> 为已经解析好的录像目录。</summary>
    public static List<RecordingEnvItem> Build(RecordingEnvSnapshot s, string recPath)
    {
        var advanced = string.Equals(s.OutputMode, "Advanced", StringComparison.OrdinalIgnoreCase);

        return new List<RecordingEnvItem>
        {
            new()
            {
                Key = "format",
                Current = s.RecFormat,
                Recommended = HybridMp4,
                // 两个分类 + 新旧两代键名都写：OBS 只读当前模式那一份，写多点不会互相打架，
                // 但能覆盖「简单/高级模式切换」与「OBS 30.2 前后参数名变化」两种情形。
                Targets = new[]
                {
                    new RecordingEnvTarget("SimpleOutput", "RecFormat2", HybridMp4),
                    new RecordingEnvTarget("SimpleOutput", "RecFormat", HybridMp4),
                    new RecordingEnvTarget("AdvOut", "RecFormat2", HybridMp4),
                    new RecordingEnvTarget("AdvOut", "RecFormat", HybridMp4),
                },
            },
            new()
            {
                Key = "quality",
                Current = advanced ? "" : s.RecQuality,
                Recommended = HighQuality,
                Targets = new[] { new RecordingEnvTarget("SimpleOutput", "RecQuality", HighQuality) },
            },
            new()
            {
                Key = "path",
                Current = s.RecPath,
                Recommended = recPath,
                Targets = new[]
                {
                    new RecordingEnvTarget("SimpleOutput", "FilePath", recPath),
                    new RecordingEnvTarget("AdvOut", "RecFilePath", recPath),
                },
            },
            new()
            {
                Key = "canvas",
                Current = Join4(s.BaseCx, s.BaseCy, s.OutCx, s.OutCy),
                Recommended = $"{Width}x{Height}",
                Targets = new[]
                {
                    new RecordingEnvTarget("Video", "BaseCX", Width.ToString()),
                    new RecordingEnvTarget("Video", "BaseCY", Height.ToString()),
                    new RecordingEnvTarget("Video", "OutputCX", Width.ToString()),
                    new RecordingEnvTarget("Video", "OutputCY", Height.ToString()),
                },
            },
            new()
            {
                Key = "fps",
                Current = s.FpsCommon,
                Recommended = Fps.ToString(),
                Targets = new[]
                {
                    new RecordingEnvTarget("Video", "FPSCommon", Fps.ToString()),
                    new RecordingEnvTarget("Video", "FPSNum", Fps.ToString()),
                    new RecordingEnvTarget("Video", "FPSDen", "1"),
                },
            },
            new()
            {
                Key = "sampleRate",
                Current = s.SampleRate,
                Recommended = SampleRate.ToString(),
                WebSocketOnly = true,
                Targets = new[] { new RecordingEnvTarget("Audio", "SampleRate", SampleRate.ToString()) },
            },
        };
    }

    /// <summary>把画布四个值拼成 <c>基础x输出</c> 的展示串（缺值用 <c>?</c> 占位）。</summary>
    public static string Join4(string baseCx, string baseCy, string outCx, string outCy)
    {
        var baseRes = Resolution(baseCx, baseCy);
        var outRes = Resolution(outCx, outCy);

        // 只有一边读得到时直接给那一边，不要拼出 ">1920x1080" 这种半截字符串。
        if (baseRes.Length == 0) return outRes;
        if (outRes.Length == 0) return baseRes;
        if (baseRes == outRes) return baseRes;
        return baseRes + ">" + outRes;
    }

    private static string Resolution(string w, string h)
        => string.IsNullOrEmpty(w) || string.IsNullOrEmpty(h) ? "" : w + "x" + h;

    // ------------------------------------------------------------ INI 读写（纯字符串）

    /// <summary>
    /// 在 INI 文本里把 <c>[section]</c> 下的 <paramref name="key"/> 设为 <paramref name="value"/>。
    /// 键不存在就插到该节标题之后；节不存在就追加到文件末尾。
    /// 其余内容**逐字保留**（含原有的行尾风格），避免把用户配置改花。
    /// </summary>
    public static string PatchIni(string? text, string section, string key, string value)
    {
        var lines = SplitKeepingCr(text ?? "");
        var header = "[" + section + "]";
        var headerIdx = -1;

        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Body.Trim().Equals(header, StringComparison.OrdinalIgnoreCase))
            {
                headerIdx = i;
                break;
            }
        }

        if (headerIdx < 0)
        {
            var eol = lines.Count > 0 && lines[^1].Cr.Length > 0 ? lines[^1].Cr : "\r\n";
            if (lines.Count > 0 && lines[^1].Body.Length > 0) lines.Add(new Line("", eol));
            lines.Add(new Line(header, eol));
            lines.Add(new Line(key + "=" + value, ""));
            return Join(lines);
        }

        // 已存在同名键 → 只改值
        for (var i = headerIdx + 1; i < lines.Count; i++)
        {
            var body = lines[i].Body;
            if (body.TrimStart().StartsWith('[')) break;      // 到了下一节
            var eq = body.IndexOf('=');
            if (eq <= 0) continue;
            if (!body[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            lines[i] = new Line(key + "=" + value, lines[i].Cr);
            return Join(lines);
        }

        lines.Insert(headerIdx + 1, new Line(key + "=" + value, lines[headerIdx].Cr));
        return Join(lines);
    }

    /// <summary>读 INI 里某个节下的键值；读不到返回空串。</summary>
    public static string ReadIni(string? text, string section, string key)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var inSection = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
            if (line[0] == '[')
            {
                inSection = line.TrimEnd(']').TrimStart('[').Trim()
                    .Equals(section, StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inSection) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            if (line[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                return line[(eq + 1)..].Trim();
        }
        return "";
    }

    private readonly record struct Line(string Body, string Cr);

    private static List<Line> SplitKeepingCr(string text)
    {
        var result = new List<Line>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;
            var end = i;
            var cr = "";
            if (end > start && text[end - 1] == '\r')
            {
                end--;
                cr = "\r\n";
            }
            else
            {
                cr = "\n";
            }
            result.Add(new Line(text[start..end], cr));
            start = i + 1;
        }
        if (start < text.Length) result.Add(new Line(text[start..], ""));
        return result;
    }

    private static string Join(List<Line> lines)
    {
        var sb = new StringBuilder();
        foreach (var l in lines) sb.Append(l.Body).Append(l.Cr);
        return sb.ToString();
    }
}
