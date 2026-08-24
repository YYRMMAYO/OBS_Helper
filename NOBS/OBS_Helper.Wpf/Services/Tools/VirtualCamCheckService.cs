using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace OBS_Helper.Wpf.Services.Tools;

/// <summary>
/// 虚拟摄像头体检服务（V2.8，GAP-5，只读）。
///
/// 探测 DirectShow 注册项（HKLM / HKCU Classes\CLSID 下 OBS Virtual Camera 的滤镜 GUID）
/// 与 OBS 插件文件 win-dshow.dll 的存在性；判定逻辑交给 <see cref="VirtualCamCheckCore"/>。
/// </summary>
public static class VirtualCamCheckService
{
    public static async Task<List<EnvCheckItem>> RunAsync()
    {
        var snapshot = await Task.Run(CollectSnapshot).ConfigureAwait(true);
        return VirtualCamCheckCore.Evaluate(snapshot);
    }

    internal static VirtualCamCheckSnapshot CollectSnapshot()
    {
        return new VirtualCamCheckSnapshot
        {
            DriverRegistered = ProbeDriverRegistered(),
            PluginDllPresent = ProbePluginDll(),
            ObsRunning = ProbeObsRunning()
        };
    }

    /// <summary>Classes\CLSID\{A3FCE0F5-...} 在 HKLM 或 HKCU 任一存在即视为已注册。</summary>
    internal static bool? ProbeDriverRegistered()
    {
        try
        {
            var path = $@"SOFTWARE\Classes\CLSID\{VirtualCamCheckCore.DsFilterClsid}";
            using var hklm = Registry.LocalMachine.OpenSubKey(path);
            if (hklm is not null) return true;
            using var hkcu = Registry.CurrentUser.OpenSubKey(path);
            return hkcu is not null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>在常见安装目录找 win-dshow.dll（64 位插件目录）。</summary>
    internal static bool? ProbePluginDll()
    {
        try
        {
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "obs-studio", "obs-plugins", "win64"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "obs-studio", "obs-plugins", "win64")
            };
            return candidates.Any(d => File.Exists(Path.Combine(d, "win-dshow.dll")));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool? ProbeObsRunning()
    {
        try
        {
            return Process.GetProcessesByName("obs64").Length > 0;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
