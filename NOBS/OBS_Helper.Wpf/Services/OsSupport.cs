namespace OBS_Helper.Wpf.Services;

/// <summary>
/// Windows 版本判定（V2.9.3）。纯逻辑，可被单测工程直接链接编译。
///
/// 为什么要有这个类：本版把兼容性扩到了 **Windows 7 SP1**，于是「这台机器到底能干什么」
/// 变成了一等公民 ——
/// <list type="bullet">
///   <item><b>现代构建</b>（net10.0-windows）最低 Windows 10；</item>
///   <item><b>兼容构建</b>（net6.0-windows）覆盖 Windows 7 SP1 ~ Windows 11，
///       安装包用 Inno 的 MinVersion 拦在 6.1sp1。</item>
/// </list>
/// 界面上的差异（例如微软商店入口、Windows Graphics Capture 捕获方式）按这里的判据显隐，
/// 而不是到处写 <c>Environment.OSVersion.Version.Major</c>。
///
/// <b>取值可信度</b>：.NET（Core 3.0 起）的 <see cref="Environment.OSVersion"/> 在 Windows 上
/// 走的是 <c>RtlGetVersion</c>，不受「清单没声明 supportedOS 就谎报 6.2」那套老规则影响，
/// 因此在 Win7 上也能如实拿到 6.1。这一点正是本类能作为判据的前提。
/// </summary>
public static class OsSupport
{
    /// <summary>Windows 7 SP1 的版本号（6.1.7601）。</summary>
    public static readonly Version Windows7Sp1 = new(6, 1, 7601, 0);

    /// <summary>Windows 10 的最低版本号（10.0）。</summary>
    public static readonly Version Windows10 = new(10, 0, 0, 0);

    /// <summary>Windows 10 1809（17763）：微软商店的 OBS 需要它以上。</summary>
    public static readonly Version Windows10_1809 = new(10, 0, 17763, 0);

    /// <summary>Windows 10 1903（18362）：Windows Graphics Capture（WGC）的起点。</summary>
    public static readonly Version Windows10_1903 = new(10, 0, 18362, 0);

    /// <summary>当前系统版本（取不到时按「未知」处理，绝不抛异常）。</summary>
    public static Version Current
    {
        get
        {
            try { return Normalize(Environment.OSVersion.Version); }
            catch (Exception) { return new Version(0, 0, 0, 0); }
        }
    }

    /// <summary>
    /// 把版本补成四段再比较。
    ///
    /// 不补的话会踩 <see cref="Version"/> 的经典坑：未指定的段按 -1 参与比较，
    /// 于是 <c>6.1.7601 &lt; 6.1.7601.0</c> 成立 —— 手里拿着「6.1.7601」去和
    /// 「Windows7Sp1 = 6.1.7601.0」比大小会得出「不是 Win7 SP1」的荒谬结论。
    /// 这类错误只在特定调用形态下出现，非常容易漏。
    /// </summary>
    public static Version Normalize(Version v)
        => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    /// <summary>是否 Windows 7 及以上（含 8、8.1、10、11）。</summary>
    public static bool IsWindows7OrNewer(Version v) => Normalize(v) >= Windows7Sp1;

    /// <summary>是否 Windows 7 / 8 / 8.1（即「老系统」，只有兼容构建才跑得起来）。</summary>
    public static bool IsLegacyWindows(Version v) => Normalize(v) >= Windows7Sp1 && Normalize(v) < Windows10;

    /// <summary>是否 Windows 10 及以上。</summary>
    public static bool IsWindows10OrNewer(Version v) => Normalize(v) >= Windows10;

    /// <summary>当前系统能否用微软商店的 OBS（需要 Win10 1809 以上的商店）。</summary>
    public static bool IsMicrosoftStoreAvailable(Version v) => Normalize(v) >= Windows10_1809;

    /// <summary>当前系统能否用 WGC 捕获方式（Win10 1903+；列出来只作提示，实际以 OBS 自身为准）。</summary>
    public static bool SupportsWindowsGraphicsCapture(Version v) => Normalize(v) >= Windows10_1903;

    /// <summary>兼容构建的最低要求是否成立（用于自检与友好的启动提示）。</summary>
    public static bool IsSupportedByCurrentBuild(Version v) => IsWindows7OrNewer(v);
}
