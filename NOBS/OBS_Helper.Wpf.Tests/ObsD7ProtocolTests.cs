using System.Text.Json;
using OBS_Helper.Wpf.Models.Obs;
using OBS_Helper.Wpf.Services.Obs;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// D7 那批「现成但未用」能力的旧协议映射与取值校验（V3.0）。
///
/// 这一批的风险不在「请求发不出去」，而在**字段名/取值对不上却仍然成功**：
/// 例如滤镜列表 v4 是 <c>name/type/enabled</c>、v5 是 <c>filterName/filterKind/filterEnabled</c>，
/// 直接透传会让上层读到一堆空字符串而**不报错**；监听类型拼错也会被 OBS 以难懂的理由拒绝。
/// 因此每条都要有断言。
/// </summary>
public class ObsD7ProtocolTests
{
    // ---------------------------------------------------------------- 旧协议请求映射

    /// <summary>
    /// 第三轮验证修正：v4 **没有** SaveSourceScreenshot，只有 TakeSourceScreenshot，
    /// 而且参数名完全不同（saveToFilePath / fileFormat / width / height / compressionQuality）。
    /// 原先按 v5 名字发出去，OBS 回 "invalid request type" —— 兼容构建下截图直接不可用。
    /// </summary>
    [Fact]
    public void SaveSourceScreenshot_MapsToV4TakeSourceScreenshot()
    {
        var data = JsonSerializer.SerializeToElement(new
        {
            sourceName = "游戏",
            imageFilePath = @"C:\shot.png",
            imageFormat = "png",
            imageWidth = 1920,
            imageHeight = 1080,
            imageCompressionQuality = 90
        });

        var mapped = ObsLegacyV4Core.MapRequest("SaveSourceScreenshot", data);
        Assert.NotNull(mapped);
        Assert.Equal("TakeSourceScreenshot", mapped!.Type);
        Assert.Equal("游戏", mapped.Data["sourceName"]);
        Assert.Equal(@"C:\shot.png", mapped.Data["saveToFilePath"]);
        Assert.Equal("png", mapped.Data["fileFormat"]);
        Assert.Equal(1920, (int)mapped.Data["width"]!);
        Assert.Equal(1080, (int)mapped.Data["height"]!);
        Assert.Equal(90, (int)mapped.Data["compressionQuality"]!);
    }

    /// <summary>没指定尺寸/质量时**不该**写进请求：v4 收到 0 尺寸会渲染失败，v5 会回 402。</summary>
    [Fact]
    public void SaveSourceScreenshot_OmitsUnspecifiedSizeAndQuality()
    {
        var data = JsonSerializer.SerializeToElement(new
        {
            sourceName = "游戏",
            imageFilePath = @"C:\shot.png",
            imageFormat = "png"
        });

        var mapped = ObsLegacyV4Core.MapRequest("SaveSourceScreenshot", data);
        Assert.NotNull(mapped);
        Assert.False(mapped!.Data.ContainsKey("width"));
        Assert.False(mapped.Data.ContainsKey("height"));
        Assert.False(mapped.Data.ContainsKey("compressionQuality"));
    }

    [Fact]
    public void SetSourceFilterEnabled_MapsToVisibilityRequest()
    {
        var data = JsonSerializer.SerializeToElement(new
        {
            sourceName = "麦克风",
            filterName = "降噪",
            filterEnabled = true
        });

        var mapped = ObsLegacyV4Core.MapRequest("SetSourceFilterEnabled", data);
        Assert.NotNull(mapped);
        Assert.Equal("SetSourceFilterVisibility", mapped!.Type);
        Assert.Equal("麦克风", mapped.Data["sourceName"]);
        Assert.Equal("降噪", mapped.Data["filterName"]);
        Assert.True((bool)mapped.Data["filterEnabled"]!);
    }

    /// <summary>v5 用 inputName，v4 的媒体请求用 sourceName —— 名字错了 OBS 会报资源找不到。</summary>
    /// <summary>
    /// 第三轮验证修正：v4 **没有** TriggerMediaInputAction，必须按动作拆成五个请求
    /// （PlayPauseMedia/RestartMedia/StopMedia/NextMedia/PreviousMedia，PlayPause 用 playPause 布尔）。
    /// </summary>
    [Theory]
    [InlineData(ObsMediaActions.Play, "PlayPauseMedia", true)]
    [InlineData(ObsMediaActions.Pause, "PlayPauseMedia", false)]
    [InlineData(ObsMediaActions.Restart, "RestartMedia", null)]
    [InlineData(ObsMediaActions.Stop, "StopMedia", null)]
    [InlineData(ObsMediaActions.Next, "NextMedia", null)]
    [InlineData(ObsMediaActions.Previous, "PreviousMedia", null)]
    public void TriggerMediaAction_SplitsIntoV4Requests(string action, string expectedType, bool? playPause)
    {
        var data = JsonSerializer.SerializeToElement(new { inputName = "BGM", mediaAction = action });

        var mapped = ObsLegacyV4Core.MapRequest("TriggerMediaInputAction", data);

        Assert.NotNull(mapped);
        Assert.Equal(expectedType, mapped!.Type);
        Assert.Equal("BGM", mapped.Data["sourceName"]);
        if (playPause is { } expected) Assert.Equal(expected, (bool)mapped.Data["playPause"]!);
    }

    /// <summary>动作认不出来时返回 null（上层据此明确提示「旧协议不支持」），而不是发一个不存在的请求。</summary>
    [Fact]
    public void TriggerMediaAction_UnknownActionReturnsNull()
    {
        var data = JsonSerializer.SerializeToElement(new { inputName = "BGM", mediaAction = "NOPE" });
        Assert.Null(ObsLegacyV4Core.MapRequest("TriggerMediaInputAction", data));
    }

    /// <summary>媒体动作在旧协议下**是支持的**（只是要带参数才算得出请求）—— 探测不能误报不支持。</summary>
    [Fact]
    public void MediaAction_IsReportedAsSupported()
        => Assert.True(ObsLegacyV4Core.SupportsRequest("TriggerMediaInputAction"));

    [Fact]
    public void GetAudioMonitorType_UsesV4RequestName()
    {
        var data = JsonSerializer.SerializeToElement(new { inputName = "麦克风" });
        var mapped = ObsLegacyV4Core.MapRequest("GetInputAudioMonitorType", data);

        Assert.NotNull(mapped);
        Assert.Equal("GetAudioMonitorType", mapped!.Type);
        Assert.Equal("麦克风", mapped.Data["sourceName"]);
    }

    // ---------------------------------------------------------------- 响应归一化

    /// <summary>滤镜列表的字段名在 v4 / v5 之间完全不同，必须逐条改名。</summary>
    [Fact]
    public void NormalizeResponse_MapsFilterFieldNames()
    {
        var v4 = JsonSerializer.SerializeToElement(new
        {
            filters = new object[]
            {
                new { name = "降噪", type = "noise_suppress_filter", enabled = true, settings = new { method = "rnnoise" } },
                new { name = "色键", type = "chroma_key_filter_v2", enabled = false, settings = new { } },
            }
        });

        var o = ObsLegacyV4Core.NormalizeResponse("GetSourceFilterList", v4);
        var filters = o["filters"]!.AsArray();

        Assert.Equal(2, filters.Count);
        Assert.Equal("降噪", (string)filters[0]!["filterName"]!);
        Assert.Equal("noise_suppress_filter", (string)filters[0]!["filterKind"]!);
        Assert.True((bool)filters[0]!["filterEnabled"]!);
        Assert.Equal(0, (int)filters[0]!["filterIndex"]!);
        Assert.Equal(1, (int)filters[1]!["filterIndex"]!);
        Assert.False((bool)filters[1]!["filterEnabled"]!);
    }

    /// <summary>
    /// 第三轮验证修正：v4 的响应字段是 <c>studio-mode</c>（连字符），
    /// 原先读 <c>studioMode</c> 永远读不到 → 演播室状态静默恒为「关」，与真的关闭不可区分。
    /// </summary>
    [Fact]
    public void NormalizeResponse_MapsStudioModeHyphenatedField()
    {
        // 直接构造官方形态（属性名带连字符，不能用匿名类型）
        var v4 = JsonSerializer.SerializeToElement(new { studioMode = true });
        var official = JsonDocument.Parse("""{"studio-mode":true}""").RootElement;
        var o = ObsLegacyV4Core.NormalizeResponse("GetStudioModeEnabled", official);
        Assert.True((bool)o["studioModeEnabled"]!);

        var off = JsonDocument.Parse("""{"studio-mode":false}""").RootElement;
        Assert.False((bool)ObsLegacyV4Core.NormalizeResponse("GetStudioModeEnabled", off)["studioModeEnabled"]!);

        // 兼容旧写法（某些版本 / 手写模拟器）：不能因为字段名不同就丢掉状态
        Assert.True((bool)ObsLegacyV4Core.NormalizeResponse("GetStudioModeEnabled", v4)["studioModeEnabled"]!);
    }

    /// <summary>v4 的监听类型是短名，读回来要翻成 v5 枚举；写入时反过来。</summary>
    [Theory]
    [InlineData("monitorOnly", "OBS_MONITORING_TYPE_MONITOR_ONLY")]
    [InlineData("monitorAndOutput", "OBS_MONITORING_TYPE_MONITOR_AND_OUTPUT")]
    [InlineData("none", "OBS_MONITORING_TYPE_NONE")]
    public void MonitorType_TranslatesBetweenProtocols(string v4, string v5)
    {
        Assert.Equal(v5, ObsLegacyV4Core.ToV5MonitorType(v4));
        Assert.Equal(v4, ObsLegacyV4Core.ToV4MonitorType(v5));
    }

    [Fact]
    public void NormalizeResponse_MonitorTypeComesBackAsV5Enum()
    {
        var v4 = JsonSerializer.SerializeToElement(new { monitorType = "monitorOnly" });
        var o = ObsLegacyV4Core.NormalizeResponse("GetInputAudioMonitorType", v4);

        Assert.Equal("OBS_MONITORING_TYPE_MONITOR_ONLY", (string)o["monitorType"]!);
    }

    /// <summary>v4 的媒体状态是短名（playing），要统一成 v5 风格枚举名，否则状态判断全部失配。</summary>
    [Fact]
    public void NormalizeResponse_MediaStateBecomesV5StyleEnum()
    {
        var v4 = JsonSerializer.SerializeToElement(new { mediaState = "playing" });
        var o = ObsLegacyV4Core.NormalizeResponse("GetMediaInputStatus", v4);

        var status = new ObsMediaStatus { State = (string)o["mediaState"]! };
        Assert.True(status.IsPlaying);
    }

    /// <summary>v4 的 CreateSource 只回 itemId，必须改名成 sceneItemId —— 否则落地器拿到 -1 会跳过全部变换。</summary>
    [Fact]
    public void NormalizeResponse_CreateInputExposesSceneItemId()
    {
        var v4 = JsonSerializer.SerializeToElement(new { itemId = 42, sourceName = "摄像头" });
        var o = ObsLegacyV4Core.NormalizeResponse("CreateInput", v4);

        Assert.Equal(42, (int)o["sceneItemId"]!);
        Assert.Equal("摄像头", (string)o["inputName"]!);
    }

    /// <summary>v4 的当前场景是 name，要归一化成 v5 的 currentProgramSceneName（截图解析要用）。</summary>
    [Fact]
    public void NormalizeResponse_CurrentProgramSceneFromV4Name()
    {
        var v4 = JsonSerializer.SerializeToElement(new { name = "游戏场景" });
        var o = ObsLegacyV4Core.NormalizeResponse("GetCurrentProgramScene", v4);

        Assert.Equal("游戏场景", (string)o["currentProgramSceneName"]!);
        Assert.Equal("游戏场景", (string)o["sceneName"]!);
    }

    /// <summary>v4 的演播室切换事件是 StudioModeSwitched + new-state，要映射成 v5 事件名。</summary>
    [Fact]
    public void MapEvent_MapsStudioModeSwitched()
    {
        var v4 = JsonDocument.Parse("""{"update-type":"StudioModeSwitched","new-state":true}""").RootElement;
        var mapped = ObsLegacyV4Core.MapEvent(v4);

        Assert.NotNull(mapped);
        Assert.Equal("StudioModeStateChanged", mapped!.Value.Type);
        Assert.True((bool)mapped.Value.Data["studioModeEnabled"]!);
    }

    [Fact]
    public void NormalizeResponse_StudioModeFalseWhenAbsent()
    {
        var v4 = JsonSerializer.SerializeToElement(new { });
        var o = ObsLegacyV4Core.NormalizeResponse("GetStudioModeEnabled", v4);

        Assert.False((bool)o["studioModeEnabled"]!);
    }

    // ---------------------------------------------------------------- 取值白名单

    [Theory]
    [InlineData(ObsMediaActions.Play)]
    [InlineData(ObsMediaActions.Pause)]
    [InlineData(ObsMediaActions.Restart)]
    [InlineData(ObsMediaActions.Stop)]
    [InlineData(ObsMediaActions.Next)]
    [InlineData(ObsMediaActions.Previous)]
    public void MediaActions_AcceptsDocumentedValues(string action)
        => Assert.True(ObsMediaActions.IsValid(action));

    [Theory]
    [InlineData("play")]          // 简写不是官方取值，必须拒绝（本地先拦，别等 OBS 报难懂的错）
    [InlineData("PLAY")]
    [InlineData("")]
    [InlineData(null)]
    public void MediaActions_RejectsAnythingElse(string? action)
        => Assert.False(ObsMediaActions.IsValid(action));

    [Theory]
    [InlineData("none", ObsMonitorTypes.None)]
    [InlineData("None", ObsMonitorTypes.None)]
    [InlineData("off", ObsMonitorTypes.None)]
    [InlineData("关闭", ObsMonitorTypes.None)]
    [InlineData("monitorOnly", ObsMonitorTypes.MonitorOnly)]
    [InlineData("MONITOR_ONLY", ObsMonitorTypes.MonitorOnly)]
    [InlineData("仅监听", ObsMonitorTypes.MonitorOnly)]
    [InlineData("OBS_MONITORING_TYPE_MONITOR_AND_OUTPUT", ObsMonitorTypes.MonitorAndOutput)]
    [InlineData("monitorAndOutput", ObsMonitorTypes.MonitorAndOutput)]
    [InlineData("监听并输出", ObsMonitorTypes.MonitorAndOutput)]
    public void MonitorTypes_NormalizesCommonSpellings(string input, string expected)
        => Assert.Equal(expected, ObsMonitorTypes.Normalize(input));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("monitor")]
    [InlineData(null)]
    public void MonitorTypes_RejectsUnknown(string? input)
        => Assert.Null(ObsMonitorTypes.Normalize(input));

    // ---------------------------------------------------------------- 媒体状态判定

    [Theory]
    [InlineData("OBS_WEBSOCKET_MEDIA_INPUT_STATE_PLAYING", true, false)]
    [InlineData("OBS_WEBSOCKET_MEDIA_INPUT_STATE_PAUSED", false, true)]
    [InlineData("OBS_WEBSOCKET_MEDIA_INPUT_STATE_ENDED", false, false)]
    [InlineData("OBS_WEBSOCKET_MEDIA_INPUT_STATE_STOPPED", false, false)]
    public void MediaStatus_ClassifiesState(string state, bool playing, bool paused)
    {
        var s = new ObsMediaStatus { State = state };
        Assert.Equal(playing, s.IsPlaying);
        Assert.Equal(paused, s.IsPaused);
    }

    [Fact]
    public void MediaStatus_EmptyStateCountsAsStopped()
        => Assert.True(new ObsMediaStatus().IsStopped);

    // ---------------------------------------------------------------- 滤镜种类展示名

    [Theory]
    [InlineData("noise_suppress_filter")]
    [InlineData("noise_suppression_filter")]
    [InlineData("chroma_key_filter_v2")]
    [InlineData("color_correction_filter")]
    [InlineData("sharpen_filter")]
    [InlineData("compressor_filter")]
    [InlineData("vst_filter")]
    public void FilterKindLabel_TranslatesKnownKinds(string kind)
    {
        var label = Localization.DataValues.FilterKindLabel(kind);
        Assert.False(string.IsNullOrWhiteSpace(label));
        Assert.NotEqual(kind, label);   // 已知种类不该把内部 id 原样显示给用户
    }

    /// <summary>认不出来的种类原样显示 —— 宁可显示 id，也不要把信息藏起来。</summary>
    [Fact]
    public void FilterKindLabel_FallsBackToRawKind()
        => Assert.Equal("some_future_filter", Localization.DataValues.FilterKindLabel("some_future_filter"));

    [Fact]
    public void FilterKindLabel_EmptyInputIsEmpty()
    {
        Assert.Equal("", Localization.DataValues.FilterKindLabel(""));
        Assert.Equal("", Localization.DataValues.FilterKindLabel(null));
    }

    // ---------------------------------------------------------------- 请求码

    /// <summary>本地校验失败必须与 OBS 的返回码区分开，否则上层会把「参数错了」当成「状态不对」重试。</summary>
    [Fact]
    public void ClientValidationCode_IsDistinctFromServerCodes()
    {
        Assert.NotEqual(ObsRequestStatusCode.Success, ObsRequestStatusCode.ClientValidationFailed);
        Assert.NotEqual(ObsRequestStatusCode.InvalidResourceState, ObsRequestStatusCode.ClientValidationFailed);
        Assert.NotEqual(ObsRequestStatusCode.ResourceNotFound, ObsRequestStatusCode.ClientValidationFailed);
    }
}
