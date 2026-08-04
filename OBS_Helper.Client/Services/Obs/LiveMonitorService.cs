namespace OBS_Helper.Client.Services.Obs;

/// <summary>一条实时告警。</summary>
public sealed class LiveAlert
{
    public string Code { get; init; } = "";
    public LogSeverity Severity { get; init; }
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public string Suggestion { get; init; } = "";
    public string? ProblemId { get; init; }
    public DateTime At { get; init; } = DateTime.Now;

    public string SeverityText => Severity switch
    {
        LogSeverity.Critical => "严重",
        LogSeverity.Error => "错误",
        LogSeverity.Warning => "警告",
        _ => "提示"
    };
}

/// <summary>一次采样的瞬时指标（相对上一次采样的增量）。</summary>
public sealed class LiveSample
{
    public DateTime At { get; init; } = DateTime.Now;
    public double CpuUsage { get; init; }
    public double ActiveFps { get; init; }
    public double FrameRenderTimeMs { get; init; }
    /// <summary>本窗口内新增的渲染丢帧率（0~1）。</summary>
    public double RenderSkipRatio { get; init; }
    /// <summary>本窗口内新增的编码丢帧率（0~1）。</summary>
    public double OutputSkipRatio { get; init; }
    /// <summary>本窗口内新增的推流丢帧率（0~1）。</summary>
    public double StreamDropRatio { get; init; }
    public double AvailableDiskGb { get; init; }
}

/// <summary>
/// 实时监控服务（方向 C）。
///
/// <b>为什么不能直接看 OBS 给的丢帧率：</b>
/// <c>GetStats</c> 返回的 <c>renderSkippedFrames / renderTotalFrames</c> 是「自 OBS 启动以来」
/// 的累计值。直播三小时后，即使此刻正在疯狂掉帧，累计比例也只会缓慢爬升——等它超过阈值，
/// 观众早就跑光了。因此本服务保存上一次采样的原始计数，每次只计算<b>两次采样之间新增</b>
/// 的丢帧比例，这才是「现在卡不卡」的真实反映。
///
/// 其余设计：
/// <list type="bullet">
///   <item>连接建立后自动开始轮询，断开自动停止——页面不需要关心生命周期；</item>
///   <item>同一类告警有冷却期，避免一直卡顿时刷屏；</item>
///   <item>告警只保留最近若干条，长时间直播不会无限吃内存；</item>
///   <item>轮询异常一律吞掉：监控本身绝不能把主连接搞崩。</item>
/// </list>
/// </summary>
public sealed class LiveMonitorService : IAsyncDisposable
{
    /// <summary>采样间隔。2 秒足以捕捉卡顿，又不会给 OBS 增加可感知负担。</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);
    /// <summary>同类告警的冷却期，防止持续异常时刷屏。</summary>
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(60);
    /// <summary>告警列表上限。</summary>
    private const int MaxAlerts = 50;
    /// <summary>指标曲线保留的采样点数（约 2 分钟）。</summary>
    private const int MaxSamples = 60;

    // —— 阈值 ——
    private const double RenderSkipWarn = 0.02;   // 窗口内 2% 渲染丢帧
    private const double RenderSkipCrit = 0.10;
    private const double OutputSkipWarn = 0.02;   // 窗口内 2% 编码丢帧
    private const double OutputSkipCrit = 0.10;
    private const double StreamDropWarn = 0.01;   // 推流丢帧对观众最敏感，阈值最低
    private const double StreamDropCrit = 0.05;
    private const double CpuWarn = 80;            // OBS 自身 CPU 占用（百分比）
    private const double DiskWarnGb = 10;
    private const double FpsDropRatio = 0.9;      // 实际帧率低于目标的 90%

    private readonly ObsConnectionService _conn;
    private readonly List<LiveAlert> _alerts = new();
    private readonly List<LiveSample> _samples = new();
    private readonly Dictionary<string, DateTime> _lastFired = new(StringComparer.Ordinal);

    private CancellationTokenSource? _cts;
    private Task? _loop;

    // 上一次采样的累计计数，用于算增量
    private long _prevRenderSkipped, _prevRenderTotal;
    private long _prevOutputSkipped, _prevOutputTotal;
    private long _prevStreamDropped, _prevStreamTotal;
    private bool _hasPrev;

    public LiveMonitorService(ObsConnectionService conn)
    {
        _conn = conn;
        // 连接状态一变就同步启停，页面无需手动管理。
        _conn.StateChanged += OnConnectionStateChanged;
    }

    /// <summary>监控是否正在运行。</summary>
    public bool IsRunning => _loop is { IsCompleted: false };

    /// <summary>告警列表（最新的在前）。</summary>
    public IReadOnlyList<LiveAlert> Alerts => _alerts;

    /// <summary>最近的采样序列（旧 → 新），可用于画迷你曲线。</summary>
    public IReadOnlyList<LiveSample> Samples => _samples;

    /// <summary>最近一次采样。</summary>
    public LiveSample? Latest => _samples.Count > 0 ? _samples[^1] : null;

    /// <summary>告警或采样更新时触发，页面据此重绘。</summary>
    public event Action? Changed;

    /// <summary>用户可以临时关掉监控（例如觉得打扰）。</summary>
    public bool Enabled { get; private set; } = true;

    public void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        if (enabled)
        {
            if (_conn.IsConnected) Start();
        }
        else
        {
            _ = StopAsync();
        }
        Changed?.Invoke();
    }

    public void ClearAlerts()
    {
        _alerts.Clear();
        _lastFired.Clear();
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------ 生命周期

    private void OnConnectionStateChanged()
    {
        if (_conn.IsConnected && Enabled)
        {
            Start();
        }
        else if (!_conn.IsConnected && IsRunning)
        {
            _ = StopAsync();
        }
    }

    public void Start()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        // 重新开始时清空基线：跨连接的计数没有可比性。
        _hasPrev = false;
        _loop = RunAsync(_cts.Token);
    }

    public async Task StopAsync()
    {
        var cts = _cts;
        var loop = _loop;
        _cts = null;
        _loop = null;

        if (cts is null) return;
        cts.Cancel();
        try
        {
            if (loop is not null) await loop;
        }
        catch (OperationCanceledException)
        {
            // 正常退出路径
        }
        cts.Dispose();
    }

    private async Task RunAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await SafeWaitAsync(timer, token))
        {
            if (!_conn.IsConnected) continue;

            try
            {
                await _conn.RefreshStatsAsync();
                await _conn.RefreshOutputsAsync();
                Sample();
            }
            catch
            {
                // 监控绝不能把主连接搞崩：单次采样失败就跳过，等下一轮。
            }
        }
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken token)
    {
        try { return await timer.WaitForNextTickAsync(token); }
        catch (OperationCanceledException) { return false; }
    }

    // ------------------------------------------------------------------ 采样与判定

    private void Sample()
    {
        var s = _conn.Stats;
        var stream = _conn.StreamStatus;

        // 增量丢帧率：这是「现在卡不卡」，而不是「开播以来平均卡不卡」
        double renderRatio = Delta(_prevRenderSkipped, s.RenderSkippedFrames, _prevRenderTotal, s.RenderTotalFrames);
        double outputRatio = Delta(_prevOutputSkipped, s.OutputSkippedFrames, _prevOutputTotal, s.OutputTotalFrames);
        double streamRatio = Delta(_prevStreamDropped, stream.SkippedFrames, _prevStreamTotal, stream.TotalFrames);

        bool firstSample = !_hasPrev;

        _prevRenderSkipped = s.RenderSkippedFrames;
        _prevRenderTotal = s.RenderTotalFrames;
        _prevOutputSkipped = s.OutputSkippedFrames;
        _prevOutputTotal = s.OutputTotalFrames;
        _prevStreamDropped = stream.SkippedFrames;
        _prevStreamTotal = stream.TotalFrames;
        _hasPrev = true;

        var sample = new LiveSample
        {
            CpuUsage = s.CpuUsage,
            ActiveFps = s.ActiveFps,
            FrameRenderTimeMs = s.AverageFrameRenderTimeMs,
            RenderSkipRatio = renderRatio,
            OutputSkipRatio = outputRatio,
            StreamDropRatio = streamRatio,
            AvailableDiskGb = s.AvailableDiskSpaceMb / 1024.0
        };

        _samples.Add(sample);
        if (_samples.Count > MaxSamples) _samples.RemoveAt(0);

        // 第一次采样没有可比基线，只记录不告警，否则会把「开播以来的累计丢帧」误报成瞬时卡顿。
        if (!firstSample) Evaluate(sample);

        Changed?.Invoke();
    }

    /// <summary>两次采样之间新增的丢帧比例；总帧数没涨（OBS 空闲）时返回 0。</summary>
    private static double Delta(long prevSkipped, long nowSkipped, long prevTotal, long nowTotal)
    {
        long dTotal = nowTotal - prevTotal;
        long dSkipped = nowSkipped - prevSkipped;
        if (dTotal <= 0 || dSkipped < 0) return 0;
        return Math.Min(1.0, (double)dSkipped / dTotal);
    }

    private void Evaluate(LiveSample s)
    {
        if (s.RenderSkipRatio >= RenderSkipWarn)
        {
            Fire("LIVE-RENDER", s.RenderSkipRatio >= RenderSkipCrit ? LogSeverity.Critical : LogSeverity.Warning,
                $"画面渲染掉帧 {s.RenderSkipRatio * 100:0.#}%",
                $"最近 {Interval.TotalSeconds:0} 秒内 GPU 没能按时完成画面合成。",
                "降低画布 / 输出分辨率或帧率；关闭其他占用显卡的程序；减少浏览器源与滤镜数量。",
                "lag-skip");
        }

        if (s.OutputSkipRatio >= OutputSkipWarn)
        {
            Fire("LIVE-ENCODE", s.OutputSkipRatio >= OutputSkipCrit ? LogSeverity.Critical : LogSeverity.Warning,
                $"编码器跳帧 {s.OutputSkipRatio * 100:0.#}%",
                $"最近 {Interval.TotalSeconds:0} 秒内编码器来不及处理画面。",
                "把 x264 预设调快一档（veryfast / ultrafast），或改用显卡硬件编码（NVENC / QSV / AMF）；也可下调输出分辨率。",
                "enc-overload");
        }

        if (s.StreamDropRatio >= StreamDropWarn)
        {
            Fire("LIVE-NETWORK", s.StreamDropRatio >= StreamDropCrit ? LogSeverity.Critical : LogSeverity.Error,
                $"推流丢帧 {s.StreamDropRatio * 100:0.#}%",
                "上行带宽不足，观众端会出现卡顿或花屏。",
                "把码率降到实测上行的 60~70%；优先使用有线网络；开启动态码率让 OBS 自动降码。",
                "lag-network");
        }

        if (s.CpuUsage >= CpuWarn)
        {
            Fire("LIVE-CPU", LogSeverity.Warning,
                $"OBS CPU 占用 {s.CpuUsage:0.#}%",
                "CPU 接近饱和，容易引发编码跳帧与音频断续。",
                "改用硬件编码把负载移到显卡；关闭不必要的后台程序；减少高开销滤镜（如降噪、色度键叠加）。",
                "enc-overload");
        }

        if (s.AvailableDiskGb > 0 && s.AvailableDiskGb < DiskWarnGb && _conn.RecordStatus.Active)
        {
            Fire("LIVE-DISK", s.AvailableDiskGb < 2 ? LogSeverity.Critical : LogSeverity.Warning,
                $"录制盘仅剩 {s.AvailableDiskGb:0.#} GB",
                "正在录制中，空间耗尽会直接导致录像文件损坏。",
                "立即清理磁盘空间，或停止录制后改到其它盘。",
                "rc-diskfull");
        }

        // 实际帧率明显低于目标：这是「掉帧」最直观的表现
        double target = _conn.Profile.Fps;
        if (target > 0 && s.ActiveFps > 0 && s.ActiveFps < target * FpsDropRatio)
        {
            Fire("LIVE-FPS", LogSeverity.Warning,
                $"实际帧率 {s.ActiveFps:0.#} / 目标 {target:0.#}",
                "OBS 没能跑满设定帧率。",
                "多为 GPU 或 CPU 压力过大所致，可先降低输出分辨率观察是否恢复。",
                "lag-skip");
        }
    }

    /// <summary>触发一条告警；同 Code 在冷却期内只记一次。</summary>
    private void Fire(string code, LogSeverity severity, string title, string detail, string suggestion, string? problemId)
    {
        var now = DateTime.Now;
        if (_lastFired.TryGetValue(code, out var last) && now - last < Cooldown) return;
        _lastFired[code] = now;

        _alerts.Insert(0, new LiveAlert
        {
            Code = code,
            Severity = severity,
            Title = title,
            Detail = detail,
            Suggestion = suggestion,
            ProblemId = problemId,
            At = now
        });

        if (_alerts.Count > MaxAlerts) _alerts.RemoveRange(MaxAlerts, _alerts.Count - MaxAlerts);
    }

    public async ValueTask DisposeAsync()
    {
        _conn.StateChanged -= OnConnectionStateChanged;
        await StopAsync();
    }
}
