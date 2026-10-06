using System.Text;
using OBS_Helper.Wpf.Localization;
using OBS_Helper.Wpf.Services.ObsConfig;

namespace OBS_Helper.Wpf.Services.Recording;

/// <summary>录制中的一次「打点」（V3.0 / D3）：位置 + 可选标签。</summary>
public sealed record RecordingMarker(TimeSpan At, string Label);

/// <summary>录制文件的健康度（用来一眼看出「哪次是空文件」）。</summary>
public enum RecordingHealth
{
    /// <summary>看着正常。</summary>
    Ok,
    /// <summary>文件过小：很可能是刚开始就停了（编码器没起来 / 立刻中断）。</summary>
    Tiny,
    /// <summary>0 字节：录了个空。</summary>
    Empty,
    /// <summary>文件已经不在磁盘上了（被移动 / 删除 / 换了盘）。</summary>
    Missing
}

/// <summary>
/// 一条录制档案（V3.0 / D3）。
///
/// 现状问题（提案原文）：录制产物只留一个**单值** <c>LastRecordFile</c>，下一轮即被覆盖 ——
/// 没有历史、没有清单，用户想知道「刚才那次录了多久、有多大、是不是空的」只能自己去翻目录。
/// </summary>
public sealed record RecordingArchiveEntry(
    /// <summary>成品的完整路径。</summary>
    string Path,
    /// <summary>本次录制开始的本地时刻。</summary>
    DateTime StartedLocal,
    /// <summary>时长（优先用 OBS 自报 timecode，暂停不计入）。</summary>
    TimeSpan Duration,
    /// <summary>文件大小（字节）。</summary>
    long Bytes,
    /// <summary>本次录制的丢帧率（0~1）。</summary>
    double DroppedRatio,
    /// <summary>是否是自动分段产生的分片。</summary>
    bool Segmented,
    /// <summary>录制中打的点。</summary>
    IReadOnlyList<RecordingMarker> Markers)
{
    public string FileName => System.IO.Path.GetFileName(Path);

    public double SizeMb => Bytes / 1024.0 / 1024.0;

    /// <summary>按大小与时长给出的健康度（<paramref name="exists"/> 由调用方给出，便于测试）。</summary>
    public RecordingHealth Health(bool exists) => RecordingArchiveCore.EvaluateHealth(exists, Bytes, Duration);
}

/// <summary>重命名规则（V3.0 / D3）：哪些信息进文件名。</summary>
public sealed record RenameRule(
    /// <summary>前缀（常用「频道名」）。</summary>
    string Prefix = "",
    bool IncludeDate = true,
    bool IncludeTime = false,
    /// <summary>把当时的场景名拼进去。</summary>
    string Scene = "",
    /// <summary>把预设名拼进去（简单录像的 quick/meeting/game）。</summary>
    string PresetLabel = "");

/// <summary>
/// 录制档案与收尾流水线的纯逻辑（V3.0 / D3）。零 WPF / 零 IO 依赖，单测工程直接链接编译。
///
/// 这里只做「算什么」：健康度判定、重命名目标名、章节文件内容、打点合并。
/// 真正的文件操作在 <see cref="RecordingArchiveService"/> 里。
/// </summary>
public static class RecordingArchiveCore
{
    /// <summary>小于这个大小且时长很短，认为「很可能没真录上」。</summary>
    public const long TinyBytesThreshold = 200 * 1024;

    /// <summary>两次打点间隔小于这个秒数视为误触（合并为一次）。</summary>
    public const double MarkerDedupeSeconds = 2;

    /// <summary>单个文件的健康度。0 字节与「过小且很短」分别给不同结论 —— 排查方向不同。</summary>
    public static RecordingHealth EvaluateHealth(bool exists, long bytes, TimeSpan duration)
    {
        if (!exists) return RecordingHealth.Missing;
        if (bytes <= 0) return RecordingHealth.Empty;
        // 只有「既小又短」才可疑：一段 1 秒的 4K 也可能上百 KB，不能只看大小
        if (bytes < TinyBytesThreshold && duration.TotalSeconds < 10) return RecordingHealth.Tiny;
        return RecordingHealth.Ok;
    }

    /// <summary>
    /// 把一次打点并入列表：按位置排序，且**合并 2 秒内的重复**。
    ///
    /// 为什么要去重：热键很容易连按两下，而生成的章节里出现两条相邻 0.2 秒的条目毫无意义。
    /// </summary>
    public static IReadOnlyList<RecordingMarker> AddMarker(
        IReadOnlyList<RecordingMarker>? existing, TimeSpan at, string? label = null)
    {
        var list = existing is null ? new List<RecordingMarker>() : existing.ToList();
        var position = at < TimeSpan.Zero ? TimeSpan.Zero : at;
        var text = label?.Trim() ?? "";

        if (list.Any(m => Math.Abs((m.At - position).TotalSeconds) < MarkerDedupeSeconds))
            return list;   // 误触：原样返回（保持调用方状态不变）

        list.Add(new RecordingMarker(position, text));
        return list.OrderBy(m => m.At).ToList();
    }

    /// <summary>
    /// 生成重命名后的文件名（不含目录）。
    ///
    /// 规则：<c>前缀_日期_时间_场景_预设.ext</c>，只拼用户勾选的部分；
    /// 非法字符统一换成下划线并压缩连续分隔符 —— 文件名里出现 <c>:</c> 或 <c>/</c> 会直接让重命名失败。
    /// </summary>
    public static string BuildNewFileName(RecordingArchiveEntry entry, RenameRule rule)
    {
        var extension = System.IO.Path.GetExtension(entry.Path);
        if (string.IsNullOrEmpty(extension)) extension = ".mkv";

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(rule.Prefix)) parts.Add(rule.Prefix);
        if (rule.IncludeDate) parts.Add(entry.StartedLocal.ToString("yyyyMMdd"));
        if (rule.IncludeTime) parts.Add(entry.StartedLocal.ToString("HHmmss"));
        if (!string.IsNullOrWhiteSpace(rule.Scene)) parts.Add(rule.Scene);
        if (!string.IsNullOrWhiteSpace(rule.PresetLabel)) parts.Add(rule.PresetLabel);

        if (parts.Count == 0) parts.Add(System.IO.Path.GetFileNameWithoutExtension(entry.Path));

        var name = string.Join("_", parts.Select(Sanitize));
        name = CollapseSeparators(name);
        if (name.Length == 0) name = "recording";
        return name + extension;
    }

    /// <summary>清掉文件名里不能用的字符（含 Windows 保留的 <c>:*?"&lt;&gt;|</c>）。</summary>
    public static string Sanitize(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw.Trim())
            sb.Append(invalid.Contains(ch) ? '_' : ch);
        return sb.ToString().Trim(' ', '.');
    }

    /// <summary>把连续的下划线/空格压成一个（「A__B」→「A_B」）。</summary>
    private static string CollapseSeparators(string name)
    {
        var sb = new StringBuilder(name.Length);
        var lastWasSeparator = false;
        foreach (var ch in name)
        {
            var isSeparator = ch is '_' or ' ' or '-';
            if (isSeparator)
            {
                if (lastWasSeparator) continue;
                lastWasSeparator = true;
            }
            else
            {
                lastWasSeparator = false;
            }
            sb.Append(ch);
        }
        return sb.ToString().Trim('_', ' ', '-');
    }

    /// <summary>
    /// 生成 **ffmpeg 元数据**格式的章节文件（<c>.ffmetadata</c>）。
    ///
    /// 这是「给成品文件写章节」最通用的做法：转 MP4 时用
    /// <c>ffmpeg -i in -i meta -map_metadata 1 -c copy out.mp4</c> 即可带上章节。
    /// 官方 OBS 至今不提供录制打点（issue #13567），这条路正好补上那个洞。
    /// </summary>
    public static string BuildFfMetadata(IReadOnlyList<RecordingMarker> markers, TimeSpan duration, string title)
    {
        var sb = new StringBuilder();
        sb.AppendLine(";FFMETADATA1");
        sb.AppendLine("title=" + EscapeMetadata(title));

        for (var i = 0; i < markers.Count; i++)
            AppendChapter(sb, markers, i, duration);

        // 一个打点都没有：不生成空章节（免得 ffmpeg 报错或产出 0 长度章节）
        return markers.Count == 0 ? "" : sb.ToString();
    }

    private static void AppendChapter(StringBuilder sb, IReadOnlyList<RecordingMarker> markers, int index, TimeSpan duration)
    {
        var start = markers[index].At;
        // 章节终点 = 下一个打点；最后一个到录制结束。若显式时长不可用，给 1 小时上限兜底。
        var end = index + 1 < markers.Count ? markers[index + 1].At : (duration > start ? duration : start + TimeSpan.FromHours(1));

        sb.AppendLine("[CHAPTER]");
        sb.AppendLine("TIMEBASE=1/1000");
        sb.AppendLine("START=" + (long)start.TotalMilliseconds);
        sb.AppendLine("END=" + (long)end.TotalMilliseconds);
        sb.AppendLine("title=" + EscapeMetadata(markers[index].Label.Length > 0
            ? markers[index].Label
            : Strings.T("d3.marker.defaultLabel", index + 1)));
    }

    /// <summary>ffmpeg 元数据里的转义：<c>\</c> 与换行必须转义，否则整份文件解析失败。</summary>
    private static string EscapeMetadata(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        return raw.Replace("\\", "\\\\").Replace("\r", "").Replace("\n", "\\\n");
    }

    /// <summary>
    /// 生成人类可读的章节清单（<c>00:00 开场</c> 这种，B 站/YouTube 的简介直接粘贴）。
    ///
    /// 与 ffmetadata 并存的原因：前者给播放器/转码用，后者给用户**贴到视频简介**用 ——
    /// 两件事，两个文件，都不要猜。
    /// </summary>
    public static string BuildChapterList(IReadOnlyList<RecordingMarker> markers, string title)
    {
        if (markers.Count == 0) return "";
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(title)) sb.AppendLine(title);
        foreach (var m in markers)
            sb.AppendLine($"{SimpleRecordingCore.FormatDuration(m.At)} {m.Label}".TrimEnd());
        return sb.ToString();
    }

    /// <summary>章节文件的落点：与视频同目录、同名（<c>xxx.chapters.txt</c> / <c>xxx.ffmetadata</c>）。</summary>
    public static string ChapterPathFor(string videoPath, bool ffmpegFormat)
    {
        var dir = System.IO.Path.GetDirectoryName(videoPath) ?? "";
        var baseName = System.IO.Path.GetFileNameWithoutExtension(videoPath);
        return System.IO.Path.Combine(dir, baseName + (ffmpegFormat ? ".ffmetadata" : ".chapters.txt"));
    }

    /// <summary>给列表/明细用的一行摘要，例如「2.4 GB · 12:34 · 丢帧 0.3% · 3 个打点」。</summary>
    public static string Describe(RecordingArchiveEntry entry, RecordingHealth health)
    {
        var parts = new List<string> { FormatSize(entry.Bytes) };
        if (entry.Duration > TimeSpan.Zero) parts.Add(SimpleRecordingCore.FormatDuration(entry.Duration));
        if (entry.DroppedRatio > 0.001) parts.Add(Strings.T("d3.dropped", (entry.DroppedRatio * 100).ToString("0.#")));
        if (entry.Segmented) parts.Add(Strings.T("d3.segmented"));
        if (entry.Markers.Count > 0) parts.Add(Strings.T("d3.markerCount", entry.Markers.Count));
        if (health != RecordingHealth.Ok) parts.Add(HealthText(health));
        return string.Join(" · ", parts);
    }

    /// <summary>健康度的用户可读文案。</summary>
    public static string HealthText(RecordingHealth health) => health switch
    {
        RecordingHealth.Empty => Strings.T("d3.health.empty"),
        RecordingHealth.Tiny => Strings.T("d3.health.tiny"),
        RecordingHealth.Missing => Strings.T("d3.health.missing"),
        _ => Strings.T("d3.health.ok")
    };

    /// <summary>时长的展示格式（复用录制那边的口径，避免三处各写一套）。</summary>
    public static string FormatDuration(TimeSpan t) => SimpleRecordingCore.FormatDuration(t);

    public static string FormatSize(long bytes)
        => bytes >= 1024L * 1024 * 1024 ? $"{bytes / 1024.0 / 1024 / 1024:0.##} GB"
         : bytes >= 1024 * 1024 ? $"{bytes / 1024.0 / 1024:0.#} MB"
         : $"{bytes / 1024.0:0.#} KB";
}
