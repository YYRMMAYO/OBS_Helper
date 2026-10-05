using OBS_Helper.Wpf.Localization;
using OBS_Helper.Wpf.Services;
using OBS_Helper.Wpf.Services.Update;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// V2.9.3：Release 资产的「按语言取」匹配。
///
/// 背景：中英资产挂在**同一个 Release** 上（<c>OBS_Helper_Knowledge_2.9.3.json</c> 与
/// <c>…2.9.3.en-US.json</c>），而原来的匹配器只看「前缀 + .json 后缀」。
/// 不加语言判据的话中英会互相拿到对方的文件 —— 而且是静默的：内容合法、解析成功，
/// 只是语言不对，用户只会觉得「切了语言没生效」。
/// </summary>
public class ReleaseAssetLanguageTests
{
    private const string KbPrefix = "OBS_Helper_Knowledge_";
    private const string PluginsPrefix = "OBS_Helper_Plugins_";

    [Theory]
    [InlineData("OBS_Helper_Knowledge_2.9.3.json", KbPrefix, ".json", false, true)]
    [InlineData("OBS_Helper_Knowledge_2.9.3.en-US.json", KbPrefix, ".json", false, false)]
    [InlineData("OBS_Helper_Knowledge_2.9.3.en-US.json", KbPrefix, ".json", true, true)]
    [InlineData("OBS_Helper_Knowledge_2.9.3.json", KbPrefix, ".json", true, false)]
    [InlineData("OBS_Helper_Plugins_1.4.json", PluginsPrefix, ".json", false, true)]
    [InlineData("OBS_Helper_Plugins_1.4.en-US.json", PluginsPrefix, ".json", true, true)]
    [InlineData("OBS_Helper_Setup_2.9.3.exe", KbPrefix, ".json", false, false)]
    [InlineData("OBS_Helper_Knowledge_", KbPrefix, ".json", false, false)]
    [InlineData("", KbPrefix, ".json", false, false)]
    [InlineData(null, KbPrefix, ".json", false, false)]
    public void AssetMatchesLanguage_SplitsByLanguageSuffix(
        string? name, string prefix, string extension, bool english, bool expected)
        => Assert.Equal(expected, UpdateService.AssetMatchesLanguage(name, prefix, extension, english));

    /// <summary>大小写不敏感（GitHub 上资产名不保证大小写一致）。</summary>
    [Theory]
    [InlineData("obs_helper_knowledge_2.9.3.EN-us.json", true)]
    [InlineData("OBS_HELPER_KNOWLEDGE_2.9.3.JSON", false)]
    public void AssetMatchesLanguage_IsCaseInsensitive(string name, bool english)
        => Assert.True(UpdateService.AssetMatchesLanguage(name, KbPrefix, ".json", english));

    /// <summary>
    /// 端到端形状：给定一份同时含中英资产的资产列表，两种语言各自只能挑到自己那一份。
    /// </summary>
    [Fact]
    public void ChineseAndEnglishMatchersNeverCross()
    {
        var assets = new[]
        {
            "OBS_Helper_Knowledge_2.9.3.en-US.json",
            "OBS_Helper_Knowledge_2.9.3.json",
            "OBS_Helper_Plugins_1.4.en-US.json",
            "OBS_Helper_Plugins_1.4.json",
        };

        var zh = assets.Where(n => UpdateService.AssetMatchesLanguage(n, KbPrefix, ".json", false)).ToList();
        var en = assets.Where(n => UpdateService.AssetMatchesLanguage(n, KbPrefix, ".json", true)).ToList();

        Assert.Equal(new[] { "OBS_Helper_Knowledge_2.9.3.json" }, zh);
        Assert.Equal(new[] { "OBS_Helper_Knowledge_2.9.3.en-US.json" }, en);
        Assert.Empty(zh.Intersect(en, StringComparer.Ordinal));

        Assert.Single(assets.Where(n => UpdateService.AssetMatchesLanguage(n, PluginsPrefix, ".json", false)));
        Assert.Single(assets.Where(n => UpdateService.AssetMatchesLanguage(n, PluginsPrefix, ".json", true)));
    }

    /// <summary>默认参数（旧调用形态）必须仍然等于「中文」语义。</summary>
    [Fact]
    public void DefaultLanguageMeansChinese()
    {
        Assert.False(ContentAssets.IsEnglish(null));
        Assert.True(UpdateService.AssetMatchesLanguage("OBS_Helper_Knowledge_9.9.json", KbPrefix, ".json", false));
        Assert.False(UpdateService.AssetMatchesLanguage("OBS_Helper_Knowledge_9.9.en-US.json", KbPrefix, ".json", false));
    }
}

/// <summary>
/// V2.9.3：下载入口白名单。
///
/// 官方源与备用通道**必须分开判定**：把瓦特工具箱 / 微软商店塞进「OBS 官方下载」的白名单，
/// 会让「只从官方渠道下载」这句提示文案失真，而这个提示正是防仿冒安装包的关键。
/// </summary>
public class DownloadChannelTests
{
    [Fact]
    public void OfficialWhitelist_StillRejectsThirdParties()
    {
        Assert.True(ObsDownloadLinks.IsOfficialDownloadUrl("https://obsproject.com/zh-cn/download"));
        Assert.True(ObsDownloadLinks.IsOfficialDownloadUrl(
            "https://github.com/obsproject/obs-studio/releases/download/32.2.2/x.exe"));
        Assert.False(ObsDownloadLinks.IsOfficialDownloadUrl("https://steampp.net/"));
        Assert.False(ObsDownloadLinks.IsOfficialDownloadUrl("https://apps.microsoft.com/detail/XPFFH613W8V6LV"));
        Assert.False(ObsDownloadLinks.IsOfficialDownloadUrl("https://evil.example/obs.exe"));
        Assert.False(ObsDownloadLinks.IsOfficialDownloadUrl("http://obsproject.com/download"));
    }

    [Fact]
    public void AlternativeChannels_AreHttpsAndWhitelisted()
    {
        Assert.StartsWith("https://", ObsDownloadLinks.WattToolkitSite);
        Assert.StartsWith("https://", ObsDownloadLinks.MicrosoftStoreObs);
        Assert.True(ObsDownloadLinks.IsTrustedAlternativeChannel(ObsDownloadLinks.WattToolkitSite));
        Assert.True(ObsDownloadLinks.IsTrustedAlternativeChannel(ObsDownloadLinks.MicrosoftStoreObs));
    }

    [Theory]
    [InlineData("https://steampp.net/", true)]
    [InlineData("https://apps.microsoft.com/detail/XPFFH613W8V6LV", true)]
    [InlineData("https://steampp.net.evil.example/", false)]      // 同前缀仿冒域
    [InlineData("http://steampp.net/", false)]                    // 非 https
    [InlineData("https://obsproject.com/", false)]                // 官方源不算备用通道
    [InlineData("https://evil.example/", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void AlternativeChannelWhitelist_IsStrict(string? url, bool expected)
        => Assert.Equal(expected, ObsDownloadLinks.IsTrustedAlternativeChannel(url));

    /// <summary>微软商店商品 ID 是官方下载页里那一个，不能被换成别的条目。</summary>
    [Fact]
    public void MicrosoftStorePointsAtTheOfficialProductPage()
        => Assert.EndsWith("/detail/XPFFH613W8V6LV", ObsDownloadLinks.MicrosoftStoreObs);

    [Fact]
    public void SafetyNoteMentionsTheSigner()
    {
        var note = ObsDownloadLinks.SafetyNote;
        Assert.Contains(ObsDownloadLinks.WindowsSignerName, note);
    }
}
