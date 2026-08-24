using System.IO;
using Microsoft.Win32;

namespace OBS_Helper.Wpf.Services.Audio;

/// <summary>
/// 音频设备深度体检服务（V2.8，GAP-3，只读）。
///
/// 探测项：麦克风隐私权限 / 通信 Ducking 策略走 HKCU 注册表；
/// 音频服务状态解析 sc query 输出；活动录音设备复用 MMDevices 枚举模式；
/// OBS 音频输入名从已连接的 websocket 快照取（未连接时为空列表，对应项降级为提示）。
///
/// 全程 try/catch 降级，绝不抛异常、绝不修改任何系统状态。
/// </summary>
public static class AudioDeviceHealthService
{
    /// <summary>执行全部探测并返回体检结论。探测在后台线程进行。</summary>
    public static async Task<List<EnvCheckItem>> RunAsync(IReadOnlyList<string> obsAudioInputs)
    {
        var snapshot = await Task.Run(() => CollectSnapshot(obsAudioInputs)).ConfigureAwait(true);
        return AudioDeviceHealthCore.Evaluate(snapshot);
    }

    internal static AudioDeviceHealthSnapshot CollectSnapshot(IReadOnlyList<string> obsAudioInputs)
    {
        return new AudioDeviceHealthSnapshot
        {
            MicGlobalConsent = ProbeMicConsent(),
            UserDuckingPolicy = ProbeDuckingPolicy(),
            AudiosrvRunning = IsServiceRunning("Audiosrv"),
            AudioEndpointBuilderRunning = IsServiceRunning("AudioEndpointBuilder"),
            ObsAudioInputs = obsAudioInputs.ToList(),
            CaptureDeviceNames = SampleRateCheckService.EnumerateDevices()
                .Where(d => !d.IsRender)
                .Select(d => d.Name)
                .ToList()
        };
    }

    /// <summary>HKCU\...\CapabilityAccessManager\ConsentStore\microphone 的 Value（Allow/Deny）。</summary>
    internal static bool? ProbeMicConsent()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone");
            var v = k?.GetValue("Value") as string;
            return v switch
            {
                "Allow" => true,
                "Deny" => false,
                _ => null
            };
        }
        catch (Exception) { return null; }
    }

    /// <summary>HKCU\Software\Microsoft\Multimedia\Audio\UserDuckingPolicy；缺省返回 null（Windows 默认压低）。</summary>
    internal static int? ProbeDuckingPolicy()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Multimedia\Audio");
            return k?.GetValue("UserDuckingPolicy") is int v ? v : null;
        }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// 服务运行状态：解析 `sc query &lt;name&gt;` 的 STATE 行（RUNNING = 4）。
    /// 不用 ServiceController：零 NuGet 依赖约束下不可用；查询失败按「在跑」处理不误报。
    /// </summary>
    private static bool IsServiceRunning(string serviceName)
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"query {serviceName}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            });
            if (p is null) return true;

            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);
            return output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return true;
        }
    }
}
