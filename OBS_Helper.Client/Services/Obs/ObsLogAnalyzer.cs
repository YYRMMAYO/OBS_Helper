using System.Globalization;
using System.Text.RegularExpressions;

namespace OBS_Helper.Client.Services.Obs;

/// <summary>问题严重程度。</summary>
public enum LogSeverity
{
    Info,
    Warning,
    Error,
    Critical
}

/// <summary>日志中发现的一条线索。</summary>
public sealed class LogFinding
{
    public string Code { get; init; } = "";
    public LogSeverity Severity { get; init; }
    public string Title { get; init; } = "";
    /// <summary>命中的日志原文（已脱敏），作为「证据」展示给用户。</summary>
    public string Evidence { get; set; } = "";
    public string Suggestion { get; init; } = "";
    /// <summary>关联到离线知识库中的问题条目 id，便于一键跳转到分步方案。</summary>
    public string? ProblemId { get; init; }
    /// <summary>同类命中次数。</summary>
    public int Occurrences { get; set; } = 1;
    /// <summary>首次出现的行号（从 1 开始）。</summary>
    public int FirstLine { get; set; }

    public string SeverityText => Severity switch
    {
        LogSeverity.Critical => "严重",
        LogSeverity.Error => "错误",
        LogSeverity.Warning => "警告",
        _ => "提示"
    };
}

/// <summary>从日志头部解析出的环境概况。</summary>
public sealed class ObsLogSummary
{
    public string ObsVersion { get; set; } = "";
    public string Platform { get; set; } = "";
    public string OsVersion { get; set; } = "";
    public string Cpu { get; set; } = "";
    public string Gpu { get; set; } = "";
    public string Memory { get; set; } = "";
    public string BaseResolution { get; set; } = "";
    public string OutputResolution { get; set; } = "";
    public string Fps { get; set; } = "";
    public string VideoEncoder { get; set; } = "";
    public string AudioSampleRate { get; set; } = "";
    public int Bitrate { get; set; }

    public int TotalLines { get; set; }
    public int WarningLines { get; set; }
    public int ErrorLines { get; set; }

    /// <summary>渲染滞后帧占比（0~1）。</summary>
    public double RenderLagRatio { get; set; }
    /// <summary>编码滞后跳帧占比（0~1）。</summary>
    public double EncodingLagRatio { get; set; }
    /// <summary>网络丢帧占比（0~1）。</summary>
    public double NetworkDropRatio { get; set; }
}

/// <summary>一次日志分析的完整结果。</summary>
public sealed class ObsLogReport
{
    public string SourceName { get; set; } = "";
    public DateTime AnalyzedAt { get; set; } = DateTime.Now;
    public ObsLogSummary Summary { get; set; } = new();
    public List<LogFinding> Findings { get; set; } = new();
    /// <summary>脱敏后的日志全文，可安全复制或发给云端 AI。</summary>
    public string SanitizedText { get; set; } = "";

    public bool HasIssues => Findings.Count > 0;

    public int CriticalCount => Findings.Count(f => f.Severity == LogSeverity.Critical);
    public int ErrorCount => Findings.Count(f => f.Severity == LogSeverity.Error);
    public int WarningCount => Findings.Count(f => f.Severity == LogSeverity.Warning);
}

/// <summary>一条匹配规则。</summary>
internal sealed class LogRule
{
    public required string Code { get; init; }
    public required Regex Pattern { get; init; }
    public required LogSeverity Severity { get; init; }
    public required string Title { get; init; }
    public required string Suggestion { get; init; }
    public string? ProblemId { get; init; }
}

/// <summary>
/// OBS 日志分析器（技术计划 §4.4）。
///
/// 全程离线、单遍扫描：
/// <list type="number">
///   <item>逐行脱敏（<see cref="LogSanitizer"/>），保证后续所有输出都不含隐私；</item>
///   <item>解析头部环境信息（版本 / 显卡 / 分辨率 / 编码器）；</item>
///   <item>用规则表匹配已知故障特征，命中后聚合计数并关联知识库条目；</item>
///   <item>把 OBS 自己统计的三类丢帧比例提取出来，给出量化结论。</item>
/// </list>
///
/// 之所以先脱敏再匹配：脱敏只影响 URL 路径、用户名等片段，不会破坏错误关键字，
/// 而这样能保证「证据」字段天然安全，不需要在每个展示点重复过滤。
/// </summary>
public sealed class ObsLogAnalyzer
{
    private const int MaxEvidenceLength = 240;
    private const int MaxFindings = 60;
    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    // ------------------------------------------------------------ 环境信息解析

    private static readonly Regex ReVersion = new(@"OBS\s+Studio\s+([\d.]+(?:-[\w.]+)?)\s*(?:\(([^)]+)\))?", Opts);
    private static readonly Regex ReCpu = new(@"^\s*CPU Name:\s*(.+)$", Opts);
    private static readonly Regex ReMemory = new(@"^\s*Physical Memory:\s*(.+)$", Opts);
    private static readonly Regex ReWinVer = new(@"^\s*Windows Version:\s*(.+)$", Opts);
    private static readonly Regex ReMacVer = new(@"^\s*OS Name:\s*(.+)$", Opts);
    private static readonly Regex ReGpu = new(@"(?:Loading up D3D11 on adapter|Adapter\s*\d*:?\s*|renderer:)\s*(.+?)\s*(?:\(\d+\))?\s*$", Opts);
    private static readonly Regex ReBaseRes = new(@"base resolution:\s*(\d+x\d+)", Opts);
    private static readonly Regex ReOutRes = new(@"output resolution:\s*(\d+x\d+)", Opts);
    private static readonly Regex ReFps = new(@"^\s*fps:\s*([\d/.]+)", Opts);
    private static readonly Regex ReEncoder = new(@"\[(x264|obs_x264|NVENC encoder|jim_nvenc|obs_qsv11|QSV Encoder|h264_texture_amf|av1_texture_amf|AMF Encoder|VideoToolbox[^\]:]*)[^\]]*\]", Opts);
    private static readonly Regex ReBitrate = new(@"^\s*(?:bitrate|rate_control.*bitrate)[:=]\s*(\d+)", Opts);
    private static readonly Regex ReSampleRate = new(@"samples per sec:\s*(\d+)", Opts);

    // OBS 收尾时打印的三类丢帧统计
    private static readonly Regex ReRenderLag = new(@"lagged frames due to rendering lag[^:]*:\s*(\d+)\s*\(([\d.]+)%\)", Opts);
    private static readonly Regex ReEncodeLag = new(@"skipped frames due to encoding lag[^:]*:\s*(\d+)(?:/(\d+))?\s*\(([\d.]+)%\)", Opts);
    private static readonly Regex ReNetDrop = new(@"dropped frames due to insufficient bandwidth[^:]*:\s*(\d+)\s*\(([\d.]+)%\)", Opts);

    // ------------------------------------------------------------------ 规则表

    private static readonly LogRule[] Rules =
    {
        // —— 编码 / 性能 ——
        new() {
            Code = "LOG-ENC-OVERLOAD", Severity = LogSeverity.Error, ProblemId = "enc-overload",
            Pattern = new Regex(@"encoding overloaded|Encoder overload|skipped frames due to encoding lag", Opts),
            Title = "编码过载（Encoding overloaded）",
            Suggestion = "降低输出分辨率或帧率、把 x264 预设调到 veryfast/ultrafast，或改用显卡硬件编码（NVENC / QSV / AMF）。"
        },
        new() {
            Code = "LOG-ENC-NVENC", Severity = LogSeverity.Error, ProblemId = "en-nvenc",
            Pattern = new Regex(@"Failed to open NVENC codec|NVENC Error|nvEncOpenEncodeSessionEx failed|No capable devices found", Opts),
            Title = "NVENC 硬件编码器初始化失败",
            Suggestion = "更新 NVIDIA 驱动到最新版；确认显卡支持 NVENC；关闭其他占用编码会话的软件（如录屏工具、云游戏客户端）。"
        },
        new() {
            Code = "LOG-ENC-AMF-QSV", Severity = LogSeverity.Error, ProblemId = "enc-nvenc",
            Pattern = new Regex(@"Failed to create.*(AMF|QSV)|AMF Error|qsv encoder.*fail|Failed to initialize encoder", Opts),
            Title = "AMD / Intel 硬件编码器初始化失败",
            Suggestion = "更新显卡驱动；核显编码需在 BIOS 中启用核显；必要时先切回 x264 软件编码确认可用。"
        },
        new() {
            Code = "LOG-ENC-AV1", Severity = LogSeverity.Warning, ProblemId = "enc-av1",
            Pattern = new Regex(@"AV1.*(not supported|unsupported|failed)", Opts),
            Title = "AV1 编码不受支持",
            Suggestion = "AV1 需要 RTX 40 系 / Arc / RX 7000 及以上显卡，且平台侧也要支持；请改用 H.264。"
        },

        // —— 渲染 / 显卡 ——
        new() {
            Code = "LOG-GPU-INIT", Severity = LogSeverity.Critical, ProblemId = "cr-driver",
            Pattern = new Regex(@"Failed to initialize video|device_create.*[Ff]ailed|Failed to create D3D11 device|Your GPU may not be supported", Opts),
            Title = "视频子系统初始化失败（显卡/驱动）",
            Suggestion = "彻底重装显卡驱动（建议用 DDU 清理后安装）；笔记本请确认 OBS 跑在正确的显卡上；尝试在 设置→高级 切换渲染器。"
        },
        new() {
            Code = "LOG-RENDER-LAG", Severity = LogSeverity.Warning, ProblemId = "lag-skip",
            Pattern = new Regex(@"lagged frames due to rendering lag", Opts),
            Title = "渲染滞后（GPU 压力过大）",
            Suggestion = "降低画布/输出分辨率与帧率；关闭游戏内的帧率上限外挂与其他 GPU 占用程序；减少浏览器源数量。"
        },
        new() {
            Code = "LOG-CAPTURE-FAIL", Severity = LogSeverity.Error, ProblemId = "bs-game",
            Pattern = new Regex(@"\[game-capture[^\]]*\].*(failed|error)|Failed to open process|hook.*failed|could not create hook", Opts),
            Title = "游戏捕获挂钩失败",
            Suggestion = "以管理员身份运行 OBS；游戏改成「无边框窗口」模式；或改用「显示器捕获」兜底。"
        },
        new() {
            Code = "LOG-DSHOW", Severity = LogSeverity.Error, ProblemId = "bs-capturecard",
            Pattern = new Regex(@"\[dshow[^\]]*\].*(failed|could not|error)|Device '.*' failed to start|Failed to start capture", Opts),
            Title = "摄像头 / 采集卡启动失败",
            Suggestion = "确认设备没被其他软件占用；更换 USB 口（避免走 Hub）；在设备属性里把分辨率/帧率/格式改成设备原生支持的组合。"
        },

        // —— 推流 / 网络 ——
        new() {
            Code = "LOG-STREAM-CONNECT", Severity = LogSeverity.Error, ProblemId = "sf-timeout",
            Pattern = new Regex(@"Failed to connect to server|Connection timed out|WSAETIMEDOUT|Could not connect to|socket error", Opts),
            Title = "推流服务器连接失败",
            Suggestion = "检查推流地址与网络；关闭 VPN / 加速器；换用 RTMPS 端口或就近的推流节点；确认防火墙放行 OBS。"
        },
        new() {
            Code = "LOG-STREAM-AUTH", Severity = LogSeverity.Error, ProblemId = "sf-auth",
            Pattern = new Regex(@"Authentication failed|invalid stream key|NetStream\.Publish\.BadName|access denied|403", Opts),
            Title = "推流鉴权失败（串流密钥问题）",
            Suggestion = "重新到直播平台后台复制串流密钥；注意密钥有有效期，开播前重新获取一次最稳妥。"
        },
        new() {
            Code = "LOG-STREAM-DROP", Severity = LogSeverity.Warning, ProblemId = "lag-network",
            Pattern = new Regex(@"dropped frames due to insufficient bandwidth|Output '.*' stopping.*reconnect|Reconnecting in \d+ second", Opts),
            Title = "上行带宽不足导致丢帧 / 自动重连",
            Suggestion = "把码率降到实测上行的 60~70%；有线网络优先于 WiFi；开启「动态码率」让 OBS 自动降码。"
        },
        new() {
            Code = "LOG-STREAM-DISCONNECT", Severity = LogSeverity.Error, ProblemId = "sf-drops",
            Pattern = new Regex(@"Disconnected from|The server has disconnected|connection closed by peer|RTMP.*disconnect", Opts),
            Title = "推流中途断开",
            Suggestion = "多为网络抖动或平台侧限制；降低码率、检查路由器 QoS、避免同时大流量上传。"
        },

        // —— 音频 ——
        new() {
            Code = "LOG-AUDIO-BUFFER", Severity = LogSeverity.Warning, ProblemId = "av-desync",
            Pattern = new Regex(@"adding \d+ milliseconds of audio buffering|Max audio buffering reached", Opts),
            Title = "音频缓冲不断增长（音画不同步风险）",
            Suggestion = "把所有音频设备的采样率统一为 48 kHz；减少 USB 声卡/蓝牙设备；必要时给对应源设置同步偏移。"
        },
        new() {
            Code = "LOG-AUDIO-DEVICE", Severity = LogSeverity.Error, ProblemId = "au-mic",
            Pattern = new Regex(@"WASAPI:.*(failed|error)|Failed to start audio|coreaudio.*failed|Device .* not found", Opts),
            Title = "音频设备启动失败",
            Suggestion = "在系统声音设置里确认设备已启用且未被独占；重新在 OBS 里选择一次设备；插拔后需重新指定。"
        },
        new() {
            Code = "LOG-AUDIO-SYNC", Severity = LogSeverity.Warning, ProblemId = "av-drift",
            Pattern = new Regex(@"out of sync|resetting audio|audio timestamp", Opts),
            Title = "音频时间戳异常 / 逐渐漂移",
            Suggestion = "统一采样率为 48 kHz；关闭声卡驱动的「增强」选项；蓝牙耳机改用有线设备。"
        },

        // —— 录制 ——
        new() {
            Code = "LOG-REC-WRITE", Severity = LogSeverity.Error, ProblemId = "rc-nofile",
            Pattern = new Regex(@"Unable to write to|Error opening file|No space left on device|failed to open output file|Could not open file", Opts),
            Title = "录制文件写入失败",
            Suggestion = "检查录制目录是否存在、是否有写权限、磁盘是否已满；录制路径避免中文与特殊字符；建议先录 MKV 再转封装。"
        },

        // —— 插件 / 崩溃 ——
        new() {
            Code = "LOG-PLUGIN", Severity = LogSeverity.Warning, ProblemId = "cr-plugin",
            Pattern = new Regex(@"os_dlopen\(.*\) failed|Module '.*' not loaded|Failed to load '.*' plugin|LoadLibrary failed", Opts),
            Title = "插件加载失败",
            Suggestion = "插件与 OBS 大版本不匹配是最常见原因；用「安全模式」启动确认，再逐个更新或移除插件。"
        },
        new() {
            Code = "LOG-VCREDIST", Severity = LogSeverity.Critical, ProblemId = "cr-vcredist",
            Pattern = new Regex(@"VCRUNTIME|MSVCP\d+\.dll|api-ms-win-crt|The specified module could not be found", Opts),
            Title = "缺少 Visual C++ 运行库",
            Suggestion = "安装最新的「Microsoft Visual C++ 2015-2022 Redistributable (x64)」后重启 OBS。"
        },
        new() {
            Code = "LOG-CRASH", Severity = LogSeverity.Critical, ProblemId = "cr-safe-mode",
            Pattern = new Regex(@"Unhandled exception|EXCEPTION_ACCESS_VIOLATION|c0000005|Crash Report|signal 11|SIGSEGV", Opts),
            Title = "检测到崩溃记录",
            Suggestion = "用安全模式（不加载第三方插件与脚本）启动排查；同时更新显卡驱动与 OBS 到最新版。"
        },
        new() {
            Code = "LOG-ADMIN", Severity = LogSeverity.Info, ProblemId = "bs-display",
            Pattern = new Regex(@"Running as administrator:\s*false", Opts),
            Title = "OBS 未以管理员身份运行",
            Suggestion = "捕获以管理员权限运行的游戏 / 全屏独占程序时会黑屏，建议右键「以管理员身份运行」。"
        },
        new() {
            Code = "LOG-SAFEMODE", Severity = LogSeverity.Info,
            Pattern = new Regex(@"Safe Mode enabled|--safe-mode", Opts),
            Title = "本次以安全模式启动",
            Suggestion = "安全模式下第三方插件、脚本与 WebSocket 均不加载，排障完成后请正常启动。"
        },

        // —— 系统层面的「配置不当」线索 ——
        // 这一组不是「已经报错」，而是日志里暴露出来的隐患：现在可能还没出事，
        // 但在高负载场景（开播 + 游戏 + 录制同时进行）几乎必然翻车。
        new() {
            Code = "LOG-SYS-HAGS", Severity = LogSeverity.Warning, ProblemId = "lag-skip",
            Pattern = new Regex(@"Hardware GPU Scheduler:\s*(?:true|on|enabled)", Opts),
            Title = "系统开启了「硬件加速 GPU 计划」（HAGS）",
            Suggestion = "HAGS 会打乱 GPU 任务排队顺序，是 NVENC 掉帧与画面卡顿的常见诱因。到 系统设置→显示→图形→更改默认图形设置 关掉它，然后重启电脑。"
        },
        new() {
            Code = "LOG-SYS-DUALGPU", Severity = LogSeverity.Warning, ProblemId = "bs-black",
            Pattern = new Regex(@"NV12 texture support:.*false|Failed to.*shared texture|different graphics adapter|adapter mismatch", Opts),
            Title = "疑似双显卡分配错误（OBS 与游戏不在同一块显卡上）",
            Suggestion = "笔记本常见问题：OBS 跑在核显、游戏跑在独显，导致捕获黑屏或额外性能损耗。到 系统设置→显示→图形 里把 OBS 指定为「高性能」（独显）。"
        },
        new() {
            Code = "LOG-SYS-GAMEMODE", Severity = LogSeverity.Info,
            Pattern = new Regex(@"Game Mode:\s*(?:true|on|enabled)", Opts),
            Title = "系统启用了 Windows 游戏模式",
            Suggestion = "游戏模式会优先把 CPU/GPU 资源让给前台游戏，可能挤压 OBS 的编码线程。若出现无规律掉帧，可试着关闭它做对比测试。"
        },

        // —— 录制格式 / 音频参数 ——
        new() {
            Code = "LOG-REC-MP4", Severity = LogSeverity.Warning, ProblemId = "rc-mp4corrupt",
            Pattern = new Regex(@"Recording Format:\s*mp4\b|\[ffmpeg muxer:.*\.mp4'", Opts),
            Title = "正在用 MP4 直接录制（有损坏风险）",
            Suggestion = "MP4 的索引写在文件末尾，一旦崩溃/断电，整段录像会彻底打不开。改用 MKV 或 hybrid MP4 录制，结束后用「录像转封装」转成 MP4。"
        },
        new() {
            Code = "LOG-AUDIO-44K", Severity = LogSeverity.Warning, ProblemId = "av-samplerate",
            Pattern = new Regex(@"samples per sec:\s*44100", Opts),
            Title = "音频采样率为 44.1 kHz（建议 48 kHz）",
            Suggestion = "绝大多数直播平台与视频编码链路都以 48 kHz 为准，44.1 kHz 需要实时重采样，长时间直播容易累积音画漂移。设置→音频→采样率 改为 48 kHz。"
        },
        new() {
            Code = "LOG-VER-OLD", Severity = LogSeverity.Info, ProblemId = "cr-update",
            Pattern = new Regex(@"OBS\s+Studio\s+(?:2[0-9]|30)\.\d+", Opts),
            Title = "OBS 版本可能偏旧",
            Suggestion = "新版通常修复了大量编码器与捕获相关的问题。到 帮助→检查更新 升级，升级前先备份配置目录。"
        },

        // —— 磁盘 / 资源 ——
        new() {
            Code = "LOG-DISK-SLOW", Severity = LogSeverity.Warning, ProblemId = "rc-diskfull",
            Pattern = new Regex(@"disk (?:is )?(?:too )?slow|Output is lagging.*disk|write.*buffer.*full|Media Write Error", Opts),
            Title = "磁盘写入速度跟不上录制码率",
            Suggestion = "换到 SSD 录制；避免录到网络盘/U 盘；关闭正在进行的大文件传输与实时杀毒扫描。"
        },
        new() {
            Code = "LOG-BROWSER-SRC", Severity = LogSeverity.Info, ProblemId = "bs-browser",
            Pattern = new Regex(@"\[obs-browser\].*(error|failed)|CEF.*(crash|error)", Opts),
            Title = "浏览器源异常",
            Suggestion = "浏览器源崩溃常由网页本身或硬件加速引起。在源属性里勾选「使用自定义帧率」并降到 30，或关闭浏览器源硬件加速后重试。"
        }
    };

    // ---------------------------------------------------------------- 分析入口

    /// <summary>分析一份 OBS 日志。<paramref name="rawText"/> 为日志原文。</summary>
    public ObsLogReport Analyze(string rawText, string sourceName = "")
    {
        var report = new ObsLogReport { SourceName = sourceName };
        if (string.IsNullOrWhiteSpace(rawText))
        {
            report.SanitizedText = "";
            return report;
        }

        var found = new Dictionary<string, LogFinding>(StringComparer.Ordinal);
        var sanitizedLines = new List<string>(1024);
        int lineNo = 0;

        foreach (var rawLine in LogSanitizer.SplitLines(rawText))
        {
            lineNo++;
            var line = LogSanitizer.SanitizeLine(rawLine);
            sanitizedLines.Add(line);

            if (line.Length == 0) continue;

            ParseSummaryLine(line, report.Summary);

            if (line.Contains("warning", StringComparison.OrdinalIgnoreCase)) report.Summary.WarningLines++;
            if (line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("failed", StringComparison.OrdinalIgnoreCase)) report.Summary.ErrorLines++;

            if (found.Count >= MaxFindings) continue;

            foreach (var rule in Rules)
            {
                if (!rule.Pattern.IsMatch(line)) continue;

                if (found.TryGetValue(rule.Code, out var existing))
                {
                    existing.Occurrences++;
                }
                else
                {
                    found[rule.Code] = new LogFinding
                    {
                        Code = rule.Code,
                        Severity = rule.Severity,
                        Title = rule.Title,
                        Suggestion = rule.Suggestion,
                        ProblemId = rule.ProblemId,
                        Evidence = Trim(line),
                        FirstLine = lineNo
                    };
                }
                // 一行可能同时命中多条规则（例如同时含 failed 与 NVENC），全部记录
            }
        }

        report.Summary.TotalLines = lineNo;
        report.SanitizedText = string.Join('\n', sanitizedLines);

        AppendQuantitativeFindings(report, found);

        report.Findings = found.Values
            .OrderByDescending(f => (int)f.Severity)
            .ThenByDescending(f => f.Occurrences)
            .ToList();

        return report;
    }

    // ------------------------------------------------------- 环境信息逐行提取

    private static void ParseSummaryLine(string line, ObsLogSummary s)
    {
        Match m;

        if (s.ObsVersion.Length == 0 && (m = ReVersion.Match(line)).Success)
        {
            s.ObsVersion = m.Groups[1].Value;
            if (m.Groups[2].Success) s.Platform = m.Groups[2].Value;
        }
        if (s.Cpu.Length == 0 && (m = ReCpu.Match(line)).Success) s.Cpu = m.Groups[1].Value.Trim();
        if (s.Memory.Length == 0 && (m = ReMemory.Match(line)).Success) s.Memory = m.Groups[1].Value.Trim();
        if (s.OsVersion.Length == 0 && (m = ReWinVer.Match(line)).Success) s.OsVersion = m.Groups[1].Value.Trim();
        if (s.OsVersion.Length == 0 && (m = ReMacVer.Match(line)).Success) s.OsVersion = m.Groups[1].Value.Trim();

        if (s.Gpu.Length == 0 && line.Contains("adapter", StringComparison.OrdinalIgnoreCase)
            && (m = ReGpu.Match(line)).Success)
        {
            s.Gpu = m.Groups[1].Value.Trim();
        }

        if (s.BaseResolution.Length == 0 && (m = ReBaseRes.Match(line)).Success) s.BaseResolution = m.Groups[1].Value;
        if (s.OutputResolution.Length == 0 && (m = ReOutRes.Match(line)).Success) s.OutputResolution = m.Groups[1].Value;
        if (s.Fps.Length == 0 && (m = ReFps.Match(line)).Success) s.Fps = NormalizeFps(m.Groups[1].Value);
        if (s.AudioSampleRate.Length == 0 && (m = ReSampleRate.Match(line)).Success) s.AudioSampleRate = m.Groups[1].Value + " Hz";
        if (s.VideoEncoder.Length == 0 && (m = ReEncoder.Match(line)).Success) s.VideoEncoder = m.Groups[1].Value;
        if (s.Bitrate == 0 && (m = ReBitrate.Match(line)).Success &&
            int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var br))
        {
            s.Bitrate = br;
        }

        if ((m = ReRenderLag.Match(line)).Success) s.RenderLagRatio = ParsePercent(m.Groups[2].Value);
        if ((m = ReEncodeLag.Match(line)).Success) s.EncodingLagRatio = ParsePercent(m.Groups[3].Value);
        if ((m = ReNetDrop.Match(line)).Success) s.NetworkDropRatio = ParsePercent(m.Groups[2].Value);
    }

    /// <summary>OBS 的 fps 可能写成 "60/1" 或 "59.94"，统一成可读形式。</summary>
    internal static string NormalizeFps(string raw)
    {
        var parts = raw.Split('/');
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var num) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var den) &&
            den > 0)
        {
            return (num / den).ToString("0.##", CultureInfo.InvariantCulture);
        }
        return raw;
    }

    private static double ParsePercent(string raw)
        => double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v / 100.0 : 0;

    // ------------------------------------------------- 基于统计数字的量化结论

    /// <summary>
    /// OBS 会在收尾时打印三类丢帧比例。比例本身比「有没有出现过这行字」更有价值，
    /// 因此单独生成量化结论，并按阈值升降级严重程度。
    /// </summary>
    private static void AppendQuantitativeFindings(ObsLogReport report, Dictionary<string, LogFinding> found)
    {
        var s = report.Summary;

        AddRatio(found, "LOG-STAT-RENDER", s.RenderLagRatio, "渲染滞后帧占比",
            "lag-skip", "GPU 渲染跟不上：降低画布分辨率/帧率，关闭其他吃显卡的程序，减少浏览器源与滤镜。");

        AddRatio(found, "LOG-STAT-ENCODE", s.EncodingLagRatio, "编码滞后跳帧占比",
            "enc-overload", "编码器跟不上：把 x264 预设调快一档，或改用显卡硬件编码；也可下调输出分辨率。");

        AddRatio(found, "LOG-STAT-NETWORK", s.NetworkDropRatio, "网络丢帧占比",
            "lag-network", "上行带宽不足：把码率降到实测上行的 60~70%，优先有线网络，必要时开启动态码率。");
    }

    private static void AddRatio(
        Dictionary<string, LogFinding> found, string code, double ratio,
        string label, string problemId, string suggestion)
    {
        if (ratio <= 0.0005) return; // 小于 0.05% 视为正常抖动

        var severity = ratio switch
        {
            >= 0.05 => LogSeverity.Critical,
            >= 0.01 => LogSeverity.Error,
            _ => LogSeverity.Warning
        };

        found[code] = new LogFinding
        {
            Code = code,
            Severity = severity,
            Title = $"{label} {ratio * 100:0.##}%",
            Suggestion = suggestion,
            ProblemId = problemId,
            Evidence = $"OBS 统计：{label} = {ratio * 100:0.##}%（1% 以上就会被观众明显感知）",
            FirstLine = 0
        };
    }

    private static string Trim(string line)
        => line.Length <= MaxEvidenceLength ? line : line[..MaxEvidenceLength] + "…";
}
