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
            ? new EnvCheckItem("ok", "管理员权限",
                "本工具正以管理员身份运行。若 OBS 也以管理员启动，捕获全屏独占 / 反作弊游戏时不易黑屏。")
            : new EnvCheckItem("info", "管理员权限",
                "当前未以管理员运行。若捕获以管理员权限运行的游戏出现黑屏，请右键 OBS 与本工具「以管理员身份运行」。"));

        // ---- HAGS ----
        items.Add(s.HwSchMode switch
        {
            2 => new EnvCheckItem("info", "硬件加速 GPU 计划（HAGS）：已开启",
                "HAGS 在部分显卡驱动组合上会引发窗口 / 游戏捕获异常。画面正常无需理会；" +
                "若遇到黑屏或捕获卡顿，可在 设置 → 系统 → 显示 → 显卡 → 默认图形设置 中尝试关闭后重启。"),
            1 => new EnvCheckItem("ok", "硬件加速 GPU 计划（HAGS）：已关闭", "关闭状态兼容性最好，无需处理。"),
            _ => new EnvCheckItem("info", "硬件加速 GPU 计划（HAGS）：未能读取",
                "注册表读取受限，无法确认状态。可手动在 设置 → 系统 → 显示 → 显卡 → 默认图形设置 中核对。")
        });

        // ---- obs64.exe GPU 偏好 ----
        var integratedCount = s.Gpus.Count(g => IsIntegratedName(g.Name));
        var hasDiscrete = s.Gpus.Any(g => IsDiscreteName(g.Name));
        var dualGpu = integratedCount > 0 && hasDiscrete;
        items.Add(s.ObsGpuPreference switch
        {
            "2;" => new EnvCheckItem("ok", "OBS 的 GPU 偏好：高性能",
                "已在 Windows 图形设置中把 OBS 绑定到高性能 GPU，双显卡错位这一变量可以排除。"),
            "1;" => new EnvCheckItem("warn", "OBS 的 GPU 偏好被设成了「省电」",
                    "省电偏好会把 OBS 跑在核显上，常见后果是游戏捕获黑屏或掉帧。" +
                     "\n建议：设置 → 系统 → 显示 → 显卡 → 找到 obs64.exe → 改为「高性能」。"),
            null when dualGpu => new EnvCheckItem("warn", "双显卡环境，但未给 OBS 指定 GPU 偏好",
                $"检测到 {s.Gpus.Count} 块显卡（含核显 + 独显）。未指定时 Windows 会自行调度，可能落在核显上。" +
                "\n建议：设置 → 系统 → 显示 → 显卡 → 添加 obs64.exe → 选「高性能」。"),
            null => new EnvCheckItem("ok", "OBS 的 GPU 偏好：未指定",
                "单显卡或未检测到核显 + 独显组合，默认调度即可，无需处理。"),
            var v => new EnvCheckItem("info", "OBS 的 GPU 偏好", $"当前值为 {v}（非标准值），可在图形设置中重新指定一次。")
        });

        // ---- Game DVR 后台录制 ----
        items.Add(s.GameDvrEnabled switch
        {
            true => new EnvCheckItem("warn", "Xbox Game Bar 后台录制已开启",
                "后台录制会常驻占用编码资源，并可能与 OBS 的捕获钩子冲突（黑屏 / 掉帧的常见诱因之一）。" +
                "\n建议：设置 → 游戏 → 摄像 和「捕获」中关闭后台录制；也可直接打开 ms-settings:gamedvr 核对。"),
            false => new EnvCheckItem("ok", "Game DVR 后台录制已关闭", "不会与 OBS 抢占编码器，无需处理。"),
            _ => new EnvCheckItem("info", "Game DVR 状态未知", "注册表读取受限，可在 设置 → 游戏 → 摄像 中手动核对后台录制是否关闭。")
        });

        // ---- 游戏模式 ----
        items.Add(s.GameModeEnabled switch
        {
            false => new EnvCheckItem("info", "游戏模式已被关闭",
                "游戏模式会为前台游戏调度更多资源。直播推流场景下两种选择都成立：开着利于游戏帧率，关着利于 OBS 编码稳定；保持现状即可。"),
            true => new EnvCheckItem("ok", "游戏模式已开启", "正常状态。"),
            _ => new EnvCheckItem("info", "游戏模式：未显式设置", "保持系统默认即可；如遇游戏掉帧可到 设置 → 游戏 → 游戏模式 核对。")
        });

        // ---- 显卡驱动 ----
        if (s.Gpus.Count == 0)
        {
            items.Add(new EnvCheckItem("info", "显卡驱动信息", "未能从 WMI 读取显卡信息，请在设备管理器中核对驱动版本与日期。"));
        }
        else
        {
            foreach (var gpu in s.Gpus)
            {
                var ageMonths = TryParseDriverAgeMonths(gpu.Date);
                if (ageMonths is null)
                {
                    items.Add(new EnvCheckItem("info", $"驱动版本：{gpu.Name}",
                        $"版本 {gpu.Version}，日期 {gpu.Date}（未能解析日期，跳过新旧判定）。"));
                }
                else if (ageMonths >= DriverAgeWarnMonths)
                {
                    items.Add(new EnvCheckItem("warn", $"驱动较旧：{gpu.Name}",
                        $"驱动日期为 {FormatDriverDate(gpu.Date)}（约 {ageMonths} 个月前）。" +
                        "编码过载、NVENC 初始化失败、黑屏等问题有相当比例随新驱动修复。" +
                        "\n建议：到 NVIDIA / AMD / Intel 官网下载最新驱动；升级失败可先用 DDU 彻底清理后重装。"));
                }
                else
                {
                    items.Add(new EnvCheckItem("ok", $"驱动版本：{gpu.Name}",
                        $"版本 {gpu.Version}，日期 {FormatDriverDate(gpu.Date)}，较新，无需处理。"));
                }
            }
        }

        // ---- 电源计划与供电（GAP-8）----
        if (s.OnBattery == true)
        {
            items.Add(new EnvCheckItem("warn", "正在使用电池供电",
                "电池供电下 Windows 会限制 CPU/GPU 性能，编码欠载、「卡在正在停止录制」多与此有关。" +
                "\n建议：插上电源后再进行录制 / 推流。"));
        }
        else if (s.OnBattery is not null)
        {
            items.Add(new EnvCheckItem("ok", "供电状态", "已接通外部电源，性能不受电池策略限制。"));
        }

        items.Add(s.ActivePowerScheme is { Length: > 0 } scheme
            ? new EnvCheckItem("info", "电源计划", $"当前计划：「{scheme}」。笔记本省电计划可能导致编码欠载，录制卡顿时可在控制面板电源选项切换为高性能。")
            : new EnvCheckItem("info", "电源计划", "未能解析当前电源计划（不影响其他检查项）。"));

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
