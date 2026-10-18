using System.Runtime.InteropServices;

namespace OBS_Helper.Wpf.Services.Compat;

/// <summary>捕获排除的结果（用于向用户如实说明，而不是承诺「绝对不会被录到」）。</summary>
public enum CaptureExclusionResult
{
    /// <summary>系统不支持（Win7 / Windows 10 2004 之前）。</summary>
    UnsupportedOs = 0,

    /// <summary>调用成功：窗口会从支持的采集方式中排除。</summary>
    Applied = 1,

    /// <summary>调用失败（句柄无效 / 系统策略拒绝）。</summary>
    Failed = 2,

    /// <summary>用户关闭了排除开关。</summary>
    Disabled = 3
}

/// <summary>
/// 窗口捕获排除与点击穿透（V3.1.0 / §12.1.3）。
///
/// 关键事实：
/// <list type="bullet">
///   <item><b>窗口采集 / 游戏采集天然不包含提词器</b>（它不是被采集窗口的一部分）；</item>
///   <item><b>显示器采集</b>只有在 Windows 10 2004+ / Windows 11 上，用
///     <c>SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)</c> 才可能排除；
///     一些采集后端会留下黑块，因此文案只写「在支持的采集方式下可排除」；</item>
///   <item>Win7 不支持该 API，走「窗口采集 / 第二屏」的降级建议。</item>
/// </list>
/// </summary>
public static class WindowCaptureExclusion
{
    private const uint WdaNone = 0x00000000;
    private const uint WdaExcludeFromCapture = 0x00000011;

    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExLayered = 0x00080000;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    /// <summary>当前系统是否支持「从显示器采集中排除」（Windows 10 2004 / build 19041 及以上）。</summary>
    public static bool IsCaptureExclusionSupported
    {
        get
        {
            try
            {
#if WIN7_COMPAT
                // Win7 兼容构建：明确不支持，避免在 OBS 27 的显示器采集里出现黑块
                return false;
#else
                return OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041);
#endif
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>应用或取消捕获排除。</summary>
    public static CaptureExclusionResult Apply(IntPtr hwnd, bool enabled)
    {
        if (!enabled) return CaptureExclusionResult.Disabled;
        if (hwnd == IntPtr.Zero) return CaptureExclusionResult.Failed;
        if (!IsCaptureExclusionSupported) return CaptureExclusionResult.UnsupportedOs;

        try
        {
            return SetWindowDisplayAffinity(hwnd, WdaExcludeFromCapture)
                ? CaptureExclusionResult.Applied
                : CaptureExclusionResult.Failed;
        }
        catch (Exception)
        {
            return CaptureExclusionResult.Failed;
        }
    }

    /// <summary>恢复为「正常参与捕获」。</summary>
    public static bool Clear(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        try { return SetWindowDisplayAffinity(hwnd, WdaNone); }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// 点击穿透：<c>WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE</c>。
    /// 开启后鼠标事件会落到下层应用，提词器仍可用全局热键控制。
    /// </summary>
    public static bool SetClickThrough(IntPtr hwnd, bool enabled)
    {
        if (hwnd == IntPtr.Zero) return false;
        try
        {
            var style = GetWindowLong(hwnd, GwlExStyle).ToInt64();
            var updated = enabled
                ? style | WsExTransparent | WsExLayered | WsExNoActivate
                : style & ~(long)(WsExTransparent | WsExNoActivate);
            // 关闭穿透时保留 Layered（窗口本身是 AllowsTransparency），只去掉透明与不激活
            if (!enabled && (style & WsExLayered) != 0) updated |= WsExLayered;
            SetWindowLong(hwnd, GwlExStyle, new IntPtr(updated));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>让窗口不出现在 Alt+Tab（工具窗口样式）。</summary>
    public static bool ApplyToolWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        try
        {
            var style = GetWindowLong(hwnd, GwlExStyle).ToInt64();
            SetWindowLong(hwnd, GwlExStyle, new IntPtr(style | WsExToolWindow));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static IntPtr GetWindowLong(IntPtr hwnd, int index)
        => IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, index) : new IntPtr(GetWindowLong32(hwnd, index));

    private static IntPtr SetWindowLong(IntPtr hwnd, int index, IntPtr value)
        => IntPtr.Size == 8
            ? SetWindowLongPtr64(hwnd, index, value)
            : new IntPtr(SetWindowLong32(hwnd, index, value.ToInt32()));
}
