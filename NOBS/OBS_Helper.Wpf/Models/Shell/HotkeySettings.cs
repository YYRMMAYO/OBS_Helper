using System.Text.Json.Serialization;

namespace OBS_Helper.Wpf.Models.Shell;

/// <summary>
/// 全局热键配置。全部存 <c>prefs.json</c>（非敏感：只是键位组合，不含任何凭据）。
/// 默认启用 4 组快捷键：录制 / 推流 / 虚拟摄像头 / 显示隐藏窗口。
/// </summary>
public sealed class HotkeySettings
{
    [JsonPropertyName("record")] public HotkeyBinding Record { get; set; } = new("R");
    [JsonPropertyName("recordEnabled")] public bool RecordEnabled { get; set; } = true;

    [JsonPropertyName("stream")] public HotkeyBinding Stream { get; set; } = new("S");
    [JsonPropertyName("streamEnabled")] public bool StreamEnabled { get; set; } = true;

    [JsonPropertyName("virtualCam")] public HotkeyBinding VirtualCam { get; set; } = new("C");
    [JsonPropertyName("virtualCamEnabled")] public bool VirtualCamEnabled { get; set; } = true;

    [JsonPropertyName("toggleWindow")] public HotkeyBinding ToggleWindow { get; set; } = new("O");
    [JsonPropertyName("toggleWindowEnabled")] public bool ToggleWindowEnabled { get; set; } = true;
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
