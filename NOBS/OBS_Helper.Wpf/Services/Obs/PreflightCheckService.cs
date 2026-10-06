using System.IO;
using OBS_Helper.Wpf.Services.ObsConfig;

namespace OBS_Helper.Wpf.Services.Obs;

/// <summary>
/// 录前 / 开播前自检服务（C1，只读）。
///
/// 包装 <see cref="PreflightCheckCore"/> 的纯检查逻辑：
/// 负责 OBS 配置目录定位、global.ini → 当前 Profile → basic.ini 的读取链路，
/// 以及「OBS 是否在运行」的环境信息项。所有磁盘操作均为只读。
/// </summary>
public sealed class PreflightCheckService
{
    private readonly ObsPathService _paths;

    public PreflightCheckService(ObsPathService paths) => _paths = paths;

    public async Task<PreflightReport> RunAsync()
    {
        var report = new PreflightReport();
        try
        {
            var loc = await _paths.LocateAsync().ConfigureAwait(false);

            string? globalIniText = null;
            string? basicIniText = null;
            if (loc.Exists)
            {
                globalIniText = TryRead(Path.Combine(loc.ConfigDir, "global.ini"));
                var profileDir = ReadProfileDir(globalIniText);
                if (!string.IsNullOrWhiteSpace(profileDir))
                {
                    var profileRoot = Path.Combine(loc.ConfigDir, "basic", "profiles", profileDir!);
                    basicIniText = TryRead(Path.Combine(profileRoot, "basic.ini"));
                }
            }

            // 环境信息：OBS 运行状态（提示级，不算问题）
            try
            {
                var proc = _paths.DetectProcess();
                report.Items.Add(new PreflightItem
                {
                    Title = Strings.T("preflight.service.obsProcessTitle"),
                    Status = PreflightStatus.Info,
                    Detail = proc.IsRunning
                        ? Strings.T("preflight.service.obsRunning", proc.Evidence)
                        : Strings.T("preflight.service.obsNotRunning")
                });
            }
            catch (Exception) { }

            Dictionary<string, string>? globalIni = null;
            if (globalIniText is not null) globalIni = PreflightCheckCore.ParseIni(globalIniText);

            PreflightCheckCore.Run(report, loc.Exists, globalIni, basicIniText);
        }
        catch (Exception ex)
        {
            report.Items.Add(new PreflightItem
            {
                Title = Strings.T("preflight.service.title"),
                Status = PreflightStatus.Fail,
                Detail = Strings.T("preflight.service.exception", ex.Message)
            });
        }

        return report;
    }

    /// <summary>从 global.ini 内容里取 [Basic] ProfileDir。</summary>
    private static string? ReadProfileDir(string? globalIniText)
    {
        if (string.IsNullOrEmpty(globalIniText)) return null;
        return PreflightCheckCore.ParseIni(globalIniText).TryGetValue("basic.profiledir", out var dir)
            ? dir
            : null;
    }

    /// <summary>
    /// 读取「本机事实」（V3.0 / D4）：编码器、视频码率、音频采样率。
    ///
    /// 与 <see cref="RunAsync"/> 走同一条只读链路（定位 → global.ini → 当前 Profile 的 basic.ini），
    /// 但返回**原始值**而不是本地化文案 —— 诊断页的清单自动回填要拿这些值做判定，
    /// 而「拿文案反推结论」既脆弱又没法单测。
    /// 读不到任何一项时返回 null（= 无法判定），绝不猜。
    /// </summary>
    public async Task<PreflightFacts?> ReadFactsAsync()
    {
        try
        {
            var loc = await _paths.LocateAsync().ConfigureAwait(false);
            if (!loc.Exists) return null;

            var globalIniText = TryRead(Path.Combine(loc.ConfigDir, "global.ini"));
            var profileDir = ReadProfileDir(globalIniText);
            if (string.IsNullOrWhiteSpace(profileDir)) return null;

            var basicIniText = TryRead(Path.Combine(loc.ConfigDir, "basic", "profiles", profileDir!, "basic.ini"));
            if (string.IsNullOrWhiteSpace(basicIniText)) return null;

            var ini = PreflightCheckCore.ParseIni(basicIniText);

            return new PreflightFacts
            {
                ConfigDir = loc.ConfigDir,
                ProfileDir = profileDir,
                EncoderNames = ReadEncoders(ini),
                VideoBitrateKbps = ReadBitrate(ini),
                AudioSampleRateHz = ReadSampleRate(ini),
                RecordingFormat = ReadRecordingFormat(ini),
            };
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Preflight", $"读取本机事实失败：{ex.Message}");
            return null;
        }
    }

    /// <summary>与 <see cref="PreflightCheckCore"/> 同口径的编码器键（简单模式 / 高级模式）。</summary>
    private static IReadOnlyList<string> ReadEncoders(Dictionary<string, string> ini)
        => ini.Where(kv =>
                kv.Key.EndsWith(".streamencoder", StringComparison.OrdinalIgnoreCase) ||
                kv.Key.EndsWith(".recencoder", StringComparison.OrdinalIgnoreCase) ||
                kv.Key.EndsWith(".encoder", StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Value.Trim())
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>视频码率（kbps）：简单模式 <c>VBitrate</c>，高级模式 <c>bitrate</c>。</summary>
    private static int? ReadBitrate(Dictionary<string, string> ini)
    {
        foreach (var kv in ini)
        {
            if (!kv.Key.EndsWith(".vbitrate", StringComparison.OrdinalIgnoreCase) &&
                !kv.Key.EndsWith(".bitrate", StringComparison.OrdinalIgnoreCase))
                continue;
            if (int.TryParse(kv.Value.Trim(), out var kbps) && kbps > 0) return kbps;
        }
        return null;
    }

    private static int? ReadSampleRate(Dictionary<string, string> ini)
        => ini.TryGetValue("audio.samplerate", out var raw) && int.TryParse(raw.Trim(), out var rate) && rate > 0
            ? rate
            : null;

    /// <summary>录制格式（简单模式 <c>RecFormat</c> / 高级模式 <c>RecFormat</c> 或 <c>Format</c>）。</summary>
    private static string? ReadRecordingFormat(Dictionary<string, string> ini)
    {
        foreach (var kv in ini)
        {
            if (!kv.Key.EndsWith(".recformat", StringComparison.OrdinalIgnoreCase) &&
                !kv.Key.EndsWith(".format", StringComparison.OrdinalIgnoreCase))
                continue;
            var value = kv.Value.Trim();
            if (value.Length > 0) return value;
        }
        return null;
    }

    private static string? TryRead(string file)
    {
        try { return File.Exists(file) ? File.ReadAllText(file) : null; }
        catch (Exception) { return null; }
    }
}

/// <summary>
/// 从 basic.ini 读到的「本机事实」（V3.0 / D4）。任意字段读不到即为 null。
/// </summary>
public sealed class PreflightFacts
{
    public string ConfigDir { get; init; } = "";
    public string ProfileDir { get; init; } = "";
    public IReadOnlyList<string> EncoderNames { get; init; } = Array.Empty<string>();
    public int? VideoBitrateKbps { get; init; }
    public int? AudioSampleRateHz { get; init; }

    /// <summary>录制格式（<c>mkv</c> / <c>mp4</c> / <c>fragmented_mp4</c> …）；读不到为 null。</summary>
    public string? RecordingFormat { get; init; }
}
