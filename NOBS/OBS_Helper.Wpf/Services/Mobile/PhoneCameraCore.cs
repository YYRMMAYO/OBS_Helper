using System.IO;
using OBS_Helper.Wpf.Models.Mobile;
using OBS_Helper.Wpf.Services.Mobile;
using OBS_Helper.Wpf.Services.Security;

namespace OBS_Helper.Wpf.Services.Mobile;

/// <summary>
/// 摄像头 URL / 布局 / 私网校验（纯逻辑，单测覆盖）。与 mediamtx 的路径约定：
/// 推流端发布到 <c>live/&lt;key&gt;</c>，OBS 从回环 RTSP 拉同一路径。
/// </summary>
public static class PhoneCameraCore
{
    public static string NewStreamKey() => LanPolicy.NewToken(16);

    public static string SrtPublishUrl(string lanAddress, int port, string key)
        => $"srt://{lanAddress}:{port}?streamid=publish:live/{key}";

    public static string RtmpPublishUrl(string lanAddress, int port, string key)
        => $"rtmp://{lanAddress}:{port}/live/{key}";

    public static string RtspLoopbackUrl(int port, string key) => $"rtsp://127.0.0.1:{port}/live/{key}";

    /// <summary>路径 key 必须是 URL 安全的高熵串（不允许枚举、不允许空）。</summary>
    public static bool IsValidKey(string? key)
        => !string.IsNullOrWhiteSpace(key) && key.Length >= 16 && key.All(ch =>
            (ch < 128 && char.IsLetterOrDigit(ch)) || ch is '-' or '_');

    /// <summary>只接受私网来源推流（RFC1918 / 链路本地 / 回环）。</summary>
    public static bool AllowsPublishFrom(string? remoteAddress) => LanPolicy.IsPrivateAddress(remoteAddress);

    /// <summary>
    /// 计算画布上的目标矩形（像素）。竖屏裁切按源画面居中裁成 9:16，再等比放进画布。
    /// </summary>
    public static (int X, int Y, int Width, int Height) ComputeBounds(
        int canvasWidth, int canvasHeight, int sourceWidth, int sourceHeight, PhoneCameraLayout layout)
    {
        if (canvasWidth <= 0 || canvasHeight <= 0) return (0, 0, 0, 0);
        return layout switch
        {
            PhoneCameraLayout.Fullscreen => (0, 0, canvasWidth, canvasHeight),
            PhoneCameraLayout.PipBottomRight => (
                (int)(canvasWidth * 0.68), (int)(canvasHeight * 0.68),
                (int)(canvasWidth * 0.30), (int)(canvasHeight * 0.30)),
            PhoneCameraLayout.SideLeftThird => (0, 0, (int)(canvasWidth / 3.0), canvasHeight),
            PhoneCameraLayout.PortraitCrop => PortraitBounds(canvasWidth, canvasHeight, sourceWidth, sourceHeight),
            _ => (0, 0, canvasWidth, canvasHeight),
        };
    }

    private static (int X, int Y, int Width, int Height) PortraitBounds(int cw, int ch, int sw, int sh)
    {
        // 目标 9:16 区域在画布内居中；源画面按 9:16 居中裁切后再缩放（由 OBS 的裁剪/缩放完成，
        // 这里只给出目标矩形，保持与 Live2D 布局同一套数学）。
        var targetW = Math.Min(cw, (int)(ch * 9.0 / 16.0));
        var targetH = (int)(targetW * 16.0 / 9.0);
        if (targetH > ch) { targetH = ch; targetW = (int)(ch * 9.0 / 16.0); }
        var x = (cw - targetW) / 2;
        var y = (ch - targetH) / 2;
        return (x, y, targetW, targetH);
    }

    /// <summary>按画质给出建议码率（kbps）；界面展示与 FFmpeg/手机 App 提示共用。</summary>
    public static int SuggestedBitrate(PhoneCameraQuality quality, bool lowBandwidth)
        => lowBandwidth ? Math.Min(quality.BitrateKbps, 1200) : quality.BitrateKbps;

    /// <summary>mediamtx 配置：只开需要的协议，HLS/WebRTC 关闭，API 只绑回环。</summary>
    public static string BuildConfig(int rtspPort, int rtmpPort, int srtPort, int apiPort)
        => $"""
logLevel: warn
readTimeout: 10s
writeTimeout: 10s

rtsp: yes
rtspAddress: :{rtspPort}
rtmp: yes
rtmpAddress: :{rtmpPort}
srt: yes
srtAddress: :{srtPort}
hls: no
webrtc: no
api: yes
apiAddress: 127.0.0.1:{apiPort}

paths:
  all_others:
""";
}

/// <summary>
/// USB 零网络摄像头引导（M08）。只做检测与步骤说明，**不自动安装** scrcpy / 驱动。
/// </summary>
public sealed class UsbCameraGuide
{
    public bool AdbFound { get; }
    public bool ScrcpyFound { get; }
    public string? AdbPath { get; }
    public string? ScrcpyPath { get; }

    public UsbCameraGuide() : this(FindOnPath("adb.exe"), FindOnPath("scrcpy.exe")) { }

    public UsbCameraGuide(string? adbPath, string? scrcpyPath)
    {
        AdbPath = adbPath;
        ScrcpyPath = scrcpyPath;
        AdbFound = adbPath is not null && File.Exists(adbPath);
        ScrcpyFound = scrcpyPath is not null && File.Exists(scrcpyPath);
    }

    public IReadOnlyList<string> AndroidSteps => new[]
    {
        "1. 手机开启「开发者选项 → USB 调试」，用数据线连接电脑。",
        "2. 电脑安装 scrcpy（Apache-2.0，本项目不捆绑、不自动安装）。",
        "3. 运行 scrcpy --video-source=camera，选择手机摄像头。",
        "4. 在 OBS 里用「窗口采集」选中 scrcpy 窗口；需要垂直构图时裁切。",
        "5. 如果 adb 无法识别：安装手机厂商 USB 驱动，或换一根支持数据传输的线。",
    };

    public IReadOnlyList<string> IosSteps => new[]
    {
        "1. iOS 没有系统级虚拟摄像头，请安装 OBS Camera / Iriun / Camo 等应用（用户自行安装）。",
        "2. 手机与电脑连同一 Wi-Fi，按应用提示在电脑端安装驱动。",
        "3. 在 OBS 里添加「视频采集设备」，选择该虚拟摄像头。",
        "4. iOS 浏览器不能直接当摄像头（secure context 限制），不要依赖网页方案。",
    };

    private static string? FindOnPath(string exe)
    {
        try
        {
            var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
            foreach (var dir in paths)
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                var candidate = Path.Combine(dir.Trim('"'), exe);
                if (File.Exists(candidate)) return candidate;
            }
        }
        catch (Exception) { }
        return null;
    }
}

/// <summary>
/// 手机通知 / 剪贴板上屏（M11）的隐私核心（纯逻辑）。
/// 敏感内容默认过滤：验证码、密码、银行卡/身份证、私钥；只有用户确认后才可能上屏。
/// </summary>
public static class MobileNotifyCore
{
    private static readonly string[] SensitiveKeywords =
    {
        "验证码", "校验码", "动态码", "密码", "password", "passcode", "pin code",
        "银行卡", "信用卡", "cvv", "身份证", "私钥", "助记词", "seed phrase", "otp",
    };

    private static readonly System.Text.RegularExpressions.Regex SixDigit =
        new(@"(?<!\d)\d{6}(?!\d)", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex CardLike =
        new(@"(?<!\d)(?:\d[ -]?){15,19}(?!\d)", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>是否包含敏感内容（命中关键词 / 6 位数字 / 卡号样式）。</summary>
    public static bool IsSensitive(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        var lower = text.ToLowerInvariant();
        if (SensitiveKeywords.Any(k => lower.Contains(k, StringComparison.Ordinal))) return true;
        if (SixDigit.IsMatch(text)) return true;
        return CardLike.IsMatch(text);
    }

    /// <summary>生成脱敏预览：只保留首尾少量字符，中间用 * 替换。</summary>
    public static string Redact(string? text, int keep = 2)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var trimmed = text.Trim();
        if (trimmed.Length <= keep * 2) return new string('*', trimmed.Length);
        return trimmed[..keep] + new string('*', Math.Min(12, trimmed.Length - keep * 2)) + trimmed[^keep..];
    }

    /// <summary>上屏前的最终文本：超长截断、去控制字符；敏感内容返回 false（必须人工改写）。</summary>
    public static bool TryPrepare(string? text, out string prepared)
    {
        prepared = "";
        if (string.IsNullOrWhiteSpace(text) || IsSensitive(text)) return false;
        var cleaned = new string(text.Where(ch => !char.IsControl(ch) || ch is '\n' or '\t').ToArray()).Trim();
        if (cleaned.Length > 300) cleaned = cleaned[..300];
        prepared = cleaned;
        return prepared.Length > 0;
    }

}
