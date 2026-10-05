using OBS_Helper.Wpf.Services;
using OBS_Helper.Wpf.Services.Update;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// V2.9.3：Windows 版本判定。引入 Windows 7 兼容构建之后，这些判据直接决定
/// 「界面上露不露某个入口」，因此逐档钉死。
/// </summary>
public class OsSupportTests
{
    [Theory]
    [InlineData(6, 1, 7601, true)]    // Windows 7 SP1 —— 兼容构建的最低要求
    [InlineData(6, 1, 7600, false)]   // Windows 7 首发版（不打 SP1）不支持
    [InlineData(6, 2, 9200, true)]    // Windows 8
    [InlineData(6, 3, 9600, true)]    // Windows 8.1
    [InlineData(10, 0, 19045, true)]  // Windows 10 22H2
    [InlineData(10, 0, 26100, true)]  // Windows 11
    public void IsWindows7OrNewer_MatchesSupportMatrix(int major, int minor, int build, bool expected)
        => Assert.Equal(expected, OsSupport.IsWindows7OrNewer(new Version(major, minor, build)));

    [Theory]
    [InlineData(6, 1, 7601, true)]
    [InlineData(6, 3, 9600, true)]
    [InlineData(10, 0, 10240, false)]
    public void IsLegacyWindows_IsSevenThroughEightOne(int major, int minor, int build, bool expected)
        => Assert.Equal(expected, OsSupport.IsLegacyWindows(new Version(major, minor, build)));

    /// <summary>微软商店的 OBS 需要 Win10 1809（17763）以上 —— 更早的系统上要隐藏入口。</summary>
    [Theory]
    [InlineData(6, 1, 7601, false)]
    [InlineData(10, 0, 17134, false)]   // 1803
    [InlineData(10, 0, 17763, true)]    // 1809
    [InlineData(10, 0, 26100, true)]
    public void MicrosoftStoreAvailability_IsGatedOn1809(int major, int minor, int build, bool expected)
        => Assert.Equal(expected, OsSupport.IsMicrosoftStoreAvailable(new Version(major, minor, build)));

    [Theory]
    [InlineData(6, 1, 7601, false)]
    [InlineData(10, 0, 17763, false)]
    [InlineData(10, 0, 18362, true)]
    public void WgcAvailability_IsGatedOn1903(int major, int minor, int build, bool expected)
        => Assert.Equal(expected, OsSupport.SupportsWindowsGraphicsCapture(new Version(major, minor, build)));

    /// <summary>当前机器上的判据不能抛异常（虚拟化 / 权限受限环境下也要能跑）。</summary>
    [Fact]
    public void CurrentVersion_IsReadable()
    {
        var v = OsSupport.Current;
        Assert.True(v.Major > 0, "取不到系统版本，判据会整体失效");
    }
}

/// <summary>
/// V2.9.3：BUG 反馈入口的常量与白名单。
/// </summary>
public class FeedbackLinksTests
{
    [Fact]
    public void FormUrl_IsHttpsOnTheExpectedHost()
    {
        Assert.StartsWith("https://", FeedbackLinks.BugReportForm);
        Assert.True(FeedbackLinks.FormUrlIsValid);
        Assert.Equal("docs.qq.com", FeedbackLinks.FormHost);
    }

    [Theory]
    [InlineData("https://docs.qq.com/form/page/DSXJMWkNDYVFUWHBo", true)]
    [InlineData("https://github.com/YYRMMAYO/OBS_Helper/issues", true)]
    [InlineData("http://docs.qq.com/form/page/x", false)]                  // 非 https
    [InlineData("https://docs.qq.com.evil.example/form", false)]           // 同前缀的仿冒域
    [InlineData("https://evil.example/form", false)]
    [InlineData("https://github.com/other/repo/issues", false)]            // 非本仓库
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("not a url", false)]
    public void TrustedFeedbackUrls_AreStrictlyWhitelisted(string? url, bool expected)
        => Assert.Equal(expected, FeedbackLinks.IsTrustedFeedbackUrl(url));

    /// <summary>
    /// 二维码里编的内容必须与表单地址**逐字符一致**：二维码是拿不到的「隐形入口」，
    /// 一旦与按钮指向的地址漂移，扫码的用户会被送到另一个页面而没有任何提示。
    /// </summary>
    [Fact]
    public void QrPayload_EqualsFormUrl()
    {
        Assert.Equal(FeedbackLinks.BugReportForm, FeedbackLinks.QrPayload);
        Assert.EndsWith("feedback_qr.png", FeedbackLinks.QrResourceName);
    }
}
