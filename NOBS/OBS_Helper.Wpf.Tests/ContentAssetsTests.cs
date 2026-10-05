using System.IO;
using OBS_Helper.Wpf.Localization;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// V2.9.3：随包资产的「按语言取」逻辑。
///
/// 这几条断言的共同背景：**回退必须是响的**。英文资产缺失时退回中文是对的，
/// 但绝不能静默 —— V2.9.1 修 raw 404 时踩过的坑就是「静态失败能潜伏很久」。
/// </summary>
public class ContentAssetsTests
{
    [Theory]
    [InlineData("problems", Strings.ZhHans, "OBS_Helper.Wpf.Assets.problems.json")]
    [InlineData("problems", "zh-CN", "OBS_Helper.Wpf.Assets.problems.json")]
    [InlineData("problems", Strings.EnUs, "OBS_Helper.Wpf.Assets.problems.en-US.json")]
    [InlineData("problems", "en", "OBS_Helper.Wpf.Assets.problems.en-US.json")]
    [InlineData("problems", "en-GB", "OBS_Helper.Wpf.Assets.problems.en-US.json")]
    [InlineData("plugins", Strings.EnUs, "OBS_Helper.Wpf.Assets.plugins.en-US.json")]
    [InlineData("scene_templates", Strings.EnUs, "OBS_Helper.Wpf.Assets.scene_templates.en-US.json")]
    [InlineData("troubleshooting", Strings.ZhHans, "OBS_Helper.Wpf.Assets.troubleshooting.md")]
    [InlineData("troubleshooting", Strings.EnUs, "OBS_Helper.Wpf.Assets.troubleshooting.en-US.md")]
    public void ResourceName_FollowsLanguage(string baseName, string language, string expected)
        => Assert.Equal(expected, ContentAssets.ResourceName(baseName, language));

    /// <summary>
    /// 中文文件名**不能**带语言后缀：raw 地址、Release 资产名、用户本机已下载的缓存
    /// 都写死了 <c>problems.json</c> 这个名字，改了就是静默回归。
    /// </summary>
    [Fact]
    public void ChineseFileNames_KeepBackwardCompatibleNames()
    {
        Assert.Equal("problems.json", ContentAssets.FileName(ContentAssets.Problems, Strings.ZhHans));
        Assert.Equal("plugins.json", ContentAssets.FileName(ContentAssets.Plugins, Strings.ZhHans));
        Assert.Equal("scene_templates.json", ContentAssets.FileName(ContentAssets.SceneTemplates, Strings.ZhHans));
        Assert.Equal("troubleshooting.md", ContentAssets.FileName(ContentAssets.Troubleshooting, Strings.ZhHans));
    }

    /// <summary>不认识的 / 空的 / null 语言一律当中文处理，绝不抛异常。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("fr-FR")]
    [InlineData("ja")]
    public void UnknownLanguage_FallsBackToChinese(string? language)
        => Assert.Equal("problems.json", ContentAssets.FileName(ContentAssets.Problems, language));

    [Fact]
    public void Resolve_PrefersRequestedLanguage()
    {
        var zh = "OBS_Helper.Wpf.Assets.problems.json";
        var en = "OBS_Helper.Wpf.Assets.problems.en-US.json";

        var text = ContentAssets.Resolve(
            name => name == en ? "EN" : name == zh ? "ZH" : null,
            ContentAssets.Problems, Strings.EnUs, out var fellBack);

        Assert.Equal("EN", text);
        Assert.False(fellBack);
    }

    /// <summary>
    /// 英文资产缺失 → 退回中文，并且**必须**把 fellBack 标成 true（调用方据此写 WARN）。
    /// </summary>
    [Fact]
    public void Resolve_MissingEnglish_FallsBackToChineseAndFlagsIt()
    {
        var zh = "OBS_Helper.Wpf.Assets.problems.json";

        var text = ContentAssets.Resolve(
            name => name == zh ? "ZH" : null,
            ContentAssets.Problems, Strings.EnUs, out var fellBack);

        Assert.Equal("ZH", text);
        Assert.True(fellBack);
    }

    [Fact]
    public void Resolve_MissingEverything_ReturnsNullAndDoesNotFlagFallback()
    {
        var text = ContentAssets.Resolve(_ => null, ContentAssets.Problems, Strings.EnUs, out var fellBack);
        Assert.Null(text);
        Assert.False(fellBack);
    }

    [Fact]
    public void Resolve_ChineseRequestNeverFallsBackMarked()
    {
        var text = ContentAssets.Resolve(_ => null, ContentAssets.Problems, Strings.ZhHans, out var fellBack);
        Assert.Null(text);
        Assert.False(fellBack);
    }

    /// <summary>空白内容视为「没有」，否则一个只剩换行的资产会把界面上全部内容清空。</summary>
    [Fact]
    public void Resolve_WhitespaceOnlyContentTreatedAsMissing()
    {
        var en = "OBS_Helper.Wpf.Assets.problems.en-US.json";
        var zh = "OBS_Helper.Wpf.Assets.problems.json";

        var text = ContentAssets.Resolve(
            name => name == en ? "   \n " : name == zh ? "ZH" : null,
            ContentAssets.Problems, Strings.EnUs, out var fellBack);

        Assert.Equal("ZH", text);
        Assert.True(fellBack);
    }

    /// <summary>
    /// 内嵌资源真的在程序集里 —— 单测程序集里没有这些资源，所以这里只断言
    /// 「资源名拼法与 csproj LogicalName 一致」这件事的字符串形状。
    /// 真实存在性由 KnowledgeBaseUrlsTests 的源码树校验 + 应用自身启动自检覆盖。
    /// </summary>
    [Fact]
    public void ResourceNames_UseTheDocumentedPrefix()
        => Assert.StartsWith(ContentAssets.ResourcePrefix,
            ContentAssets.ResourceName(ContentAssets.Problems, Strings.EnUs));

    [Fact]
    public void AssetFiles_ArePresentOnDisk()
    {
        foreach (var name in new[]
                 {
                     ContentAssets.FileName(ContentAssets.Problems, Strings.ZhHans),
                     ContentAssets.FileName(ContentAssets.Problems, Strings.EnUs),
                     ContentAssets.FileName(ContentAssets.Plugins, Strings.ZhHans),
                     ContentAssets.FileName(ContentAssets.Plugins, Strings.EnUs),
                     ContentAssets.FileName(ContentAssets.SceneTemplates, Strings.ZhHans),
                     ContentAssets.FileName(ContentAssets.SceneTemplates, Strings.EnUs),
                     ContentAssets.FileName(ContentAssets.Troubleshooting, Strings.ZhHans),
                     ContentAssets.FileName(ContentAssets.Troubleshooting, Strings.EnUs),
                 })
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", name);
            Assert.True(File.Exists(path), $"缺少随包资产：{name}");
        }
    }
}
