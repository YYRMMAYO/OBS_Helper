using OBS_Helper.Wpf.Services;
using OBS_Helper.Wpf.Services.Update;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// dev（测试）通道的编号规则：dev tag 是 <c>01</c>、<c>02</c>、<c>03</c>（纯数字、<b>不带字母 V</b>），
/// 与正式版的 <c>V3.1.0</c> 体系完全隔离 —— 两类 tag 挂在同一个仓库里时，谁也不会被对方的通道当成新版本。
///
/// 这组用例同时守住「dev 编号不许写成 V01 / 1.0 / 01-beta」这条约定：
/// 一旦写歪，应用会静默地认为「没有新 dev 版本」，用户永远收不到提示。
/// </summary>
public class DevChannelTests
{
    [Theory]
    [InlineData("01", 1)]
    [InlineData("02", 2)]
    [InlineData("1", 1)]
    [InlineData(" 03 ", 3)]
    [InlineData("100", 100)]
    public void TryParseDevTag_AcceptsPureNumbers(string tag, int expected)
    {
        Assert.True(UpdateChannel.TryParseDevTag(tag, out var build));
        Assert.Equal(expected, build);
    }

    [Theory]
    [InlineData("V3.1.0")]
    [InlineData("v1.4.8")]
    [InlineData("3.1.0")]
    [InlineData("V01")]
    [InlineData("01-beta")]
    [InlineData("01.2")]
    [InlineData("1000")]
    [InlineData("-1")]
    [InlineData("dev01")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParseDevTag_RejectsEverythingElse(string? tag)
    {
        Assert.False(UpdateChannel.TryParseDevTag(tag, out var build));
        Assert.Equal(0, build);
    }

    [Theory]
    [InlineData(1, "01")]
    [InlineData(2, "02")]
    [InlineData(9, "09")]
    [InlineData(12, "12")]
    public void FormatDevBuild_IsZeroPadded(int build, string expected)
        => Assert.Equal(expected, UpdateChannel.FormatDevBuild(build));

    [Fact]
    public void DevBuildNumber_RoundTripsThroughItsOwnLabel()
    {
        Assert.True(UpdateChannel.TryParseDevTag(UpdateChannel.DevBuildLabel, out var parsed));
        Assert.Equal(UpdateChannel.DevBuildNumber, parsed);
        Assert.Equal("DEV " + UpdateChannel.DevBuildLabel, UpdateChannel.DevVersionLabel);
    }

    [Fact]
    public void CurrentChannelVersion_IsTheDevBuildNumber()
    {
        // dev 分支恒为 dev 通道：当前版本按「构建序号」比，不拿 3.1.0 去和 01 比
        Assert.True(UpdateChannel.IsDev);
        Assert.Equal(new Version(UpdateChannel.DevBuildNumber, 0, 0), UpdateService.CurrentChannelVersion);
    }

    [Theory]
    [InlineData("02", 2)]
    [InlineData("1", 1)]
    public void ParseChannelVersion_ReadsDevTags(string tag, int build)
        => Assert.Equal(new Version(build, 0, 0), UpdateService.ParseChannelVersion(tag));

    [Theory]
    [InlineData("V3.1.0")]
    [InlineData("3.1.0")]
    [InlineData("v2.9.6")]
    [InlineData("")]
    [InlineData(null)]
    public void ParseChannelVersion_IgnoresStableTags(string? tag)
        => Assert.Null(UpdateService.ParseChannelVersion(tag));

    [Fact]
    public void ChannelVersions_DisplayWithoutLetterV()
    {
        Assert.Equal("DEV 02", UpdateService.FormatChannelVersion(new Version(2, 0, 0)));
        Assert.Equal("02", UpdateService.ChannelVersionNumber(new Version(2, 0, 0)));
        Assert.Equal("—", UpdateService.FormatChannelVersion(null));
        Assert.Equal("—", UpdateService.ChannelVersionNumber(null));
    }

    [Fact]
    public void FindLatestDevTagged_PicksHighestDevTagAndSkipsStableTags()
    {
        const string json = """
        [
          { "tag_name": "V3.1.0" },
          { "tag_name": "01" },
          { "tag_name": "not-a-dev-tag" },
          { "tag_name": "03" },
          { "tag_name": "02" }
        ]
        """;

        var (version, tag) = UpdateService.FindLatestDevTagged(json, "tag_name");
        Assert.Equal(new Version(3, 0, 0), version);
        Assert.Equal("03", tag);
    }

    [Fact]
    public void FindLatestDevTagged_HandlesTagsApiShape()
    {
        // tags 接口的字段名是 name（Release 是 tag_name），两条来源都要认
        const string json = """[ { "name": "01" }, { "name": "V2.9.6" } ]""";
        var (version, tag) = UpdateService.FindLatestDevTagged(json, "name");
        Assert.Equal(new Version(1, 0, 0), version);
        Assert.Equal("01", tag);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("""{ "message": "Not Found" }""")]
    [InlineData(null)]
    public void FindLatestDevTagged_ReturnsNothingWhenNoDevTag(string? json)
    {
        var (version, tag) = UpdateService.FindLatestDevTagged(json, "tag_name");
        Assert.Null(version);
        Assert.Null(tag);
    }

    [Fact]
    public void ReleasePageUrl_TargetsDevReleasePage()
    {
        Assert.Equal("https://github.com/YYRMMAYO/OBS_Helper/releases", UpdateService.ReleasesPageUrl);
        Assert.Equal("https://github.com/YYRMMAYO/OBS_Helper/releases/tag/02",
            UpdateService.ReleaseTagPageUrl("02"));
        Assert.Equal("https://github.com/YYRMMAYO/OBS_Helper/releases/tag/03",
            UpdateService.GetReleasePageUrl(" 03 "));
        Assert.Equal(UpdateService.ReleasesPageUrl, UpdateService.GetReleasePageUrl(null));
    }
}
