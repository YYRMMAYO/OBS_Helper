using System.IO;
using OBS_Helper.Wpf.Services.Automation;
using OBS_Helper.Wpf.Services.Mobile;
using OBS_Helper.Wpf.Services.Obs;
using OBS_Helper.Wpf.Services.Streaming;

namespace OBS_Helper.Wpf.Services.Audio;

/// <summary>
/// AI 音频链（Q03）：在 OBS 输入上创建受控滤镜（降噪 / 压缩 / 增益），支持旁路 A/B。
/// 不动用户已有滤镜；只管理本应用命名的三个滤镜，可一键旁路。
/// </summary>
public sealed class AudioChainService
{
    private readonly ObsConnectionService _obs;

    public AudioChainService(ObsConnectionService obs) => _obs = obs;

    public static readonly string[] FilterNames = { "OBS助手·降噪", "OBS助手·压缩", "OBS助手·增益" };

    public async Task<(bool Ok, string Code)> ApplyAsync(string inputName, string presetId, bool bypass, CancellationToken ct = default)
    {
        if (!_obs.IsConnected) return (false, FeatureErrors.MobObsOffline);
        var preset = AudioChainCore.Presets.FirstOrDefault(p => p.Id == presetId) ?? AudioChainCore.Presets[0];
        var noise = preset.NoiseReductionDb > 0;
        var ok = true;
        ok &= await EnsureFilterAsync(inputName, FilterNames[0], "noise_suppress_filter_v2",
            new Dictionary<string, object?> { ["method"] = "rnnoise" }, ct).ConfigureAwait(false);
        ok &= await EnsureFilterAsync(inputName, FilterNames[1], "compressor_filter",
            new Dictionary<string, object?>
            {
                ["ratio"] = 3.0,
                ["threshold"] = -18.0,
                ["attack_time"] = 6,
                ["release_time"] = 60,
                ["output_gain"] = 0.0,
            }, ct).ConfigureAwait(false);
        ok &= await EnsureFilterAsync(inputName, FilterNames[2], "gain_filter",
            new Dictionary<string, object?> { ["db"] = preset.LoudnessLufs + 16 }, ct).ConfigureAwait(false);

        foreach (var name in FilterNames)
        {
            await _obs.RawRequestAsync("SetSourceFilterEnabled", new Dictionary<string, object?>
            {
                ["sourceName"] = inputName,
                ["filterName"] = name,
                ["filterEnabled"] = !bypass && (name != FilterNames[0] || noise),
            }, ct).ConfigureAwait(false);
        }
        return ok ? (true, "") : (false, FeatureErrors.ClipOutputFailed);
    }

    private async Task<bool> EnsureFilterAsync(string input, string name, string kind, Dictionary<string, object?> settings, CancellationToken ct)
    {
        var result = await _obs.RawRequestAsync("CreateSourceFilter", new Dictionary<string, object?>
        {
            ["sourceName"] = input,
            ["filterName"] = name,
            ["filterKind"] = kind,
            ["filterSettings"] = settings,
        }, ct).ConfigureAwait(false);
        if (!result.Ok)
        {
            var update = await _obs.RawRequestAsync("SetSourceFilterSettings", new Dictionary<string, object?>
            {
                ["sourceName"] = input,
                ["filterName"] = name,
                ["filterSettings"] = settings,
                ["overlay"] = true,
            }, ct).ConfigureAwait(false);
            return update.Ok;
        }
        return true;
    }
}
