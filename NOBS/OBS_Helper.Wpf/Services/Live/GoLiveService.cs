using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;
using OBS_Helper.Wpf.Localization;
using OBS_Helper.Wpf.Models.Live;
using OBS_Helper.Wpf.Services.Host;
using OBS_Helper.Wpf.Services.Obs;

namespace OBS_Helper.Wpf.Services.Live;

/// <summary>开播操作的结果。</summary>
public sealed record GoLiveResult(bool Ok, string? ErrorCode, IReadOnlyList<string> Notes);

/// <summary>
/// 开播编排（V3.3.0 / P0-4）：
/// <list type="bullet">
///   <item><b>直推</b>：确保 OBS 连接 → 写推流设置 → 复核 → 开播 → 轮询确认真的播出去了；</item>
///   <item><b>虚拟摄像头</b>：确保连接 → 启动虚拟摄像头 → 确认 outputActive；</item>
///   <item><b>第三方客户端</b>：只读探测进程 / 注册表 / 安装目录，不做 UI 自动化，不点「开始直播」。</item>
/// </list>
///
/// 推流码只在内存与（用户显式勾选后的）<c>secrets.dat</c> 里出现；界面只拿遮罩，
/// 日志 / 诊断 / Toast 永远不包含明文。
/// </summary>
public sealed class GoLiveService
{
    private const string SecretPrefix = "golive.key.";

    private readonly ObsConnectionService _obs;
    private readonly HostBridge _host;
    private bool _weStartedStream;
    private bool _weStartedVirtualCam;

    public GoLiveService(ObsConnectionService obs, HostBridge host)
    {
        _obs = obs;
        _host = host;
    }

    /// <summary>本次会话是否由本应用启动了推流（用于「只停自己开的」）。</summary>
    public bool WeStartedStream => _weStartedStream;

    /// <summary>本次会话是否由本应用启动了虚拟摄像头。</summary>
    public bool WeStartedVirtualCam => _weStartedVirtualCam;

    /// <summary>当前直推是否可用（Win7 兼容构建不支持写入推流设置）。</summary>
    public static bool SupportsDirectStreaming
    {
        get
        {
#if WIN7_COMPAT
            return false;
#else
            return true;
#endif
        }
    }

    /// <summary>
    /// 直推：写入服务器 / 推流码并开播，成功后轮询确认 <c>outputActive</c>。
    /// </summary>
    public async Task<GoLiveResult> StartDirectAsync(
        LivePlatform platform, string server, string key, bool rememberKey,
        IProgress<string>? progress, CancellationToken ct)
    {
        var notes = new List<string>();

        if (!SupportsDirectStreaming)
            return new GoLiveResult(false, "GOLIVE_V4_UNSUPPORTED", notes);

        if (platform is null || !GoLiveCore.SupportsDirect(platform))
            return new GoLiveResult(false, "GOLIVE_MODE_UNSUPPORTED", notes);

        progress?.Report(Strings.T("golive.step.validate"));
        var normalizedServer = GoLiveCore.NormalizeServer(server);
        var normalizedKey = GoLiveCore.NormalizeKey(key);
        var validation = GoLiveCore.ValidateDirectInput(server, key);
        if (validation.Length > 0)
            return new GoLiveResult(false, validation, notes);

        progress?.Report(Strings.T("golive.step.connect"));
        if (!_obs.IsConnected)
        {
            var ensure = await _obs.EnsureConnectedAsync(TimeSpan.FromSeconds(12), null, ct).ConfigureAwait(false);
            if (!ensure.Ok)
                return new GoLiveResult(false, "GOLIVE_OBS_NOT_CONNECTED", notes);
        }

        if (_obs.StreamStatus.Active)
            return new GoLiveResult(false, "GOLIVE_ALREADY_STREAMING", notes);

        progress?.Report(Strings.T("golive.step.write"));
        var write = await _obs.SetStreamServiceSettingsAsync(
            GoLiveCore.BuildStreamServiceSettings(normalizedServer!, normalizedKey!), ct).ConfigureAwait(false);
        if (!write.Ok)
        {
            notes.Add(write.Comment ?? "");
            return new GoLiveResult(false, "GOLIVE_WRITE_FAILED", notes);
        }

        // 写入后复核「有服务器 / 有密钥」——只判存在性，不保留密钥
        progress?.Report(Strings.T("golive.step.verify"));
        var verify = await _obs.GetStreamServiceSettingsRawAsync(ct).ConfigureAwait(false);
        if (!verify.Ok || !HasServerAndKey(verify.Data))
            return new GoLiveResult(false, "GOLIVE_VERIFY_FAILED", notes);

        progress?.Report(Strings.T("golive.step.start"));
        var start = await _obs.StartStreamAsync().ConfigureAwait(false);
        if (!start.Ok)
        {
            notes.Add(start.Comment ?? "");
            return new GoLiveResult(false, GoLiveCore.ClassifyStreamError(start.Comment), notes);
        }

        _weStartedStream = true;

        // 轮询确认：OBS 返回 Ok 只代表请求被接受，不代表流真的推上去了
        progress?.Report(Strings.T("golive.step.confirm"));
        var status = await WaitForStreamActiveAsync(TimeSpan.FromSeconds(12), ct).ConfigureAwait(false);
        if (status is null || !status.Active)
            return new GoLiveResult(false, "GOLIVE_NOT_CONFIRMED", notes);

        if (rememberKey)
        {
            try
            {
                await _host.SetSecretAsync(SecretPrefix + platform.Id, normalizedKey!).ConfigureAwait(false);
                notes.Add(Strings.T("golive.note.keyRemembered"));
            }
            catch (Exception ex)
            {
                FileLogger.Warn("GoLive", $"记住推流码失败（不影响本次开播）：{ex.Message}");
            }
        }
        else
        {
            // 用户没勾选「记住」：清掉该平台的旧密钥，避免上次的残留继续存在
            try { await _host.DeleteSecretAsync(SecretPrefix + platform.Id).ConfigureAwait(false); }
            catch { /* 没有旧密钥也无妨 */ }
        }

        notes.Add(Strings.T("golive.note.server", GoLiveCore.MaskServer(normalizedServer)));
        return new GoLiveResult(true, null, notes);
    }

    /// <summary>启动虚拟摄像头并确认 <c>outputActive</c>。</summary>
    public async Task<GoLiveResult> StartVirtualCameraAsync(IProgress<string>? progress, CancellationToken ct)
    {
        var notes = new List<string>();

        progress?.Report(Strings.T("golive.step.checkVcam"));
        if (!_obs.IsConnected)
        {
            var ensure = await _obs.EnsureConnectedAsync(TimeSpan.FromSeconds(12), null, ct).ConfigureAwait(false);
            if (!ensure.Ok)
                return new GoLiveResult(false, "GOLIVE_OBS_NOT_CONNECTED", notes);
        }

        progress?.Report(Strings.T("golive.step.startVcam"));
        var start = await _obs.StartVirtualCamAsync().ConfigureAwait(false);
        // 已经在运行（601）也算成功：用户可能自己先开了
        if (!start.Ok && start.Code != 601)
        {
            notes.Add(start.Comment ?? "");
            return new GoLiveResult(false, "GOLIVE_VCAM_START_FAILED", notes);
        }

        _weStartedVirtualCam = true;

        progress?.Report(Strings.T("golive.step.confirmVcam"));
        var active = await WaitForVirtualCamActiveAsync(TimeSpan.FromSeconds(8), ct).ConfigureAwait(false);
        if (!active)
            return new GoLiveResult(false, "GOLIVE_VCAM_NOT_CONFIRMED", notes);

        notes.Add(Strings.T("golive.note.vcamRunning"));
        return new GoLiveResult(true, null, notes);
    }

    /// <summary>停止本次由应用启动的推流；不是自己开的就不动。</summary>
    public async Task<GoLiveResult> StopDirectAsync(CancellationToken ct)
    {
        if (!_weStartedStream)
            return new GoLiveResult(true, null, new[] { Strings.T("golive.note.notOurs") });

        var stop = await _obs.StopStreamAsync().ConfigureAwait(false);
        if (!stop.Ok)
            return new GoLiveResult(false, "GOLIVE_STOP_FAILED", new[] { stop.Comment ?? "" });

        _weStartedStream = false;
        return new GoLiveResult(true, null, new[] { Strings.T("golive.note.stopped") });
    }

    /// <summary>停止本次由应用启动的虚拟摄像头；不是自己开的就不动。</summary>
    public async Task<GoLiveResult> StopVirtualCameraAsync(CancellationToken ct)
    {
        if (!_weStartedVirtualCam)
            return new GoLiveResult(true, null, new[] { Strings.T("golive.note.notOurs") });

        var stop = await _obs.StopVirtualCamAsync().ConfigureAwait(false);
        if (!stop.Ok)
            return new GoLiveResult(false, "GOLIVE_VCAM_STOP_FAILED", new[] { stop.Comment ?? "" });

        _weStartedVirtualCam = false;
        return new GoLiveResult(true, null, new[] { Strings.T("golive.note.stopped") });
    }

    /// <summary>读取用户之前「记住」的推流码（没有返回 null）。</summary>
    public async Task<string?> GetRememberedKeyAsync(string platformId)
    {
        if (string.IsNullOrWhiteSpace(platformId)) return null;
        try { return await _host.GetSecretAsync(SecretPrefix + platformId).ConfigureAwait(false); }
        catch (Exception) { return null; }
    }

    /// <summary>忘记某个平台记住的推流码。</summary>
    public async Task ForgetKeyAsync(string platformId)
    {
        if (string.IsNullOrWhiteSpace(platformId)) return;
        try { await _host.DeleteSecretAsync(SecretPrefix + platformId).ConfigureAwait(false); }
        catch (Exception ex) { FileLogger.Warn("GoLive", $"清除推流码失败：{ex.Message}"); }
    }

    /// <summary>探测平台关联的第三方客户端（进程 / 注册表 / 安装目录三路交叉，只读）。</summary>
    public IReadOnlyList<CompanionStatus> DetectCompanions(LivePlatform platform, PlatformCatalogService catalog)
    {
        var result = new List<CompanionStatus>();
        var apps = catalog.CompanionsOf(platform);
        if (apps.Count == 0) return result;

        HashSet<string> running;
        try
        {
            running = Process.GetProcesses()
                .Select(p => { try { return p.ProcessName; } catch { return ""; } })
                .Where(n => n.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var uninstallNames = ReadUninstallDisplayNames();

        foreach (var app in apps)
        {
            var isRunning = app.ProcessNames.Any(n => running.Contains(n.Replace(".exe", "", StringComparison.OrdinalIgnoreCase)));
            var registryHit = app.UninstallNames.Any(n => uninstallNames.Any(x => x.Contains(n, StringComparison.OrdinalIgnoreCase)));
            var installPath = CompanionAppCore.FindInstallDirectory(app);
            result.Add(CompanionAppCore.Combine(app, isRunning, registryHit, installPath));
        }

        return result;
    }

    /// <summary>读取注册表「卸载」项显示名（HKCU + HKLM，64/32 位视图）。</summary>
    private static List<string> ReadUninstallDisplayNames()
    {
        var names = new List<string>();
        var roots = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };
        var hives = new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine };

        foreach (var hive in hives)
        {
            foreach (var root in roots)
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                    using var key = baseKey.OpenSubKey(root);
                    if (key is null) continue;
                    foreach (var sub in key.GetSubKeyNames())
                    {
                        try
                        {
                            using var item = key.OpenSubKey(sub);
                            if (item?.GetValue("DisplayName") is string display && display.Length > 0)
                                names.Add(display);
                        }
                        catch (Exception) { /* 单个项读不到就跳过 */ }
                    }
                }
                catch (Exception)
                {
                    // 权限 / 平台不支持：忽略这一支
                }
            }
        }
        return names;
    }

    private async Task<GoLiveStreamStatus?> WaitForStreamActiveAsync(TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        GoLiveStreamStatus? last = null;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var r = await _obs.GetStreamStatusRawAsync(ct).ConfigureAwait(false);
            if (r.Ok && r.Data is { } d)
            {
                last = GoLiveCore.ParseStreamStatus(d);
                if (last.Active) return last;
            }
            await Task.Delay(500, ct).ConfigureAwait(false);
        }
        return last;
    }

    private async Task<bool> WaitForVirtualCamActiveAsync(TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (_obs.VirtualCamStatus.Active) return true;

            var r = await _obs.GetVirtualCamStatusRawAsync(ct).ConfigureAwait(false);
            if (r.Ok && r.Data is { } d && d.ValueKind == JsonValueKind.Object
                && d.TryGetProperty("outputActive", out var a) && a.ValueKind == JsonValueKind.True)
                return true;

            await Task.Delay(500, ct).ConfigureAwait(false);
        }
        return _obs.VirtualCamStatus.Active;
    }

    /// <summary>只判断「服务器 / 密钥都有值」，不读、不保留具体内容。</summary>
    private static bool HasServerAndKey(JsonElement? data)
    {
        if (data is not { ValueKind: JsonValueKind.Object } d) return false;
        if (!d.TryGetProperty("streamServiceSettings", out var s) || s.ValueKind != JsonValueKind.Object) return false;

        var hasServer = s.TryGetProperty("server", out var sv) && sv.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(sv.GetString());
        var hasKey = s.TryGetProperty("key", out var kv) && kv.ValueKind == JsonValueKind.String
                     && !string.IsNullOrWhiteSpace(kv.GetString());
        return hasServer && hasKey;
    }
}
