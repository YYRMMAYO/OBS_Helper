using OBS_Helper.Wpf.Services.Tools;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

public class VirtualCamCheckCoreTests
{
    [Fact]
    public void DriverRegistered_Ok_WithGuidanceItems()
    {
        var items = VirtualCamCheckCore.Evaluate(new VirtualCamCheckSnapshot
        {
            DriverRegistered = true,
            PluginDllPresent = true,
            ObsRunning = true
        });

        Assert.Contains(items, i => i.Status == "ok" && i.Title.Contains("已注册"));
        Assert.Contains(items, i => i.Title.Contains("使用方法"));
        Assert.Contains(items, i => i.Title.Contains("找不到时"));
    }

    [Fact]
    public void DriverMissing_PluginPresent_WarnsWithReregisterHint()
    {
        var items = VirtualCamCheckCore.Evaluate(new VirtualCamCheckSnapshot
        {
            DriverRegistered = false,
            PluginDllPresent = true,
            ObsRunning = false
        });

        var item = items.First(i => i.Title.Contains("未注册"));
        Assert.Equal("warn", item.Status);
        Assert.Contains("管理员", item.Detail);
        // 驱动缺失时不给后续使用指引（避免噪音），只有一条结论
        Assert.DoesNotContain(items, i => i.Title.Contains("使用方法"));
    }

    [Fact]
    public void DriverMissing_PluginMissing_ErrorsWithReinstallHint()
    {
        var items = VirtualCamCheckCore.Evaluate(new VirtualCamCheckSnapshot
        {
            DriverRegistered = false,
            PluginDllPresent = false,
            ObsRunning = false
        });

        var item = items.First(i => i.Title.Contains("未注册"));
        Assert.Equal("error", item.Status);
        Assert.Contains("重装", item.Detail);
    }

    [Fact]
    public void UnknownDriverState_DoesNotCrash()
    {
        var items = VirtualCamCheckCore.Evaluate(new VirtualCamCheckSnapshot());
        Assert.NotEmpty(items);
    }
}
