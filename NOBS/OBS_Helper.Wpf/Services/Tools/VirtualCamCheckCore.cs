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
            true => new EnvCheckItem("ok", "虚拟摄像头驱动：已注册",
                "系统里能找到 OBS Virtual Camera 的 DirectShow 注册项，驱动层正常。"),
            false => new EnvCheckItem(s.PluginDllPresent == true ? "warn" : "error",
                "虚拟摄像头驱动：未注册",
                s.PluginDllPresent == true
                    ? "OBS 程序文件在，但系统的 DirectShow 注册项缺失——Windows 大版本升级后常见此回归。" +
                      "\n建议：以管理员身份启动一次 OBS 并点击「启动虚拟摄像头」（会重新注册驱动）；无效则重装最新版 OBS。"
                    : "既没有驱动注册项，也没有找到 OBS 的插件文件，虚拟摄像头功能不可用。" +
                      "\n建议：安装或重装最新版 OBS（自带虚拟摄像头组件）。"),
            _ => new EnvCheckItem("info", "虚拟摄像头驱动：状态未知",
                "注册表读取受限，无法确认驱动注册情况。可在 OBS 中点「启动虚拟摄像头」实测。")
        });

        if (s.DriverRegistered != true)
        {
            // 后续项都建立在驱动在位的基础上，避免噪音
            return items;
        }

        items.Add(s.ObsRunning == true
            ? new EnvCheckItem("info", "使用方法",
                "在 OBS 底部控制栏点「启动虚拟摄像头」，然后在会议软件的摄像头列表里选「OBS Virtual Camera」。")
            : new EnvCheckItem("info", "使用方法",
                "先启动 OBS 并点「启动虚拟摄像头」，再在会议软件的摄像头列表里选「OBS Virtual Camera」。"));

        items.Add(new EnvCheckItem("info", "会议软件列表里找不到时",
            "① 先完全退出并重新打开会议软件（多数软件只在启动时枚举摄像头）；" +
            "② 腾讯会议等若仍看不到，检查是否被安全软件拦截了驱动的加载；" +
            "③ 浏览器类应用需授予摄像头权限后刷新页面。"));

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
