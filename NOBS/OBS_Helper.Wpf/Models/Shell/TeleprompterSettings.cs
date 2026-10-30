using System.Text;
using System.Text.Json.Serialization;

namespace OBS_Helper.Wpf.Models.Shell;

/// <summary>提词器设置（V3.1.0 / §12.1）。持久化在 <c>prefs.json</c>，不含敏感内容。</summary>
public sealed class TeleprompterSettings
{
    [JsonPropertyName("fontSize")] public double FontSize { get; set; } = 42;
    [JsonPropertyName("lineHeight")] public double LineHeight { get; set; } = 1.45;
    [JsonPropertyName("speed")] public double Speed { get; set; } = 40;
    [JsonPropertyName("textColor")] public string TextColor { get; set; } = "#ffffff";
    [JsonPropertyName("background")] public string Background { get; set; } = "#000000";
    [JsonPropertyName("backgroundOpacity")] public double BackgroundOpacity { get; set; } = 0.72;
    [JsonPropertyName("sideMargin")] public double SideMargin { get; set; } = 80;
    [JsonPropertyName("mirrorH")] public bool MirrorHorizontal { get; set; }
    [JsonPropertyName("mirrorV")] public bool MirrorVertical { get; set; }
    [JsonPropertyName("clickThrough")] public bool ClickThrough { get; set; }
    [JsonPropertyName("topmost")] public bool Topmost { get; set; } = true;
    [JsonPropertyName("excludeFromCapture")] public bool ExcludeFromCapture { get; set; } = true;
    [JsonPropertyName("showGuide")] public bool ShowGuide { get; set; } = true;
    [JsonPropertyName("monitor")] public int Monitor { get; set; } = -1;
    [JsonPropertyName("left")] public double Left { get; set; } = double.NaN;
    [JsonPropertyName("top")] public double Top { get; set; } = double.NaN;
    [JsonPropertyName("width")] public double Width { get; set; } = 900;
    [JsonPropertyName("height")] public double Height { get; set; } = 320;
    /// <summary>当前滚动偏移（像素）。手机端遥控时由 PC 推送，避免两端漂移（V3.4.0 / M05）。</summary>
    [JsonPropertyName("offset")] public double Offset { get; set; }

    public TeleprompterSettings Clone() => (TeleprompterSettings)MemberwiseClone();
}
