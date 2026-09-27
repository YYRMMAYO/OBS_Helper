using OBS_Helper.Wpf.Localization;
namespace OBS_Helper.Wpf.Services.Tools;

/// <summary>
/// 虚拟摄像头体检核心（纯逻辑，供单元测试）。GAP-5。
///
/// 「会议软件里找不到 OBS Virtual Camera」的排查树：
/// 驱动未注册 → 引导重装 OBS；驱动已注册但会议软件看不到 → 杀毒拦截 / 需重启会议软件指引。
/// </summary>
public static class VirtualCamCheckCore
{
    /// <summary>OBS Virtual Camera 的 DirectShow 源滤镜 CLSID（OBS 26+ 官方注册项）。</summary>
    public const string DsFilterClsid = "{A3FCE0F5-3493-419f-8582-7E28BCB15EAF}";

    public static List<EnvCheckItem> Evaluate(VirtualCamCheckSnapshot s)
    {
        var items = new List<EnvCheckItem>();

        items.Add(s.DriverRegistered switch
        {
            true => new EnvCheckItem("ok", Strings.T("vcam.driver.registeredTitle"),
                Strings.T("vcam.driver.registeredDetail")),
            false => new EnvCheckItem(s.PluginDllPresent == true ? "warn" : "error",
                Strings.T("vcam.driver.missingTitle"),
                s.PluginDllPresent == true
                    ? Strings.T("vcam.driver.missingWithFiles")
                    : Strings.T("vcam.driver.missingNoFiles")),
            _ => new EnvCheckItem("info", Strings.T("vcam.driver.unknownTitle"),
                Strings.T("vcam.driver.unknownDetail"))
        });

        if (s.DriverRegistered != true)
        {
            // 后续项都建立在驱动在位的基础上，避免噪音
            return items;
        }

        items.Add(s.ObsRunning == true
            ? new EnvCheckItem("info", Strings.T("vcam.usage.title"),
                Strings.T("vcam.usage.running"))
            : new EnvCheckItem("info", Strings.T("vcam.usage.title"),
                Strings.T("vcam.usage.notRunning")));

        items.Add(new EnvCheckItem("info", Strings.T("vcam.notListed.title"),
            Strings.T("vcam.notListed.detail")));

        return items;
    }
}

/// <summary>虚拟摄像头体检快照。</summary>
public sealed class VirtualCamCheckSnapshot
{
    /// <summary>DirectShow 滤镜注册项是否存在；null = 注册表读取失败。</summary>
    public bool? DriverRegistered { get; init; }

    /// <summary>OBS 插件目录下 win-dshow.dll 是否存在（虚拟摄像头由它提供）。</summary>
    public bool? PluginDllPresent { get; init; }

    /// <summary>obs64.exe 当前是否在运行。</summary>
    public bool? ObsRunning { get; init; }
}