using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using OBS_Helper.Client.Models.Obs;
using OBS_Helper.Client.Models.ObsConfig;
using OBS_Helper.Client.Services.Host;
using OBS_Helper.Client.Services.Obs;

namespace OBS_Helper.Client.Services.ObsConfig;

/// <summary>
/// 直播间场景模板：把内置模板一键落地到 OBS（与 Windows 版 SceneTemplateService 同源）。
///
/// <list type="bullet">
///   <item><b>在线落地（ApplyAsync）</b>：已连 OBS 时走 obs-websocket，新建一个干净配置集合并切过去，
///         再逐场景 / 逐来源创建。跨场景复用的来源（麦克风 / 等待音乐）<c>shared</c> 标记走 CreateSceneItem。</item>
///   <item><b>离线降级（ExportAsync）</b>：未连接时生成标准 OBS 场景集合 JSON，交给宿主弹原生保存对话框落盘
///         （文件名用 ASCII slug，显示名靠 JSON 内的 <c>name</c>）。</item>
/// </list>
/// 所有设备 / 文件 / URL 在模板里留空，落地后汇总成「还需你手动补齐」清单（Placeholder）。
/// </summary>
public sealed class SceneTemplateService
{
    private readonly HttpClient _http;
    private readonly ObsConnectionService _obs;
    private readonly HostBridge _host;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private List<SceneTemplate>? _cache;
    private string? _loadError;

    public SceneTemplateService(HttpClient http, ObsConnectionService obs, HostBridge host)
    {
        _http = http;
        _obs = obs;
        _host = host;
    }

    /// <summary>数据加载失败时的错误信息（供 UI 展示）。</summary>
    public string? LoadError => _loadError;

    public async Task<IReadOnlyList<SceneTemplate>> LoadAsync()
    {
        if (_cache is not null) return _cache;
        await _lock.WaitAsync();
        try
        {
            if (_cache is not null) return _cache;
            try
            {
                _cache = await _http.GetFromJsonAsync<List<SceneTemplate>>("data/scene_templates.json")
                         ?? new List<SceneTemplate>();
                _loadError = null;
            }
            catch (Exception ex)
            {
                _cache = new List<SceneTemplate>();
                _loadError = $"模板数据加载失败：{ex.Message}";
            }
            return _cache;
        }
        finally
        {
            _lock.Release();
        }
    }

    // ------------------------------------------------------------ 在线落地

    public async Task<ApplyResult> ApplyAsync(string templateId, bool applyCanvas, Action<string>? onProgress)
    {
        var tpl = (await LoadAsync()).FirstOrDefault(t => t.Id == templateId);
        if (tpl is null)
            return new ApplyResult { Ok = false, Error = "未找到该模板。" };

        if (!_obs.IsConnected)
            return new ApplyResult { Ok = false, Error = "未连接到 OBS，无法在线落地。请先在控制台连接，或使用「导出场景集合 JSON」。" };

        try
        {
            onProgress?.Invoke("正在读取 OBS 可用来源类型…");
            var kindsResult = await _obs.RawRequestAsync("GetInputKindList");
            var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (kindsResult.Ok && kindsResult.Data is JsonElement kd && kd.TryGetProperty("inputKinds", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var e in arr.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.String) available.Add(e.GetString()!);

            var transitionNames = await LoadTransitionNamesAsync();

            onProgress?.Invoke("正在新建模板专属配置集合…");
            var collectionName = await EnsureSceneCollectionAsync($"模板 · {tpl.Title}");

            if (applyCanvas)
            {
                onProgress?.Invoke("正在设置画布分辨率…");
                await _obs.RawRequestAsync("SetVideoSettings", new
                {
                    baseWidth = tpl.Canvas.BaseWidth,
                    baseHeight = tpl.Canvas.BaseHeight,
                    outputWidth = tpl.Canvas.OutputWidth,
                    outputHeight = tpl.Canvas.OutputHeight,
                    fpsNumerator = tpl.Canvas.FpsNumerator,
                    fpsDenominator = tpl.Canvas.FpsDenominator
                });
            }

            var transitionNotes = new List<string>();
            if (transitionNames.Count > 0)
            {
                var cur = PickTransitionName(tpl.Transition, transitionNames);
                if (cur is not null)
                {
                    await _obs.RawRequestAsync("SetCurrentSceneTransition", new { transitionName = cur });
                    await _obs.RawRequestAsync("SetCurrentSceneTransitionDuration", new { transitionDuration = tpl.TransitionDurationMs });
                }
                else
                {
                    transitionNotes.Add($"模板默认过渡「{tpl.Transition}」在 OBS 中不可用，已保持 OBS 原过渡。");
                }
            }

            int created = 0, skipped = 0;
            var placeholders = new List<string>();
            var createdInputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var scene in tpl.Scenes)
            {
                onProgress?.Invoke($"正在创建场景「{scene.Name}」…");
                var cs = await _obs.RawRequestAsync("CreateScene", new { sceneName = scene.Name });
                if (!cs.Ok) { skipped++; continue; }
                created++;

                if (transitionNames.Count > 0 && (scene.Transition is not null || scene.TransitionDurationMs is not null))
                {
                    var ovName = PickTransitionName(scene.Transition ?? tpl.Transition, transitionNames);
                    var ovDur = scene.TransitionDurationMs ?? tpl.TransitionDurationMs;
                    var ovOk = await _obs.RawRequestAsync("SetSceneTransitionOverride", new
                    {
                        sceneName = scene.Name,
                        transitionName = ovName,
                        transitionDuration = ovDur
                    });
                    if (!ovOk.Ok && ovName is null)
                        transitionNotes.Add($"场景「{scene.Name}」的过渡覆盖未生效（{Describe(ovOk)}）。");
                }

                foreach (var src in scene.Sources.OrderBy(s => s.ZOrder))
                {
                    try
                    {
                        await CreateOneSourceAsync(scene.Name, src, available, createdInputs, placeholders);
                    }
                    catch (Exception ex)
                    {
                        skipped++;
                        placeholders.Add($"{scene.Name} / {src.Name}：创建失败（{ex.Message}），已跳过。");
                    }
                }
            }

            onProgress?.Invoke("正在切换到主场景…");
            if (tpl.Scenes.Count > 0)
                await _obs.RawRequestAsync("SetCurrentProgramScene", new { sceneName = tpl.Scenes[0].Name });

            onProgress?.Invoke("正在刷新状态…");
            await _obs.RefreshAllAsync();

            var hotkeyScenes = tpl.Scenes.Where(s => !string.IsNullOrWhiteSpace(s.Hotkey)).ToList();
            if (hotkeyScenes.Count > 0)
                transitionNotes.Add("场景切换快捷键（" + string.Join(" / ", hotkeyScenes.Select(s => $"{s.Hotkey} → {s.Name}")) + "）需在 OBS 中手动绑定，或改用「导出场景集合 JSON」方式导入后自动生效。");

            if (transitionNotes.Count > 0)
                placeholders.InsertRange(0, transitionNotes);

            return new ApplyResult
            {
                Ok = true,
                Created = created,
                Skipped = skipped,
                Placeholders = placeholders,
                Error = skipped > 0 ? $"已落地，但有 {skipped} 个来源未能创建（多为本地设备 / 文件需在 OBS 中补齐）。" : null
            };
        }
        catch (Exception ex)
        {
            return new ApplyResult { Ok = false, Error = $"模板落地失败：{ex.Message}" };
        }
    }

    private async Task CreateOneSourceAsync(string sceneName, TemplateSource src, HashSet<string> available,
        Dictionary<string, string> createdInputs, List<string> placeholders)
    {
        int itemId;
        string inputName;

        if (src.Shared && createdInputs.TryGetValue(src.Name, out var existingInput))
        {
            var ci = await _obs.RawRequestAsync("CreateSceneItem", new { sceneName, sourceName = existingInput, sceneItemEnabled = src.Enabled });
            if (!ci.Ok || ci.Data is not JsonElement cid || !cid.TryGetProperty("sceneItemId", out var siid) || siid.ValueKind != JsonValueKind.Number)
                throw new InvalidOperationException("复用来源失败：" + Describe(ci));
            itemId = siid.GetInt32();
            inputName = existingInput;
        }
        else
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
            });

            if (!ci.Ok || ci.Data is not JsonElement cid)
            {
                var fallback = await _obs.RawRequestAsync("CreateSceneItem", new { sceneName, sourceName = src.Name, sceneItemEnabled = src.Enabled });
                if (!fallback.Ok || fallback.Data is not JsonElement fcid || !fcid.TryGetProperty("sceneItemId", out var fsiid) || fsiid.ValueKind != JsonValueKind.Number)
                {
                    placeholders.Add($"{sceneName} / {src.Name}：{Describe(ci)}");
                    throw new InvalidOperationException("创建来源失败");
                }
                itemId = fsiid.GetInt32();
                inputName = src.Name;
            }
            else
            {
                inputName = cid.TryGetProperty("inputName", out var inn) && inn.ValueKind == JsonValueKind.String ? inn.GetString()! : src.Name;
                itemId = cid.TryGetProperty("sceneItemId", out var siidn) && siidn.ValueKind == JsonValueKind.Number ? siidn.GetInt32() : -1;
                if (!createdInputs.ContainsKey(src.Name)) createdInputs[src.Name] = inputName;
            }
        }

        if (itemId < 0) return;

        if (src.Transform is not null)
            await ApplyTransformAsync(sceneName, itemId, src.Transform);

        var ordered = await _obs.RawRequestAsync("GetSceneItemList", new { sceneName });
        int count = 0;
        if (ordered.Ok && ordered.Data is JsonElement od && od.TryGetProperty("sceneItems", out var sal) && sal.ValueKind == JsonValueKind.Array)
            count = sal.GetArrayLength();
        if (count > 0)
            await _obs.RawRequestAsync("SetSceneItemIndex", new { sceneName, sceneItemId = itemId, sceneItemIndex = Math.Max(0, count - 1 - src.ZOrder) });

        if (!src.Enabled)
            await _obs.RawRequestAsync("SetSceneItemEnabled", new { sceneName, sceneItemId = itemId, sceneItemEnabled = false });

        if (src.Placeholder is not null)
            placeholders.Add($"{sceneName} / {src.Name}：{src.Placeholder.Hint}");
    }

    private async Task ApplyTransformAsync(string sceneName, int itemId, TransformSpec t)
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
            tf["scaleX"] = t.ScaleX ?? 1;
            tf["scaleY"] = t.ScaleY ?? 1;
        }
        else
        {
            tf["boundsType"] = t.BoundsType!;
            tf["boundsWidth"] = t.BoundsWidth ?? 0;
            tf["boundsHeight"] = t.BoundsHeight ?? 0;
        }

        await _obs.RawRequestAsync("SetSceneItemTransform", new { sceneName, sceneItemId = itemId, sceneItemTransform = tf });
    }

    // ------------------------------------------------------------ 离线导出

    /// <summary>生成标准 OBS 场景集合 JSON 并交给宿主保存。返回保存路径；取消或失败返回 null。</summary>
    public async Task<string?> ExportAsync(string templateId, Action<string>? onProgress)
    {
        var tpl = (await LoadAsync()).FirstOrDefault(t => t.Id == templateId);
        if (tpl is null) return null;

        var collectionName = $"模板 · {tpl.Title}";
        var json = BuildSceneCollectionJson(tpl, collectionName);
        var text = json.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        SelfCheckCollection(json);

        var fileName = $"obshelper_{Slugify(tpl.Id)}.json";
        onProgress?.Invoke("正在选择保存位置…");
        var path = await _host.ExportTemplateAsync(fileName, text);
        if (string.IsNullOrEmpty(path))
            return null;

        // 若宿主不可用，给出提示由页面展示
        onProgress?.Invoke($"已导出：{path}");
        return path;
    }

    // ------------------------------------------------------------ 场景集合 JSON 生成（标准 OBS 格式，version 2）

    private static JsonObject BuildSceneCollectionJson(SceneTemplate tpl, string collectionName)
    {
        var sources = new JsonArray();
        var sceneOrder = new JsonArray();
        var uuidByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var canvasW = Math.Max(1, tpl.Canvas.BaseWidth);
        var canvasH = Math.Max(1, tpl.Canvas.BaseHeight);
        var canvasUuid = "6c69626f-6273-4c00-9d88-c5136d61696e";

        foreach (var scene in tpl.Scenes)
        {
            sceneOrder.Add(new JsonObject { ["name"] = scene.Name });
            foreach (var src in scene.Sources.OrderBy(s => s.ZOrder))
            {
                if (!uuidByName.TryGetValue(src.Name, out var srcUuid))
                {
                    srcUuid = Guid.NewGuid().ToString("D").ToLowerInvariant();
                    uuidByName[src.Name] = srcUuid;
                    var (id, versionedId) = ResolveSourceId(src.InputKind);
                    var isAudio = IsAudioInput(src.InputKind);
                    sources.Add(BuildSourceJson(src, id, versionedId, srcUuid, isAudio));
                }
            }
        }

        int sceneItemId = 1;
        foreach (var scene in tpl.Scenes)
        {
            var sceneUuid = Guid.NewGuid().ToString("D").ToLowerInvariant();
            var items = new JsonArray();
            var itemIds = new List<int>();

            foreach (var src in scene.Sources.OrderBy(s => s.ZOrder))
            {
                var srcUuid = uuidByName[src.Name];
                items.Add(BuildFileItem(src, srcUuid, sceneItemId, canvasW, canvasH));
                itemIds.Add(sceneItemId);
                sceneItemId++;
            }

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

            var sceneHotkeys = new JsonObject
            {
                ["OBSBasic.SelectScene"] = BuildHotkeyBindings(scene.Hotkey)
            };
            foreach (var id in itemIds)
            {
                sceneHotkeys[$"libobs.show_scene_item.{id}"] = new JsonArray();
                sceneHotkeys[$"libobs.hide_scene_item.{id}"] = new JsonArray();
            }

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
                ["hotkeys"] = sceneHotkeys,
                ["deinterlace_mode"] = 0,
                ["deinterlace_field_order"] = 0,
                ["monitoring_type"] = 0,
                ["canvas_uuid"] = canvasUuid,
                ["private_settings"] = new JsonObject()
            });
        }

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

    private static JsonObject BuildSourceJson(TemplateSource src, string id, string versionedId, string uuid, bool isAudio)
        => new()
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
            ["pos_rel"] = new JsonObject { ["x"] = (t?.PosX ?? 0) / canvasW, ["y"] = (t?.PosY ?? 0) / canvasH },
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

    private static JsonArray BuildHotkeyBindings(string? hotkey)
    {
        var arr = new JsonArray();
        if (string.IsNullOrWhiteSpace(hotkey)) return arr;
        var binding = ParseHotkey(hotkey);
        if (binding is not null) arr.Add(binding);
        return arr;
    }

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

    private static JsonObject BuildAudioHotkeys() => new()
    {
        ["libobs.mute"] = new JsonArray(),
        ["libobs.unmute"] = new JsonArray(),
        ["libobs.push-to-mute"] = new JsonArray(),
        ["libobs.push-to-talk"] = new JsonArray()
    };

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

    private static void SelfCheckCollection(JsonObject root)
    {
        using var doc = JsonDocument.Parse(root.ToJsonString());
        var uuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (doc.RootElement.TryGetProperty("sources", out var ss) && ss.ValueKind == JsonValueKind.Array)
            foreach (var s in ss.EnumerateArray())
                if (s.TryGetProperty("uuid", out var u) && u.ValueKind == JsonValueKind.String) uuids.Add(u.GetString() ?? "");

        if (doc.RootElement.TryGetProperty("sources", out var sc) && sc.ValueKind == JsonValueKind.Array)
        {
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

    private async Task<List<string>> LoadTransitionNamesAsync()
    {
        var names = new List<string>();
        var r = await _obs.RawRequestAsync("GetSceneTransitionList");
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

    private static string? PickTransitionName(string? preferred, List<string> available)
    {
        if (string.IsNullOrWhiteSpace(preferred)) return available.FirstOrDefault();
        var p = preferred.Trim();
        if (available.Contains(p, StringComparer.Ordinal)) return p;
        if (available.Contains(p, StringComparer.OrdinalIgnoreCase)) return available.First(a => string.Equals(a, p, StringComparison.OrdinalIgnoreCase));

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
        if (available.Count == 0) return src.InputKind;
        if (available.Contains(src.InputKind)) return src.InputKind;
        foreach (var fk in src.FallbackKinds)
            if (available.Contains(fk)) return fk;
        return null;
    }

    private async Task<string> EnsureSceneCollectionAsync(string baseName)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = await _obs.RawRequestAsync("GetSceneCollectionList");
        if (list.Ok && list.Data is JsonElement d && d.TryGetProperty("sceneCollections", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var e in arr.EnumerateArray())
                if (e.ValueKind == JsonValueKind.String) existing.Add(e.GetString()!);

        var name = baseName;
        var i = 1;
        while (existing.Contains(name)) name = $"{baseName} {i++}";

        try
        {
            var r = await _obs.RawRequestAsync("CreateSceneCollection", new { sceneCollectionName = name });
            if (r.Ok) return name;
        }
        catch (Exception) { /* 落到复查 */ }

        var verify = await _obs.RawRequestAsync("GetSceneCollectionList");
        if (verify.Ok && CollectionExists(verify.Data, name)) return name;
        throw new InvalidOperationException("新建模板配置集合失败。");
    }

    private static bool CollectionExists(JsonElement? data, string name)
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
}
