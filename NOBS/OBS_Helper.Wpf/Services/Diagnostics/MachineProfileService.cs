using OBS_Helper.Wpf.Services.Obs;
using OBS_Helper.Wpf.Services.ObsConfig;
using OBS_Helper.Wpf.Services.SystemCheck;
using OBS_Helper.Wpf.Services.Tools;

namespace OBS_Helper.Wpf.Services.Diagnostics;

/// <summary>
/// 开播前体检服务（V3.0 / D4 后半段）：把五项检查跑一遍，交给
/// <see cref="MachineProfileCore"/> 合成一个结论。
///
/// 分工与边界：
/// <list type="bullet">
///   <item>本服务**只读**：探测全部只读，磁盘基准也只写一个临时文件并立即删除；</item>
///   <item>推流侧只产出建议（不改任何推流设置）；</item>
///   <item>录制侧的落地由界面调用既有 <c>SimpleRecordingService</c> 的可回滚路径完成（见 ReleaseNotes 5.6）。</item>
/// </list>
/// </summary>
public sealed class MachineProfileService
{
    private readonly PreflightCheckService _preflight;
    private readonly ObsPathService _paths;

    public MachineProfileService(PreflightCheckService preflight, ObsPathService paths)
    {
        _preflight = preflight;
        _paths = paths;
    }

    /// <summary>跑一次完整体检。永不抛异常（单项失败只让那一项保持「未知」）。</summary>
    public async Task<MachineProfile> RunAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var inputs = new MachineProfileInputs();

        // ① 图形环境（注册表 / WMI 全部在后台线程）
        progress?.Report(Strings.T("machine.step.graphics"));
        try
        {
            var snap = await Task.Run(GraphicsEnvCheckService.CollectSnapshot, ct).ConfigureAwait(false);
            inputs = inputs with
            {
                Elevated = snap.Elevated,
                GameDvrEnabled = snap.GameDvrEnabled,
                HwSchMode = snap.HwSchMode,
                GpuCount = snap.Gpus.Count,
                ObsGpuPreferenceSet = !string.IsNullOrEmpty(snap.ObsGpuPreference),
                OnBattery = snap.OnBattery,
                ActivePowerScheme = snap.ActivePowerScheme,
            };
        }
        catch (Exception ex)
        {
            FileLogger.Warn("MachineProfile", $"读取图形环境失败（按未知处理）：{ex.Message}");
        }

        // ② OBS 录制设置（编码器 / 码率 / 采样率 / 格式）
        progress?.Report(Strings.T("machine.step.facts"));
        try
        {
            var facts = await _preflight.ReadFactsAsync().ConfigureAwait(false);
            if (facts is not null)
            {
                inputs = inputs with
                {
                    EncoderNames = facts.EncoderNames,
                    AudioSampleRateHz = facts.AudioSampleRateHz,
                    StreamBitrateKbps = facts.VideoBitrateKbps,
                    RecordingFormat = facts.RecordingFormat,
                };
            }
        }
        catch (Exception ex)
        {
            FileLogger.Warn("MachineProfile", $"读取录制设置失败（按未知处理）：{ex.Message}");
        }

        // ③ 上行速度（用户在工具箱填写的值）
        try
        {
            var raw = AppServices.Store.GetItem("bandwidth_upload_mbps");
            if (!string.IsNullOrWhiteSpace(raw) &&
                double.TryParse(raw, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var mbps) && mbps > 0)
            {
                inputs = inputs with { UplinkMbps = mbps };
            }
        }
        catch (Exception) { /* 没填过属正常 */ }

        // ④ 磁盘：剩余空间 + 顺序写入实测
        progress?.Report(Strings.T("machine.step.disk"));
        try
        {
            var dir = await ResolveRecordingDirAsync().ConfigureAwait(false);
            if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
            {
                var freeBytes = DiskBenchmarkService.FreeBytesOf(dir);
                inputs = inputs with { DiskFreeGb = freeBytes / 1024.0 / 1024 / 1024 };

                var writeMbps = await Task.Run(() => DiskBenchmarkService.MeasureSequentialWrite(dir), ct)
                    .ConfigureAwait(false);
                if (writeMbps > 0) inputs = inputs with { DiskWriteMbps = writeMbps };
            }
        }
        catch (Exception ex)
        {
            FileLogger.Warn("MachineProfile", $"磁盘基准失败（按未知处理）：{ex.Message}");
        }

        // ⑤ 推流节点延迟（取最快的一个：用户通常就该选最近的节点）
        progress?.Report(Strings.T("machine.step.network"));
        try
        {
            var results = await IngestPingService.MeasureAllAsync(IngestPingService.DefaultTargets, ct)
                .ConfigureAwait(false);
            var best = results.Where(r => r.Ok).OrderBy(r => r.RttMs).FirstOrDefault();
            if (best?.RttMs is { } rtt) inputs = inputs with { IngestRttMs = rtt };
        }
        catch (Exception ex)
        {
            FileLogger.Warn("MachineProfile", $"节点探测失败（按未知处理）：{ex.Message}");
        }

        return MachineProfileCore.Build(inputs);
    }

    /// <summary>录制目录（OBS 设置里的录像路径）；拿不到时退回「视频」文件夹。</summary>
    private async Task<string?> ResolveRecordingDirAsync()
    {
        try
        {
            var loc = await _paths.LocateAsync().ConfigureAwait(false);
            if (loc.Exists)
            {
                var globalIni = System.IO.Path.Combine(loc.ConfigDir, "global.ini");
                if (System.IO.File.Exists(globalIni))
                {
                    var ini = PreflightCheckCore.ParseIni(System.IO.File.ReadAllText(globalIni));
                    if (ini.TryGetValue("basic.recordingpath", out var path) && !string.IsNullOrWhiteSpace(path))
                        return path;
                }
            }
        }
        catch (Exception ex)
        {
            FileLogger.Warn("MachineProfile", $"解析录像目录失败：{ex.Message}");
        }

        var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        return string.IsNullOrEmpty(videos) ? null : videos;
    }
}
