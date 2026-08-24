namespace OBS_Helper.Wpf.Services;

/// <summary>
/// 系统侧体检的统一结论条目（黑屏体检 / 音频深度体检 / 虚拟摄像头体检共用）。
/// Status 四档：「ok」通过、「info」提示、「warn」建议处理、「error」必须处理。
/// </summary>
public sealed record EnvCheckItem(string Status, string Title, string Detail);
