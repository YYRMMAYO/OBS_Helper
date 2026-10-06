using OBS_Helper.Wpf.Localization;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// 内容资产回退提示（V3.0 / E5）的判定与文案测试。
///
/// 这一项的存在理由就是「不能静默」：英文资产缺失时以前只写日志、界面上什么都不说，
/// 英文用户只会觉得「怎么一半是中文」。因此三个判定条件（发生过回退 / 非中文界面 / 用户没点过忽略）
/// 与「必须点名是哪几块内容」都要有断言。
///
/// 用 IDisposable 在用例之间复位静态登记处：它是进程级状态，串起来会让用例互相干扰。
/// </summary>
public class FallbackNoticeTests : IDisposable
{
    public FallbackNoticeTests() => FallbackNotice.Reset();

    public void Dispose() => FallbackNotice.Reset();

    // ---------------------------------------------------------------- 登记处

    [Fact]
    public void Report_RecordsAssetOnce()
    {
        FallbackNotice.Report(ContentAssets.Problems);
        FallbackNotice.Report(ContentAssets.Problems);
        FallbackNotice.Report(ContentAssets.Plugins);

        Assert.True(FallbackNotice.HasFallback);
        Assert.Equal(new[] { ContentAssets.Problems, ContentAssets.Plugins }, FallbackNotice.FallbackAssets);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Report_IgnoresEmptyAssetName(string? baseName)
    {
        FallbackNotice.Report(baseName!);
        Assert.False(FallbackNotice.HasFallback);
    }

    /// <summary>
    /// 资产补齐后必须能**撤销**登记（V3.0 第三轮验证）：
    /// 只 Report 不 Clear 的话，后台把英文资产下下来、界面已是英文内容，提示条仍在说「缺失」。
    /// </summary>
    [Fact]
    public void Clear_RemovesSingleAssetOnly()
    {
        FallbackNotice.Report(ContentAssets.Problems);
        FallbackNotice.Report(ContentAssets.Plugins);

        FallbackNotice.Clear(ContentAssets.Plugins);

        Assert.Equal(new[] { ContentAssets.Problems }, FallbackNotice.FallbackAssets);

        FallbackNotice.Clear(ContentAssets.Problems);
        Assert.False(FallbackNotice.HasFallback);
    }

    /// <summary>Clear 一个没登记过的资产不该触发事件（避免无谓的界面刷新）。</summary>
    [Fact]
    public void Clear_UnknownAssetDoesNotRaise()
    {
        var count = 0;
        void Handler() => count++;
        FallbackNotice.Changed += Handler;
        try
        {
            FallbackNotice.Clear("never-reported");
            Assert.Equal(0, count);
        }
        finally { FallbackNotice.Changed -= Handler; }
    }

    [Fact]
    public void Clear_EmptyNameIsIgnored()
    {
        FallbackNotice.Report(ContentAssets.Problems);
        FallbackNotice.Clear("");
        Assert.True(FallbackNotice.HasFallback);
    }

    [Fact]
    public void Reset_ClearsAssetsAndDismissal()
    {
        FallbackNotice.Report(ContentAssets.Problems);
        FallbackNotice.Dismiss();

        FallbackNotice.Reset();

        Assert.False(FallbackNotice.HasFallback);
        Assert.False(FallbackNotice.Dismissed);
    }

    [Fact]
    public void Changed_FiresOnNewAssetAndOnDismissButNotOnRepeat()
    {
        var count = 0;
        void Handler() => count++;
        FallbackNotice.Changed += Handler;
        try
        {
            FallbackNotice.Report(ContentAssets.Problems);
            Assert.Equal(1, count);

            FallbackNotice.Report(ContentAssets.Problems);   // 重复上报不重复通知
            Assert.Equal(1, count);

            FallbackNotice.Dismiss();
            Assert.Equal(2, count);

            FallbackNotice.Dismiss();                        // 已忽略再点不重复通知
            Assert.Equal(2, count);
        }
        finally
        {
            FallbackNotice.Changed -= Handler;
        }
    }

    /// <summary>事件订阅者抛异常不能影响上报方（提示条刷新失败不该拖垮知识库加载）。</summary>
    [Fact]
    public void Report_SurvivesThrowingSubscriber()
    {
        void Boom() => throw new InvalidOperationException("boom");
        FallbackNotice.Changed += Boom;
        try
        {
            FallbackNotice.Report(ContentAssets.Problems);
            Assert.True(FallbackNotice.HasFallback);
        }
        finally
        {
            FallbackNotice.Changed -= Boom;
        }
    }

    // ---------------------------------------------------------------- 判定

    [Theory]
    [InlineData("en-US", true, false, true)]     // 英文 + 有回退 + 未忽略 → 提示
    [InlineData("en-US", true, true, false)]     // 点了忽略 → 不提示
    [InlineData("en-US", false, false, false)]   // 没回退 → 不提示
    [InlineData("zh-Hans", true, false, false)]  // 中文界面：回退到中文本来就对 → 不提示
    [InlineData("", true, false, false)]         // 空语言（= 默认中文）→ 不提示
    public void ShouldShow_CoversAllThreeConditions(string language, bool hasFallback, bool dismissed, bool expected)
        => Assert.Equal(expected, FallbackNoticeCore.ShouldShow(language, hasFallback, dismissed));

    // ---------------------------------------------------------------- 文案

    [Fact]
    public void BuildMessage_NamesAffectedAssets()
    {
        var message = FallbackNoticeCore.BuildMessage(
            Strings.EnUs,
            new[] { ContentAssets.Problems, ContentAssets.Plugins },
            hasFallback: true, dismissed: false);

        Assert.Contains(FallbackNoticeCore.AssetLabel(ContentAssets.Problems), message);
        Assert.Contains(FallbackNoticeCore.AssetLabel(ContentAssets.Plugins), message);
        // 不受影响的资产不该被点名
        Assert.DoesNotContain(FallbackNoticeCore.AssetLabel(ContentAssets.SceneTemplates), message);
    }

    [Fact]
    public void BuildMessage_EmptyWhenNotShowing()
    {
        Assert.Equal("", FallbackNoticeCore.BuildMessage(Strings.ZhHans, new[] { ContentAssets.Problems }, true, false));
        Assert.Equal("", FallbackNoticeCore.BuildMessage(Strings.EnUs, new[] { ContentAssets.Problems }, true, dismissed: true));
        Assert.Equal("", FallbackNoticeCore.BuildMessage(Strings.EnUs, Array.Empty<string>(), hasFallback: false, dismissed: false));
    }

    /// <summary>四份资产都有可读的中文名；未知基名原样返回（不把内部名藏起来）。</summary>
    [Fact]
    public void AssetLabel_CoversAllContentAssets()
    {
        foreach (var asset in new[]
                 {
                     ContentAssets.Problems, ContentAssets.Plugins,
                     ContentAssets.SceneTemplates, ContentAssets.Troubleshooting
                 })
        {
            var label = FallbackNoticeCore.AssetLabel(asset);
            Assert.False(string.IsNullOrWhiteSpace(label));
            Assert.NotEqual(asset, label);
        }

        Assert.Equal("unknown-asset", FallbackNoticeCore.AssetLabel("unknown-asset"));
        Assert.Equal("", FallbackNoticeCore.AssetLabel(null));
    }

    /// <summary>两个语言的提示文案都要点名具体资产占位符，而不是只说「出问题了」。</summary>
    [Fact]
    public void MessageTemplate_HasPlaceholder()
    {
        Assert.Contains("{0}", Strings.Table(Strings.ZhHans)["i18n.fallback.messageList"]);
        Assert.Contains("{0}", Strings.Table(Strings.EnUs)["i18n.fallback.messageList"]);
    }
}
