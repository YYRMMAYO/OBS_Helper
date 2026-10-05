using System.Text.Json;
using OBS_Helper.Wpf.Models.Obs;

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

    private void SetState(ObsConnectionState s, string? error = null)
    {
        State = s;
        LastError = error;
        Notify();
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

            SetState(ObsConnectionState.Connected);
            _attempt = 0;
            await RefreshAllAsync();
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            // 密码错误不做自动重连：重试没有意义，只会反复弹错。
            SetState(ObsConnectionState.Failed, ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            SetState(ObsConnectionState.Disconnected, DescribeConnectError(ex));
            ScheduleReconnect();
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
        SetState(ObsConnectionState.Disconnected, reason);
        ScheduleReconnect();
    }

    private void ScheduleReconnect()
    {
        if (!_settings.Current.AutoReconnect) return;

        _attempt++;
        if (!_policy.ShouldRetry(_attempt))
        {
            SetState(ObsConnectionState.Failed, Strings.T("obs.connect.reconnectGaveUp", _attempt - 1));
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
        // 三条输出状态查询合并成一次 CallBatch，连接/刷新时少两次往返
        var results = await _client.CallBatchAsync(new[]
        {
            new ObsBatchRequest { RequestType = "GetRecordStatus" },
            new ObsBatchRequest { RequestType = "GetStreamStatus" },
            new ObsBatchRequest { RequestType = "GetVirtualCamStatus" },
        });

        var rec = results[0];
        if (rec.Ok && rec.Data is { } rd)
        {
            var wasActive = RecordStatus.Active;
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
            TrackRecordTransition(wasActive, nowActive);
        }

        var st = results[1];
        if (st.Ok && st.Data is { } sd)
        {
            StreamStatus = new ObsOutputStatus
            {
                Active = Bool(sd, "outputActive"),
                Reconnecting = Bool(sd, "outputReconnecting"),
                Timecode = Str(sd, "outputTimecode"),
                Bytes = Lng(sd, "outputBytes"),
                Congestion = Dbl(sd, "outputCongestion"),
                SkippedFrames = Lng(sd, "outputSkippedFrames"),
                TotalFrames = Lng(sd, "outputTotalFrames")
            };
        }

        var vc = results[2];
        if (vc.Ok && vc.Data is { } vd)
        {
            VirtualCamStatus = new ObsOutputStatus { Active = Bool(vd, "outputActive") };
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

    /// <summary>按当前状态切换虚拟摄像头开关（托盘菜单 / 全局热键共用）。</summary>
    public Task<ObsRequestResult> ToggleVirtualCamAsync()
        => VirtualCamStatus.Active ? StopVirtualCamAsync() : StartVirtualCamAsync();

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
                LastError = Strings.T("obs.connect.shuttingDown");
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
        LastRecordFile = path;
        RecordFileUtc = DateTime.UtcNow;
    }

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
        }
        else if (!nowActive && wasActive)
        {
            RecordStartedUtc = null;
        }
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

    private void OnStreamStateChanged(ObsEventMessage e)
    {
        StreamStatus.Active = Bool(e.Data, "outputActive");
        StreamStatus.Reconnecting = Str(e.Data, "outputState") == "OBS_WEBSOCKET_OUTPUT_RECONNECTING";
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
        catch (Exception) { /* 后台刷新失败不影响 UI，下次事件或手动刷新会纠正 */ }
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
