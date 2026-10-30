using System.Text.Json.Nodes;
using OBS_Helper.Wpf.Models.Live;

namespace OBS_Helper.Wpf.Services.Live;

/// <summary>
/// Live2D 采集链路选择（V3.3.0 / P0-3）——纯逻辑。
///
/// 优先级与 PLAN_V3.3.0 §4.5 一致：
/// NDI → Spout2 → VTS 虚拟摄像头（DirectShow）→ 窗口采集 + 色键。
/// 每一级都要先确认 OBS 真的装了对应输入类型；装没装由调用方查询后传进来。
/// </summary>
public static class Live2DCaptureCore
{
    /// <summary>DirectShow 设备名里可能出现的 VTS 关键字（VTS 虚拟摄像头 / 采集卡 / OBS 自身）。</summary>
    private static readonly string[] CameraKeywords =
    {
        "vtube studio", "vtuber", "vtube", "obs virtual", "obs-camera"
    };

    /// <summary>判断某个 DirectShow 设备是不是 VTS / OBS 虚拟摄像头。</summary>
    public static bool LooksLikeVtsCamera(string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName)) return false;
        var name = deviceName.ToLowerInvariant();
        return CameraKeywords.Any(name.Contains);
    }

    /// <summary>
    /// 选一个采集方案。参数都来自 OBS 的实时查询结果：
    /// <paramref name="inputKinds"/> = <c>GetInputKindList</c>，
    /// <paramref name="dshowDevices"/> = 已有的 dshow 输入属性列表（拿不到就传空）。
    /// </summary>
    public static Live2DCaptureChoice Choose(IEnumerable<string>? inputKinds, IEnumerable<string>? dshowDevices)
    {
        var kinds = new HashSet<string>(inputKinds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        if (kinds.Contains("ndi_source"))
            return new Live2DCaptureChoice { Method = Live2DCaptureKind.Ndi, InputKind = "ndi_source" };

        if (kinds.Contains("spout2_source"))
            return new Live2DCaptureChoice { Method = Live2DCaptureKind.Spout2, InputKind = "spout2_source" };

        var camera = dshowDevices?.FirstOrDefault(LooksLikeVtsCamera);
        if (camera is not null && (kinds.Contains("dshow_input") || kinds.Contains("dshow_video_source")))
        {
            return new Live2DCaptureChoice
            {
                Method = Live2DCaptureKind.VirtualCamera,
                InputKind = kinds.Contains("dshow_input") ? "dshow_input" : "dshow_video_source",
                Device = camera
            };
        }

        // 没有设备名也允许建 dshow 源：用户在 OBS 里自己选摄像头，好过直接判定失败
        if (kinds.Contains("dshow_input"))
            return new Live2DCaptureChoice { Method = Live2DCaptureKind.VirtualCamera, InputKind = "dshow_input" };

        if (kinds.Contains("window_capture") || kinds.Contains("window_capture_2"))
        {
            return new Live2DCaptureChoice
            {
                Method = Live2DCaptureKind.WindowCapture,
                InputKind = kinds.Contains("window_capture") ? "window_capture" : "window_capture_2"
            };
        }

        return new Live2DCaptureChoice { Method = Live2DCaptureKind.None, InputKind = "" };
    }

    /// <summary>构造 CreateInput 的 inputSettings（只填已知字段，其余交给用户在 OBS 里补）。</summary>
    public static JsonObject BuildSettings(Live2DCaptureChoice? choice)
    {
        var settings = new JsonObject();
        if (choice is null) return settings;

        if (choice.Method == Live2DCaptureKind.VirtualCamera && !string.IsNullOrWhiteSpace(choice.Device))
            settings["video_device_id"] = choice.Device;

        if (choice.Method == Live2DCaptureKind.Ndi)
            settings["ndi_source_name"] = ""; // 用户在 OBS 属性里选源；不硬编码 VTS 的源名

        return settings;
    }

    /// <summary>采集不可用时给用户的出口（界面映射成文案键）。</summary>
    public static string UnavailableHintKey(Live2DCaptureChoice? choice)
        => choice?.Method switch
        {
            Live2DCaptureKind.Ndi => "live2d.capture.ndi",
            Live2DCaptureKind.Spout2 => "live2d.capture.spout",
            Live2DCaptureKind.VirtualCamera => "live2d.capture.vcam",
            Live2DCaptureKind.WindowCapture => "live2d.capture.window",
            _ => "live2d.capture.none"
        };

    /// <summary>把 Windows 进程名判断成「VTS 是否在运行」。</summary>
    public static bool IsVtsProcess(string? processName)
        => !string.IsNullOrWhiteSpace(processName)
           && processName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase)
               .Contains("VTube Studio", StringComparison.OrdinalIgnoreCase);
}
