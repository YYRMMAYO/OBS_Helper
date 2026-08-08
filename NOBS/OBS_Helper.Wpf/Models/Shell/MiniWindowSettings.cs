using System.Text.Json.Serialization;

namespace OBS_Helper.Wpf.Models.Shell;

/// <summary>迷你小窗的位置记忆（非敏感，存 <c>prefs.json</c>，便于多屏 / 换分辨率后仍能找回）。</summary>
public sealed class MiniWindowSettings
{
    [JsonPropertyName("x")] public double X { get; set; } = double.NaN;
    [JsonPropertyName("y")] public double Y { get; set; } = double.NaN;
}
