using System.IO;
using OBS_Helper.Wpf.Services.ObsConfig;

namespace OBS_Helper.Wpf.Services.Obs;

/// <summary>
/// 录前 / 开播前自检服务（C1，只读）。
///
/// 包装 <see cref="PreflightCheckCore"/> 的纯检查逻辑：
/// 负责 OBS 配置目录定位、global.ini → 当前 Profile → basic.ini 的读取链路，
/// 以及「OBS 是否在运行」的环境信息项。所有磁盘操作均为只读。
/// </summary>
public sealed class PreflightCheckService
{
    private readonly ObsPathService _paths;

    public PreflightCheckService(ObsPathService paths) => _paths = paths;

    public async Task<PreflightReport> RunAsync()
    {
        var report = new PreflightReport();
        try
        {
            var loc = await _paths.LocateAsync().ConfigureAwait(false);

            string? globalIniText = null;
            string? basicIniText = null;
            if (loc.Exists)
            {
                globalIniText = TryRead(Path.Combine(loc.ConfigDir, "global.ini"));
                var profileDir = ReadProfileDir(globalIniText);
                if (!string.IsNullOrWhiteSpace(profileDir))
                {
                    var profileRoot = Path.Combine(loc.ConfigDir, "basic", "profiles", profileDir!);
                    basicIniText = TryRead(Path.Combine(profileRoot, "basic.ini"));
                }
            }

            // 环境信息：OBS 运行状态（提示级，不算问题）
            try
            {
                var proc = _paths.DetectProcess();
                report.Items.Add(new PreflightItem
                {
                    Title = "OBS 进程",
                    Status = PreflightStatus.Info,
                    Detail = proc.IsRunning
                        ? $"OBS 正在运行（{proc.Evidence}）改动设置后需重启 OBS 生效。"
                        : "OBS 未在运行；本检查读取的是磁盘上的配置。"
                });
            }
            catch (Exception) { }

            Dictionary<string, string>? globalIni = null;
            if (globalIniText is not null) globalIni = PreflightCheckCore.ParseIni(globalIniText);

            PreflightCheckCore.Run(report, loc.Exists, globalIni, basicIniText);
        }
        catch (Exception ex)
        {
            report.Items.Add(new PreflightItem
            {
                Title = "录前自检",
                Status = PreflightStatus.Fail,
                Detail = $"自检过程出现异常：{ex.Message}。请稍后重试。"
            });
        }

        return report;
    }

    /// <summary>从 global.ini 内容里取 [Basic] ProfileDir。</summary>
    private static string? ReadProfileDir(string? globalIniText)
    {
        if (string.IsNullOrEmpty(globalIniText)) return null;
        return PreflightCheckCore.ParseIni(globalIniText).TryGetValue("basic.profiledir", out var dir)
            ? dir
            : null;
    }

    private static string? TryRead(string file)
    {
        try { return File.Exists(file) ? File.ReadAllText(file) : null; }
        catch (Exception) { return null; }
    }
}
