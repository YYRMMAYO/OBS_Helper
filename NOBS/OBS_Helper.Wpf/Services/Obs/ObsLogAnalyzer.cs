using OBS_Helper.Wpf.Localization;
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
        LogSeverity.Critical => Localization.Strings.T("severity.critical"),
        LogSeverity.Error => Localization.Strings.T("severity.error"),
        LogSeverity.Warning => Localization.Strings.T("severity.warning"),
        _ => Localization.Strings.T("severity.info")
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

    /// <summary>
    /// 脱敏后的日志全文，可安全复制或发给云端 AI。
    ///
    /// V3.0.0（F4）由「分析时就拼好」改为<b>懒构造</b>：整份脱敏全文和原文一样有好几 MB，
    /// 而真正会读它的只有「复制脱敏日志」与「云端诊断附日志」两条路径；先拼好再常驻，
    /// 等于分析完还白白多留一份内存。现在只登记原文，首次读取时才逐行脱敏拼出来并缓存。
    /// 赋值方（例如日志页离开时要收缩内存，见 <see cref="TrimSanitizedText"/>）写入什么就是什么，
    /// 不会再回落到原文，因此这里只缓存、不重置。用 <see cref="Lazy{T}"/> 的默认
    /// ExecutionAndPublication 语义：并发首次读取只有一个线程真正构造，构造过程不回调本对象，
    /// 不存在重入或死锁。
    /// </summary>
    public string SanitizedText
    {
        get => _sanitizedText.Value;
        set => _sanitizedText = new Lazy<string>(() => value);
    }

    private Lazy<string> _sanitizedText = new(() => "");

    /// <summary>登记「脱敏全文」的数据来源：真正去读 <see cref="SanitizedText"/> 时才会逐行脱敏。</summary>
    internal void SetSanitizedSource(string rawText)
        => _sanitizedText = new Lazy<string>(() => LogSanitizer.Sanitize(rawText));

    /// <summary>
    /// 收缩脱敏全文（V3.0.0 F4 × F7）：日志页离开时用来把这份大字符串降成一段前缀。
    ///
    /// 与直接写 <see cref="SanitizedText"/> 的区别是：<b>还没被读过就不去读</b> ——
    /// 直接取 <c>SanitizedText.Length</c> 判断要不要截断，会先触发一次全文脱敏，
    /// 为了「省内存」反而先把整份文本拼出来，白花一次 CPU 与一份内存。
    /// 没读过时这里连原文引用一起丢掉，读过则截断成前缀（不保留原文，避免再次回落）。
    /// </summary>
    internal void TrimSanitizedText(int keepPrefix)
    {
        var current = _sanitizedText;
        if (!current.IsValueCreated)
        {
            _sanitizedText = new Lazy<string>(() => "");
            return;
        }

        var text = current.Value;
        if (text.Length > keepPrefix)
            _sanitizedText = new Lazy<string>(() => text[..keepPrefix]);
    }

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
    /// <summary>标题文案键（语言无关；V2.9.2）。</summary>
    public required string TitleKey { get; init; }
    /// <summary>处置建议文案键（语言无关；V2.9.2）。</summary>
    public required string SuggestionKey { get; init; }
    public string? ProblemId { get; init; }

    /// <summary>标题（按当前语言取文案）。</summary>
    public string Title => Localization.Strings.T(TitleKey);

    /// <summary>处置建议（按当前语言取文案）。</summary>
    public string Suggestion => Localization.Strings.T(SuggestionKey);
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

    /// <summary>
    /// 环境解析与规则表共用的匹配选项。V3.0.0（F4）加 <see cref="RegexOptions.Compiled"/>：
    /// 这里的正则全是静态字段，编译成本只付一次，换来的是每行日志、每条规则各少一轮解释执行。
    /// </summary>
    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    // ------------------------------------------------------------ 环境信息解析

    /// <summary>
    /// 真实 OBS 日志每行都带 "HH:mm:ss.mmm: " 前缀（如 <c>19:41:42.564: CPU Name: AMD Ryzen 9 3900X</c>），
    /// 而早期版本的规则直接用了 <c>^</c> 纯行首锚定，在真实日志上永远匹配不到 ——
    /// 实测 OBS 32.2.2 的真实日志里 CPU / 内存 / 系统版本 / 帧率 / 码率全部解析为空。
    ///
    /// 这里统一用「可选时间戳前缀」代替纯行首：既保留锚定语义（不被正文里的同名字段误伤），
    /// 又能吃到真实日志；随后的 <c>\s*</c> 吃掉 OBS 用 Tab 缩进的二级字段。
    /// </summary>
    private const string LineStart = @"^(?:\d{1,2}:\d{2}:\d{2}[.,]\d{1,3}:\s*)?\s*";

    // OBS 日志版本行实为 "OBS 30.0.0 (64-bit, windows)"（不带 "Studio"），但个别文案/旧版会带 "Studio"，
    // 因此把 "Studio" 做成可选段，避免只认其中一种导致版本与平台永远解析不出来。
    private static readonly Regex ReVersion = new(@"OBS(?:\s+Studio)?\s+([\d.]+(?:-[\w.]+)?)\s*(?:\(([^)]+)\))?", Opts);
    private static readonly Regex ReCpu = new(LineStart + @"CPU Name:\s*(.+)$", Opts);
    private static readonly Regex ReMemory = new(LineStart + @"Physical Memory:\s*(.+)$", Opts);
    private static readonly Regex ReWinVer = new(LineStart + @"Windows Version:\s*(.+)$", Opts);
    private static readonly Regex ReMacVer = new(LineStart + @"OS Name:\s*(.+)$", Opts);

    // OBS 实际用于渲染的适配器：32.x 打印 "Loading up D3D11 on adapter NVIDIA GeForce RTX 2080 Ti (0)"，
    // 末尾括号里是适配器序号，需要剥掉。
    private static readonly Regex ReGpuActive =
        new(@"Loading up (?:D3D11|OpenGL|Vulkan|Metal) on adapter\s+(.+?)\s*(?:\(\d+\))?\s*$", Opts);

    // 系统枚举出的适配器清单："  Adapter 0: NVIDIA GeForce RTX 2080 Ti"。
    // 必须要求 "Adapter" 后面跟编号：早期写法把序号段做成可选（Adapter\s*\d*:?\s*），
    // 于是 "Available Video Adapters: " 里的 "Adapters:" 被当成适配器，Gpu 被解析成垃圾值 "s:"，
    // 连带双显卡错位检测（依赖 Adapters 与 Gpu）在任何真实日志上都失效。
    private static readonly Regex ReGpuAdapter = new(@"Adapter\s+\d+\s*:\s*(.+?)\s*$", Opts);

    private static readonly Regex ReBaseRes = new(@"base resolution:\s*(\d+x\d+)", Opts);
    private static readonly Regex ReOutRes = new(@"output resolution:\s*(\d+x\d+)", Opts);
    private static readonly Regex ReFps = new(LineStart + @"fps:\s*([\d/.]+)", Opts);

    // 编码器标识行："[obs-nvenc: 'advanced_video_stream'] settings:"、"[obs_x264: ...] settings:"。
    // 两个历史缺陷：一是名单里没有 OBS 28 起 NVENC 的新模块名 obs-nvenc（旧名 jim_nvenc），
    // 二是没区分「设置块」与「[obs-nvenc] NVENC version: 13.0 …」这类模块自述行。
    // 这里要求行尾是 settings:，即 OBS 真正创建编码器时打印的那一行，语义明确且不会误配。
    private static readonly Regex ReEncoder = new(
        @"\[([^\]\n]{0,60}?(?:x264|nvenc|qsv|amf|av1|aom|svt|videotoolbox)[^\]\n]{0,60}?)\]\s*settings:", Opts);

    private static readonly Regex ReBitrate = new(LineStart + @"(?:bitrate|rate_control.*bitrate)[:=]\s*(\d+)", Opts);
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

    // V2.8 起共享给实时日志尾随（LogTailerService）使用，规则只在这一处维护
    internal static readonly LogRule[] Rules =
    {
        // —— 编码 / 性能 ——
        new() {
            Code = "LOG-ENC-OVERLOAD", Severity = LogSeverity.Error, ProblemId = "enc-overload",
            Pattern = new Regex(@"encoding overloaded|Encoder overload|skipped frames due to encoding lag", Opts),
            TitleKey = "log.rule.LOG-ENC-OVERLOAD.title",
            SuggestionKey = "log.rule.LOG-ENC-OVERLOAD.suggestion"
        },
        new() {
            Code = "LOG-ENC-NVENC", Severity = LogSeverity.Error, ProblemId = "en-nvenc",
            Pattern = new Regex(@"Failed to open NVENC codec|NVENC Error|nvEncOpenEncodeSessionEx failed|No capable devices found", Opts),
            TitleKey = "log.rule.LOG-ENC-NVENC.title",
            SuggestionKey = "log.rule.LOG-ENC-NVENC.suggestion"
        },
        new() {
            Code = "LOG-ENC-AMF-QSV", Severity = LogSeverity.Error, ProblemId = "enc-nvenc",
            Pattern = new Regex(@"Failed to create.*(AMF|QSV)|AMF Error|qsv encoder.*fail|Failed to initialize encoder", Opts),
            TitleKey = "log.rule.LOG-ENC-AMF-QSV.title",
            SuggestionKey = "log.rule.LOG-ENC-AMF-QSV.suggestion"
        },
        new() {
            Code = "LOG-ENC-AV1", Severity = LogSeverity.Warning, ProblemId = "enc-av1",
            Pattern = new Regex(@"AV1.*(not supported|unsupported|failed)", Opts),
            TitleKey = "log.rule.LOG-ENC-AV1.title",
            SuggestionKey = "log.rule.LOG-ENC-AV1.suggestion"
        },

        // —— 渲染 / 显卡 ——
        new() {
            Code = "LOG-GPU-INIT", Severity = LogSeverity.Critical, ProblemId = "cr-driver",
            Pattern = new Regex(@"Failed to initialize video|device_create.*[Ff]ailed|Failed to create D3D11 device|Your GPU may not be supported", Opts),
            TitleKey = "log.rule.LOG-GPU-INIT.title",
            SuggestionKey = "log.rule.LOG-GPU-INIT.suggestion"
        },
        new() {
            Code = "LOG-RENDER-LAG", Severity = LogSeverity.Warning, ProblemId = "lag-skip",
            Pattern = new Regex(@"lagged frames due to rendering lag", Opts),
            TitleKey = "log.rule.LOG-RENDER-LAG.title",
            SuggestionKey = "log.rule.LOG-RENDER-LAG.suggestion"
        },
        new() {
            Code = "LOG-CAPTURE-FAIL", Severity = LogSeverity.Error, ProblemId = "bs-game",
            Pattern = new Regex(@"\[game-capture[^\]]*\].*(failed|error)|Failed to open process|hook.*failed|could not create hook", Opts),
            TitleKey = "log.rule.LOG-CAPTURE-FAIL.title",
            SuggestionKey = "log.rule.LOG-CAPTURE-FAIL.suggestion"
        },
        new() {
            Code = "LOG-DSHOW", Severity = LogSeverity.Error, ProblemId = "bs-capturecard",
            Pattern = new Regex(@"\[dshow[^\]]*\].*(failed|could not|error)|Device '.*' failed to start|Failed to start capture", Opts),
            TitleKey = "log.rule.LOG-DSHOW.title",
            SuggestionKey = "log.rule.LOG-DSHOW.suggestion"
        },
        new() {
            Code = "LOG-CAPTURE-CARD", Severity = LogSeverity.Error, ProblemId = "bs-capturecard",
            Pattern = new Regex(@"decklink[^.\n]{0,60}(?:fail|error|invalid|timeout)|failed to open (?:the )?capture card", Opts),
            TitleKey = "log.rule.LOG-CAPTURE-CARD.title",
            SuggestionKey = "log.rule.LOG-CAPTURE-CARD.suggestion"
        },

        // —— 推流 / 网络 ——
        new() {
            Code = "LOG-STREAM-CONNECT", Severity = LogSeverity.Error, ProblemId = "sf-timeout",
            Pattern = new Regex(@"Failed to connect to server|Connection timed out|WSAETIMEDOUT|Could not connect to|socket error", Opts),
            TitleKey = "log.rule.LOG-STREAM-CONNECT.title",
            SuggestionKey = "log.rule.LOG-STREAM-CONNECT.suggestion"
        },
        new() {
            Code = "LOG-STREAM-AUTH", Severity = LogSeverity.Error, ProblemId = "sf-auth",
            Pattern = new Regex(@"Authentication failed|invalid stream key|NetStream\.Publish\.BadName|access denied|403", Opts),
            TitleKey = "log.rule.LOG-STREAM-AUTH.title",
            SuggestionKey = "log.rule.LOG-STREAM-AUTH.suggestion"
        },
        new() {
            Code = "LOG-STREAM-DROP", Severity = LogSeverity.Warning, ProblemId = "lag-network",
            Pattern = new Regex(@"dropped frames due to insufficient bandwidth|Output '.*' stopping.*reconnect|Reconnecting in \d+ second", Opts),
            TitleKey = "log.rule.LOG-STREAM-DROP.title",
            SuggestionKey = "log.rule.LOG-STREAM-DROP.suggestion"
        },
        new() {
            Code = "LOG-STREAM-DISCONNECT", Severity = LogSeverity.Error, ProblemId = "sf-drops",
            Pattern = new Regex(@"Disconnected from|The server has disconnected|connection closed by peer|RTMP.*disconnect", Opts),
            TitleKey = "log.rule.LOG-STREAM-DISCONNECT.title",
            SuggestionKey = "log.rule.LOG-STREAM-DISCONNECT.suggestion"
        },

        // —— 音频 ——
        new() {
            Code = "LOG-AUDIO-BUFFER", Severity = LogSeverity.Warning, ProblemId = "av-desync",
            // 「音频缓冲不断增长」的信号是总量在涨，而不是「出现过缓冲」：真实 OBS 日志每次启动
            // 都会打印一行 "adding 42 milliseconds of audio buffering, total audio buffering is now
            // 42 milliseconds"（实测 OBS 32.2.2），按原文匹配会让每个健康日志都报一条警告。
            // 这里改为只在总量 ≥ 100ms（真正值得关注的量）或 OBS 明确报出缓冲上限时才提示；
            // 「添加量 == 总量」这种一次性初始缓冲不再误报。
            Pattern = new Regex(
                @"adding \d+ milliseconds of audio buffering, total audio buffering is now [1-9]\d{2,} milliseconds|Max audio buffering reached",
                Opts),
            TitleKey = "log.rule.LOG-AUDIO-BUFFER.title",
            SuggestionKey = "log.rule.LOG-AUDIO-BUFFER.suggestion"
        },
        new() {
            Code = "LOG-AUDIO-DEVICE", Severity = LogSeverity.Error, ProblemId = "au-mic",
            Pattern = new Regex(@"WASAPI:.*(failed|error)|Failed to start audio|coreaudio.*failed|Device .* not found", Opts),
            TitleKey = "log.rule.LOG-AUDIO-DEVICE.title",
            SuggestionKey = "log.rule.LOG-AUDIO-DEVICE.suggestion"
        },
        new() {
            Code = "LOG-AUDIO-SYNC", Severity = LogSeverity.Warning, ProblemId = "av-drift",
            Pattern = new Regex(@"out of sync|resetting audio|audio timestamp", Opts),
            TitleKey = "log.rule.LOG-AUDIO-SYNC.title",
            SuggestionKey = "log.rule.LOG-AUDIO-SYNC.suggestion"
        },

        // —— 录制 ——
        new() {
            Code = "LOG-REC-WRITE", Severity = LogSeverity.Error, ProblemId = "rc-nofile",
            Pattern = new Regex(@"Unable to write to|Error opening file|No space left on device|failed to open output file|Could not open file", Opts),
            TitleKey = "log.rule.LOG-REC-WRITE.title",
            SuggestionKey = "log.rule.LOG-REC-WRITE.suggestion"
        },
        new() {
            Code = "LOG-HYBRID-MP4", Severity = LogSeverity.Info, ProblemId = "rc-hybrid-mp4",
            Pattern = new Regex(@"hybrid[_ -]?mp4|hybrid[_ -]?mov", Opts),
            TitleKey = "log.rule.LOG-HYBRID-MP4.title",
            SuggestionKey = "log.rule.LOG-HYBRID-MP4.suggestion"
        },
        new() {
            Code = "LOG-VIRTUALCAM", Severity = LogSeverity.Error, ProblemId = "st-virtualcam",
            Pattern = new Regex(@"virtual[_ -]?cam(?:era)?.{0,40}(?:fail(?:ed)?|error)|failed to start virtual camera", Opts),
            TitleKey = "log.rule.LOG-VIRTUALCAM.title",
            SuggestionKey = "log.rule.LOG-VIRTUALCAM.suggestion"
        },

        // —— 插件 / 崩溃 ——
        new() {
            Code = "LOG-PLUGIN", Severity = LogSeverity.Warning, ProblemId = "cr-plugin",
            Pattern = new Regex(@"os_dlopen\(.*\)\s*failed|os_dlopen.*(?:failed|could not|无法|拒绝|找不到)|Module '.*' not loaded|Failed to load '.*' plugin|LoadLibrary failed", Opts),
            TitleKey = "log.rule.LOG-PLUGIN.title",
            SuggestionKey = "log.rule.LOG-PLUGIN.suggestion"
        },
        new() {
            Code = "LOG-PLUGIN-STREAMFX", Severity = LogSeverity.Info, ProblemId = "cr-streamfx",
            Pattern = new Regex(@"streamfx", Opts),
            TitleKey = "log.rule.LOG-PLUGIN-STREAMFX.title",
            SuggestionKey = "log.rule.LOG-PLUGIN-STREAMFX.suggestion"
        },
        new() {
            Code = "LOG-PLUGIN-MULTI-RTMP", Severity = LogSeverity.Info, ProblemId = "st-multi-rtmp",
            Pattern = new Regex(@"obs-multi-rtmp|multi[_ -]?rtmp", Opts),
            TitleKey = "log.rule.LOG-PLUGIN-MULTI-RTMP.title",
            SuggestionKey = "log.rule.LOG-PLUGIN-MULTI-RTMP.suggestion"
        },
        new() {
            Code = "LOG-VCREDIST", Severity = LogSeverity.Critical, ProblemId = "cr-vcredist",
            Pattern = new Regex(@"VCRUNTIME|MSVCP\d+\.dll|api-ms-win-crt|The specified module could not be found", Opts),
            TitleKey = "log.rule.LOG-VCREDIST.title",
            SuggestionKey = "log.rule.LOG-VCREDIST.suggestion"
        },
        new() {
            Code = "LOG-CRASH", Severity = LogSeverity.Critical, ProblemId = "cr-safe-mode",
            Pattern = new Regex(@"Unhandled exception|EXCEPTION_ACCESS_VIOLATION|c0000005|Crash Report|signal 11|SIGSEGV", Opts),
            TitleKey = "log.rule.LOG-CRASH.title",
            SuggestionKey = "log.rule.LOG-CRASH.suggestion"
        },
        new() {
            Code = "LOG-ADMIN", Severity = LogSeverity.Info, ProblemId = "bs-display",
            Pattern = new Regex(@"Running as administrator:\s*false", Opts),
            TitleKey = "log.rule.LOG-ADMIN.title",
            SuggestionKey = "log.rule.LOG-ADMIN.suggestion"
        },
        new() {
            Code = "LOG-SAFEMODE", Severity = LogSeverity.Info,
            Pattern = new Regex(@"Safe Mode enabled|--safe-mode", Opts),
            TitleKey = "log.rule.LOG-SAFEMODE.title",
            SuggestionKey = "log.rule.LOG-SAFEMODE.suggestion"
        },

        // —— 双显卡 / 集成显卡 ——
        new() {
            Code = "LOG-GPU-HYBRID", Severity = LogSeverity.Warning, ProblemId = "bs-display",
            Pattern = new Regex(@"Intel\(R\)\s+(?:UHD|HD Graphics|Iris)|AMD Radeon\(TM\) Graphics\b", Opts),
            TitleKey = "log.rule.LOG-GPU-HYBRID.title",
            SuggestionKey = "log.rule.LOG-GPU-HYBRID.suggestion"
        },

        // —— 音频采样率 ——
        new() {
            Code = "LOG-AUDIO-SAMPLERATE", Severity = LogSeverity.Warning, ProblemId = "av-desync",
            Pattern = new Regex(@"sample rate(?:s)?[^.\n]{0,40}(?:don't match|doesn't match|mismatch|differ)", Opts),
            TitleKey = "log.rule.LOG-AUDIO-SAMPLERATE.title",
            SuggestionKey = "log.rule.LOG-AUDIO-SAMPLERATE.suggestion"
        },
        new() {
            Code = "LOG-AUDIO-RESAMPLE", Severity = LogSeverity.Warning, ProblemId = "au-sample-mismatch",
            Pattern = new Regex(@"\bresampl(?:ing|ed|er)\b|Failed to initialize audio resampler", Opts),
            TitleKey = "log.rule.LOG-AUDIO-RESAMPLE.title",
            SuggestionKey = "log.rule.LOG-AUDIO-RESAMPLE.suggestion"
        },

        // —— 色彩 / 画质（V2.7）——
        new() {
            Code = "LOG-COLOR-RANGE", Severity = LogSeverity.Info, ProblemId = "cf-colorrange",
            // OBS 30 前后的日志格式变了：老版打印独立的 "color range: Full" / "color space: Rec. 709"，
            // 32.x 合并成一行 "YUV mode:          Rec. 709/Partial"（色彩空间/范围）。
            // 只认老格式的话，新版日志里这条规则永远不会命中，色彩范围体检在日志侧等于失效。
            Pattern = new Regex(@"(?:color|colour)[_ ]?range:?\s*full\b|yuv\s+mode:\s*[^\n]*?/\s*full\b", Opts),
            TitleKey = "log.rule.LOG-COLOR-RANGE.title",
            SuggestionKey = "log.rule.LOG-COLOR-RANGE.suggestion"
        },

        new() {
            Code = "LOG-PLUGIN-DUPLICATE", Severity = LogSeverity.Warning, ProblemId = "cr-plugin",
            // 实测真实日志：同一插件存在两份副本时 OBS 会告警
            // "Dock id 'obs-helper-dock' already used!  Duplicate library?"
            Pattern = new Regex(@"Duplicate library|Dock id '[^']+' already used", Opts),
            TitleKey = "log.rule.LOG-PLUGIN-DUPLICATE.title",
            SuggestionKey = "log.rule.LOG-PLUGIN-DUPLICATE.suggestion"
        },

        // —— 推流网络补充（V2.7）——
        new() {
            Code = "LOG-BITRATE-DROP", Severity = LogSeverity.Warning, ProblemId = "lag-dynamic-bitrate",
            Pattern = new Regex(@"dynamic bitrate|bitrate[^.\n]{0,30}(?:reduced|lowered|dropp?ing)", Opts),
            TitleKey = "log.rule.LOG-BITRATE-DROP.title",
            SuggestionKey = "log.rule.LOG-BITRATE-DROP.suggestion"
        },

        // —— 推流密钥泄漏风险 ——
        new() {
            Code = "LOG-STREAMKEY-LEAK", Severity = LogSeverity.Warning, ProblemId = "sf-auth",
            Pattern = new Regex(@"stream[_-]?key\s*[:=]", Opts),
            TitleKey = "log.rule.LOG-STREAMKEY-LEAK.title",
            SuggestionKey = "log.rule.LOG-STREAMKEY-LEAK.suggestion"
        },

        // —— 崩溃肇事模块 ——
        new() {
            Code = "LOG-CRASH-MODULE", Severity = LogSeverity.Critical, ProblemId = "cr-plugin",
            Pattern = new Regex(@"(?:faulting module|fault module|crashed module|module that caused)[^:\n]*:\s*([^\s]+)|Exception Module Name:\s*([^\s]+)", Opts),
            TitleKey = "log.rule.LOG-CRASH-MODULE.title",
            SuggestionKey = "log.rule.LOG-CRASH-MODULE.suggestion"
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

        // V3.0.0（F4）：脱敏全文交给 report 懒构造，这里不再顺手攒一份逐行结果列表 ——
        // 扫描过程只用得到当前行，扫完把原文交给报告即可。常驻内存从
        // 「原文 + 行列表 + 拼好的全文」三份降到一份，分析结论一字不变。
        report.SetSanitizedSource(rawText);

        var found = new Dictionary<string, LogFinding>(StringComparer.Ordinal);
        int lineNo = 0;

        foreach (var rawLine in LogSanitizer.SplitLines(rawText))
        {
            lineNo++;
            var line = LogSanitizer.SanitizeLine(rawLine);
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

        if ((m = ReGpuActive.Match(line)).Success)
        {
            var active = CleanAdapterName(m.Groups[1].Value);
            if (s.Gpu.Length == 0) s.Gpu = active;
            // 实际渲染的适配器同样计入清单：一是保证 Adapters 一定包含 Gpu
            // （日志被截断时可能没有枚举段），二是视频重置 / 切换渲染器时 OBS 会重复打印该行。
            AddAdapter(s.Adapters, active);
        }

        // B3：收集日志中枚举到的全部适配器（去重，上限 8 个），供双显卡错位检测
        if ((m = ReGpuAdapter.Match(line)).Success)
        {
            AddAdapter(s.Adapters, CleanAdapterName(m.Groups[1].Value));
        }

        if (s.BaseResolution.Length == 0 && (m = ReBaseRes.Match(line)).Success) s.BaseResolution = m.Groups[1].Value;
        if (s.OutputResolution.Length == 0 && (m = ReOutRes.Match(line)).Success) s.OutputResolution = m.Groups[1].Value;
        if (s.Fps.Length == 0 && (m = ReFps.Match(line)).Success) s.Fps = NormalizeFps(m.Groups[1].Value);
        if (s.AudioSampleRate.Length == 0 && (m = ReSampleRate.Match(line)).Success) s.AudioSampleRate = m.Groups[1].Value + " Hz";
        if (s.VideoEncoder.Length == 0 && (m = ReEncoder.Match(line)).Success)
        {
            // 只取冒号前的编码器标识（"obs-nvenc: 'advanced_video_stream'" → "obs-nvenc"）：
            // 分诊里的 Contains 判定与界面展示都只需要编码器名，整段原样带出去太啰嗦。
            s.VideoEncoder = m.Groups[1].Value.Split(':')[0].Trim();
        }
        if (s.Bitrate == 0 && (m = ReBitrate.Match(line)).Success &&
            int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var br))
        {
            s.Bitrate = br;
        }

        if ((m = ReRenderLag.Match(line)).Success) s.RenderLagRatio = ParsePercent(m.Groups[2].Value);
        if ((m = ReEncodeLag.Match(line)).Success) s.EncodingLagRatio = ParsePercent(m.Groups[3].Value);
        if ((m = ReNetDrop.Match(line)).Success) s.NetworkDropRatio = ParsePercent(m.Groups[2].Value);
    }

    /// <summary>
    /// 清洗适配器名：去首尾空白与引号，并剥掉末尾可能残留的驱动序号括号（如 "NVIDIA ... (0)"），
    /// 让界面展示与 ReDiscreteGpu / ReIntegratedGpu 的关键字判定保持一致。
    /// </summary>
    private static string CleanAdapterName(string raw)
    {
        var s = raw.Trim().Trim('"').Trim();
        // 只剥「名字最末尾」的一层 "(数字)"：Intel(R) UHD Graphics 630 这类合法名字不受影响。
        return ReAdapterIndexSuffix.Replace(s, "").Trim();
    }

    /// <summary>适配器名末尾的驱动序号括号（"… (0)"）。独立成静态字段而不是内联 Regex.Replace：
    /// V3.0.0（F4）让这条同样吃编译缓存，不必每次走 BCL 的解释版静态缓存。</summary>
    private static readonly Regex ReAdapterIndexSuffix = new(@"\s*\(\d+\)\s*$", Opts);

    /// <summary>把适配器并入清单：去重（大小写不敏感）、忽略空值、上限 8 个。</summary>
    private static void AddAdapter(List<string> adapters, string name)
    {
        if (name.Length == 0 || adapters.Count >= 8) return;
        if (adapters.Contains(name, StringComparer.OrdinalIgnoreCase)) return;
        adapters.Add(name);
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

        AddRatio(found, "LOG-STAT-RENDER", s.RenderLagRatio, Localization.Strings.T("log.rule.LOG-STAT-RENDER.label"),
            "lag-skip", Localization.Strings.T("log.rule.LOG-STAT-RENDER.fix"));

        AddRatio(found, "LOG-STAT-ENCODE", s.EncodingLagRatio, Localization.Strings.T("log.rule.LOG-STAT-ENCODE.label"),
            "enc-overload", Localization.Strings.T("log.rule.LOG-STAT-ENCODE.fix"));

        AddRatio(found, "LOG-STAT-NETWORK", s.NetworkDropRatio, Localization.Strings.T("log.rule.LOG-STAT-NETWORK.label"),
            "lag-network", Localization.Strings.T("log.rule.LOG-STAT-NETWORK.fix"));
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
            Evidence = Localization.Strings.T("log.stat.evidence", label, (ratio * 100).ToString("0.##", CultureInfo.InvariantCulture)),
            FirstLine = 0
        };
    }

    private static string Trim(string line)
        => line.Length <= MaxEvidenceLength ? line : line[..MaxEvidenceLength] + "…";

    // --------------------------------------- B1/B2/B3/A1：分诊与联动（V2.5）

    /// <summary>
    /// 丢帧三分类 → 对应知识库条目。
    ///
    /// V3.0（F5）：由 <c>static readonly</c> 数组改为**按需构造** ——
    /// 静态字段在类型初始化时就把文案冻住了，切语言后这里仍是旧语言（三处文案一起错）。
    /// 只有三条，每次判定重建的代价可以忽略。
    /// </summary>
    private static (string Code, string Label, string ProblemId, string Fix)[] DropKinds() => new[]
    {
        ("LOG-STAT-RENDER",  Localization.Strings.T("log.drop.kind.render"), "lag-skip",
            Localization.Strings.T("log.drop.kind.render.fix")),
        ("LOG-STAT-ENCODE",  Localization.Strings.T("log.drop.kind.encode"), "enc-overload",
            Localization.Strings.T("log.drop.kind.encode.fix")),
        ("LOG-STAT-NETWORK", Localization.Strings.T("log.drop.kind.network"), "lag-network",
            Localization.Strings.T("log.drop.kind.network.fix")),
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
        var meaningful = DropKinds()
            .Select(k => (Kind: k, Ratio: RatioOf(s, k.Code)))
            .Where(x => x.Ratio > 0.005)
            .ToList();
        if (meaningful.Count == 0) return;

        var dominant = meaningful.OrderByDescending(x => x.Ratio).First().Kind;
        var evidence = string.Join(Localization.Strings.T("log.drop.evidenceJoin"), meaningful.Select(x => $"{x.Kind.Label} " + (x.Ratio * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%"));
        var maxRatio = meaningful.Max(x => x.Ratio);

        found["LOG-DROP-DOMINANT"] = new LogFinding
        {
            Code = "LOG-DROP-DOMINANT",
            Severity = DropSeverity(maxRatio),
            Title = Localization.Strings.T("log.drop.title", dominant.Label),
            Suggestion = dominant.Fix + Localization.Strings.T("log.drop.suffix"),
            ProblemId = dominant.ProblemId,
            Evidence = Localization.Strings.T("log.drop.evidence", evidence),
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
                ? Localization.Strings.T("log.triage.step1.hw")
                : Localization.Strings.T("log.triage.step1.hwVendor", hw.Value.Vendor, hw.Value.EncoderName));
            steps.Add(Localization.Strings.T("log.triage.step2.x264"));
        }
        else if (enc.Contains("nvenc") || enc.Contains("jim"))
        {
            steps.Add(Localization.Strings.T("log.triage.step1.nvenc"));
            steps.Add(Localization.Strings.T("log.triage.step2.nvenc"));
        }
        else if (enc.Contains("qsv") || enc.Contains("amf"))
        {
            steps.Add(Localization.Strings.T("log.triage.step1.driver"));
            steps.Add(Localization.Strings.T("log.triage.step2.gpuBusy"));
        }

        if (double.TryParse(s.Fps, NumberStyles.Float, CultureInfo.InvariantCulture, out var fps) && fps >= 50)
            steps.Add(Localization.Strings.T("log.triage.stepN.fps", steps.Count + 1, s.Fps));

        if (TryParseHeight(s.OutputResolution, out var h) && h >= 1000)
            steps.Add(Localization.Strings.T("log.triage.stepN.res", steps.Count + 1, s.OutputResolution));

        steps.Add(Localization.Strings.T("log.triage.stepN.cleanup", steps.Count + 1));

        found["LOG-TRIAGE-ENCODE"] = new LogFinding
        {
            Code = "LOG-TRIAGE-ENCODE",
            Severity = LogSeverity.Info,
            Title = Localization.Strings.T("log.triage.title"),
            Suggestion = string.Join("\n", steps),
            ProblemId = "enc-overload",
            Evidence = Localization.Strings.T("log.triage.evidence",
                string.IsNullOrEmpty(s.VideoEncoder) ? Localization.Strings.T("common.unknown") : s.VideoEncoder,
                string.IsNullOrEmpty(s.Fps) ? Localization.Strings.T("common.unknown") : s.Fps,
                string.IsNullOrEmpty(s.OutputResolution) ? Localization.Strings.T("common.unknown") : s.OutputResolution),
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
                Title = Localization.Strings.T("log.gpu.dual.title"),
                Suggestion = Localization.Strings.T("log.gpu.dual.suggestion"),
                ProblemId = "bs-dualgpu",
                Evidence = Localization.Strings.T("log.gpu.evidence", adapterText, s.Gpu),
                FirstLine = 0
            };
        }
        else if (IsDiscrete(s.Gpu) && others.Any(IsIntegrated))
        {
            found["LOG-GPU-DUAL-OK"] = new LogFinding
            {
                Code = "LOG-GPU-DUAL-OK",
                Severity = LogSeverity.Info,
                Title = Localization.Strings.T("log.gpu.dualOk.title"),
                Suggestion = Localization.Strings.T("log.gpu.dualOk.suggestion"),
                ProblemId = "bs-dualgpu",
                Evidence = Localization.Strings.T("log.gpu.evidence", adapterText, s.Gpu),
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
            Title = Localization.Strings.T("log.pluginObsVer.title", report.Summary.ObsVersion),
            Suggestion = Localization.Strings.T("log.pluginObsVer.suggestion"),
            ProblemId = "cr-plugin-load",
            Evidence = Localization.Strings.T("log.pluginObsVer.evidence", report.Summary.ObsVersion),
            FirstLine = 0
        };
    }
}