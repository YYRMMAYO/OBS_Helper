using OBS_Helper.Wpf.Services.Obs;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// OBS 32.2.2 真实日志的解析回归（V2.9.0）。
///
/// 背景：早期规则把头部字段写成 <c>^</c> 纯行首锚定，但真实 OBS 日志每行都带
/// <c>HH:mm:ss.mmm: </c> 前缀，于是这些规则在真实日志上永远匹配不到 ——
/// 用 OBS 32.2.2 的真实日志实测：CPU / 内存 / 系统版本 / 帧率 / 码率全部为空，
/// 显卡被解析成垃圾值 "s:"（"Available Video Adapters:" 被当成适配器名），
/// 连带依赖 Gpu 与 Adapters 的双显卡错位检测、编码过载分诊一起失效。
///
/// 本文件里的日志片段全部逐字取自或严格对齐 OBS 32.2.2 的真实输出格式
/// （含时间戳前缀、Tab 缩进的二级字段），而不是省略前缀的理想化写法。
/// </summary>
public class ObsLogAnalyzerOBS322Tests
{
    private readonly ObsLogAnalyzer _analyzer = new();

    /// <summary>对齐 OBS 32.2.2 启动段的真实日志（含时间戳前缀）。</summary>
    private const string StartupLog = """
        19:41:42.564: CPU Name: AMD Ryzen 9 3900X 12-Core Processor
        19:41:42.564: CPU Speed: 3800MHz
        19:41:42.564: Physical Memory: 32675MB Total, 21992MB Free
        19:41:42.564: Windows Version: 10.0 Build 19045 (release: 22H2; revision: 6456; 64-bit)
        19:41:42.568: Qt Version: 6.11.1 (runtime), 6.11.1 (compiled)
        19:41:42.882: OBS 32.2.2 (64-bit, windows)
        19:41:42.891: Available Video Adapters:
        19:41:42.893: 	Adapter 0: NVIDIA GeForce RTX 2080 Ti
        19:41:42.894: 	Adapter 1: Intel(R) UHD Graphics 630
        19:41:42.894: Loading up D3D11 on adapter NVIDIA GeForce RTX 2080 Ti (0)
        19:41:43.144: video settings reset:
        19:41:43.144: 	base resolution:   1920x1080
        19:41:43.144: 	output resolution: 1920x1080
        19:41:43.144: 	fps:               60/1
        19:41:43.144: 	format:            NV12
        19:41:43.144: 	YUV mode:          Rec. 709/Partial
        19:41:42.886: audio settings reset:
        19:41:42.886: 	samples per sec: 48000
        """;

    [Fact]
    public void StartupLog_ParsesRealTimestampPrefixedHeaderFields()
    {
        var s = _analyzer.Analyze(StartupLog).Summary;

        Assert.Equal("32.2.2", s.ObsVersion);
        Assert.Equal("64-bit, windows", s.Platform);
        Assert.Equal("AMD Ryzen 9 3900X 12-Core Processor", s.Cpu);
        Assert.Equal("32675MB Total, 21992MB Free", s.Memory);
        Assert.Equal("10.0 Build 19045 (release: 22H2; revision: 6456; 64-bit)", s.OsVersion);
        Assert.Equal("1920x1080", s.BaseResolution);
        Assert.Equal("1920x1080", s.OutputResolution);
        Assert.Equal("60", s.Fps);
        Assert.Equal("48000 Hz", s.AudioSampleRate);
    }

    [Fact]
    public void StartupLog_ExtractsRenderingAdapterWithoutGarbageValues()
    {
        var s = _analyzer.Analyze(StartupLog).Summary;

        // 回归点：历史上这里是 "s:"（"Available Video Adapters:" 被误配成适配器名）
        Assert.Equal("NVIDIA GeForce RTX 2080 Ti", s.Gpu);
        Assert.Equal(new[] { "NVIDIA GeForce RTX 2080 Ti", "Intel(R) UHD Graphics 630" }, s.Adapters);
    }

    [Fact]
    public void StartupLog_DetectsDualGpuRunningOnDiscrete()
    {
        var report = _analyzer.Analyze(StartupLog);

        var dual = report.Findings.FirstOrDefault(f => f.Code == "LOG-GPU-DUAL-OK");
        Assert.NotNull(dual);
        Assert.Null(report.Findings.FirstOrDefault(f => f.Code == "LOG-GPU-DUAL"));
    }

    [Fact]
    public void StartupLog_IntegratedRendering_IsReportedAsMisplacement()
    {
        // 同一台机器，但 OBS 跑在核显上 —— 这是双显卡笔记本最典型的黑屏/掉帧根因
        var log = StartupLog.Replace(
            "Loading up D3D11 on adapter NVIDIA GeForce RTX 2080 Ti (0)",
            "Loading up D3D11 on adapter Intel(R) UHD Graphics 630 (0)");
        var report = _analyzer.Analyze(log);

        var misplacement = report.Findings.FirstOrDefault(f => f.Code == "LOG-GPU-DUAL");
        Assert.NotNull(misplacement);
        Assert.Equal("bs-dualgpu", misplacement!.ProblemId);
        Assert.Equal(LogSeverity.Warning, misplacement.Severity);
    }

    [Fact]
    public void StreamSession_ParsesEncoderAndBitrateFromSettingsBlock()
    {
        var log = StartupLog + "\n" +
            """
            19:41:57.100: [obs-nvenc] NVENC version: 13.0 (compiled) / 13.1 (driver), CUDA driver version: 13.30, AV1 supported: false
            19:41:58.100: [obs-nvenc: 'advanced_video_stream'] settings:
            19:41:58.100: 	codec:          H264
            19:41:58.100: 	bitrate:        6000
            19:41:58.100: 	keyint:         120
            """;
        var s = _analyzer.Analyze(log).Summary;

        // 回归点：历史上编码器/码率在真实日志里永远解析为空。
        // 编码器只取标识名（不含 ": '输出名'"），便于界面展示与分诊判定。
        Assert.Equal("obs-nvenc", s.VideoEncoder);
        Assert.Equal(6000, s.Bitrate);
    }

    [Fact]
    public void StreamSession_ModuleVersionLineIsNotMistakenForEncoderSettings()
    {
        // "[obs-nvenc] NVENC version: …" 是模块自述行，不能当成「正在使用的编码器设置块」
        var report = _analyzer.Analyze(
            "19:41:57.100: [obs-nvenc] NVENC version: 13.0 (compiled) / 13.1 (driver), AV1 supported: false");
        Assert.Empty(report.Summary.VideoEncoder);
        Assert.Equal(0, report.Summary.Bitrate);
    }

    [Fact]
    public void StreamSession_EncoderTriageUsesDetectedEncoder()
    {
        var log = StartupLog + "\n" +
            """
            19:41:58.100: [obs-nvenc: 'advanced_video_stream'] settings:
            19:41:58.100: 	bitrate:        6000
            19:42:30.100: Video stopped, number of skipped frames due to encoding lag: 216/3600 (6.0%)
            """;
        var triage = _analyzer.Analyze(log).Findings.First(f => f.Code == "LOG-TRIAGE-ENCODE");

        // 已识别到 NVENC 时给出的应是「降 NVENC 预设」，而不是「改用硬件编码」
        Assert.Contains("NVENC 预设", triage.Suggestion);
    }

    [Fact]
    public void YuvModeFull_HitsColorRangeRule_AndPartialDoesNot()
    {
        var full = _analyzer.Analyze("19:41:43.144: 	YUV mode:          Rec. 709/Full");
        Assert.Contains(full.Findings, f => f.Code == "LOG-COLOR-RANGE");

        // StartupLog 里是 Partial，不应命中
        Assert.DoesNotContain(_analyzer.Analyze(StartupLog).Findings, f => f.Code == "LOG-COLOR-RANGE");
    }

    [Fact]
    public void LegacyColorRangeLine_StillHitsColorRangeRule()
    {
        // 老版日志（OBS 30 之前）单独打印 color range，规则需同时兼容两种格式
        var report = _analyzer.Analyze("22:10:02.001: color range: Full");
        Assert.Contains(report.Findings, f => f.Code == "LOG-COLOR-RANGE");
    }

    [Fact]
    public void DuplicatePluginInstall_IsReported()
    {
        // 取自真实日志：同一插件存在两份副本时 OBS 的告警
        var report = _analyzer.Analyze(
            "19:41:43.966: Dock id 'obs-helper-dock' already used!  Duplicate library?");

        var finding = report.Findings.FirstOrDefault(f => f.Code == "LOG-PLUGIN-DUPLICATE");
        Assert.NotNull(finding);
        Assert.Equal(LogSeverity.Warning, finding!.Severity);
        Assert.Equal("cr-plugin", finding.ProblemId);
    }

    [Fact]
    public void HealthyStartupLog_ProducesNoCriticalOrErrorFindings()
    {
        // 一台正常机器（无硬件卡、无直播）的启动日志不应报错：确保新增解析不引入误报
        var report = _analyzer.Analyze(StartupLog);
        Assert.DoesNotContain(report.Findings, f => f.Severity >= LogSeverity.Error);
    }

    [Fact]
    public void InitialAudioBufferingLine_DoesNotWarn()
    {
        // 逐字取自真实 32.2.2 日志：启动时的一次性初始缓冲（添加量 == 总量）不是「不断增长」，
        // 旧规则按原文匹配会让每个健康日志都报一条警告 —— 这里钉死不再误报。
        var report = _analyzer.Analyze(
            "19:36:45.090: adding 42 milliseconds of audio buffering, total audio buffering is now 42 milliseconds (source: 音频输入采集)");
        Assert.DoesNotContain(report.Findings, f => f.Code == "LOG-AUDIO-BUFFER");
    }

    [Fact]
    public void GrowingAudioBuffering_StillWarns()
    {
        var report = _analyzer.Analyze(
            "20:00:05.000: adding 300 milliseconds of audio buffering, total audio buffering is now 300 milliseconds (source: 麦克风/Aux)");
        var finding = report.Findings.FirstOrDefault(f => f.Code == "LOG-AUDIO-BUFFER");
        Assert.NotNull(finding);
        Assert.Equal(LogSeverity.Warning, finding!.Severity);
        Assert.Equal("av-desync", finding.ProblemId);

        // OBS 明确报出缓冲上限同样要提示
        Assert.Contains(
            _analyzer.Analyze("Max audio buffering reached!").Findings,
            f => f.Code == "LOG-AUDIO-BUFFER");
    }
}
