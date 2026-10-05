using System.Text.Json.Serialization;

namespace OBS_Helper.Wpf.Models.Shell;

/// <summary>托盘与后台行为设置。</summary>
public sealed class ShellSettings
{
    /// <summary>点主窗口关闭按钮时最小化到托盘而不是退出。</summary>
    [JsonPropertyName("closeToTray")] public bool CloseToTray { get; set; } = true;

    /// <summary>录制 / 推流状态开始或停止时弹出系统通知。</summary>
    [JsonPropertyName("notifyStateChange")] public bool NotifyStateChange { get; set; } = true;

    /// <summary>录制守护：断连 / 心跳超时 / 重连后录制丢失时强提醒（V2.8）。</summary>
    [JsonPropertyName("recordWatchdog")] public bool RecordWatchdogEnabled { get; set; } = true;

    /// <summary>实时日志尾随预警：直播中命中掉帧 / 过载等特征时托盘提醒（V2.8）。</summary>
    [JsonPropertyName("realtimeLogAlert")] public bool RealtimeLogAlertEnabled { get; set; } = true;

    /// <summary>
    /// 简单录像（V2.9.4）：文件通道落地后允许本工具自动拉起 OBS 并开始录制。
    /// 关掉时只改配置并提示用户自己启动 OBS —— 有些用户不希望工具去启动别的程序。
    /// </summary>
    [JsonPropertyName("simpleRecordAutoLaunch")] public bool SimpleRecordAutoLaunch { get; set; } = true;

    /// <summary>简单录像（V2.9.4）上次使用的预设键（quick / meeting / game）。</summary>
    [JsonPropertyName("simpleRecordPreset")] public string SimpleRecordPreset { get; set; } = "quick";
}
