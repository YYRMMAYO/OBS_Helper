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
            new("NVIDIA GeForce RTX 4070", "551.23", "20260301000000.000000+480")
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
            new("Intel(R) UHD Graphics", "31.0.101.5333", "20250601000000.000000+480"),
            new("NVIDIA GeForce RTX 4070", "551.23", "20260301000000.000000+480")
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
            new("AMD Radeon RX 6600", "31.0.1", "20230101000000.000000+480")
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
    [InlineData("20240311000000.000000+480", 29)]
    [InlineData("20260801000000.000000+480", 0)]
    [InlineData("", -1)]
    [InlineData("garbage!", -1)]
    public void DriverAgeParsing(string raw, int expected)
    {
        var age = GraphicsEnvCheckCore.TryParseDriverAgeMonths(raw);
        if (expected < 0)
        {
            Assert.Null(age);
        }
        else
        {
            // 期望值随运行日期漂移，允许 ±1 个月误差
            Assert.InRange(age!.Value, expected - 1, expected + 1);
        }
    }

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
