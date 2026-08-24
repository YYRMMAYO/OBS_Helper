using System.Globalization;
using System.IO;

namespace OBS_Helper.Wpf.Services.Obs;

/// <summary>录前自检单项状态。</summary>
public enum PreflightStatus
{
    Ok,
    Warn,
    Fail,
    Info
}

/// <summary>录前自检一项结果。</summary>
public sealed class PreflightItem
{
    public required string Title { get; init; }
    public required PreflightStatus Status { get; init; }
    public string Detail { get; init; } = "";
    /// <summary>命中问题时关联的知识库条目 id，便于一键跳转分步方案。</summary>
    public string? ProblemId { get; init; }

    public string StatusText => Status switch
    {
        PreflightStatus.Ok => "通过",
        PreflightStatus.Warn => "建议",
        PreflightStatus.Fail => "未通过",
        _ => "提示"
    };
}

/// <summary>一次录前自检的完整报告。</summary>
public sealed class PreflightReport
{
    public List<PreflightItem> Items { get; } = new();
    public DateTime CheckedAt { get; set; } = DateTime.Now;

    public int WarnCount => Items.Count(i => i.Status == PreflightStatus.Warn);
    public int FailCount => Items.Count(i => i.Status == PreflightStatus.Fail);
}

/// <summary>
/// 录前 / 开播前自检核心（只读，纯逻辑，供单元测试）。
///
/// 只读检查当前 Profile 的 basic.ini 与录制目录：
/// 录制格式是否防崩溃的 MKV、录制路径是否有效、所在盘剩余空间、
/// 是否还在用 x264 软件编码、音频采样率与麦克风设备配置。
/// 绝不修改任何文件；任何探测失败都降级为「未通过」项而不是抛异常。
/// </summary>
public static class PreflightCheckCore
{
    private const long MinFreeBytes = 10L * 1024 * 1024 * 1024; // 10 GB

    /// <summary>
    /// 极简 INI 解析：返回 "section.key"（小写）→ 原始值。
    /// OBS 的 basic.ini / global.ini 都是标准 INI，无需处理转义。
    /// </summary>
    internal static Dictionary<string, string> ParseIni(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(text)) return result;

        var section = "";
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key = $"{section}.{line[..eq].Trim()}".ToLowerInvariant();
            var value = line[(eq + 1)..].Trim();
            result[key] = value;
        }
        return result;
    }

    /// <summary>执行全部检查。参数均可为 null（对应文件 / 目录缺失的场景）。</summary>
    public static void Run(
        PreflightReport report,
        bool configDirExists,
        Dictionary<string, string>? globalIni,
        string? basicIniText,
        Func<string, long>? freeBytesOf = null)
    {
        if (!configDirExists)
        {
            report.Items.Add(new PreflightItem
            {
                Title = "OBS 配置目录",
                Status = PreflightStatus.Fail,
                Detail = "未找到 OBS 配置目录。若为自定义安装，请先在「设置 → OBS 配置管理」手动指定目录后重试。"
            });
            return;
        }

        var profileDir = globalIni is not null &&
                         globalIni.TryGetValue("basic.profiledir", out var pd)
            ? pd
            : null;

        if (string.IsNullOrWhiteSpace(profileDir))
        {
            report.Items.Add(new PreflightItem
            {
                Title = "当前 Profile",
                Status = PreflightStatus.Warn,
                Detail = "global.ini 中没有 Profile 记录（OBS 可能从未保存过设置），无法读取输出配置；先在 OBS 里随便改一项设置并关闭，即可生成。"
            });
            return;
        }

        var ini = ParseIni(basicIniText ?? "");
        if (ini.Count == 0)
        {
            report.Items.Add(new PreflightItem
            {
                Title = $"Profile「{profileDir}」的 basic.ini",
                Status = PreflightStatus.Warn,
                Detail = "找不到或读不了该 Profile 的 basic.ini，以下输出相关检查跳过。"
            });
            return;
        }

        CheckRecordingFormat(report, ini);
        CheckRecordingPath(report, ini, freeBytesOf ?? DefaultFreeBytes);
        CheckEncoder(report, ini);
        CheckSampleRate(report, ini);
        CheckMicDevices(report, ini);
        CheckKeyframeInterval(report, ini);
    }

    // ------------------------------------------------------------- 各项检查

    private static void CheckRecordingFormat(PreflightReport report, Dictionary<string, string> ini)
    {
        var format = FirstValue(ini,
            "simpleoutput.recformat2", "advout.recformat2",
            "simpleoutput.recformat", "advout.recformat");

        // OBS 默认即 MKV；键缺失按默认处理，不吓唬用户
        if (format.Length == 0 || format.StartsWith("mkv", StringComparison.OrdinalIgnoreCase)
                               || format.Contains("hybrid", StringComparison.OrdinalIgnoreCase))
        {
            report.Items.Add(new PreflightItem
            {
                Title = "录制格式（防崩溃）",
                Status = PreflightStatus.Ok,
                Detail = string.IsNullOrEmpty(format)
                    ? "使用默认 MKV（崩溃 / 断电时已写入部分可保留）。"
                    : $"当前 {format}，崩溃或断电时已写入部分可保留。"
            });
            return;
        }

        report.Items.Add(new PreflightItem
        {
            Title = "录制格式（防崩溃）",
            Status = PreflightStatus.Warn,
            Detail = $"当前为 {format}：直接录 MP4 等格式一旦崩溃整个文件报废。建议改为 MKV 录制，录完再用「文件 → 录像转封装」转 MP4。",
            ProblemId = "rc-mkv"
        });
    }

    private static void CheckRecordingPath(PreflightReport report, Dictionary<string, string> ini,
        Func<string, long> freeBytesOf)
    {
        var path = FirstValue(ini, "advout.recfilepath", "simpleoutput.filepath");
        if (path.Length == 0)
        {
            report.Items.Add(new PreflightItem
            {
                Title = "录制保存路径",
                Status = PreflightStatus.Info,
                Detail = "未在配置中找到自定义录制路径，将使用系统「视频」文件夹。"
            });
            return;
        }

        try
        {
            var dir = Path.Combine(path, ""); // 规整尾随分隔符
            if (!Directory.Exists(dir))
            {
                report.Items.Add(new PreflightItem
                {
                    Title = "录制保存路径",
                    Status = PreflightStatus.Fail,
                    Detail = $"配置的路径不存在：{path}。开播后录制会直接失败，请在 设置 → 输出 → 录像 中重新选择。",
                    ProblemId = "rc-nofile"
                });
                return;
            }

            var free = freeBytesOf(Path.GetPathRoot(Path.GetFullPath(dir)) ?? dir);
            if (free is > 0 && free < MinFreeBytes)
            {
                report.Items.Add(new PreflightItem
                {
                    Title = "录制盘剩余空间",
                    Status = PreflightStatus.Warn,
                    Detail = $"录制路径所在盘仅剩约 {free / 1024.0 / 1024 / 1024:0.#}GB（不足 10GB），长录制可能中途写满导致文件损坏，建议清理或换盘。",
                    ProblemId = "rc-disk-space"
                });
                return;
            }

            var freeText = free is > 0 ? $"，剩余约 {free / 1024.0 / 1024 / 1024:0}GB" : "";
            report.Items.Add(new PreflightItem
            {
                Title = "录制保存路径",
                Status = PreflightStatus.Ok,
                Detail = $"路径有效：{path}{freeText}。"
            });
        }
        catch (Exception ex)
        {
            report.Items.Add(new PreflightItem
            {
                Title = "录制保存路径",
                Status = PreflightStatus.Info,
                Detail = $"路径状态无法确认（{ex.Message}），请自行核对 设置 → 输出 → 录像 的保存位置。"
            });
        }
    }

    private static void CheckEncoder(PreflightReport report, Dictionary<string, string> ini)
    {
        // 简单模式 [SimpleOutput] StreamEncoder / RecEncoder；高级模式 [AdvOut] Encoder / RecEncoder 等
        var encoders = ini.Where(kv =>
                kv.Key.EndsWith(".streamencoder", StringComparison.OrdinalIgnoreCase) ||
                kv.Key.EndsWith(".recencoder", StringComparison.OrdinalIgnoreCase) ||
                kv.Key.EndsWith(".encoder", StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Value.Trim())
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (encoders.Count == 0)
        {
            report.Items.Add(new PreflightItem
            {
                Title = "编码器",
                Status = PreflightStatus.Info,
                Detail = "配置中未找到编码器记录，无法判断。"
            });
            return;
        }

        var software = encoders.FirstOrDefault(IsSoftwareEncoder);
        if (software is not null)
        {
            report.Items.Add(new PreflightItem
            {
                Title = "编码器（硬件优先）",
                Status = PreflightStatus.Warn,
                Detail = $"检测到仍在使用软件编码（{software}）：CPU 负载高、易编码过载掉帧。有独显建议切 NVENC / AMF，核显可用 QSV（设置 → 输出 → 编码器）。",
                ProblemId = "enc-overload"
            });
            return;
        }

        report.Items.Add(new PreflightItem
        {
            Title = "编码器（硬件优先）",
            Status = PreflightStatus.Ok,
            Detail = $"使用硬件编码（{string.Join(" / ", encoders)}），CPU 压力小。"
        });
    }

    private static bool IsSoftwareEncoder(string value)
        => value.Equals("obs_x264", StringComparison.OrdinalIgnoreCase)
        || value.Equals("x264", StringComparison.OrdinalIgnoreCase)
        || value.Contains("264", StringComparison.OrdinalIgnoreCase) && !value.Contains("nvenc", StringComparison.OrdinalIgnoreCase);

    private static void CheckSampleRate(PreflightReport report, Dictionary<string, string> ini)
    {
        if (!ini.TryGetValue("audio.samplerate", out var raw) ||
            !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rate))
        {
            report.Items.Add(new PreflightItem
            {
                Title = "音频采样率",
                Status = PreflightStatus.Info,
                Detail = "配置中未记录采样率（默认 48kHz）。"
            });
            return;
        }

        if (rate == 48000)
        {
            report.Items.Add(new PreflightItem
            {
                Title = "音频采样率",
                Status = PreflightStatus.Ok,
                Detail = "48kHz，与绝大多数设备一致。"
            });
            return;
        }

        report.Items.Add(new PreflightItem
        {
            Title = "音频采样率",
            Status = PreflightStatus.Warn,
            Detail = $"当前 {rate}Hz：与其他设备混用 44.1k/48k 是爆音与音画漂移的典型根因，建议统一为 48kHz（设置 → 音频 → 采样率）。",
            ProblemId = "av-sample"
        });
    }

    private static void CheckMicDevices(PreflightReport report, Dictionary<string, string> ini)
    {
        var deviceKeys = new[]
        {
            "audio.micdevice", "audio.auxdevice1", "audio.auxdevice2",
            "audio.auxdevice3", "audio.auxdevice4"
        };

        var enabled = deviceKeys
            .Select(k => ini.TryGetValue(k, out var v) ? v.Trim() : "")
            .Where(v => v.Length > 0 && !v.Equals("disabled", StringComparison.OrdinalIgnoreCase))
            .Count();

        if (enabled > 0)
        {
            report.Items.Add(new PreflightItem
            {
                Title = "麦克风 / 辅助音源",
                Status = PreflightStatus.Ok,
                Detail = $"已在音频设置中启用 {enabled} 个输入设备。"
            });
            return;
        }

        report.Items.Add(new PreflightItem
        {
            Title = "麦克风 / 辅助音源",
            Status = PreflightStatus.Info,
            Detail = "当前未启用任何麦克风 / 辅助输入设备（如为纯录屏场景可忽略）；需要人声时在 设置 → 音频 → 麦克风 选择设备。",
            ProblemId = "au-mic"
        });
    }

    // --------------------------------------------------------------- 工具

    /// <summary>
    /// 关键帧间隔检查（V2.7）：编码器设置里 keyint 为 0（让编码器自行决定）
    /// 会产生分钟级的关键帧间隔，观众中途进入长时间模糊、拖动进度条失灵。
    /// 键缺失按默认 2 秒处理，不制造虚假告警。
    /// </summary>
    private static void CheckKeyframeInterval(PreflightReport report, Dictionary<string, string> ini)
    {
        var entry = ini.FirstOrDefault(kv =>
            kv.Key.EndsWith(".keyint_sec", StringComparison.OrdinalIgnoreCase) ||
            kv.Key.EndsWith(".keyintsec", StringComparison.OrdinalIgnoreCase));

        if (entry.Key is null || entry.Value.Length == 0)
        {
            report.Items.Add(new PreflightItem
            {
                Title = "关键帧间隔",
                Status = PreflightStatus.Info,
                Detail = "未找到自定义记录（推流默认 2 秒）。若直播中观众反馈「中途进入画面模糊」，到编码器高级设置里确认该项。"
            });
            return;
        }

        if (!int.TryParse(entry.Value, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var sec))
        {
            report.Items.Add(new PreflightItem
            {
                Title = "关键帧间隔",
                Status = PreflightStatus.Info,
                Detail = $"读到非数值记录「{entry.Value}」，建议在设置 → 输出里核对一遍。"
            });
            return;
        }

        if (sec > 0 && sec <= 4)
        {
            report.Items.Add(new PreflightItem
            {
                Title = "关键帧间隔",
                Status = PreflightStatus.Ok,
                Detail = $"{sec} 秒，符合平台共识值（2 秒上下）。"
            });
            return;
        }

        if (sec == 0)
        {
            report.Items.Add(new PreflightItem
            {
                Title = "关键帧间隔设为 0（自动）",
                Status = PreflightStatus.Warn,
                Detail = "0 表示交给编码器决定，实际可能几分钟才一个关键帧：观众中途进入会长时间模糊，平台录制 / 拖动进度条也会异常。" +
                         "\n建议：设置 → 输出 → 关键帧间隔固定为 2 秒。",
                ProblemId = "lag-keyint"
            });
            return;
        }

        report.Items.Add(new PreflightItem
        {
            Title = "关键帧间隔偏大",
            Status = PreflightStatus.Warn,
            Detail = $"当前 {sec} 秒：间隔越长，观众中途进入的模糊恢复越慢。" +
                     "\n建议：设置 → 输出 → 关键帧间隔改为 2 秒。",
            ProblemId = "lag-keyint"
        });
    }

    /// <summary>按优先级取第一个非空值（键名已由 ParseIni 小写化）。</summary>
    private static string FirstValue(Dictionary<string, string> ini, params string[] keys)
        => keys.Select(k => ini.TryGetValue(k, out var v) ? v : "").FirstOrDefault(v => v.Length > 0) ?? "";

    private static long DefaultFreeBytes(string root)
    {
        try { return new DriveInfo(root).AvailableFreeSpace; }
        catch (Exception) { return -1; }
    }
}
