using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using OBS_Helper.Wpf.Models.Obs;
using OBS_Helper.Wpf.Models.ObsConfig;
using OBS_Helper.Wpf.Services.Obs;

namespace OBS_Helper.Wpf.Services.ObsConfig;

/// <summary>
/// 直播间场景模板：把内置模板一键落地到 OBS。
///
/// <list type="bullet">
///   <item><b>在线落地（ApplyAsync）</b>：已连 OBS 时走 obs-websocket，新建一个干净配置集合并切过去，
///         再逐场景 / 逐来源创建。跨场景复用的来源（麦克风 / 等待音乐）<c>shared</c> 标记走 CreateSceneItem。</item>
///   <item><b>离线降级（ExportToObsAsync）</b>：未连接时生成标准 OBS 场景集合 JSON，落进配置目录的
///         <c>basic/scenes/</c>（文件名用 ASCII slug，显示名靠 JSON 内的 <c>name</c>），或导出到用户指定目录。</item>
/// </list>
/// 所有设备 / 文件 / URL 在模板里留空，落地后汇总成「还需你手动补齐」清单（Placeholder）。
/// </summary>
public sealed class SceneTemplateService
{
    private const string TemplatesResource = "OBS_Helper.Wpf.Assets.scene_templates.json";

    private readonly ObsConnectionService _obs;
    private readonly ObsPathService _paths;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private List<SceneTemplate>? _cache;
    private string? _loadError;

    public SceneTemplateService(ObsConnectionService obs, ObsPathService paths)
    {
        _obs = obs;
        _paths = paths;
    }

    /// <summary>数据加载失败时的错误信息（供 UI 展示）。</summary>
    public string? LoadError => _loadError;

    public async Task<IReadOnlyList<SceneTemplate>> LoadAsync()
    {
        if (_cache is not null) return _cache;
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_cache is not null) return _cache;
            _cache = await Task.Run(LoadEmbedded).ConfigureAwait(false);
            return _cache;
        }
        finally
        {
            _lock.Release();
        }
    }

    private List<SceneTemplate> LoadEmbedded()
    {
        try
        {
            var raw = ReadResource(TemplatesResource);
            if (raw is null) { _loadError = Errors.ErrorCodes.ResourceMissing; return new List<SceneTemplate>(); }
            var list = JsonSerializer.Deserialize<List<SceneTemplate>>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (list is null) { _loadError = Errors.ErrorCodes.DataParseFailed; return new List<SceneTemplate>(); }
            return list;
        }
        catch (JsonException) { _loadError = Errors.ErrorCodes.DataParseFailed; return new List<SceneTemplate>(); }
        catch (Exception) { _loadError = Errors.ErrorCodes.DataLoadFailed; return new List<SceneTemplate>(); }
    }

    // ------------------------------------------------------------ 在线落地

    public async Task<ApplyResult> ApplyAsync(string templateId, bool applyCanvas, CancellationToken ct, IProgress<string> p)
    {
        var tpl = (await LoadAsync()).FirstOrDefault(t => t.Id == templateId);
        if (tpl is null)
            return new ApplyResult(false, 0, 0, Array.Empty<string>(), "未找到该模板。");

        if (!_obs.IsConnected)
            return new ApplyResult(false, 0, 0, Array.Empty<string>(), "未连接到 OBS，无法在线落地。请先在控制台连接，或使用「导出场景集合 JSON」。");

        var transitionNotes = new List<string>();
        try
        {
            p.Report("正在读取 OBS 可用来源类型…");
            var available = await LoadAvailableInputKindsAsync(ct);

            // 读取可用过渡，落地时把模板默认过渡设为当前过渡
            var transitionNames = await LoadTransitionNamesAsync(ct);

            p.Report("正在新建模板专属配置集合…");
            await EnsureSceneCollectionAsync(ct, $"模板 · {tpl.Title}");

            if (applyCanvas)
                await ApplyCanvasAsync(tpl, p, ct);

            await ApplyDefaultTransitionAsync(tpl, transitionNames, transitionNotes, ct);

            var (created, skipped, placeholders) = await CreateAllScenesAsync(tpl, available, transitionNames, transitionNotes, p, ct);

            p.Report("正在切换到主场景…");
            await SwitchToFirstSceneAsync(tpl, ct);

            p.Report("正在刷新状态…");
            await _obs.RefreshAllAsync();

            // 快捷键：obs-websocket 无设置场景快捷键的 API，落地后提示用户
            AppendHotkeyHints(tpl, transitionNotes);

            if (transitionNotes.Count > 0)
                placeholders.InsertRange(0, transitionNotes);

            return new ApplyResult(true, created, skipped, placeholders,
                skipped > 0 ? $"已落地，但有 {skipped} 个来源未能创建（多为本地设备 / 文件需在 OBS 中补齐）。" : null);
        }
        catch (OperationCanceledException)
        {
            return new ApplyResult(false, 0, 0, Array.Empty<string>(), "操作已取消。");
        }
        catch (Exception ex)
        {
            return new ApplyResult(false, 0, 0, Array.Empty<string>(), $"模板落地失败：{ex.Message}");
        }
    }

    /// <summary>读取 OBS 可用的输入来源类型集合（大小写不敏感）。</summary>
    private async Task<HashSet<string>> LoadAvailableInputKindsAsync(CancellationToken ct)
    {
        var kindsResult = await _obs.RawRequestAsync("GetInputKindList", null, ct);
        var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (kindsResult.Ok && kindsResult.Data is JsonElement kd && kd.TryGetProperty("inputKinds", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var e in arr.EnumerateArray())
                if (e.ValueKind == JsonValueKind.String) available.Add(e.GetString()!);
        return available;
    }

    /// <summary>按模板画布设置 OBS 视频分辨率与帧率。</summary>
    private async Task ApplyCanvasAsync(SceneTemplate tpl, IProgress<string> p, CancellationToken ct)
    {
        p.Report("正在设置画布分辨率…");
        var canvas = tpl.Canvas;
        await _obs.RawRequestAsync("SetVideoSettings", new
        {
            baseWidth = canvas.BaseWidth,
            baseHeight = canvas.BaseHeight,
            outputWidth = canvas.OutputWidth,
            outputHeight = canvas.OutputHeight,
            fpsNumerator = canvas.FpsNumerator,
            fpsDenominator = canvas.FpsDenominator
        }, ct);
    }

    /// <summary>把模板默认过渡设为 OBS 当前过渡（不可用时记录提示并保持 OBS 原过渡）。</summary>
    private async Task ApplyDefaultTransitionAsync(SceneTemplate tpl, List<string> transitionNames, List<string> notes, CancellationToken ct)
    {
        if (transitionNames.Count == 0) return;

        // 当前过渡 + 时长
        var cur = PickTransitionName(tpl.Transition, transitionNames);
        if (cur is null)
        {
            notes.Add($"模板默认过渡「{tpl.Transition}」在 OBS 中不可用，已保持 OBS 原过渡。");
            return;
        }

        await _obs.RawRequestAsync("SetCurrentSceneTransition", new { transitionName = cur }, ct);
        await _obs.RawRequestAsync("SetCurrentSceneTransitionDuration", new { transitionDuration = tpl.TransitionDurationMs }, ct);
    }

    /// <summary>逐场景创建场景与来源，返回 (创建场景数, 跳过数, 占位提示列表)。</summary>
    private async Task<(int Created, int Skipped, List<string> Placeholders)> CreateAllScenesAsync(
        SceneTemplate tpl, HashSet<string> available, List<string> transitionNames, List<string> notes,
        IProgress<string> p, CancellationToken ct)
    {
        int created = 0, skipped = 0;
        var placeholders = new List<string>();
        var createdInputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var scene in tpl.Scenes)
        {
            p.Report($"正在创建场景「{scene.Name}」…");
            var cs = await _obs.RawRequestAsync("CreateScene", new { sceneName = scene.Name }, ct);
            if (!cs.Ok) { skipped++; continue; }
            created++;

            await ApplySceneTransitionOverrideAsync(scene, tpl, transitionNames, notes, ct);

            foreach (var src in scene.Sources.OrderBy(s => s.ZOrder))
            {
                try
                {
                    await CreateOneSourceAsync(scene.Name, src, available, createdInputs, placeholders, ct);
                }
                catch (Exception ex)
                {
                    skipped++;
                    placeholders.Add($"{scene.Name} / {src.Name}：创建失败（{ex.Message}），已跳过。");
                }
            }
        }

        return (created, skipped, placeholders);
    }

    /// <summary>场景级过渡覆盖（仅当该场景单独设置了过渡 / 时长时下发）。</summary>
    private async Task ApplySceneTransitionOverrideAsync(TemplateScene scene, SceneTemplate tpl, List<string> transitionNames, List<string> notes, CancellationToken ct)
    {
        if (transitionNames.Count == 0 || (scene.Transition is null && scene.TransitionDurationMs is null)) return;

        var ovName = PickTransitionName(scene.Transition ?? tpl.Transition, transitionNames);
        var ovDur = scene.TransitionDurationMs ?? tpl.TransitionDurationMs;
        var ovOk = await _obs.RawRequestAsync("SetSceneTransitionOverride", new
        {
            sceneName = scene.Name,
            transitionName = ovName ?? (object?)null,
            transitionDuration = ovDur
        }, ct);

        if (!ovOk.Ok && ovName is null)
            notes.Add($"场景「{scene.Name}」的过渡覆盖未生效（{Describe(ovOk)}）。");
    }

    /// <summary>落地后切到模板的第一个场景作为主场景。</summary>
    private async Task SwitchToFirstSceneAsync(SceneTemplate tpl, CancellationToken ct)
    {
        if (tpl.Scenes.Count > 0)
            await _obs.RawRequestAsync("SetCurrentProgramScene", new { sceneName = tpl.Scenes[0].Name }, ct);
    }

    /// <summary>obs-websocket 无设置场景快捷键的 API，落地后把「需手动绑定」提示写入 notes。</summary>
    private static void AppendHotkeyHints(SceneTemplate tpl, List<string> notes)
    {
        var hotkeyScenes = tpl.Scenes.Where(s => !string.IsNullOrWhiteSpace(s.Hotkey)).ToList();
        if (hotkeyScenes.Count == 0) return;
        notes.Add("场景切换快捷键（" + string.Join(" / ", hotkeyScenes.Select(s => $"{s.Hotkey} → {s.Name}")) + "）需在 OBS 中手动绑定，或改用「导出场景集合 JSON」方式导入后自动生效。");
    }

    /// <summary>创建一个来源：优先复用跨场景共享输入，否则新建；随后应用变换、层级与显隐，并追加占位提示。</summary>
    private async Task CreateOneSourceAsync(string sceneName, TemplateSource src, HashSet<string> available,
        Dictionary<string, string> createdInputs, List<string> placeholders, CancellationToken ct)
    {
        var (itemId, inputName) = src.Shared && createdInputs.TryGetValue(src.Name, out var existingInput)
            ? await ReuseSharedSourceAsync(sceneName, src, existingInput, ct)
            : await CreateSourceWithFallbackAsync(sceneName, src, available, createdInputs, placeholders, ct);

        if (itemId < 0) return;

        // 变换（带层级）
        if (src.Transform is not null)
            await ApplyTransformAsync(sceneName, itemId, src.Transform, ct);

        // 层级：OBS index 0 = 最上，模板 zOrder 0 = 最底 → index = count-1-zOrder
        await ApplyZOrderAsync(sceneName, itemId, src.ZOrder, ct);

        if (!src.Enabled)
            await _obs.RawRequestAsync("SetSceneItemEnabled", new { sceneName, sceneItemId = itemId, sceneItemEnabled = false }, ct);

        // 占位提示
        if (src.Placeholder is not null)
            placeholders.Add($"{sceneName} / {src.Name}：{src.Placeholder.Hint}");
    }

    /// <summary>复用已创建的输入：在同一场景里用 CreateSceneItem 引用它。</summary>
    private async Task<(int ItemId, string InputName)> ReuseSharedSourceAsync(string sceneName, TemplateSource src, string existingInput, CancellationToken ct)
    {
        var ci = await _obs.RawRequestAsync("CreateSceneItem", new { sceneName, sourceName = existingInput, sceneItemEnabled = src.Enabled }, ct);
        if (!ci.Ok || ci.Data is not JsonElement cid || !cid.TryGetProperty("sceneItemId", out var siid) || siid.ValueKind != JsonValueKind.Number)
            throw new InvalidOperationException("复用来源失败：" + Describe(ci));
        return (siid.GetInt32(), existingInput);
    }

    /// <summary>
    /// 新建输入源：类型不可用直接抛错（外层记占位）；CreateInput 失败（如 601 已存在）时
    /// 降级用 CreateSceneItem 引用同名来源；成功则记录 inputName 供跨场景复用。
    /// </summary>
    private async Task<(int ItemId, string InputName)> CreateSourceWithFallbackAsync(string sceneName, TemplateSource src, HashSet<string> available,
        Dictionary<string, string> createdInputs, List<string> placeholders, CancellationToken ct)
    {
        var kind = PickKind(src, available);
        if (kind is null)
        {
            placeholders.Add($"{sceneName} / {src.Name}：来源类型不可用，需在 OBS 中手动添加。");
            throw new InvalidOperationException("来源类型不可用");
        }

        var ci = await _obs.RawRequestAsync("CreateInput", new
        {
            sceneName,
            inputName = src.Name,
            inputKind = kind,
            inputSettings = src.Settings ?? new JsonObject(),
            sceneItemEnabled = src.Enabled
        }, ct);

        if (ci.Ok && ci.Data is JsonElement cid)
        {
            var inputName = cid.TryGetProperty("inputName", out var inn) && inn.ValueKind == JsonValueKind.String ? inn.GetString()! : src.Name;
            var itemId = cid.TryGetProperty("sceneItemId", out var siidn) && siidn.ValueKind == JsonValueKind.Number ? siidn.GetInt32() : -1;
            if (!createdInputs.ContainsKey(src.Name)) createdInputs[src.Name] = inputName;
            return (itemId, inputName);
        }

        // 601 ResourceAlreadyExists 等情况：尝试用 CreateSceneItem 引用同名来源
        var fallback = await _obs.RawRequestAsync("CreateSceneItem", new { sceneName, sourceName = src.Name, sceneItemEnabled = src.Enabled }, ct);
        if (fallback.Ok && fallback.Data is JsonElement fcid && fcid.TryGetProperty("sceneItemId", out var fsiid) && fsiid.ValueKind == JsonValueKind.Number)
            return (fsiid.GetInt32(), src.Name);

        placeholders.Add($"{sceneName} / {src.Name}：{Describe(ci)}");
        throw new InvalidOperationException("创建来源失败");
    }

    /// <summary>应用来源层级：OBS index 0 = 最上，模板 zOrder 0 = 最底，故 index = count-1-zOrder。</summary>
    private async Task ApplyZOrderAsync(string sceneName, int itemId, int zOrder, CancellationToken ct)
    {
        var ordered = await _obs.RawRequestAsync("GetSceneItemList", new { sceneName }, ct);
        int count = 0;
        if (ordered.Ok && ordered.Data is JsonElement od && od.TryGetProperty("sceneItems", out var sal) && sal.ValueKind == JsonValueKind.Array)
            count = sal.GetArrayLength();
        if (count > 0)
            await _obs.RawRequestAsync("SetSceneItemIndex", new { sceneName, sceneItemId = itemId, sceneItemIndex = Math.Max(0, count - 1 - zOrder) }, ct);
    }

    /// <summary>把变换应用到某个场景元素。boundsType=NONE 时不带 bounds 尺寸；用 bounds 时不带 scale。</summary>
    private async Task ApplyTransformAsync(string sceneName, int itemId, TransformSpec t, CancellationToken ct)
    {
        var tf = new JsonObject
        {
            ["positionX"] = t.PosX ?? 0,
            ["positionY"] = t.PosY ?? 0,
            ["alignment"] = t.Alignment ?? 0,
            ["crop"] = new JsonObject { ["top"] = 0, ["bottom"] = 0, ["left"] = 0, ["right"] = 0 }
        };

        var boundsNone = string.Equals(t.BoundsType, "OBS_BOUNDS_NONE", StringComparison.OrdinalIgnoreCase)
                         || string.IsNullOrEmpty(t.BoundsType);
        if (boundsNone)
        {
            // 只发 scale，不带 bounds
            tf["scaleX"] = t.ScaleX ?? 1;
            tf["scaleY"] = t.ScaleY ?? 1;
        }
        else
        {
            // 用 bounds 驱动，不重复发 scale
            tf["boundsType"] = t.BoundsType!;
            tf["boundsWidth"] = t.BoundsWidth ?? 0;
            tf["boundsHeight"] = t.BoundsHeight ?? 0;
        }

        await _obs.RawRequestAsync("SetSceneItemTransform", new { sceneName, sceneItemId = itemId, sceneItemTransform = tf }, ct);
    }

    // ------------------------------------------------------------ 离线降级

    /// <summary>未连接 OBS 时，把模板生成为标准 OBS 场景集合 JSON 写入目录（默认写进 OBS 配置目录的 basic/scenes）。</summary>
    public async Task<string> ExportToObsAsync(string templateId, string? outDir, CancellationToken ct)
    {
        var tpl = (await LoadAsync()).FirstOrDefault(t => t.Id == templateId);
        if (tpl is null) throw new InvalidOperationException("未找到该模板。");

        var dir = ResolveExportDir(outDir);
        Directory.CreateDirectory(dir);

        var collectionName = $"模板 · {tpl.Title}";
        var json = BuildSceneCollectionJson(tpl, collectionName);
        var text = json.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        // 自校验：能解析 + 每个 source_uuid 都能在 sources 找到
        SelfCheckCollection(json);

        var stamp = $"{DateTime.Now:yyyyMMdd}";
        var fileName = $"obshelper_{Slugify(tpl.Id)}_{stamp}.json";
        var path = Path.Combine(dir, fileName);
        for (int n = 2; File.Exists(path); n++)
            path = Path.Combine(dir, $"obshelper_{Slugify(tpl.Id)}_{stamp}_{n}.json");

        await File.WriteAllTextAsync(path, text, new UTF8Encoding(false), ct);
        return path;
    }

    private string ResolveExportDir(string? outDir)
    {
        if (!string.IsNullOrWhiteSpace(outDir) && Directory.Exists(outDir)) return outDir;
        var loc = _paths.LocateAsync().GetAwaiter().GetResult();
        if (loc.Exists)
        {
            var scenes = Path.Combine(loc.ConfigDir, "basic", "scenes");
            if (Directory.Exists(scenes)) return scenes;
        }
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        return string.IsNullOrEmpty(desktop) ? Path.GetTempPath() : desktop;
    }

    // ------------------------------------------------------------ 场景集合 JSON 生成（标准 OBS 格式，version 2）

    /// <summary>
    /// 生成标准 OBS 场景集合 JSON（一个集合含模板全部场景）。
    /// 采用 OBS 28+ 的 <c>version: 2</c> 格式：场景本身就是 sources 里的一个 source（id=scene），
    /// 场景内容放在该 source 的 <c>settings.items</c>，每个 item 通过 <c>source_uuid</c> 引用输入源；
    /// 顶层带 <c>current_transition / transition_duration / quick_transitions</c> 过渡设置，
    /// 场景 source 的 <c>hotkeys</c> 写入切换快捷键（OBSBasic.SelectScene）与显隐快捷键。
    /// </summary>
    private static JsonObject BuildSceneCollectionJson(SceneTemplate tpl, string collectionName)
    {
        var sources = new JsonArray();
        var sceneOrder = new JsonArray();
        var uuidByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var canvasW = Math.Max(1, tpl.Canvas.BaseWidth);
        var canvasH = Math.Max(1, tpl.Canvas.BaseHeight);

        // 先收集输入源，再生成场景 source（场景依赖 source_uuid）
        foreach (var scene in tpl.Scenes)
        {
            sceneOrder.Add(new JsonObject { ["name"] = scene.Name });
            foreach (var src in scene.Sources.OrderBy(s => s.ZOrder))
                if (!uuidByName.ContainsKey(src.Name))
                    AddInputSource(src, sources, uuidByName);
        }

        // 场景 source（id=scene），items 引用上面的 source_uuid
        int sceneItemId = 1;
        foreach (var scene in tpl.Scenes)
            AddSceneSource(scene, uuidByName, sources, ref sceneItemId, canvasW, canvasH);

        return BuildCollectionRoot(tpl, collectionName, sources, sceneOrder, canvasW, canvasH);
    }

    /// <summary>为模板输入源分配 uuid 并生成标准输入 source（同名输入全集合复用同一 uuid）。</summary>
    private static void AddInputSource(TemplateSource src, JsonArray sources, Dictionary<string, string> uuidByName)
    {
        var srcUuid = Guid.NewGuid().ToString("D").ToLowerInvariant();
        uuidByName[src.Name] = srcUuid;

        var (id, versionedId) = ResolveSourceId(src.InputKind);
        sources.Add(BuildSourceJson(src, id, versionedId, srcUuid, IsAudioInput(src.InputKind)));
    }

    /// <summary>构建一个场景 source（id=scene）：items 按 zOrder 引用输入源 uuid，并写场景级过渡覆盖与快捷键。</summary>
    private static void AddSceneSource(TemplateScene scene, Dictionary<string, string> uuidByName, JsonArray sources, ref int sceneItemId, int canvasW, int canvasH)
    {
        var sceneUuid = Guid.NewGuid().ToString("D").ToLowerInvariant();
        var items = new JsonArray();
        var itemIds = new List<int>();

        foreach (var src in scene.Sources.OrderBy(s => s.ZOrder))
        {
            items.Add(BuildFileItem(src, uuidByName[src.Name], sceneItemId, canvasW, canvasH));
            itemIds.Add(sceneItemId);
            sceneItemId++;
        }

        // 场景级过渡覆盖：transition_override + transition_override_duration
        var sceneSettings = new JsonObject
        {
            ["id_counter"] = sceneItemId,
            ["custom_size"] = false,
            ["items"] = items
        };
        if (!string.IsNullOrWhiteSpace(scene.Transition))
            sceneSettings["transition_override"] = scene.Transition;
        if (scene.TransitionDurationMs is > 0)
            sceneSettings["transition_override_duration"] = scene.TransitionDurationMs.Value;

        // OBS 默认主画布 uuid（libobs 固定值）
        const string canvasUuid = "6c69626f-6273-4c00-9d88-c5136d61696e";

        sources.Add(new JsonObject
        {
            ["prev_ver"] = 537001985,
            ["name"] = scene.Name,
            ["uuid"] = sceneUuid,
            ["id"] = "scene",
            ["versioned_id"] = "scene",
            ["settings"] = sceneSettings,
            ["mixers"] = 0,
            ["sync"] = 0,
            ["flags"] = 0,
            ["volume"] = 1.0,
            ["balance"] = 0.5,
            ["enabled"] = true,
            ["muted"] = false,
            ["push-to-mute"] = false,
            ["push-to-mute-delay"] = 0,
            ["push-to-talk"] = false,
            ["push-to-talk-delay"] = 0,
            ["hotkeys"] = BuildSceneHotkeys(scene, itemIds),
            ["deinterlace_mode"] = 0,
            ["deinterlace_field_order"] = 0,
            ["monitoring_type"] = 0,
            ["canvas_uuid"] = canvasUuid,
            ["private_settings"] = new JsonObject()
        });
    }

    /// <summary>场景快捷键：切场景（SelectScene）与每个 item 的显/隐快捷键占位。</summary>
    private static JsonObject BuildSceneHotkeys(TemplateScene scene, List<int> itemIds)
    {
        var hotkeys = new JsonObject
        {
            ["OBSBasic.SelectScene"] = BuildHotkeyBindings(scene.Hotkey)
        };
        foreach (var id in itemIds)
        {
            hotkeys[$"libobs.show_scene_item.{id}"] = new JsonArray();
            hotkeys[$"libobs.hide_scene_item.{id}"] = new JsonArray();
        }
        return hotkeys;
    }

    /// <summary>组装场景集合根对象（过渡 / 模块 / 分辨率等集合级设置）。</summary>
    private static JsonObject BuildCollectionRoot(SceneTemplate tpl, string collectionName, JsonArray sources, JsonArray sceneOrder, int canvasW, int canvasH)
    {
        return new JsonObject
        {
            ["name"] = collectionName,
            ["sources"] = sources,
            ["groups"] = new JsonArray(),
            ["scene_order"] = sceneOrder,
            ["current_scene"] = tpl.Scenes.Count > 0 ? tpl.Scenes[0].Name : "",
            ["current_program_scene"] = tpl.Scenes.Count > 0 ? tpl.Scenes[0].Name : "",
            ["canvases"] = new JsonArray(),
            ["current_transition"] = string.IsNullOrWhiteSpace(tpl.Transition) ? "淡入淡出" : tpl.Transition,
            ["transition_duration"] = tpl.TransitionDurationMs > 0 ? tpl.TransitionDurationMs : 300,
            ["transitions"] = new JsonArray(),
            ["quick_transitions"] = BuildQuickTransitions(),
            ["saved_projectors"] = new JsonArray(),
            ["preview_locked"] = false,
            ["scaling_enabled"] = false,
            ["scaling_level"] = -19,
            ["scaling_off_x"] = 0.0,
            ["scaling_off_y"] = 0.0,
            ["virtual-camera"] = new JsonObject { ["type2"] = 3 },
            ["modules"] = new JsonObject
            {
                ["output-timer"] = new JsonObject
                {
                    ["streamTimerHours"] = 0, ["streamTimerMinutes"] = 0, ["streamTimerSeconds"] = 0,
                    ["recordTimerHours"] = 0, ["recordTimerMinutes"] = 0, ["recordTimerSeconds"] = 0,
                    ["autoStartStreamTimer"] = false, ["autoStartRecordTimer"] = false, ["pauseRecordTimer"] = false
                },
                ["auto-scene-switcher"] = new JsonObject
                {
                    ["interval"] = 300, ["non_matching_scene"] = "", ["switch_if_not_matching"] = false,
                    ["active"] = false, ["switches"] = new JsonArray()
                },
                ["captions"] = new JsonObject { ["source"] = "", ["enabled"] = false, ["lang_id"] = 2052, ["provider"] = "mssapi" }
            },
            ["resolution"] = new JsonObject { ["x"] = canvasW, ["y"] = canvasH },
            ["version"] = 2
        };
    }

    /// <summary>构建标准输入源 source 对象（含音频混音等 OBS 必需字段）。</summary>
    private static JsonObject BuildSourceJson(TemplateSource src, string id, string versionedId, string uuid, bool isAudio)
    {
        var obj = new JsonObject
        {
            ["prev_ver"] = 537001985,
            ["name"] = src.Name,
            ["uuid"] = uuid,
            ["id"] = id,
            ["versioned_id"] = versionedId,
            ["settings"] = src.Settings ?? new JsonObject(),
            ["mixers"] = isAudio ? 255 : 0,
            ["sync"] = 0,
            ["flags"] = 0,
            ["volume"] = 1.0,
            ["balance"] = 0.5,
            ["enabled"] = true,
            ["muted"] = false,
            ["push-to-mute"] = false,
            ["push-to-mute-delay"] = 0,
            ["push-to-talk"] = false,
            ["push-to-talk-delay"] = 0,
            ["hotkeys"] = isAudio ? BuildAudioHotkeys() : new JsonObject(),
            ["deinterlace_mode"] = 0,
            ["deinterlace_field_order"] = 0,
            ["monitoring_type"] = 0,
            ["private_settings"] = new JsonObject()
        };
        return obj;
    }

    /// <summary>构建场景内 item（标准 OBS 字段，transform 用 pos/scale/bounds 驱动）。</summary>
    private static JsonObject BuildFileItem(TemplateSource src, string srcUuid, int itemId, int canvasW, int canvasH)
    {
        var t = src.Transform;
        var boundsNone = t is null || string.IsNullOrEmpty(t.BoundsType) ||
                         string.Equals(t.BoundsType, "OBS_BOUNDS_NONE", StringComparison.OrdinalIgnoreCase);
        var align = t?.Alignment ?? 5;

        return new JsonObject
        {
            ["name"] = src.Name,
            ["source_uuid"] = srcUuid,
            ["visible"] = src.Enabled,
            ["locked"] = false,
            ["rot"] = 0.0,
            ["scale_ref"] = new JsonObject { ["x"] = canvasW, ["y"] = canvasH },
            ["align"] = align,
            ["bounds_type"] = boundsNone ? 0 : BoundsTypeToNumber(t!.BoundsType!),
            ["bounds_align"] = 0,
            ["bounds_crop"] = false,
            ["crop_left"] = 0,
            ["crop_top"] = 0,
            ["crop_right"] = 0,
            ["crop_bottom"] = 0,
            ["id"] = itemId,
            ["group_item_backup"] = false,
            ["pos"] = new JsonObject { ["x"] = t?.PosX ?? 0, ["y"] = t?.PosY ?? 0 },
            ["pos_rel"] = new JsonObject { ["x"] = (t?.PosX ?? 0) / (double)canvasW, ["y"] = (t?.PosY ?? 0) / (double)canvasH },
            ["scale"] = new JsonObject { ["x"] = t?.ScaleX ?? 1, ["y"] = t?.ScaleY ?? 1 },
            ["scale_rel"] = new JsonObject { ["x"] = t?.ScaleX ?? 1, ["y"] = t?.ScaleY ?? 1 },
            ["bounds"] = boundsNone
                ? new JsonObject { ["x"] = 0.0, ["y"] = 0.0 }
                : new JsonObject { ["x"] = t!.BoundsWidth ?? 0, ["y"] = t.BoundsHeight ?? 0 },
            ["bounds_rel"] = new JsonObject { ["x"] = 0.0, ["y"] = 0.0 },
            ["scale_filter"] = "disable",
            ["blend_method"] = "default",
            ["blend_type"] = "normal",
            ["show_transition"] = new JsonObject { ["duration"] = 0 },
            ["hide_transition"] = new JsonObject { ["duration"] = 0 },
            ["private_settings"] = new JsonObject()
        };
    }

    /// <summary>把模板快捷键（如「Ctrl+1」）解析成 OBS hotkey 绑定数组；空则返回空数组。</summary>
    private static JsonArray BuildHotkeyBindings(string? hotkey)
    {
        var arr = new JsonArray();
        if (string.IsNullOrWhiteSpace(hotkey)) return arr;
        var binding = ParseHotkey(hotkey);
        if (binding is not null) arr.Add(binding);
        return arr;
    }

    /// <summary>解析「Ctrl+Shift+1」风格的快捷键为 OBS hotkey 对象（key + modifiers）。</summary>
    private static JsonObject? ParseHotkey(string spec)
    {
        var parts = spec.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return null;

        var shift = false; var alt = false; var control = false; var command = false;
        var keyPart = "";
        foreach (var p in parts)
        {
            if (string.Equals(p, "Ctrl", StringComparison.OrdinalIgnoreCase) || string.Equals(p, "Control", StringComparison.OrdinalIgnoreCase)) control = true;
            else if (string.Equals(p, "Shift", StringComparison.OrdinalIgnoreCase)) shift = true;
            else if (string.Equals(p, "Alt", StringComparison.OrdinalIgnoreCase)) alt = true;
            else if (string.Equals(p, "Win", StringComparison.OrdinalIgnoreCase) || string.Equals(p, "Cmd", StringComparison.OrdinalIgnoreCase) || string.Equals(p, "Meta", StringComparison.OrdinalIgnoreCase)) command = true;
            else keyPart = p;
        }
        var key = MapObsKey(keyPart);
        if (key is null) return null;

        return new JsonObject
        {
            ["key"] = key,
            ["modifiers"] = new JsonObject
            {
                ["shift"] = shift,
                ["alt"] = alt,
                ["control"] = control,
                ["command"] = command
            }
        };
    }

    /// <summary>把「1 / A / F5」等映射为 OBS 键名（OBS_KEY_1 / OBS_KEY_A / OBS_KEY_F5）。</summary>
    private static string? MapObsKey(string part)
    {
        if (part.Length == 1 && char.IsLetter(part[0]))
            return $"OBS_KEY_{char.ToUpperInvariant(part[0])}";
        if (part.Length == 1 && char.IsDigit(part[0]))
            return $"OBS_KEY_{part[0]}";
        if (part.StartsWith("F", StringComparison.OrdinalIgnoreCase) && part.Length > 1 && int.TryParse(part[1..], out var fn) && fn is >= 1 and <= 24)
            return $"OBS_KEY_F{fn}";
        return part.ToUpperInvariant() switch
        {
            "SPACE" => "OBS_KEY_SPACE",
            "ENTER" or "RETURN" => "OBS_KEY_RETURN",
            "TAB" => "OBS_KEY_TAB",
            "ESC" or "ESCAPE" => "OBS_KEY_ESCAPE",
            "BACKSPACE" => "OBS_KEY_BACKSPACE",
            "DELETE" => "OBS_KEY_DELETE",
            "HOME" => "OBS_KEY_HOME",
            "END" => "OBS_KEY_END",
            "PAGEUP" => "OBS_KEY_PAGEUP",
            "PAGEDOWN" => "OBS_KEY_PAGEDOWN",
            "UP" => "OBS_KEY_UP",
            "DOWN" => "OBS_KEY_DOWN",
            "LEFT" => "OBS_KEY_LEFT",
            "RIGHT" => "OBS_KEY_RIGHT",
            "INSERT" => "OBS_KEY_INSERT",
            "PRINTSCREEN" => "OBS_KEY_PRINTSCREEN",
            "PAUSE" => "OBS_KEY_PAUSE",
            _ => null
        };
    }

    /// <summary>音频源的静音 / 按键说话 hotkeys（空绑定占位）。</summary>
    private static JsonObject BuildAudioHotkeys() => new()
    {
        ["libobs.mute"] = new JsonArray(),
        ["libobs.unmute"] = new JsonArray(),
        ["libobs.push-to-mute"] = new JsonArray(),
        ["libobs.push-to-talk"] = new JsonArray()
    };

    /// <summary>标准快捷过渡三件套（直接切换 / 淡入淡出 / 淡入淡出到黑）。</summary>
    private static JsonArray BuildQuickTransitions()
    {
        var arr = new JsonArray();
        arr.Add(new JsonObject { ["name"] = "直接切换", ["duration"] = 300, ["hotkeys"] = new JsonArray(), ["id"] = 1, ["fade_to_black"] = false });
        arr.Add(new JsonObject { ["name"] = "淡入淡出", ["duration"] = 300, ["hotkeys"] = new JsonArray(), ["id"] = 2, ["fade_to_black"] = false });
        arr.Add(new JsonObject { ["name"] = "淡入淡出", ["duration"] = 300, ["hotkeys"] = new JsonArray(), ["id"] = 3, ["fade_to_black"] = true });
        return arr;
    }

    private static bool IsAudioInput(string kind) => kind switch
    {
        "wasapi_input_capture" or "wasapi_output_capture" or "coreaudio_input_capture" or "coreaudio_output_capture"
            or "pulse_input_capture" or "pulse_output_capture" or "ffmpeg_source" or "vlc_source" => true,
        _ => false
    };

    /// <summary>反推 source id：text_gdiplus_v3 → id=text_gdiplus, versioned_id=text_gdiplus_v3。</summary>
    private static (string id, string versioned) ResolveSourceId(string kind)
    {
        if (string.IsNullOrWhiteSpace(kind)) return ("unknown", "unknown");
        var m = Regex.Match(kind, @"^(.*?)_v(\d+)$");
        return m.Success ? (m.Groups[1].Value, kind) : (kind, kind);
    }

    private static int BoundsTypeToNumber(string bounds)
        => bounds switch
        {
            "OBS_BOUNDS_STRETCH" => 1,
            "OBS_BOUNDS_SCALE_INNER" => 2,
            "OBS_BOUNDS_SCALE_OUTER" => 3,
            "OBS_BOUNDS_TO_WIDTH" => 4,
            "OBS_BOUNDS_TO_HEIGHT" => 5,
            "OBS_BOUNDS_MAX_ONLY" => 6,
            _ => 0
        };

    /// <summary>离线 JSON 自校验：能解析，且每个场景 item 的 source_uuid 都能在 sources 中找到对应 uuid。</summary>
    private static void SelfCheckCollection(JsonObject root)
    {
        using var doc = JsonDocument.Parse(root.ToJsonString());
        var uuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (doc.RootElement.TryGetProperty("sources", out var ss) && ss.ValueKind == JsonValueKind.Array)
            foreach (var s in ss.EnumerateArray())
                if (s.TryGetProperty("uuid", out var u) && u.ValueKind == JsonValueKind.String) uuids.Add(u.GetString() ?? "");

        if (doc.RootElement.TryGetProperty("sources", out var sc) && sc.ValueKind == JsonValueKind.Array)
        {
            // 标准 OBS 格式：场景也是 source（id=scene），items 在 settings 里
            foreach (var sceneSrc in sc.EnumerateArray())
            {
                if (sceneSrc.ValueKind != JsonValueKind.Object) continue;
                var isScene = sceneSrc.TryGetProperty("id", out var sid) && sid.ValueKind == JsonValueKind.String
                              && string.Equals(sid.GetString(), "scene", StringComparison.OrdinalIgnoreCase);
                if (!isScene) continue;
                if (!sceneSrc.TryGetProperty("settings", out var settings) || settings.ValueKind != JsonValueKind.Object) continue;
                if (!settings.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) continue;

                foreach (var item in items.EnumerateArray())
                {
                    if (item.TryGetProperty("source_uuid", out var su) && su.ValueKind == JsonValueKind.String)
                    {
                        var suv = su.GetString() ?? "";
                        if (!uuids.Contains(suv))
                            throw new InvalidOperationException($"场景集合 JSON 不一致：source_uuid {suv} 在 sources 中找不到。");
                    }
                }
            }
        }
    }

    // ------------------------------------------------------------ 通用辅助

    /// <summary>读取 OBS 当前可用的过渡名称列表（优先本地化的「淡入淡出 / Fade」）。</summary>
    private async Task<List<string>> LoadTransitionNamesAsync(CancellationToken ct)
    {
        var names = new List<string>();
        var r = await _obs.RawRequestAsync("GetSceneTransitionList", null, ct);
        if (r.Ok && r.Data is JsonElement d && d.TryGetProperty("transitions", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in arr.EnumerateArray())
            {
                if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("transitionName", out var n) && n.ValueKind == JsonValueKind.String)
                {
                    var s = n.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) names.Add(s);
                }
            }
        }
        return names;
    }

    /// <summary>从可用过渡中挑选匹配项：优先精确匹配，其次不区分大小写，再尝试 Fade/淡入淡出 别名。</summary>
    private static string? PickTransitionName(string? preferred, List<string> available)
    {
        if (string.IsNullOrWhiteSpace(preferred)) return available.FirstOrDefault();
        var p = preferred.Trim();
        if (available.Contains(p, StringComparer.Ordinal)) return p;
        if (available.Contains(p, StringComparer.OrdinalIgnoreCase)) return available.First(a => string.Equals(a, p, StringComparison.OrdinalIgnoreCase));

        // 别名兜底：淡入淡出 ↔ Fade / 直接切换 ↔ Cut
        var alias = p.Contains("淡入淡出") || p.Equals("Fade", StringComparison.OrdinalIgnoreCase) ? new[] { "Fade", "淡入淡出" }
            : p.Contains("直接切换") || p.Equals("Cut", StringComparison.OrdinalIgnoreCase) ? new[] { "Cut", "直接切换" }
            : null;
        if (alias is not null)
            foreach (var a in alias)
            {
                var hit = available.FirstOrDefault(x => string.Equals(x, a, StringComparison.OrdinalIgnoreCase));
                if (hit is not null) return hit;
            }
        return null;
    }

    private static string? PickKind(TemplateSource src, HashSet<string> available)
    {
        if (available.Count == 0) return src.InputKind;   // 无法获知时直接试首选类型
        if (available.Contains(src.InputKind)) return src.InputKind;
        foreach (var fk in src.FallbackKinds)
            if (available.Contains(fk)) return fk;
        return null;
    }

    private async Task<string> EnsureSceneCollectionAsync(CancellationToken ct, string baseName)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = await _obs.RawRequestAsync("GetSceneCollectionList", null, ct);
        if (list.Ok && list.Data is JsonElement d && d.TryGetProperty("sceneCollections", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var e in arr.EnumerateArray())
                if (e.ValueKind == JsonValueKind.String) existing.Add(e.GetString()!);

        var name = baseName;
        var i = 1;
        while (existing.Contains(name)) name = $"{baseName} {i++}";

        try
        {
            var r = await _obs.RawRequestAsync("CreateSceneCollection", new { sceneCollectionName = name }, ct);
            if (r.Ok) return name;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { /* 落到复查 */ }

        var verify = await _obs.RawRequestAsync("GetSceneCollectionList", null, ct);
        if (verify.Ok && CollectionExists(verify.Data, name)) return name;
        throw new InvalidOperationException("新建模板配置集合失败。");
    }

    private static bool CollectionExists(System.Text.Json.JsonElement? data, string name)
    {
        if (data is not JsonElement d) return false;
        if (!d.TryGetProperty("sceneCollections", out var arr) || arr.ValueKind != JsonValueKind.Array) return false;
        foreach (var e in arr.EnumerateArray())
            if (e.ValueKind == JsonValueKind.String && string.Equals(e.GetString(), name, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static string Describe(ObsRequestResult r)
        => !string.IsNullOrWhiteSpace(r.Comment) ? r.Comment! : $"OBS 返回错误码 {r.Code}";

    internal static string Slugify(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s ?? "")
        {
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(char.ToLowerInvariant(c));
            else if (c == ' ') sb.Append('_');
        }
        var r = sb.ToString();
        return string.IsNullOrEmpty(r) ? "template" : r;
    }

    private static string? ReadResource(string name)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(name);
        if (stream is null) return null;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
