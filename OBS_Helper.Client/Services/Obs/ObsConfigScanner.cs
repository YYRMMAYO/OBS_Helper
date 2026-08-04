using System.Text.Json;
using OBS_Helper.Client.Services.Host;

namespace OBS_Helper.Client.Services.Obs;

/// <summary>OBS 配置文件里发现的一条线索（与 <see cref="LogFinding"/> 同构，便于诊断页统一展示）。</summary>
public sealed class ConfigFinding
{
    public string Code { get; init; } = "";
    public LogSeverity Severity { get; init; }
    public string Title { get; init; } = "";
    public string Detail { get; set; } = "";
    public string Suggestion { get; init; } = "";
    public string? ProblemId { get; init; }
}

/// <summary>一次 OBS 配置扫描的结果。</summary>
public sealed class ObsConfigReport
{
    /// <summary>是否成功读到任何配置（区分「没装 OBS」与「读到了但无问题」）。</summary>
    public bool Available { get; set; }

    /// <summary>扫描到的条目来源说明（用于 UI 展示）。</summary>
    public string Source { get; set; } = "";

    public List<ConfigFinding> Findings { get; set; } = new();

    public bool HasIssues => Findings.Count > 0;
}

/// <summary>
/// OBS 配置文件扫描器（方向 B）。
///
/// 宿主已开放 <c>config.list</c> / <c>config.read</c>，限定在 <c>%AppData%/obs-studio</c> 内，
/// 因此这里读取的是用户本机真实配置而非日志反推：
/// <list type="bullet">
///   <item>根 <c>basic.ini</c> 与每个 <c>profiles/&lt;名称&gt;/basic.ini</c>：录制格式、音频采样率、编码器、码率；</item>
///   <item>每个 <c>sceneCollections/&lt;名称&gt;/*.json</c>：检测同一场景内是否放了多个游戏捕获源。</item>
/// </list>
/// 全程容错：任一步解析失败只跳过该项，不影响其它检查。
/// </summary>
public sealed class ObsConfigScanner
{
    private readonly HostBridge _host;

    public ObsConfigScanner(HostBridge host) => _host = host;

    public async Task<ObsConfigReport> ScanAsync()
    {
        var report = new ObsConfigReport();

        // 1) 根配置 + 各 profile 配置
        var rootIni = await _host.ReadObsConfigAsync("basic.ini");
        if (rootIni is not null) AnalyzeIni(report, rootIni, "basic.ini");

        var profiles = await _host.ListObsConfigAsync("profiles");
        foreach (var prof in profiles.Where(e => e.IsDir))
        {
            var ini = await _host.ReadObsConfigAsync($"profiles/{prof.Name}/basic.ini");
            if (ini is not null) AnalyzeIni(report, ini, $"profiles/{prof.Name}/basic.ini");
        }

        // 2) 场景集合：检测多游戏捕获同场景
        var collections = await _host.ListObsConfigAsync("sceneCollections");
        foreach (var col in collections.Where(e => e.IsDir))
        {
            var files = await _host.ListObsConfigAsync($"sceneCollections/{col.Name}");
            foreach (var f in files.Where(x => !x.IsDir && x.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            {
                var json = await _host.ReadObsConfigAsync($"sceneCollections/{col.Name}/{f.Name}");
                if (json is not null) AnalyzeSceneCollection(report, json, f.Name);
            }
        }

        report.Available = true;
        report.Source = "basic.ini / profiles / sceneCollections";
        return report;
    }

    // ----------------------------------------------------------- INI 解析

    private static void AnalyzeIni(ObsConfigReport report, string text, string source)
    {
        var ini = ParseIni(text);

        // 录制格式：MP4 中断易损坏，建议 MKV
        var recFormat = FirstValue(ini, new[] { "AdvOut", "Output", "SimpleOutput" }, "RecFormat");
        if (!string.IsNullOrWhiteSpace(recFormat) && recFormat.Equals("mp4", StringComparison.OrdinalIgnoreCase))
        {
            report.Findings.Add(new ConfigFinding
            {
                Code = "CFG-REC-MP4",
                Severity = LogSeverity.Warning,
                ProblemId = "rc-mp4corrupt",
                Title = "录制格式为 MP4（中断会损坏）",
                Detail = $"[{source}] 录制格式 = mp4。MP4 容器在意外中断（崩溃/断电）时文件常无法修复。",
                Suggestion = "建议改为 MKV：设置 → 输出 → 录制 → 类型选「自定义」，容器用 mkv；需要 MP4 时再用「混流录制」转封装。"
            });
        }

        // 音频采样率：统一 48kHz 可降低音画不同步
        var sampleRate = FirstValue(ini, new[] { "Audio" }, "SampleRate");
        if (!string.IsNullOrWhiteSpace(sampleRate) && sampleRate != "48000")
        {
            report.Findings.Add(new ConfigFinding
            {
                Code = "CFG-AUDIO-SAMPLERATE",
                Severity = LogSeverity.Warning,
                ProblemId = "av-samplerate",
                Title = $"音频采样率为 {sampleRate} Hz（建议 48000）",
                Detail = $"[{source}] SampleRate = {sampleRate}。非 48kHz 易与部分设备/平台不匹配，导致音画不同步或爆音。",
                Suggestion = "在 OBS 设置 → 音频 → 采样率 改为 48 kHz，并同步系统播放/录制设备的默认格式。"
            });
        }

        // 编码器：若使用 x264 软件编码且分辨率/帧率较高，提示硬件编码
        var encoder = FirstValue(ini, new[] { "AdvOut", "Output", "SimpleOutput" }, "Encoder");
        if (!string.IsNullOrWhiteSpace(encoder) && encoder.Contains("x264", StringComparison.OrdinalIgnoreCase))
        {
            report.Findings.Add(new ConfigFinding
            {
                Code = "CFG-ENC-X264",
                Severity = LogSeverity.Info,
                ProblemId = "enc-overload",
                Title = "当前使用 x264 软件编码",
                Detail = $"[{source}] Encoder = {encoder}。CPU 编码在复杂场景下易编码过载掉帧。",
                Suggestion = "若显卡支持，优先改用硬件编码（NVENC / AMF / QSV / Apple），可大幅降低 CPU 占用。"
            });
        }
    }

    private static string? FirstValue(Dictionary<string, Dictionary<string, string>> ini, string[] sections, string key)
    {
        foreach (var sec in sections)
            if (ini.TryGetValue(sec, out var dict) && dict.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
                return v;
        return null;
    }

    /// <summary>极简 INI 解析：section -> (key -> value)。大小写不敏感匹配键。</summary>
    private static Dictionary<string, Dictionary<string, string>> ParseIni(string text)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? current = null;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                var sec = line[1..^1].Trim();
                current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                result[sec] = current;
            }
            else if (current is not null && line.Contains('='))
            {
                var idx = line.IndexOf('=');
                var k = line[..idx].Trim();
                var v = line[(idx + 1)..].Trim();
                if (k.Length > 0) current[k] = v;
            }
        }
        return result;
    }

    // ----------------------------------------------------------- 场景集合解析

    private static void AnalyzeSceneCollection(ObsConfigReport report, string json, string fileName)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("scenes", out var scenes) || scenes.ValueKind != JsonValueKind.Array)
                return;

            foreach (var scene in scenes.EnumerateArray())
            {
                if (scene.ValueKind != JsonValueKind.Object) continue;
                var sceneName = scene.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                if (!scene.TryGetProperty("sources", out var srcArr) || srcArr.ValueKind != JsonValueKind.Array)
                    continue;

                var gameCaptures = new List<string>();
                foreach (var src in srcArr.EnumerateArray())
                {
                    if (src.ValueKind != JsonValueKind.Object) continue;
                    var id = src.TryGetProperty("id", out var sid) ? sid.GetString() ?? "" : "";
                    if (id == "game_capture")
                    {
                        var sname = src.TryGetProperty("name", out var sn) ? sn.GetString() ?? "" : "";
                        gameCaptures.Add(sname);
                    }
                }

                if (gameCaptures.Count > 1)
                {
                    report.Findings.Add(new ConfigFinding
                    {
                        Code = "CFG-MULTI-GAMECAPTURE",
                        Severity = LogSeverity.Warning,
                        ProblemId = "bs-multigame",
                        Title = $"场景「{sceneName}」放了 {gameCaptures.Count} 个游戏捕获源",
                        Detail = $"[{fileName}] 场景 {sceneName} 同时包含：{string.Join("、", gameCaptures)}。多个游戏捕获互相干扰会导致黑屏或卡顿。",
                        Suggestion = "一个场景只保留一个游戏捕获源；其它游戏改用「窗口捕获」或「显示器捕获」。"
                    });
                }
            }
        }
        catch (Exception)
        {
            // 场景集合 JSON 结构异常：跳过该项
        }
    }
}
