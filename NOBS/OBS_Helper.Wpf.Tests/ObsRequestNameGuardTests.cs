using System.Text.RegularExpressions;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// obs-websocket **v5 请求名**守卫（V3.0 第四轮验证新增）。
///
/// 为什么需要它：请求名写错（多一个词、少一个 Scene、用了 v4 的名字）在编译期、单元测试、
/// 无界面自检里**都不会报错** —— 运行时 OBS 回一个 "invalid request type"，或者更糟：
/// 参数被忽略但返回码是成功。第四轮交叉验证正是靠人工逐名比对，抓到
/// <c>Set/GetSceneTransitionOverride</c> 少了第二个 Scene（v5 真实名字是
/// <c>Set/GetSceneSceneTransitionOverride</c>），代价是「场景过渡覆盖」捕获永远为空、落地永远不生效而**完全静默**。
///
/// 这个测试把「源码里出现的每个 v5 请求名都必须在官方协议表里」变成一条断言：
/// 新增/改名请求时如果没对照协议，测试直接红，而不是等用户遇到一个静默失效的功能。
/// </summary>
public class ObsRequestNameGuardTests
{
    /// <summary>
    /// 官方 obs-websocket 5.x 请求名（据 protocol.md 整理；**不是从本仓源码抄的** ——
    /// 从源码抄就失去意义了）。只列本应用涉及的域，够用且便于核对。
    /// </summary>
    private static readonly HashSet<string> OfficialV5Requests = new(StringComparer.Ordinal)
    {
        // 通用 / 版本 / 统计 / 热键 / 持久数据
        "GetVersion", "GetStats", "BroadcastCustomEvent", "CallVendorRequest",
        "GetHotkeyList", "TriggerHotkeyByName", "TriggerHotkeyByKeySequence", "Sleep",
        "GetPersistentData", "SetPersistentData",
        // 场景集合 / 配置档案 / 视频 / 串流服务
        "GetSceneCollectionList", "SetCurrentSceneCollection", "CreateSceneCollection",
        "GetProfileList", "SetCurrentProfile", "CreateProfile", "RemoveProfile",
        "GetProfileParameter", "SetProfileParameter",
        "GetVideoSettings", "SetVideoSettings",
        "GetStreamServiceSettings", "SetStreamServiceSettings",
        "GetRecordDirectory", "SetRecordDirectory",
        // 输入（来源）
        "GetInputList", "GetInputKindList", "GetSpecialInputs", "CreateInput", "RemoveInput",
        "SetInputName", "GetInputSettings", "SetInputSettings",
        "GetInputMute", "SetInputMute", "ToggleInputMute",
        "GetInputVolume", "SetInputVolume",
        "GetInputAudioBalance", "SetInputAudioBalance",
        "GetInputAudioSyncOffset", "SetInputAudioSyncOffset",
        "GetInputAudioMonitorType", "SetInputAudioMonitorType",
        "GetInputAudioTracks", "SetInputAudioTracks",
        "GetInputDeinterlaceMode", "SetInputDeinterlaceMode",
        "GetInputDeinterlaceFieldOrder", "SetInputDeinterlaceFieldOrder",
        "GetInputPropertiesListPropertyItems", "PressInputPropertiesButton",
        // 场景元素
        "GetSceneItemList", "GetGroupSceneItemList", "GetSceneItemId", "CreateSceneItem",
        "RemoveSceneItem", "DuplicateSceneItem",
        "GetSceneItemTransform", "SetSceneItemTransform",
        "GetSceneItemEnabled", "SetSceneItemEnabled",
        "GetSceneItemLocked", "SetSceneItemLocked",
        "GetSceneItemIndex", "SetSceneItemIndex",
        "GetSceneItemBlendMode", "SetSceneItemBlendMode",
        // 场景
        "GetSceneList", "GetCurrentProgramScene", "SetCurrentProgramScene",
        "GetCurrentPreviewScene", "SetCurrentPreviewScene",
        "CreateScene", "RemoveScene", "SetSceneName",
        // 注意：这里就是双 Scene —— 官方名字是 SceneSceneTransitionOverride
        "GetSceneSceneTransitionOverride", "SetSceneSceneTransitionOverride",
        // 转场
        "GetTransitionKindList", "GetSceneTransitionList", "GetCurrentSceneTransition",
        "SetCurrentSceneTransition", "SetCurrentSceneTransitionDuration",
        "SetCurrentSceneTransitionSettings", "GetCurrentSceneTransitionCursor",
        "TriggerStudioModeTransition",
        // 滤镜
        "GetSourceFilterKindList", "GetSourceFilterList", "GetSourceFilterDefaultSettings",
        "CreateSourceFilter", "RemoveSourceFilter", "SetSourceFilterName", "GetSourceFilter",
        "SetSourceFilterIndex", "SetSourceFilterSettings", "SetSourceFilterEnabled",
        // 演播室模式
        "GetStudioModeEnabled", "SetStudioModeEnabled",
        // 输出 / 虚拟摄像头 / 回放缓冲
        "GetVirtualCamStatus", "ToggleVirtualCam", "StartVirtualCam", "StopVirtualCam",
        "GetReplayBufferStatus", "ToggleReplayBuffer", "StartReplayBuffer", "StopReplayBuffer",
        "SaveReplayBuffer", "GetLastReplayBufferReplay",
        "GetOutputList", "GetOutputStatus", "ToggleOutput", "StartOutput", "StopOutput",
        "GetOutputSettings", "SetOutputSettings",
        // 推流 / 录制
        "GetStreamStatus", "ToggleStream", "StartStream", "StopStream", "SendStreamCaption",
        "GetRecordStatus", "ToggleRecord", "StartRecord", "StopRecord",
        "ToggleRecordPause", "PauseRecord", "ResumeRecord",
        // 媒体源
        "TriggerMediaInputAction", "GetMediaInputStatus", "SetMediaInputCursor", "OffsetMediaInputCursor",
        // 截图 / 来源状态
        "GetSourceActive", "GetSourceScreenshot", "SaveSourceScreenshot",
        // 界面与投影
        "GetMonitorList", "OpenVideoMixProjector", "OpenSourceProjector",
        "OpenInputPropertiesDialog", "OpenInputFiltersDialog", "OpenInputInteractDialog",
    };

    /// <summary>
    /// 豁免清单：**不是** v5 请求名，但在源码里同样以 <c>RequestAsync("X")</c> 形式出现 ——
    /// 它们是旧协议握手或客户端内部伪请求，逐条给出理由，避免「豁免」变成随便放行。
    /// </summary>
    private static readonly Dictionary<string, string> AllowedNonV5 = new(StringComparer.Ordinal)
    {
        ["Authenticate"] = "v4.9 握手请求（只在旧协议路径发）",
        ["GetAuthRequired"] = "v4.9 握手请求（判断是否需要密码）",
        ["ExecuteBatch"] = "客户端伪请求：一次 socket 往返里跑多条子请求（obs-websocket v5 的 ExecuteBatch，v4 由客户端自行展开）",
        ["CallBatch"] = "客户端伪请求（旧协议批处理入口）",
        // 注：ToggleRecordPause 是官方名，已在上表；这里不重复豁免
    };

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

    /// <summary>源码里所有 <c>RequestAsync("X", ...)</c> / <c>MapRequest("X", ...)</c> 的请求名。</summary>
    private static Dictionary<string, List<string>> UsedRequestNames()
    {
        var projectDir = Path.Combine(SourceRoot(), "OBS_Helper.Wpf");
        var used = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var pattern = new Regex(@"(?:RawRequestAsync|RequestAsync|MapRequest|SupportsRequest)\(\s*""([A-Za-z0-9_]+)""");

        foreach (var file in Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;

            foreach (Match m in pattern.Matches(File.ReadAllText(file)))
            {
                var name = m.Groups[1].Value;
                if (!used.TryGetValue(name, out var where)) used[name] = where = new List<string>();
                var leaf = Path.GetFileName(file);
                if (!where.Contains(leaf)) where.Add(leaf);
            }
        }
        return used;
    }

    [Fact]
    public void EveryUsedRequestNameIsAnOfficialV5Request()
    {
        var used = UsedRequestNames();
        Assert.True(used.Count > 50, $"源码里只找到 {used.Count} 个请求名，扫描逻辑可能失效");

        var bogus = used
            .Where(kv => !OfficialV5Requests.Contains(kv.Key) && !AllowedNonV5.ContainsKey(kv.Key))
            .Select(kv => $"{kv.Key}（出现在 {string.Join(", ", kv.Value)}）")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.True(bogus.Count == 0,
            "以下请求名不在官方 v5 请求表里，也没有豁免理由 —— 请对照 obs-websocket protocol.md 确认：\n  "
            + string.Join("\n  ", bogus));
    }

    /// <summary>豁免项必须写了理由（防止「豁免」被当成万能放行条）。</summary>
    [Fact]
    public void AllowedNonV5EntriesHaveReasons()
        => Assert.All(AllowedNonV5, kv => Assert.True(kv.Value.Length > 5, $"{kv.Key} 的豁免理由太短"));

    /// <summary>
    /// 反向守卫：**已被证实错误**的请求名不允许再出现在源码里。
    /// 这一条是本轮的真实教训 —— <c>SetSceneTransitionOverride</c> 曾被当成 v5 名字用了一整轮。
    /// </summary>
    [Theory]
    [InlineData("SetSceneTransitionOverride")]
    [InlineData("GetSceneTransitionOverride")]
    [InlineData("GetStudioModeStatus")]
    [InlineData("EnableStudioMode")]
    [InlineData("DisableStudioMode")]
    [InlineData("TransitionToProgram")]
    [InlineData("AddFilterToSource")]
    [InlineData("GetMediaState")]
    [InlineData("TakeSourceScreenshot", "ObsConnectionService.cs")]   // 只允许出现在旧协议核心与测试里
    public void KnownWrongV5NamesAreNotUsedAsV5Names(string wrongName, string? allowedFile = null)
    {
        var projectDir = Path.Combine(SourceRoot(), "OBS_Helper.Wpf");
        var hits = new List<string>();

        // 只检查「当作 v5 请求名发给客户端」的调用形态；旧协议映射表里的 v4 名字是**字符串字面量参数**，
        // 形态是 new LegacyRequest("X" / MapRequest("v5名"，因此不会被这个正则命中。
        var pattern = new Regex($@"(?:RawRequestAsync|RequestAsync)\(\s*""{Regex.Escape(wrongName)}""");

        foreach (var file in Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;
            if (allowedFile is not null && Path.GetFileName(file) == allowedFile) continue;
            if (pattern.IsMatch(File.ReadAllText(file))) hits.Add(Path.GetFileName(file));
        }

        Assert.True(hits.Count == 0,
            $"「{wrongName}」不是 v5 请求名（旧协议名字），却被当成 v5 请求发出：{string.Join(", ", hits)}");
    }
}
