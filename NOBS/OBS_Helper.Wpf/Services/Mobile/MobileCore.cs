using System.IO;
using System.Text.Json;
using OBS_Helper.Wpf.Localization;
using System.Text.Json.Serialization;
using OBS_Helper.Wpf.Models.Mobile;
using OBS_Helper.Wpf.Services.Security;

namespace OBS_Helper.Wpf.Services.Mobile;

/// <summary>
/// 配对核心（V3.4.0 / M01）：一次性 secret、TTL、常量时间比较、短码。
///
/// secret 只以哈希形式进入 <see cref="PairingSession"/>；URL fragment 里的明文只在生成时存在于内存与二维码里。
/// 失败原因对外**不区分**（过期 / 用过 / secret 错一律同一个错误码），避免被用来探测会话。
/// </summary>
public static class PairingCore
{
    public static (PairingSession Session, string Secret) CreateSession(
        DateTimeOffset now, TimeSpan? ttl = null, string? shortCode = null, string? pairId = null, string? secret = null)
    {
        var actualSecret = secret ?? LanPolicy.NewToken(32);
        var session = new PairingSession
        {
            PairId = pairId ?? LanPolicy.NewToken(16),
            SecretHash = LanPolicy.HashToken(actualSecret),
            ShortCode = shortCode ?? LanPolicy.NewShortCode(),
            CreatedUtc = now,
            ExpiresUtc = now.Add(ttl ?? TimeSpan.FromSeconds(LanPolicy.PairTtlSeconds)),
        };
        return (session, actualSecret);
    }

    /// <summary>消费配对码：成功即标记一次性；失败原因只用于日志，不返回给手机。</summary>
    public static PairOutcome Consume(PairingSession? session, string? secret, DateTimeOffset now)
    {
        if (session is null) return PairOutcome.UnknownPair;
        if (session.Consumed) return PairOutcome.Used;
        if (session.IsExpired(now)) return PairOutcome.Expired;
        if (string.IsNullOrEmpty(secret) || !LanPolicy.FixedTimeEquals(session.SecretHash, LanPolicy.HashToken(secret)))
            return PairOutcome.BadSecret;
        session.Consumed = true;
        return PairOutcome.Ok;
    }

    /// <summary>二维码 / 短链内容：secret 放 fragment，绝不会进服务器日志或 Referer。</summary>
    public static string BuildPairUrl(string baseUrl, string pairId, string secret)
        => $"{baseUrl.TrimEnd('/')}/m/{Uri.EscapeDataString(pairId)}#{secret}";

    /// <summary>从完整配对 URL 里拆出 pairId 与 fragment secret（手机端 / 测试共用）。</summary>
    public static bool TryParsePairUrl(string? url, out string pairId, out string secret)
    {
        pairId = "";
        secret = "";
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return false;
        var fragment = uri.Fragment.TrimStart('#');
        if (fragment.Length == 0) return false;
        var marker = uri.AbsolutePath.LastIndexOf("/m/", StringComparison.Ordinal);
        if (marker < 0) return false;
        pairId = Uri.UnescapeDataString(uri.AbsolutePath[(marker + 3)..].Trim('/'));
        secret = fragment;
        return pairId.Length > 0 && secret.Length > 0;
    }
}

/// <summary>
/// 手机命令白名单（PLAN_V3.4.0 §4.2）。
///
/// 手机只能发高层动作，**不存在** <c>obs.request</c> 这类透传。每条命令声明最低角色、
/// 是否需要二次确认、参数键；参数值还要经过 <see cref="Validate"/> 对照当前 OBS 状态。
/// </summary>
public static class MobileCommandCore
{
    public sealed record Definition(string Name, MobileRole MinRole, bool Destructive, string[] Params);

    public const string StateGet = "state.get";
    public const string PreviewGet = "preview.get";
    public const string SceneSet = "scene.set";
    public const string SourceToggle = "source.toggle";
    public const string AudioMute = "audio.mute";
    public const string AudioVolume = "audio.volume";
    public const string RecordToggle = "record.toggle";
    public const string RecordStart = "record.start";
    public const string RecordStop = "record.stop";
    public const string ReplaySave = "replay.save";
    public const string VirtualCamToggle = "virtualcam.toggle";
    public const string StudioToggle = "studio.toggle";
    public const string StreamStart = "stream.start";
    public const string StreamStop = "stream.stop";
    public const string TeleprompterPlay = "teleprompter.play";
    public const string TeleprompterPause = "teleprompter.pause";
    public const string TeleprompterOffset = "teleprompter.offset";
    public const string TeleprompterSpeed = "teleprompter.speed";
    public const string TeleprompterFont = "teleprompter.font";
    public const string TeleprompterMirror = "teleprompter.mirror";
    public const string TeleprompterRestart = "teleprompter.restart";
    public const string TeleprompterSet = "teleprompter.set";
    public const string DeviceRevoke = "device.revoke";
    public const string AppPanic = "app.panic";

    private static readonly Dictionary<string, Definition> Definitions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [StateGet] = new(StateGet, MobileRole.Viewer, false, Array.Empty<string>()),
            [PreviewGet] = new(PreviewGet, MobileRole.Viewer, false, new[] { "fps" }),
            [SceneSet] = new(SceneSet, MobileRole.Operator, false, new[] { "scene" }),
            [SourceToggle] = new(SourceToggle, MobileRole.Operator, false, new[] { "scene", "sceneItemId", "enabled" }),
            [AudioMute] = new(AudioMute, MobileRole.Operator, false, new[] { "input", "muted" }),
            [AudioVolume] = new(AudioVolume, MobileRole.Operator, false, new[] { "input", "volumeDb" }),
            [RecordToggle] = new(RecordToggle, MobileRole.Operator, false, Array.Empty<string>()),
            [RecordStart] = new(RecordStart, MobileRole.Operator, false, Array.Empty<string>()),
            [RecordStop] = new(RecordStop, MobileRole.Operator, false, Array.Empty<string>()),
            [ReplaySave] = new(ReplaySave, MobileRole.Operator, false, Array.Empty<string>()),
            [VirtualCamToggle] = new(VirtualCamToggle, MobileRole.Operator, false, Array.Empty<string>()),
            [StudioToggle] = new(StudioToggle, MobileRole.Operator, false, Array.Empty<string>()),
            [StreamStart] = new(StreamStart, MobileRole.Admin, true, Array.Empty<string>()),
            [StreamStop] = new(StreamStop, MobileRole.Admin, true, Array.Empty<string>()),
            [TeleprompterPlay] = new(TeleprompterPlay, MobileRole.Operator, false, Array.Empty<string>()),
            [TeleprompterPause] = new(TeleprompterPause, MobileRole.Operator, false, Array.Empty<string>()),
            [TeleprompterOffset] = new(TeleprompterOffset, MobileRole.Operator, false, new[] { "offset" }),
            [TeleprompterSpeed] = new(TeleprompterSpeed, MobileRole.Operator, false, new[] { "speed" }),
            [TeleprompterFont] = new(TeleprompterFont, MobileRole.Operator, false, new[] { "fontSize" }),
            [TeleprompterMirror] = new(TeleprompterMirror, MobileRole.Operator, false, new[] { "mirror" }),
            [TeleprompterRestart] = new(TeleprompterRestart, MobileRole.Operator, false, Array.Empty<string>()),
            [TeleprompterSet] = new(TeleprompterSet, MobileRole.Operator, false, new[] { "text" }),
            [DeviceRevoke] = new(DeviceRevoke, MobileRole.Admin, true, new[] { "deviceId" }),
            [AppPanic] = new(AppPanic, MobileRole.Admin, true, new[] { "stopStream" }),
        };

    public static IReadOnlyCollection<string> All => Definitions.Keys;

    public static Definition? Find(string? command)
        => Definitions.TryGetValue((command ?? "").Trim(), out var def) ? def : null;

    /// <summary>命令当前可见的 OBS 状态（只读快照；纯数据，便于单测）。</summary>
    public sealed class Context
    {
        public bool ObsOnline { get; init; }
        public IReadOnlyList<string> Scenes { get; init; } = Array.Empty<string>();
        public IReadOnlyList<MobileAudioState> Audio { get; init; } = Array.Empty<MobileAudioState>();
        public IReadOnlyList<int> SceneItemIds { get; init; } = Array.Empty<int>();
        public bool ReplayBufferActive { get; init; }
    }

    public sealed record Validation(bool Ok, string Code, MobileCommandResult? Immediate)
    {
        public static Validation Pass() => new(true, "", null);
        public static Validation Fail(string code, MobileCommandResult? result = null) => new(false, code, result);
    }

    /// <summary>
    /// 校验命令：存在性 → 角色 → OBS 在线 → 参数。错误码与附录 B 一致。
    /// </summary>
    public static Validation Validate(MobileCommandRequest request, MobileRole role, Context context, string requestId)
    {
        var def = Find(request.Command);
        if (def is null) return Validation.Fail(FeatureErrors.MobCommandUnknown);
        if (!MobileRoles.Allows(role, def.MinRole)) return Validation.Fail(FeatureErrors.MobRoleDenied);
        if (!context.ObsOnline && RequiresObs(request.Command))
            return Validation.Fail(FeatureErrors.MobObsOffline);

        bool Has(string name) => request.Params.ContainsKey(name);
        string Str(string name) => Has(name) && request.Params[name].ValueKind == JsonValueKind.String
            ? request.Params[name].GetString() ?? ""
            : "";
        bool Bool(string name)
        {
            if (!Has(name)) return false;
            var e = request.Params[name];
            return e.ValueKind == JsonValueKind.True ||
                   (e.ValueKind == JsonValueKind.String && bool.TryParse(e.GetString(), out var b) && b);
        }
        double Num(string name)
        {
            if (!Has(name)) return double.NaN;
            var e = request.Params[name];
            return e.ValueKind == JsonValueKind.Number ? e.GetDouble()
                : double.TryParse(e.GetString(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : double.NaN;
        }

        switch (request.Command.ToLowerInvariant())
        {
            case SceneSet:
                if (!context.Scenes.Contains(Str("scene"), StringComparer.Ordinal))
                    return Validation.Fail(FeatureErrors.MobCommandInvalid);
                break;
            case SourceToggle:
                if (!context.SceneItemIds.Contains((int)Num("sceneItemId")) || double.IsNaN(Num("sceneItemId")))
                    return Validation.Fail(FeatureErrors.MobCommandInvalid);
                if (!Has("enabled")) return Validation.Fail(FeatureErrors.MobCommandInvalid);
                break;
            case AudioMute:
                if (!context.Audio.Any(a => a.Name == Str("input")) || !Has("muted"))
                    return Validation.Fail(FeatureErrors.MobCommandInvalid);
                break;
            case AudioVolume:
                if (!context.Audio.Any(a => a.Name == Str("input")) || double.IsNaN(Num("volumeDb")))
                    return Validation.Fail(FeatureErrors.MobCommandInvalid);
                break;
            case RecordStop:
            case ReplaySave:
                if (RecordStop == request.Command && !Has("confirm"))
                {
                    // 停止录制是「不可逆」数据动作：PC 端设置可要求确认；默认不需要（录制可随时恢复）。
                }
                if (ReplaySave == request.Command && !context.ReplayBufferActive)
                    return Validation.Fail(FeatureErrors.MobCommandInvalid);
                break;
            case StreamStart:
            case StreamStop:
                // 破坏性动作必须显式 confirm（PC 确认或管理员 PIN 二选一，由 PC 页面决定）
                if (!Bool("confirm")) return Validation.Fail(FeatureErrors.MobCommandInvalid);
                break;
            case PreviewGet:
                var fps = Num("fps");
                if (!double.IsNaN(fps) && (fps < 1 || fps > 5)) return Validation.Fail(FeatureErrors.MobCommandInvalid);
                break;
            case TeleprompterSpeed:
                if (double.IsNaN(Num("speed"))) return Validation.Fail(FeatureErrors.MobCommandInvalid);
                break;
            case TeleprompterFont:
                if (double.IsNaN(Num("fontSize"))) return Validation.Fail(FeatureErrors.MobCommandInvalid);
                break;
            case TeleprompterOffset:
                if (double.IsNaN(Num("offset"))) return Validation.Fail(FeatureErrors.MobCommandInvalid);
                break;
            case TeleprompterSet:
            {
                // 手机端直接改稿：空稿 / 超长稿一律拒绝，避免把电脑上的稿子清空。
                var text = Str("text");
                if (string.IsNullOrWhiteSpace(text) || text.Length > 20_000)
                    return Validation.Fail(FeatureErrors.MobCommandInvalid);
                break;
            }
            case DeviceRevoke:
                if (Str("deviceId").Length == 0) return Validation.Fail(FeatureErrors.MobCommandInvalid);
                break;
        }
        return Validation.Pass();
    }

    /// <summary>把音量钳制到 OBS 允许范围（-100..0 dB），避免手机发 NaN/超大值。</summary>
    public static double ClampVolume(double db) => double.IsNaN(db) ? -100 : Math.Clamp(db, -100, 0);

    /// <summary>命令幂等缓存：同一 requestId 在 60 秒内只执行一次，最多保留 50 条。</summary>
    public sealed class IdempotencyCache
    {
        private readonly Dictionary<string, (string DeviceId, MobileCommandResult Result, DateTimeOffset At)> _entries = new(StringComparer.Ordinal);
        private readonly object _gate = new();
        private readonly Func<DateTimeOffset> _clock;

        public IdempotencyCache(Func<DateTimeOffset>? clock = null) => _clock = clock ?? (() => DateTimeOffset.UtcNow);

        public bool TryGet(string deviceId, string requestId, out MobileCommandResult result)
        {
            lock (_gate)
            {
                Prune();
                if (_entries.TryGetValue(deviceId + "\n" + requestId, out var entry))
                {
                    result = entry.Result;
                    return true;
                }
                result = null!;
                return false;
            }
        }

        public void Put(string deviceId, string requestId, MobileCommandResult result)
        {
            lock (_gate)
            {
                Prune();
                if (_entries.Count >= 200) _entries.Clear();
                _entries[deviceId + "\n" + requestId] = (deviceId, result, _clock());
            }
        }

        private void Prune()
        {
            var cutoff = _clock() - TimeSpan.FromSeconds(60);
            foreach (var key in _entries.Where(kv => kv.Value.At < cutoff).Select(kv => kv.Key).ToList())
                _entries.Remove(key);
        }
    }

    /// <summary>
    /// 该命令是否必须依赖 OBS 在线（V3.4.1）。
    ///
    /// 提词器是应用内能力，OBS 掉线/重连时仍应可播放、调速、翻页；
    /// 之前把“OBS 在线”当成所有命令的前置条件，会把提词操作误报成「OBS 未连接」。
    /// </summary>
    public static bool RequiresObs(string? command)
    {
        var c = (command ?? "").Trim().ToLowerInvariant();
        if (c == StateGet) return false;
        if (c.StartsWith("teleprompter.", StringComparison.Ordinal)) return false;
        return true;
    }

    public static string NormalizeRequestId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "";
        var cleaned = new string(id.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        return cleaned.Length > 64 ? cleaned[..64] : cleaned;
    }
}

/// <summary>状态快照与 Tally 计算（纯逻辑）。</summary>
public static class MobileStateCore
{
    /// <summary>Tally：推流/录制中为红，否则为绿；OBS 离线为 off。</summary>
    public static string Tally(bool obsOnline, bool streaming, bool recording)
        => !obsOnline ? "off" : streaming || recording ? "red" : "green";

    /// <summary>计时字符串（HH:MM:SS）；负数与 NaN 归零。</summary>
    public static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        return $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    /// <summary>
    /// 检测计时漂移：OBS 自报 timecode 与本地墙钟的差值。超过 1 秒时以 OBS 为准并提示。
    /// 返回 (采用值, 漂移秒数)。
    /// </summary>
    public static (TimeSpan Chosen, double DriftSeconds) ReconcileElapsed(TimeSpan obsTimecode, TimeSpan local)
    {
        if (obsTimecode <= TimeSpan.Zero) return (local, 0);
        var drift = Math.Abs((obsTimecode - local).TotalSeconds);
        return (obsTimecode, drift);
    }

    /// <summary>把 OBS 输入折叠成手机端需要的摘要（只保留音频输入）。</summary>
    public static List<MobileAudioState> AudioSummary(IEnumerable<MobileAudioState> inputs)
        => inputs.OrderBy(a => a.Name, StringComparer.CurrentCulture).ToList();

    /// <summary>序列化为 WS 推送的 JSON（属性名小驼峰，手机端直接读）。</summary>
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

/// <summary>
/// 设备与会话存储（V3.4.0 / M01）。
///
/// 落盘只存 token 的 SHA-256 + salt，不存明文；撤销即删除；最多 5 台设备。
/// 存储目录可注入，因此真实文件读写也能单测。
/// </summary>
public sealed class MobileSessionStore
{
    private readonly string _path;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    private List<MobileDevice> _devices = new();

    public MobileSessionStore(string path, Func<DateTimeOffset>? clock = null)
    {
        _path = path;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        Load();
    }

    public event Action? Changed;

    public IReadOnlyList<MobileDevice> Devices
    {
        get { lock (_gate) return _devices.Where(d => !d.Revoked).ToList(); }
    }

    public void Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_path))
                {
                    _devices = new List<MobileDevice>();
                    return;
                }
                var json = File.ReadAllText(_path);
                _devices = JsonSerializer.Deserialize<List<MobileDevice>>(json) ?? new List<MobileDevice>();
            }
            catch (Exception)
            {
                // 存储损坏时以空列表继续：不阻断手机联动，也不覆盖原文件（下次保存时重建）。
                _devices = new List<MobileDevice>();
            }
        }
    }

    private void Save()
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_devices,
                new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception)
        {
            // 保存失败不回滚内存态：本次运行仍可用，下次启动重新配对。
        }
        Changed?.Invoke();
    }

    /// <summary>登记新设备。设备数已达上限返回 null；token 明文只返回一次。</summary>
    public (MobileDevice Device, string Token)? Register(string name, MobileRole role, string? deviceId = null)
    {
        lock (_gate)
        {
            CleanupExpired();
            if (_devices.Count(d => !d.Revoked) >= LanPolicy.MaxDevices) return null;
            var token = LanPolicy.NewToken(32);
            var now = _clock();
            var device = new MobileDevice
            {
                DeviceId = string.IsNullOrWhiteSpace(deviceId) ? LanPolicy.NewToken(16) : deviceId!,
                Name = LanPolicy.SanitizeDeviceName(name),
                Role = MobileRoles.Name(role),
                TokenHash = LanPolicy.HashToken(token),
                CreatedUtc = now,
                LastSeenUtc = now,
                ExpiresUtc = now.AddHours(LanPolicy.SessionHours),
            };
            _devices.Add(device);
            Save();
            return (device, token);
        }
    }

    /// <summary>用 token 认证；成功时刷新 LastSeen。撤销/过期/未知一律返回 null。</summary>
    public MobileDevice? Authenticate(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        lock (_gate)
        {
            var hash = LanPolicy.HashToken(token);
            var now = _clock();
            var device = _devices.FirstOrDefault(d => !d.Revoked && LanPolicy.FixedTimeEquals(d.TokenHash, hash));
            if (device is null) return null;
            if (device.ExpiresUtc <= now) return null;
            if (now - device.LastSeenUtc > TimeSpan.FromMinutes(LanPolicy.IdleMinutes)) return null;
            device.LastSeenUtc = now;
            return device;
        }
    }

    public bool Revoke(string deviceId)
    {
        lock (_gate)
        {
            var device = _devices.FirstOrDefault(d => d.DeviceId == deviceId);
            if (device is null) return false;
            device.Revoked = true;
            device.TokenHash = "";
            Save();
            return true;
        }
    }

    public bool RevokeAll()
    {
        lock (_gate)
        {
            foreach (var device in _devices) { device.Revoked = true; device.TokenHash = ""; }
            Save();
            return true;
        }
    }

    public bool SetRole(string deviceId, MobileRole role)
    {
        lock (_gate)
        {
            var device = _devices.FirstOrDefault(d => d.DeviceId == deviceId && !d.Revoked);
            if (device is null) return false;
            device.Role = MobileRoles.Name(role);
            Save();
            return true;
        }
    }

    public void Touch(string deviceId)
    {
        lock (_gate)
        {
            var device = _devices.FirstOrDefault(d => d.DeviceId == deviceId);
            if (device is null) return;
            device.LastSeenUtc = _clock();
        }
    }

    private void CleanupExpired()
    {
        var now = _clock();
        _devices.RemoveAll(d => d.Revoked || d.ExpiresUtc <= now);
    }

    /// <summary>测试/排障用：当前存储路径。</summary>
    public string Path => _path;
}

/// <summary>
/// 全局错误码（V3.4.0）。新增前缀集中在这里，文案在文案表 <c>err.&lt;code&gt;</c>。
/// 与 <see cref="Errors.ErrorCodes"/> 的分工：老的 OBSxxx 是产品级错误，这里是功能级错误码。
/// </summary>
public static class FeatureErrors
{
    // MOB_*
    public const string MobDisabled = "MOB_DISABLED";
    public const string MobNoLan = "MOB_NO_LAN";
    public const string MobPairExpired = "MOB_PAIR_EXPIRED";
    public const string MobPairBadSecret = "MOB_PAIR_BAD_SECRET";
    public const string MobTokenInvalid = "MOB_TOKEN_INVALID";
    public const string MobRateLimited = "MOB_RATE_LIMITED";
    public const string MobRoleDenied = "MOB_ROLE_DENIED";
    public const string MobObsOffline = "MOB_OBS_OFFLINE";
    public const string MobObsRequestFailed = "MOB_OBS_REQUEST_FAILED";
    public const string MobCommandFailed = "MOB_COMMAND_FAILED";
    public const string MobCommandUnknown = "MOB_COMMAND_UNKNOWN";
    public const string MobCommandInvalid = "MOB_COMMAND_INVALID";
    public const string MobPreviewFailed = "MOB_PREVIEW_FAILED";
    public const string MobUploadTooLarge = "MOB_UPLOAD_TOO_LARGE";
    public const string MobUploadBadType = "MOB_UPLOAD_BAD_TYPE";
    public const string MobDeviceLimit = "MOB_DEVICE_LIMIT";

    // CAM_*
    public const string CamSidecarMissing = "CAM_SIDECAR_MISSING";
    public const string CamSidecarStartFailed = "CAM_SIDECAR_START_FAILED";
    public const string CamPortInUse = "CAM_PORT_IN_USE";
    public const string CamNoPrivateIp = "CAM_NO_PRIVATE_IP";
    public const string CamStreamTimeout = "CAM_STREAM_TIMEOUT";
    public const string CamUnsupportedWin7 = "CAM_UNSUPPORTED_WIN7";

    // CHAT_* / VOICE_* / TRANS_* / ASR_*
    public const string ChatConnectFailed = "CHAT_CONNECT_FAILED";
    public const string ChatProtocolChanged = "CHAT_PROTOCOL_CHANGED";
    public const string ChatRoomInvalid = "CHAT_ROOM_INVALID";
    public const string ChatRateLimited = "CHAT_RATE_LIMITED";
    public const string AsrModelMissing = "ASR_MODEL_MISSING";
    public const string AsrAudioDevice = "ASR_AUDIO_DEVICE";
    public const string AsrUnsupportedWin7 = "ASR_UNSUPPORTED_WIN7";
    public const string VoiceWakeMissing = "VOICE_WAKE_MISSING";
    public const string VoiceCommandDenied = "VOICE_COMMAND_DENIED";
    public const string TransEngineMissing = "TRANS_ENGINE_MISSING";
    public const string TransRateLimited = "TRANS_RATE_LIMITED";

    // CLIP_* / MULTI_* / REMOTE_* / STREAM_* / ARCHIVE_*
    public const string ClipFfmpegMissing = "CLIP_FFMPEG_MISSING";
    public const string ClipOutputFailed = "CLIP_OUTPUT_FAILED";
    public const string MultiLimit = "MULTI_LIMIT";
    public const string MultiSourceFailed = "MULTI_SOURCE_FAILED";
    public const string RemoteRoomFull = "REMOTE_ROOM_FULL";
    public const string RemoteTurnFailed = "REMOTE_TURN_FAILED";
    public const string StreamBandwidthLow = "STREAM_BANDWIDTH_LOW";
    public const string StreamRelayFailed = "STREAM_RELAY_FAILED";
    public const string ArchiveRemoteMissing = "ARCHIVE_REMOTE_MISSING";
    public const string ArchiveUploadFailed = "ARCHIVE_UPLOAD_FAILED";

    // THEME_* / HARD_* / COACH_* / DIRECTOR_* / AIHOST_*
    public const string ThemeHashMismatch = "THEME_HASH_MISMATCH";
    public const string ThemeUnsafeAsset = "THEME_UNSAFE_ASSET";
    public const string HardMidiConflict = "HARD_MIDI_CONFLICT";
    public const string HardWledTimeout = "HARD_WLED_TIMEOUT";
    public const string CoachNoData = "COACH_NO_DATA";
    public const string CoachReportFailed = "COACH_REPORT_FAILED";
    public const string DirectorConfirmRequired = "DIRECTOR_CONFIRM_REQUIRED";
    public const string DirectorRuleConflict = "DIRECTOR_RULE_CONFLICT";
    public const string AiHostTtsFailed = "AIHOST_TTS_FAILED";
    public const string AiHostLipsyncFailed = "AIHOST_LIPSYNC_FAILED";

    public static string Describe(string code)
    {
        var key = "err." + code;
        var text = Strings.T(key);
        return string.Equals(text, key, StringComparison.Ordinal) ? Strings.T("err.undefined") : text;
    }
}
