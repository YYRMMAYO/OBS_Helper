using System.Text.Json.Serialization;

namespace OBS_Helper.Wpf.Models.Shell;

/// <summary>场景自动切换配置。</summary>
public sealed class AutoSwitchSettings
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }

    [JsonPropertyName("rules")] public List<AutoSwitchRule> Rules { get; set; } = new();
}

/// <summary>
/// 一条自动切换规则：当前台窗口标题匹配 <see cref="Pattern"/> 时，把 OBS 切到 <see cref="SceneName"/>。
/// 按列表顺序匹配，第一条命中生效；规则之间用「去抖」避免连续重复切换。
/// </summary>
public sealed class AutoSwitchRule
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);

    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;

    /// <summary>匹配内容：窗口标题的关键词（Contains）或正则表达式（<see cref="UseRegex"/>）。</summary>
    [JsonPropertyName("pattern")] public string Pattern { get; set; } = "";

    [JsonPropertyName("useRegex")] public bool UseRegex { get; set; }

    /// <summary>命中后要切换到的 OBS 场景名。</summary>
    [JsonPropertyName("scene")] public string SceneName { get; set; } = "";

    public AutoSwitchRule Clone() => new()
    {
        Id = Id,
        Enabled = Enabled,
        Pattern = Pattern,
        UseRegex = UseRegex,
        SceneName = SceneName
    };
}
