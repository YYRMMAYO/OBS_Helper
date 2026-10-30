using System.Text.Json;
using System.Text.Json.Nodes;
using OBS_Helper.Wpf.Models.Live;
using OBS_Helper.Wpf.Services.Live;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// 开播引导的纯逻辑（V3.3.0 / P0-4）：模式、推流码遮罩、OBS 请求体、状态解析与错误分类。
/// 推流码泄漏 / 遮罩写错都属于「一上生产就出事」的类别，这里逐条钉住。
/// </summary>
public class GoLiveCoreTests
{
    [Theory]
    [InlineData("direct", GoLiveMode.Direct)]
    [InlineData("virtualcam", GoLiveMode.VirtualCamera)]
    [InlineData("manual", GoLiveMode.Manual)]
    [InlineData("unknown", GoLiveMode.Manual)]
    [InlineData(null, GoLiveMode.Manual)]
    public void ParseMode_MapsKnownValues(string? mode, GoLiveMode expected)
        => Assert.Equal(expected, GoLiveCore.ParseMode(mode));

    [Theory]
    [InlineData("rtmp://live.example.com/live", "rtmp://live.example.com/live")]
    [InlineData("rtmps://a.rtmps.youtube.com/live2", "rtmps://a.rtmps.youtube.com/live2")]
    [InlineData("  rtmp://x/y  ", "rtmp://x/y")]
    public void NormalizeServer_AcceptsRtmp(string input, string expected)
        => Assert.Equal(expected, GoLiveCore.NormalizeServer(input));

    [Theory]
    [InlineData("")]
    [InlineData("http://example.com")]
    [InlineData("ftp://example.com")]
    [InlineData("rtmp://bad\nhost")]
    public void NormalizeServer_RejectsUnsafe(string input)
        => Assert.Null(GoLiveCore.NormalizeServer(input));

    [Fact]
    public void NormalizeKey_StripsWhitespaceAndNewlines()
    {
        Assert.Equal("abcdef123456", GoLiveCore.NormalizeKey("  abcd\r\n ef12 3456 "));
        Assert.Null(GoLiveCore.NormalizeKey("   "));
        Assert.Null(GoLiveCore.NormalizeKey(new string('x', GoLiveCore.MaxKeyLength + 1)));
    }

    [Fact]
    public void MaskKey_RevealsNoPlaintextCharacters()
    {
        const string key = "live_1234567890_SECRET";
        var masked = GoLiveCore.MaskKey(key);

        Assert.DoesNotContain("SECRET", masked, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("live", masked, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(key[0].ToString(), masked);
        Assert.Contains($"({key.Length})", masked);
        Assert.Equal("", GoLiveCore.MaskKey(""));
    }

    [Fact]
    public void MaskServer_DropsPathAndQuery()
    {
        Assert.Equal("rtmp://live.example.com", GoLiveCore.MaskServer("rtmp://live.example.com/app?token=secret"));
        Assert.Equal("rtmp://live.example.com:1936", GoLiveCore.MaskServer("rtmp://live.example.com:1936/app"));
        Assert.Equal("", GoLiveCore.MaskServer("http://example.com"));
    }

    [Fact]
    public void ValidateDirectInput_ReturnsStableCodes()
    {
        Assert.Equal("GOLIVE_BAD_SERVER", GoLiveCore.ValidateDirectInput("http://x", "key"));
        Assert.Equal("GOLIVE_NO_KEY", GoLiveCore.ValidateDirectInput("rtmp://x", ""));
        Assert.Equal("GOLIVE_KEY_TOO_LONG",
            GoLiveCore.ValidateDirectInput("rtmp://x", new string('k', GoLiveCore.MaxKeyLength + 1)));
        Assert.Equal("", GoLiveCore.ValidateDirectInput("rtmp://x", "key"));
    }

    [Fact]
    public void BuildStreamServiceSettings_UsesRtmpCustom()
    {
        var body = GoLiveCore.BuildStreamServiceSettings("rtmp://x/app", "k1");

        Assert.Equal("rtmp_custom", body["streamServiceType"]!.GetValue<string>());
        var settings = body["streamServiceSettings"]!.AsObject();
        Assert.Equal("rtmp://x/app", settings["server"]!.GetValue<string>());
        Assert.Equal("k1", settings["key"]!.GetValue<string>());
    }

    [Fact]
    public void ParseStreamStatus_ReadsActiveAndCounters()
    {
        using var doc = JsonDocument.Parse("""
        {
          "outputActive": true,
          "outputReconnecting": false,
          "outputTimecode": "00:01:02.500",
          "outputCongestion": 0.12,
          "outputSkippedFrames": 3,
          "outputTotalFrames": 900
        }
        """);

        var status = GoLiveCore.ParseStreamStatus(doc.RootElement);

        Assert.True(status.Active);
        Assert.False(status.Reconnecting);
        Assert.Equal("00:01:02.500", status.Timecode);
        Assert.Equal(0.12, status.Congestion, 3);
        Assert.Equal(3, status.SkippedFrames);
        Assert.Equal(900, status.TotalFrames);
    }

    [Fact]
    public void ParseStreamStatus_DetectsReconnectingState()
    {
        using var doc = JsonDocument.Parse("""
        { "outputActive": true, "outputState": "OBS_WEBSOCKET_OUTPUT_RECONNECTING" }
        """);

        Assert.True(GoLiveCore.ParseStreamStatus(doc.RootElement).Reconnecting);
    }

    [Theory]
    [InlineData("invalid stream key", "GOLIVE_KEY_INVALID")]
    [InlineData("Failed to connect to server: timeout", "GOLIVE_SERVER_UNREACHABLE")]
    [InlineData("NVENC encoder failed", "GOLIVE_ENCODER_FAILED")]
    [InlineData("dropped frames due to network", "GOLIVE_NETWORK")]
    [InlineData("", "GOLIVE_STREAM_FAILED")]
    public void ClassifyStreamError_MapsToActionableCodes(string message, string expected)
        => Assert.Equal(expected, GoLiveCore.ClassifyStreamError(message));

    [Fact]
    public void SupportsDirect_OnlyForDirectPlatforms()
    {
        Assert.True(GoLiveCore.SupportsDirect(new LivePlatform { Mode = "direct" }));
        Assert.False(GoLiveCore.SupportsDirect(new LivePlatform { Mode = "virtualcam" }));
        Assert.False(GoLiveCore.SupportsDirect(null));
    }
}

/// <summary>第三方客户端探测规则（V3.3.0 / P0-4）。</summary>
public class CompanionAppCoreTests
{
    private static readonly CompanionApp App = new()
    {
        Id = "douyin-live",
        Name = "抖音直播伴侣",
        ProcessNames = new List<string> { "直播伴侣", "LiveCompanion" },
        InstallDirectories = new List<string> { "%TEMP%\\nobs-no-such-dir" },
        UninstallNames = new List<string> { "抖音直播伴侣" }
    };

    [Theory]
    [InlineData("LiveCompanion", true)]
    [InlineData("LiveCompanion.exe", true)]
    [InlineData("livecompanion", true)]
    [InlineData("obs64", false)]
    [InlineData("", false)]
    public void MatchesProcess_IsCaseInsensitiveAndIgnoresExe(string name, bool expected)
        => Assert.Equal(expected, CompanionAppCore.MatchesProcess(name, App));

    [Theory]
    [InlineData("抖音直播伴侣 3.2.1", true)]
    [InlineData("Douyin Live Companion", false)]
    [InlineData("Visual Studio", false)]
    public void MatchesUninstallName_AllowsVersionSuffix(string name, bool expected)
        => Assert.Equal(expected, CompanionAppCore.MatchesUninstallName(name, App));

    [Fact]
    public void ExpandPath_ExpandsEnvironmentVariables()
    {
        var expanded = CompanionAppCore.ExpandPath("%TEMP%\\x");
        Assert.DoesNotContain("%TEMP%", expanded);
        Assert.EndsWith("\\x", expanded);
    }

    [Fact]
    public void FindInstallDirectory_UsesInjectedProbe()
    {
        var found = CompanionAppCore.FindInstallDirectory(App, p => p.Contains("nobs-no-such-dir"));
        Assert.NotNull(found);

        Assert.Null(CompanionAppCore.FindInstallDirectory(App, _ => false));
        Assert.Null(CompanionAppCore.FindInstallDirectory(null));
    }

    [Fact]
    public void Combine_InstalledWhenAnySignalHits()
    {
        Assert.True(CompanionAppCore.Combine(App, running: false, registryHit: false, installPath: @"C:\x").Installed);
        Assert.True(CompanionAppCore.Combine(App, running: true, registryHit: false, installPath: null).Installed);
        Assert.True(CompanionAppCore.Combine(App, running: false, registryHit: true, installPath: null).Installed);
        Assert.False(CompanionAppCore.Combine(App, running: false, registryHit: false, installPath: null).Installed);
    }

    [Theory]
    [InlineData("douyin", "golive.guide.douyin")]
    [InlineData("tiktok", "golive.guide.tiktok")]
    [InlineData("bilibili", "golive.guide.bilibili")]
    [InlineData("custom-rtmp", "golive.guide.generic")]
    public void VirtualCameraGuideKey_IsPlatformSpecific(string id, string expectedKey)
        => Assert.Equal(expectedKey, CompanionAppCore.VirtualCameraGuideKey(new LivePlatform { Id = id }));
}

/// <summary>随包平台目录的自洽性（V3.3.0）：中英 id 一致、模式合法、客户端引用存在。</summary>
public class PlatformCatalogAssetTests
{
    [Fact]
    public void Catalog_IsConsistentInBothLanguages()
    {
        var zh = Load("platforms.json");
        var en = Load("platforms.en-US.json");

        Assert.Equal(16, zh.Platforms.Count);
        Assert.Equal(zh.Platforms.Select(p => p.Id).OrderBy(x => x),
            en.Platforms.Select(p => p.Id).OrderBy(x => x));

        var validModes = new[] { "direct", "virtualcam", "manual" };
        Assert.All(zh.Platforms, p => Assert.Contains(p.Mode, validModes));
        Assert.All(zh.Platforms, p => Assert.False(string.IsNullOrWhiteSpace(p.Name)));

        var companionIds = zh.Companions.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.All(zh.Platforms.SelectMany(p => p.Companions), id => Assert.Contains(id, companionIds));
        Assert.All(en.Platforms.SelectMany(p => p.Companions), id => Assert.Contains(id, companionIds));

        // 英文资产不能残留中文（与场景模板同一条规则）
        foreach (var p in en.Platforms)
        {
            Assert.DoesNotMatch(@"[\u4e00-\u9fff]", p.Name + p.Notes);
        }
    }

    [Fact]
    public void Catalog_ContainsBothChineseAndGlobalPlatforms()
    {
        var zh = Load("platforms.json");

        Assert.Contains(zh.Platforms, p => p.Id == "bilibili" && p.Mode == "direct");
        Assert.Contains(zh.Platforms, p => p.Id == "douyin" && p.Mode == "virtualcam");
        Assert.Contains(zh.Platforms, p => p.Id == "youtube" && p.Mode == "direct");
        Assert.Contains(zh.Platforms, p => p.Id == "custom-rtmp" && p.Mode == "direct");
        Assert.Contains(zh.Platforms, p => p.Id == "instagram" && p.Mode == "manual");

        // 已核验的条目必须带日期，未核验的必须为空（不假装核验过）
        Assert.All(zh.Platforms, p =>
        {
            if (p.Id is "bilibili" or "douyin" or "youtube") Assert.False(string.IsNullOrWhiteSpace(p.VerifiedAt));
        });
    }

    private static PlatformCatalogData Load(string file)
    {
        var path = Path.Combine(SourceRoot(), "OBS_Helper.Wpf", "Assets", file);
        Assert.True(File.Exists(path), path);
        var data = JsonSerializer.Deserialize<PlatformCatalogData>(
            File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(data);
        return data!;
    }

    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "OBS_Helper.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("找不到源码根目录（OBS_Helper.slnx）");
    }
}
