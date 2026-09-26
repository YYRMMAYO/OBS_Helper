using OBS_Helper.Wpf.Services.Update;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// 官方 OBS 下载入口的测试（V2.9.1）。
///
/// 这类常量的风险不是编译期，而是「手一抖写成第三方站点」——直接跳转出去，用户装到捆绑包 / 木马。
/// 因此这里把「只允许官方域名」钉死：常量本身、以及白名单判定函数的行为。
/// </summary>
public class ObsDownloadLinksTests
{
    [Fact]
    public void OfficialLinks_PointAtObsProjectAndOfficialRepo()
    {
        foreach (var url in new[]
                 {
                     ObsDownloadLinks.OfficialSite,
                     ObsDownloadLinks.OfficialDownload,
                 })
        {
            Assert.StartsWith("https://obsproject.com/", url);
            Assert.True(ObsDownloadLinks.IsOfficialDownloadUrl(url), url);
        }

        foreach (var url in new[]
                 {
                     ObsDownloadLinks.GitHubRepo,
                     ObsDownloadLinks.GitHubReleases,
                     ObsDownloadLinks.GitHubLatestRelease,
                 })
        {
            Assert.StartsWith("https://github.com/obsproject/obs-studio", url);
            Assert.True(ObsDownloadLinks.IsOfficialDownloadUrl(url), url);
        }
    }

    [Fact]
    public void ChineseOfficialSite_AndDownloadPage_AreKept()
    {
        // 中文站与中文下载页是给国内用户的默认入口（V2.9.1 实测 HTTP 200），改动需谨慎
        Assert.Equal("https://obsproject.com/zh-cn", ObsDownloadLinks.OfficialSite);
        Assert.Equal("https://obsproject.com/zh-cn/download", ObsDownloadLinks.OfficialDownload);
    }

    [Theory]
    [InlineData("https://obsproject.com/download")]
    [InlineData("https://obsproject.com/zh-cn/download")]
    [InlineData("https://cdn-fastly.obsproject.com/downloads/OBS-Studio-32.2.2-Windows-x64-Installer.exe")]
    [InlineData("https://github.com/obsproject/obs-studio/releases/download/32.2.2/OBS-Studio-32.2.2-Windows-x64-Installer.exe")]
    public void IsOfficialDownloadUrl_AcceptsOfficialHosts(string url)
        => Assert.True(ObsDownloadLinks.IsOfficialDownloadUrl(url), url);

    [Theory]
    [InlineData("http://obsproject.com/download")]                     // 非 https
    [InlineData("https://obsproject.com.evil.example/download")]       // 伪装子串
    [InlineData("https://obsproject-cn.example.com/download")]         // 同形域名
    [InlineData("https://github.com/someone-else/obs-studio/releases")] // 非官方仓库
    [InlineData("https://github.com/evil/obs-studio/releases/download/x.exe")]
    [InlineData("https://github.com/obsproject/obs-studio-evil/releases/download/x.exe")] // 同前缀的其它仓库
    [InlineData("https://gitlab.com/obsproject/obs-studio/releases")]
    [InlineData("https://gitee.com/mirror/obs-studio/releases")]       // 第三方镜像
    [InlineData("file:///C:/tmp/OBS-Setup.exe")]
    [InlineData("")]
    [InlineData(null)]
    public void IsOfficialDownloadUrl_RejectsEverythingElse(string? url)
        => Assert.False(ObsDownloadLinks.IsOfficialDownloadUrl(url), url ?? "(null)");

    [Fact]
    public void SafetyNote_MentionsOnlyOfficialSources()
    {
        Assert.Contains("obsproject.com", ObsDownloadLinks.SafetyNote);
        Assert.Contains("github.com/obsproject", ObsDownloadLinks.SafetyNote);
        Assert.Contains(ObsDownloadLinks.WindowsSignerName, ObsDownloadLinks.SafetyNote);
        Assert.Equal("OBS Project Corporation", ObsDownloadLinks.WindowsSignerName);
    }
}
