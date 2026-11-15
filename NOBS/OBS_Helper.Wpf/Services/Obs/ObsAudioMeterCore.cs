using System.Text.Json;

namespace OBS_Helper.Wpf.Services.Obs;

/// <summary>一路音频输入的电平快照（V3.6.0 / G05）。</summary>
public sealed record ObsAudioMeterLevel(string InputName, double Mul, double Db);

/// <summary>
/// <c>InputVolumeMeters</c> 事件解析纯逻辑（V3.6.0 / G05）。
///
/// obs-websocket 的该事件每秒会推送多次，服务层只做「解析 + 节流」，
/// 界面按 10 Hz 刷新即可；这里不依赖 WPF，单测直接喂 JSON。
/// </summary>
public static class ObsAudioMeterCore
{
    public static IReadOnlyList<ObsAudioMeterLevel> Parse(JsonElement data)
    {
        if (!data.TryGetProperty("inputs", out var inputs) || inputs.ValueKind != JsonValueKind.Array)
            return Array.Empty<ObsAudioMeterLevel>();

        var list = new List<ObsAudioMeterLevel>();
        foreach (var item in inputs.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var name = item.TryGetProperty("inputName", out var n) && n.ValueKind == JsonValueKind.String
                ? n.GetString() ?? ""
                : "";
            if (string.IsNullOrWhiteSpace(name)) continue;

            var mul = ReadDouble(item, "inputVolumeMul", 0);
            var db = ReadDouble(item, "inputVolumeDb", -100);
            if (double.IsNaN(mul) || double.IsInfinity(mul)) mul = 0;
            if (double.IsNaN(db) || double.IsInfinity(db)) db = -100;

            list.Add(new ObsAudioMeterLevel(name, Math.Clamp(mul, 0, 1), Math.Clamp(db, -100, 0)));
        }
        return list;
    }

    private static double ReadDouble(JsonElement item, string name, double fallback)
        => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
           && value.TryGetDouble(out var d)
            ? d
            : fallback;
}
