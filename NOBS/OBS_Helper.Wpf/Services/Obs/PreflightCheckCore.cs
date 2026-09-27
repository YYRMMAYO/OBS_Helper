using OBS_Helper.Wpf.Localization;
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
        PreflightStatus.Ok => Strings.T("preflight.status.ok"),
        PreflightStatus.Warn => Strings.T("preflight.status.warn"),
        PreflightStatus.Fail => Strings.T("preflight.status.fail"),
        _ => Strings.T("preflight.status.info")
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
                Title = Strings.T("preflight.noConfig.title"),
                Status = PreflightStatus.Fail,
                Detail = Strings.T("preflight.noConfig.detail")
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
                Title = Strings.T("preflight.noProfile.title"),
                Status = PreflightStatus.Warn,
                Detail = Strings.T("preflight.noProfile.detail")
            });
            return;
        }

        var ini = ParseIni(basicIniText ?? "");
        if (ini.Count == 0)
        {
            report.Items.Add(new PreflightItem
            {
                Title = Strings.T("preflight.noBasicIni.title", profileDir),
                Status = PreflightStatus.Warn,
                Detail = Strings.T("preflight.noBasicIni.detail")
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
                Title = Strings.T("preflight.recFormat.title"),
                Status = PreflightStatus.Ok,
                Detail = string.IsNullOrEmpty(format)
                    ? Strings.T("preflight.recFormat.okDefault")
                    : Strings.T("preflight.recFormat.okCurrent", format)
            });
            return;
        }

        report.Items.Add(new PreflightItem
        {
            Title = Strings.T("preflight.recFormat.title"),
            Status = PreflightStatus.Warn,
            Detail = Strings.T("preflight.recFormat.warn", format),
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
                Title = Strings.T("preflight.recPath.title"),
                Status = PreflightStatus.Info,
                Detail = Strings.T("preflight.recPath.default")
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
                    Title = Strings.T("preflight.recPath.title"),
                    Status = PreflightStatus.Fail,
                    Detail = Strings.T("preflight.recPath.missing", path),
                    ProblemId = "rc-nofile"
                });
                return;
            }

            var free = freeBytesOf(Path.GetPathRoot(Path.GetFullPath(dir)) ?? dir);
            if (free is > 0 && free < MinFreeBytes)
            {
                report.Items.Add(new PreflightItem
                {
                    Title = Strings.T("preflight.recPath.lowSpaceTitle"),
                    Status = PreflightStatus.Warn,
                    Detail = Strings.T("preflight.recPath.lowSpace", free / 1024.0 / 1024 / 1024),
                    ProblemId = "rc-disk-space"
                });
                return;
            }

            var freeText = free is > 0 ? Strings.T("preflight.recPath.freeSuffix", free / 1024.0 / 1024 / 1024) : "";
            report.Items.Add(new PreflightItem
            {
                Title = Strings.T("preflight.recPath.title"),
                Status = PreflightStatus.Ok,
                Detail = Strings.T("preflight.recPath.ok", path, freeText)
            });
        }
        catch (Exception ex)
        {
            report.Items.Add(new PreflightItem
            {
                Title = Strings.T("preflight.recPath.title"),
                Status = PreflightStatus.Info,
                Detail = Strings.T("preflight.recPath.unknown", ex.Message)
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
                Title = Strings.T("preflight.encoder.noneTitle"),
                Status = PreflightStatus.Info,
                Detail = Strings.T("preflight.encoder.none")
            });
            return;
        }

        var software = encoders.FirstOrDefault(IsSoftwareEncoder);
        if (software is not null)
        {
            report.Items.Add(new PreflightItem
            {
                Title = Strings.T("preflight.encoder.title"),
                Status = PreflightStatus.Warn,
                Detail = Strings.T("preflight.encoder.software", software),
                ProblemId = "enc-overload"
            });
            return;
        }

        report.Items.Add(new PreflightItem
        {
            Title = Strings.T("preflight.encoder.title"),
            Status = PreflightStatus.Ok,
            Detail = Strings.T("preflight.encoder.hardware", string.Join(" / ", encoders))
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
                Title = Strings.T("preflight.sampleRate.title"),
                Status = PreflightStatus.Info,
                Detail = Strings.T("preflight.sampleRate.none")
            });
            return;
        }

        if (rate == 48000)
        {
            report.Items.Add(new PreflightItem
            {
                Title = Strings.T("preflight.sampleRate.title"),
                Status = PreflightStatus.Ok,
                Detail = Strings.T("preflight.sampleRate.ok")
            });
            return;
        }

        report.Items.Add(new PreflightItem
        {
            Title = Strings.T("preflight.sampleRate.title"),
            Status = PreflightStatus.Warn,
            Detail = Strings.T("preflight.sampleRate.warn", rate),
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
                Title = Strings.T("preflight.mic.title"),
                Status = PreflightStatus.Ok,
                Detail = Strings.T("preflight.mic.ok", enabled)
            });
            return;
        }

        report.Items.Add(new PreflightItem
        {
            Title = Strings.T("preflight.mic.title"),
            Status = PreflightStatus.Info,
            Detail = Strings.T("preflight.mic.none"),
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
                Title = Strings.T("preflight.keyint.title"),
                Status = PreflightStatus.Info,
                Detail = Strings.T("preflight.keyint.unknown")
            });
            return;
        }

        if (!int.TryParse(entry.Value, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var sec))
        {
            report.Items.Add(new PreflightItem
            {
                Title = Strings.T("preflight.keyint.title"),
                Status = PreflightStatus.Info,
                Detail = Strings.T("preflight.keyint.notNumber", entry.Value)
            });
            return;
        }

        if (sec > 0 && sec <= 4)
        {
            report.Items.Add(new PreflightItem
            {
                Title = Strings.T("preflight.keyint.title"),
                Status = PreflightStatus.Ok,
                Detail = Strings.T("preflight.keyint.ok", sec)
            });
            return;
        }

        if (sec == 0)
        {
            report.Items.Add(new PreflightItem
            {
                Title = Strings.T("preflight.keyint.autoTitle"),
                Status = PreflightStatus.Warn,
                Detail = Strings.T("preflight.keyint.autoDetail"),
                ProblemId = "lag-keyint"
            });
            return;
        }

        report.Items.Add(new PreflightItem
        {
            Title = Strings.T("preflight.keyint.largeTitle"),
            Status = PreflightStatus.Warn,
            Detail = Strings.T("preflight.keyint.largeDetail", sec),
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