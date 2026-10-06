using OBS_Helper.Wpf.Localization;

namespace OBS_Helper.Wpf.Services.ObsConfig;

/// <summary>「简单录像」的三档预设。默认 <see cref="Quick"/>。</summary>
public enum SimplePresetId
{
    /// <summary>快速录像：1080p60，通用推荐。</summary>
    Quick = 0,

    /// <summary>会议 / 网课：1080p30，文件小、CPU 友好。</summary>
    Meeting = 1,

    /// <summary>游戏高码率：1080p60 + 自动分段，长时间录制不产生单个巨型文件。</summary>
    Game = 2
}

/// <summary>
/// 一档预设：只声明「录什么」（帧率 / 分段 / 估算码率），
/// **不声明「怎么编码」** —— 编码器与码率属于用户自己的选择，本版不碰。
/// </summary>
public sealed record SimplePreset(
    SimplePresetId Id,
    string Key,
    int Fps,
    int SplitSeconds,
    int EstimatedBitrateKbps,
    int SplitSizeMb = 0);

/// <summary>简单录像的一次点击走到了哪一步。</summary>
public enum SimpleRecordingStep
{
    /// <summary>未开始，等待用户。</summary>
    Idle = 0,

    /// <summary>正在自检（读配置、跑录前检查）。</summary>
    Checking,

    /// <summary>有硬阻断，不能继续（正在推流 / 缺配置 / 文件通道写不了）。</summary>
    Blocked,

    /// <summary>正在落地推荐项（先整备份）。</summary>
    SettingUp,

    /// <summary>正在拉起 OBS 进程。</summary>
    LaunchingObs,

    /// <summary>已拉起，正在等 obs-websocket 就绪。</summary>
    WaitingObs,

    /// <summary>已发出 StartRecord。</summary>
    Recording,

    /// <summary>正在停止并保存。</summary>
    Stopping,

    /// <summary>已停止（可能录到了文件，也可能一个字节都没写）。</summary>
    Stopped
}

/// <summary>「现在能不能录」的结论。</summary>
public sealed record SimpleReadyCheck(
    bool Ready,
    /// <summary>不能录的<b>硬</b>原因（文案键的成品文案）；能录时为 null。</summary>
    string? BlockReason,
    /// <summary>能录但需要提醒（warn / info 级）的说明；没有时为 null。</summary>
    string? Hint,
    int WarnCount,
    int FailCount);

/// <summary>一次「简单录像」要落地的项（复用一键部署的模型与双通道）。</summary>
public sealed record SimpleLaunchPlan(
    bool ViaWebSocket,
    string? BlockedReason,
    string? BasicIniPath,
    IReadOnlyList<RecordingEnvItem> Items)
{
    /// <summary>OBS 配置目录（V3.0：文件通道写入前要过 <c>ObsSafePath.AssertWritable</c> 护栏）。</summary>
    public string? ConfigDir { get; init; }
}

/// <summary>录制中的进度与容量估算。</summary>
public sealed record SimpleProgress(
    TimeSpan Elapsed,
    /// <summary>录像盘剩余空间（GB）。</summary>
    double FreeGb,
    /// <summary>当前录像文件已写入的大小（MB）；读不到为 0。</summary>
    double UsedMb,
    /// <summary>剩余可录分钟数（按实测写入速率或预设码率估算）。</summary>
    double RemainingMinutes,
    /// <summary>剩余可录不足 <see cref="SimpleRecordingCore.LowSpaceMinutes"/> 分钟。</summary>
    bool LowSpace)
{
    /// <summary>
    /// 剩余可录是否基于**实测写入速率**（V3.0）。false = 基于预设码率估算。
    /// 界面上要如实区分：本产品刻意不写码率，预设常数与用户实际设置可能差几倍。
    /// </summary>
    public bool FromMeasuredRate { get; init; }

    /// <summary>实测写入速率（kbps）；未取得样本时为 0。</summary>
    public double MeasuredKbps { get; init; }
}

/// <summary>
/// 「简单录像」的核心逻辑（V2.9.4）。纯 BCL、零 WPF 依赖，单测工程直接链接编译。
///
/// 设计口径（与 V2.9.3 的一键部署一脉相承）：
/// <list type="bullet">
///   <item><b>预设只声明录制参数，不声明编码方式</b>：不碰输出模式、编码器族、码率、推流参数；</item>
///   <item><b>帧率是唯一按档位变化的项</b>（30 / 60），其余取值与一键部署完全一致；</item>
///   <item><b>分段只给游戏档默认开</b>：长录制不产生单个巨型文件，其它档保持用户原样；</item>
///   <item><b>写入仍然先备份、可回滚、写后读回校验</b> —— 本文件只产出「要写什么」，落地由服务层负责。</item>
/// </list>
/// </summary>
public static class SimpleRecordingCore
{
    /// <summary>低于这个剩余可录时长就提醒用户（分钟）。</summary>
    public const double LowSpaceMinutes = 10;

    /// <summary>剩余可录时长的冗余系数：按预估码率的 1.5 倍算，宁可低估。</summary>
    public const double BitrateSafetyFactor = 1.5;

    /// <summary>
    /// 实测写入速率下的冗余系数（V3.0）：与预设码率**同为 1.5**。
    ///
    /// 为什么不因为「实测更准」就收紧到 1.15（原本这么写，审查后回退）：
    /// 实测样本来自「录像文件长度差」，自动分段/切换文件时会回落归零，本质是
    /// 「最近一次画面复杂度」的外推；而画面复杂度是可以突然变化的（静止画面 → 高速运动）。
    /// 1.5 是唯一还能兜住这种突变、并让低空告警**提前**响起的余量 ——
    /// 用「看起来更准」换「告警更晚」，在这个场景里是负收益。
    /// </summary>
    public const double MeasuredSafetyFactor = 1.5;

    private static readonly SimplePreset[] AllPresets =
    {
        new(SimplePresetId.Quick, "quick", Fps: 60, SplitSeconds: 0, EstimatedBitrateKbps: 20_000),
        new(SimplePresetId.Meeting, "meeting", Fps: 30, SplitSeconds: 0, EstimatedBitrateKbps: 8_000),
        new(SimplePresetId.Game, "game", Fps: 60, SplitSeconds: 3600, EstimatedBitrateKbps: 30_000,
            SplitSizeMb: 4_096),
    };

    /// <summary>三档预设（顺序即界面顺序：quick / meeting / game）。</summary>
    public static IReadOnlyList<SimplePreset> Presets => AllPresets;

    /// <summary>取预设；未知 id 回退到 <see cref="SimplePresetId.Quick"/>，永不抛。</summary>
    public static SimplePreset Get(SimplePresetId id)
        => AllPresets.FirstOrDefault(p => p.Id == id) ?? AllPresets[0];

    /// <summary>
    /// 把持久化的字符串归一化成预设 id。空 / 未知 / 大小写不同一律回退 Quick ——
    /// 配置是用户手边可以改的文件，读到脏值不能让功能打不开。
    /// </summary>
    public static SimplePresetId Parse(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return SimplePresetId.Quick;
        var k = key.Trim();
        foreach (var p in AllPresets)
        {
            if (string.Equals(p.Key, k, StringComparison.OrdinalIgnoreCase))
                return p.Id;
        }
        // 也接受枚举名（"Game" / "SimplePresetId.Game" 这类手写值）
        var name = k.Contains('.') ? k[(k.LastIndexOf('.') + 1)..] : k;
        return Enum.TryParse<SimplePresetId>(name, ignoreCase: true, out var id) && Enum.IsDefined(id)
            ? id
            : SimplePresetId.Quick;
    }

    /// <summary>
    /// 预设 → 要落地的推荐项。
    ///
    /// 基准项来自 <see cref="RecordingEnvCore.StaticTargets"/>（格式 / 质量 / 画布 / 帧率），
    /// 再补录像目录、音频采样率、音频轨，以及游戏档的分段。
    /// 键顺序固定：<c>format, quality, path, canvas, fps, recTracks, sampleRate[, split]</c>。
    /// </summary>
    public static List<RecordingEnvItem> BuildItems(
        SimplePreset preset, RecordingEnvSnapshot snapshot, string recPath)
    {
        var items = RecordingEnvCore.StaticTargets(snapshot, preset.Fps);

        items.Insert(2, new RecordingEnvItem
        {
            Key = "path",
            Current = snapshot.RecPath,
            Recommended = recPath,
            Targets = new[]
            {
                new RecordingEnvTarget("SimpleOutput", "FilePath", recPath),
                new RecordingEnvTarget("AdvOut", "RecFilePath", recPath),
            },
        });

        // 音频轨数：写 1 轨。老版本 OBS 的 RecTracks 缺失时行为不定，显式写 1 是单轨录制的事实标准。
        items.Add(new RecordingEnvItem
        {
            Key = "recTracks",
            Current = snapshot.RecTracks,
            Recommended = "1",
            Targets = new[] { new RecordingEnvTarget("SimpleOutput", "RecTracks", "1") },
        });

        // 采样率只有 WebSocket 通道能改（INI 里音频设置的位置随版本变动，赌错就把用户配置搞乱）
        items.Add(new RecordingEnvItem
        {
            Key = "sampleRate",
            Current = snapshot.SampleRate,
            Recommended = RecordingEnvCore.SampleRate.ToString(),
            WebSocketOnly = true,
            Targets = new[]
            {
                new RecordingEnvTarget("Audio", "SampleRate", RecordingEnvCore.SampleRate.ToString()),
            },
        });

        if (preset.SplitSeconds > 0)
        {
            var targets = new List<RecordingEnvTarget>
            {
                new("SimpleOutput", "RecSplitFile", "true"),
                new("SimpleOutput", "RecSplitFileTime", preset.SplitSeconds.ToString()),
            };
            if (preset.SplitSizeMb > 0)
                targets.Add(new RecordingEnvTarget("SimpleOutput", "RecSplitFileSize", preset.SplitSizeMb.ToString()));

            items.Add(new RecordingEnvItem
            {
                Key = "split",
                Current = snapshot.RecSplitFile,
                Recommended = FormatSplit(preset),
                Targets = targets,
            });
        }

        return items;
    }

    /// <summary>分段的展示值（秒 → <c>60 分钟</c> 这种人类可读形式）。</summary>
    public static string FormatSplit(SimplePreset preset)
    {
        if (preset.SplitSeconds <= 0) return "";
        var minutes = preset.SplitSeconds / 60.0;
        return minutes >= 1
            ? minutes.ToString("0.#") + " min"
            : preset.SplitSeconds + " s";
    }

    /// <summary>
    /// 就绪判定（纯函数）。判定优先级：硬阻断 → 通道可用性 → 录前自检 → 就绪。
    /// 文案来自文案表，因此切语言即时生效（这里**不缓存**任何成品文案）。
    /// </summary>
    /// <param name="viaWebSocket">OBS 已连上、且没有在录 / 在推（WebSocket 通道可用）。</param>
    /// <param name="canWriteFiles">OBS 已退出、且能定位到当前配置集的 basic.ini（文件通道可用）。</param>
    /// <param name="obsProcessRunning">obs64/obs32/obs 进程在跑。</param>
    /// <param name="streaming">正在推流。</param>
    /// <param name="recording">正在录制。</param>
    /// <param name="failCount">录前自检的 Fail 项数。</param>
    /// <param name="warnCount">录前自检的 Warn 项数。</param>
    /// <param name="blockedReason">调用方已知的阻断原因（例如「找不到配置目录」），优先级最高。</param>
    /// <param name="noChannelReason">两条通道都不可用时的说明；为空时用默认文案。</param>
    public static SimpleReadyCheck Check(
        bool viaWebSocket,
        bool canWriteFiles,
        bool obsProcessRunning,
        bool streaming,
        bool recording,
        int failCount,
        int warnCount,
        string? blockedReason = null,
        string? noChannelReason = null)
    {
        // 0) 调用方已经知道确切原因（配置目录缺失 / 配置集读不到）：直接用，别拿通用文案糊过去。
        //    这条排在第一位，是因为它的信息量最大 —— 用户要的是「怎么修」，不是「哪一类失败」。
        if (blockedReason is { Length: > 0 })
            return Blocked(blockedReason);

        // 1) 直播 / 录制中一律不动：改输出参数会打断用户的直播，抢录制输出更不可接受
        if (streaming || recording)
            return Blocked(Strings.T("simple.blocked.streaming"));

        // 2) 两条通道都不可用：常见是「OBS 在跑但没连上」（WebSocket 未开 / 密码不对），
        //    此时文件层也不能写 —— OBS 正在运行会把内存里的配置回写，改了等于白改。
        //    也覆盖「OBS 没跑、但定位不到 basic.ini」这一种，此时由调用方给出更贴切的原因。
        if (!viaWebSocket && !canWriteFiles)
            return Blocked(noChannelReason is { Length: > 0 }
                ? noChannelReason
                : Strings.T("simple.blocked.noChannel"));

        // 3) 录前自检的 Fail 级问题：录像路径不存在 / 配置目录找不到这类，落地也录不出东西
        if (failCount > 0)
            return Blocked(Strings.T("simple.blocked.preflightFail", failCount));

        // 4) 可录，但有需要用户知情的提醒（磁盘余量、软编、关键帧间隔…）—— 警告不阻断
        if (warnCount > 0)
            return new SimpleReadyCheck(true, null, Strings.T("simple.check.warns", warnCount), warnCount, 0);

        return new SimpleReadyCheck(true, null, null, 0, 0);
    }

    private static SimpleReadyCheck Blocked(string reason) => new(false, reason, null, 0, 0);

    /// <summary>
    /// 录制进度估算。
    ///
    /// 剩余可录分钟 = 剩余空间(GB) × 1024 × 1024 × 8 ÷ (生效码率 kbps × 60)。
    ///
    /// 三个换算是不能省的：GB→MB 是 ×1024，MB→Mb 是 ×8，而 Mb→kb 还要再 ×1024
    /// （本项目的「码率」一律按 1024 进制，与界面的 kbps 显示一致）。
    ///
    /// **V3.0 修正**：这里原先漏了最后一个 ×1024（写成 <c>freeGb × 1024 × 8</c>），
    /// 于是「剩余可录」被低估 1024 倍 —— 100 GB 空闲 @30 Mbps 真实约 466 分钟，却算成 0.45 分钟，
    /// 结果就是**低空告警在任何机器上都恒亮**（告警疲劳），而新产品名下的「按实测写入速率」也一起失去意义。
    /// 这条从 V2.9.4 就存在，第二轮对抗式审查才把它算清楚。
    /// </summary>
    public static SimpleProgress Estimate(TimeSpan elapsed, double freeGb, int bitrateKbps, double usedMb)
        => Estimate(elapsed, freeGb, bitrateKbps, usedMb, measuredKbps: 0);

    /// <summary>
    /// 录制进度估算（V3.0 起支持**实测写入速率**校正）。
    ///
    /// <paramref name="measuredKbps"/> &gt; 0 时优先用它：本产品刻意不写码率，
    /// 预设常数只是「典型值」，与用户实际设置（CQP/HQ 等）可能差几倍，
    /// 于是「还能录多久」这个录制场景第一大未知会失准。有了实测样本就用实测值，
    /// 界面据此如实标注「按实测写入速率」。
    /// </summary>
    public static SimpleProgress Estimate(TimeSpan elapsed, double freeGb, int bitrateKbps, double usedMb,
        double measuredKbps)
    {
        if (freeGb < 0) freeGb = 0;
        if (usedMb < 0) usedMb = 0;

        var fromMeasured = measuredKbps > 0;
        var rateForEstimate = fromMeasured ? measuredKbps * MeasuredSafetyFactor : bitrateKbps * BitrateSafetyFactor;

        var remaining = 0.0;
        if (rateForEstimate > 0)
        {
            // GB → MB (×1024) → Mb (×8) → kb (×1024)，再除以码率与 60 秒
            var availableKb = freeGb * 1024 * 1024 * 8;
            remaining = availableKb / rateForEstimate / 60;
        }

        return new SimpleProgress(
            Elapsed: elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed,
            FreeGb: freeGb,
            UsedMb: usedMb,
            RemainingMinutes: remaining,
            LowSpace: rateForEstimate > 0 && remaining < LowSpaceMinutes)
        {
            FromMeasuredRate = fromMeasured,
            MeasuredKbps = measuredKbps
        };
    }

    /// <summary>文件通道落地后必须重启 OBS 才生效；WebSocket 通道即时生效。</summary>
    public static bool NeedsObsRestart(bool viaWebSocket) => !viaWebSocket;

    /// <summary>
    /// 本轮要落地的「参数名 → 目标值」。
    ///
    /// 同时供两处使用：文件通道<b>幂等重写</b>（即使 OBS 反复回写已存在的键，我们每次都写成同样的值），
    /// 以及暂存「落地前的旧值」——这样应用过程中的任何异常都能按原值回滚，
    /// 不再是「部署卡回滚一次就丢、用户再点一次也不还原」。
    /// </summary>
    public static IReadOnlyDictionary<string, string> TargetValues(IEnumerable<RecordingEnvItem> items)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
            foreach (var t in item.Targets)
                map[t.Parameter] = t.Value;
        return map;
    }

    /// <summary>
    /// 应用成功但「重启 OBS 后才生效」时，把落地前的旧值记到本地存储。
    /// 下次启动由 <c>SimpleRecordingService</c> 读回并提示用户「要不要恢复上次改动前的值」——
    /// 不落盘的话，用户关掉程序后回滚按钮就消失了，只剩同目录那份 .bak 可翻。
    /// 键名固定，便于排障时直接翻 prefs.json。
    /// </summary>
    public const string PendingRestartKey = "simple.pendingRestart";

    /// <summary>旧值表：本地存储里的对象键（与 <see cref="PendingRestartKey"/> 同时存在才有效）。</summary>
    public const string PendingRollbackKey = "simple.pendingRollback";

    /// <summary>落地方式标签：obs-websocket 通道（写入即时生效）。</summary>
    public const string ChannelWebSocket = "websocket";

    /// <summary>落地方式标签：basic.ini 文件通道（需重启 OBS 才生效）。</summary>
    public const string ChannelFile = "file";

    /// <summary>
    /// 「上次会话的改动还没回滚」的提示语。文案键是 <c>simple.pending.notice</c>；
    /// 参数在调用点组装（界面只显示一行：方式 + 时间 + 参数个数）。
    /// </summary>
    public static string PendingNoticeText(string channel, string appliedAt, int entryCount)
        => Strings.T("simple.pending.notice", channel, appliedAt, entryCount);

    // ------------------------------------------------------------ 上一版遗留的序列化口径（仅供排障时的文本导出）

    /// <summary>把「参数名 → 旧值」序列化成可读文本（排障日志用；存储走 JSON 对象）。</summary>
    public static string SerializeRollback(IEnumerable<KeyValuePair<string, string>> pairs)
        => string.Join("\n", pairs
            .Where(p => !string.IsNullOrEmpty(p.Key))
            .Select(p => p.Key + "=" + (p.Value ?? "")));

    /// <summary>反序列化 <see cref="SerializeRollback"/> 的输出；空串返回空表。</summary>
    public static Dictionary<string, string> DeserializeRollback(string? text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(text)) return result;
        foreach (var line in text.Split('\n'))
        {
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            if (key.Length == 0) continue;
            result[key] = line[(eq + 1)..].Trim();
        }
        return result;
    }

    /// <summary>把时长格式化成 <c>hh:mm:ss</c>（不足 1 小时为 <c>mm:ss</c>）。</summary>
    public static string FormatDuration(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        return elapsed.TotalHours >= 1
            ? elapsed.ToString(@"h\:mm\:ss")
            : elapsed.ToString(@"mm\:ss");
    }
}
