using System.Globalization;
using System.IO;
using OBS_Helper.Wpf.Services.Mobile;
using System.Security.Cryptography;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OBS_Helper.Wpf.Services.Automation;

/// <summary>硬件控台映射（E01）：MIDI 音符/CC → 白名单动作；冲突检测与导入导出。</summary>
public static class MidiCore
{
    public sealed class Mapping
    {
        public string Device { get; set; } = "";
        public int Channel { get; set; } = 1;
        public int Note { get; set; } = -1;
        public int Controller { get; set; } = -1;
        public string Action { get; set; } = "";
        public string Argument { get; set; } = "";
    }

    public static readonly string[] Actions =
    {
        "scene.next", "scene.prev", "scene.set", "record.toggle", "stream.toggle",
        "mute.toggle", "virtualcam.toggle", "replay.save", "studio.toggle", "teleprompter.playpause",
    };

    public static (bool Ok, string Code, string Detail) Validate(IEnumerable<Mapping> mappings)
    {
        var list = mappings.ToList();
        foreach (var mapping in list)
        {
            if (!Actions.Contains(mapping.Action)) return (false, FeatureErrors.HardMidiConflict, "未知动作：" + mapping.Action);
            if (mapping.Note < 0 && mapping.Controller < 0) return (false, FeatureErrors.HardMidiConflict, "音符与 CC 至少填一个");
            if (mapping.Channel is < 1 or > 16) return (false, FeatureErrors.HardMidiConflict, "通道必须是 1~16");
            if (mapping.Action == "scene.set" && string.IsNullOrWhiteSpace(mapping.Argument))
                return (false, FeatureErrors.HardMidiConflict, "scene.set 需要场景名");
        }
        var duplicate = list.GroupBy(m => (m.Device, m.Channel, m.Note, m.Controller))
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null) return (false, FeatureErrors.HardMidiConflict, "同一按键绑定了多个动作");
        return (true, "", "");
    }

    public static Mapping? Match(IEnumerable<Mapping> mappings, string device, int channel, int? note, int? controller)
        => mappings.FirstOrDefault(m =>
            (string.IsNullOrEmpty(m.Device) || m.Device == device) &&
            m.Channel == channel &&
            ((note.HasValue && m.Note == note.Value) || (controller.HasValue && m.Controller == controller.Value)));

    public static string Export(IEnumerable<Mapping> mappings)
        => JsonSerializer.Serialize(mappings.ToList(), new JsonSerializerOptions { WriteIndented = true });

    public static (bool Ok, List<Mapping> Mappings) Import(string? json)
    {
        try
        {
            var list = JsonSerializer.Deserialize<List<Mapping>>(json ?? "[]") ?? new List<Mapping>();
            return (Validate(list).Ok, list);
        }
        catch (JsonException) { return (false, new List<Mapping>()); }
    }

    /// <summary>默认配置：脚踏板常见的 CC 64/65/66 与音符 60/61。</summary>
    public static List<Mapping> Defaults() => new()
    {
        new() { Channel = 1, Controller = 64, Action = "record.toggle" },
        new() { Channel = 1, Controller = 65, Action = "stream.toggle" },
        new() { Channel = 1, Controller = 66, Action = "mute.toggle" },
        new() { Channel = 1, Note = 60, Action = "scene.next" },
        new() { Channel = 1, Note = 61, Action = "scene.prev" },
    };
}

/// <summary>灯光联动（E02）：WLED HTTP JSON；仅私网、超时降级、不依赖云。</summary>
public static class WledCore
{
    public sealed record Preset(string Id, int Effect, int Brightness, string Color);

    public static readonly Preset[] Presets =
    {
        new("idle", 1, 80, "1a6b5a"),
        new("live", 0, 200, "e5484d"),
        new("gift", 9, 255, "ffd166"),
        new("brb", 2, 60, "4c9ffe"),
        new("off", 0, 0, "000000"),
    };

    public static (bool Ok, string Code) ValidateUrl(string? url)
    {
        if (!Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out var uri)) return (false, FeatureErrors.HardWledTimeout);
        if (uri.Scheme is not ("http" or "https")) return (false, FeatureErrors.HardWledTimeout);
        var host = uri.Host;
        if (host is "localhost" or "127.0.0.1") return (true, "");
        if (!Services.Security.LanPolicy.IsPrivateAddress(host)) return (false, FeatureErrors.HardWledTimeout);
        return (true, "");
    }

    /// <summary>WLED JSON API：<c>/json/state</c>。</summary>
    public static string BuildStateUrl(string baseUrl) => baseUrl.TrimEnd('/') + "/json/state";

    public static string BuildPayload(Preset preset) => JsonSerializer.Serialize(new
    {
        on = preset.Brightness > 0,
        bri = preset.Brightness,
        seg = new[] { new { id = 0, fx = preset.Effect, col = new[] { new[] { HexToRgb(preset.Color) } } } },
    });

    public static int[] HexToRgb(string hex)
    {
        var value = hex.TrimStart('#');
        if (value.Length != 6) return new[] { 255, 255, 255 };
        return new[]
        {
            int.Parse(value[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            int.Parse(value[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            int.Parse(value[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        };
    }
}

/// <summary>WLED 客户端：超时 2 秒、失败降级为日志，不阻塞 UI。</summary>
public sealed class WledClient : IDisposable
{
    private readonly HttpClient _http;
    public string? LastError { get; private set; }

    public WledClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
    }

    public async Task<bool> ApplyAsync(string baseUrl, WledCore.Preset preset)
    {
        if (!WledCore.ValidateUrl(baseUrl).Ok) { LastError = FeatureErrors.HardWledTimeout; return false; }
        try
        {
            var body = new StringContent(WledCore.BuildPayload(preset), Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync(WledCore.BuildStateUrl(baseUrl), body).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) { LastError = FeatureErrors.HardWledTimeout; return false; }
            LastError = null;
            return true;
        }
        catch (Exception)
        {
            LastError = FeatureErrors.HardWledTimeout;
            return false;
        }
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>插件市场 2.0 信任核心（E05）：签名、评分、依赖、沙箱策略；不静默安装。</summary>
public static class PluginTrustCore
{
    public sealed class TrustManifest
    {
        public string Id { get; set; } = "";
        public string Version { get; set; } = "";
        public List<string> Files { get; set; } = new();
        public string Signature { get; set; } = "";
        public double Rating { get; set; }
        public List<string> Dependencies { get; set; } = new();
    }

    public sealed record Policy(bool AllowUnsigned, bool AllowNetwork, bool AllowNative, bool AllowWrites);

    public static bool VerifySignature(TrustManifest manifest, string payload, string publicKeyPem)
    {
        if (string.IsNullOrWhiteSpace(manifest.Signature)) return false;
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            return rsa.VerifyData(Encoding.UTF8.GetBytes(payload),
                Convert.FromBase64String(manifest.Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (Exception) { return false; }
    }

    /// <summary>沙箱策略：默认禁止网络/原生库/写系统目录；插件只能写自己的数据目录。</summary>
    public static readonly Policy DefaultPolicy = new(false, false, false, false);

    public static (bool Ok, List<string> Issues) Check(TrustManifest manifest, Policy policy, bool signatureValid)
    {
        var issues = new List<string>();
        if (!signatureValid && !policy.AllowUnsigned) issues.Add("签名无效（默认拒绝未签名插件）");
        if (manifest.Rating < 0 || manifest.Rating > 5) issues.Add("评分越界");
        if (manifest.Dependencies.Count > 20) issues.Add("依赖数量异常");
        if (manifest.Files.Any(f => f.Contains("..") || Path.IsPathRooted(f))) issues.Add("包含不安全路径");
        return (issues.Count == 0, issues);
    }

    public static string TrustLabel(double rating)
        => rating >= 4.5 ? "trusted" : rating >= 3.5 ? "ok" : rating > 0 ? "caution" : "unknown";
}

/// <summary>装修市场（E03）：资产包哈希、CSP、预览、回滚；禁止远程代码。</summary>
public static class ThemeMarketCore
{
    public sealed class ThemeFile
    {
        public string Path { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public long Size { get; set; }
    }

    public sealed class ThemePackage
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        public List<ThemeFile> Files { get; set; } = new();
        public string Author { get; set; } = "";
    }

    public static (bool Ok, string Code, List<string> Issues) Validate(ThemePackage package)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(package.Id)) issues.Add("缺少 id");
        foreach (var file in package.Files)
        {
            var safe = Services.Interop.SidecarCore.SafeRelativePath(file.Path);
            if (safe is null) issues.Add("不安全路径：" + file.Path);
            if (file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit)) issues.Add("哈希无效：" + file.Path);
            var ext = Path.GetExtension(file.Path).ToLowerInvariant();
            if (ext is not (".css" or ".html" or ".png" or ".jpg" or ".webp" or ".json" or ".woff2"))
                issues.Add("不允许的文件类型：" + ext);
        }
        if (package.Files.Count == 0) issues.Add("资产包为空");
        return (issues.Count == 0, issues.Count == 0 ? "" : FeatureErrors.ThemeUnsafeAsset, issues);
    }

    public static (bool Ok, string Code) VerifyFile(string path, ThemeFile expected)
    {
        try
        {
            if (!File.Exists(path)) return (false, FeatureErrors.ThemeHashMismatch);
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            var hash = Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
            return hash == expected.Sha256.ToLowerInvariant()
                ? (true, "")
                : (false, FeatureErrors.ThemeHashMismatch);
        }
        catch (Exception) { return (false, FeatureErrors.ThemeHashMismatch); }
    }

    /// <summary>回滚计划：把旧版本目录复制回来；返回操作说明（由服务执行）。</summary>
    public static IReadOnlyList<string> RollbackPlan(string themeId, string currentVersion, string previousVersion)
        => new[]
        {
            $"停止 overlay 服务（{themeId}）",
            $"备份 {themeId}\\{currentVersion}",
            $"恢复 {themeId}\\{previousVersion}",
            "重新加载 overlay 并校验 CSP",
        };
}

/// <summary>多操作员 / 团队协作（E04）：角色、邀请过期、审计、踢人；最小权限。</summary>
public sealed class TeamRolesCore
{
    public enum Role { Viewer, Operator, Admin }

    public sealed class Member
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N")[..12];
        public string Name { get; init; } = "";
        public Role Role { get; init; } = Role.Viewer;
        public DateTimeOffset JoinedUtc { get; init; } = DateTimeOffset.UtcNow;
    }

    public sealed class Invite
    {
        public string Code { get; init; } = Guid.NewGuid().ToString("N")[..10];
        public Role Role { get; init; } = Role.Operator;
        public DateTimeOffset ExpiresUtc { get; init; } = DateTimeOffset.UtcNow.AddMinutes(10);
    }

    public sealed record Audit(DateTimeOffset At, string Actor, string Action, string Detail);

    private readonly List<Member> _members = new();
    private readonly List<Audit> _audit = new();
    private readonly object _gate = new();

    public IReadOnlyList<Member> Members { get { lock (_gate) return _members.ToList(); } }
    public IReadOnlyList<Audit> AuditLog { get { lock (_gate) return _audit.ToList(); } }

    public Invite CreateInvite(Role role, TimeSpan? ttl = null)
        => new() { Role = role, ExpiresUtc = DateTimeOffset.UtcNow.Add(ttl ?? TimeSpan.FromMinutes(10)) };

    public (bool Ok, string Code, Member? Member) Accept(Invite invite, string name, DateTimeOffset now)
    {
        if (now > invite.ExpiresUtc) return (false, FeatureErrors.MobPairExpired, null);
        lock (_gate)
        {
            var member = new Member { Name = name, Role = invite.Role };
            _members.Add(member);
            _audit.Add(new Audit(now, "admin", "invite", $"{name} ({invite.Role})"));
            return (true, "", member);
        }
    }

    public bool Kick(string memberId)
    {
        lock (_gate)
        {
            var member = _members.FirstOrDefault(m => m.Id == memberId);
            if (member is null) return false;
            _members.Remove(member);
            _audit.Add(new Audit(DateTimeOffset.Now, "admin", "kick", member.Name));
            return true;
        }
    }

    public bool Can(Role role, Role required) => role >= required;
}

/// <summary>AI 直播搭档 / 数字人（S04）：TTS 队列、口型参数、可打断、明确 AI 标注。</summary>
public static class AiHostCore
{
    public sealed class Line
    {
        public string Text { get; init; } = "";
        public bool IsAi { get; init; } = true;
        public DateTimeOffset At { get; init; } = DateTimeOffset.Now;
    }

    public static readonly string[] InterruptWords = { "停", "别说了", "停一下", "stop", "quiet" };

    public static bool ShouldInterrupt(string? heard)
        => !string.IsNullOrWhiteSpace(heard) && InterruptWords.Any(w => heard.Contains(w, StringComparison.OrdinalIgnoreCase));

    /// <summary>VTuber Studio 口型参数：MouthOpen 0~1，按音节数做平滑。</summary>
    public static double LipSyncValue(string text, double t)
    {
        var syllables = Math.Max(1, text.Count(ch => char.IsLetterOrDigit(ch) || (ch >= 0x4E00 && ch <= 0x9FFF)));
        return Math.Clamp(Math.Abs(Math.Sin(t * syllables * 3.0)) * 0.8, 0, 1);
    }

    /// <summary>AI 输出的公开标注：overlay 与状态栏都要显示，不能冒充真人。</summary>
    public static string Label(bool zh = true) => zh ? "AI 生成 · 非真人" : "AI-generated · not a human";
}

/// <summary>语音控制（S03）：唤醒词 + 命令白名单 + 破坏性动作确认。</summary>
public static class VoiceCommandCore
{
    public static readonly string[] DefaultWakeWords = { "小助手", "obs helper" };

    public sealed record Command(string Id, string[] Phrases, bool Destructive);

    public static readonly Command[] Commands =
    {
        new("record.toggle", new[] { "开始录制", "停止录制", "record", "stop recording" }, false),
        new("scene.next", new[] { "下一个场景", "next scene" }, false),
        new("stream.toggle", new[] { "开始推流", "停止推流", "go live", "stop stream" }, true),
        new("mute.toggle", new[] { "静音", "取消静音", "mute", "unmute" }, false),
        new("brb", new[] { "切到休息", "brb" }, true),
    };

    public static string Normalize(string? text)
        => (text ?? "").Trim().ToLowerInvariant().Replace(" ", "");

    public static bool HasWakeWord(string? text, IEnumerable<string> wakeWords)
        => wakeWords.Any(w => Normalize(text).Contains(Normalize(w)));

    /// <summary>解析命令：必须同时命中唤醒词与短语；破坏性动作返回 NeedsConfirm。</summary>
    public static (Command? Command, bool NeedsConfirm, string Code) Parse(string? text, IEnumerable<string> wakeWords)
    {
        if (!HasWakeWord(text, wakeWords)) return (null, false, FeatureErrors.VoiceWakeMissing);
        var normalized = Normalize(text);
        foreach (var command in Commands)
        {
            if (command.Phrases.Any(p => normalized.Contains(Normalize(p))))
                return (command, command.Destructive, "");
        }
        return (null, false, FeatureErrors.VoiceCommandDenied);
    }
}

/// <summary>录制自动归档（Q05）：计划、校验、重试、带宽限制、可撤销。</summary>
public static class ArchiveCore
{
    public sealed class Plan
    {
        public string LocalPath { get; init; } = "";
        public string RemoteName { get; init; } = "";
        public bool VerifyChecksum { get; init; } = true;
        public int MaxRetries { get; init; } = 3;
        public int BandwidthLimitKbps { get; init; }
        public DateTimeOffset? DeleteLocalAfterUtc { get; init; }
    }

    public static (bool Ok, string Code) Validate(Plan plan, string? rclonePath, string? remote)
    {
        if (string.IsNullOrWhiteSpace(remote)) return (false, FeatureErrors.ArchiveRemoteMissing);
        if (string.IsNullOrWhiteSpace(rclonePath) || !File.Exists(rclonePath)) return (false, FeatureErrors.ArchiveRemoteMissing);
        if (string.IsNullOrWhiteSpace(plan.LocalPath) || !File.Exists(plan.LocalPath)) return (false, FeatureErrors.ArchiveUploadFailed);
        if (plan.RemoteName.Contains("..") || plan.RemoteName.Contains(':')) return (false, FeatureErrors.ArchiveUploadFailed);
        return (true, "");
    }

    /// <summary>rclone 参数：限带宽、校验、重试；不经过 shell。</summary>
    public static IReadOnlyList<string> BuildArgs(Plan plan, string remote)
    {
        var args = new List<string> { "copy", plan.LocalPath, remote.TrimEnd('/') + "/" + plan.RemoteName, "--checksum" };
        if (plan.BandwidthLimitKbps > 0) args.Add("--bwlimit=" + plan.BandwidthLimitKbps.ToString(CultureInfo.InvariantCulture) + "k");
        args.Add("--retries=" + Math.Clamp(plan.MaxRetries, 1, 10).ToString(CultureInfo.InvariantCulture));
        return args;
    }
}
