using System.Text.Json.Nodes;

namespace OBS_Helper.Wpf.Models.ObsConfig;

/// <summary>一套直播间场景模板（含画布与若干场景，每个场景含若干来源）。</summary>
public sealed class SceneTemplate
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Icon { get; set; } = "";
    /// <summary>是否为竖屏模板（切到 OBS 全局画布为 1080×1920）。</summary>
    public bool Portrait { get; set; }
    public string Notes { get; set; } = "";
    public CanvasSpec Canvas { get; set; } = new();
    public List<TemplateScene> Scenes { get; set; } = new();
}

public sealed class CanvasSpec
{
    public int BaseWidth { get; set; } = 1920;
    public int BaseHeight { get; set; } = 1080;
    public int OutputWidth { get; set; } = 1920;
    public int OutputHeight { get; set; } = 1080;
    public int FpsNumerator { get; set; } = 30;
    public int FpsDenominator { get; set; } = 1;
}

public sealed class TemplateScene
{
    public string Name { get; set; } = "";
    public List<TemplateSource> Sources { get; set; } = new();
}

public sealed class TemplateSource
{
    public string Name { get; set; } = "";
    /// <summary>首选来源类型（obs-websocket inputKind），如 text_gdiplus_v3 / image_source。</summary>
    public string InputKind { get; set; } = "";
    /// <summary>层级：0 最底，数字越大越靠上。</summary>
    public int ZOrder { get; set; }
    public bool Enabled { get; set; } = true;
    /// <summary>跨场景复用的输入（如麦克风 / 等待音乐）：第二次遇到走 CreateSceneItem 而非重新 CreateInput。</summary>
    public bool Shared { get; set; }
    /// <summary>首选类型不可用时依次尝试的回退类型。</summary>
    public List<string> FallbackKinds { get; set; } = new();
    /// <summary>透传给 obs-websocket CreateInput 的该来源设置（设备 / 文件 / URL 留空，落地后由用户补齐）。</summary>
    public JsonObject? Settings { get; set; }
    public TransformSpec? Transform { get; set; }
    /// <summary>落地后仍需用户在 OBS 里手动补齐的项（设备 / 文件 / URL 等）。</summary>
    public PlaceholderSpec? Placeholder { get; set; }
}

public sealed class TransformSpec
{
    public double? PosX { get; set; }
    public double? PosY { get; set; }
    public double? ScaleX { get; set; }
    public double? ScaleY { get; set; }
    /// <summary>obs-websocket 字符串枚举：OBS_BOUNDS_NONE / STRETCH / SCALE_INNER / SCALE_OUTER / TO_WIDTH / TO_HEIGHT / MAX_ONLY。</summary>
    public string? BoundsType { get; set; }
    public double? BoundsWidth { get; set; }
    public double? BoundsHeight { get; set; }
    /// <summary>对齐：0 正中，5 左上，6 右上，9 左下，10 右下 等。</summary>
    public int? Alignment { get; set; }
}

public sealed class PlaceholderSpec
{
    /// <summary>device | file | url | window | text</summary>
    public string Kind { get; set; } = "";
    public string Hint { get; set; } = "";
}
