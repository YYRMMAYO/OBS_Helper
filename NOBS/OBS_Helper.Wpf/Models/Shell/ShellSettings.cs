using System.Text.Json.Serialization;

namespace OBS_Helper.Wpf.Models.Shell;

/// <summary>托盘与后台行为设置。</summary>
public sealed class ShellSettings
{
    /// <summary>点主窗口关闭按钮时最小化到托盘而不是退出。</summary>
    [JsonPropertyName("closeToTray")] public bool CloseToTray { get; set; } = true;

    /// <summary>录制 / 推流状态开始或停止时弹出系统通知。</summary>
    [JsonPropertyName("notifyStateChange")] public bool NotifyStateChange { get; set; } = true;
}
