using System.IO;
using OBS_Helper.Wpf.Services.Obs;

namespace OBS_Helper.Wpf.Services.ObsConfig;

/// <summary>一次色彩体检的结果。</summary>
public sealed class ColorCheckResult
{
    /// <summary>true=成功读取并评估；false=未能读取 OBS 配置。</summary>
    public bool Ok { get; init; }
    /// <summary>失败时的可读原因。</summary>
    public string Message { get; init; } = "";
    public List<ColorCheckItem> Items { get; init; } = new();
}

/// <summary>
/// 色彩设置体检服务（V2.7 工具箱，只读）。
///
/// 沿用录前自检的配置定位链路（global.ini → 当前 Profile → basic.ini），
/// 把色彩三件套（范围 / 空间 / 格式）交给 <see cref="ColorCheckCore"/> 评估。
/// 绝不修改任何 OBS 配置；任何失败都降级为可读提示。
/// </summary>
public sealed class ColorCheckService
{
    private readonly ObsPathService _paths;

    public ColorCheckService(ObsPathService paths) => _paths = paths;

    public async Task<ColorCheckResult> RunAsync()
    {
        try
        {
            var loc = await _paths.LocateAsync().ConfigureAwait(false);
            if (!loc.Exists)
            {
                return new ColorCheckResult
                {
                    Ok = false,
                    Message = "未找到 OBS 配置目录。若为自定义安装，请先在「设置 → OBS 配置管理」手动指定目录后重试。"
                };
            }

            var globalIniText = TryRead(Path.Combine(loc.ConfigDir, "global.ini"));
            var profileDir = PreflightCheckCore.ParseIni(globalIniText ?? "")
                .TryGetValue("basic.profiledir", out var dir) ? dir : null;
            if (string.IsNullOrWhiteSpace(profileDir))
            {
                return new ColorCheckResult
                {
                    Ok = false,
                    Message = "global.ini 中没有 Profile 记录（OBS 可能从未保存过设置）；先在 OBS 里随便改一项设置并关闭后重试。"
                };
            }

            var basicIniText = TryRead(Path.Combine(
                loc.ConfigDir, "basic", "profiles", profileDir!, "basic.ini"));
            if (basicIniText is null)
            {
                return new ColorCheckResult
                {
                    Ok = false,
                    Message = $"找不到 Profile「{profileDir}」的 basic.ini，无法检查色彩设置。"
                };
            }

            return new ColorCheckResult
            {
                Ok = true,
                Items = ColorCheckCore.Evaluate(PreflightCheckCore.ParseIni(basicIniText))
            };
        }
        catch (Exception ex)
        {
            FileLogger.Warn("ColorCheck", $"色彩体检异常：{ex.Message}");
            return new ColorCheckResult
            {
                Ok = false,
                Message = $"色彩体检过程出现异常：{ex.Message}。请稍后重试。"
            };
        }
    }

    private static string? TryRead(string file)
    {
        try { return File.Exists(file) ? File.ReadAllText(file) : null; }
        catch (Exception) { return null; }
    }
}
