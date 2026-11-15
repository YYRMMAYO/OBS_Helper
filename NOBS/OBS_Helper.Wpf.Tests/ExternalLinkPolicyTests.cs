using OBS_Helper.Wpf.Services.Security;

namespace OBS_Helper.Wpf.Tests;

/// <summary>外链白名单（V3.6.0 / G04）：只允许 https + 默认端口 + 已知域名（含子域名）。</summary>
public class ExternalLinkPolicyTests
{
    [Theory]
    [InlineData("https://obsproject.com/kb")]
    [InlineData("https://www.obsproject.com.cn/")]
    [InlineData("https://raw.githubusercontent.com/YYRMMAYO/OBS_Helper/master/README.md")]
    [InlineData("https://github.com/zanjie1999/meTimer")]
    [InlineData("https://space.bilibili.com/123456")]
    [InlineData("https://www.douyin.com/")]
    [InlineData("https://wwbpq.lanzouu.com/abc")]
    [InlineData("https://GITHUB.COM/YYRMMAYO/OBS_Helper")]
    [InlineData("https://github.com./YYRMMAYO/OBS_Helper")]
    public void Allowed(string url) => Assert.True(ExternalLinkPolicy.IsAllowed(url), url);

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("http://obsproject.com/kb")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("https://evil.example/")]
    [InlineData("https://github.com.evil.example/")]
    [InlineData("https://github.com@evil.example/")]
    [InlineData("https://github.com:8443/x")]
    [InlineData("https://127.0.0.1/")]
    [InlineData("https://localhost/")]
    [InlineData("http://192.168.1.1/")]
    public void Rejected(string url) => Assert.False(ExternalLinkPolicy.IsAllowed(url), url);
}
