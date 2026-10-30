namespace OBS_Helper.Wpf.Models.Live;

/// <summary>平台开播目录（V3.3.0）：<c>Assets/platforms.json</c> 的根对象。</summary>
public sealed class PlatformCatalogData
{
    public string Version { get; set; } = "";

    /// <summary>目录核对日期（YYYY-MM-DD）；平台入口会变，界面要如实展示这个日期。</summary>
    public string VerifiedAt { get; set; } = "";

    public List<LivePlatform> Platforms { get; set; } = new();
    public List<CompanionApp> Companions { get; set; } = new();
}

/// <summary>一个直播平台（V3.3.0）。</summary>
public sealed class LivePlatform
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>cn / global。</summary>
    public string Region { get; set; } = "";

    /// <summary>direct（OBS 直推 RTMP/RTMPS）/ virtualcam（平台客户端 + OBS 虚拟摄像头）/ manual（只给图文）。</summary>
    public string Mode { get; set; } = "";

    public string OfficialUrl { get; set; } = "";
    public string HelpUrl { get; set; } = "";
    public string Notes { get; set; } = "";
    public string StreamKeyHint { get; set; } = "";

    /// <summary>该条目最后一次从官方页面核对到的日期；空串 = 尚未核验。</summary>
    public string VerifiedAt { get; set; } = "";

    /// <summary>关联的第三方客户端（companions 里的 id）。</summary>
    public List<string> Companions { get; set; } = new();
}

/// <summary>第三方直播客户端 / 追踪软件（V3.3.0）。只做检测与官方入口，不静默安装。</summary>
public sealed class CompanionApp
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>进程名（不含 .exe）。</summary>
    public List<string> ProcessNames { get; set; } = new();

    /// <summary>安装目录候选（支持 %ProgramFiles% 这类环境变量）。</summary>
    public List<string> InstallDirectories { get; set; } = new();

    /// <summary>注册表「卸载」项显示名（用于判断是否安装过）。</summary>
    public List<string> UninstallNames { get; set; } = new();

    public string DownloadUrl { get; set; } = "";
    public string HelpUrl { get; set; } = "";
}
