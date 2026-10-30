using System.Text.Json.Nodes;

namespace OBS_Helper.Wpf.Models.Live;

/// <summary>校验通过的 Live2D 模型（V3.3.0）。</summary>
public sealed class Live2DModelInfo
{
    /// <summary>用户选择的 .model3.json 绝对路径。</summary>
    public string ModelJsonPath { get; init; } = "";

    /// <summary>模型根目录（.model3.json 所在目录）。</summary>
    public string RootDirectory { get; init; } = "";

    /// <summary>展示名（目录名去掉扩展名；取不到时用文件名）。</summary>
    public string Name { get; init; } = "";

    /// <summary>.moc3 绝对路径。</summary>
    public string MocPath { get; init; } = "";

    /// <summary>纹理绝对路径列表。</summary>
    public IReadOnlyList<string> Textures { get; init; } = Array.Empty<string>();

    /// <summary>引用到的其它文件（物理 / 动作 / 表情 / 姿势 / 音频）。</summary>
    public IReadOnlyList<string> ReferencedFiles { get; init; } = Array.Empty<string>();

    /// <summary>目录清单 SHA-256（sha256 of sorted relative path + content）。</summary>
    public string ManifestHash { get; init; } = "";

    /// <summary>目录总字节数。</summary>
    public long TotalBytes { get; init; }

    /// <summary>文件总数。</summary>
    public int FileCount { get; init; }
}

/// <summary>Live2D 布局预设（V3.3.0）：只描述几何，不依赖 OBS / WPF。</summary>
public sealed class Live2DLayout
{
    public string Id { get; init; } = "";
    public string TitleKey { get; init; } = "";

    /// <summary>占画布宽度的比例（0~1）。</summary>
    public double WidthRatio { get; init; }

    /// <summary>占画布高度的比例（0~1）。</summary>
    public double HeightRatio { get; init; }

    /// <summary>水平位置：0 = 左，0.5 = 居中，1 = 右。</summary>
    public double AlignX { get; init; }

    /// <summary>垂直位置：0 = 顶，0.5 = 居中，1 = 底。</summary>
    public double AlignY { get; init; }

    /// <summary>底部留白（占画布高度比例），常用于「半身站在底部」。</summary>
    public double BottomInset { get; init; }
}

/// <summary>按画布算出的变换（对应 obs-websocket 的 sceneItemTransform 字段）。</summary>
public sealed record Live2DTransform(
    double PosX, double PosY, double Width, double Height, string BoundsType = "OBS_BOUNDS_SCALE_INNER");

/// <summary>VTS 模型条目（ModelsRequest 的 availableModels 之一）。</summary>
public sealed class VtsModel
{
    public string ModelId { get; init; } = "";
    public string ModelName { get; init; } = "";
    public string VtsModelName { get; init; } = "";
    public string ModelFileName { get; init; } = "";
}

/// <summary>Live2D 采集方式候选（V3.3.0）。</summary>
public enum Live2DCaptureKind
{
    Ndi,
    Spout2,
    VirtualCamera,
    WindowCapture,
    None
}

/// <summary>采集方案：输入类型 + 设备 / 窗口信息。</summary>
public sealed class Live2DCaptureChoice
{
    public Live2DCaptureKind Method { get; init; }
    public string InputKind { get; init; } = "";

    /// <summary>DirectShow 设备 id（虚拟摄像头）或窗口名（窗口采集）。</summary>
    public string? Device { get; init; }

    /// <summary>音频是否另走麦克风（始终 true：不从 VTS / NDI 再采一路，避免双声）。</summary>
    public bool AudioFromMicrophoneOnly => true;

    public JsonObject BuildSettings() => new();
}
