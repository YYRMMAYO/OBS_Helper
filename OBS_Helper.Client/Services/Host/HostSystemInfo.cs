using System.Text.Json.Serialization;

namespace OBS_Helper.Client.Services.Host;

/// <summary>宿主上报的本机系统环境信息（<c>system.info</c> 命令的返回结构）。</summary>
public sealed class HostSystemInfo
{
    /// <summary>平台标识：windows / macos。</summary>
    public string Platform { get; set; } = "";

    /// <summary>操作系统版本（如 10.0.22631 / 15.5）。</summary>
    public string OsVersion { get; set; } = "";

    /// <summary>操作系统构建号（Windows 特有，macOS 可为空）。</summary>
    public string OsBuild { get; set; } = "";

    /// <summary>硬件加速 GPU 调度（HAGS）是否开启（Windows 特有，macOS 恒为 false）。</summary>
    public bool HagsEnabled { get; set; }

    /// <summary>Windows 游戏模式（Game Mode）是否开启。</summary>
    public bool GameModeEnabled { get; set; }

    /// <summary>OBS 进程状态。</summary>
    public ObsProcessInfo Obs { get; set; } = new();

    /// <summary>本机 GPU 列表（用于双显卡识别）。</summary>
    public List<GpuInfo> Gpus { get; set; } = new();

    /// <summary>当前主要 GPU 名称（best-effort）。</summary>
    public string PrimaryGpu { get; set; } = "";

    /// <summary>录制盘剩余空间（GB）。</summary>
    public double RecordingDiskFreeGb { get; set; }

    /// <summary>录制盘总空间（GB）。</summary>
    public double RecordingDiskTotalGb { get; set; }
}

/// <summary>OBS 进程状态。</summary>
public sealed class ObsProcessInfo
{
    /// <summary>OBS 是否正在运行。</summary>
    public bool Running { get; set; }

    /// <summary>是否以管理员 / root 权限运行。</summary>
    public bool Elevated { get; set; }

    /// <summary>CPU 占用百分比（best-effort，可能取不到）。</summary>
    public double CpuPercent { get; set; }

    /// <summary>内存占用（MB）。</summary>
    public double MemoryMb { get; set; }

    /// <summary>OBS 版本（best-effort）。</summary>
    public string Version { get; set; } = "";
}

/// <summary>一块显卡的信息。</summary>
public sealed class GpuInfo
{
    public string Name { get; set; } = "";
    public string Vendor { get; set; } = "";
    public bool IsActive { get; set; }
}
