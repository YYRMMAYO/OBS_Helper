using System.Text.Json;
using OBS_Helper.Wpf.Models.Obs;
using OBS_Helper.Wpf.Services.ObsConfig;

namespace OBS_Helper.Wpf.Services.Obs;

/// <summary>
/// OBS 控制层门面（技术计划 §4.1 / §4.2）。
///
/// 职责：
/// <list type="bullet">
///   <item>维护连接状态机 Disconnected → Connecting → Authenticating → Connected →（Reconnecting）</item>
///   <item>断线后按指数退避自动重连</item>
///   <item>把 obs-websocket 原始请求封装成领域方法（场景 / 录制 / 推流 / 音频 / 源 / 统计）</item>
///   <item>把服务端事件翻译成 UI 可直接消费的状态变更通知</item>
/// </list>
///
/// 生命周期为单例（Program.cs 中注册），页面通过 <see cref="StateChanged"/> 订阅刷新。
/// </summary>
public sealed class ObsConnectionService : IAsyncDisposable
{
    private readonly ObsWebSocketClient _client = new();
    private readonly ObsReconnectPolicy _policy = new();
    private readonly ObsSettingsService _settings;

    private CancellationTokenSource? _reconnectCts;
    private int _attempt;
    private bool _userInitiatedDisconnect;

    public ObsConnectionService(ObsSettingsService settings)
    {
        _settings = settings;
        _client.EventReceived += OnObsEvent;
        _client.Closed += OnClosed;
    }

    // ---------------------------------------------------------------- 状态

    public ObsConnectionState State { get; private set; } = ObsConnectionState.Disconnected;

    /// <summary>最近一次错误说明（用于 UI 提示）。</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// <see cref="LastError"/> 是否代表**一次真正的失败**（V3.0 审查修正）。
    ///
    /// 为什么需要拆开：正常关闭 OBS 也会写一条原因（「OBS 正在退出」），
    /// 而徽章原先只看 LastError 是否非空就变红提示「连接失败」—— 把正常关闭渲染成故障，
    /// 正是本版想消除的那类误报。红色只由这个字段决定。
    /// </summary>
    public bool LastErrorIsFailure { get; private set; }

    /// <summary>
    /// 当前连接是否走<b>旧协议</b>（obs-websocket 4.x，OBS 27 那一代；只在 Win7 兼容构建里可能为 true）。
    ///
    /// 用途：旧协议没有 <c>SetProfileParameter</c> / <c>SetVideoSettings</c> 这类请求，
    /// 依赖它们的写路径要改用文件通道，界面也要如实告诉用户「哪些能力这代协议没有」。
    /// </summary>
    public bool IsLegacyProtocol => _client.IsLegacyProtocol;

    /// <summary>当前协议代次，用于徽章与诊断报告（如 <c>v5</c> / <c>v4</c>）。</summary>
    public string ProtocolVersion => _client.Protocol;

    /// <summary>下一次自动重连的倒计时秒数；非重连状态为 0。</summary>
    public int ReconnectInSeconds { get; private set; }

    public int ReconnectAttempt => _attempt;

    public ObsProfileInfo Profile { get; private set; } = new();
    public List<ObsSceneInfo> Scenes { get; private set; } = new();
    public string CurrentScene { get; private set; } = "";
    public List<ObsInputInfo> AudioInputs { get; private set; } = new();
    public List<ObsSceneItemInfo> CurrentSceneItems { get; private set; } = new();
    public ObsOutputStatus RecordStatus { get; private set; } = new();
    public ObsOutputStatus StreamStatus { get; private set; } = new();
    public ObsOutputStatus VirtualCamStatus { get; private set; } = new();
    public ObsStats Stats { get; private set; } = new();

    /// <summary>本次录制开始的本地时刻（V2.9.4）；未在录制时为 null。</summary>
    public DateTime? RecordStartedUtc { get; private set; }

    /// <summary>本次推流开始的本地时刻（V3.0 / D2）；未在推流时为 null。</summary>
    public DateTime? StreamStartedUtc { get; private set; }

    /// <summary>
    /// 已播时长（V3.0 / D2）：与录制同口径 —— 优先用 OBS 自报的 timecode，
    /// 拿不到时退回「本地开始时刻到现在的墙钟」。
    /// </summary>
    public TimeSpan StreamElapsed
    {
        get
        {
            var fromTimecode = ParseTimecode(StreamStatus.Timecode);
            if (fromTimecode > TimeSpan.Zero) return fromTimecode;
            if (StreamStartedUtc is { } start)
            {
                var delta = DateTime.UtcNow - start;
                return delta > TimeSpan.Zero ? delta : TimeSpan.Zero;
            }
            return TimeSpan.Zero;
        }
    }

    /// <summary>
    /// OBS 最近一次「切到哪个文件」的录像文件路径（V2.9.4）。
    /// 来自 <c>RecordFileChanged</c> / <c>RecordStateChanged.outputPath</c>；
    /// 配合 <see cref="RecordFileUtc"/> 可判断它是不是本轮录制的产物。
    /// </summary>
    public string LastRecordFile { get; private set; } = "";

    /// <summary><see cref="LastRecordFile"/> 的写入时刻；为空表示还没拿到过。</summary>
    public DateTime? RecordFileUtc { get; private set; }

    /// <summary>
    /// 已录时长（V2.9.4）：优先用 OBS 自报的 timecode（暂停不累加），
    /// 拿不到时退回「本地开始时刻到现在的墙钟」。
    /// </summary>
    public TimeSpan RecordElapsed
    {
        get
        {
            var fromTimecode = ParseTimecode(RecordStatus.Timecode);
            if (fromTimecode > TimeSpan.Zero) return fromTimecode;
            if (RecordStartedUtc is { } start)
            {
                var delta = DateTime.UtcNow - start;
                return delta > TimeSpan.Zero ? delta : TimeSpan.Zero;
            }
            return TimeSpan.Zero;
        }
    }

    /// <summary>把 OBS 的 <c>HH:MM:SS.mmm</c> 时长解析成 <see cref="TimeSpan"/>；解析不出返回 Zero。</summary>
    public static TimeSpan ParseTimecode(string? timecode)
    {
        if (string.IsNullOrWhiteSpace(timecode)) return TimeSpan.Zero;
        return TimeSpan.TryParse(timecode.Trim(), System.Globalization.CultureInfo.InvariantCulture,
            out var parsed) && parsed > TimeSpan.Zero
            ? parsed
            : TimeSpan.Zero;
    }

    public bool IsConnected => State == ObsConnectionState.Connected;

    /// <summary>任意状态或数据变化时触发，供页面 StateHasChanged。</summary>
    public event Action? StateChanged;

    private void Notify() => StateChanged?.Invoke();

    private void SetState(ObsConnectionState s, string? error = null, bool isFailure = false)
    {
        State = s;
        LastError = error;
        LastErrorIsFailure = isFailure && !string.IsNullOrEmpty(error);

        // V3.0 审查修正：断开 / 连接失败时把「输出状态」一并复位。
        //
        // 原先这些字段只在 RefreshOutputsAsync 里赋值，于是 OBS 退出后它们永远停在最后一次
        // 快照上：界面继续显示「正在推流 / 正在录制 / 回放缓存已开」，一键开播的状态机也据此
        // 判定，而实际上 socket 早就断了（每秒空转一次必然失败的查询）。
        if (s is ObsConnectionState.Disconnected or ObsConnectionState.Failed)
            ResetOutputStatuses();

        Notify();
    }

    /// <summary>把各项输出状态复位为「未在运行」（断线时调用；不动用户可见的设置）。</summary>
    private void ResetOutputStatuses()
    {
        RecordStatus = new ObsOutputStatus();
        StreamStatus = new ObsOutputStatus();
        VirtualCamStatus = new ObsOutputStatus();
        ReplayBufferStatus = new ObsOutputStatus();
        RecordStartedUtc = null;
        StreamStartedUtc = null;
        // 演播室状态也要复位（V3.0 第三轮验证）：OBS 退出后缓存里的 true 会让控制台
        // 一直显示「已启用」，而点「切换」必然失败。
        SetStudioMode(false);
    }

    // ------------------------------------------------------------ 连接管理

    /// <summary>按当前设置连接 OBS。<paramref name="password"/> 为空时使用设置中已保存的密码。</summary>
    public async Task<bool> ConnectAsync(string? password = null)
    {
        _userInitiatedDisconnect = false;
        CancelReconnect();
        _attempt = 0;
        return await ConnectCoreAsync(password);
    }

    private async Task<bool> ConnectCoreAsync(string? password)
    {
        var cfg = _settings.Current;
        var url = cfg.BuildUrl();
        var pwd = password ?? await _settings.GetPasswordAsync();

        SetState(ObsConnectionState.Connecting);
        try
        {
            // Identify（含鉴权）在 ConnectAsync 内部完成，这里先切到 Authenticating 便于 UI 展示
            var connectTask = _client.ConnectAsync(url, pwd, ObsEventSubscription.Default);
            SetState(ObsConnectionState.Authenticating);
            await connectTask;

            // V3.0（审查发现的竞态）：socket 可能在「Identify 成功」与这里之间被关闭
            // （OBS 刚连上就退出）。此时 OnClosed 看到的状态还不是 Connected，按新规则不会排重连；
            // 若这里无条件 SetState(Connected)，就会永久停在「显示已连接、socket 已死、永不重连」。
            if (!_client.IsOpen)
            {
                SetState(ObsConnectionState.Disconnected, Strings.T("obs.ws.closed"), isFailure: true);
                ScheduleReconnect();
                return false;
            }

            SetState(ObsConnectionState.Connected);
            _attempt = 0;
            await RefreshAllAsync();
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            // 密码错误不做自动重连：重试没有意义，只会反复弹错。
            SetState(ObsConnectionState.Failed, ex.Message, isFailure: true);
            return false;
        }
        catch (Exception ex)
        {
            SetState(ObsConnectionState.Disconnected, DescribeConnectError(ex), isFailure: true);
            // V3.0（审查发现的缺陷）：用户中途点了「断开」时不能再排自动重连 ——
            // 否则会在「连接中点了断开」之后排出一个成功的自动重连，而 `_userInitiatedDisconnect`
            // 仍为 true，导致此后真实断线被 OnClosed 的第一道分支吞掉、本会话再也不自动重连。
            if (!_userInitiatedDisconnect) ScheduleReconnect();
            return false;
        }
    }

    /// <summary>把底层异常翻译成用户能照做的提示。</summary>
    private string DescribeConnectError(Exception ex)
    {
        var cfg = _settings.Current;
        return Strings.T("obs.connect.failed", cfg.Host, cfg.Port, ex.Message);
    }

    public async Task DisconnectAsync()
    {
        _userInitiatedDisconnect = true;
        CancelReconnect();
        await _client.CloseAsync();
        SetState(ObsConnectionState.Disconnected);
    }

    private void OnClosed(string reason)
    {
        if (State == ObsConnectionState.Failed) return;
        if (_userInitiatedDisconnect)
        {
            SetState(ObsConnectionState.Disconnected);
            return;
        }

        // V3.0 修复（重连额度双扣）：只有「已经建立过会话」的断开才需要在这里排重连。
        //
        // 原来不管什么状态都排一次：握手阶段失败时，接收循环结束也会广播 Closed，
        // 而失败分支（ConnectCoreAsync 的 catch）已经排过一次 —— 两处各扣一次额度，
        // 于是 MaxAttempts = 8 实际约 4 次就放弃（约 60 秒），OBS 冷启动慢一点就会看到
        // 「已连续重连 N 次仍未成功」，而用户其实只是启动得慢。
        //
        // V3.0 审查补正：非 Connected 时**也不能把在途状态踩成 Disconnected** ——
        // 倒计时循环只更新 ReconnectInSeconds、不再设 State，一旦被踩成 Disconnected，
        // 徽章会停在「未连接」而倒计时还在跑，且录制守护依赖的「Reconnecting → Connected」
        // 跳变会消失（用户丢了一段录制却收不到任何提示）。这里只记录原因，状态编排交给
        // ConnectCoreAsync / ScheduleReconnect。
        if (State != ObsConnectionState.Connected)
        {
            LastError = reason;
            Notify();
            return;
        }

        SetState(ObsConnectionState.Disconnected, reason);
        ScheduleReconnect();
    }

    private void ScheduleReconnect()
    {
        if (!_settings.Current.AutoReconnect) return;

        _attempt++;
        if (!_policy.ShouldRetry(_attempt))
        {
            SetState(ObsConnectionState.Failed, Strings.T("obs.connect.reconnectGaveUp", _attempt - 1), isFailure: true);
            return;
        }

        CancelReconnect();
        _reconnectCts = new CancellationTokenSource();
        var token = _reconnectCts.Token;
        var delay = _policy.DelayFor(_attempt);

        SetState(ObsConnectionState.Reconnecting);
        Task.Run(() => RunReconnectCountdownAsync(delay, token), token).FireAndForget("ObsReconnect", "自动重连任务");
    }

    /// <summary>倒计时展示重连剩余秒数，结束后发起重连；取消与异常均在此收敛。</summary>
    private async Task RunReconnectCountdownAsync(TimeSpan delay, CancellationToken token)
    {
        try
        {
            var remaining = (int)Math.Ceiling(delay.TotalSeconds);
            while (remaining > 0 && !token.IsCancellationRequested)
            {
                ReconnectInSeconds = remaining;
                Notify();
                await Task.Delay(1000, token);
                remaining--;
            }
            ReconnectInSeconds = 0;
            if (!token.IsCancellationRequested)
                await ConnectCoreAsync(null);
        }
        catch (OperationCanceledException)
        {
            // 用户手动重连 / 断开时取消倒计时，属正常路径。
        }
        catch (Exception ex)
        {
            // P3-2：重连链路异常不能静默丢失，落盘留痕（不弹窗，重连失败本就有状态提示）
            FileLogger.Error("ObsReconnect", ex);
        }
    }

    private void CancelReconnect()
    {
        try { _reconnectCts?.Cancel(); } catch (Exception) { /* 已释放 */ }
        _reconnectCts?.Dispose();
        _reconnectCts = null;
        ReconnectInSeconds = 0;
    }

    // ------------------------------------------------------------ 数据刷新

    /// <summary>拉取一次完整快照（连接成功、切换页面时调用）。</summary>
    public async Task RefreshAllAsync()
    {
        if (!_client.IsOpen) return;
        await RefreshProfileAsync();
        await RefreshScenesAsync();
        await RefreshAudioInputsAsync();
        await RefreshOutputsAsync();
        // 演播室模式（V3.0 / D7）：OBS 面板上手动开关过之后，这里的按钮要能对上
        await RefreshStudioModeAsync();
        await RefreshStatsAsync();
        Notify();
    }

    public async Task RefreshProfileAsync()
    {
        var v = await _client.RequestAsync("GetVersion");
        if (v.Ok && v.Data is { } vd)
        {
            Profile.ObsVersion = Str(vd, "obsVersion");
            Profile.WebSocketVersion = Str(vd, "obsWebSocketVersion");
            Profile.Platform = Str(vd, "platformDescription");
            if (string.IsNullOrEmpty(Profile.Platform)) Profile.Platform = Str(vd, "platform");
        }

        var s = await _client.RequestAsync("GetVideoSettings");
        if (s.Ok && s.Data is { } sd)
        {
            Profile.BaseWidth = Int(sd, "baseWidth");
            Profile.BaseHeight = Int(sd, "baseHeight");
            Profile.OutputWidth = Int(sd, "outputWidth");
            Profile.OutputHeight = Int(sd, "outputHeight");
            var num = Dbl(sd, "fpsNumerator");
            var den = Dbl(sd, "fpsDenominator");
            Profile.Fps = den > 0 ? Math.Round(num / den, 2) : 0;
        }
    }

    public async Task RefreshScenesAsync()
    {
        var r = await _client.RequestAsync("GetSceneList");
        if (!r.Ok || r.Data is not { } d) return;

        CurrentScene = Str(d, "currentProgramSceneName");
        var list = new List<ObsSceneInfo>();
        if (d.TryGetProperty("scenes", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in arr.EnumerateArray())
            {
                var name = Str(e, "sceneName");
                list.Add(new ObsSceneInfo
                {
                    Name = name,
                    Index = Int(e, "sceneIndex"),
                    IsCurrent = name == CurrentScene
                });
            }
        }
        // OBS 返回的场景是倒序（索引大的在前），这里按索引升序，和 OBS 界面从上到下一致
        list.Sort((a, b) => b.Index.CompareTo(a.Index));
        Scenes = list;

        await RefreshSceneItemsAsync();
    }

    public async Task RefreshSceneItemsAsync()
    {
        if (string.IsNullOrEmpty(CurrentScene)) { CurrentSceneItems = new(); return; }

        var r = await _client.RequestAsync("GetSceneItemList", new { sceneName = CurrentScene });
        var items = new List<ObsSceneItemInfo>();
        if (r.Ok && r.Data is { } d && d.TryGetProperty("sceneItems", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in arr.EnumerateArray())
            {
                items.Add(new ObsSceneItemInfo
                {
                    Id = Int(e, "sceneItemId"),
                    SourceName = Str(e, "sourceName"),
                    Enabled = Bool(e, "sceneItemEnabled"),
                    Locked = Bool(e, "sceneItemLocked")
                });
            }
        }
        CurrentSceneItems = items;
    }

    /// <summary>输入类型中属于音频的种类（跨平台）。</summary>
    private static bool IsAudioKind(string kind) =>
        kind.Contains("audio", StringComparison.OrdinalIgnoreCase) ||
        kind.Contains("wasapi", StringComparison.OrdinalIgnoreCase) ||
        kind.Contains("coreaudio", StringComparison.OrdinalIgnoreCase) ||
        kind.Contains("pulse", StringComparison.OrdinalIgnoreCase);

    public async Task RefreshAudioInputsAsync()
    {
        var r = await _client.RequestAsync("GetInputList");
        var inputs = new List<ObsInputInfo>();
        if (r.Ok && r.Data is { } d && d.TryGetProperty("inputs", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in arr.EnumerateArray())
            {
                var kind = Str(e, "inputKind");
                if (!IsAudioKind(kind)) continue;
                inputs.Add(new ObsInputInfo { Name = Str(e, "inputName"), Kind = kind, IsAudio = true });
            }
        }

        // 逐输入补齐静音 / 音量：原来 2N 次串行往返，改为一次 CallBatch（服务端并行执行）。
        // CallBatch 是 obs-websocket 5.x 核心能力（OBS 28+ 内置），结果按请求 id 回填到原顺序。
        if (inputs.Count > 0)
        {
            var batch = new List<ObsBatchRequest>(inputs.Count * 2);
            foreach (var i in inputs)
            {
                batch.Add(new ObsBatchRequest { RequestType = "GetInputMute", RequestData = new { inputName = i.Name } });
                batch.Add(new ObsBatchRequest { RequestType = "GetInputVolume", RequestData = new { inputName = i.Name } });
            }

            var results = await _client.CallBatchAsync(batch);
            for (var idx = 0; idx < inputs.Count; idx++)
            {
                var mute = results[idx * 2];
                var vol = results[idx * 2 + 1];
                if (mute.Ok && mute.Data is { } md) inputs[idx].Muted = Bool(md, "inputMuted");
                if (vol.Ok && vol.Data is { } vd) inputs[idx].VolumeDb = (float)Dbl(vd, "inputVolumeDb");
            }
        }
        AudioInputs = inputs;
    }

    public async Task RefreshOutputsAsync()
    {
        // V3.0 审查修正：**先**取旧值再 await。
        // 原先在 await 之后才读 RecordStatus.Active / StreamStatus.Active，而事件线程可能在这段
        // 等待期间已经改过它们 —— 于是「翻转」判定会凭空造出一个新起点（已录/已播时长跳变）。
        var recordWasActive = RecordStatus.Active;
        var streamWasActive = StreamStatus.Active;

        // 四条输出状态查询合并成一次 CallBatch，连接/刷新时少三次往返
        var results = await _client.CallBatchAsync(new[]
        {
            new ObsBatchRequest { RequestType = "GetRecordStatus" },
            new ObsBatchRequest { RequestType = "GetStreamStatus" },
            new ObsBatchRequest { RequestType = "GetVirtualCamStatus" },
            new ObsBatchRequest { RequestType = "GetReplayBufferStatus" },
        });

        var rec = results[0];
        if (rec.Ok && rec.Data is { } rd)
        {
            var nowActive = Bool(rd, "outputActive");

            RecordStatus = new ObsOutputStatus
            {
                Active = nowActive,
                Paused = Bool(rd, "outputPaused"),
                Timecode = Str(rd, "outputTimecode"),
                Bytes = Lng(rd, "outputBytes")
            };

            // 本地开始时刻只作为「拿不到 timecode」时的兜底；状态码翻转时才更新，
            // 否则每次刷新都会把起点推后，时长永远停在零附近。
            TrackRecordTransition(recordWasActive, nowActive);
        }

        var st = results[1];
        if (st.Ok && st.Data is { } sd)
        {
            var streamNowActive = Bool(sd, "outputActive");
            StreamStatus = new ObsOutputStatus
            {
                Active = streamNowActive,
                Reconnecting = Bool(sd, "outputReconnecting"),
                Timecode = Str(sd, "outputTimecode"),
                Bytes = Lng(sd, "outputBytes"),
                Congestion = Dbl(sd, "outputCongestion"),
                SkippedFrames = Lng(sd, "outputSkippedFrames"),
                TotalFrames = Lng(sd, "outputTotalFrames")
            };
            TrackStreamTransition(streamWasActive, streamNowActive);
        }

        var vc = results[2];
        if (vc.Ok && vc.Data is { } vd)
        {
            VirtualCamStatus = new ObsOutputStatus { Active = Bool(vd, "outputActive") };
        }

        var rb = results[3];
        if (rb.Ok && rb.Data is { } rbd)
        {
            ReplayBufferStatus = new ObsOutputStatus { Active = Bool(rbd, "outputActive") };
        }
    }

    public async Task RefreshStatsAsync()
    {
        var r = await _client.RequestAsync("GetStats");
        if (!r.Ok || r.Data is not { } d) return;

        Stats = new ObsStats
        {
            CpuUsage = Dbl(d, "cpuUsage"),
            MemoryUsageMb = Dbl(d, "memoryUsage"),
            AvailableDiskSpaceMb = Dbl(d, "availableDiskSpace"),
            ActiveFps = Dbl(d, "activeFps"),
            AverageFrameRenderTimeMs = Dbl(d, "averageFrameRenderTime"),
            RenderSkippedFrames = Lng(d, "renderSkippedFrames"),
            RenderTotalFrames = Lng(d, "renderTotalFrames"),
            OutputSkippedFrames = Lng(d, "outputSkippedFrames"),
            OutputTotalFrames = Lng(d, "outputTotalFrames")
        };
        Notify();
    }

    // -------------------------------------------------------------- 写操作
    // 注意：所有写操作都应由 UI 在「用户确认」后调用（技术计划 §6：AI 写操作执行前必须确认）。

    public Task<ObsRequestResult> SetSceneAsync(string sceneName)
        => _client.RequestAsync("SetCurrentProgramScene", new { sceneName });

    public Task<ObsRequestResult> StartRecordAsync() => _client.RequestAsync("StartRecord");
    public Task<ObsRequestResult> StopRecordAsync() => _client.RequestAsync("StopRecord");
    public Task<ObsRequestResult> ToggleRecordPauseAsync() => _client.RequestAsync("ToggleRecordPause");

    /// <summary>按当前状态切换录制开关（托盘菜单 / 全局热键共用）。</summary>
    public Task<ObsRequestResult> ToggleRecordAsync()
        => RecordStatus.Active ? StopRecordAsync() : StartRecordAsync();

    public Task<ObsRequestResult> StartStreamAsync() => _client.RequestAsync("StartStream");
    public Task<ObsRequestResult> StopStreamAsync() => _client.RequestAsync("StopStream");

    /// <summary>按当前状态切换推流开关（托盘菜单 / 全局热键共用）。</summary>
    public Task<ObsRequestResult> ToggleStreamAsync()
        => StreamStatus.Active ? StopStreamAsync() : StartStreamAsync();

    public Task<ObsRequestResult> StartVirtualCamAsync() => _client.RequestAsync("StartVirtualCam");
    public Task<ObsRequestResult> StopVirtualCamAsync() => _client.RequestAsync("StopVirtualCam");

    // -------------------------------------------------------------- 音画同步偏移（V3.0 / D7）

    /// <summary>
    /// 读取某个音频输入的同步偏移（毫秒；正数 = 声音延后）。
    ///
    /// 为什么值得单列：音画不同步是「观众一眼看出来的问题、主播自己却很难确认」的典型，
    /// 而 OBS 的入口藏在「高级音频属性」里，且单位是毫秒 —— 用户很难靠自己试出来。
    /// </summary>
    public async Task<int?> GetAudioSyncOffsetMsAsync(string inputName)
    {
        var r = await _client.RequestAsync("GetInputAudioSyncOffset", new { inputName });
        if (!r.Ok || r.Data is not { } d) return null;
        if (!d.TryGetProperty("inputAudioSyncOffset", out var v) || v.ValueKind != JsonValueKind.Number) return null;
        return (int)Math.Round(v.GetDouble());
    }

    /// <summary>设置同步偏移（毫秒）。旧协议会自动换算成纳秒（见 <c>ObsLegacyV4Core.MapRequest</c>）。</summary>
    public Task<ObsRequestResult> SetAudioSyncOffsetMsAsync(string inputName, int offsetMs)
        => _client.RequestAsync("SetInputAudioSyncOffset", new { inputName, inputAudioSyncOffset = offsetMs });

    /// <summary>按当前状态切换虚拟摄像头开关（托盘菜单 / 全局热键共用）。</summary>
    public Task<ObsRequestResult> ToggleVirtualCamAsync()
        => VirtualCamStatus.Active ? StopVirtualCamAsync() : StartVirtualCamAsync();

    // -------------------------------------------------------------- 回放缓存（V3.0）

    /// <summary>
    /// 回放缓存（Replay Buffer）状态。开启后 OBS 在内存里滚动保留最近一段画面，
    /// <see cref="SaveReplayBufferAsync"/> 一键把「刚才那段」落盘 —— 直播里存精彩片段的通用做法。
    /// </summary>
    public ObsOutputStatus ReplayBufferStatus { get; private set; } = new();

    /// <summary>最近一次存片落盘的文件路径（来自 <c>ReplayBufferSaved</c> 事件；旧协议拿不到，为空）。</summary>
    public string LastReplayFile { get; private set; } = "";

    /// <summary>最近一次存片的时间（本地）。</summary>
    public DateTime? LastReplaySavedUtc { get; private set; }

    /// <summary>存片成功时触发（参数为文件路径，可能为空串 —— 旧协议 / 未带路径时）。</summary>
    public event Action<string>? ReplaySaved;

    public Task<ObsRequestResult> StartReplayBufferAsync() => _client.RequestAsync("StartReplayBuffer");
    public Task<ObsRequestResult> StopReplayBufferAsync() => _client.RequestAsync("StopReplayBuffer");

    /// <summary>
    /// 一键存片：把回放缓存里的「刚才那段」写盘。
    ///
    /// 这是本产品的差异点之一 —— 社区里同类能力（Shadowplay 式剪辑）要么靠第三方插件、要么靠外部脚本，
    /// 而 obs-websocket 本身就有这个能力，只是本工具此前一处未用。
    /// </summary>
    public Task<ObsRequestResult> SaveReplayBufferAsync() => _client.RequestAsync("SaveReplayBuffer");

    /// <summary>按当前状态切换回放缓存开关（托盘菜单 / 全局热键共用）。</summary>
    public Task<ObsRequestResult> ToggleReplayBufferAsync()
        => ReplayBufferStatus.Active ? StopReplayBufferAsync() : StartReplayBufferAsync();

    /// <summary>读取 OBS 当前录制输出目录（「打开录制目录」用）。</summary>
    public async Task<string?> GetRecordDirectoryAsync()
    {
        var r = await _client.RequestAsync("GetRecordDirectory");
        if (!r.Ok || r.Data is not { } d) return null;
        return d.TryGetProperty("recordDirectory", out var p) && p.ValueKind == System.Text.Json.JsonValueKind.String
            ? p.GetString()
            : null;
    }

    public Task<ObsRequestResult> SetMuteAsync(string inputName, bool muted)
        => _client.RequestAsync("SetInputMute", new { inputName, inputMuted = muted });

    public Task<ObsRequestResult> SetVolumeDbAsync(string inputName, double db)
        => _client.RequestAsync("SetInputVolume", new { inputName, inputVolumeDb = db });

    public Task<ObsRequestResult> SetSceneItemEnabledAsync(string sceneName, int sceneItemId, bool enabled)
        => _client.RequestAsync("SetSceneItemEnabled", new { sceneName, sceneItemId, sceneItemEnabled = enabled });

    /// <summary>透传任意请求，供诊断引擎的工具调用使用。</summary>
    public Task<ObsRequestResult> RawRequestAsync(string requestType, object? data = null)
        => _client.RequestAsync(requestType, data);

    // -------------------------------------------------------------- D7：其余现成能力（V3.0）

    // 这一批请求 obs-websocket 一直都有，本工具此前一处未用。每条都保持「薄封装」：
    // 只做请求与形状归一化，不做业务判断 —— 连接态护栏由调用方（界面/托盘）用 IsConnected 把住。

    /// <summary>
    /// 读取某个来源的滤镜列表（降噪 / 色键 / 锐化…）。
    /// v4 与 v5 的字段名完全不同，归一化在 <see cref="ObsLegacyV4Core.NormalizeResponse"/> 里完成。
    /// </summary>
    public async Task<IReadOnlyList<ObsFilterInfo>> GetSourceFiltersAsync(string sourceName)
    {
        var r = await _client.RequestAsync("GetSourceFilterList", new { sourceName });
        var list = new List<ObsFilterInfo>();
        if (!r.Ok || r.Data is not { } d) return list;
        if (!d.TryGetProperty("filters", out var arr) || arr.ValueKind != JsonValueKind.Array) return list;

        foreach (var e in arr.EnumerateArray())
        {
            list.Add(new ObsFilterInfo
            {
                Name = Str(e, "filterName"),
                Kind = Str(e, "filterKind"),
                Index = Int(e, "filterIndex"),
                Enabled = e.TryGetProperty("filterEnabled", out var en) && en.ValueKind == JsonValueKind.True,
            });
        }
        return list;
    }

    /// <summary>启用 / 停用某个滤镜（直播中临时关掉降噪是常见操作）。</summary>
    public Task<ObsRequestResult> SetSourceFilterEnabledAsync(string sourceName, string filterName, bool enabled)
        => _client.RequestAsync("SetSourceFilterEnabled",
            new { sourceName, filterName, filterEnabled = enabled });

    /// <summary>媒体源状态（BGM / 视频源）：播放中还是暂停、总时长与进度。</summary>
    public async Task<ObsMediaStatus?> GetMediaStatusAsync(string inputName)
    {
        var r = await _client.RequestAsync("GetMediaInputStatus", new { inputName });
        if (!r.Ok || r.Data is not { } d) return null;
        return new ObsMediaStatus
        {
            State = Str(d, "mediaState"),
            DurationMs = Lng(d, "mediaDuration"),
            CursorMs = Lng(d, "mediaCursor"),
        };
    }

    /// <summary>
    /// 触发媒体源动作：<c>OBS_WEBSOCKET_MEDIA_INPUT_ACTION_PLAY</c> / PAUSE / RESTART /
    /// STOP / NEXT / PREVIOUS。取值拼错时 OBS 会直接报错，因此这里做一次白名单校验。
    /// </summary>
    public Task<ObsRequestResult> TriggerMediaActionAsync(string inputName, string action)
    {
        if (!ObsMediaActions.IsValid(action))
            return Task.FromResult(ObsRequestResult.Fail(ObsRequestStatusCode.ClientValidationFailed, 
                Strings.T("obs.media.badAction", action, string.Join(" / ", ObsMediaActions.All))));

        return _client.RequestAsync("TriggerMediaInputAction", new { inputName, mediaAction = action });
    }

    /// <summary>读取音频输入的监听类型（None / MonitorOnly / MonitorAndOutput）。</summary>
    public async Task<string?> GetAudioMonitorTypeAsync(string inputName)
    {
        var r = await _client.RequestAsync("GetInputAudioMonitorType", new { inputName });
        if (!r.Ok || r.Data is not { } d) return null;
        var v = Str(d, "monitorType");
        return string.IsNullOrEmpty(v) ? null : v;
    }

    /// <summary>
    /// 设置音频监听类型。用 <see cref="ObsMonitorTypes.Normalize"/> 把常见写法归一化，
    /// 拼错时直接返回失败而不是发一个必然被拒的请求（旧协议对非法取值的报错很难懂）。
    /// </summary>
    public Task<ObsRequestResult> SetAudioMonitorTypeAsync(string inputName, string monitorType)
    {
        var normalized = ObsMonitorTypes.Normalize(monitorType);
        if (normalized is null)
            return Task.FromResult(ObsRequestResult.Fail(ObsRequestStatusCode.ClientValidationFailed, 
                Strings.T("obs.monitor.badType", monitorType, string.Join(" / ", ObsMonitorTypes.All))));

        return _client.RequestAsync("SetInputAudioMonitorType", new { inputName, monitorType = normalized });
    }

    /// <summary>场景集合 / 配置集快切（换机位方案、游戏↔会议）。成功与否由返回的 <c>Ok</c> 判定。</summary>
    public Task<ObsRequestResult> SetCurrentSceneCollectionAsync(string name)
        => _client.RequestAsync("SetCurrentSceneCollection", new { sceneCollectionName = name });

    public Task<ObsRequestResult> SetCurrentProfileAsync(string name)
        => _client.RequestAsync("SetCurrentProfile", new { profileName = name });

    // -------------------------------------------------------------- 演播室模式（V3.0 / D7）

    /// <summary>演播室模式是否开启（预览 → 转场 → 节目）。未连接时保持 false。</summary>
    public bool StudioModeEnabled { get; private set; }

    /// <summary>演播室模式状态变化时触发（界面据此改按钮文案）。</summary>
    public event Action? StudioModeChanged;

    public async Task<bool> RefreshStudioModeAsync()
    {
        // 断连/未就绪时不拿缓存值糊弄调用方：那会把「读不到」显示成「已关闭」。
        if (!_client.IsOpen)
        {
            SetStudioMode(false);
            return false;
        }

        var r = await _client.RequestAsync("GetStudioModeEnabled");
        if (!r.Ok || r.Data is not { } d) return StudioModeEnabled;

        var enabled = d.TryGetProperty("studioModeEnabled", out var v) && v.ValueKind == JsonValueKind.True;
        SetStudioMode(enabled);
        return StudioModeEnabled;
    }

    /// <summary>更新演播室状态并在真的变化时广播（请求成功 / 事件 / 断连复位三条路径共用）。</summary>
    private void SetStudioMode(bool enabled)
    {
        if (StudioModeEnabled == enabled) return;
        StudioModeEnabled = enabled;
        try { StudioModeChanged?.Invoke(); }
        catch (Exception) { /* 界面回调出错不能影响连接层 */ }
    }

    public async Task<ObsRequestResult> SetStudioModeEnabledAsync(bool enabled)
    {
        var r = await _client.RequestAsync("SetStudioModeEnabled", new { studioModeEnabled = enabled });
        if (r.Ok) SetStudioMode(enabled);   // 就地更新并广播：不等下一次事件/轮询，按钮文案立刻跟手
        return r;
    }

    public Task<ObsRequestResult> ToggleStudioModeAsync() => SetStudioModeEnabledAsync(!StudioModeEnabled);

    /// <summary>演播室模式下「切换」：把预览画面推到节目（等同面板上的 Transition 按钮）。</summary>
    public Task<ObsRequestResult> TriggerStudioModeTransitionAsync()
        => _client.RequestAsync("TriggerStudioModeTransition");

    // -------------------------------------------------------------- 节目画面截图（V3.0 / D7）

    /// <summary>
    /// 抓一张画面并存成文件。
    ///
    /// V3.0 第三轮验证修正（原先这里 100% 失败）：
    /// <list type="number">
    ///   <item>v5 的 <c>Request::ValidateSource</c> 把**空 sourceName** 判为 <c>RequestFieldEmpty</c>
    ///     （随后又因缺 sourceUuid 报 <c>MissingRequestField</c>），所以「空串 = 当前画面」这个假设
    ///     只在旧协议成立。这里先把空串解析成当前节目场景名，解析不到就明确失败。</item>
    ///   <item>尺寸/质量在 v5 叫 <c>imageWidth/imageHeight/imageCompressionQuality</c>，
    ///     写成 width/height/quality 会被**静默忽略**；而且没指定就不该带上
    ///     （<c>imageWidth=0</c> 会触发 402 RequestFieldOutOfRange）。</item>
    /// </list>
    /// </summary>
    public async Task<ObsRequestResult> SaveSourceScreenshotAsync(
        string sourceName, string filePath, string format = "png", int width = 0, int height = 0, int quality = -1)
    {
        var name = sourceName;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = await GetCurrentProgramSceneAsync() ?? "";
            if (name.Length == 0)
                return ObsRequestResult.Fail(ObsRequestStatusCode.ClientValidationFailed,
                    Strings.T("obs.screenshot.noScene"));
        }

        var data = new Dictionary<string, object?>
        {
            ["sourceName"] = name,
            ["imageFilePath"] = filePath,
            ["imageFormat"] = string.IsNullOrWhiteSpace(format) ? "png" : format,
        };
        if (width > 0) data["imageWidth"] = width;
        if (height > 0) data["imageHeight"] = height;
        if (quality >= 0) data["imageCompressionQuality"] = quality;

        return await _client.RequestAsync("SaveSourceScreenshot", data);
    }

    /// <summary>
    /// 当前节目场景名。v5 走 <c>GetCurrentProgramScene</c>，旧协议走 <c>GetCurrentScene</c>
    /// （归一化后两者字段一致）。拿不到返回 null —— 调用方据此中止，而不是发一个空名出去。
    /// </summary>
    public async Task<string?> GetCurrentProgramSceneAsync()
    {
        var r = await _client.RequestAsync("GetCurrentProgramScene");
        if (!r.Ok || r.Data is not { } d) return null;

        var name = Str(d, "currentProgramSceneName");
        if (name.Length == 0) name = Str(d, "sceneName");
        return name.Length > 0 ? name : null;
    }

    /// <summary>透传任意请求，并支持中途取消（模板落地 / 重置等长时间操作使用）。</summary>
    public Task<ObsRequestResult> RawRequestAsync(string requestType, object? data, CancellationToken ct)
        => _client.RequestAsync(requestType, data, ct);

    // ---------------------------------------------------------------- 事件

    private void OnObsEvent(ObsEventMessage e)
    {
        switch (e.EventType)
        {
            case "CurrentProgramSceneChanged":
                OnCurrentSceneChanged(e);
                break;

            // V3.0 第三轮验证：订阅演播室状态变化。不订阅的话，用户在 OBS 窗口里开关演播室模式后，
            // 本工具会一直显示旧状态（「切换」按钮也跟着错），直到离开页面或手动刷新。
            case "StudioModeStateChanged":
                {
                    var enabled = e.Data.TryGetProperty("studioModeEnabled", out var sm) && sm.ValueKind == JsonValueKind.True;
                    SetStudioMode(enabled);
                    break;
                }

            case "SceneListChanged":
            case "SceneCreated":
            case "SceneRemoved":
            case "SceneNameChanged":
                _ = FireAndForget(RefreshScenesAsync);
                break;

            case "RecordStateChanged":
                OnRecordStateChanged(e);
                break;

            case "RecordFileChanged":
                OnRecordFileChanged(e);
                break;

            case "StreamStateChanged":
                OnStreamStateChanged(e);
                break;

            case "VirtualcamStateChanged":
                OnVirtualCamStateChanged(e);
                break;

            // V3.0 回放缓存：状态变化刷新开关按钮；存片成功给出「存到哪了」的明确反馈
            case "ReplayBufferStateChanged":
                ReplayBufferStatus.Active = Bool(e.Data, "outputActive");
                break;

            case "ReplayBufferSaved":
                LastReplayFile = Str(e.Data, "savedReplayPath");
                LastReplaySavedUtc = DateTime.UtcNow;
                ReplaySaved?.Invoke(LastReplayFile);
                break;

            case "InputMuteStateChanged":
                OnInputMuteChanged(e);
                break;

            case "InputVolumeChanged":
                OnInputVolumeChanged(e);
                break;

            case "InputCreated":
            case "InputRemoved":
            case "InputNameChanged":
                _ = FireAndForget(RefreshAudioInputsAsync);
                break;

            case "SceneItemEnableStateChanged":
                OnSceneItemEnabledChanged(e);
                break;

            case "SceneItemCreated":
            case "SceneItemRemoved":
                _ = FireAndForget(RefreshSceneItemsAsync);
                break;

            case "ExitStarted":
                // 正常退出：写原因**但不算失败**（徽章不该把「用户自己关了 OBS」渲染成连接失败）
                LastError = Strings.T("obs.connect.shuttingDown");
                LastErrorIsFailure = false;
                break;
        }
        Notify();
    }

    /// <summary>主场景切换：更新高亮并异步刷新当前场景的来源列表。</summary>
    private void OnCurrentSceneChanged(ObsEventMessage e)
    {
        CurrentScene = Str(e.Data, "sceneName");
        foreach (var s in Scenes) s.IsCurrent = s.Name == CurrentScene;
        _ = FireAndForget(RefreshSceneItemsAsync);
    }

    private void OnRecordStateChanged(ObsEventMessage e)
    {
        var wasActive = RecordStatus.Active;
        RecordStatus.Active = Bool(e.Data, "outputActive");
        RecordStatus.Paused = Str(e.Data, "outputState") == "OBS_WEBSOCKET_OUTPUT_PAUSED";

        // 停止事件里带的路径就是刚落盘的成品文件：优先于上次的 RecordFileChanged
        var path = Str(e.Data, "outputPath");
        if (path.Length > 0) SetRecordFile(path);

        TrackRecordTransition(wasActive, RecordStatus.Active);

        // 开始 / 停止都补一次状态查询：把 timecode 与 outputBytes 拉准（事件里不带这两个值）
        _ = FireAndForget(RefreshRecordStatusAsync);
    }

    /// <summary>OBS 切换到新的录像文件（自动分段 / 长录制会连续触发）。</summary>
    private void OnRecordFileChanged(ObsEventMessage e) => SetRecordFile(Str(e.Data, "newOutputPath"));

    private void SetRecordFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        // V3.0（D3）：本次录制是否发生过**换文件**（自动分段 / 长录制切割）。
        // 判断放在这里是因为 OBS 只在真的切到新文件时才会广播 RecordFileChanged。
        if (!string.IsNullOrEmpty(LastRecordFile)
            && !string.Equals(LastRecordFile, path, StringComparison.OrdinalIgnoreCase))
            RecordFileSwitchCount++;

        LastRecordFile = path;
        RecordFileUtc = DateTime.UtcNow;
    }

    /// <summary>本次录制中 OBS 切换过多少次输出文件（&gt;0 即「分段录制」，V3.0 / D3）。</summary>
    public int RecordFileSwitchCount { get; private set; }

    /// <summary>维护「本次录制」的起止时刻，供界面显示已录时长。</summary>
    private void TrackRecordTransition(bool wasActive, bool nowActive)
    {
        if (nowActive && !wasActive)
        {
            RecordStartedUtc = DateTime.UtcNow;
            // 新一轮录制开始：清掉上一次的文件名。不清的话界面会拿旧文件估算
            // 「已写大小 / 剩余可录」，用户换了录像盘也照样按上一块盘算，低空间告警不会触发。
            LastRecordFile = "";
            RecordFileUtc = null;
            RecordFileSwitchCount = 0;   // V3.0（D3）：分段计数按「本次录制」重新开始
        }
        else if (!nowActive && wasActive)
        {
            RecordStartedUtc = null;
        }
    }

    /// <summary>维护「本次推流」的起止时刻（V3.0 / D2），供界面显示已播时长。</summary>
    private void TrackStreamTransition(bool wasActive, bool nowActive)
    {
        if (nowActive && !wasActive) StreamStartedUtc = DateTime.UtcNow;
        else if (!nowActive && wasActive) StreamStartedUtc = null;
    }

    /// <summary>
    /// 读取推流服务配置的**存在性**（V3.0 / D2）：有没有填服务器、有没有填串流密钥。
    ///
    /// 关键约束：<b>密钥不离开这个方法</b>。响应里确实带着密钥明文，这里只把它归约成两个布尔，
    /// 不存字段、不写日志、不进界面状态 —— 于是密钥没有泄漏面（连崩溃转储里也不会有）。
    /// 旧协议（v4）的映射已经在归一化阶段就丢掉了密钥，只留下 <c>hasStreamKey</c>。
    /// </summary>
    public async Task<SimpleStreamServiceInfo?> GetStreamServiceInfoAsync()
    {
        var r = await _client.RequestAsync("GetStreamServiceSettings");
        if (!r.Ok || r.Data is not { } d) return null;

        var type = Str(d, "streamServiceType");

        // 旧协议路径：映射阶段已经给出现成结论
        var hasServer = d.TryGetProperty("hasStreamServer", out var hs) && hs.ValueKind == JsonValueKind.True;
        var hasKey = d.TryGetProperty("hasStreamKey", out var hk) && hk.ValueKind == JsonValueKind.True;

        // v5 路径：从 streamServiceSettings 里现算，算完立刻丢弃原响应（不保留、不打日志）
        if (d.TryGetProperty("streamServiceSettings", out var s) && s.ValueKind == JsonValueKind.Object)
        {
            if (!hasServer && s.TryGetProperty("server", out var sv) && sv.ValueKind == JsonValueKind.String)
                hasServer = !string.IsNullOrWhiteSpace(sv.GetString());
            if (!hasKey && s.TryGetProperty("key", out var kv) && kv.ValueKind == JsonValueKind.String)
                hasKey = !string.IsNullOrWhiteSpace(kv.GetString());
        }

        return new SimpleStreamServiceInfo(hasServer, hasKey, type ?? "");
    }

    /// <summary>单独查一次录制状态（事件驱动后的补齐，失败静默）。</summary>
    private async Task RefreshRecordStatusAsync()
    {
        var r = await _client.RequestAsync("GetRecordStatus");
        if (!r.Ok || r.Data is not { } d) return;

        var wasActive = RecordStatus.Active;
        var nowActive = Bool(d, "outputActive");
        RecordStatus.Active = nowActive;
        RecordStatus.Paused = Bool(d, "outputPaused");
        RecordStatus.Timecode = Str(d, "outputTimecode");
        RecordStatus.Bytes = Lng(d, "outputBytes");
        TrackRecordTransition(wasActive, nowActive);
    }

    /// <summary>
    /// 单独查一次推流状态（V3.0 / D2；事件驱动后的补齐，失败静默）。
    /// 与录制同理：<c>StreamStateChanged</c> 事件里不带 timecode 与字节数，要补一次查询。
    /// </summary>
    private async Task RefreshStreamStatusAsync()
    {
        var r = await _client.RequestAsync("GetStreamStatus");
        if (!r.Ok || r.Data is not { } d) return;

        var wasActive = StreamStatus.Active;
        var nowActive = Bool(d, "outputActive");
        StreamStatus.Active = nowActive;
        StreamStatus.Reconnecting = Bool(d, "outputReconnecting");
        StreamStatus.Timecode = Str(d, "outputTimecode");
        StreamStatus.Bytes = Lng(d, "outputBytes");
        StreamStatus.Congestion = Dbl(d, "outputCongestion");
        StreamStatus.SkippedFrames = Lng(d, "outputSkippedFrames");
        StreamStatus.TotalFrames = Lng(d, "outputTotalFrames");
        TrackStreamTransition(wasActive, nowActive);
    }

    private void OnStreamStateChanged(ObsEventMessage e)
    {
        var wasActive = StreamStatus.Active;
        StreamStatus.Active = Bool(e.Data, "outputActive");
        StreamStatus.Reconnecting = Str(e.Data, "outputState") == "OBS_WEBSOCKET_OUTPUT_RECONNECTING";

        // V3.0（D2）：维护「本次推流」的起止时刻，并补一次查询把 timecode / 字节数拉准
        TrackStreamTransition(wasActive, StreamStatus.Active);
        _ = FireAndForget(RefreshStreamStatusAsync);
    }

    private void OnVirtualCamStateChanged(ObsEventMessage e)
        => VirtualCamStatus.Active = Bool(e.Data, "outputActive");

    private void OnInputMuteChanged(ObsEventMessage e)
    {
        var name = Str(e.Data, "inputName");
        var i = AudioInputs.FirstOrDefault(x => x.Name == name);
        if (i is not null) i.Muted = Bool(e.Data, "inputMuted");
    }

    private void OnInputVolumeChanged(ObsEventMessage e)
    {
        var name = Str(e.Data, "inputName");
        var i = AudioInputs.FirstOrDefault(x => x.Name == name);
        if (i is not null) i.VolumeDb = (float)Dbl(e.Data, "inputVolumeDb");
    }

    private void OnSceneItemEnabledChanged(ObsEventMessage e)
    {
        var id = Int(e.Data, "sceneItemId");
        var item = CurrentSceneItems.FirstOrDefault(x => x.Id == id);
        if (item is not null) item.Enabled = Bool(e.Data, "sceneItemEnabled");
    }

    private async Task FireAndForget(Func<Task> action)
    {
        try { await action(); Notify(); }
        catch (Exception ex)
        {
            // V3.0：不再完全静默。后台刷新失败时界面会一直显示陈旧数据（场景列表 / 音频 / 录制状态），
            // 而线上日志里一个字的线索都没有 —— 与仓库既有的「异常一律落 FileLogger」口径也不一致
            // （对照 TaskExtensions.FireAndForget）。
            FileLogger.Warn("ObsRefresh", $"连接后刷新失败，界面可能短暂显示旧数据：{ex.Message}");
        }
    }

    // ------------------------------------------------------------ JSON 读取
    // OBS 返回值类型偶有差异（如整数被序列化为 double），统一容错读取。

    private static string Str(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";

    private static bool Bool(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static int Int(JsonElement e, string name)
        => (int)Dbl(e, name);

    private static long Lng(JsonElement e, string name)
        => (long)Dbl(e, name);

    private static double Dbl(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return 0;
        return v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : 0;
    }

    public async ValueTask DisposeAsync()
    {
        CancelReconnect();
        _client.EventReceived -= OnObsEvent;
        _client.Closed -= OnClosed;
        await _client.DisposeAsync();
    }
}
