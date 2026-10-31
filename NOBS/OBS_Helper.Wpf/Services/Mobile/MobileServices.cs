using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using OBS_Helper.Wpf.Models.Mobile;
using OBS_Helper.Wpf.Models.Obs;
using OBS_Helper.Wpf.Models.Shell;
using OBS_Helper.Wpf.Localization;
using OBS_Helper.Wpf.Services.Host;
using OBS_Helper.Wpf.Services.Interop;
using OBS_Helper.Wpf.Services.Live;
using OBS_Helper.Wpf.Services.Obs;
using OBS_Helper.Wpf.Services.Security;
using OBS_Helper.Wpf.Services.Shell;

namespace OBS_Helper.Wpf.Services.Mobile;

/// <summary>把 OBS / 提词器 / 系统状态折叠成手机快照（V3.4.0 / M02）。</summary>
public sealed class MobileSnapshotService
{
    private readonly ObsConnectionService _obs;
    private readonly TeleprompterService _prompter;
    private readonly SystemMonitorService? _monitor;

    public MobileSnapshotService(ObsConnectionService obs, TeleprompterService prompter, SystemMonitorService? monitor = null)
    {
        _obs = obs;
        _prompter = prompter;
        _monitor = monitor;
    }

    public MobileSnapshot Build(MobileDevice? device)
    {
        var settings = _prompter.Settings;
        var sample = _monitor?.Latest;
        return new MobileSnapshot
        {
            CurrentScene = _obs.CurrentScene,
            Scenes = _obs.Scenes.Select(s => s.Name).ToList(),
            Audio = MobileStateCore.AudioSummary(_obs.AudioInputs
                .Where(i => i.IsAudio)
                .Select(i => new MobileAudioState { Name = i.Name, Muted = i.Muted, VolumeDb = i.VolumeDb })),
            Recording = _obs.RecordStatus.Active,
            RecordPaused = _obs.RecordStatus.Paused,
            RecordTime = MobileStateCore.FormatElapsed(_obs.RecordElapsed),
            Streaming = _obs.StreamStatus.Active,
            StreamTime = MobileStateCore.FormatElapsed(_obs.StreamElapsed),
            VirtualCam = _obs.VirtualCamStatus.Active,
            ReplayBuffer = _obs.ReplayBufferStatus.Active,
            StudioMode = _obs.StudioModeEnabled,
            Tally = MobileStateCore.Tally(_obs.IsConnected, _obs.StreamStatus.Active, _obs.RecordStatus.Active),
            Role = device is null ? "viewer" : MobileRoles.Name(device.ParsedRole),
            DeviceName = device?.Name ?? "",
            ObsTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Teleprompter = new MobileTeleprompterState
            {
                Playing = _prompter.IsPlaying,
                Speed = (int)Math.Round(settings.Speed),
                FontSize = (int)Math.Round(settings.FontSize),
                Mirror = settings.MirrorHorizontal,
                Script = _prompter.Script,
            },
            System = new MobileSystemState
            {
                Cpu = sample?.CpuPercent ?? 0,
                MemoryMb = sample?.MemUsedMb ?? 0,
                DiskFreeMb = (sample?.LowestDisk?.FreeGb ?? 0) * 1024,
            },
        };
    }
}

/// <summary>
/// 节目画面预览（M02）：480p JPEG，默认 1 fps、最高 5 fps；单飞 + 背压 + 失败退避。
/// 连续失败 3 次后冷却 10 秒，避免在 OBS 未输出时把 CPU 打满。
/// </summary>
public sealed class MobilePreviewService
{
    private readonly ObsConnectionService _obs;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _single = new(1, 1);
    private readonly object _gate = new();
    private byte[]? _lastFrame;
    private DateTimeOffset _lastCapture;
    private int _failures;
    private DateTimeOffset _cooldownUntil;

    public MobilePreviewService(ObsConnectionService obs, Func<DateTimeOffset>? clock = null)
    {
        _obs = obs;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public int Failures => _failures;

    /// <summary>取一帧；返回 null 且 code 非空表示失败。请求过快时直接返回缓存帧。</summary>
    public async Task<(byte[]? Frame, string Code)> GetFrameAsync(int fps, CancellationToken ct = default)
    {
        fps = Math.Clamp(fps, 1, 5);
        var now = _clock();
        lock (_gate)
        {
            if (now < _cooldownUntil) return (_lastFrame, FeatureErrors.MobPreviewFailed);
            if (_lastFrame is not null && (now - _lastCapture).TotalMilliseconds < 1000.0 / fps)
                return (_lastFrame, "");
        }
        if (!await _single.WaitAsync(0, ct).ConfigureAwait(false)) return (_lastFrame, "");
        try
        {
            var frame = await CaptureAsync(ct).ConfigureAwait(false);
            lock (_gate)
            {
                if (frame is not null)
                {
                    _lastFrame = frame;
                    _lastCapture = _clock();
                    _failures = 0;
                    return (frame, "");
                }
                _failures++;
                if (_failures >= 3) _cooldownUntil = _clock().AddSeconds(10);
                return (_lastFrame, FeatureErrors.MobPreviewFailed);
            }
        }
        finally
        {
            _single.Release();
        }
    }

    private async Task<byte[]?> CaptureAsync(CancellationToken ct)
    {
        if (!_obs.IsConnected) return null;
        try
        {
            var scene = await _obs.GetCurrentProgramSceneAsync().ConfigureAwait(false);
            if (string.IsNullOrEmpty(scene)) return null;
            var result = await _obs.RawRequestAsync("GetSourceScreenshot", new Dictionary<string, object?>
            {
                ["sourceName"] = scene,
                ["imageFormat"] = "jpg",
                ["imageWidth"] = 480,
                ["imageCompressionQuality"] = 60,
            }, ct).ConfigureAwait(false);
            if (result.Ok && result.Data is { } data &&
                data.TryGetProperty("imageData", out var imageData) &&
                imageData.ValueKind == JsonValueKind.String)
            {
                var base64 = imageData.GetString() ?? "";
                var comma = base64.IndexOf(',');
                if (comma >= 0) base64 = base64[(comma + 1)..];
                return Convert.FromBase64String(base64);
            }

            // 旧协议不一定支持 GetSourceScreenshot：退回落盘截图再读回
            var temp = Path.Combine(Path.GetTempPath(), "obshelper-preview-" + Guid.NewGuid().ToString("N") + ".jpg");
            var saved = await _obs.SaveSourceScreenshotAsync(scene, temp, "jpg", 480, 0, 60).ConfigureAwait(false);
            if (!saved.Ok || !File.Exists(temp)) return null;
            var bytes = await File.ReadAllBytesAsync(temp, ct).ConfigureAwait(false);
            try { File.Delete(temp); } catch { }
            return bytes;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }
}

/// <summary>上传文件的元信息（不包含用户原始文件名）。</summary>
public sealed class MobileUploadInfo
{
    public string FileName { get; init; } = "";
    public string Path { get; init; } = "";
    public string Kind { get; init; } = "";
    public long Size { get; init; }
    public DateTimeOffset UploadedUtc { get; init; }
}

/// <summary>
/// 手机文件快传（M06）：50 MB 上限、JPEG/PNG/MP4、魔数校验、文件名不复用用户输入。
/// 默认只落盘，**不自动进 OBS**；PC 页面上用户确认后才创建 image_source / ffmpeg_source。
/// </summary>
public sealed class MobileUploadService
{
    public const long MaxBytes = 50L * 1024 * 1024;
    private readonly string _directory;

    public MobileUploadService(string? directory = null)
        => _directory = directory ?? Path.Combine(HostBridge.AppDataDirectory, "mobile", "uploads");

    public string DirectoryPath => _directory;

    public (bool Ok, string Code, MobileUploadInfo? Info) Save(MultipartPart part)
    {
        if (part.Data.LongLength > MaxBytes) return (false, FeatureErrors.MobUploadTooLarge, null);
        var kind = DetectKind(part.Data);
        if (kind.Length == 0) return (false, FeatureErrors.MobUploadBadType, null);
        try
        {
            Directory.CreateDirectory(_directory);
            var name = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + LanPolicy.NewToken(6)[..8] + kind;
            var path = Path.Combine(_directory, name);
            File.WriteAllBytes(path, part.Data);
            return (true, "", new MobileUploadInfo
            {
                FileName = name,
                Path = path,
                Kind = kind,
                Size = part.Data.LongLength,
                UploadedUtc = DateTimeOffset.Now,
            });
        }
        catch (Exception)
        {
            return (false, FeatureErrors.MobUploadBadType, null);
        }
    }

    /// <summary>按魔数判断类型，返回扩名（含点）；不认识返回空串。</summary>
    public static string DetectKind(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return ".jpg";
        if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47
            && data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A) return ".png";
        if (data.Length >= 12 && data[4] == 'f' && data[5] == 't' && data[6] == 'y' && data[7] == 'p') return ".mp4";
        return "";
    }

    public IReadOnlyList<MobileUploadInfo> List(int max = 50)
    {
        try
        {
            if (!Directory.Exists(_directory)) return Array.Empty<MobileUploadInfo>();
            return new DirectoryInfo(_directory).GetFiles()
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(max)
                .Select(f => new MobileUploadInfo
                {
                    FileName = f.Name,
                    Path = f.FullName,
                    Kind = f.Extension.ToLowerInvariant(),
                    Size = f.Length,
                    UploadedUtc = f.LastWriteTimeUtc,
                })
                .ToList();
        }
        catch (Exception)
        {
            return Array.Empty<MobileUploadInfo>();
        }
    }

    /// <summary>用户确认后把上传文件加进 OBS 当前场景（图片 → image_source，视频 → ffmpeg_source）。</summary>
    public async Task<ObsRequestResult> AddToObsAsync(ObsConnectionService obs, MobileUploadInfo info)
    {
        var scene = await obs.GetCurrentProgramSceneAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(scene))
            return ObsRequestResult.Fail(1, FeatureErrors.MobObsOffline);
        var isVideo = string.Equals(info.Kind, ".mp4", StringComparison.OrdinalIgnoreCase);
        var inputName = $"手机快传 {Path.GetFileNameWithoutExtension(info.FileName)}";
        var settings = isVideo
            ? new Dictionary<string, object?>
            {
                ["is_local_file"] = true,
                ["local_file"] = info.Path,
                ["looping"] = false,
            }
            : new Dictionary<string, object?> { ["file"] = info.Path };
        return await obs.RawRequestAsync("CreateInput", new Dictionary<string, object?>
        {
            ["sceneName"] = scene,
            ["inputName"] = inputName,
            ["inputKind"] = isVideo ? "ffmpeg_source" : "image_source",
            ["inputSettings"] = settings,
            ["sceneItemEnabled"] = true,
        }).ConfigureAwait(false);
    }
}

/// <summary>
/// 手机命令路由（M01）：只调用白名单动作，永远不把 obs-websocket 原始请求暴露给手机。
/// 每个请求带 requestId 幂等；限流 20 次/秒/设备；破坏性动作在 <see cref="MobileCommandCore"/> 已校验 confirm。
/// </summary>
public sealed class MobileCommandRouter
{
    private readonly ObsConnectionService _obs;
    private readonly TeleprompterService _prompter;
    private readonly MobileSessionStore _sessions;
    private readonly MobileSnapshotService _snapshots;
    private readonly MobilePreviewService _preview;
    private readonly LanPolicy.RateLimiter _limiter;
    private readonly MobileCommandCore.IdempotencyCache _idempotency = new();
    private readonly Action<string>? _revokeCallback;

    public MobileCommandRouter(
        ObsConnectionService obs, TeleprompterService prompter, MobileSessionStore sessions,
        MobileSnapshotService snapshots, MobilePreviewService preview, Action<string>? revokeCallback = null)
    {
        _obs = obs;
        _prompter = prompter;
        _sessions = sessions;
        _snapshots = snapshots;
        _preview = preview;
        _revokeCallback = revokeCallback;
        _limiter = new LanPolicy.RateLimiter(LanPolicy.CommandsPerSecond);
    }

    public async Task<MobileCommandResult> ExecuteAsync(MobileDevice device, MobileCommandRequest request, CancellationToken ct = default)
    {
        var requestId = MobileCommandCore.NormalizeRequestId(request.RequestId);
        if (requestId.Length == 0)
            return MobileCommandResult.Fail("", FeatureErrors.MobCommandInvalid, FeatureErrors.Describe(FeatureErrors.MobCommandInvalid));
        if (_idempotency.TryGet(device.DeviceId, requestId, out var cached)) return cached;
        if (!_limiter.TryAcquire(device.DeviceId))
            return MobileCommandResult.Fail(requestId, FeatureErrors.MobRateLimited, FeatureErrors.Describe(FeatureErrors.MobRateLimited));

        var context = new MobileCommandCore.Context
        {
            ObsOnline = _obs.IsConnected,
            Scenes = _obs.Scenes.Select(s => s.Name).ToList(),
            Audio = _obs.AudioInputs.Where(i => i.IsAudio)
                .Select(i => new MobileAudioState { Name = i.Name, Muted = i.Muted, VolumeDb = i.VolumeDb }).ToList(),
            SceneItemIds = _obs.CurrentSceneItems.Select(i => i.Id).ToList(),
            ReplayBufferActive = _obs.ReplayBufferStatus.Active,
        };
        var validation = MobileCommandCore.Validate(request, device.ParsedRole, context, requestId);
        if (!validation.Ok)
        {
            var result = MobileCommandResult.Fail(requestId, validation.Code, FeatureErrors.Describe(validation.Code));
            _idempotency.Put(device.DeviceId, requestId, result);
            return result;
        }

        MobileCommandResult response;
        try
        {
            response = await DispatchAsync(device, request, requestId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 真正的异常（提词器保存失败、本地 IO 等）不能伪装成「OBS 未连接」：
            // 旧实现让任何异常都返回 MOB_OBS_OFFLINE，手机端就出现「明明连着却提示未连接」。
            FileLogger.Warn("Mobile", $"命令执行失败（{request.Command}）：{ex.Message}");
            response = MobileCommandResult.Fail(requestId, FeatureErrors.MobCommandFailed,
                FeatureErrors.Describe(FeatureErrors.MobCommandFailed));
        }
        _idempotency.Put(device.DeviceId, requestId, response);
        if (response.Ok) _sessions.Touch(device.DeviceId);
        return response;
    }

    private async Task<MobileCommandResult> DispatchAsync(MobileDevice device, MobileCommandRequest request, string requestId, CancellationToken ct)
    {
        string Str(string name) => request.Params.TryGetValue(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : "";
        double Num(string name) => request.Params.TryGetValue(name, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble()
            : double.TryParse(Str(name), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : double.NaN;
        bool Bool(string name) => request.Params.TryGetValue(name, out var e) &&
            (e.ValueKind == JsonValueKind.True || (e.ValueKind == JsonValueKind.String && bool.TryParse(e.GetString(), out var b) && b));

        switch (request.Command.ToLowerInvariant())
        {
            case MobileCommandCore.StateGet:
                return MobileCommandResult.Success(requestId,
                    JsonSerializer.SerializeToElement(_snapshots.Build(device)));

            case MobileCommandCore.PreviewGet:
            {
                var fps = (int)Math.Round(double.IsNaN(Num("fps")) ? 1 : Num("fps"));
                var (frame, code) = await _preview.GetFrameAsync(fps, ct).ConfigureAwait(false);
                if (frame is null) return Fail(requestId, code.Length > 0 ? code : FeatureErrors.MobPreviewFailed);
                return MobileCommandResult.Success(requestId, JsonSerializer.SerializeToElement(new { frame = Convert.ToBase64String(frame) }));
            }

            case MobileCommandCore.SceneSet:
            {
                var r = await _obs.SetSceneAsync(Str("scene")).ConfigureAwait(false);
                return From(r, requestId);
            }

            case MobileCommandCore.SourceToggle:
            {
                var r = await _obs.SetSceneItemEnabledAsync(_obs.CurrentScene, (int)Num("sceneItemId"), Bool("enabled")).ConfigureAwait(false);
                return From(r, requestId);
            }

            case MobileCommandCore.AudioMute:
            {
                var r = await _obs.SetMuteAsync(Str("input"), Bool("muted")).ConfigureAwait(false);
                return From(r, requestId);
            }

            case MobileCommandCore.AudioVolume:
            {
                var r = await _obs.SetVolumeDbAsync(Str("input"), MobileCommandCore.ClampVolume(Num("volumeDb"))).ConfigureAwait(false);
                return From(r, requestId);
            }

            case MobileCommandCore.RecordStart:
                return From(await _obs.StartRecordAsync().ConfigureAwait(false), requestId);
            case MobileCommandCore.RecordStop:
                return From(await _obs.StopRecordAsync().ConfigureAwait(false), requestId);
            case MobileCommandCore.RecordToggle:
                return From(await _obs.ToggleRecordAsync().ConfigureAwait(false), requestId);
            case MobileCommandCore.ReplaySave:
                return From(await _obs.SaveReplayBufferAsync().ConfigureAwait(false), requestId);
            case MobileCommandCore.VirtualCamToggle:
                return From(await _obs.ToggleVirtualCamAsync().ConfigureAwait(false), requestId);
            case MobileCommandCore.StudioToggle:
                return From(await _obs.ToggleStudioModeAsync().ConfigureAwait(false), requestId);
            case MobileCommandCore.StreamStart:
                return From(await _obs.StartStreamAsync().ConfigureAwait(false), requestId);
            case MobileCommandCore.StreamStop:
                return From(await _obs.StopStreamAsync().ConfigureAwait(false), requestId);

            case MobileCommandCore.TeleprompterPlay:
                _prompter.SetPlaying(true);
                return MobileCommandResult.Success(requestId);
            case MobileCommandCore.TeleprompterPause:
                _prompter.SetPlaying(false);
                return MobileCommandResult.Success(requestId);
            case MobileCommandCore.TeleprompterRestart:
                _prompter.ScrollToStart();
                return MobileCommandResult.Success(requestId);
            case MobileCommandCore.TeleprompterSpeed:
            {
                var settings = _prompter.Settings.Clone();
                settings.Speed = Math.Clamp(Num("speed"), 1, 200);
                _prompter.SaveSettings(settings);
                return MobileCommandResult.Success(requestId);
            }
            case MobileCommandCore.TeleprompterFont:
            {
                var settings = _prompter.Settings.Clone();
                settings.FontSize = Math.Clamp(Num("fontSize"), 12, 160);
                _prompter.SaveSettings(settings);
                return MobileCommandResult.Success(requestId);
            }
            case MobileCommandCore.TeleprompterMirror:
            {
                var settings = _prompter.Settings.Clone();
                settings.MirrorHorizontal = Bool("mirror");
                _prompter.SaveSettings(settings);
                return MobileCommandResult.Success(requestId);
            }
            case MobileCommandCore.TeleprompterOffset:
            {
                var settings = _prompter.Settings.Clone();
                settings.Offset = Math.Max(0, Num("offset"));
                _prompter.SaveSettings(settings);
                return MobileCommandResult.Success(requestId);
            }

            case MobileCommandCore.DeviceRevoke:
            {
                var target = Str("deviceId");
                if (!_sessions.Revoke(target)) return Fail(requestId, FeatureErrors.MobCommandInvalid);
                _revokeCallback?.Invoke(target);
                return MobileCommandResult.Success(requestId);
            }

            case MobileCommandCore.AppPanic:
            {
                foreach (var input in _obs.AudioInputs.Where(i => i.IsAudio && !i.Muted))
                    await _obs.SetMuteAsync(input.Name, true).ConfigureAwait(false);
                var brb = _obs.Scenes.FirstOrDefault(s =>
                    s.Name.Contains("BRB", StringComparison.OrdinalIgnoreCase) ||
                    s.Name.Contains("休息", StringComparison.Ordinal));
                if (brb is not null) await _obs.SetSceneAsync(brb.Name).ConfigureAwait(false);
                if (Bool("stopStream")) await _obs.StopStreamAsync().ConfigureAwait(false);
                return MobileCommandResult.Success(requestId);
            }

            default:
                return Fail(requestId, FeatureErrors.MobCommandUnknown);
        }
    }

    private static MobileCommandResult From(ObsRequestResult result, string requestId)
        => result.Ok
            ? MobileCommandResult.Success(requestId)
            : MobileCommandResult.Fail(
                requestId,
                FeatureErrors.MobObsRequestFailed,
                string.IsNullOrWhiteSpace(result.Comment)
                    ? Strings.T("console.opErrorCode", result.Code)
                    : result.Comment!);

    private static MobileCommandResult Fail(string requestId, string code)
        => MobileCommandResult.Fail(requestId, code, FeatureErrors.Describe(code));
}

/// <summary>
/// 本地媒体服务器（M03/M04/M09）：mediamtx 单实例、多路随机 key、OBS 媒体源与布局。
/// Win7 明确禁用；未安装 sidecar 时返回 <c>CAM_SIDECAR_MISSING</c>，其余功能不受影响。
/// </summary>
public sealed class MediaServerManager : IDisposable
{
    private readonly SidecarManager _sidecars;
    private readonly ObsConnectionService _obs;
    private readonly Func<Version> _osVersion;
    private readonly object _gate = new();
    private readonly Dictionary<string, PhoneCameraSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private string? _configPath;
    private int _rtspPort, _rtmpPort, _srtPort, _apiPort;

    public MediaServerManager(SidecarManager sidecars, ObsConnectionService obs, Func<Version>? osVersion = null)
    {
        _sidecars = sidecars;
        _obs = obs;
        _osVersion = osVersion ?? (() => OsSupport.Current);
    }

    public PhoneCameraSession? Session
    {
        get { lock (_gate) return _sessions.Values.OrderBy(s => s.CameraId, StringComparer.Ordinal).FirstOrDefault(); }
    }

    public IReadOnlyList<PhoneCameraSession> Sessions
    {
        get { lock (_gate) return _sessions.Values.OrderBy(s => s.CameraId, StringComparer.Ordinal).ToList(); }
    }

    public bool IsRunning => Sessions.Count > 0;
    public bool IsSupported => OsSupport.IsWindows10OrNewer(_osVersion());

    public (bool Ok, string Code) ValidateAdd(string? cameraId)
    {
        lock (_gate)
        {
            if (!MultiCamCore.CanAdd(_sessions.Values.Select(s => new MultiCamCore.Slot
                {
                    CameraId = s.CameraId, SceneName = s.SceneName, Enabled = true,
                    BitrateKbps = s.Quality.BitrateKbps,
                }).ToList()))
                return (false, FeatureErrors.MultiLimit);
            return (true, "");
        }
    }

    private string NextCameraId()
    {
        for (var i = 1; i <= MultiCamCore.MaxCameras; i++)
        {
            var id = "cam" + i;
            if (!_sessions.ContainsKey(id)) return id;
        }
        return "cam" + (MultiCamCore.MaxCameras + 1);
    }

    /// <summary>启动/复用媒体服务器，并新增一路手机机位。</summary>
    public async Task<(bool Ok, string Code, PhoneCameraSession? Session)> StartAsync(
        string lanAddress, PhoneCameraQuality quality, PhoneCameraLayout layout, bool withAudio,
        string? cameraId = null, CancellationToken ct = default)
    {
        if (!IsSupported) return (false, FeatureErrors.CamUnsupportedWin7, null);
        if (!LanPolicy.IsPrivateAddress(lanAddress)) return (false, FeatureErrors.CamNoPrivateIp, null);
        var entry = _sidecars.Manifest.Find("mediamtx");
        if (entry is null || !_sidecars.IsInstalled(entry)) return (false, FeatureErrors.CamSidecarMissing, null);

        var validated = ValidateAdd(cameraId);
        if (!validated.Ok) return (false, validated.Code, null);

        bool startedNow = false;
        if (Sessions.Count == 0)
        {
            try
            {
                _rtspPort = FreePort();
                _rtmpPort = FreePort();
                _srtPort = FreePort();
                _apiPort = FreePort();
            }
            catch (SocketException) { return (false, FeatureErrors.CamPortInUse, null); }

            var dir = Path.Combine(_sidecars.Root, "mediamtx", entry.Version, "runtime");
            Directory.CreateDirectory(dir);
            var config = Path.Combine(dir, "mediamtx.yml");
            await File.WriteAllTextAsync(config, PhoneCameraCore.BuildConfig(_rtspPort, _rtmpPort, _srtPort, _apiPort), ct).ConfigureAwait(false);
            if (!_sidecars.Start(entry, new[] { config })) return (false, FeatureErrors.CamSidecarStartFailed, null);
            _configPath = config;
            startedNow = true;
        }

        var id = string.IsNullOrWhiteSpace(cameraId) ? NextCameraId() : cameraId!.Trim();
        var session = new PhoneCameraSession
        {
            CameraId = id,
            StreamKey = LanPolicy.NewToken(16),
            SrtPort = _srtPort,
            RtmpPort = _rtmpPort,
            RtspPort = _rtspPort,
            LanAddress = lanAddress,
            Quality = quality,
            Layout = layout,
            WithAudio = withAudio,
            SceneName = "手机机位",
            SourceName = "手机摄像头 " + id,
            StartedUtc = DateTimeOffset.UtcNow,
        };

        var prepared = await EnsureObsMediaSourceAsync(session, ct).ConfigureAwait(false);
        if (!prepared)
        {
            if (startedNow) StopAll();
            return (false, FeatureErrors.CamSidecarStartFailed, null);
        }
        await ApplyLayoutAsync(session, layout, ct).ConfigureAwait(false);
        lock (_gate) _sessions[id] = session;
        return (true, "", session);
    }

    public void StopCamera(string cameraId)
    {
        PhoneCameraSession? session;
        lock (_gate)
        {
            if (!_sessions.Remove(cameraId, out session)) return;
        }
        _ = _obs.RawRequestAsync("RemoveInput", new Dictionary<string, object?> { ["inputName"] = session.SourceName });
        if (Sessions.Count == 0) StopAll();
    }

    public async Task<(bool Ok, string Code)> SetLayoutAsync(string cameraId, PhoneCameraLayout layout)
    {
        PhoneCameraSession? session;
        lock (_gate) _sessions.TryGetValue(cameraId, out session);
        if (session is null) return (false, FeatureErrors.MultiSourceFailed);
        session.Layout = layout;
        return await ApplyLayoutAsync(session, layout, CancellationToken.None).ConfigureAwait(false)
            ? (true, "") : (false, FeatureErrors.MultiSourceFailed);
    }

    public void StopAll()
    {
        string? config;
        int count;
        lock (_gate)
        {
            count = _sessions.Count;
            _sessions.Clear();
            config = _configPath;
            _configPath = null;
        }
        var entry = _sidecars.Manifest.Find("mediamtx");
        if (count > 0 && entry is not null) _sidecars.Stop(entry);
        try { if (config is not null) File.Delete(config); } catch { }
    }

    private async Task<bool> EnsureObsMediaSourceAsync(PhoneCameraSession session, CancellationToken ct)
    {
        if (!_obs.IsConnected) return false;
        if (!_obs.Scenes.Any(s => s.Name == session.SceneName))
        {
            var created = await _obs.RawRequestAsync("CreateScene",
                new Dictionary<string, object?> { ["sceneName"] = session.SceneName }, ct).ConfigureAwait(false);
            if (!created.Ok) return false;
            await _obs.RefreshScenesAsync().ConfigureAwait(false);
        }

        var url = PhoneCameraCore.RtspLoopbackUrl(session.RtspPort, session.StreamKey);
        var existing = await _obs.RawRequestAsync("GetInputSettings",
            new Dictionary<string, object?> { ["inputName"] = session.SourceName }, ct).ConfigureAwait(false);
        if (existing.Ok)
        {
            await _obs.RawRequestAsync("SetInputSettings", new Dictionary<string, object?>
            {
                ["inputName"] = session.SourceName,
                ["inputSettings"] = new Dictionary<string, object?> { ["input"] = url },
                ["overlay"] = true,
            }, ct).ConfigureAwait(false);
        }
        else
        {
            var created = await _obs.RawRequestAsync("CreateInput", new Dictionary<string, object?>
            {
                ["sceneName"] = session.SceneName,
                ["inputName"] = session.SourceName,
                ["inputKind"] = "ffmpeg_source",
                ["inputSettings"] = new Dictionary<string, object?>
                {
                    ["is_local_file"] = false,
                    ["input"] = url,
                    ["reconnect_delay_sec"] = 2,
                    ["close_when_inactive"] = false,
                    ["buffering_mb"] = 2,
                    ["hw_decode"] = false,
                },
                ["sceneItemEnabled"] = true,
            }, ct).ConfigureAwait(false);
            if (!created.Ok) return false;
        }

        var item = await _obs.RawRequestAsync("GetSceneItemId", new Dictionary<string, object?>
        {
            ["sceneName"] = session.SceneName,
            ["sourceName"] = session.SourceName,
        }, ct).ConfigureAwait(false);
        if (item.Ok && item.Data is { } data && data.TryGetProperty("sceneItemId", out var id) && id.TryGetInt32(out var sceneItemId))
            session.SceneItemId = sceneItemId;
        await _obs.RefreshScenesAsync().ConfigureAwait(false);
        return true;
    }

    private async Task<bool> ApplyLayoutAsync(PhoneCameraSession session, PhoneCameraLayout layout, CancellationToken ct)
    {
        if (session.SceneItemId <= 0 || !_obs.IsConnected) return true;
        var settings = await _obs.RawRequestAsync("GetVideoSettings", null, ct).ConfigureAwait(false);
        var width = 1920;
        var height = 1080;
        if (settings.Ok && settings.Data is { } data)
        {
            if (data.TryGetProperty("baseWidth", out var w) && w.TryGetInt32(out var bw)) width = bw;
            if (data.TryGetProperty("baseHeight", out var h) && h.TryGetInt32(out var bh)) height = bh;
        }
        var index = Sessions.ToList().FindIndex(s => s.CameraId == session.CameraId);
        if (index < 0) index = 0;
        var (x, y, w2, h2) = MultiCamCore.SlotBounds(
            layout == PhoneCameraLayout.PipBottomRight ? MultiCamCore.Layout.Pip : MultiCamCore.Layout.Grid2x2,
            index, width, height);
        var result = await _obs.RawRequestAsync("SetSceneItemTransform", new Dictionary<string, object?>
        {
            ["sceneName"] = session.SceneName,
            ["sceneItemId"] = session.SceneItemId,
            ["sceneItemTransform"] = new Dictionary<string, object?>
            {
                ["positionX"] = x,
                ["positionY"] = y,
                ["boundsType"] = "OBS_BOUNDS_SCALE_INNER",
                ["boundsWidth"] = w2,
                ["boundsHeight"] = h2,
                ["boundsAlignment"] = 0,
            },
        }, ct).ConfigureAwait(false);
        return result.Ok;
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public void Dispose() => StopAll();
}

/// <summary>
/// 手机通知/剪贴板服务（M11）：仅 Android + ADB、每次显式授权、不持久化、可一键关闭。
/// 实际读取依赖设备是否允许 <c>adb shell cmd clipboard</c>；不支持时明确返回原因，不静默假装成功。
/// </summary>
public sealed class MobileNotifyService
{
    private readonly UsbCameraGuide _guide;
    private readonly Func<DateTimeOffset> _clock;
    private DateTimeOffset? _authorizedUntil;

    public MobileNotifyService(UsbCameraGuide? guide = null, Func<DateTimeOffset>? clock = null)
    {
        _guide = guide ?? new UsbCameraGuide();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>总开关，默认关闭；关闭会立即撤销授权。</summary>
    public bool Enabled { get; private set; }

    public bool Authorized => Enabled && _authorizedUntil is { } until && _clock() < until;

    public void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        if (!enabled) _authorizedUntil = null;
    }

    /// <summary>单次授权（默认 10 分钟），过期自动失效。</summary>
    public void Authorize(TimeSpan? duration = null)
        => _authorizedUntil = _clock().Add(duration ?? TimeSpan.FromMinutes(10));

    /// <summary>读取一次剪贴板文本；失败返回错误码（ASR_AUDIO_DEVICE 不合适，这里用 MOB_COMMAND_INVALID）。</summary>
    public async Task<(bool Ok, string Text, string Code)> ReadClipboardOnceAsync(CancellationToken ct = default)
    {
        if (!Enabled) return (false, "", FeatureErrors.MobDisabled);
        if (!Authorized) return (false, "", FeatureErrors.MobRoleDenied);
        if (!_guide.AdbFound) return (false, "", FeatureErrors.MobCommandInvalid);
        try
        {
            var info = new ProcessStartInfo(_guide.AdbPath!)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            info.ArgumentList.Add("shell");
            info.ArgumentList.Add("cmd");
            info.ArgumentList.Add("clipboard");
            info.ArgumentList.Add("get");
            using var process = Process.Start(info);
            if (process is null) return (false, "", FeatureErrors.MobCommandInvalid);
            var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            if (process.ExitCode != 0) return (false, "", FeatureErrors.MobCommandInvalid);
            return (true, output.Trim(), "");
        }
        catch (Exception)
        {
            return (false, "", FeatureErrors.MobCommandInvalid);
        }
    }
}
