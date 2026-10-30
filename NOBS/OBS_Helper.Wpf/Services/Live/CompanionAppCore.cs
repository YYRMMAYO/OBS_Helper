using System.IO;
using OBS_Helper.Wpf.Models.Live;

namespace OBS_Helper.Wpf.Services.Live;

/// <summary>第三方客户端的探测结果。</summary>
public sealed record CompanionStatus(CompanionApp App, bool Running, bool Installed, string? InstallPath);

/// <summary>
/// 第三方直播客户端 / 追踪软件的探测规则（V3.3.0 / P0-4）——纯逻辑。
///
/// 只做只读探测（进程名 / 安装目录 / 注册表卸载项三路交叉），不启动、不注入、不读账号数据。
/// 三路信号只要有一路命中就认为「装过」，但「在运行」必须以进程为准。
/// </summary>
public static class CompanionAppCore
{
    /// <summary>展开 %ProgramFiles% 这类环境变量；展开失败时原样返回。</summary>
    public static string ExpandPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        try
        {
            return Environment.ExpandEnvironmentVariables(path.Trim());
        }
        catch (Exception)
        {
            return path.Trim();
        }
    }

    /// <summary>进程名是否匹配（不区分大小写，忽略 .exe 后缀）。</summary>
    public static bool MatchesProcess(string? processName, CompanionApp? app)
    {
        if (app is null || string.IsNullOrWhiteSpace(processName)) return false;
        var name = processName.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];

        foreach (var candidate in app.ProcessNames)
        {
            var c = candidate.Trim();
            if (c.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) c = c[..^4];
            if (string.Equals(name, c, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>注册表卸载项显示名是否匹配（允许「名称 + 版本号」这种带后缀的写法）。</summary>
    public static bool MatchesUninstallName(string? displayName, CompanionApp? app)
    {
        if (app is null || string.IsNullOrWhiteSpace(displayName)) return false;
        var name = displayName.Trim();

        foreach (var candidate in app.UninstallNames)
        {
            if (candidate.Length == 0) continue;
            if (name.Contains(candidate, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>在候选安装目录里找第一个真实存在的目录；都没有返回 null。</summary>
    public static string? FindInstallDirectory(CompanionApp? app, Func<string, bool>? exists = null)
    {
        if (app is null) return null;
        exists ??= Directory.Exists;

        foreach (var raw in app.InstallDirectories)
        {
            var path = ExpandPath(raw);
            if (path.Length == 0) continue;
            try
            {
                if (exists(path)) return path;
            }
            catch (Exception)
            {
                // 无权限访问的目录直接跳过
            }
        }
        return null;
    }

    /// <summary>汇总三路信号（调用方负责真正枚举进程 / 读注册表）。</summary>
    public static CompanionStatus Combine(CompanionApp app, bool running, bool registryHit, string? installPath)
        => new(app, running, running || registryHit || !string.IsNullOrEmpty(installPath), installPath);

    /// <summary>平台模式是否支持「虚拟摄像头」路径。</summary>
    public static bool SupportsVirtualCamera(LivePlatform? platform)
        => platform is not null
           && GoLiveCore.ParseMode(platform.Mode) is GoLiveMode.VirtualCamera or GoLiveMode.Direct;

    /// <summary>
    /// 平台侧的操作提示 key（图文回退用）：不写死按钮路径，只描述「要做什么」。
    /// 具体入口随平台版本变化，界面同时给官方帮助链接。
    /// </summary>
    public static string VirtualCameraGuideKey(LivePlatform? platform)
        => platform?.Id switch
        {
            "douyin" => "golive.guide.douyin",
            "tiktok" => "golive.guide.tiktok",
            "bilibili" => "golive.guide.bilibili",
            _ => "golive.guide.generic"
        };
}
