using OBS_Helper.Wpf.Services.SystemCheck;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

public class GraphicsEnvCheckCoreTests
{
    private static GraphicsEnvSnapshot Base() => new()
    {
        HwSchMode = 1,
        ObsGpuPreference = "2;",
        GameDvrEnabled = false,
        GameModeEnabled = true,
        Gpus = new List<GpuDriverInfo>
        {
            new("NVIDIA GeForce RTX 4070", "551.23", DriverDateMonthsAgo(3))
        },
        ActivePowerScheme = "平衡",
        OnBattery = false,
        Elevated = true
    };

    [Fact]
    public void HealthyDesktop_AllPass_NoWarns()
    {
        var items = GraphicsEnvCheckCore.Evaluate(Base());
        Assert.DoesNotContain(items, i => i.Status == "warn");
        Assert.Contains(items, i => i.Title.Contains("管理员权限") && i.Status == "ok");
    }

    [Fact]
    public void GpuPreferencePowerSaver_Warns()
    {
        var snapshot = CloneWith(Base(), obsGpuPreference: "1;");
        var items = GraphicsEnvCheckCore.Evaluate(snapshot);
        Assert.Contains(items, i => i.Status == "warn" && i.Title.Contains("省电"));
    }

    [Fact]
    public void DualGpuWithoutPreference_Warns()
    {
        var snapshot = CloneWith(Base(), gpus: new List<GpuDriverInfo>
        {
            new("Intel(R) UHD Graphics", "31.0.101.5333", DriverDateMonthsAgo(6)),
            new("NVIDIA GeForce RTX 4070", "551.23", DriverDateMonthsAgo(3))
        }, obsGpuPreference: null);
        var items = GraphicsEnvCheckCore.Evaluate(snapshot);
        Assert.Contains(items, i => i.Status == "warn" && i.Title.Contains("双显卡"));
    }

    [Fact]
    public void SingleGpuWithoutPreference_Ok()
    {
        var snapshot = CloneWith(Base(), obsGpuPreference: null);
        var items = GraphicsEnvCheckCore.Evaluate(snapshot);
        Assert.Contains(items, i => i.Status == "ok" && i.Title.Contains("GPU 偏好"));
    }

    [Fact]
    public void GameDvrOn_Warns_Off_Passes()
    {
        Assert.Contains(GraphicsEnvCheckCore.Evaluate(CloneWith(Base(), gameDvr: true)),
            i => i.Status == "warn" && i.Title.Contains("后台录制"));

        Assert.DoesNotContain(GraphicsEnvCheckCore.Evaluate(CloneWith(Base(), gameDvr: false)),
            i => i.Title.Contains("后台录制") && i.Status == "warn");
    }

    [Fact]
    public void OnBattery_Warns()
    {
        var items = GraphicsEnvCheckCore.Evaluate(CloneWith(Base(), onBattery: true));
        Assert.Contains(items, i => i.Status == "warn" && i.Title.Contains("电池"));
    }

    [Fact]
    public void OldDriver_Warns_NewDriver_Ok()
    {
        var old = CloneWith(Base(), gpus: new List<GpuDriverInfo>
        {
            new("AMD Radeon RX 6600", "31.0.1", DriverDateMonthsAgo(30))
        });
        Assert.Contains(GraphicsEnvCheckCore.Evaluate(old),
            i => i.Status == "warn" && i.Title.StartsWith("驱动较旧"));

        // 未知日期格式 → info 而非崩溃 / warn
        var unknown = CloneWith(Base(), gpus: new List<GpuDriverInfo> { new("X", "1", "") });
        Assert.Contains(GraphicsEnvCheckCore.Evaluate(unknown),
            i => i.Status == "info" && i.Title.StartsWith("驱动版本"));
    }

    [Fact]
    public void UnknownValues_DegradeToInfo_NotCrash()
    {
        var snapshot = new GraphicsEnvSnapshot(); // 全部未知
        var items = GraphicsEnvCheckCore.Evaluate(snapshot);
        Assert.All(items, i => Assert.NotEqual("error", i.Status));
    }

    [Theory]
    [InlineData(29)]
    [InlineData(30)]
    [InlineData(1)]
    [InlineData(0)]
    public void DriverAgeParsing(int monthsAgo)
    {
        var age = GraphicsEnvCheckCore.TryParseDriverAgeMonths(DriverDateMonthsAgo(monthsAgo));
        Assert.NotNull(age);

        // 期望值随运行日期漂移（跨月边界 / 闰年 2 月被 clamp），允许 ±1 个月误差。
        Assert.InRange(age!.Value, monthsAgo - 1, monthsAgo + 1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage!")]
    [InlineData("20")]
    public void DriverAgeParsing_Unparseable_ReturnsNull(string raw)
        => Assert.Null(GraphicsEnvCheckCore.TryParseDriverAgeMonths(raw));

    /// <summary>
    /// 造一个「N 个月前」的 WMI 驱动日期串（形如 <c>20240311000000.000000+480</c>）。
    ///
    /// 【为什么不用写死的日期】原来这里写死了 <c>20260801000000.000000+480</c> 并断言 age ≈ 0，
    /// 于是这份测试**随系统日期漂移**：2026-10 再跑就变成 2 个月，直接失败。
    /// 也就是说测试本身是有保质期的 —— 这里改成相对当前日期生成，越久跑越准。
    /// </summary>
    private static string DriverDateMonthsAgo(int months)
        => DateTime.Today.AddMonths(-months).ToString("yyyyMM01000000.000000+480");

    [Fact]
    public void IntegratedAndDiscreteDetection()
    {
        Assert.True(GraphicsEnvCheckCore.IsIntegratedName("Intel(R) UHD Graphics 770"));
        Assert.False(GraphicsEnvCheckCore.IsIntegratedName("NVIDIA GeForce RTX 4070"));
        Assert.True(GraphicsEnvCheckCore.IsDiscreteName("NVIDIA GeForce RTX 4070"));
        Assert.True(GraphicsEnvCheckCore.IsDiscreteName("AMD Radeon RX 7900 XT"));
        Assert.False(GraphicsEnvCheckCore.IsDiscreteName("Intel(R) UHD Graphics 770"));
    }

    private static GraphicsEnvSnapshot CloneWith(
        GraphicsEnvSnapshot baseSnapshot,
        string? obsGpuPreference = null,
        bool? gameDvr = null,
        List<GpuDriverInfo>? gpus = null,
        bool? onBattery = null)
        => new()
        {
            HwSchMode = baseSnapshot.HwSchMode,
            ObsGpuPreference = obsGpuPreference,
            GameDvrEnabled = gameDvr,
            GameModeEnabled = baseSnapshot.GameModeEnabled,
            Gpus = gpus ?? baseSnapshot.Gpus,
            ActivePowerScheme = baseSnapshot.ActivePowerScheme,
            OnBattery = onBattery,
            Elevated = baseSnapshot.Elevated
        };
}
