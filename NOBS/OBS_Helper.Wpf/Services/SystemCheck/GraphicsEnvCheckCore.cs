using OBS_Helper.Wpf.Localization;
namespace OBS_Helper.Wpf.Services.SystemCheck;

/// <summary>一块显卡的驱动信息（来自显示适配器类注册表项）。</summary>
public sealed record GpuDriverInfo(string Name, string Version, string Date);

/// <summary>
/// 系统图形环境快照：全部由只读探测填充；探测失败的项保持 null（= 未知，不参与判定）。
/// </summary>
public sealed class GraphicsEnvSnapshot
{
    /// <summary>硬件加速 GPU 计划（HAGS）：注册表 HwSchMode（1=关 2=开）。</summary>
    public int? HwSchMode { get; init; }

    /// <summary>Windows 图形首选项中 obs64.exe 的 GPU 绑定值："2;"=高性能 "1;"=省电 null=未设置。</summary>
    public string? ObsGpuPreference { get; init; }

    /// <summary>Game DVR 后台录制开关（AppCaptureEnabled / GameDVR_Enabled 合成结论）。</summary>
    public bool? GameDvrEnabled { get; init; }

    /// <summary>游戏模式开关（AllowAutoGameMode）；null = 未显式设置（新版默认开启）。</summary>
    public bool? GameModeEnabled { get; init; }

    /// <summary>显卡列表与驱动版本 / 日期。</summary>
    public List<GpuDriverInfo> Gpus { get; init; } = new();

    /// <summary>当前电源计划名称（powercfg 解析失败为 null）。</summary>
    public string? ActivePowerScheme { get; init; }

    /// <summary>是否正在使用电池供电。</summary>
    public bool? OnBattery { get; init; }

    /// <summary>本工具进程是否以管理员身份运行（OBS 通常同权限启动，可作参考）。</summary>
    public bool? Elevated { get; init; }
}

/// <summary>
/// 黑屏专项体检核心（纯逻辑，供单元测试）。GAP-2 + GAP-8。
///
/// 社区标准黑屏排查链的可程序化部分：
/// 管理员权限 → GPU 偏好 → HAGS → Game DVR / 游戏模式 → 驱动版本日期 → 电源与供电。
/// 每项给出「ok / warn / info」三档结论与修复指引；写入类操作一律以 ms-settings: 跳转替代直接改注册表。
/// </summary>
public static class GraphicsEnvCheckCore
{
    /// <summary>驱动超过该月数视为「较旧」，给提示（不做硬性警告——很多老驱动跑得好好的）。</summary>
    public const int DriverAgeWarnMonths = 18;

    public static List<EnvCheckItem> Evaluate(GraphicsEnvSnapshot s)
    {
        var items = new List<EnvCheckItem>();

        // ---- 管理员权限（info 参考项：OBS 多数情况跟随用户启动方式）----
        items.Add(s.Elevated == true
            ? new EnvCheckItem("ok", Strings.T("graphics.admin.okTitle"), Strings.T("graphics.admin.okDetail"))
            : new EnvCheckItem("info", Strings.T("graphics.admin.noneTitle"), Strings.T("graphics.admin.noneDetail")));

        // ---- HAGS ----
        items.Add(s.HwSchMode switch
        {
            2 => new EnvCheckItem("info", Strings.T("graphics.hags.onTitle"), Strings.T("graphics.hags.onDetail")),
            1 => new EnvCheckItem("ok", Strings.T("graphics.hags.offTitle"), Strings.T("graphics.hags.offDetail")),
            _ => new EnvCheckItem("info", Strings.T("graphics.hags.unknownTitle"), Strings.T("graphics.hags.unknownDetail"))
        });

        // ---- obs64.exe GPU 偏好 ----
        var integratedCount = s.Gpus.Count(g => IsIntegratedName(g.Name));
        var hasDiscrete = s.Gpus.Any(g => IsDiscreteName(g.Name));
        var dualGpu = integratedCount > 0 && hasDiscrete;
        items.Add(s.ObsGpuPreference switch
        {
            "2;" => new EnvCheckItem("ok", Strings.T("graphics.gpuPref.highTitle"), Strings.T("graphics.gpuPref.highDetail")),
            "1;" => new EnvCheckItem("warn", Strings.T("graphics.gpuPref.powerSavingTitle"), Strings.T("graphics.gpuPref.powerSavingDetail")),
            null when dualGpu => new EnvCheckItem("warn", Strings.T("graphics.gpuPref.dualNoPrefTitle"), Strings.T("graphics.gpuPref.dualNoPrefDetail", s.Gpus.Count)),
            null => new EnvCheckItem("ok", Strings.T("graphics.gpuPref.noneTitle"), Strings.T("graphics.gpuPref.noneDetail")),
            var v => new EnvCheckItem("info", Strings.T("graphics.gpuPref.unknownTitle"), Strings.T("graphics.gpuPref.unknownDetail", v))
        });

        // ---- Game DVR 后台录制 ----
        items.Add(s.GameDvrEnabled switch
        {
            true => new EnvCheckItem("warn", Strings.T("graphics.gameDvr.onTitle"), Strings.T("graphics.gameDvr.onDetail")),
            false => new EnvCheckItem("ok", Strings.T("graphics.gameDvr.offTitle"), Strings.T("graphics.gameDvr.offDetail")),
            _ => new EnvCheckItem("info", Strings.T("graphics.gameDvr.unknownTitle"), Strings.T("graphics.gameDvr.unknownDetail"))
        });

        // ---- 游戏模式 ----
        items.Add(s.GameModeEnabled switch
        {
            false => new EnvCheckItem("info", Strings.T("graphics.gameMode.offTitle"), Strings.T("graphics.gameMode.offDetail")),
            true => new EnvCheckItem("ok", Strings.T("graphics.gameMode.onTitle"), Strings.T("graphics.gameMode.onDetail")),
            _ => new EnvCheckItem("info", Strings.T("graphics.gameMode.unknownTitle"), Strings.T("graphics.gameMode.unknownDetail"))
        });

        // ---- 显卡驱动 ----
        if (s.Gpus.Count == 0)
        {
            items.Add(new EnvCheckItem("info", Strings.T("graphics.driver.noWmiTitle"), Strings.T("graphics.driver.noWmiDetail")));
        }
        else
        {
            foreach (var gpu in s.Gpus)
            {
                var ageMonths = TryParseDriverAgeMonths(gpu.Date);
                if (ageMonths is null)
                {
                    items.Add(new EnvCheckItem("info", Strings.T("graphics.driver.unparsedTitle", gpu.Name),
                        Strings.T("graphics.driver.unparsedDetail", gpu.Version, gpu.Date)));
                }
                else if (ageMonths >= DriverAgeWarnMonths)
                {
                    items.Add(new EnvCheckItem("warn", Strings.T("graphics.driver.oldTitle", gpu.Name),
                        Strings.T("graphics.driver.oldDetail", FormatDriverDate(gpu.Date), ageMonths)));
                }
                else
                {
                    items.Add(new EnvCheckItem("ok", Strings.T("graphics.driver.okTitle", gpu.Name),
                        Strings.T("graphics.driver.okDetail", gpu.Version, FormatDriverDate(gpu.Date))));
                }
            }
        }

        // ---- 电源计划与供电（GAP-8）----
        if (s.OnBattery == true)
        {
            items.Add(new EnvCheckItem("warn", Strings.T("graphics.power.batteryTitle"), Strings.T("graphics.power.batteryDetail")));
        }
        else if (s.OnBattery is not null)
        {
            items.Add(new EnvCheckItem("ok", Strings.T("graphics.power.acTitle"), Strings.T("graphics.power.acDetail")));
        }

        items.Add(s.ActivePowerScheme is { Length: > 0 } scheme
            ? new EnvCheckItem("info", Strings.T("graphics.power.planTitle"), Strings.T("graphics.power.planDetail", scheme))
            : new EnvCheckItem("info", Strings.T("graphics.power.planTitle"), Strings.T("graphics.power.planUnknown")));
        return items;
    }

    /// <summary>WMI DriverDate 形如 "20240311000000.000000+480"，取前 8 位 yyyyMMdd 计算距今月数。</summary>
    public static int? TryParseDriverAgeMonths(string? rawDate)
    {
        if (string.IsNullOrWhiteSpace(rawDate) || rawDate.Length < 8) return null;
        if (!int.TryParse(rawDate[..4], out var y) ||
            !int.TryParse(rawDate.Substring(4, 2), out var mo) ||
            !int.TryParse(rawDate.Substring(6, 2), out var d))
        {
            return null;
        }
        try
        {
            var date = new DateTime(y, mo, d);
            var now = DateTime.Today;
            if (date > now) return 0;
            var months = (now.Year - date.Year) * 12 + now.Month - date.Month;
            if (now.Day < d) months--;
            return Math.Max(0, months);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>"20240311..." → "2024-03-11"；解析失败原样返回。</summary>
    public static string FormatDriverDate(string rawDate)
        => rawDate.Length >= 8 && int.TryParse(rawDate[..8], out var compact)
            ? $"{compact / 10000:0000}-{compact / 100 % 100:00}-{compact % 100:00}"
            : rawDate;

    // 与日志分析器 LOG-GPU-HYBRID 同源的命名特征（此处独立维护，避免 UI 层反向依赖分析器内部）
    internal static bool IsIntegratedName(string name) =>
        !string.IsNullOrEmpty(name) &&
        (name.Contains("Intel(R) UHD", StringComparison.OrdinalIgnoreCase) ||
         name.Contains("Intel(R) HD Graphics", StringComparison.OrdinalIgnoreCase) ||
         name.Contains("Iris", StringComparison.OrdinalIgnoreCase) ||
         name.Contains("Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase));

    internal static bool IsDiscreteName(string name) =>
        !string.IsNullOrEmpty(name) &&
        (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
         name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
         name.Contains("Quadro", StringComparison.OrdinalIgnoreCase) ||
         name.Contains("Radeon RX", StringComparison.OrdinalIgnoreCase) ||
         name.Contains("Arc", StringComparison.OrdinalIgnoreCase));
}