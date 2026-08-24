using System.Globalization;
using System.Text.RegularExpressions;

namespace OBS_Helper.Wpf.Services.Obs;

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
    /// <summary>
    /// 命中插件类线索时提取出的嫌疑模块名（如 foo.dll）。
    /// 用于与本地插件体检结果 / 插件广场目录联动，给出「跳转查看」入口。
    /// </summary>
    public string? SuspectModule { get; set; }
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

    /// <summary>
    /// 日志中枚举到的全部显卡适配器名（去重，最多 8 个）。
    /// 用于双显卡环境判断（B3）：OBS 实际选用的适配器 vs 机内其他适配器。
    /// </summary>
    public List<string> Adapters { get; } = new();

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

    // OBS 日志版本行实为 "OBS 30.0.0 (64-bit, windows)"（不带 "Studio"），但个别文案/旧版会带 "Studio"，
    // 因此把 "Studio" 做成可选段，避免只认其中一种导致版本与平台永远解析不出来。
    private static readonly Regex ReVersion = new(@"OBS(?:\s+Studio)?\s+([\d.]+(?:-[\w.]+)?)\s*(?:\(([^)]+)\))?", Opts);
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

    // 插件嫌疑模块提取（P0-2：日志 × 插件联动）
    private static readonly Regex ReDlopen = new(@"os_dlopen\s*\(\s*['""“”]?([^)'""“”]+)", Opts);
    private static readonly Regex ReModuleNotLoaded = new(@"(?:Module|插件)\s+'([^']+)'\s+(?:not loaded|未加载|加载失败)", Opts);
    private static readonly Regex RePluginLoadFail = new(@"Failed to load (?:the )?'?([^'""，。\n]+?)'?\s+plugin", Opts);

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
        new() {
            Code = "LOG-HYBRID-MP4", Severity = LogSeverity.Info, ProblemId = "rc-hybrid-mp4",
            Pattern = new Regex(@"hybrid[_ -]?mp4|hybrid[_ -]?mov", Opts),
            Title = "使用 Hybrid MP4/MOV 输出（32.x 新默认格式）",
            Suggestion = "Hybrid MP4 是 32.0 起的默认录像格式（防崩溃）。若剪辑软件 / 播放器打不开：用 文件 → 录像转封装 转 MP4，或改回 MKV 录制。详见知识库条目。"
        },
        new() {
            Code = "LOG-VIRTUALCAM", Severity = LogSeverity.Error, ProblemId = "st-virtualcam",
            Pattern = new Regex(@"virtual[_ -]?cam(?:era)?.{0,40}(?:fail(?:ed)?|error)|failed to start virtual camera", Opts),
            Title = "虚拟摄像头启动失败",
            Suggestion = "确认 OBS 菜单里的「启动虚拟摄像头」已开启且未被其他程序占用；Windows 更新后失效属常见回归，重装最新版 OBS 或重选设备即可恢复。详见知识库条目。"
        },

        // —— 插件 / 崩溃 ——
        new() {
            Code = "LOG-PLUGIN", Severity = LogSeverity.Warning, ProblemId = "cr-plugin",
            Pattern = new Regex(@"os_dlopen\(.*\)\s*failed|os_dlopen.*(?:failed|could not|无法|拒绝|找不到)|Module '.*' not loaded|Failed to load '.*' plugin|LoadLibrary failed", Opts),
            Title = "插件加载失败",
            Suggestion = "先把 OBS 升级到 32.2.2 或更高（32.2 首发的 Windows 插件加载变更曾导致带依赖的插件首启失败，补丁版已修复），再逐个更新或重装报错插件。可用「安全模式」启动确认，并在「插件」页的本机体检面板核对已装插件版本。"
        },
        new() {
            Code = "LOG-PLUGIN-STREAMFX", Severity = LogSeverity.Info, ProblemId = "cr-streamfx",
            Pattern = new Regex(@"streamfx", Opts),
            Title = "日志中出现 StreamFX（已停止维护的插件）",
            Suggestion = "StreamFX 已实质停更，在 OBS 30+ 上兼容性持续恶化，是老教程用户的高频故障源；建议迁移到单一职责轻量插件（模糊用 Composite Blur、遮罩用 Advanced Masks 等），详见知识库条目。"
        },
        new() {
            Code = "LOG-PLUGIN-MULTI-RTMP", Severity = LogSeverity.Info, ProblemId = "st-multi-rtmp",
            Pattern = new Regex(@"obs-multi-rtmp|multi[_ -]?rtmp", Opts),
            Title = "使用了 obs-multi-rtmp 多路推流插件",
            Suggestion = "该插件在新版 OBS 上有带宽骤降 / 编码过载 / 杀软误报的零星报告；多路推流不稳时优先评估 Aitum Multistream（维护活跃），详见知识库条目。"
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

        // —— 双显卡 / 集成显卡 ——
        new() {
            Code = "LOG-GPU-HYBRID", Severity = LogSeverity.Warning, ProblemId = "bs-display",
            Pattern = new Regex(@"Intel\(R\)\s+(?:UHD|HD Graphics|Iris)|AMD Radeon\(TM\) Graphics\b", Opts),
            Title = "疑似正在使用集成显卡渲染",
            Suggestion = "笔记本双显卡请把 OBS 指定到独立显卡：Windows「设置 → 系统 → 显示 → 图形」里为 OBS 选择「高性能」，或在 NVIDIA 控制面板里单独指定。"
        },

        // —— 音频采样率 ——
        new() {
            Code = "LOG-AUDIO-SAMPLERATE", Severity = LogSeverity.Warning, ProblemId = "av-desync",
            Pattern = new Regex(@"sample rate(?:s)?[^.\n]{0,40}(?:don't match|doesn't match|mismatch|differ)", Opts),
            Title = "音频采样率不匹配",
            Suggestion = "把所有音频设备（麦克风 / 扬声器 / 声卡）的采样率统一为 48 kHz，并在 Windows 声音设置里保持一致，可避免爆音与音画漂移。"
        },

        // —— 推流密钥泄漏风险 ——
        new() {
            Code = "LOG-STREAMKEY-LEAK", Severity = LogSeverity.Warning, ProblemId = "sf-auth",
            Pattern = new Regex(@"stream[_-]?key\s*[:=]", Opts),
            Title = "日志中出现串流密钥",
            Suggestion = "日志里可能包含推流密钥，切勿直接公开分享原始日志；本工具对复制 / 发送到云端的内容已自动脱敏。"
        },

        // —— 崩溃肇事模块 ——
        new() {
            Code = "LOG-CRASH-MODULE", Severity = LogSeverity.Critical, ProblemId = "cr-plugin",
            Pattern = new Regex(@"(?:faulting module|fault module|crashed module|module that caused)[^:\n]*:\s*([^\s]+)|Exception Module Name:\s*([^\s]+)", Opts),
            Title = "崩溃报告：定位到肇事模块",
            Suggestion = "从崩溃报告中提取到了引发崩溃的模块，通常是某个插件或驱动；禁用对应插件 / 更新驱动后再试。"
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

            // 逐行解析环境信息与错误统计
            ParseSummaryLine(line, report.Summary);
            CountIssueLines(line, report.Summary);

            // 用规则表匹配已知故障特征（一行可能同时命中多条，全部记录）
            if (found.Count < MaxFindings)
            {
                foreach (var rule in Rules)
                    MatchRules(rule, line, lineNo, found);
            }
        }

        report.Summary.TotalLines = lineNo;
        report.SanitizedText = string.Join('\n', sanitizedLines);

        AppendQuantitativeFindings(report, found);
        AppendDropTriage(report, found);          // B2：掉帧三分类主因判定
        AppendEncoderTriage(report, found);       // B1：编码过载按当前设置分诊
        AppendGpuAdapterFindings(report, found);  // B3：双显卡错位检测
        AppendPluginObsVersionHint(report, found);// A1：插件加载失败 × OBS 版本联动

        report.Findings = found.Values
            .OrderByDescending(f => (int)f.Severity)
            .ThenByDescending(f => f.Occurrences)
            .ToList();

        return report;
    }

    /// <summary>统计 warning / error 行数。</summary>
    private static void CountIssueLines(string line, ObsLogSummary s)
    {
        if (line.Contains("warning", StringComparison.OrdinalIgnoreCase)) s.WarningLines++;
        if (line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("failed", StringComparison.OrdinalIgnoreCase)) s.ErrorLines++;
    }

    /// <summary>用规则表匹配一行：命中则聚合计数或新建 finding。</summary>
    private static void MatchRules(LogRule rule, string line, int lineNo, Dictionary<string, LogFinding> found)
    {
        if (!rule.Pattern.IsMatch(line)) return;

        if (found.TryGetValue(rule.Code, out var existing))
        {
            existing.Occurrences++;
            // 首次命中行没提取到嫌疑模块时，后续行补上（插件类线索跨多行出现很常见）
            if (existing.SuspectModule is null)
            {
                var late = ExtractSuspectModule(rule, line);
                if (late is not null) existing.SuspectModule = late;
            }
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
                SuspectModule = ExtractSuspectModule(rule, line),
                FirstLine = lineNo
            };
        }
        // 一行可能同时命中多条规则（例如同时含 failed 与 NVENC），全部记录
    }

    /// <summary>
    /// 从命中的日志行里提取「嫌疑插件 / 模块」名（P0-2）。
    /// 只对插件加载失败与崩溃肇事模块两类线索提取；提取不到返回 null。
    /// </summary>
    private static string? ExtractSuspectModule(LogRule rule, string line)
    {
        if (rule.Code == "LOG-CRASH-MODULE")
        {
            // 该规则的 Pattern 自带两个捕获组：faulting module … / Exception Module Name …
            var m = rule.Pattern.Match(line);
            if (!m.Success) return null;
            for (var g = 1; g < m.Groups.Count; g++)
            {
                if (m.Groups[g].Success && m.Groups[g].Value.Length > 0)
                {
                    var name = CleanModuleName(m.Groups[g].Value);
                    if (name.Length > 0) return name;
                }
            }
            return null;
        }

        if (rule.Code != "LOG-PLUGIN") return null;

        foreach (var rx in new[] { ReDlopen, ReModuleNotLoaded, RePluginLoadFail })
        {
            var m = rx.Match(line);
            if (!m.Success) continue;
            var name = CleanModuleName(m.Groups[1].Value);
            if (!string.IsNullOrEmpty(name)) return name;
        }
        return null;
    }

    /// <summary>把捕获到的模块路径 / 名称清洗成纯文件名（去路径、去引号与标点）。</summary>
    private static string CleanModuleName(string raw)
    {
        var s = raw.Trim().Trim('"', '\'', '`', ',', '.', ';', ':', ')', ']'); 
        var slash = Math.Max(s.LastIndexOf('/'), s.LastIndexOf('\\'));
        if (slash >= 0) s = s[(slash + 1)..];
        s = s.Trim();
        return s.Length == 0 || s.IndexOf(' ') >= 0 ? "" : s;
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

        // B3：收集日志中枚举到的全部适配器（去重，上限 8 个），供双显卡错位检测
        if (line.Contains("adapter", StringComparison.OrdinalIgnoreCase)
            && (m = ReGpu.Match(line)).Success)
        {
            var adapterName = m.Groups[1].Value.Trim();
            if (adapterName.Length > 0 &&
                !s.Adapters.Contains(adapterName, StringComparer.OrdinalIgnoreCase) &&
                s.Adapters.Count < 8)
            {
                s.Adapters.Add(adapterName);
            }
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

    // --------------------------------------- B1/B2/B3/A1：分诊与联动（V2.5）

    /// <summary>丢帧三分类 → 对应知识库条目。</summary>
    private static readonly (string Code, string Label, string ProblemId, string Fix)[] DropKinds =
    {
        ("LOG-STAT-RENDER",  "渲染滞后", "lag-skip",
         "GPU 渲染跟不上：降画布分辨率/帧率、关掉吃显卡的程序、减少浏览器源与滤镜。"),
        ("LOG-STAT-ENCODE",  "编码滞后", "enc-overload",
         "编码器跟不上：x264 预设调快或改用硬件编码，必要时下调输出分辨率。"),
        ("LOG-STAT-NETWORK", "网络丢帧", "lag-network",
         "上行带宽不足：码率降到实测上行 60~70%，优先有线网络，可开动态码率。"),
    };

    private static double RatioOf(ObsLogSummary s, string code) => code switch
    {
        "LOG-STAT-RENDER" => s.RenderLagRatio,
        "LOG-STAT-ENCODE" => s.EncodingLagRatio,
        _ => s.NetworkDropRatio
    };

    private static LogSeverity DropSeverity(double ratio) => ratio switch
    {
        >= 0.05 => LogSeverity.Critical,
        >= 0.01 => LogSeverity.Error,
        _ => LogSeverity.Warning
    };

    /// <summary>
    /// B2 掉帧主因判定：OBS 的三类丢帧统计病因完全不同（GPU / 编码器 / 网络），
    /// 占比最高的一项即主因。生成一条「先治哪里」的结论，避免用户按错误方向折腾。
    /// </summary>
    private static void AppendDropTriage(ObsLogReport report, Dictionary<string, LogFinding> found)
    {
        var s = report.Summary;
        var meaningful = DropKinds
            .Select(k => (Kind: k, Ratio: RatioOf(s, k.Code)))
            .Where(x => x.Ratio > 0.005)
            .ToList();
        if (meaningful.Count == 0) return;

        var dominant = meaningful.OrderByDescending(x => x.Ratio).First().Kind;
        var evidence = string.Join("；", meaningful.Select(x => $"{x.Kind.Label} {x.Ratio * 100:0.##}%"));
        var maxRatio = meaningful.Max(x => x.Ratio);

        found["LOG-DROP-DOMINANT"] = new LogFinding
        {
            Code = "LOG-DROP-DOMINANT",
            Severity = DropSeverity(maxRatio),
            Title = $"掉帧主因判定：{dominant.Label}占比最高",
            Suggestion = dominant.Fix + " 三类丢帧的病因互不相同，请先处理主因再复测，不要同时改一堆设置。",
            ProblemId = dominant.ProblemId,
            Evidence = $"OBS 统计：{evidence}",
            FirstLine = 0
        };
    }

    /// <summary>显卡厂商提示（B1 分诊用）：能从日志判断出厂商时给出具体编码器名。</summary>
    private static (string Vendor, string EncoderName)? GpuVendorHint(ObsLogSummary s)
    {
        var gpu = s.Gpu + " " + s.Cpu;
        if (gpu.Contains("nvidia", StringComparison.OrdinalIgnoreCase) ||
            gpu.Contains("geforce", StringComparison.OrdinalIgnoreCase))
            return ("NVIDIA", "NVIDIA NVENC H.264");
        if (gpu.Contains("radeon", StringComparison.OrdinalIgnoreCase) ||
            gpu.Contains("amd", StringComparison.OrdinalIgnoreCase))
            return ("AMD", "AMD AMF H.264");
        if (gpu.Contains("intel", StringComparison.OrdinalIgnoreCase))
            return ("Intel", "Intel Quick Sync (QSV)");
        return null;
    }

    /// <summary>
    /// B1 编码过载分诊：结合日志头部解析出的编码器 / 显卡 / 帧率 / 分辨率，
    /// 生成按当前设置定制的处理顺序，而不是一句放之四海皆准的「降低设置」。
    /// </summary>
    private static void AppendEncoderTriage(ObsLogReport report, Dictionary<string, LogFinding> found)
    {
        var s = report.Summary;
        if (!found.ContainsKey("LOG-ENC-OVERLOAD") && s.EncodingLagRatio < 0.01) return;

        var steps = new List<string>();
        var enc = s.VideoEncoder.ToLowerInvariant();

        if (enc.Length == 0 || enc.Contains("x264"))
        {
            var hw = GpuVendorHint(s);
            steps.Add(hw is null
                ? "第 1 步：改用显卡硬件编码（设置 → 输出 → 编码器选 NVENC / QSV / AMF 之一），把编码负载从 CPU 挪走。"
                : $"第 1 步：检测到 {hw.Value.Vendor} 显卡，改用 {hw.Value.EncoderName} 硬件编码（设置 → 输出 → 编码器），把编码负载从 CPU 挪走。");
            steps.Add("第 2 步：若必须用 x264，把预设调到 veryfast / superfast（设置 → 输出 → 预设）。");
        }
        else if (enc.Contains("nvenc") || enc.Contains("jim"))
        {
            steps.Add("第 1 步：NVENC 预设从 P7 降到 P5 或 P4（设置 → 输出 → 预设），吞吐提升明显、画质损失小。");
            steps.Add("第 2 步：把游戏帧率锁到略低于显示器刷新率（如 144Hz 锁 138），给 OBS 合成与编码留出 GPU 余量。");
        }
        else if (enc.Contains("qsv") || enc.Contains("amf"))
        {
            steps.Add("第 1 步：确认显卡驱动为最新版，硬件编码器的性能修复通常随驱动发布。");
            steps.Add("第 2 步：若游戏已占满 GPU，同样需要锁帧或降档输出分辨率。");
        }

        if (double.TryParse(s.Fps, NumberStyles.Float, CultureInfo.InvariantCulture, out var fps) && fps >= 50)
            steps.Add($"第 {steps.Count + 1} 步：当前帧率 {s.Fps}，降到 30 可直接减半编码工作量（观众端几乎无感）。");

        if (TryParseHeight(s.OutputResolution, out var h) && h >= 1000)
            steps.Add($"第 {steps.Count + 1} 步：当前输出分辨率 {s.OutputResolution}，降到 1280x720 是最立竿见影的一步。");

        steps.Add($"第 {steps.Count + 1} 步：清理重复捕获与不用的浏览器源；浏览器源长时间直播要定期刷新防内存膨胀。");

        found["LOG-TRIAGE-ENCODE"] = new LogFinding
        {
            Code = "LOG-TRIAGE-ENCODE",
            Severity = LogSeverity.Info,
            Title = "编码过载分诊：按当前设置生成的处理顺序",
            Suggestion = string.Join("\n", steps),
            ProblemId = "enc-overload",
            Evidence = $"当前设置：编码器 {(string.IsNullOrEmpty(s.VideoEncoder) ? "未知" : s.VideoEncoder)}" +
                       $" · 帧率 {(string.IsNullOrEmpty(s.Fps) ? "未知" : s.Fps)}" +
                       $" · 输出分辨率 {(string.IsNullOrEmpty(s.OutputResolution) ? "未知" : s.OutputResolution)}",
            FirstLine = 0
        };
    }

    /// <summary>解析 "1920x1080" 形式的高度值。</summary>
    private static bool TryParseHeight(string? resolution, out int height)
    {
        height = 0;
        var x = resolution?.IndexOf('x');
        if (x is null || x <= 0) return false;
        return int.TryParse(resolution![(x.Value + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out height);
    }

    // ------------------------------------------------ B3：双显卡错位检测

    /// <summary>与 LOG-GPU-HYBRID 同源的核显命名特征。</summary>
    private static readonly Regex ReIntegratedGpu =
        new(@"Intel\(R\)\s+(?:UHD|HD Graphics|Iris)|AMD Radeon\(TM\) Graphics\b", Opts);

    private static readonly Regex ReDiscreteGpu =
        new(@"NVIDIA|GeForce|Quadro|Radeon RX|Radeon\(TM\) RX|Arc\b", Opts);

    private static bool IsIntegrated(string name) => !string.IsNullOrEmpty(name) && ReIntegratedGpu.IsMatch(name);
    private static bool IsDiscrete(string name) => !string.IsNullOrEmpty(name) && ReDiscreteGpu.IsMatch(name);

    /// <summary>
    /// B3 双显卡错位：日志枚举出 ≥2 个适配器时，检查 OBS 实际选用的适配器。
    /// 用核显渲染而独显在位 → 警告（关联既有知识库条目 bs-dualgpu）；
    /// 已用独显 → 给一条确认级提示，让用户放心排除这个变量。
    /// </summary>
    private static void AppendGpuAdapterFindings(ObsLogReport report, Dictionary<string, LogFinding> found)
    {
        var s = report.Summary;
        var adapters = s.Adapters
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();
        if (adapters.Count < 2 || string.IsNullOrEmpty(s.Gpu)) return;

        var others = adapters.Where(a => !a.Equals(s.Gpu, StringComparison.OrdinalIgnoreCase)).ToList();
        var adapterText = string.Join("；", adapters);

        if (IsIntegrated(s.Gpu) && others.Any(IsDiscrete))
        {
            found["LOG-GPU-DUAL"] = new LogFinding
            {
                Code = "LOG-GPU-DUAL",
                Severity = LogSeverity.Warning,
                Title = "双显卡错位：OBS 正在使用集成显卡渲染",
                Suggestion = "Windows「设置 → 系统 → 显示 → 显卡」里为 OBS 选择「高性能 GPU」，或在 NVIDIA / AMD 控制面板中单独指定；改完后完全退出并重启 OBS。详见知识库「笔记本双显卡」条目。",
                ProblemId = "bs-dualgpu",
                Evidence = $"日志适配器：{adapterText}（OBS 选用：{s.Gpu}）",
                FirstLine = 0
            };
        }
        else if (IsDiscrete(s.Gpu) && others.Any(IsIntegrated))
        {
            found["LOG-GPU-DUAL-OK"] = new LogFinding
            {
                Code = "LOG-GPU-DUAL-OK",
                Severity = LogSeverity.Info,
                Title = "双显卡环境确认：OBS 已使用独立显卡渲染",
                Suggestion = "本机为双显卡环境，OBS 当前跑在独显上，无需处理；若游戏捕获黑屏，再检查游戏所在 GPU 与捕获方式。",
                ProblemId = "bs-dualgpu",
                Evidence = $"日志适配器：{adapterText}（OBS 选用：{s.Gpu}）",
                FirstLine = 0
            };
        }
    }

    // ------------------------------------------------ A1：插件失败 × OBS 版本联动

    /// <summary>32.2 首发的 Windows 插件加载问题在此补丁版修复。</summary>
    internal static readonly int[] PluginLoadFixVersion = { 32, 2, 2 };

    /// <summary>把 "31.1.1" / "30.0.0-beta1" 形式的版本号解析成整数段，解析失败返回 null。</summary>
    internal static int[]? TryParseVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;
        var head = version.Trim().Split('-')[0];
        var parts = head.Split('.');
        var result = new List<int>(parts.Length);
        foreach (var p in parts)
        {
            if (!int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return null;
            result.Add(n);
        }
        return result.Count == 0 ? null : result.ToArray();
    }

    internal static int CompareVersions(int[] a, int[] b)
    {
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var av = i < a.Length ? a[i] : 0;
            var bv = i < b.Length ? b[i] : 0;
            if (av != bv) return av.CompareTo(bv);
        }
        return 0;
    }

    /// <summary>
    /// A1 联动：出现插件加载失败、且日志版本低于修复补丁版（32.2.2）时，
    /// 单独提示「先升级 OBS」这条最高性价比解法，避免用户先去折腾重装插件。
    /// </summary>
    private static void AppendPluginObsVersionHint(ObsLogReport report, Dictionary<string, LogFinding> found)
    {
        if (!found.ContainsKey("LOG-PLUGIN")) return;

        var v = TryParseVersion(report.Summary.ObsVersion);
        if (v is null || CompareVersions(v, PluginLoadFixVersion) >= 0) return;

        found["LOG-PLUGIN-OBSVER"] = new LogFinding
        {
            Code = "LOG-PLUGIN-OBSVER",
            Severity = LogSeverity.Info,
            Title = $"插件加载失败且 OBS 版本（{report.Summary.ObsVersion}）低于修复补丁版 32.2.2",
            Suggestion = "先把 OBS 升级到 32.2.2 或更高（设置 → 一般 → 检查更新，或官网重装）：32.2 首发的 Windows 插件加载变更导致的首启失败已在该补丁版修复，升级后再重装报错的插件即可。",
            ProblemId = "cr-plugin-load",
            Evidence = $"OBS 版本 {report.Summary.ObsVersion} < 32.2.2，且日志存在插件加载失败记录",
            FirstLine = 0
        };
    }
}
