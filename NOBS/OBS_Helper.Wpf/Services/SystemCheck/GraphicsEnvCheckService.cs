using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;

namespace OBS_Helper.Wpf.Services.SystemCheck;

/// <summary>
/// 黑屏专项体检服务（V2.8，GAP-2 + GAP-8，只读）。
///
/// 逐项探测系统图形环境并交给 <see cref="GraphicsEnvCheckCore"/> 判定：
/// HAGS / GPU 偏好 / Game DVR / 游戏模式与显卡驱动走注册表只读；
/// 电源计划解析 powercfg 输出；供电状态用 PowerStatus；管理员权限检查当前进程令牌。
///
/// 全程不写任何注册表、不改任何系统设置；读取失败一律降级为「未知」，绝不抛异常。
/// </summary>
public static class GraphicsEnvCheckService
{
    /// <summary>执行全部探测并返回体检结论。探测在后台线程进行（WMI / powercfg 有阻塞 IO）。</summary>
    public static async Task<List<EnvCheckItem>> RunAsync()
    {
        var snapshot = await Task.Run(CollectSnapshot).ConfigureAwait(true);
        return GraphicsEnvCheckCore.Evaluate(snapshot);
    }

    internal static GraphicsEnvSnapshot CollectSnapshot()
    {
        List<GpuDriverInfo>? gpus = null;
        string? powerScheme = null;
        try { gpus = ProbeGpus(); } catch (Exception) { }
        try { powerScheme = ProbeActivePowerScheme(); } catch (Exception) { }

        bool? onBattery = null;
        try { onBattery = ProbeOnBattery(); }
        catch (Exception) { }

        return new GraphicsEnvSnapshot
        {
            HwSchMode = ProbeHwSchMode(),
            ObsGpuPreference = ProbeObsGpuPreference(),
            GameDvrEnabled = ProbeGameDvr(),
            GameModeEnabled = ProbeGameMode(),
            Gpus = gpus ?? new List<GpuDriverInfo>(),
            ActivePowerScheme = powerScheme,
            OnBattery = onBattery,
            Elevated = ProbeElevated()
        };
    }

    /// <summary>台式机（无电池）恒为外部供电；笔记本按电源线状态判断，未知也视为外部供电。</summary>
    internal static bool? ProbeOnBattery()
    {
        var status = System.Windows.Forms.SystemInformation.PowerStatus;
        if (status.BatteryChargeStatus == System.Windows.Forms.BatteryChargeStatus.NoSystemBattery) return false;
        return status.PowerLineStatus switch
        {
            System.Windows.Forms.PowerLineStatus.Offline => true,
            _ => false
        };
    }

    /// <summary>HAGS：HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\HwSchMode。</summary>
    internal static int? ProbeHwSchMode()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
            return k?.GetValue("HwSchMode") is int v ? v : null;
        }
        catch (Exception) { return null; }
    }

    /// <summary>obs64.exe 的 GPU 偏好：HKCU\Software\Microsoft\DirectX\UserGpuPreferences。</summary>
    internal static string? ProbeObsGpuPreference()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences");
            return k?.GetValue("obs64.exe") as string;
        }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// Game DVR 后台录制合成结论：HKCU AppCaptureEnabled 与 HKCU GameConfigStore\GameDVR_Enabled，
    /// 任一确认关闭且无相反开关 → 关闭；任一确认开启 → 开启；读不到 → null。
    /// </summary>
    internal static bool? ProbeGameDvr()
    {
        try
        {
            var appCapture = ReadDword(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled");
            var gameDvr = ReadDword(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled");
            if (appCapture == 0 && gameDvr != 1) return false;
            if (gameDvr == 0 && appCapture != 1) return false;
            if (appCapture == 1 || gameDvr == 1) return true;
            return null;
        }
        catch (Exception) { return null; }
    }

    internal static bool? ProbeGameMode()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\GameBar");
            return k?.GetValue("AllowAutoGameMode") is int v ? v != 0 : null;
        }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// 显卡驱动信息：枚举显示适配器类注册表（DriverDesc / DriverVersion / DriverDate）。
    /// 不用 WMI：System.Management 在本工程的零 NuGet 依赖约束下不可用，注册表同样只读可靠。
    /// DriverDate 为 REG_BINARY FILETIME，转成 yyyyMMdd 字符串交给核心解析。
    /// </summary>
    internal static List<GpuDriverInfo> ProbeGpus()
    {
        const string classKey =
            @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
        var result = new List<GpuDriverInfo>();
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(classKey);
            if (root is null) return result;

            foreach (var sub in root.GetSubKeyNames())
            {
                if (!sub.StartsWith("0", StringComparison.Ordinal)) continue;
                try
                {
                    using var k = root.OpenSubKey(sub!);
                    if (k?.GetValue("DriverDesc") is not string desc || desc.Length == 0) continue;
                    if (desc.StartsWith("HDA", StringComparison.OrdinalIgnoreCase)) continue; // 声卡不在显示类里，保险跳过

                    var version = k.GetValue("DriverVersion") as string ?? "";
                    var date = k.GetValue("DriverDate") is byte[] ft && ft.Length == 8
                        ? FileTimeToDateString(ft)
                        : "";
                    result.Add(new GpuDriverInfo(desc, version, date));
                }
                catch (Exception) { }
            }
        }
        catch (Exception) { }
        return result;
    }

    private static string FileTimeToDateString(byte[] fileTime)
    {
        try
        {
            var ft = BitConverter.ToInt64(fileTime, 0);
            var dt = DateTime.FromFileTime(ft);
            return $"{dt:yyyyMMdd}000000";
        }
        catch (Exception)
        {
            return "";
        }
    }

    /// <summary>解析 powercfg /getactivescheme 的「电源计划 GUID (名称)」行，返回显示名。</summary>
    internal static string? ProbeActivePowerScheme()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = "/getactivescheme",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                // powercfg 按控制台代码页输出中文计划名，GUI 进程需显式指定系统默认编码防乱码
                StandardOutputEncoding = System.Text.Encoding.Default,
                CreateNoWindow = true
            });
            if (p is null) return null;

            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);

            foreach (var line in output.Split('\n'))
            {
                var t = line.Trim();
                // 形如：电源计划 GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (平衡)
                var idx = t.IndexOf('(');
                if (!t.Contains(':') || idx <= 0 || !t.EndsWith(')')) continue;
                var name = t[(idx + 1)..^1].Trim();
                if (name.Length > 0) return name;
            }
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>当前进程是否以管理员令牌运行（OBS 一般由用户以同样方式启动，可作参考）。</summary>
    internal static bool? ProbeElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static int? ReadDword(RegistryKey root, string path, string value)
    {
        try
        {
            using var k = root.OpenSubKey(path);
            return k?.GetValue(value) is int v ? v : null;
        }
        catch (Exception) { return null; }
    }
}
