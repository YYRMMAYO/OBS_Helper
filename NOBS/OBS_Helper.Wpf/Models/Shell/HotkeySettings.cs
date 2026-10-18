using System.Text.Json.Serialization;

namespace OBS_Helper.Wpf.Models.Shell;

/// <summary>
/// 全局热键配置。全部存 <c>prefs.json</c>（非敏感：只是键位组合，不含任何凭据）。
/// 默认启用 6 组快捷键：录制 / 推流 / 虚拟摄像头 / 存片 / 小窗 / 显示隐藏窗口。
/// </summary>
public sealed class HotkeySettings
{
    [JsonPropertyName("record")] public HotkeyBinding Record { get; set; } = new("R");
    [JsonPropertyName("recordEnabled")] public bool RecordEnabled { get; set; } = true;

    [JsonPropertyName("stream")] public HotkeyBinding Stream { get; set; } = new("S");
    [JsonPropertyName("streamEnabled")] public bool StreamEnabled { get; set; } = true;

    [JsonPropertyName("virtualCam")] public HotkeyBinding VirtualCam { get; set; } = new("C");
    [JsonPropertyName("virtualCamEnabled")] public bool VirtualCamEnabled { get; set; } = true;

    /// <summary>回放缓存存片（V3.0）：直播中「把刚才那段留下」最常用的入口。</summary>
    [JsonPropertyName("saveReplay")] public HotkeyBinding SaveReplay { get; set; } = new("V");
    [JsonPropertyName("saveReplayEnabled")] public bool SaveReplayEnabled { get; set; } = true;

    /// <summary>
    /// 录制中打点（V3.0 / D3）：把当前时间点标出来，停止后导出成章节。
    /// OBS 官方至今没有这个能力（issue #13567），默认 <c>Ctrl+Alt+K</c>（Marker）。
    /// </summary>
    [JsonPropertyName("markRecording")] public HotkeyBinding MarkRecording { get; set; } = new("K");
    [JsonPropertyName("markRecordingEnabled")] public bool MarkRecordingEnabled { get; set; } = true;

    [JsonPropertyName("miniWindow")] public HotkeyBinding MiniWindow { get; set; } = new("M");
    [JsonPropertyName("miniWindowEnabled")] public bool MiniWindowEnabled { get; set; } = true;

    [JsonPropertyName("toggleWindow")] public HotkeyBinding ToggleWindow { get; set; } = new("O");
    [JsonPropertyName("toggleWindowEnabled")] public bool ToggleWindowEnabled { get; set; } = true;

    // ---- 提词器（V3.1.0 / §12.1）----

    /// <summary>呼出 / 隐藏提词器，默认 <c>Ctrl+Alt+T</c>。</summary>
    [JsonPropertyName("teleprompterToggle")] public HotkeyBinding TeleprompterToggle { get; set; } = new("T");
    [JsonPropertyName("teleprompterToggleEnabled")] public bool TeleprompterToggleEnabled { get; set; } = true;

    /// <summary>提词器播放 / 暂停，默认 <c>Ctrl+Alt+P</c>。</summary>
    [JsonPropertyName("teleprompterPlayPause")] public HotkeyBinding TeleprompterPlayPause { get; set; } = new("P");
    [JsonPropertyName("teleprompterPlayPauseEnabled")] public bool TeleprompterPlayPauseEnabled { get; set; } = true;

    /// <summary>提词器点击穿透开关，默认 <c>Ctrl+Alt+L</c>（穿透后唯一的退出方式）。</summary>
    [JsonPropertyName("teleprompterClickThrough")] public HotkeyBinding TeleprompterClickThrough { get; set; } = new("L");
    [JsonPropertyName("teleprompterClickThroughEnabled")] public bool TeleprompterClickThroughEnabled { get; set; } = true;
}

/// <summary>
/// 一组键位：三个修饰键 + 一个主键（字母 / 数字 / F1-F12）。
/// <c>Win</c>（Windows 键）注册会触发 UAC 弹窗提示，默认不勾。
/// </summary>
public sealed class HotkeyBinding
{
    public HotkeyBinding() { }

    public HotkeyBinding(string key)
    {
        Key = key;
        Ctrl = true;
        Alt = true;
    }

    [JsonPropertyName("ctrl")] public bool Ctrl { get; set; }
    [JsonPropertyName("alt")] public bool Alt { get; set; }
    [JsonPropertyName("shift")] public bool Shift { get; set; }
    [JsonPropertyName("win")] public bool Win { get; set; }

    /// <summary>主键，如 A / 5 / F8。</summary>
    [JsonPropertyName("key")] public string Key { get; set; } = "";

    public HotkeyBinding Clone() => new()
    {
        Ctrl = Ctrl,
        Alt = Alt,
        Shift = Shift,
        Win = Win,
        Key = Key
    };

    /// <summary>显示名，如「Ctrl+Alt+R」。</summary>
    public string DisplayName
    {
        get
        {
            var parts = new List<string>();
            if (Ctrl) parts.Add("Ctrl");
            if (Alt) parts.Add("Alt");
            if (Shift) parts.Add("Shift");
            if (Win) parts.Add("Win");
            if (!string.IsNullOrWhiteSpace(Key)) parts.Add(Key.ToUpperInvariant());
            return string.Join("+", parts);
        }
    }
}
