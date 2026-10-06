using System.Text.Json;
using System.Text.Json.Nodes;
using OBS_Helper.Wpf.Services.Obs;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// Win7 旧协议（obs-websocket 4.x）兼容核心的回归测试（V3.0）。
///
/// 这层是「向下部分兼容」的唯一翻译层：鉴权算法错一个字符就连不上 OBS 27，
/// 请求/响应映射错了会让 Win7 用户看到空场景列表或错误的音量 —— 因此这里逐条钉死。
/// </summary>
public class ObsLegacyV4CoreTests
{
    // ---------------------------------------------------------------- 鉴权

    /// <summary>
    /// 4.x 的鉴权应答 = base64(sha256(base64(sha256(password + salt)) + challenge))。
    /// 这里的期望值按官方文档的伪代码手算一遍，防止有人把它「顺手」改成 v5 的算法。
    /// </summary>
    [Fact]
    public void AuthResponse_MatchesSpecAlgorithm()
    {
        const string password = "supersecretpassword";
        const string salt = "PZVbYpvAnZut2SS6JNJytDm9";
        const string challenge = "ztTBnnuqrqaKDzRM3xcVdbYm";

        var secret = Convert.ToBase64String(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(password + salt)));
        var expected = Convert.ToBase64String(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret + challenge)));

        Assert.Equal(expected, ObsLegacyV4Core.BuildAuthResponse(password, salt, challenge));
    }

    [Fact]
    public void AuthResponse_UsesSaltAndChallenge()
    {
        // 注意：v4 与 v5 的**公式相同**（都是 base64(sha256(base64(sha256(password+salt)) + challenge))），
        // 区别只在下发通道（Hello vs GetAuthRequired）与请求形状（Identify.authentication vs Authenticate.auth）。
        // 这条测试锁的是「salt 与 challenge 都真的参与了运算」——任一为空都会得到不同结果。
        var a = ObsLegacyV4Core.BuildAuthResponse("pw", "salt", "challenge");
        var b = ObsLegacyV4Core.BuildAuthResponse("pw", "salt", "");
        var c = ObsLegacyV4Core.BuildAuthResponse("pw", "", "challenge");
        Assert.NotEqual(a, b);
        Assert.NotEqual(a, c);
        Assert.NotEqual(b, c);
    }

    // ---------------------------------------------------------------- 端口

    [Fact]
    public void DefaultPort_Is4444OnCompatBuild_4455Otherwise()
    {
#if WIN7_COMPAT
        Assert.Equal(4444, ObsLegacyV4Core.DefaultPort);
        Assert.True(ObsLegacyV4Core.LegacyEnabled);
#else
        Assert.Equal(4455, ObsLegacyV4Core.DefaultPort);
#endif
    }

    // ---------------------------------------------------------------- 请求映射

    public static IEnumerable<object[]> RequestMapCases()
    {
        yield return new object[] { "GetRecordStatus", "GetRecordingStatus" };
        yield return new object[] { "StartRecord", "StartRecording" };
        yield return new object[] { "StopRecord", "StopRecording" };
        yield return new object[] { "StartStream", "StartStreaming" };
        yield return new object[] { "StopStream", "StopStreaming" };
        yield return new object[] { "GetSceneList", "GetSceneList" };
        yield return new object[] { "GetInputList", "GetSourcesList" };
        yield return new object[] { "GetStats", "GetStats" };
        yield return new object[] { "SaveReplayBuffer", "SaveReplayBuffer" };
        yield return new object[] { "StartVirtualCam", "StartVirtualCam" };
        yield return new object[] { "GetRecordDirectory", "GetRecordingFolder" };
        yield return new object[] { "GetInputKindList", "GetSourceTypesList" };
        yield return new object[] { "GetSceneCollectionList", "ListSceneCollections" };
        yield return new object[] { "GetSceneTransitionList", "GetTransitionList" };

        // V3.0（D7）：其余现成能力的旧协议映射
        // 第三轮验证：v4 没有 SaveSourceScreenshot，只有 TakeSourceScreenshot（参数名也不同）
        yield return new object[] { "SaveSourceScreenshot", "TakeSourceScreenshot" };
        yield return new object[] { "GetSourceFilterList", "GetSourceFilters" };
        yield return new object[] { "SetSourceFilterEnabled", "SetSourceFilterVisibility" };
        // 媒体动作要带参数才能算出目标请求（见 MapMediaRequest），这里不放进「名字一一对应」表
        yield return new object[] { "GetMediaInputStatus", "GetMediaState" };
        yield return new object[] { "GetInputAudioMonitorType", "GetAudioMonitorType" };
        yield return new object[] { "GetStudioModeEnabled", "GetStudioModeStatus" };
        yield return new object[] { "GetCurrentProgramScene", "GetCurrentScene" };
        // 关闭态（无 data）→ DisableStudioMode；v4 没有带布尔的 SetStudioModeEnabled
        yield return new object[] { "SetStudioModeEnabled", "DisableStudioMode" };
        // v4 的转场请求叫 TransitionToProgram
        yield return new object[] { "TriggerStudioModeTransition", "TransitionToProgram" };
    }

    [Theory]
    [MemberData(nameof(RequestMapCases))]
    public void MapRequest_TranslatesKnownRequests(string v5, string expectedV4)
    {
        var mapped = ObsLegacyV4Core.MapRequest(v5, null);
        Assert.NotNull(mapped);
        Assert.Equal(expectedV4, mapped!.Type);
    }

    /// <summary>
    /// v4 确实没有的能力必须返回 null（上层据此提示「旧协议不支持」并改走文件通道），
    /// 绝不能「映射到一个看起来差不多」的请求 —— 那会把用户的配置写到别的地方去。
    /// </summary>
    [Theory]
    [InlineData("SetProfileParameter")]   // v4 无 ProfileParameter API → 一键部署改走 basic.ini
    [InlineData("SetVideoSettings")]      // v4 无 SetVideoSettings
    [InlineData("SetSceneItemIndex")]     // v4 只有语义不同的 ReorderSceneItems
    [InlineData("CreateSceneCollection")] // v4 只能切换 / 列出现有集合，不能新建
    [InlineData("SomethingInvented")]
    public void MapRequest_ReturnsNullForUnsupported(string v5)
        => Assert.Null(ObsLegacyV4Core.MapRequest(v5, null));

    [Fact]
    public void MapRequest_MapsFieldNamesToHyphenatedV4()
    {
        var data = JsonSerializer.SerializeToElement(new { sceneName = "主场景" });
        var mapped = ObsLegacyV4Core.MapRequest("SetCurrentProgramScene", data);
        Assert.NotNull(mapped);
        Assert.Equal("SetCurrentScene", mapped!.Type);
        Assert.Equal("主场景", mapped.Data["scene-name"]);
    }

    [Fact]
    public void MapRequest_ConvertsVolumeDbToMultiplier()
    {
        // 0 dB → 1.0 倍；v4 的 SetVolume 收的是倍率而不是 dB
        var data = JsonSerializer.SerializeToElement(new { inputName = "麦克风", inputVolumeDb = 0.0 });
        var mapped = ObsLegacyV4Core.MapRequest("SetInputVolume", data);
        Assert.NotNull(mapped);
        Assert.Equal("麦克风", mapped!.Data["source"]);
        Assert.Equal(1.0, Assert.IsType<double>(mapped.Data["volume"]), 3);
    }

    [Fact]
    public void MapRequest_MapsSceneItemRender()
    {
        var data = JsonSerializer.SerializeToElement(new { sceneName = "s", sceneItemId = 7, sceneItemEnabled = true });
        var mapped = ObsLegacyV4Core.MapRequest("SetSceneItemEnabled", data);
        Assert.NotNull(mapped);
        Assert.Equal("SetSceneItemRender", mapped!.Type);
        Assert.Equal(7, mapped.Data["item"]);
        Assert.Equal(true, mapped.Data["render"]);
    }

    // ---------------------------------------------------------------- 单位换算

    [Theory]
    [InlineData(1.0, 0.0)]
    [InlineData(0.5, -6.02)]
    [InlineData(2.0, 6.02)]
    public void MulToDb_ConvertsCorrectly(double mul, double expectedDb)
        => Assert.Equal(expectedDb, ObsLegacyV4Core.MulToDb(mul), 1);

    [Fact]
    public void MulToDb_TreatsZeroAsSilenceFloor()
        => Assert.Equal(-100, ObsLegacyV4Core.MulToDb(0));

    [Fact]
    public void DbToMul_RoundTrips()
    {
        foreach (var db in new[] { 0.0, -6.0, 6.0, -20.0 })
            Assert.Equal(db, ObsLegacyV4Core.MulToDb(ObsLegacyV4Core.DbToMul(db)), 1);
    }

    // ---------------------------------------------------------------- 响应归一化（v4 → v5 形状）

    /// <summary>
    /// 把归一化结果再序列化成 <see cref="JsonElement"/> 再断言 —— 这正是上层实际拿到的形态
    /// （<c>ObsRequestResult.Data</c> 就是 JsonElement），因此连「整数 / 小数形态」也一并钉住了。
    /// </summary>
    private static JsonElement ToJson(System.Text.Json.Nodes.JsonObject o) => JsonSerializer.SerializeToElement(o);

    [Fact]
    public void NormalizeResponse_RecordStatus_UsesV5FieldNames()
    {
        var v4 = JsonSerializer.Deserialize<JsonElement>("""
            {"isRecording":true,"isRecordingPaused":false,"recordTimecode":"00:01:02.500","recordingFilename":"D:\\a.mkv"}
            """);
        var o = ToJson(ObsLegacyV4Core.NormalizeResponse("GetRecordStatus", v4));
        Assert.True(o.GetProperty("outputActive").GetBoolean());
        Assert.False(o.GetProperty("outputPaused").GetBoolean());
        Assert.Equal("00:01:02.500", o.GetProperty("outputTimecode").GetString());
        Assert.Equal(@"D:\a.mkv", o.GetProperty("outputPath").GetString());
    }

    [Fact]
    public void NormalizeResponse_Stats_MapsHyphenatedFields()
    {
        // 真实形状（4.9.1 文档）：GetStats 的响应是**嵌套**的 { "stats": {...} }。
        // 审查曾发现实现按顶层扁平字段读，导致旧协议下性能数据全是 0 —— 这里按官方形状钉死。
        var v4 = JsonSerializer.Deserialize<JsonElement>("""
            {"stats":{"fps":59.9,"cpu-usage":12.5,"memory-usage":800.0,"free-disk-space":4096.0,
             "average-frame-time":1.7,"render-missed-frames":3,"render-total-frames":100,
             "output-skipped-frames":1,"output-total-frames":99}}
            """);
        var o = ToJson(ObsLegacyV4Core.NormalizeResponse("GetStats", v4));
        Assert.Equal(59.9, o.GetProperty("activeFps").GetDouble(), 3);
        Assert.Equal(12.5, o.GetProperty("cpuUsage").GetDouble(), 3);
        Assert.Equal(4096.0, o.GetProperty("availableDiskSpace").GetDouble(), 3);
        Assert.Equal(3, o.GetProperty("renderSkippedFrames").GetInt32());
        Assert.Equal(99, o.GetProperty("outputTotalFrames").GetInt32());
    }

    [Fact]
    public void NormalizeResponse_Stats_AcceptsFlatShapeAsFallback()
    {
        // 容错：万一某个版本把字段平铺在顶层，也要能读出来（不能因为形状差异又变回全 0）
        var v4 = JsonSerializer.Deserialize<JsonElement>("""{"fps":30.0,"cpu-usage":5.0}""");
        var o = ToJson(ObsLegacyV4Core.NormalizeResponse("GetStats", v4));
        Assert.Equal(30.0, o.GetProperty("activeFps").GetDouble(), 3);
        Assert.Equal(5.0, o.GetProperty("cpuUsage").GetDouble(), 3);
    }

    /// <summary>
    /// 上层的解析器只接受**字符串数组**（v5 形状）。审查曾发现实现产出对象数组，
    /// 于是 Win7 上「彻底重置 / 应用模板」读不到任何集合名。
    /// </summary>
    [Fact]
    public void NormalizeResponse_SceneCollectionList_IsStringArray()
    {
        var v4 = JsonSerializer.Deserialize<JsonElement>("""
            {"scene-collections":[{"sc-name":"默认"},{"sc-name":"游戏"}]}
            """);
        var o = ToJson(ObsLegacyV4Core.NormalizeResponse("GetSceneCollectionList", v4));
        var arr = o.GetProperty("sceneCollections");
        Assert.Equal(2, arr.GetArrayLength());
        Assert.Equal("默认", arr[0].GetString());
        Assert.Equal("游戏", arr[1].GetString());
    }

    /// <summary>v5 的 inputAudioSyncOffset 是毫秒，v4 的 offset 是**纳秒**（官方文档明写）。</summary>
    [Fact]
    public void MapRequest_SyncOffset_ConvertsMsToNanoseconds()
    {
        var data = JsonSerializer.SerializeToElement(new { inputName = "麦克风", inputAudioSyncOffset = 100 });
        var mapped = ObsLegacyV4Core.MapRequest("SetInputAudioSyncOffset", data);
        Assert.NotNull(mapped);
        Assert.Equal("SetSyncOffset", mapped!.Type);
        Assert.Equal(100_000_000L, mapped.Data["offset"]);
    }

    /// <summary>读回来也要换算（否则界面显示的偏移会大一百万倍）。</summary>
    [Fact]
    public void NormalizeResponse_SyncOffset_ConvertsNanosecondsToMs()
    {
        var v4 = JsonSerializer.Deserialize<JsonElement>("""{"offset":100000000}""");
        var o = ToJson(ObsLegacyV4Core.NormalizeResponse("GetInputAudioSyncOffset", v4));
        Assert.Equal(100, o.GetProperty("inputAudioSyncOffset").GetInt32());
    }

    /// <summary>读取请求要映射到 v4 的 GetSyncOffset（缺了它，对话框永远显示 0）。</summary>
    [Fact]
    public void MapRequest_GetSyncOffset_UsesLegacyRequestName()
    {
        var data = JsonSerializer.SerializeToElement(new { inputName = "麦克风" });
        var mapped = ObsLegacyV4Core.MapRequest("GetInputAudioSyncOffset", data);
        Assert.NotNull(mapped);
        Assert.Equal("GetSyncOffset", mapped!.Type);
        Assert.Equal("麦克风", mapped.Data["source"]);
    }

    /// <summary>推流设置：旧协议路径必须**只留存在性**，绝不把密钥带进统一数据模型。</summary>
    [Fact]
    public void NormalizeResponse_StreamServiceSettings_NeverCarriesTheKey()
    {
        var v4 = JsonSerializer.Deserialize<JsonElement>("""
            {"type":"rtmp_custom","settings":{"server":"rtmp://live.example.com/app","key":"super-secret-stream-key"}}
            """);
        var o = ToJson(ObsLegacyV4Core.NormalizeResponse("GetStreamServiceSettings", v4));

        Assert.Equal("rtmp_custom", o.GetProperty("streamServiceType").GetString());
        Assert.True(o.GetProperty("hasStreamServer").GetBoolean());
        Assert.True(o.GetProperty("hasStreamKey").GetBoolean());
        // 关键：归一化结果里不能出现密钥本身（也不该出现 settings 对象）
        Assert.DoesNotContain("super-secret-stream-key", o.GetRawText());
        Assert.False(o.TryGetProperty("streamServiceSettings", out _));
    }

    /// <summary>只填了服务器、没填密钥：必须能区分出来（这是「按了没反应」的头号原因）。</summary>
    [Fact]
    public void NormalizeResponse_StreamServiceSettings_DetectsMissingKey()
    {
        var v4 = JsonSerializer.Deserialize<JsonElement>("""
            {"type":"rtmp_custom","settings":{"server":"rtmp://live.example.com/app","key":""}}
            """);
        var o = ToJson(ObsLegacyV4Core.NormalizeResponse("GetStreamServiceSettings", v4));
        Assert.True(o.GetProperty("hasStreamServer").GetBoolean());
        Assert.False(o.GetProperty("hasStreamKey").GetBoolean());
    }

    /// <summary>推流时长：v4 的 stream-timecode 要落到 v5 的 outputTimecode。</summary>
    [Fact]
    public void NormalizeResponse_StreamStatus_MapsTimecodeAndStrain()
    {
        var v4 = JsonSerializer.Deserialize<JsonElement>("""
            {"streaming":true,"stream-timecode":"00:12:34.567","num-dropped-frames":5,"num-total-frames":1000,"strain":0.4}
            """);
        var o = ToJson(ObsLegacyV4Core.NormalizeResponse("GetStreamStatus", v4));
        Assert.True(o.GetProperty("outputActive").GetBoolean());
        Assert.Equal("00:12:34.567", o.GetProperty("outputTimecode").GetString());
        Assert.Equal(5, o.GetProperty("outputSkippedFrames").GetInt32());
        Assert.Equal(0.4, o.GetProperty("outputCongestion").GetDouble(), 3);
    }

    /// <summary>没有 stream-timecode 时用 total-stream-time（秒）兜底。</summary>
    [Fact]
    public void NormalizeResponse_StreamStatus_FallsBackToTotalStreamTime()
    {
        var v4 = JsonSerializer.Deserialize<JsonElement>("""{"streaming":true,"total-stream-time":65}""");
        var o = ToJson(ObsLegacyV4Core.NormalizeResponse("GetStreamStatus", v4));
        Assert.Equal("00:01:05.000", o.GetProperty("outputTimecode").GetString());
    }

    /// <summary>模板落地在旧协议下也要能建场景 / 来源 / 场景元素（此前这三个映射缺失 → Win7 上 0 场景）。</summary>
    [Theory]
    [InlineData("CreateScene", "CreateScene")]
    [InlineData("CreateInput", "CreateSource")]
    [InlineData("CreateSceneItem", "AddSceneItem")]
    [InlineData("SetSceneItemTransform", "SetSceneItemProperties")]
    public void MapRequest_MapsTemplateCreationRequests(string v5, string expectedV4)
    {
        var mapped = ObsLegacyV4Core.MapRequest(v5, null);
        Assert.NotNull(mapped);
        Assert.Equal(expectedV4, mapped!.Type);
    }

    /// <summary>v5 的扁平变换要摊成 v4 的嵌套对象，且缺省字段不能写成 0（否则会把用户原有的位置覆盖掉）。</summary>
    [Fact]
    public void MapRequest_SceneItemTransform_FlattensIntoNestedV4Shape()
    {
        var data = JsonSerializer.Deserialize<JsonElement>("""
            {"sceneName":"主","sceneItemId":7,
             "sceneItemTransform":{"positionX":10,"positionY":20,"alignment":5,"scaleX":0.5,"scaleY":0.5,
                                   "boundsType":"OBS_BOUNDS_SCALE_INNER","boundsWidth":640,"boundsHeight":360,
                                   "crop":{"top":1,"bottom":2,"left":3,"right":4},"rotation":90}}
            """);
        var mapped = ObsLegacyV4Core.MapRequest("SetSceneItemTransform", data);
        Assert.NotNull(mapped);
        Assert.Equal("SetSceneItemProperties", mapped!.Type);
        Assert.Equal("主", mapped.Data["scene-name"]);
        Assert.Equal(7, ((Dictionary<string, object?>)mapped.Data["item"]!)["id"]);
        var pos = (Dictionary<string, object?>)mapped.Data["position"]!;
        Assert.Equal(10.0, pos["x"]);
        Assert.Equal(20.0, pos["y"]);
        Assert.Equal(5, pos["alignment"]);
        var bounds = (Dictionary<string, object?>)mapped.Data["bounds"]!;
        Assert.Equal("OBS_BOUNDS_SCALE_INNER", bounds["type"]);
        Assert.Equal(640.0, bounds["x"]);
        Assert.Equal(90.0, mapped.Data["rotation"]);
    }

    [Fact]
    public void MapRequest_SceneItemTransform_OnlyEmitsPresentFields()
    {
        // 只给了位置：不能凭空补出 scale / bounds（那会覆盖用户已有的缩放与裁剪）
        var data = JsonSerializer.Deserialize<JsonElement>("""
            {"sceneName":"主","sceneItemId":1,"sceneItemTransform":{"positionX":1,"positionY":2}}
            """);
        var mapped = ObsLegacyV4Core.MapRequest("SetSceneItemTransform", data);
        Assert.NotNull(mapped);
        Assert.True(mapped!.Data.ContainsKey("position"));
        Assert.False(mapped.Data.ContainsKey("scale"));
        Assert.False(mapped.Data.ContainsKey("bounds"));
        Assert.False(mapped.Data.ContainsKey("crop"));
    }

    [Fact]
    public void NormalizeResponse_InputKindList_FiltersOutNonInputTypes()
    {
        // v4 把滤镜 / 转场类型也列在里面，v5 的 GetInputKindList 只给输入
        var v4 = JsonSerializer.Deserialize<JsonElement>("""
            {"types":[{"typeId":"wasapi_input_capture","type":"input"},
                      {"typeId":"color_filter_v2","type":"filter"},
                      {"typeId":"fade_transition","type":"transition"}]}
            """);
        var o = ToJson(ObsLegacyV4Core.NormalizeResponse("GetInputKindList", v4));
        var kinds = o.GetProperty("inputKinds");
        Assert.Equal(1, kinds.GetArrayLength());
        Assert.Equal("wasapi_input_capture", kinds[0].GetString());
    }

    /// <summary>
    /// v4 的 scenes 是「界面从上到下」，v5 是倒序 + sceneIndex。上层按 sceneIndex 降序排，
    /// 因此这里必须反着填，否则 Win7 用户的场景顺序会整个反过来。
    /// </summary>
    [Fact]
    public void NormalizeResponse_SceneList_ReversesOrderToMatchV5()
    {
        var v4 = JsonSerializer.Deserialize<JsonElement>("""
            {"current-scene":"B","scenes":[{"name":"A"},{"name":"B"},{"name":"C"}]}
            """);
        var o = ObsLegacyV4Core.NormalizeResponse("GetSceneList", v4);
        Assert.Equal("B", o["currentProgramSceneName"]!.GetValue<string>());

        var scenes = o["scenes"]!.AsArray();
        Assert.Equal(3, scenes.Count);
        Assert.Equal("A", scenes[0]!["sceneName"]!.GetValue<string>());
        Assert.Equal(2, scenes[0]!["sceneIndex"]!.GetValue<int>());
        Assert.Equal("C", scenes[2]!["sceneName"]!.GetValue<string>());
        Assert.Equal(0, scenes[2]!["sceneIndex"]!.GetValue<int>());

        // 上层按 Index 降序排 → 得到 A、B、C（与 OBS 界面一致）
        var ordered = scenes.Select(s => s!["sceneName"]!.GetValue<string>())
            .Zip(scenes.Select(s => s!["sceneIndex"]!.GetValue<int>()))
            .OrderByDescending(x => x.Second)
            .Select(x => x.First)
            .ToList();
        Assert.Equal(new[] { "A", "B", "C" }, ordered);
    }

    [Fact]
    public void NormalizeResponse_InputList_FiltersOutScenesAndGroups()
    {
        var v4 = JsonSerializer.Deserialize<JsonElement>("""
            {"sources":[
              {"name":"场景1","type":"scene","typeId":"scene"},
              {"name":"分组","type":"group","typeId":"group"},
              {"name":"麦克风","type":"input","typeId":"wasapi_input_capture"}
            ]}
            """);
        var o = ObsLegacyV4Core.NormalizeResponse("GetInputList", v4);
        var inputs = o["inputs"]!.AsArray();
        Assert.Single(inputs);
        Assert.Equal("麦克风", inputs[0]!["inputName"]!.GetValue<string>());
        Assert.Equal("wasapi_input_capture", inputs[0]!["inputKind"]!.GetValue<string>());
    }

    [Fact]
    public void NormalizeResponse_Volume_ConvertsMultiplierToDb()
    {
        var v4 = JsonSerializer.Deserialize<JsonElement>("""{"volume":1.0,"muted":false}""");
        var o = ObsLegacyV4Core.NormalizeResponse("GetInputVolume", v4);
        Assert.Equal(0.0, o["inputVolumeDb"]!.GetValue<double>(), 2);
        Assert.Equal(1.0, o["inputVolumeMul"]!.GetValue<double>(), 3);
    }

    [Fact]
    public void NormalizeResponse_MutedVolume_ReportsSilenceFloor()
    {
        var v4 = JsonSerializer.Deserialize<JsonElement>("""{"volume":1.0,"muted":true}""");
        var o = ObsLegacyV4Core.NormalizeResponse("GetInputVolume", v4);
        Assert.Equal(-100.0, o["inputVolumeDb"]!.GetValue<double>(), 2);
    }

    [Fact]
    public void NormalizeResponse_VideoInfo_SynthesizesFpsFraction()
    {
        var v4 = JsonSerializer.Deserialize<JsonElement>(
            """{"baseWidth":1920,"baseHeight":1080,"outputWidth":1280,"outputHeight":720,"fps":60}""");
        var o = ToJson(ObsLegacyV4Core.NormalizeResponse("GetVideoSettings", v4));
        Assert.Equal(1920, o.GetProperty("baseWidth").GetInt32());
        Assert.Equal(60, o.GetProperty("fpsNumerator").GetInt32());
        Assert.Equal(1, o.GetProperty("fpsDenominator").GetInt32());
    }

    [Fact]
    public void NormalizeResponse_SceneItemList_KeepsItemIdsAndDefaultsVisible()
    {
        var v4 = JsonSerializer.Deserialize<JsonElement>("""
            {"sceneName":"主","sceneItems":[{"itemId":3,"sourceName":"摄像头","sourceKind":"dshow_input"}]}
            """);
        var o = ToJson(ObsLegacyV4Core.NormalizeResponse("GetSceneItemList", v4));
        Assert.Equal("主", o.GetProperty("sceneName").GetString());
        var items = o.GetProperty("sceneItems");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal(3, items[0].GetProperty("sceneItemId").GetInt32());
        Assert.True(items[0].GetProperty("sceneItemEnabled").GetBoolean());
    }

    [Fact]
    public void NormalizeResponse_UnknownRequest_PassesThroughUnchanged()
    {
        var v4 = JsonSerializer.Deserialize<JsonElement>("""{"foo":"bar"}""");
        var o = ObsLegacyV4Core.NormalizeResponse("Whatever", v4);
        Assert.Equal("bar", o["foo"]!.GetValue<string>());
    }

    // ---------------------------------------------------------------- 事件映射（v4 → v5）

    public static IEnumerable<object[]> EventMapCases()
    {
        yield return new object[] { """{"update-type":"SwitchScenes","scene-name":"B"}""", "CurrentProgramSceneChanged" };
        yield return new object[] { """{"update-type":"RecordingStarted"}""", "RecordStateChanged" };
        yield return new object[] { """{"update-type":"RecordingStopped"}""", "RecordStateChanged" };
        yield return new object[] { """{"update-type":"RecordingPaused"}""", "RecordStateChanged" };
        yield return new object[] { """{"update-type":"StreamStarted"}""", "StreamStateChanged" };
        yield return new object[] { """{"update-type":"StreamStopped"}""", "StreamStateChanged" };
        yield return new object[] { """{"update-type":"VirtualCamStarted"}""", "VirtualcamStateChanged" };
        yield return new object[] { """{"update-type":"ReplayStarted"}""", "ReplayBufferStateChanged" };
        yield return new object[] { """{"update-type":"SourceMuteStateChanged","sourceName":"麦","muted":true}""", "InputMuteStateChanged" };
        yield return new object[] { """{"update-type":"SourceVolumeChanged","sourceName":"麦","volume":0.5}""", "InputVolumeChanged" };
        yield return new object[] { """{"update-type":"SceneItemVisibilityChanged","scene-name":"s","item-id":2,"item-visible":false}""", "SceneItemEnableStateChanged" };
        yield return new object[] { """{"update-type":"Exiting"}""", "ExitStarted" };
    }

    [Theory]
    [MemberData(nameof(EventMapCases))]
    public void MapEvent_TranslatesToV5EventNames(string v4Json, string expectedV5)
    {
        var e = JsonSerializer.Deserialize<JsonElement>(v4Json);
        var mapped = ObsLegacyV4Core.MapEvent(e);
        Assert.NotNull(mapped);
        Assert.Equal(expectedV5, mapped!.Value.Type);
    }

    [Fact]
    public void MapEvent_SceneChange_CarriesSceneName()
    {
        var e = JsonSerializer.Deserialize<JsonElement>("""{"update-type":"SwitchScenes","scene-name":"游戏"}""");
        var mapped = ObsLegacyV4Core.MapEvent(e);
        Assert.Equal("游戏", mapped!.Value.Data["sceneName"]!.GetValue<string>());
    }

    [Fact]
    public void MapEvent_RecordingStopped_ReportsInactive()
    {
        var e = JsonSerializer.Deserialize<JsonElement>("""{"update-type":"RecordingStopped"}""");
        var mapped = ObsLegacyV4Core.MapEvent(e);
        Assert.False(mapped!.Value.Data["outputActive"]!.GetValue<bool>());
    }

    [Fact]
    public void MapEvent_SceneItemVisibility_CarriesNewState()
    {
        var e = JsonSerializer.Deserialize<JsonElement>(
            """{"update-type":"SceneItemVisibilityChanged","scene-name":"s","item-id":2,"item-visible":false}""");
        var mapped = ObsLegacyV4Core.MapEvent(e);
        var d = mapped!.Value.Data;
        Assert.Equal("s", d["sceneName"]!.GetValue<string>());
        Assert.Equal(2, d["sceneItemId"]!.GetValue<int>());
        Assert.False(d["sceneItemEnabled"]!.GetValue<bool>());
    }

    /// <summary>不关心的事件返回 null（例如每 2 秒一次的 Heartbeat / StreamStatus），上层据此忽略。</summary>
    [Theory]
    [InlineData("""{"update-type":"Heartbeat"}""")]
    [InlineData("""{"update-type":"StreamStatus","streaming":true}""")]
    [InlineData("""{"update-type":"BroadcastCustomMessage","data":{}}""")]
    public void MapEvent_ReturnsNullForUninterestingEvents(string v4Json)
    {
        var e = JsonSerializer.Deserialize<JsonElement>(v4Json);
        Assert.Null(ObsLegacyV4Core.MapEvent(e));
    }

    [Fact]
    public void MapEvent_ReturnsNullWhenNoUpdateType()
    {
        var e = JsonSerializer.Deserialize<JsonElement>("""{"message-id":"1","status":"ok"}""");
        Assert.Null(ObsLegacyV4Core.MapEvent(e));
    }

    // ---------------------------------------------------------------- 支持面查询

    [Fact]
    public void SupportsRequest_TrueForMappedAndClientSpecialCases()
    {
        Assert.True(ObsLegacyV4Core.SupportsRequest("StartRecord"));
        Assert.True(ObsLegacyV4Core.SupportsRequest("ToggleRecordPause"));
        Assert.True(ObsLegacyV4Core.SupportsRequest("CallBatch"));
        Assert.False(ObsLegacyV4Core.SupportsRequest("SetProfileParameter"));
    }
    // ------------------------------------------------ 第四轮验证：捕获用到的映射与字段形状

    /// <summary>
    /// <c>GetInputSettings</c> 此前**没有** v4 映射：兼容构建下每个来源的 kind 都读不到，
    /// 于是全部落进「分组没能展开」分支，照存一份「全是占位」的模板 —— 错误归因，不是协议限制。
    /// </summary>
    [Fact]
    public void GetInputSettings_MapsToV4GetSourceSettings()
    {
        var data = JsonSerializer.SerializeToElement(new { inputName = "摄像头" });
        var mapped = ObsLegacyV4Core.MapRequest("GetInputSettings", data);

        Assert.NotNull(mapped);
        Assert.Equal("GetSourceSettings", mapped!.Type);
        Assert.Equal("摄像头", mapped.Data["sourceName"]);
    }

    /// <summary>v4 返回 sourceType/sourceSettings，要归一化成 v5 的 inputKind/inputSettings。</summary>
    [Fact]
    public void NormalizeResponse_InputSettingsFromV4Shape()
    {
        var v4 = JsonDocument.Parse("""
        { "sourceType": "dshow_input", "sourceSettings": { "video_device_id": "USB" } }
        """).RootElement;

        var o = ObsLegacyV4Core.NormalizeResponse("GetInputSettings", v4);

        Assert.Equal("dshow_input", (string)o["inputKind"]!);
        Assert.NotNull(o["inputSettings"]);
    }

    /// <summary>v4 的 GetSceneItemProperties 返回**扁平**字段，要归一化成 v5 的 sceneItemTransform（内含扁平 crop）。</summary>
    [Fact]
    public void NormalizeResponse_SceneItemTransformFromV4FlatFields()
    {
        var v4 = JsonDocument.Parse("""
        { "positionX": 10, "positionY": 20, "scaleX": 1.5, "rotation": 90, "cropLeft": 4, "cropBottom": 16 }
        """).RootElement;

        var o = ObsLegacyV4Core.NormalizeResponse("GetSceneItemTransform", v4);
        var tf = Assert.IsType<JsonObject>(o["sceneItemTransform"]);

        Assert.Equal(10.0, (double)tf["positionX"]!);
        Assert.Equal(4.0, (double)tf["cropLeft"]!);
        Assert.Equal(16.0, (double)tf["cropBottom"]!);
    }

    /// <summary>
    /// v5 发来的变换里裁剪是**扁平**字段，而 v4 要嵌套 crop —— 第四轮验证前这里按「输入也带嵌套」写，
    /// 上层一改对，v4 的裁剪就整块丢（还返回成功）。
    /// </summary>
    [Fact]
    public void SetSceneItemTransform_ConvertsFlatCropToV4Nested()
    {
        var data = JsonDocument.Parse("""
        {
          "sceneName": "主场景", "sceneItemId": 3,
          "sceneItemTransform": { "positionX": 1, "positionY": 2, "cropLeft": 4, "cropTop": 8, "cropRight": 12, "cropBottom": 16, "rotation": 90 }
        }
        """).RootElement;

        var mapped = ObsLegacyV4Core.MapRequest("SetSceneItemTransform", data);

        Assert.NotNull(mapped);
        Assert.Equal("SetSceneItemProperties", mapped!.Type);
        var crop = Assert.IsType<Dictionary<string, object?>>(mapped.Data["crop"]);
        Assert.Equal(4, crop["left"]);
        Assert.Equal(8, crop["top"]);
        Assert.Equal(12, crop["right"]);
        Assert.Equal(16, crop["bottom"]);
        Assert.Equal(90.0, mapped.Data["rotation"]);
    }

    /// <summary>
    /// v4 的 CreateSource/CreateInput 只回 itemId；**缺失时必须给 -1**（默认 0 是合法 id，
    /// 会让上层「拿不到 id → 走占位」的检测永不触发，于是静默跳过变换/层级/显隐）。
    /// </summary>
    [Fact]
    public void NormalizeResponse_CreateInputMissingItemIdBecomesMinusOne()
    {
        var v4 = JsonDocument.Parse("""{ "sourceName": "摄像头" }""").RootElement;

        var o = ObsLegacyV4Core.NormalizeResponse("CreateInput", v4);

        Assert.Equal(-1, (int)o["sceneItemId"]!);
    }

    /// <summary>过渡覆盖：v5 是双 Scene，v4 是单 Scene —— 两边都要映射到真实存在的请求。</summary>
    [Theory]
    [InlineData("GetSceneSceneTransitionOverride", "GetSceneTransitionOverride")]
    [InlineData("SetSceneSceneTransitionOverride", "SetSceneTransitionOverride")]
    public void SceneTransitionOverride_MapsBothDirections(string v5, string v4)
    {
        var mapped = ObsLegacyV4Core.MapRequest(v5, JsonSerializer.SerializeToElement(new { sceneName = "主场景" }));

        Assert.NotNull(mapped);
        Assert.Equal(v4, mapped!.Type);
    }

    /// <summary>v4.9 没有「读分组子项」的请求 → 故意不映射，让上层写成「旧协议读不到分组」的显式占位。</summary>
    [Fact]
    public void GetGroupSceneItemList_IsNotPretendedToBeSupported()
    {
        Assert.Null(ObsLegacyV4Core.MapRequest("GetGroupSceneItemList", null));
        Assert.False(ObsLegacyV4Core.SupportsRequest("GetGroupSceneItemList"));
    }
}