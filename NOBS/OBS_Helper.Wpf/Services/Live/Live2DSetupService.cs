using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using OBS_Helper.Wpf.Localization;
using OBS_Helper.Wpf.Models.Live;
using OBS_Helper.Wpf.Services.Obs;

namespace OBS_Helper.Wpf.Services.Live;

/// <summary>Live2D 环境体检结果（V3.3.0）。</summary>
public sealed record Live2DEnvStatus(
    bool ObsConnected,
    bool VtsRunning,
    int CanvasWidth,
    int CanvasHeight,
    Live2DCaptureKind Capture,
    string CaptureHintKey);

/// <summary>Live2D 一键配置结果。</summary>
public sealed record Live2DApplyResult(
    bool Ok,
    string? ErrorCode,
    IReadOnlyList<string> Notes,
    string SceneName);

/// <summary>
/// Live2D 一键配置编排（V3.3.0 / P0-3）：
/// 校验模型 → 确保 OBS 连接 → 选采集方式 → 建场景 / 源 / 变换 → （可选）在 VTS 里加载模型。
///
/// 设计约束：
/// <list type="bullet">
///   <item>默认**新建**「虚拟形象」场景，不覆盖用户已有场景；已存在则复用。</item>
///   <item>音频始终走麦克风，不从 VTS / NDI 再采一路（避免双声）。</item>
///   <item>VTS 只走公开 WebSocket API；模型导入到 VTS 目录不做静默复制，
///         找不到就告诉用户 VTS 的模型目录在哪（由 <c>VTSFolderInfoRequest</c> 读取）。</item>
///   <item>每一步都可失败并给出稳定错误码；失败不把「部分成功」显示成成功。</item>
/// </list>
/// </summary>
public sealed class Live2DSetupService : IAsyncDisposable
{
    private readonly ObsConnectionService _obs;
    private readonly VTubeStudioClient _vts = new();
    private readonly SemaphoreSlim _single = new(1, 1);

    /// <summary>默认场景名（与虚拟直播间模板的「形象位」不冲突：模板里是来源，这里是独立场景）。</summary>
    public const string DefaultSceneName = "虚拟形象";

    public Live2DSetupService(ObsConnectionService obs)
    {
        _obs = obs;
    }

    /// <summary>VTS 是否在运行（只看进程名，不读账号 / 不注入）。</summary>
    public static bool IsVtsRunning()
    {
        try
        {
            return Process.GetProcesses().Any(p =>
            {
                try { return Live2DCaptureCore.IsVtsProcess(p.ProcessName); }
                catch { return false; }
            });
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>体检：OBS 是否已连接、VTS 是否在跑、当前画布与首选采集方式。</summary>
    public async Task<Live2DEnvStatus> CheckEnvironmentAsync(CancellationToken ct)
    {
        var vts = IsVtsRunning();
        if (!_obs.IsConnected)
            return new Live2DEnvStatus(false, vts, 1920, 1080, Live2DCaptureKind.None, "live2d.capture.none");

        var (w, h) = await ReadCanvasAsync(ct).ConfigureAwait(false);
        var kinds = await ReadInputKindsAsync(ct).ConfigureAwait(false);
        var devices = await ReadCameraDevicesAsync(ct).ConfigureAwait(false);
        var choice = Live2DCaptureCore.Choose(kinds, devices);
        return new Live2DEnvStatus(true, vts, w, h, choice.Method, Live2DCaptureCore.UnavailableHintKey(choice));
    }

    /// <summary>
    /// 一键配置：把模型作为「虚拟形象」采集源放进 OBS，并按布局预设摆好位置。
    /// 不会修改用户已存在的场景集合，也不会替用户打开 VTS 的授权弹窗之外的任何按钮。
    /// </summary>
    public async Task<Live2DApplyResult> ApplyAsync(
        string modelJsonPath, string layoutId, IProgress<string>? progress, CancellationToken ct)
    {
        await _single.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var notes = new List<string>();
            progress?.Report(Strings.T("live2d.step.validate"));
            if (!Live2DModelCore.TryValidate(modelJsonPath, out var model, out var modelError))
                return new Live2DApplyResult(false, modelError, notes, DefaultSceneName);

            // 1) OBS 连接（先自检，不硬拉）
            if (!_obs.IsConnected)
            {
                progress?.Report(Strings.T("live2d.step.connect"));
                var ensure = await _obs.EnsureConnectedAsync(TimeSpan.FromSeconds(12), null, ct).ConfigureAwait(false);
                if (!ensure.Ok)
                    return new Live2DApplyResult(false, "L2D_OBS_NOT_CONNECTED", notes, DefaultSceneName);
            }

            // 2) 画布
            var (canvasW, canvasH) = await ReadCanvasAsync(ct).ConfigureAwait(false);

            // 3) 采集方式
            progress?.Report(Strings.T("live2d.step.capture"));
            var kinds = await ReadInputKindsAsync(ct).ConfigureAwait(false);
            var devices = await ReadCameraDevicesAsync(ct).ConfigureAwait(false);
            var choice = Live2DCaptureCore.Choose(kinds, devices);
            if (choice.Method == Live2DCaptureKind.None)
                return new Live2DApplyResult(false, "L2D_CAPTURE_UNAVAILABLE", notes, DefaultSceneName);

            notes.Add(Strings.T(Live2DCaptureCore.UnavailableHintKey(choice)));

            // 4) 场景（存在则复用）
            progress?.Report(Strings.T("live2d.step.scene"));
            await EnsureSceneAsync(DefaultSceneName, ct).ConfigureAwait(false);

            // 5) 采集源
            progress?.Report(Strings.T("live2d.step.source"));
            var (itemId, inputName, created) = await EnsureAvatarSourceAsync(
                DefaultSceneName, choice, ct).ConfigureAwait(false);
            if (itemId < 0)
                return new Live2DApplyResult(false, "L2D_CAPTURE_UNAVAILABLE", notes, DefaultSceneName);

            if (!created) notes.Add(Strings.T("live2d.note.reusedSource"));

            // 6) 布局
            progress?.Report(Strings.T("live2d.step.layout"));
            var tf = Live2DModelCore.ComputeTransform(layoutId, canvasW, canvasH);
            await _obs.RawRequestAsync("SetSceneItemTransform", new
            {
                sceneName = DefaultSceneName,
                sceneItemId = itemId,
                sceneItemTransform = new
                {
                    positionX = tf.PosX,
                    positionY = tf.PosY,
                    boundsType = tf.BoundsType,
                    boundsWidth = tf.Width,
                    boundsHeight = tf.Height,
                    // 5 = 左上对齐：位置按「左上角」算，和 ComputeTransform 的语义一致
                    alignment = 5
                }
            }, ct).ConfigureAwait(false);

            // 7) 麦克风（音频始终走麦克风，避免双声）
            progress?.Report(Strings.T("live2d.step.audio"));
            await EnsureMicrophoneAsync(ct).ConfigureAwait(false);

            // 8) 切到形象场景
            progress?.Report(Strings.T("live2d.step.switch"));
            await _obs.RawRequestAsync("SetCurrentProgramScene", new { sceneName = DefaultSceneName }, ct)
                .ConfigureAwait(false);

            notes.Add(Strings.T("live2d.note.captureMethod", inputName));
            notes.Add(Strings.T("live2d.note.model", model!.Name, model.ManifestHash.Length >= 8 ? model.ManifestHash[..8] : model.ManifestHash));
            return new Live2DApplyResult(true, null, notes, DefaultSceneName);
        }
        catch (OperationCanceledException)
        {
            return new Live2DApplyResult(false, "L2D_CANCELLED", Array.Empty<string>(), DefaultSceneName);
        }
        catch (Exception ex)
        {
            FileLogger.Error("Live2D", $"一键配置失败：{ex.Message}");
            return new Live2DApplyResult(false, "L2D_UNEXPECTED", new[] { ex.Message }, DefaultSceneName);
        }
        finally
        {
            _single.Release();
        }
    }

    /// <summary>
    /// 在 VTS 里加载同名模型（不复制模型文件）。返回 (是否成功, 错误码, 模型目录名)。
    /// token 为 null 时走首次授权；调用方负责把新 token 存进 secrets.dat。
    /// </summary>
    public async Task<(bool Ok, string ErrorCode, string? ModelsFolder)> LoadInVtsAsync(
        string modelName, string? token, Func<string, Task> saveToken, CancellationToken ct)
    {
        if (!IsVtsRunning())
            return (false, "L2D_NO_VTS", null);

        if (!_vts.IsConnected && !await _vts.ConnectAsync(VtsProtocolCore.DefaultPort, ct).ConfigureAwait(false))
            return (false, "L2D_VTS_NOT_RUNNING", null);

        var (authOk, newToken, authError) = await _vts.AuthenticateAsync(token, ct).ConfigureAwait(false);
        if (!authOk) return (false, authError, null);
        if (!string.IsNullOrWhiteSpace(newToken) && !string.Equals(newToken, token, StringComparison.Ordinal))
        {
            try { await saveToken(newToken!).ConfigureAwait(false); }
            catch (Exception ex) { FileLogger.Warn("Live2D", $"保存 VTS token 失败：{ex.Message}"); }
        }

        var folder = await _vts.GetModelsFolderAsync(ct).ConfigureAwait(false);
        var models = await _vts.GetModelsAsync(ct).ConfigureAwait(false);
        var match = VtsProtocolCore.FindModel(models, modelName);
        if (match is null)
            return (false, "L2D_MODEL_NOT_IN_VTS", folder);

        if (!await _vts.LoadModelAsync(match.ModelId, ct).ConfigureAwait(false))
            return (false, _vts.LastErrorCode.Length > 0 ? _vts.LastErrorCode : "L2D_MODEL_LOAD_FAILED", folder);

        return (true, "", folder);
    }

    // ---------------------------------------------------------------- OBS 辅助

    private async Task<(int Width, int Height)> ReadCanvasAsync(CancellationToken ct)
    {
        var r = await _obs.RawRequestAsync("GetVideoSettings", null, ct).ConfigureAwait(false);
        if (r.Ok && r.Data is JsonElement d)
        {
            var w = d.TryGetProperty("baseWidth", out var bw) && bw.TryGetInt32(out var vw) ? vw : 1920;
            var h = d.TryGetProperty("baseHeight", out var bh) && bh.TryGetInt32(out var vh) ? vh : 1080;
            return (Math.Clamp(w, 320, 7680), Math.Clamp(h, 240, 4320));
        }
        return (1920, 1080);
    }

    private async Task<HashSet<string>> ReadInputKindsAsync(CancellationToken ct)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var r = await _obs.RawRequestAsync("GetInputKindList", null, ct).ConfigureAwait(false);
        if (r.Ok && r.Data is JsonElement d && d.TryGetProperty("inputKinds", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in arr.EnumerateArray())
                if (e.ValueKind == JsonValueKind.String) set.Add(e.GetString()!);
        }
        return set;
    }

    /// <summary>从已有的 DirectShow 输入里读摄像头设备列表（没有 dshow 源时返回空）。</summary>
    private async Task<IReadOnlyList<string>> ReadCameraDevicesAsync(CancellationToken ct)
    {
        var devices = new List<string>();
        try
        {
            var list = await _obs.RawRequestAsync("GetInputList", new { inputKind = "dshow_input" }, ct).ConfigureAwait(false);
            if (!list.Ok || list.Data is not JsonElement ld || !ld.TryGetProperty("inputs", out var inputs)
                || inputs.ValueKind != JsonValueKind.Array) return devices;

            foreach (var input in inputs.EnumerateArray())
            {
                if (!input.TryGetProperty("inputName", out var nameEl) || nameEl.ValueKind != JsonValueKind.String) continue;
                var name = nameEl.GetString();
                if (string.IsNullOrWhiteSpace(name)) continue;

                var props = await _obs.RawRequestAsync("GetInputPropertiesListPropertyItems", new
                {
                    inputName = name,
                    propertyName = "video_device_id"
                }, ct).ConfigureAwait(false);

                if (!props.Ok || props.Data is not JsonElement pd
                    || !pd.TryGetProperty("propertyItems", out var items) || items.ValueKind != JsonValueKind.Array) continue;

                foreach (var item in items.EnumerateArray())
                {
                    var itemName = item.TryGetProperty("itemName", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(itemName) && Live2DCaptureCore.LooksLikeVtsCamera(itemName))
                        devices.Add(itemName!);
                }
                if (devices.Count > 0) break;
            }
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Live2D", $"读取摄像头设备失败（将只建空设备源）：{ex.Message}");
        }
        return devices;
    }

    private async Task EnsureSceneAsync(string sceneName, CancellationToken ct)
    {
        var list = await _obs.RawRequestAsync("GetSceneList", null, ct).ConfigureAwait(false);
        if (list.Ok && list.Data is JsonElement d && d.TryGetProperty("scenes", out var scenes) && scenes.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in scenes.EnumerateArray())
            {
                if (s.TryGetProperty("sceneName", out var n) && n.ValueKind == JsonValueKind.String
                    && string.Equals(n.GetString(), sceneName, StringComparison.Ordinal))
                    return;
            }
        }
        await _obs.RawRequestAsync("CreateScene", new { sceneName }, ct).ConfigureAwait(false);
    }

    private async Task<(int ItemId, string InputName, bool Created)> EnsureAvatarSourceAsync(
        string sceneName, Live2DCaptureChoice choice, CancellationToken ct)
    {
        var settings = Live2DCaptureCore.BuildSettings(choice);
        var create = await _obs.RawRequestAsync("CreateInput", new
        {
            sceneName,
            inputName = "虚拟形象",
            inputKind = choice.InputKind,
            inputSettings = settings,
            sceneItemEnabled = true
        }, ct).ConfigureAwait(false);

        int itemId = -1;
        string inputName = "虚拟形象";
        if (create.Ok && create.Data is JsonElement d)
        {
            if (d.TryGetProperty("sceneItemId", out var si) && si.TryGetInt32(out var id)) itemId = id;
            if (d.TryGetProperty("inputName", out var inn) && inn.ValueKind == JsonValueKind.String)
                inputName = inn.GetString() ?? inputName;
            return (itemId, inputName, true);
        }

        // 已存在同名来源：引用它（不覆盖用户的设置）
        var reuse = await _obs.RawRequestAsync("CreateSceneItem", new
        {
            sceneName,
            sourceName = inputName,
            sceneItemEnabled = true
        }, ct).ConfigureAwait(false);

        if (reuse.Ok && reuse.Data is JsonElement rd && rd.TryGetProperty("sceneItemId", out var rsi) && rsi.TryGetInt32(out var rid))
            itemId = rid;

        return (itemId, inputName, false);
    }

    private async Task EnsureMicrophoneAsync(CancellationToken ct)
    {
        try
        {
            await _obs.RawRequestAsync("CreateInput", new
            {
                sceneName = DefaultSceneName,
                inputName = "我的麦克风",
                inputKind = "wasapi_input_capture",
                inputSettings = new JsonObject(),
                sceneItemEnabled = true
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 已存在 / 设备不在：不影响形象采集，交给用户自己选设备
            FileLogger.Warn("Live2D", $"创建麦克风源失败（不影响形象采集）：{ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _vts.DisposeAsync().ConfigureAwait(false);
        _single.Dispose();
    }
}
