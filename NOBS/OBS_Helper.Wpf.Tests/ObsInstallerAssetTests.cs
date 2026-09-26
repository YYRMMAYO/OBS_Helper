using OBS_Helper.Wpf.Services.Update;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// Windows 安装包直链解析的测试（V2.9.1）：从 GitHub <c>releases/latest</c> 的 JSON 里
/// 挑出 <c>*-Windows-x64-Installer.exe</c>，并且只接受官方域名下的地址。
///
/// 样例 JSON 按 GitHub API 的真实字段裁剪而来（asset 的 name / browser_download_url）；
/// 这里不联网，纯解析 + 白名单校验。
/// </summary>
public class ObsInstallerAssetTests
{
    private const string SampleReleaseJson = """
    {
      "tag_name": "32.2.2",
      "html_url": "https://github.com/obsproject/obs-studio/releases/tag/32.2.2",
      "assets": [
        {
          "name": "OBS-Studio-32.2.2-Windows-x64-Installer.exe",
          "browser_download_url": "https://github.com/obsproject/obs-studio/releases/download/32.2.2/OBS-Studio-32.2.2-Windows-x64-Installer.exe"
        },
        {
          "name": "OBS-Studio-32.2.2-Windows-x64.zip",
          "browser_download_url": "https://github.com/obsproject/obs-studio/releases/download/32.2.2/OBS-Studio-32.2.2-Windows-x64.zip"
        },
        {
          "name": "OBS-Studio-32.2.2-macOS-Apple.dmg",
          "browser_download_url": "https://github.com/obsproject/obs-studio/releases/download/32.2.2/OBS-Studio-32.2.2-macOS-Apple.dmg"
        }
      ]
    }
    """;

    [Fact]
    public void IsWindowsInstaller_MatchesOnlyWindowsInstaller()
    {
        Assert.True(ObsInstallerAsset.IsWindowsInstaller("OBS-Studio-32.2.2-Windows-x64-Installer.exe"));
        Assert.True(ObsInstallerAsset.IsWindowsInstaller("obs-studio-99.0-WINDOWS-X64-INSTALLER.EXE"));
        Assert.False(ObsInstallerAsset.IsWindowsInstaller("OBS-Studio-32.2.2-Windows-x64.zip"));
        Assert.False(ObsInstallerAsset.IsWindowsInstaller("OBS-Studio-32.2.2-macOS-Apple.dmg"));
        Assert.False(ObsInstallerAsset.IsWindowsInstaller(null));
        Assert.False(ObsInstallerAsset.IsWindowsInstaller(""));
    }

    [Fact]
    public void PickWindowsInstallerUrl_PicksTheInstallerAsset()
    {
        var url = ObsInstallerAsset.PickWindowsInstallerUrl(SampleReleaseJson);

        Assert.NotNull(url);
        Assert.EndsWith("OBS-Studio-32.2.2-Windows-x64-Installer.exe", url);
        Assert.True(ObsDownloadLinks.IsOfficialDownloadUrl(url));
    }

    [Fact]
    public void PickWindowsInstallerVersion_ReadsVersionFromAssetName()
        => Assert.Equal("32.2.2", ObsInstallerAsset.PickWindowsInstallerVersion(SampleReleaseJson));

    [Fact]
    public void PickWindowsInstallerUrl_ReturnsNullWhenNoInstallerAsset()
    {
        const string noInstaller = """
        { "tag_name": "9.9.9", "assets": [ { "name": "OBS-Studio-9.9.9-Windows-x64.zip",
          "browser_download_url": "https://github.com/obsproject/obs-studio/releases/download/9.9.9/OBS-Studio-9.9.9-Windows-x64.zip" } ] }
        """;

        Assert.Null(ObsInstallerAsset.PickWindowsInstallerUrl(noInstaller));
        Assert.Null(ObsInstallerAsset.PickWindowsInstallerVersion(noInstaller));
    }

    [Fact]
    public void PickWindowsInstallerUrl_RejectsNonOfficialDownloadHost()
    {
        // 响应体被篡改（或结构异常）时，宁可拿不到直链也不能把用户送去第三方站点
        const string hijacked = """
        { "tag_name": "32.2.2", "assets": [ {
          "name": "OBS-Studio-32.2.2-Windows-x64-Installer.exe",
          "browser_download_url": "https://malware.example.com/OBS-Studio-32.2.2-Windows-x64-Installer.exe" } ] }
        """;

        Assert.Null(ObsInstallerAsset.PickWindowsInstallerUrl(hijacked));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"assets\": null}")]
    [InlineData("{\"assets\": [null, 1, \"x\"]}")]
    public void PickWindowsInstallerUrl_NeverThrows(string? json)
    {
        Assert.Null(ObsInstallerAsset.PickWindowsInstallerUrl(json));
        Assert.Null(ObsInstallerAsset.PickWindowsInstallerVersion(json));
    }
}
