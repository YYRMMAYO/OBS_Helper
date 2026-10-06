using System.Linq;
using OBS_Helper.Wpf.Services.ObsConfig;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// V2.9.4：简单录像的纯逻辑（预设 / 落地项 / 就绪判定 / 进度估算）。
///
/// 这一块最怕的错法：
/// <list type="bullet">
///   <item>把「怎么编码」也写进落地项（推流参数、输出模式、编码器、码率）——用户自己的选择被改掉；</item>
///   <item>预设的 fps / 分段没有真正生效（沿用全局默认 60、或把分段泄漏到非游戏档）；</item>
///   <item>就绪判定的优先级写反（正在推流却提示「去开 WebSocket」；有明确原因却给通用文案）；</item>
///   <item>进度估算在 0 码率上除零、或把负数空间当成「还有很多空间」。</item>
/// </list>
///
/// 以下口径由生产端 <c>SimpleRecordingCore</c> 确认（测试按实现钉死，改实现须同步改这里）：
/// <list type="number">
///   <item>落地项键顺序固定 <c>format, quality, path, canvas, fps, recTracks, sampleRate</c>，
///     <c>preset.SplitSeconds &gt; 0</c> 时最后追加 <c>split</c>；
///     <c>RecSplitFileSize</c> 只在 <c>SplitSizeMb &gt; 0</c> 时写；</item>
///   <item><c>BuildItems</c> 的基准四项直接来自 <c>RecordingEnvCore.StaticTargets(snapshot, preset.Fps)</c>，
///     fps 用预设值而不是全局 60；</item>
///   <item><c>Check</c> 优先级：<c>blockedReason</c>（非空即用）→ <c>streaming||recording</c> →
///     <c>!viaWebSocket &amp;&amp; !canWriteFiles</c>（用 <c>noChannelReason</c> 覆盖文案）→
///     <c>failCount&gt;0</c> → <c>warnCount&gt;0</c>（不阻断，只给 Hint）→ Ready。
///     <c>obsProcessRunning</c> 只作展示：能不能写文件由调用方用 <c>canWriteFiles</c> 表达；</item>
///   <item>硬阻断时只回传阻断原因（Hint / WarnCount / FailCount 都归零）；
///     不硬编码文案键，只断言「非空 / 两两不同 / 原样透传」，键集一致性由 StringsTests 覆盖；</item>
///   <item><c>Estimate</c>：负数输入归零，<c>bitrateKbps &lt;= 0</c> 时剩余 0 且不算低空间，
///     <c>LowSpace</c> 用「严格小于 10 分钟」；</item>
///   <item><c>Parse</c> 先去空白、再大小写不敏感匹配 key，也接受枚举名（含 <c>SimplePresetId.X</c>）。</item>
/// </list>
///
/// 本文件只用纯字符串与内存对象：不碰注册表、不碰文件系统、不启动进程。
/// </summary>
public class SimpleRecordingCoreTests
{
    private const string RecPath = @"E:\Recordings";

    private static RecordingEnvSnapshot Snapshot() => new()
    {
        OutputMode = "Simple",
        RecFormat = "mkv",
        RecQuality = "Stream",
        RecPath = @"D:\OldRec",
        BaseCx = "1280",
        BaseCy = "720",
        OutCx = "1280",
        OutCy = "720",
        FpsCommon = "30",
        SampleRate = "44100",
        RecTracks = "2",
        RecSplitFile = "false",
    };

    /// <summary>造一个取值刻意远离内置默认（60fps / 常见分段）的预设，用来发现「预设没生效」。</summary>
    private static SimplePreset Preset(
        SimplePresetId id, int fps = 60, int splitSeconds = 0, int kbps = 20000, int splitSizeMb = 0)
        => new(id, id.ToString().ToLowerInvariant(), fps, splitSeconds, kbps, splitSizeMb);

    /// <summary>把目标键值对拍平成可排序字符串，比较时不受 Targets 排列顺序影响。</summary>
    private static List<string> Flat(IEnumerable<RecordingEnvTarget> targets)
        => targets.Select(t => t.Category + "|" + t.Parameter + "|" + t.Value)
                  .OrderBy(s => s, StringComparer.Ordinal)
                  .ToList();

    private static SimpleReadyCheck Check(
        bool viaWebSocket = true,
        bool canWriteFiles = true,
        bool obsProcessRunning = false,
        bool streaming = false,
        bool recording = false,
        int failCount = 0,
        int warnCount = 0,
        string? blockedReason = null,
        string? noChannelReason = null)
        => SimpleRecordingCore.Check(
            viaWebSocket, canWriteFiles, obsProcessRunning, streaming, recording,
            failCount, warnCount, blockedReason, noChannelReason);

    // ------------------------------------------------------------ 预设

    [Fact]
    public void Presets_AreExactlyQuickMeetingGameInThatOrder()
    {
        Assert.Equal(3, SimpleRecordingCore.Presets.Count);
        Assert.Equal(
            new[] { SimplePresetId.Quick, SimplePresetId.Meeting, SimplePresetId.Game },
            SimpleRecordingCore.Presets.Select(p => p.Id));
    }

    [Fact]
    public void Presets_FollowTheDocumentedFpsTiers()
    {
        // 「会议 / 网课」档的卖点就是 30fps 省资源；两档写反了用户不会立刻发现，只会觉得「怎么更卡了」
        Assert.Equal(60, SimpleRecordingCore.Get(SimplePresetId.Quick).Fps);
        Assert.Equal(30, SimpleRecordingCore.Get(SimplePresetId.Meeting).Fps);
        Assert.Equal(60, SimpleRecordingCore.Get(SimplePresetId.Game).Fps);
    }

    [Fact]
    public void Presets_OnlyTheGamePresetTurnsOnSplitting()
    {
        var quick = SimpleRecordingCore.Get(SimplePresetId.Quick);
        var meeting = SimpleRecordingCore.Get(SimplePresetId.Meeting);
        var game = SimpleRecordingCore.Get(SimplePresetId.Game);

        Assert.Equal(0, quick.SplitSeconds);
        Assert.Equal(0, meeting.SplitSeconds);
        Assert.True(game.SplitSeconds > 0);
        Assert.True(game.SplitSizeMb > 0);

        // 分段是「游戏档默认开」的：其它档写 SplitSeconds 就会把普通用户的单文件录像切成多段
        Assert.DoesNotContain(
            SimpleRecordingCore.BuildItems(quick, Snapshot(), RecPath), i => i.Key == "split");
        Assert.DoesNotContain(
            SimpleRecordingCore.BuildItems(meeting, Snapshot(), RecPath), i => i.Key == "split");
        Assert.Contains(
            SimpleRecordingCore.BuildItems(game, Snapshot(), RecPath), i => i.Key == "split");
    }

    [Fact]
    public void Presets_AllDeclareSaneNumbers()
    {
        foreach (var preset in SimpleRecordingCore.Presets)
        {
            Assert.True(preset.Fps > 0, preset.Key + " 的 fps 必须为正");
            Assert.True(preset.EstimatedBitrateKbps > 0, preset.Key + " 的估算码率必须为正");
            Assert.True(preset.SplitSeconds >= 0, preset.Key + " 的分段秒数不能为负");
            Assert.True(preset.SplitSizeMb >= 0, preset.Key + " 的分段大小不能为负");
        }
    }

    [Fact]
    public void Presets_HaveUniqueNonEmptyKeysThatParseBackToThemselves()
    {
        var keys = SimpleRecordingCore.Presets.Select(p => p.Key).ToList();
        Assert.All(keys, k => Assert.False(string.IsNullOrWhiteSpace(k)));
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // 界面上持久化的是 Key：存下去再用 Parse 读回来，必须还是同一档
        foreach (var preset in SimpleRecordingCore.Presets)
            Assert.Equal(preset.Id, SimpleRecordingCore.Parse(preset.Key));
    }

    [Fact]
    public void Parse_TrimsAndNormalizesCaseAndEnumNames()
    {
        foreach (var preset in SimpleRecordingCore.Presets)
        {
            Assert.Equal(preset.Id, SimpleRecordingCore.Parse(preset.Key.ToUpperInvariant()));
            Assert.Equal(preset.Id, SimpleRecordingCore.Parse("  " + preset.Key + "  "));
            // 用户/旧版本可能把枚举名写进配置
            Assert.Equal(preset.Id, SimpleRecordingCore.Parse("SimplePresetId." + preset.Id));
            Assert.Equal(preset.Id, SimpleRecordingCore.Parse(preset.Id.ToString()));
        }
    }

    [Fact]
    public void Parse_FallsBackToQuickForUnknownOrEmptyKeys()
    {
        Assert.Equal(SimplePresetId.Quick, SimpleRecordingCore.Parse(null));
        Assert.Equal(SimplePresetId.Quick, SimpleRecordingCore.Parse(""));
        Assert.Equal(SimplePresetId.Quick, SimpleRecordingCore.Parse("   "));
        Assert.Equal(SimplePresetId.Quick, SimpleRecordingCore.Parse("gaming"));
        Assert.Equal(SimplePresetId.Quick, SimpleRecordingCore.Parse("quickish"));
        Assert.Equal(SimplePresetId.Quick, SimpleRecordingCore.Parse("SimplePresetId.Nope"));
    }

    [Fact]
    public void Get_ReturnsThePresetListEntryAndFallsBackToQuick()
    {
        foreach (var preset in SimpleRecordingCore.Presets)
            Assert.Equal(preset, SimpleRecordingCore.Get(preset.Id));

        // 未知枚举值（例如以后删掉某档、旧 prefs.json 还留着）不能抛异常
        Assert.Equal(SimpleRecordingCore.Get(SimplePresetId.Quick), SimpleRecordingCore.Get((SimplePresetId)999));
    }

    // ------------------------------------------------------------ 落地项

    [Fact]
    public void BuildItems_UsesTheFixedKeyOrder()
    {
        var quick = SimpleRecordingCore.BuildItems(Preset(SimplePresetId.Quick), Snapshot(), RecPath);
        var game = SimpleRecordingCore.BuildItems(Preset(SimplePresetId.Game, splitSeconds: 3600, splitSizeMb: 4096), Snapshot(), RecPath);

        // 界面上「当前 → 建议」的列表顺序：键盘顺序变了用户会以为少了项
        Assert.Equal(
            new[] { "format", "quality", "path", "canvas", "fps", "recTracks", "sampleRate" },
            quick.Select(i => i.Key));
        Assert.Equal(
            new[] { "format", "quality", "path", "canvas", "fps", "recTracks", "sampleRate", "split" },
            game.Select(i => i.Key));
    }

    [Fact]
    public void BuildItems_TakesFpsAndSplitFromThePresetNotFromGlobalDefaults()
    {
        var preset = Preset(SimplePresetId.Game, fps: 144, splitSeconds: 300, kbps: 20000, splitSizeMb: 2048);
        var items = SimpleRecordingCore.BuildItems(preset, Snapshot(), RecPath);

        var fps = items.Single(i => i.Key == "fps");
        Assert.Equal("144", fps.Recommended);
        Assert.Contains(new RecordingEnvTarget("Video", "FPSCommon", "144"), fps.Targets);
        Assert.Contains(new RecordingEnvTarget("Video", "FPSNum", "144"), fps.Targets);
        Assert.Contains(new RecordingEnvTarget("Video", "FPSDen", "1"), fps.Targets);
        // 写死全局默认 60 就是错：预设的 fps 没生效
        Assert.DoesNotContain(fps.Targets, t => t.Value == RecordingEnvCore.Fps.ToString());

        var split = items.Single(i => i.Key == "split");
        Assert.Equal("5 min", split.Recommended);
        Assert.Contains(new RecordingEnvTarget("SimpleOutput", "RecSplitFile", "true"), split.Targets);
        Assert.Contains(new RecordingEnvTarget("SimpleOutput", "RecSplitFileTime", "300"), split.Targets);
    }

    [Fact]
    public void BuildItems_WritesSplitParametersOnlyForSplittingPresets()
    {
        var quick = SimpleRecordingCore.BuildItems(Preset(SimplePresetId.Quick), Snapshot(), RecPath);
        var meeting = SimpleRecordingCore.BuildItems(Preset(SimplePresetId.Meeting), Snapshot(), RecPath);
        var game = SimpleRecordingCore.BuildItems(Preset(SimplePresetId.Game, splitSeconds: 3600, splitSizeMb: 4096), Snapshot(), RecPath);

        Assert.DoesNotContain(quick, i => i.Key == "split");
        Assert.DoesNotContain(meeting, i => i.Key == "split");
        Assert.Contains(game, i => i.Key == "split");

        // 分段参数一个都不许泄漏到未开分段的档
        var splitParameters = new[] { "RecSplitFile", "RecSplitFileTime", "RecSplitFileSize" };
        var nonSplit = quick.Concat(meeting).SelectMany(i => i.Targets).Select(t => t.Parameter).ToHashSet();
        Assert.All(splitParameters, p => Assert.DoesNotContain(p, nonSplit));
    }

    [Fact]
    public void BuildItems_WritesSplitSizeOnlyWhenThePresetDeclaresIt()
    {
        var withoutSize = SimpleRecordingCore.BuildItems(
            Preset(SimplePresetId.Game, splitSeconds: 3600), Snapshot(), RecPath).Single(i => i.Key == "split");
        Assert.DoesNotContain(withoutSize.Targets, t => t.Parameter == "RecSplitFileSize");

        var withSize = SimpleRecordingCore.BuildItems(
            Preset(SimplePresetId.Game, splitSeconds: 3600, splitSizeMb: 4096), Snapshot(), RecPath).Single(i => i.Key == "split");
        Assert.Contains(new RecordingEnvTarget("SimpleOutput", "RecSplitFileSize", "4096"), withSize.Targets);
    }

    [Fact]
    public void BuildItems_ReusesTheRecordingEnvStaticTargetsForTheBaseKeys()
    {
        var preset = Preset(SimplePresetId.Meeting, fps: 30, kbps: 8000);
        var snapshot = Snapshot();
        var items = SimpleRecordingCore.BuildItems(preset, snapshot, RecPath);
        var statics = RecordingEnvCore.StaticTargets(snapshot, preset.Fps);

        // 基准四项必须与「一键部署」同源，否则同一次开录会给出两种口径
        foreach (var key in new[] { "format", "quality", "canvas", "fps" })
        {
            var mine = items.Single(i => i.Key == key);
            var theirs = statics.Single(i => i.Key == key);

            Assert.Equal(theirs.Current, mine.Current);
            Assert.Equal(theirs.Recommended, mine.Recommended);
            Assert.Equal(Flat(theirs.Targets), Flat(mine.Targets));
        }
    }

    [Fact]
    public void BuildItems_PointsTheRecordingDirectoryAtThePassedPath()
    {
        var items = SimpleRecordingCore.BuildItems(Preset(SimplePresetId.Quick), Snapshot(), RecPath);
        var path = items.Single(i => i.Key == "path");

        Assert.Equal(RecPath, path.Recommended);
        Assert.Equal(Snapshot().RecPath, path.Current);        // 当前值仍是快照里的旧目录
        Assert.Contains(new RecordingEnvTarget("SimpleOutput", "FilePath", RecPath), path.Targets);
        Assert.Contains(new RecordingEnvTarget("AdvOut", "RecFilePath", RecPath), path.Targets);
        Assert.NotEqual(Snapshot().RecPath, path.Recommended);
    }

    [Fact]
    public void BuildItems_AlwaysPinsTheLegacyAudioTrackValue()
    {
        // 旧版 OBS 的 RecTracks 缺失时行为不定，不写这一项会出现「录下来没声音 / 只有一路」
        var snapshot = Snapshot();
        var recTracks = SimpleRecordingCore.BuildItems(Preset(SimplePresetId.Quick), snapshot, RecPath)
            .Single(i => i.Key == "recTracks");

        Assert.Equal("2", recTracks.Current);
        Assert.Equal("1", recTracks.Recommended);
        Assert.Contains(new RecordingEnvTarget("SimpleOutput", "RecTracks", "1"), recTracks.Targets);
        Assert.False(recTracks.WebSocketOnly);
    }

    [Fact]
    public void BuildItems_MarksSampleRateAsWebSocketOnly()
    {
        var items = SimpleRecordingCore.BuildItems(Preset(SimplePresetId.Game, splitSeconds: 3600), Snapshot(), RecPath);
        var sampleRate = items.Single(i => i.Key == "sampleRate");

        Assert.Equal("44100", sampleRate.Current);
        Assert.Equal(RecordingEnvCore.SampleRate.ToString(), sampleRate.Recommended);
        Assert.True(sampleRate.WebSocketOnly);
        Assert.Contains(
            new RecordingEnvTarget("Audio", "SampleRate", RecordingEnvCore.SampleRate.ToString()),
            sampleRate.Targets);

        // 文件通道写不了音频设置：其它项都必须能在文件层落地
        Assert.All(items.Where(i => i.Key != "sampleRate"), i => Assert.False(i.WebSocketOnly));
    }

    [Fact]
    public void BuildItems_NeverTouchesStreamingOutputModeOrEncoderParameters()
    {
        var forbidden = new[] { "Mode", "StreamEncoder", "VBitrate", "ABitrate", "RecEncoder", "StreamService" };

        var presets = new[]
        {
            Preset(SimplePresetId.Quick),
            Preset(SimplePresetId.Meeting, fps: 30, kbps: 8000),
            Preset(SimplePresetId.Game, splitSeconds: 3600, splitSizeMb: 4096),
        };

        foreach (var preset in presets)
        {
            var parameters = SimpleRecordingCore.BuildItems(preset, Snapshot(), RecPath)
                .SelectMany(i => i.Targets).Select(t => t.Parameter).ToHashSet();

            Assert.All(forbidden, p => Assert.DoesNotContain(p, parameters));
        }
    }

    // ------------------------------------------------------------ 就绪判定

    [Fact]
    public void Check_ReadyWhenAtLeastOneChannelIsUsable()
    {
        var clean = Check();
        Assert.True(clean.Ready);
        Assert.Null(clean.BlockReason);
        Assert.Null(clean.Hint);
        Assert.Equal(0, clean.WarnCount);

        // 任一通道可用即可；obsProcessRunning 只作展示，能不能写文件由调用方用 canWriteFiles 表达
        Assert.True(Check(viaWebSocket: false, canWriteFiles: true, obsProcessRunning: true).Ready);
        Assert.True(Check(viaWebSocket: true, canWriteFiles: false, obsProcessRunning: true).Ready);
        Assert.False(Check(viaWebSocket: false, canWriteFiles: false, obsProcessRunning: true).Ready);
    }

    [Fact]
    public void Check_ExplicitBlockedReasonWinsOverEverythingElse()
    {
        // 刻意用一个不可能出现在文案表里的串：断言的是「原因原样透传、且优先级最高」
        const string custom = "【测试注入的原因】配置目录缺失";

        var check = Check(
            viaWebSocket: false, canWriteFiles: false, streaming: true, recording: true,
            failCount: 3, warnCount: 2, blockedReason: custom);

        Assert.False(check.Ready);
        Assert.Equal(custom, check.BlockReason);
        // 已知确切原因时不能被通用文案（正在推流 / 通道不可用）盖过去
        Assert.NotEqual(custom, Check(streaming: true).BlockReason);
        Assert.NotEqual(custom, Check(viaWebSocket: false, canWriteFiles: false, noChannelReason: "X").BlockReason);
    }

    [Fact]
    public void Check_EmptyBlockedReasonIsIgnored()
    {
        // 调用方「没有额外原因」时习惯传空串：不能把空串当成阻断原因
        var check = Check(blockedReason: "");
        Assert.True(check.Ready);
        Assert.Null(check.BlockReason);
    }

    [Fact]
    public void Check_StreamingAndRecordingShareOneReasonAndOutrankChannels()
    {
        var streaming = Check(streaming: true).BlockReason;
        var recording = Check(recording: true).BlockReason;

        Assert.False(string.IsNullOrWhiteSpace(streaming));
        Assert.Equal(streaming, recording);

        // 「正在录 / 正在推」是硬阻断：通道通不通都不该改变理由
        Assert.Equal(
            streaming,
            Check(viaWebSocket: false, canWriteFiles: false, streaming: true, recording: true).BlockReason);
    }

    [Fact]
    public void Check_NoChannelReasonIsPassedThroughVerbatim()
    {
        // 刻意用一个不可能出现在文案表里的串：断言的是「原样透传」，不是具体文案
        const string reason = "【测试注入的原因】两条通道都不可用";

        var check = Check(viaWebSocket: false, canWriteFiles: false, obsProcessRunning: true, noChannelReason: reason);
        Assert.False(check.Ready);
        Assert.Equal(reason, check.BlockReason);

        // 不给原因时用默认文案，但绝不能是空串
        var fallback = Check(viaWebSocket: false, canWriteFiles: false, obsProcessRunning: true);
        Assert.False(string.IsNullOrWhiteSpace(fallback.BlockReason));
        Assert.NotEqual(reason, fallback.BlockReason);

        // 空串等同于「没给」→ 回退默认文案
        Assert.Equal(
            fallback.BlockReason,
            Check(viaWebSocket: false, canWriteFiles: false, noChannelReason: "").BlockReason);
    }

    [Fact]
    public void Check_NoChannelOutranksPreflightFailures()
    {
        // 刻意用一个不可能出现在文案表里的串：断言的是「原样透传」，不是具体文案
        const string reason = "【测试注入的原因】通道未开";

        // 通道不可用是「现在做不了」，比「做完发现有问题」更靠前
        var check = Check(
            viaWebSocket: false, canWriteFiles: false, failCount: 5, noChannelReason: reason);
        Assert.Equal(reason, check.BlockReason);

        var failOnOpenChannel = Check(viaWebSocket: true, canWriteFiles: true, failCount: 5);
        Assert.False(failOnOpenChannel.Ready);
        Assert.NotEqual(reason, failOnOpenChannel.BlockReason);
    }

    [Fact]
    public void Check_PreflightFailuresBlockAndHaveTheirOwnReason()
    {
        var fail = Check(viaWebSocket: true, canWriteFiles: true, failCount: 2);
        Assert.False(fail.Ready);
        Assert.False(string.IsNullOrWhiteSpace(fail.BlockReason));

        // 三个阻断级别必须给出不同的话，否则用户照着提示修不好
        Assert.NotEqual(Check(streaming: true).BlockReason, fail.BlockReason);
        Assert.NotEqual(Check(viaWebSocket: false, canWriteFiles: false).BlockReason, fail.BlockReason);
    }

    [Fact]
    public void Check_WarningsDoNotBlockButAreReported()
    {
        var warned = Check(warnCount: 3);

        Assert.True(warned.Ready);              // 警告不阻断：能不能录只看硬阻断与通道
        Assert.Null(warned.BlockReason);
        Assert.Equal(3, warned.WarnCount);
        Assert.False(string.IsNullOrWhiteSpace(warned.Hint));   // 警告必须有地方说出来
        Assert.Equal(0, warned.FailCount);
    }

    [Fact]
    public void Check_HardBlocksReportOnlyTheBlockReason()
    {
        var check = Check(streaming: true, failCount: 2, warnCount: 5);

        Assert.False(check.Ready);
        Assert.False(string.IsNullOrWhiteSpace(check.BlockReason));
        // 硬阻断时界面只显示一句「怎么修」，不再混入提示与计数
        Assert.Null(check.Hint);
        Assert.Equal(0, check.WarnCount);
        Assert.Equal(0, check.FailCount);
    }

    // ------------------------------------------------------------ 进度估算

    [Fact]
    public void Estimate_UsesTheDocumentedSpareCapacityFormula()
    {
        var progress = SimpleRecordingCore.Estimate(TimeSpan.FromMinutes(3.5), 100.0, 6000, 4096.5);

        Assert.Equal(TimeSpan.FromMinutes(3.5), progress.Elapsed);
        Assert.Equal(100.0, progress.FreeGb);
        Assert.Equal(4096.5, progress.UsedMb);         // 已写文件大小原样回传，不在这里重算
        // 100GB × 1024(MB) × 1024(kb/Mb) × 8 ÷ (6000kbps × 1.5 冗余) ÷ 60 ≈ 1553.45 分钟
        // V3.0 修正：这里原先漏了 Mb→kb 的那个 ×1024，把结果低估了 1024 倍
        Assert.Equal(1553.4459259259, progress.RemainingMinutes, 6);
        Assert.False(progress.LowSpace);               // 26 小时余量当然不是「低空」
    }

    /// <summary>
    /// 单位换算的**回归锚点**：100 GB 空闲 + 常见推流码率，剩余必须是「小时级」。
    ///
    /// 这条测试专门防住 V3.0 修掉的那个 1024 倍错误 —— 它当年让低空告警在任何机器上恒亮，
    /// 而单纯断言公式系数（上面那条）在没有「量级直觉」时很容易被再次改错。
    /// </summary>
    [Theory]
    [InlineData(6000, 200)]      // 1080p60 保守
    [InlineData(20000, 60)]      // 直播档
    [InlineData(30000, 40)]      // 高质量档
    public void Estimate_HundredGigabytes_IsHoursNotMinutes(int bitrateKbps, int minimumMinutes)
    {
        var progress = SimpleRecordingCore.Estimate(TimeSpan.Zero, 100.0, bitrateKbps, 0);
        Assert.True(progress.RemainingMinutes > minimumMinutes,
            $"{bitrateKbps} kbps 下 100GB 应能录 {minimumMinutes} 分钟以上，实际 {progress.RemainingMinutes:0.#}");
        Assert.False(progress.LowSpace);
    }

    [Fact]
    public void Estimate_ReportsZeroRemainingOnAFullDisk()
    {
        var progress = SimpleRecordingCore.Estimate(TimeSpan.Zero, 0.0, 6000, 0.0);

        Assert.Equal(0.0, progress.RemainingMinutes);
        Assert.True(progress.LowSpace);
    }

    [Fact]
    public void Estimate_DoesNotDivideByZeroOnAMissingBitrate()
    {
        var progress = SimpleRecordingCore.Estimate(TimeSpan.FromMinutes(1), 100.0, 0, 512.0);

        // 码率未知时不能算出 Infinity / NaN：宁可报 0 并不算低空间
        Assert.Equal(0.0, progress.RemainingMinutes);
        Assert.False(double.IsNaN(progress.RemainingMinutes));
        Assert.False(progress.LowSpace);
        Assert.Equal(512.0, progress.UsedMb);
    }

    [Fact]
    public void Estimate_ClampsNegativeInputs()
    {
        var progress = SimpleRecordingCore.Estimate(TimeSpan.FromMinutes(-5), -10.0, 6000, -1.5);

        Assert.Equal(TimeSpan.Zero, progress.Elapsed);
        Assert.Equal(0.0, progress.FreeGb);
        Assert.Equal(0.0, progress.UsedMb);
        Assert.Equal(0.0, progress.RemainingMinutes);
        Assert.True(progress.LowSpace);     // 码率有效但没有空间
    }

    [Fact]
    public void Estimate_TreatsExactlyTenMinutesAsEnoughSpace()
    {
        // 让剩余恰好等于 10 分钟：freeGb × 1024 × 1024 × 8 ÷ (900 × 1.5) ÷ 60 = 10
        //   ⇒ freeGb = 10 × 60 × 1350 ÷ (1024 × 1024 × 8)
        var hundredTenMinutesGb = 10.0 * 60 * (900 * 1.5) / (1024.0 * 1024 * 8);

        var exactlyTen = SimpleRecordingCore.Estimate(TimeSpan.Zero, hundredTenMinutesGb, 900, 0);
        Assert.Equal(10.0, exactlyTen.RemainingMinutes, 9);
        Assert.False(exactlyTen.LowSpace);            // 阈值是「严格小于 10 分钟」

        var justBelow = SimpleRecordingCore.Estimate(TimeSpan.Zero, hundredTenMinutesGb - 0.01, 900, 0);
        Assert.True(justBelow.RemainingMinutes < 10);
        Assert.True(justBelow.LowSpace);

        var justAbove = SimpleRecordingCore.Estimate(TimeSpan.Zero, hundredTenMinutesGb + 0.01, 900, 0);
        Assert.False(justAbove.LowSpace);
    }

    /// <summary>实测写入速率同样要走修好的单位换算（否则「按实测」这条新路径还是 1024 倍错）。</summary>
    [Fact]
    public void Estimate_WithMeasuredRate_UsesTheSameUnitMath()
    {
        // 实测 6000 kbps：100GB 应能录约 1553 分钟 ÷ (1.15/1.5 的系数差)…
        // 这里只钉量级与「不再恒亮低空告警」，避免把系数写死成实现细节。
        var progress = SimpleRecordingCore.Estimate(TimeSpan.FromMinutes(1), 100.0, 20000, 100, measuredKbps: 6000);

        Assert.True(progress.FromMeasuredRate);
        Assert.True(progress.RemainingMinutes > 60, $"实测速率下 100GB 应有小时级余量，实际 {progress.RemainingMinutes:0.#}");
        Assert.False(progress.LowSpace);
        Assert.Equal(6000, progress.MeasuredKbps, 3);
    }

    // ------------------------------------------------------------ 生效通道与展示格式

    [Fact]
    public void NeedsObsRestart_IsExactlyTheFileChannelIndicator()
    {
        Assert.True(SimpleRecordingCore.NeedsObsRestart(false));    // 文件通道：重启 OBS 才生效
        Assert.False(SimpleRecordingCore.NeedsObsRestart(true));    // WebSocket 通道：即时生效
    }

    [Fact]
    public void FormatSplit_ReadsAsHumanMinutesAndSeconds()
    {
        Assert.Equal("", SimpleRecordingCore.FormatSplit(Preset(SimplePresetId.Quick)));
        Assert.Equal("45 s", SimpleRecordingCore.FormatSplit(Preset(SimplePresetId.Game, splitSeconds: 45)));
        Assert.Equal("5 min", SimpleRecordingCore.FormatSplit(Preset(SimplePresetId.Game, splitSeconds: 300)));
        Assert.Equal("60 min", SimpleRecordingCore.FormatSplit(Preset(SimplePresetId.Game, splitSeconds: 3600)));
        Assert.Equal("90 min", SimpleRecordingCore.FormatSplit(Preset(SimplePresetId.Game, splitSeconds: 5400)));
    }

    [Fact]
    public void FormatDuration_SwitchesToHoursOnlyAfterAnHour()
    {
        Assert.Equal("00:00", SimpleRecordingCore.FormatDuration(TimeSpan.Zero));
        Assert.Equal("01:05", SimpleRecordingCore.FormatDuration(TimeSpan.FromSeconds(65)));
        Assert.Equal("03:04", SimpleRecordingCore.FormatDuration(new TimeSpan(0, 3, 4)));
        Assert.Equal("2:03:04", SimpleRecordingCore.FormatDuration(new TimeSpan(2, 3, 4)));
        Assert.Equal("00:00", SimpleRecordingCore.FormatDuration(TimeSpan.FromSeconds(-5)));
    }

    // ------------------------------------------------------------ 落地参数映射与回滚

    [Fact]
    public void TargetValues_LastWriteWinsAndKeyCaseIsIgnored()
    {
        var items = new List<RecordingEnvItem>
        {
            new()
            {
                Key = "path",
                Current = "",
                Recommended = "",
                Targets = new[] { new RecordingEnvTarget("AdvOut", "FilePath", @"D:\A") },
            },
            new()
            {
                Key = "sampleRate",
                Current = "",
                Recommended = "",
                Targets = new[]
                {
                    new RecordingEnvTarget("AdvOut", "filepath", @"D:\B"),   // 同参数（忽略大小写）
                    new RecordingEnvTarget("Video", "FPSCommon", "60"),
                },
            },
        };

        var map = SimpleRecordingCore.TargetValues(items);

        Assert.Equal(2, map.Count);
        Assert.Equal(@"D:\B", map["FilePath"]);      // 后写覆盖先写：与实际落地顺序一致
        Assert.Equal("60", map["FPSCommon"]);        // 其它参数不受影响
    }

    [Fact]
    public void Rollback_RoundTripsKeysAndEmptyValues()
    {
        var pairs = new List<KeyValuePair<string, string>>
        {
            new("FPSCommon", "30"),
            new("RecFormat2", ""),                   // 原本没有这个键
            new("", "ignored"),                      // 空键不参与序列化
        };

        var text = SimpleRecordingCore.SerializeRollback(pairs);
        Assert.Equal("FPSCommon=30\nRecFormat2=", text);

        var back = SimpleRecordingCore.DeserializeRollback(text);
        Assert.Equal(2, back.Count);
        Assert.Equal("30", back["FPSCommon"]);
        Assert.Equal("", back["RecFormat2"]);

        Assert.Empty(SimpleRecordingCore.DeserializeRollback(null));
        Assert.Empty(SimpleRecordingCore.DeserializeRollback(""));
    }

    [Fact]
    public void Rollback_KeepsEqualsSignsThatBelongToTheValue()
    {
        var pairs = new List<KeyValuePair<string, string>>
        {
            new("FilePath", @"D:\A=B"),
        };

        var back = SimpleRecordingCore.DeserializeRollback(SimpleRecordingCore.SerializeRollback(pairs));

        // 只在第一个 '=' 处切分：Windows 路径里出现 '=' 不能把值截断
        Assert.Equal(@"D:\A=B", back["FilePath"]);
    }
}
