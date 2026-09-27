using OBS_Helper.Wpf.Localization;
namespace OBS_Helper.Wpf.Services.Audio;

/// <summary>一个音频端点（播放 / 录音设备）的采样率信息。</summary>
public sealed record AudioDeviceInfo(string Name, int SampleRateHz, bool IsRender);

/// <summary>音频采样率体检结论。</summary>
public sealed class SampleRateCheckItem
{
    /// <summary>"ok" / "warn" / "info"。</summary>
    public string Status { get; init; } = "ok";
    public required string Title { get; init; }
    public required string Detail { get; init; }
    /// <summary>命中问题时关联的知识库条目 id。</summary>
    public string? ProblemId { get; init; }
}

/// <summary>
/// 音频采样率体检核心（纯逻辑，供单元测试）。
///
/// 规则：OBS 与系统共享模式的采样率应统一为 48kHz。
/// - OBS 侧非 48k → 警告（重采样伪影、爆音、漂移的典型根因）；
/// - 任一活动设备共享模式为 44.1k 且 OBS 为 48k → 提示改哪一端；
/// - 全部一致 → 通过。
/// </summary>
public static class SampleRateCheckCore
{
    public const int TargetRate = 48000;

    /// <param name="obsRateHz">OBS 设置的采样率；null 表示未在配置中找到（默认 48k）。</param>
    /// <param name="devices">系统活动音频设备的共享模式采样率列表（可为空 = 枚举失败）。</param>
    public static List<SampleRateCheckItem> Evaluate(int? obsRateHz, IReadOnlyList<AudioDeviceInfo> devices)
    {
        var items = new List<SampleRateCheckItem>();
        var obsRate = obsRateHz ?? TargetRate;

        // ---- OBS 侧 ----
        if (obsRate == TargetRate)
        {
            items.Add(new SampleRateCheckItem
            {
                Status = "ok",
                Title = Strings.T("samplerate.obs.title"),
                Detail = Strings.T("samplerate.obs.ok")
            });
        }
        else
        {
            items.Add(new SampleRateCheckItem
            {
                Status = "warn",
                Title = Strings.T("samplerate.obs.warnTitle"),
                Detail = Strings.T("samplerate.obs.warnDetail", obsRate),
                ProblemId = "au-sample-mismatch"
            });
        }

        // ---- 系统设备侧 ----
        if (devices.Count == 0)
        {
            items.Add(new SampleRateCheckItem
            {
                Status = "info",
                Title = Strings.T("samplerate.devices.title"),
                Detail = Strings.T("samplerate.devices.unknown")
            });
            return items;
        }

        var mismatched = devices.Where(d => d.SampleRateHz != TargetRate).ToList();
        if (mismatched.Count == 0)
        {
            items.Add(new SampleRateCheckItem
            {
                Status = "ok",
                Title = Strings.T("samplerate.devices.title"),
                Detail = Strings.T("samplerate.devices.ok", devices.Count, TargetRate / 1000)
            });
            return items;
        }

        var names = string.Join("、", mismatched.Select(d => $"{d.Name}({d.SampleRateHz / 1000}kHz)"));
        items.Add(new SampleRateCheckItem
        {
            Status = "warn",
            Title = Strings.T("samplerate.devices.warnTitle", mismatched.Count),
            Detail = Strings.T("samplerate.devices.warnDetail", names),
            ProblemId = "au-sample-mismatch"
        });

        return items;
    }
}