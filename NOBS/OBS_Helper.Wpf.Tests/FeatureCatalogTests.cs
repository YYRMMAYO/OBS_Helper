using OBS_Helper.Wpf.Navigation;
using OBS_Helper.Wpf.Services.Shell;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>功能中心目录：46 项能力必须全部登记、ID 唯一、路由存在。</summary>
public class FeatureCatalogTests
{
    [Fact]
    public void CatalogCoversAllFortySixNewFeatures()
        => Assert.Equal(46, FeatureCatalog.NewFeatureCount);

    [Fact]
    public void CatalogAlsoCoversConvenientOlderFeatures()
        => Assert.True(FeatureCatalog.FeatureCount > 46);

    [Fact]
    public void FeatureIdsAreUnique()
    {
        var ids = FeatureCatalog.Modules.SelectMany(m => m.Features).Select(f => f.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EveryModuleHasFeaturesAndAKnownRoute()
    {
        var routes = new[]
        {
            Routes.Features, Routes.MobileLink, Routes.Interaction, Routes.Insights, Routes.Stream, Routes.Automation,
            Routes.Home, Routes.Console, Routes.Toolbox, Routes.ObsConfig, Routes.VirtualLive, Routes.Plugins,
            Routes.Settings, Routes.Diagnostic, Routes.Assistant, Routes.Search, Routes.Logs, Routes.Setup,
            Routes.Performance, Routes.Templates, Routes.Guide,
        };
        foreach (var module in FeatureCatalog.Modules)
        {
            Assert.NotEmpty(module.Features);
            Assert.Contains(module.Route, routes);
            Assert.False(string.IsNullOrWhiteSpace(module.TitleKey));
            Assert.False(string.IsNullOrWhiteSpace(module.DescKey));
        }
    }

    [Fact]
    public void PhoneLinkIsTheFeaturedModule()
    {
        var featured = Assert.Single(FeatureCatalog.Modules.Where(m => m.Highlighted));
        Assert.Equal("mobile", featured.Id);
        Assert.Equal(12, featured.Features.Count);
    }

    [Fact]
    public void FeatureEntriesPointAtRealRoutes()
    {
        var routes = new[]
        {
            Routes.MobileLink, Routes.Interaction, Routes.Insights, Routes.Stream, Routes.Automation,
            Routes.Home, Routes.Console, Routes.Toolbox, Routes.ObsConfig, Routes.VirtualLive, Routes.Plugins,
            Routes.Settings, Routes.Diagnostic, Routes.Assistant, Routes.Search, Routes.Logs, Routes.Setup,
            Routes.Performance, Routes.Templates, Routes.Guide,
        };
        foreach (var feature in FeatureCatalog.Modules.SelectMany(m => m.Features))
            Assert.Contains(feature.Route, routes);
    }
}
