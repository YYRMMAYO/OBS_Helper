using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OBS_Helper.Wpf.Services.Obs;

/// <summary>一条「v5 请求 → v4 请求」的映射结果。</summary>
/// <param name="Type">v4 的 request-type。</param>
/// <param name="Data">v4 的请求字段（键名带连字符，直接序列化即可）。</param>
public sealed record LegacyRequest(string Type, Dictionary<string, object?> Data);

/// <summary>
/// obs-websocket <b>4.x（旧协议）</b>兼容核心（V3.0，纯逻辑、零 IO、可单测）。
///
/// <b>为什么要它</b>：obs-websocket 5.x 是随 OBS 28 一起内置的，而 OBS 28 起不再支持 Windows 7/8.1。
/// 也就是说 Win7 用户能用的最新 OBS 是 27.x，只能配 <b>obs-websocket 4.9.x 插件</b>（默认端口 4444）。
/// 没有这一层，兼容构建在他们机器上「连不上 OBS」，只剩离线知识库那一半能力。
///
/// <b>兼容策略（刻意只做「部分兼容」）</b>：
/// <list type="bullet">
///   <item>请求层：把上层在用的 v5 请求名与字段<b>翻译</b>成 v4 等价物；</item>
///   <item>响应层：把 v4 的响应<b>归一化回 v5 形状</b>，因此 <see cref="ObsConnectionService"/> 与各页面一行都不用改；</item>
///   <item>事件层：把 v4 的 update-type 映射成 v5 事件名与字段；</item>
///   <item>v4 确实没有的能力（例如 <c>SetProfileParameter</c> / <c>SetVideoSettings</c>）<b>不假装支持</b>：
///         返回「旧协议不支持」并由上层走文件通道或给出明确提示。</item>
/// </list>
///
/// 协议依据：obs-websocket 4.9.1 官方生成的 protocol.md（鉴权算法、请求字段、事件字段均按该文档实现）。
/// </summary>
public static class ObsLegacyV4Core
{
    public const string ProtocolV4 = "v4";
    public const string ProtocolV5 = "v5";

    /// <summary>obs-websocket 4.x 插件的默认端口（v5 是 4455）。</summary>
    public const int LegacyDefaultPort = 4444;

    /// <summary>obs-websocket 5.x 的默认端口。</summary>
    public const int ModernDefaultPort = 4455;

    /// <summary>
    /// 旧协议是否可用。
    ///
    /// <b>只在 Win7 兼容构建里打开</b>（<c>net6.0-windows</c> 定义 <c>WIN7_COMPAT</c>）——
    /// 主构建面向 OBS 28+，不需要也不应该在握手时多等一次「没有 Hello」。
    ///
    /// 写成属性而不是 <c>const</c>：<c>const</c> 会被编译期折叠，主构建里那段旧协议分支就成了
    /// 「无法访问的代码」（CS0162 警告），而本仓的质量基线是零警告。
    /// </summary>
#if WIN7_COMPAT
    public static bool LegacyEnabled => true;
#else
    public static bool LegacyEnabled => false;
#endif

    /// <summary>本构建默认使用的连接端口：兼容构建默认 4444（v4 插件），主构建默认 4455。</summary>
    public static int DefaultPort => LegacyEnabled ? LegacyDefaultPort : ModernDefaultPort;

    // ---------------------------------------------------------------- 鉴权（v4）

    /// <summary>
    /// 计算 obs-websocket 4.x 的鉴权应答：
    /// <c>base64(sha256(base64(sha256(password + salt)) + challenge))</c>。
    /// </summary>
    public static string BuildAuthResponse(string password, string salt, string challenge)
    {
        var secret = Convert.ToBase64String(
            SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
        return Convert.ToBase64String(
            SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
    }

    // ---------------------------------------------------------------- 单位换算（v4 的 GetVolume/SetVolume 用倍率，v5 用 dB）
    /// <summary>倍率 → dB（OBS 的静音下限是 -100 dB）。</summary>
    public static double MulToDb(double mul)
        => mul <= 0.00001 ? -100 : Math.Round(20 * Math.Log10(mul), 2);

    /// <summary>dB → 倍率。</summary>
    public static double DbToMul(double db) => Math.Round(Math.Pow(10, db / 20), 4);

    // ---------------------------------------------------------------- 请求映射

    /// <summary>
    /// 把 v5 请求翻译成 v4 请求；<b>返回 null 表示旧协议没有这个能力</b>（上层应给出「旧协议不支持」提示）。
    /// </summary>
    public static LegacyRequest? MapRequest(string v5Type, JsonElement? data)
    {
        string S(string name) => data is { } d && d.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";
        int I(string name) => data is { } d && d.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32() : 0;
        bool B(string name) => data is { } d && d.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
        double D(string name) => data is { } d && d.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble() : 0;

        return v5Type switch
        {
            // ---- 版本 / 视频 / 统计 ----
            "GetVersion" => new LegacyRequest("GetVersion", new()),
            "GetVideoSettings" => new LegacyRequest("GetVideoInfo", new()),
            "GetStats" => new LegacyRequest("GetStats", new()),

            // ---- 场景 ----
            "GetSceneList" => new LegacyRequest("GetSceneList", new()),
            // v5 的当前节目场景 ↔ v4 的 GetCurrentScene（截图前解析空 sourceName 要用）
            "GetCurrentProgramScene" => new LegacyRequest("GetCurrentScene", new()),
            "SetCurrentProgramScene" => new LegacyRequest("SetCurrentScene",
                new() { ["scene-name"] = S("sceneName") }),
            "GetSceneItemList" => new LegacyRequest("GetSceneItemList",
                new() { ["sceneName"] = S("sceneName") }),
            "SetSceneItemEnabled" => new LegacyRequest("SetSceneItemRender",
                new() { ["scene-name"] = S("sceneName"), ["item"] = I("sceneItemId"), ["render"] = B("sceneItemEnabled") }),

            // ---- 转场 ----
            "GetSceneTransitionList" => new LegacyRequest("GetTransitionList", new()),
            "SetCurrentSceneTransition" => new LegacyRequest("SetCurrentTransition",
                new() { ["transition-name"] = S("transitionName") }),
            "SetCurrentSceneTransitionDuration" => new LegacyRequest("SetTransitionDuration",
                new() { ["duration"] = I("transitionDuration") }),
            "SetSceneTransitionOverride" => new LegacyRequest("SetSceneTransitionOverride", new()
            {
                ["scene-name"] = S("sceneName"),
                ["transition-name"] = S("transitionName"),
                ["transition-duration"] = I("transitionDuration"),
            }),

            // ---- 输入 / 音频 ----
            // ---- V3.0 第四轮验证补：D6 反向捕获用到的五条，此前完全没映射 →
            //      兼容构建下 kind 读不到、变换读不到，每个来源都被误判成「分组没能展开」。
            "GetInputSettings" => new LegacyRequest("GetSourceSettings", new() { ["sourceName"] = S("inputName") }),
            "GetSceneItemTransform" => new LegacyRequest("GetSceneItemProperties", new()
            {
                ["scene-name"] = S("sceneName"),
                ["item"] = new Dictionary<string, object?> { ["id"] = I("sceneItemId") },
            }),
            // v4.9 没有「读分组子项」的请求（GetGroupSceneItemList 是 v5 新增）→ 故意不映射，
            // 由上层写成「旧协议读不到分组」的显式占位（好过静默丢弃整组来源）。
            "GetSourceFilter" => new LegacyRequest("GetSourceFilterInfo", new()
            {
                ["sourceName"] = S("sourceName"),
                ["filterName"] = S("filterName"),
            }),
            "GetSceneSceneTransitionOverride" => new LegacyRequest("GetSceneTransitionOverride", new() { ["sceneName"] = S("sceneName") }),
            "SetSceneSceneTransitionOverride" => new LegacyRequest("SetSceneTransitionOverride", new()
            {
                ["sceneName"] = S("sceneName"),
                ["transitionName"] = S("transitionName"),
                ["transitionDuration"] = I("transitionDuration"),
            }),
            "GetInputList" => new LegacyRequest("GetSourcesList", new()),
            "GetInputKindList" => new LegacyRequest("GetSourceTypesList", new()),
            "GetInputMute" => new LegacyRequest("GetMute", new() { ["source"] = S("inputName") }),
            "SetInputMute" => new LegacyRequest("SetMute",
                new() { ["source"] = S("inputName"), ["mute"] = B("inputMuted") }),
            "GetInputVolume" => new LegacyRequest("GetVolume", new() { ["source"] = S("inputName") }),
            "SetInputVolume" => new LegacyRequest("SetVolume",
                new() { ["source"] = S("inputName"), ["volume"] = DbToMul(D("inputVolumeDb")) }),
            // V3.0（D7）：读取同步偏移。v5 的 inputAudioSyncOffset 单位是**毫秒**，
            // v4 的 offset 是**纳秒**（官方 4.9.1 文档明写 "in nanoseconds"），
            // 写入时乘 10⁶、读取时除 10⁶ —— 直接透传会让实际生效值差一百万倍，而且不报错。
            "GetInputAudioSyncOffset" => new LegacyRequest("GetSyncOffset", new() { ["source"] = S("inputName") }),
            "SetInputAudioSyncOffset" => new LegacyRequest("SetSyncOffset",
                new() { ["source"] = S("inputName"), ["offset"] = I("inputAudioSyncOffset") * 1_000_000L }),
            // v4.9.1 的 SetAudioMonitorType 只接受短名（none / monitorOnly / monitorAndOutput），
            // 收到 v5 的 OBS_MONITORING_TYPE_* 会直接报 invalid monitorType —— 必须翻译。
            "SetInputAudioMonitorType" => new LegacyRequest("SetAudioMonitorType",
                new() { ["sourceName"] = S("inputName"), ["monitorType"] = ToV4MonitorType(S("monitorType")) }),

            // ---- 录制 ----
            "GetRecordStatus" => new LegacyRequest("GetRecordingStatus", new()),
            "StartRecord" => new LegacyRequest("StartRecording", new()),
            "StopRecord" => new LegacyRequest("StopRecording", new()),
            "GetRecordDirectory" => new LegacyRequest("GetRecordingFolder", new()),

            // ---- 推流 ----
            "GetStreamStatus" => new LegacyRequest("GetStreamingStatus", new()),
            // v4 的推流设置查询（V3.0 / D2）：键名不同，归一化时只保留「填没填」的存在性
            "GetStreamServiceSettings" => new LegacyRequest("GetStreamSettings", new()),
            "StartStream" => new LegacyRequest("StartStreaming", new()),
            "StopStream" => new LegacyRequest("StopStreaming", new()),

            // ---- 虚拟摄像头（4.9 起同名）----
            "GetVirtualCamStatus" => new LegacyRequest("GetVirtualCamStatus", new()),
            "StartVirtualCam" => new LegacyRequest("StartVirtualCam", new()),
            "StopVirtualCam" => new LegacyRequest("StopVirtualCam", new()),

            // ---- 回放缓存（4.2 起）----
            "GetReplayBufferStatus" => new LegacyRequest("GetReplayBufferStatus", new()),
            "StartReplayBuffer" => new LegacyRequest("StartReplayBuffer", new()),
            "StopReplayBuffer" => new LegacyRequest("StopReplayBuffer", new()),
            "SaveReplayBuffer" => new LegacyRequest("SaveReplayBuffer", new()),

            // ---- 配置集 / 场景集合 ----
            "GetSceneCollectionList" => new LegacyRequest("ListSceneCollections", new()),
            "SetCurrentSceneCollection" => new LegacyRequest("SetCurrentSceneCollection",
                new() { ["sc-name"] = S("sceneCollectionName") }),
            "GetProfileList" => new LegacyRequest("ListProfiles", new()),
            "SetCurrentProfile" => new LegacyRequest("SetCurrentProfile",
                new() { ["profile-name"] = S("profileName") }),

            // ---- 截图（v4.6 起 TakeSourceScreenshot）----
            "GetSourceScreenshot" => new LegacyRequest("TakeSourceScreenshot", new()
            {
                ["sourceName"] = S("sourceName"),
                ["embedPictureFormat"] = "png",
            }),
            // V3.0（D7，第三轮验证修正）：v4 **没有** SaveSourceScreenshot 这个请求，
            // 只有 TakeSourceScreenshot（写文件时用 saveToFilePath / fileFormat，字段名与 v5 完全不同）。
            // 原先按 v5 的名字发出去，OBS 回 "invalid request type" —— 兼容构建下截图直接不可用。
            "SaveSourceScreenshot" => new LegacyRequest("TakeSourceScreenshot", BuildScreenshotRequest(data)),

            // ---- V3.0（D7）：滤镜 / 媒体控制 / 音频监听 / 演播室模式 ----
            "GetSourceFilterList" => new LegacyRequest("GetSourceFilters", new() { ["sourceName"] = S("sourceName") }),
            // v5 的 SetSourceFilterEnabled ↔ v4 的 SetSourceFilterVisibility（都是「显示/隐藏这个滤镜」）
            "SetSourceFilterEnabled" => new LegacyRequest("SetSourceFilterVisibility", new()
            {
                ["sourceName"] = S("sourceName"),
                ["filterName"] = S("filterName"),
                ["filterEnabled"] = B("filterEnabled"),
            }),
            // 新建滤镜（「我的模板」落地时要把降噪 / 色键建回来）：
            // v4 的请求名是 **AddFilterToSource**，字段是 filterType（不是 filterKind），
            // 且 filterSettings 在 v4 是**必填**（缺了 OBS 直接报错）。
            "CreateSourceFilter" => new LegacyRequest("AddFilterToSource", new()
            {
                ["sourceName"] = S("sourceName"),
                ["filterName"] = S("filterName"),
                ["filterType"] = S("filterKind"),
                ["filterSettings"] = data is { } cf && cf.TryGetProperty("filterSettings", out var cfs)
                    && cfs.ValueKind == JsonValueKind.Object
                    ? JsonNode.Parse(cfs.GetRawText())
                    : new JsonObject(),
            }),
            "SetSourceFilterSettings" => new LegacyRequest("SetSourceFilterSettings", new()
            {
                ["sourceName"] = S("sourceName"),
                ["filterName"] = S("filterName"),
                // v4 用 filterSettings，v5 用 filterSettings + overlay；这里保持同名（两个协议都用这个键）
                ["filterSettings"] = data is { } fd && fd.TryGetProperty("filterSettings", out var fs)
                    ? JsonNode.Parse(fs.GetRawText())
                    : new JsonObject(),
            }),
            // V3.0（D7，第三轮验证修正）：v4 **没有** TriggerMediaInputAction / GetMediaInputStatus，
            // 必须按动作拆成 PlayPauseMedia / RestartMedia / StopMedia / NextMedia / PreviousMedia
            // （PlayPauseMedia 用 playPause 布尔），状态查询则只有 GetMediaState。
            "TriggerMediaInputAction" => MapMediaRequest(data),
            "GetMediaInputStatus" => new LegacyRequest("GetMediaState", new() { ["sourceName"] = S("inputName") }),
            // v4 的监听类型读取叫 GetAudioMonitorType（参数 sourceName），写入叫 SetAudioMonitorType（见上）
            "GetInputAudioMonitorType" => new LegacyRequest("GetAudioMonitorType", new() { ["sourceName"] = S("inputName") }),
            // V3.0（D7，第三轮验证修正）：v4 是 GetStudioModeStatus，但**响应字段叫 studio-mode**；
            // 开启/关闭要拆成 EnableStudioMode / DisableStudioMode（v4 没有带布尔的 SetStudioModeEnabled），
            // 转场请求叫 TransitionToProgram。原先这三条都发了不存在的请求名。
            "GetStudioModeEnabled" => new LegacyRequest("GetStudioModeStatus", new()),
            "SetStudioModeEnabled" => B("studioModeEnabled")
                ? new LegacyRequest("EnableStudioMode", new())
                : new LegacyRequest("DisableStudioMode", new()),
            "TriggerStudioModeTransition" => new LegacyRequest("TransitionToProgram", new()),

            // ---- 场景 / 来源的创建（模板落地在旧协议下也能建起来，虽然放置与层级不支持）----
            "CreateScene" => new LegacyRequest("CreateScene", new() { ["sceneName"] = S("sceneName") }),
            // 第三轮验证修正：原先固定 setVisible=false 且**不透传设置** →
            // v4 上「应用模板」的每个来源都建出来就不可见、设置也全丢（而且状态是 Ok）。
            "CreateInput" => new LegacyRequest("CreateSource", new()
            {
                ["sourceName"] = S("inputName"),
                ["sourceKind"] = S("inputKind"),
                ["sourceSettings"] = data is { } ci && ci.TryGetProperty("inputSettings", out var csettings)
                    && csettings.ValueKind == JsonValueKind.Object
                    ? JsonNode.Parse(csettings.GetRawText())
                    : new JsonObject(),
                ["setVisible"] = true,
            }),
            "CreateSceneItem" => new LegacyRequest("AddSceneItem", new()
            {
                ["sceneName"] = S("sceneName"),
                ["sourceName"] = S("sourceName"),
                ["setVisible"] = B("sceneItemEnabled"),
            }),
            // v5 的变换是**扁平**字段（positionX/scaleX/boundsType…），v4 是**嵌套**对象
            // （position.x/scale.x/bounds.type…）—— 逐字段可映射，因此旧协议下模板的放置也能落地。
            "SetSceneItemTransform" => new LegacyRequest("SetSceneItemProperties", BuildSceneItemTransform(data)),

            // ---- 已知「旧协议没有」的能力：明确返回 null，不假装支持 ----
            // SetProfileParameter / SetVideoSettings：v4 无对应请求 → 上层改走文件通道（basic.ini）
            // SetSceneItemIndex / SetSceneItemTransform：v4 只有 ReorderSceneItems 与 SetSceneItemProperties，
            //   语义与 v5 不同，模板落地的这两步在旧协议下不执行（由上层提示）
            _ => null
        };
    }

    /// <summary>v4 TakeSourceScreenshot 的写文件参数（字段名与 v5 完全不同）。</summary>
    private static Dictionary<string, object?> BuildScreenshotRequest(JsonElement? data)
    {
        var o = new Dictionary<string, object?>();
        if (data is not { } d) return o;

        string S(string name) => d.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";
        int I(string name) => d.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32() : 0;

        var source = S("sourceName");
        if (source.Length > 0) o["sourceName"] = source;

        var file = S("imageFilePath");
        var format = S("imageFormat");
        if (file.Length > 0) o["saveToFilePath"] = file;
        o["fileFormat"] = format.Length > 0 ? format : "png";

        // v4 同样不该收到 0 尺寸：没指定就不写
        if (I("imageWidth") > 0) o["width"] = I("imageWidth");
        if (I("imageHeight") > 0) o["height"] = I("imageHeight");
        if (I("imageCompressionQuality") >= 0 && d.TryGetProperty("imageCompressionQuality", out _))
            o["compressionQuality"] = I("imageCompressionQuality");

        return o;
    }

    /// <summary>
    /// 把 v5 的媒体动作拆成 v4 的五个请求（v4 没有 TriggerMediaInputAction）。
    /// 认不出的动作返回 null —— 上层会因 <see cref="SupportsRequest"/> 为 false 而明确报「旧协议不支持」，
    /// 而不是发一个不存在的请求让用户看 "invalid request type"。
    /// </summary>
    private static LegacyRequest? MapMediaRequest(JsonElement? data)
    {
        if (data is not { } d) return null;

        string S(string name) => d.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";

        Dictionary<string, object?> Source() => new() { ["sourceName"] = S("inputName") };
        return S("mediaAction") switch
        {
            "OBS_WEBSOCKET_MEDIA_INPUT_ACTION_PLAY" =>
                new LegacyRequest("PlayPauseMedia", new() { ["sourceName"] = S("inputName"), ["playPause"] = true }),
            "OBS_WEBSOCKET_MEDIA_INPUT_ACTION_PAUSE" =>
                new LegacyRequest("PlayPauseMedia", new() { ["sourceName"] = S("inputName"), ["playPause"] = false }),
            "OBS_WEBSOCKET_MEDIA_INPUT_ACTION_RESTART" => new LegacyRequest("RestartMedia", Source()),
            "OBS_WEBSOCKET_MEDIA_INPUT_ACTION_STOP" => new LegacyRequest("StopMedia", Source()),
            "OBS_WEBSOCKET_MEDIA_INPUT_ACTION_NEXT" => new LegacyRequest("NextMedia", Source()),
            "OBS_WEBSOCKET_MEDIA_INPUT_ACTION_PREVIOUS" => new LegacyRequest("PreviousMedia", Source()),
            _ => null,
        };
    }

    /// <summary>v5 的监听类型枚举 → v4 的短名（v4 只认短名）。</summary>
    internal static string ToV4MonitorType(string v5Type) => v5Type switch
    {
        "OBS_MONITORING_TYPE_MONITOR_ONLY" => "monitorOnly",
        "OBS_MONITORING_TYPE_MONITOR_AND_OUTPUT" => "monitorAndOutput",
        "OBS_MONITORING_TYPE_NONE" => "none",
        _ => "none",
    };

    /// <summary>v4 的监听类型短名 → v5 枚举（读回来时用，否则上层拿 v5 枚举比会永远不等）。</summary>
    internal static string ToV5MonitorType(string v4Type) => v4Type switch
    {
        "monitorOnly" => "OBS_MONITORING_TYPE_MONITOR_ONLY",
        "monitorAndOutput" => "OBS_MONITORING_TYPE_MONITOR_AND_OUTPUT",
        _ => "OBS_MONITORING_TYPE_NONE",
    };

    /// <summary>该 v5 请求在旧协议下是否支持。</summary>
    public static bool SupportsRequest(string v5Type) => MapRequest(v5Type, null) is not null
        // 这三个要带参数才能算出目标请求（媒体动作按动作拆请求；暂停切换与批处理在客户端里特殊处理），
        // 传 null 的探测会得到 null —— 必须显式列为「支持」，否则上层会误报「旧协议不支持」。
        || v5Type is "ToggleRecordPause" or "CallBatch" or "TriggerMediaInputAction";

    // ---------------------------------------------------------------- 响应归一化（v4 → v5 形状）

    /// <summary>
    /// 把 v4 的响应对象归一化成 v5 的字段名，让上层代码不必知道连的是哪一代协议。
    /// 未知请求原样返回。
    /// </summary>
    public static JsonObject NormalizeResponse(string v5Type, JsonElement v4)
    {
        var o = new JsonObject();
        switch (v5Type)
        {
            case "GetVersion":
                o["obsVersion"] = Str(v4, "obs-studio-version") ?? Str(v4, "version") ?? "";
                o["obsWebSocketVersion"] = Str(v4, "obs-websocket-version") ?? "";
                o["platformDescription"] = Str(v4, "platform-description") ?? Str(v4, "platform") ?? "";
                break;

            case "GetVideoSettings":
                o["baseWidth"] = Num(v4, "baseWidth");
                o["baseHeight"] = Num(v4, "baseHeight");
                o["outputWidth"] = Num(v4, "outputWidth");
                o["outputHeight"] = Num(v4, "outputHeight");
                // v4 只给一个 fps；v5 要分子分母 —— 换算成 fps/1 让上层算出同一个值
                o["fpsNumerator"] = Num(v4, "fps");
                o["fpsDenominator"] = 1;
                break;

            case "GetSceneList":
                {
                    o["currentProgramSceneName"] = Str(v4, "current-scene") ?? "";
                    var scenes = new JsonArray();
                    if (v4.TryGetProperty("scenes", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        // v4 的 scenes 是「OBS 界面从上到下」的顺序；v5 是倒序且带 sceneIndex。
                        // 这里按 v5 的约定反着填索引，上层那句「按索引降序排」就能得到同样的界面顺序。
                        var names = arr.EnumerateArray()
                            .Select(e => Str(e, "name") ?? "")
                            .Where(n => n.Length > 0)
                            .ToList();
                        for (var i = 0; i < names.Count; i++)
                            scenes.Add(new JsonObject { ["sceneName"] = names[i], ["sceneIndex"] = names.Count - 1 - i });
                    }
                    o["scenes"] = scenes;
                    break;
                }

            case "GetSceneItemList":
                {
                    o["sceneName"] = Str(v4, "sceneName") ?? "";
                    var items = new JsonArray();
                    if (v4.TryGetProperty("sceneItems", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var e in arr.EnumerateArray())
                        {
                            items.Add(new JsonObject
                            {
                                ["sceneItemId"] = Num(e, "itemId"),
                                ["sourceName"] = Str(e, "sourceName") ?? "",
                                // v4 的 GetSceneItemList 不带可见性，客户端会用 ExecuteBatch 补齐；
                                // 补不到时按「可见」处理（与 OBS 默认一致）
                                ["sceneItemEnabled"] = e.TryGetProperty("visible", out var vis) ? vis.GetBoolean() : true,
                                ["sceneItemLocked"] = e.TryGetProperty("locked", out var lk) && lk.GetBoolean(),
                            });
                        }
                    }
                    o["sceneItems"] = items;
                    break;
                }

            case "GetInputList":
                {
                    var inputs = new JsonArray();
                    if (v4.TryGetProperty("sources", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var e in arr.EnumerateArray())
                        {
                            // v4 的 GetSourcesList 把场景 / 分组 / 滤镜一起列出来了，v5 的 GetInputList 只给输入
                            var type = Str(e, "type") ?? "";
                            if (!string.Equals(type, "input", StringComparison.OrdinalIgnoreCase)) continue;
                            inputs.Add(new JsonObject
                            {
                                ["inputName"] = Str(e, "name") ?? "",
                                ["inputKind"] = Str(e, "typeId") ?? type,
                            });
                        }
                    }
                    o["inputs"] = inputs;
                    break;
                }

            case "GetInputKindList":
                {
                    var kinds = new JsonArray();
                    if (v4.TryGetProperty("types", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var e in arr.EnumerateArray())
                        {
                            // v4 的 GetSourceTypesList 把过滤器 / 转场类型一起列出来了，v5 的 GetInputKindList 只给输入。
                            // 不过滤的话，插件的过滤器类型会被当成「可用输入类型」（审查指出语义偏宽）。
                            var kindType = Str(e, "type");
                            if (!string.IsNullOrEmpty(kindType)
                                && !string.Equals(kindType, "input", StringComparison.OrdinalIgnoreCase))
                                continue;
                            var id = Str(e, "typeId");
                            if (!string.IsNullOrEmpty(id)) kinds.Add(id);
                        }
                    }
                    o["inputKinds"] = kinds;
                    break;
                }

            case "GetInputMute":
                o["inputMuted"] = v4.TryGetProperty("muted", out var m) && m.ValueKind == JsonValueKind.True;
                break;

            case "GetInputVolume":
                {
                    var mul = v4.TryGetProperty("volume", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
                    var muted = v4.TryGetProperty("muted", out var mm) && mm.ValueKind == JsonValueKind.True;
                    o["inputVolumeMul"] = mul;
                    o["inputVolumeDb"] = muted ? -100 : MulToDb(mul);
                    break;
                }

            case "GetRecordStatus":
                o["outputActive"] = v4.TryGetProperty("isRecording", out var rec) && rec.ValueKind == JsonValueKind.True;
                o["outputPaused"] = v4.TryGetProperty("isRecordingPaused", out var p) && p.ValueKind == JsonValueKind.True;
                o["outputTimecode"] = Str(v4, "recordTimecode") ?? "";
                o["outputPath"] = Str(v4, "recordingFilename") ?? "";
                break;

            case "GetStreamStatus":
                o["outputActive"] = v4.TryGetProperty("streaming", out var st) && st.ValueKind == JsonValueKind.True;
                o["outputReconnecting"] = false;
                o["outputSkippedFrames"] = Num(v4, "num-dropped-frames");
                o["outputTotalFrames"] = Num(v4, "num-total-frames");
                // v4 的时长字段是 stream-timecode（HH:MM:SS.mmm）；部分版本只给 total-stream-time（秒）
                var totalSeconds = v4.TryGetProperty("total-stream-time", out var tst) && tst.ValueKind == JsonValueKind.Number
                    ? tst.GetDouble() : 0;
                o["outputTimecode"] = Str(v4, "stream-timecode") ?? SecondsToTimecode(totalSeconds);
                // v4 用 strain（0~1 的「吃力程度」）表达上行压力，与 v5 的 congestion 语义接近
                o["outputCongestion"] = Num(v4, "strain");
                break;

            case "GetStreamServiceSettings":
                {
                    // v4 是 GetStreamSettings：{ type, settings: { server, key, ... } }。
                    // 关键：**只保留存在性**，不把 server / key 带进统一数据模型 ——
                    // 串流密钥属于最敏感的一类凭据，本应用没有任何理由持有它的明文（V3.0 / D2）。
                    var settings = v4.TryGetProperty("settings", out var s) && s.ValueKind == JsonValueKind.Object
                        ? s : default;
                    var server = settings.ValueKind == JsonValueKind.Object ? Str(settings, "server") : null;
                    var key = settings.ValueKind == JsonValueKind.Object ? Str(settings, "key") : null;

                    o["streamServiceType"] = Str(v4, "type") ?? "";
                    o["hasStreamServer"] = !string.IsNullOrWhiteSpace(server);
                    o["hasStreamKey"] = !string.IsNullOrWhiteSpace(key);
                    break;
                }

            case "GetInputAudioSyncOffset":
                {
                    // v4 的 offset 单位是纳秒，统一成 v5 的毫秒（V3.0 / D7）
                    var nanos = v4.TryGetProperty("offset", out var off) && off.ValueKind == JsonValueKind.Number
                        ? off.GetDouble() : 0;
                    o["inputAudioSyncOffset"] = Math.Round(nanos / 1_000_000.0);
                    break;
                }

            case "GetVirtualCamStatus":
                o["outputActive"] = v4.TryGetProperty("isVirtualCam", out var vc) && vc.ValueKind == JsonValueKind.True;
                break;

            case "GetReplayBufferStatus":
                o["outputActive"] = v4.TryGetProperty("isReplayBufferActive", out var rb) && rb.ValueKind == JsonValueKind.True;
                break;

            case "GetStats":
                {
                    // 4.9.1 的 GetStats 响应是**嵌套**的：{ "stats": { "cpu-usage": …, "fps": … } }。
                    // 审查发现的缺陷：原先从顶层读，于是旧协议下所有性能数据都是 0。
                    var s = v4.TryGetProperty("stats", out var nested) && nested.ValueKind == JsonValueKind.Object
                        ? nested
                        : v4;   // 容错：万一某些版本是平铺的
                    o["cpuUsage"] = Num(s, "cpu-usage");
                    o["memoryUsage"] = Num(s, "memory-usage");
                    o["availableDiskSpace"] = Num(s, "free-disk-space");
                    o["activeFps"] = Num(s, "fps");
                    o["averageFrameRenderTime"] = Num(s, "average-frame-time");
                    o["renderSkippedFrames"] = Num(s, "render-missed-frames");
                    o["renderTotalFrames"] = Num(s, "render-total-frames");
                    o["outputSkippedFrames"] = Num(s, "output-skipped-frames");
                    o["outputTotalFrames"] = Num(s, "output-total-frames");
                    break;
                }

            case "GetRecordDirectory":
                o["recordDirectory"] = Str(v4, "rec-folder") ?? "";
                break;

            case "GetSceneCollectionList":
                {
                    // 形状必须与 v5 一致：**字符串数组**。
                    // 审查发现的缺陷：原先产出对象数组 [{sceneCollectionName}]，
                    // 而 ObsResetService / SceneTemplateService 的解析器只接受字符串数组 ——
                    // 于是 Win7 上「彻底重置」「应用模板」必失败。
                    var list = new JsonArray();
                    if (v4.TryGetProperty("scene-collections", out var arr) && arr.ValueKind == JsonValueKind.Array)
                        foreach (var e in arr.EnumerateArray())
                        {
                            var name = Str(e, "sc-name");
                            if (!string.IsNullOrEmpty(name)) list.Add(name);
                        }
                    o["sceneCollections"] = list;
                    // 当前场景集合名要另发一次 GetCurrentSceneCollection，这里不猜
                    o["currentSceneCollectionName"] = "";
                    break;
                }

            case "GetSceneTransitionList":
                {
                    var list = new JsonArray();
                    if (v4.TryGetProperty("transitions", out var arr) && arr.ValueKind == JsonValueKind.Array)
                        foreach (var e in arr.EnumerateArray())
                            list.Add(new JsonObject { ["transitionName"] = Str(e, "name") ?? "" });
                    o["transitions"] = list;
                    o["currentTransitionName"] = Str(v4, "current-transition") ?? "";
                    break;
                }

            case "GetProfileList":
                {
                    var list = new JsonArray();
                    if (v4.TryGetProperty("profiles", out var arr) && arr.ValueKind == JsonValueKind.Array)
                        foreach (var e in arr.EnumerateArray())
                            list.Add(new JsonObject { ["profileName"] = Str(e, "profile-name") ?? "" });
                    o["profiles"] = list;
                    break;
                }

            case "GetInputSettings":
                // v4 GetSourceSettings 返回 {sourceType, sourceSettings} → v5 形状 {inputKind, inputSettings}
                o["inputKind"] = Str(v4, "sourceType") ?? "";
                o["inputSettings"] = v4.TryGetProperty("sourceSettings", out var ss) && ss.ValueKind == JsonValueKind.Object
                    ? JsonNode.Parse(ss.GetRawText())
                    : new JsonObject();
                break;

            case "GetSceneItemTransform":
                // v4 GetSceneItemProperties 返回扁平字段（position/scale/crop/rotation/bounds 直接挂在顶层）
                // → v5 把变换放在 sceneItemTransform 对象里，且裁剪是**扁平** cropLeft/...（第四轮验证修正）
                {
                    var tf = new JsonObject();
                    foreach (var name in new[] { "positionX", "positionY", "scaleX", "scaleY", "rotation",
                                                 "cropLeft", "cropTop", "cropRight", "cropBottom",
                                                 "boundsWidth", "boundsHeight", "alignment", "boundsType" })
                    {
                        if (v4.TryGetProperty(name, out var nv)) tf[name] = JsonNode.Parse(nv.GetRawText());
                    }
                    o["sceneItemTransform"] = tf;
                    break;
                }

            case "GetSourceFilter":
                o["filterSettings"] = v4.TryGetProperty("filterSettings", out var fs2) && fs2.ValueKind == JsonValueKind.Object
                    ? JsonNode.Parse(fs2.GetRawText())
                    : new JsonObject();
                break;

            case "GetInputAudioMonitorType":
                // v4 返回短名（monitorOnly），上层按 v5 枚举比较 → 必须翻译，否则永远判为「不同」
                o["monitorType"] = ToV5MonitorType(Str(v4, "monitorType") ?? "none");
                break;

            case "GetMediaInputStatus":
                // v4 GetMediaState 返回 playing / paused / stopped / ended（短名）→ 统一成 v5 风格枚举名，
                // 否则 ObsMediaStatus.IsPlaying 之类的判断全部失配
                {
                    var state = Str(v4, "mediaState") ?? "";
                    o["mediaState"] = state.Length == 0 ? "" : "OBS_MEDIA_STATE_" + state.ToUpperInvariant();
                    break;
                }

            case "GetCurrentProgramScene":
                // v4 GetCurrentScene 用 name；v5 用 currentProgramSceneName / sceneName
                o["currentProgramSceneName"] = Str(v4, "name") ?? "";
                o["sceneName"] = Str(v4, "name") ?? "";
                break;

            case "GetSourceScreenshot":
                // v4 用 img（Data URI），v5 用 imageData —— 统一成 imageData
                o["imageData"] = Str(v4, "img") ?? "";
                break;

            case "GetSourceFilterList":
                {
                    // v4：{filters:[{name,type,enabled,settings}]}
                    // v5：{filters:[{filterName,filterKind,filterIndex,filterEnabled,filterSettings}]}
                    // 字段名完全不同，必须逐条改名 —— 直接透传会让上层读到的全是空值（不报错）。
                    var list = new JsonArray();
                    var index = 0;
                    if (v4.TryGetProperty("filters", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var e in arr.EnumerateArray())
                        {
                            list.Add(new JsonObject
                            {
                                ["filterName"] = Str(e, "name") ?? "",
                                ["filterKind"] = Str(e, "type") ?? "",
                                ["filterIndex"] = index++,
                                ["filterEnabled"] = e.TryGetProperty("enabled", out var en) && en.ValueKind == JsonValueKind.True,
                                ["filterSettings"] = e.TryGetProperty("settings", out var fset) ? JsonNode.Parse(fset.GetRawText()) : new JsonObject(),
                            });
                        }
                    }
                    o["filters"] = list;
                    break;
                }

            case "GetStudioModeEnabled":
                {
                    // v4 GetStudioModeStatus 的响应字段是 **studio-mode**（连字符，官方源码
                    // obs_data_set_bool(response, "studio-mode", previewActive)）；
                    // 写成 studioMode 会永远读不到 → 静默恒为「关」，与真的关闭不可区分。
                    var enabled = v4.TryGetProperty("studio-mode", out var sm)
                        ? sm.ValueKind == JsonValueKind.True
                        : v4.TryGetProperty("studioMode", out var sm2) && sm2.ValueKind == JsonValueKind.True;
                    o["studioModeEnabled"] = enabled;
                    break;
                }

            case "CreateSceneItem":
            case "CreateInput":
                // v4 的 AddSceneItem / CreateSource 都只返回 itemId；v5 统一叫 sceneItemId。
                // 漏掉 CreateInput 这一支会让落地器拿到 -1 → 变换/层级/显隐全部静默跳过（Win7 上来源全不可见）。
                // 第四轮验证补：**缺失字段必须给 -1**（Num 的默认 0 是合法 id，会让上层「拿不到 id」的
                // 检测永不触发，于是照样静默跳过）。
                o["sceneItemId"] = v4.TryGetProperty("itemId", out var createdItemId) && createdItemId.ValueKind == JsonValueKind.Number
                    ? createdItemId.GetInt32()
                    : -1;
                if (v4.TryGetProperty("sourceName", out var createdName) && createdName.ValueKind == JsonValueKind.String)
                    o["inputName"] = createdName.GetString() ?? "";
                break;

            default:
                return JsonNode.Parse(v4.GetRawText()) as JsonObject ?? new JsonObject();
        }
        return o;
    }

    // ---------------------------------------------------------------- 事件映射（v4 → v5）

    /// <summary>
    /// 把 v4 事件映射成 v5 事件名与字段；返回 null 表示上层不关心这条事件。
    /// </summary>
    public static (string Type, JsonObject Data)? MapEvent(JsonElement e)
    {
        var type = Str(e, "update-type");
        if (string.IsNullOrEmpty(type)) return null;

        switch (type)
        {
            case "StudioModeSwitched":
                // v4 的事件字段是 new-state（布尔）：映射成 v5 的 StudioModeStateChanged
                return ("StudioModeStateChanged", new JsonObject
                {
                    ["studioModeEnabled"] = e.TryGetProperty("new-state", out var ns) && ns.ValueKind == JsonValueKind.True,
                });

            case "SwitchScenes":
                return ("CurrentProgramSceneChanged", new JsonObject { ["sceneName"] = Str(e, "scene-name") ?? "" });

            case "ScenesChanged":
            case "SceneCollectionChanged":
            case "ProfileChanged":
                return ("SceneListChanged", new JsonObject());

            case "RecordingStarted":
                return ("RecordStateChanged", new JsonObject
                {
                    ["outputActive"] = true,
                    ["outputState"] = "OBS_WEBSOCKET_OUTPUT_STARTED"
                });

            case "RecordingStopped":
                return ("RecordStateChanged", new JsonObject
                {
                    ["outputActive"] = false,
                    ["outputState"] = "OBS_WEBSOCKET_OUTPUT_STOPPED"
                });

            case "RecordingPaused":
                return ("RecordStateChanged", new JsonObject
                {
                    ["outputActive"] = true,
                    ["outputState"] = "OBS_WEBSOCKET_OUTPUT_PAUSED"
                });

            case "RecordingResumed":
                return ("RecordStateChanged", new JsonObject
                {
                    ["outputActive"] = true,
                    ["outputState"] = "OBS_WEBSOCKET_OUTPUT_RESUMED"
                });

            case "StreamStarted":
                return ("StreamStateChanged", new JsonObject
                {
                    ["outputActive"] = true,
                    ["outputState"] = "OBS_WEBSOCKET_OUTPUT_STARTED"
                });

            case "StreamStopped":
                return ("StreamStateChanged", new JsonObject
                {
                    ["outputActive"] = false,
                    ["outputState"] = "OBS_WEBSOCKET_OUTPUT_STOPPED"
                });

            case "VirtualCamStarted":
                return ("VirtualcamStateChanged", new JsonObject { ["outputActive"] = true });

            case "VirtualCamStopped":
                return ("VirtualcamStateChanged", new JsonObject { ["outputActive"] = false });

            case "ReplayStarting":
            case "ReplayStarted":
                return ("ReplayBufferStateChanged", new JsonObject { ["outputActive"] = true });

            case "ReplayStopping":
            case "ReplayStopped":
                return ("ReplayBufferStateChanged", new JsonObject { ["outputActive"] = false });

            case "SourceMuteStateChanged":
                return ("InputMuteStateChanged", new JsonObject
                {
                    ["inputName"] = Str(e, "sourceName") ?? "",
                    ["inputMuted"] = e.TryGetProperty("muted", out var mu) && mu.ValueKind == JsonValueKind.True
                });

            case "SourceVolumeChanged":
                {
                    var mul = e.TryGetProperty("volume", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
                    return ("InputVolumeChanged", new JsonObject
                    {
                        ["inputName"] = Str(e, "sourceName") ?? "",
                        ["inputVolumeMul"] = mul,
                        ["inputVolumeDb"] = MulToDb(mul)
                    });
                }

            case "SceneItemVisibilityChanged":
                return ("SceneItemEnableStateChanged", new JsonObject
                {
                    ["sceneName"] = Str(e, "scene-name") ?? "",
                    ["sceneItemId"] = Num(e, "item-id"),
                    ["sceneItemEnabled"] = e.TryGetProperty("item-visible", out var iv) && iv.ValueKind == JsonValueKind.True
                });

            case "SourceCreated":
                return ("InputCreated", new JsonObject { ["inputName"] = Str(e, "sourceName") ?? "" });

            case "SourceDestroyed":
                return ("InputRemoved", new JsonObject { ["inputName"] = Str(e, "sourceName") ?? "" });

            case "SourceRenamed":
                return ("InputNameChanged", new JsonObject
                {
                    ["inputName"] = Str(e, "newName") ?? "",
                    ["oldInputName"] = Str(e, "previousName") ?? ""
                });

            case "SceneItemAdded":
                return ("SceneItemCreated", new JsonObject
                {
                    ["sceneName"] = Str(e, "scene-name") ?? "",
                    ["sceneItemId"] = Num(e, "item-id"),
                    ["sourceName"] = Str(e, "item-name") ?? ""
                });

            case "SceneItemRemoved":
                return ("SceneItemRemoved", new JsonObject
                {
                    ["sceneName"] = Str(e, "scene-name") ?? "",
                    ["sceneItemId"] = Num(e, "item-id"),
                    ["sourceName"] = Str(e, "item-name") ?? ""
                });

            case "Exiting":
                return ("ExitStarted", new JsonObject());

            // Heartbeat / StreamStatus / 其它：上层要么已经自行轮询，要么不关心
            default:
                return null;
        }
    }

    // ---------------------------------------------------------------- 小工具

    /// <summary>把「秒数」转成 OBS 的 <c>HH:MM:SS.mmm</c> 时长串（拿不到时才用的兜底字段）。</summary>
    private static string SecondsToTimecode(double seconds)
    {
        if (seconds <= 0) return "";
        var t = TimeSpan.FromSeconds(seconds);
        return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds:000}";
    }

    /// <summary>
    /// 把 v5 的扁平 <c>sceneItemTransform</c> 摊成 v4 <c>SetSceneItemProperties</c> 的嵌套字段。
    ///
    /// v5 发的是 <c>{positionX, positionY, alignment, crop{…}, scaleX, scaleY, boundsType, boundsWidth, boundsHeight}</c>，
    /// v4 收的是 <c>{position:{x,y,alignment}, crop:{…}, scale.{x,y}, bounds:{type,x,y}}</c>。
    /// 只在字段确实存在时才写对应的嵌套对象，避免用 0 覆盖用户原有的位置。
    /// </summary>
    private static Dictionary<string, object?> BuildSceneItemTransform(JsonElement? data)
    {
        var result = new Dictionary<string, object?>
        {
            ["scene-name"] = data is { } d && d.TryGetProperty("sceneName", out var sn) && sn.ValueKind == JsonValueKind.String
                ? sn.GetString() : "",
            ["item"] = new Dictionary<string, object?>
            {
                ["id"] = data is { } d2 && d2.TryGetProperty("sceneItemId", out var id) && id.ValueKind == JsonValueKind.Number
                    ? id.GetInt32() : 0
            }
        };

        if (data is not { } root || !root.TryGetProperty("sceneItemTransform", out var tf) || tf.ValueKind != JsonValueKind.Object)
            return result;

        var position = new Dictionary<string, object?>();
        if (tf.TryGetProperty("positionX", out var px) && px.ValueKind == JsonValueKind.Number) position["x"] = px.GetDouble();
        if (tf.TryGetProperty("positionY", out var py) && py.ValueKind == JsonValueKind.Number) position["y"] = py.GetDouble();
        if (tf.TryGetProperty("alignment", out var al) && al.ValueKind == JsonValueKind.Number) position["alignment"] = al.GetInt32();
        if (position.Count > 0) result["position"] = position;

        var scale = new Dictionary<string, object?>();
        if (tf.TryGetProperty("scaleX", out var sx) && sx.ValueKind == JsonValueKind.Number) scale["x"] = sx.GetDouble();
        if (tf.TryGetProperty("scaleY", out var sy) && sy.ValueKind == JsonValueKind.Number) scale["y"] = sy.GetDouble();
        if (scale.Count > 0) result["scale"] = scale;

        // V3.0 第四轮验证修正：上层发来的 v5 变换里裁剪是**扁平**字段（cropLeft/cropTop/...），
        // 而 v4 的 SetSceneItemProperties 要**嵌套** crop 对象 —— 原先按「输入也带嵌套 crop」写，
        // 上层一改对，v4 这边就整块丢掉（裁剪永远 0，而返回码是成功）。
        if (tf.TryGetProperty("cropLeft", out _) || tf.TryGetProperty("cropTop", out _)
            || tf.TryGetProperty("cropRight", out _) || tf.TryGetProperty("cropBottom", out _))
        {
            int Crop(string name) => tf.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
            result["crop"] = new Dictionary<string, object?>
            {
                ["top"] = Crop("cropTop"),
                ["bottom"] = Crop("cropBottom"),
                ["left"] = Crop("cropLeft"),
                ["right"] = Crop("cropRight"),
            };
        }

        // 旋转在两边都是扁平字段（v5 rotation / v4 rotation）
        if (tf.TryGetProperty("boundsType", out var bt) && bt.ValueKind == JsonValueKind.String)
        {
            var bounds = new Dictionary<string, object?> { ["type"] = bt.GetString() };
            if (tf.TryGetProperty("boundsWidth", out var bw) && bw.ValueKind == JsonValueKind.Number) bounds["x"] = bw.GetDouble();
            if (tf.TryGetProperty("boundsHeight", out var bh) && bh.ValueKind == JsonValueKind.Number) bounds["y"] = bh.GetDouble();
            result["bounds"] = bounds;
        }

        // 旋转（两边都是扁平字段；这一行原本就在，第四轮验证只是确认它没错）
        if (tf.TryGetProperty("rotation", out var rot) && rot.ValueKind == JsonValueKind.Number) result["rotation"] = rot.GetDouble();
        return result;
    }

    private static string? Str(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    /// <summary>
    /// 取一个数值字段并保留它原本的「整数 / 小数」形态。
    ///
    /// 为什么要区分：上层（<see cref="ObsConnectionService"/>）用 <c>GetInt32()</c> / <c>GetDouble()</c> 分别读取
    /// 这两类字段，而 <c>JsonValue</c> 不做数值类型转换 —— 把 <c>baseWidth</c> 写成 double、
    /// 或把 <c>fps</c> 写成 long，都会在运行时抛 InvalidOperationException / FormatException。
    /// </summary>
    private static JsonNode? Num(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Number)
            return 0;
        if (v.TryGetInt32(out var i)) return JsonValue.Create(i);
        if (v.TryGetInt64(out var l)) return JsonValue.Create(l);
        return JsonValue.Create(v.GetDouble());
    }
}
