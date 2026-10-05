using System.Linq;
using OBS_Helper.Wpf.Services.ObsConfig;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// V2.9.3：一键部署录制环境的核心逻辑。
///
/// 这一块最怕的不是「算错推荐值」，而是**改坏用户的 OBS 配置**：
/// 所以这里重点钉的是 INI 打补丁的保真性（只动该动的那一行、其余逐字保留）
/// 与「已有值 → 推荐值」的判定。
/// </summary>
public class RecordingEnvCoreTests
{
    private static RecordingEnvSnapshot Simple() => new()
    {
        OutputMode = "Simple",
        RecFormat = "mkv",
        RecQuality = "Stream",
        RecPath = @"D:\Rec",
        BaseCx = "1920",
        BaseCy = "1080",
        OutCx = "1280",
        OutCy = "720",
        FpsCommon = "30",
        SampleRate = "44100",
    };

    [Fact]
    public void Build_AlwaysReturnsTheSameSixKeysInOrder()
    {
        var items = RecordingEnvCore.Build(Simple(), @"D:\Rec");
        Assert.Equal(
            new[] { "format", "quality", "path", "canvas", "fps", "sampleRate" },
            items.Select(i => i.Key));
    }

    [Fact]
    public void Build_TargetsOnlyRecordingRelatedParameters()
    {
        var items = RecordingEnvCore.Build(Simple(), @"D:\Rec");
        var parameters = items.SelectMany(i => i.Targets).Select(t => t.Parameter).ToHashSet();

        Assert.Contains("RecFormat2", parameters);
        Assert.Contains("FilePath", parameters);
        Assert.Contains("BaseCX", parameters);
        Assert.Contains("FPSCommon", parameters);
        Assert.Contains("SampleRate", parameters);

        // 推流参数、输出模式一律不碰：那些属于用户自己的选择，改了会连带影响正在用的工作流。
        Assert.DoesNotContain("Mode", parameters);
        Assert.DoesNotContain("StreamEncoder", parameters);
        Assert.DoesNotContain("VBitrate", parameters);
    }

    [Fact]
    public void Build_MarksAlreadyRecommendedItems()
    {
        var snapshot = Simple() with
        {
            RecFormat = RecordingEnvCore.HybridMp4,
            RecQuality = RecordingEnvCore.HighQuality,
            FpsCommon = RecordingEnvCore.Fps.ToString(),
            BaseCx = "1920", BaseCy = "1080", OutCx = "1920", OutCy = "1080",
            SampleRate = RecordingEnvCore.SampleRate.ToString(),
        };

        var items = RecordingEnvCore.Build(snapshot, @"D:\Rec");
        Assert.True(items.Single(i => i.Key == "format").AlreadyOk);
        Assert.True(items.Single(i => i.Key == "quality").AlreadyOk);
        Assert.True(items.Single(i => i.Key == "canvas").AlreadyOk);
        Assert.True(items.Single(i => i.Key == "fps").AlreadyOk);
        Assert.True(items.Single(i => i.Key == "sampleRate").AlreadyOk);

        // 录像目录是外部传入的，与本机实际路径比较，不参与「已符合」的批量断言
        Assert.Equal(@"D:\Rec", items.Single(i => i.Key == "path").Recommended);
    }

    /// <summary>采样率只有 obs-websocket 通道能改：文件通道必须把它排除掉。</summary>
    [Fact]
    public void Build_SampleRateIsWebSocketOnly()
    {
        var items = RecordingEnvCore.Build(Simple(), @"D:\Rec");
        Assert.True(items.Single(i => i.Key == "sampleRate").WebSocketOnly);
        Assert.All(items.Where(i => i.Key != "sampleRate"), i => Assert.False(i.WebSocketOnly));
    }

    [Fact]
    public void Join4_FormatsCanvasDisplay()
    {
        Assert.Equal("1920x1080", RecordingEnvCore.Join4("1920", "1080", "1920", "1080"));
        Assert.Equal("1920x1080>1280x720", RecordingEnvCore.Join4("1920", "1080", "1280", "720"));
        Assert.Equal("", RecordingEnvCore.Join4("", "", "", ""));
        // 只有一边读得到时给那一边，而不是拼出 ">1920x1080" 这种半截字符串
        Assert.Equal("1920x1080", RecordingEnvCore.Join4("1920", "", "1920", "1080"));
        Assert.Equal("1920x1080", RecordingEnvCore.Join4("1920", "1080", "", ""));
    }

    // ------------------------------------------------------------ INI 读写

    [Fact]
    public void PatchIni_ReplacesExistingKeyInPlace()
    {
        var ini = "[Video]\nBaseCX=1280\nBaseCY=720\n\n[Output]\nMode=Simple\n";
        var patched = RecordingEnvCore.PatchIni(ini, "Video", "BaseCX", "1920");

        Assert.Contains("BaseCX=1920", patched);
        Assert.Contains("BaseCY=720", patched);          // 其它键逐字不动
        Assert.Contains("Mode=Simple", patched);
        Assert.Equal(1, patched.Split('\n').Count(l => l.StartsWith("BaseCX", StringComparison.Ordinal)));
    }

    [Fact]
    public void PatchIni_InsertsMissingKeyRightAfterTheSectionHeader()
    {
        var ini = "[Video]\nBaseCX=1280\n";
        var patched = RecordingEnvCore.PatchIni(ini, "Video", "FPSCommon", "60");

        var lines = patched.Split('\n');
        Assert.Equal("[Video]", lines[0]);
        Assert.Equal("FPSCommon=60", lines[1]);
        Assert.Equal("BaseCX=1280", lines[2]);
    }

    [Fact]
    public void PatchIni_AppendsWholeSectionWhenAbsent()
    {
        var patched = RecordingEnvCore.PatchIni("[Video]\nBaseCX=1280\n", "SimpleOutput", "RecFormat2", "hybrid_mp4");
        Assert.Contains("[SimpleOutput]", patched);
        Assert.Contains("RecFormat2=hybrid_mp4", patched);
        Assert.Contains("[Video]", patched);
    }

    [Fact]
    public void PatchIni_IsCaseInsensitiveOnSectionAndKey()
    {
        var patched = RecordingEnvCore.PatchIni("[video]\nbasecx=1280\n", "Video", "BaseCX", "1920");
        Assert.Equal("1920", RecordingEnvCore.ReadIni(patched, "Video", "BaseCX"));
        Assert.Equal(1, patched.Split('\n').Count(l => l.Contains("basecx", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void PatchIni_DoesNotTouchOtherSectionsWithSameKeyName()
    {
        var ini = "[SimpleOutput]\nFilePath=D:\\A\n[AdvOut]\nRecFilePath=D:\\B\n";
        var patched = RecordingEnvCore.PatchIni(ini, "SimpleOutput", "FilePath", "D:\\New");
        Assert.Contains("FilePath=D:\\New", patched);
        Assert.Contains("RecFilePath=D:\\B", patched);
    }

    [Fact]
    public void PatchIni_PreservesCrLfLineEndings()
    {
        var ini = "[Video]\r\nBaseCX=1280\r\n";
        var patched = RecordingEnvCore.PatchIni(ini, "Video", "BaseCX", "1920");
        Assert.DoesNotContain("\n", patched.Replace("\r\n", ""));
    }

    [Fact]
    public void PatchIni_HandlesNullAndEmptyText()
    {
        var patched = RecordingEnvCore.PatchIni(null, "Video", "BaseCX", "1920");
        Assert.Equal("1920", RecordingEnvCore.ReadIni(patched, "Video", "BaseCX"));
    }

    [Fact]
    public void PatchIni_IgnoresCommentLinesWhenSearchingForTheKey()
    {
        var ini = "[Video]\n; BaseCX=999\nBaseCX=1280\n";
        var patched = RecordingEnvCore.PatchIni(ini, "Video", "BaseCX", "1920");
        Assert.Contains("; BaseCX=999", patched);       // 注释保持原样
        Assert.Contains("BaseCX=1920", patched);
        Assert.Equal("1920", RecordingEnvCore.ReadIni(patched, "Video", "BaseCX"));
    }

    [Fact]
    public void PatchIni_ReadBackAlwaysYieldsTheValue()
    {
        var cases = new[]
        {
            ("[Video]\nBaseCX=1\n", "Video", "BaseCX", "1920"),
            ("", "Video", "BaseCX", "1920"),
            ("[Other]\nX=1\n", "Video", "BaseCX", "1920"),
            ("[Video]\nFPSCommon=30\n", "Video", "BaseCX", "1920"),
        };

        foreach (var (ini, section, key, value) in cases)
        {
            var patched = RecordingEnvCore.PatchIni(ini, section, key, value);
            Assert.Equal(value, RecordingEnvCore.ReadIni(patched, section, key));
        }
    }

    [Theory]
    [InlineData("", "Video", "BaseCX", "")]
    [InlineData("; nothing here\n", "Video", "BaseCX", "")]
    [InlineData("[Video]\nBaseCX=1280\n", "SimpleOutput", "BaseCX", "")]
    [InlineData("[Video]\nBaseCX=1280\n", "Video", "XX", "")]
    public void ReadIni_ReturnsEmptyWhenAbsent(string ini, string section, string key, string expected)
        => Assert.Equal(expected, RecordingEnvCore.ReadIni(ini, section, key));

    // ------------------------------------------------------------ V2.9.4：StaticTargets（简单录像的基准项）

    /// <summary>
    /// V2.9.4 起 <c>Build</c> 的 format/quality/canvas/fps 四项由 <c>StaticTargets</c> 提供
    /// （简单录像的落地项也复用它）。键集合与顺序必须一致，否则「录制环境」页与「简单录像」
    /// 会推荐出两套不一样的东西，用户按哪边都觉得自己被改了配置。
    /// </summary>
    [Fact]
    public void StaticTargets_ReturnsTheFourBaseKeysInTheFixedOrder()
    {
        var statics = RecordingEnvCore.StaticTargets(Simple());

        Assert.Equal(new[] { "format", "quality", "canvas", "fps" }, statics.Select(i => i.Key));
        Assert.Equal(4, statics.Select(i => i.Key).Distinct(StringComparer.Ordinal).Count());

        // 与 Build 的同名项是同一批：Build 只是把 path 插到 quality 之后、把 sampleRate 追到末尾
        var built = RecordingEnvCore.Build(Simple(), @"D:\Rec")
            .Where(i => i.Key != "path" && i.Key != "sampleRate")
            .ToList();
        Assert.Equal(built.Select(i => i.Key).ToList(), statics.Select(i => i.Key).ToList());
    }

    /// <summary>基准项只声明「录什么」；推流 / 编码参数与文件通道专属项都不该混进来。</summary>
    [Fact]
    public void StaticTargets_OnlyCoversRecordingParameters()
    {
        var parameters = RecordingEnvCore.StaticTargets(Simple())
            .SelectMany(i => i.Targets)
            .Select(t => t.Parameter)
            .ToHashSet();

        Assert.DoesNotContain("Mode", parameters);
        Assert.DoesNotContain("StreamEncoder", parameters);
        Assert.DoesNotContain("VBitrate", parameters);
        Assert.DoesNotContain("FilePath", parameters);      // 录像目录由调用方按 recPath 追加
        Assert.DoesNotContain("SampleRate", parameters);    // 采样率由调用方按通道追加
        Assert.DoesNotContain("RecTracks", parameters);
    }

    /// <summary>帧率必须用调用方传进来的值：写死 60 会让会议档的 30fps 不生效。</summary>
    [Fact]
    public void StaticTargets_UsesTheRequestedFps()
    {
        var fps = RecordingEnvCore.StaticTargets(Simple(), 30).Single(i => i.Key == "fps");

        Assert.Equal("30", fps.Recommended);
        Assert.Contains(new RecordingEnvTarget("Video", "FPSCommon", "30"), fps.Targets);
        Assert.Contains(new RecordingEnvTarget("Video", "FPSNum", "30"), fps.Targets);
        Assert.Contains(new RecordingEnvTarget("Video", "FPSDen", "1"), fps.Targets);
        Assert.DoesNotContain(fps.Targets, t => t.Value == RecordingEnvCore.Fps.ToString());
    }

    /// <summary>不传 fps 时回落到「一键部署」的推荐帧率，两条路不能各说各话。</summary>
    [Fact]
    public void StaticTargets_DefaultsToTheRecordingEnvFps()
    {
        var fps = RecordingEnvCore.StaticTargets(Simple()).Single(i => i.Key == "fps");
        Assert.Equal(RecordingEnvCore.Fps.ToString(), fps.Recommended);
    }

    /// <summary>画布 / 格式 / 质量与 <c>Build</c> 完全同源：不许在 StaticTargets 里另写一份常量。</summary>
    [Fact]
    public void StaticTargets_MatchesBuildOnTheSharedKeys()
    {
        var snapshot = Simple();
        var built = RecordingEnvCore.Build(snapshot, @"D:\Rec");
        var statics = RecordingEnvCore.StaticTargets(snapshot);

        foreach (var item in statics)
        {
            var same = built.Single(i => i.Key == item.Key);
            Assert.Equal(same.Current, item.Current);
            Assert.Equal(same.Recommended, item.Recommended);
            Assert.Equal(
                same.Targets.Select(t => t.Category + "|" + t.Parameter + "|" + t.Value)
                            .OrderBy(s => s, StringComparer.Ordinal),
                item.Targets.Select(t => t.Category + "|" + t.Parameter + "|" + t.Value)
                            .OrderBy(s => s, StringComparer.Ordinal));
        }
    }

    /// <summary>
    /// 高级输出模式下「简单模式的录像质量档」不适用：当前值清空（界面据此显示成待处理），
    /// 但推荐值与目标键仍在 —— 「不适用」的语义由调用方按 <c>OutputMode</c> 判断。
    /// </summary>
    [Fact]
    public void StaticTargets_BlanksQualityCurrentInAdvancedMode()
    {
        var simple = RecordingEnvCore.StaticTargets(Simple()).Single(i => i.Key == "quality");
        Assert.Equal("Stream", simple.Current);
        Assert.Equal(RecordingEnvCore.HighQuality, simple.Recommended);
        Assert.False(simple.AlreadyOk);

        var advancedSnapshot = Simple() with { OutputMode = "Advanced" };
        var advanced = RecordingEnvCore.StaticTargets(advancedSnapshot).Single(i => i.Key == "quality");
        Assert.Equal("", advanced.Current);
        Assert.Equal(RecordingEnvCore.HighQuality, advanced.Recommended);
        Assert.False(advanced.AlreadyOk);

        // 其它项不受输出模式影响
        Assert.Equal(
            RecordingEnvCore.StaticTargets(Simple()).Single(i => i.Key == "format").Current,
            RecordingEnvCore.StaticTargets(advancedSnapshot).Single(i => i.Key == "format").Current);
    }
}
