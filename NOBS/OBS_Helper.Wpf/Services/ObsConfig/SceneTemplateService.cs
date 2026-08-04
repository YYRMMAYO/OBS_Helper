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

        try
        {
            p.Report("正在读取 OBS 可用来源类型…");
            var kindsResult = await _obs.RawRequestAsync("GetInputKindList", null, ct);
            var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (kindsResult.Ok && kindsResult.Data is JsonElement kd && kd.TryGetProperty("inputKinds", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var e in arr.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.String) available.Add(e.GetString()!);

            p.Report("正在新建模板专属配置集合…");
            var collectionName = await EnsureSceneCollectionAsync(ct, $"模板 · {tpl.Title}");

            if (applyCanvas)
            {
                p.Report("正在设置画布分辨率…");
                await _obs.RawRequestAsync("SetVideoSettings", new
                {
                    baseWidth = tpl.Canvas.BaseWidth,
                    baseHeight = tpl.Canvas.BaseHeight,
                    outputWidth = tpl.Canvas.OutputWidth,
                    outputHeight = tpl.Canvas.OutputHeight,
                    fpsNumerator = tpl.Canvas.FpsNumerator,
                    fpsDenominator = tpl.Canvas.FpsDenominator
                }, ct);
            }

            int created = 0, skipped = 0;
            var placeholders = new List<string>();
            var createdInputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var scene in tpl.Scenes)
            {
                p.Report($"正在创建场景「{scene.Name}」…");
                var cs = await _obs.RawRequestAsync("CreateScene", new { sceneName = scene.Name }, ct);
                if (!cs.Ok) { skipped++; continue; }
                created++;

                var ordered = scene.Sources.OrderBy(s => s.ZOrder).ToList();
                foreach (var src in ordered)
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

            p.Report("正在切换到主场景…");
            if (tpl.Scenes.Count > 0)
                await _obs.RawRequestAsync("SetCurrentProgramScene", new { sceneName = tpl.Scenes[0].Name }, ct);

            p.Report("正在刷新状态…");
            await _obs.RefreshAllAsync();

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

    private async Task CreateOneSourceAsync(string sceneName, TemplateSource src, HashSet<string> available,
        Dictionary<string, string> createdInputs, List<string> placeholders, CancellationToken ct)
    {
        int itemId;
        string inputName;

        if (src.Shared && createdInputs.TryGetValue(src.Name, out var existingInput))
        {
            // 复用已创建的输入：在同一场景里用 CreateSceneItem 引用它
            var ci = await _obs.RawRequestAsync("CreateSceneItem", new { sceneName, sourceName = existingInput, sceneItemEnabled = src.Enabled }, ct);
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
            }, ct);

            if (!ci.Ok || ci.Data is not JsonElement cid)
            {
                // 601 ResourceAlreadyExists 等情况：尝试用 CreateSceneItem 引用同名来源
                var fallback = await _obs.RawRequestAsync("CreateSceneItem", new { sceneName, sourceName = src.Name, sceneItemEnabled = src.Enabled }, ct);
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

        // 变换（带层级）
        if (src.Transform is not null)
            await ApplyTransformAsync(sceneName, itemId, src.Transform, ct);

        // 层级：OBS index 0 = 最上，模板 zOrder 0 = 最底 → index = count-1-zOrder
        var ordered = (await _obs.RawRequestAsync("GetSceneItemList", new { sceneName }, ct));
        int count = 0;
        if (ordered.Ok && ordered.Data is JsonElement od && od.TryGetProperty("sceneItems", out var sal) && sal.ValueKind == JsonValueKind.Array)
            count = sal.GetArrayLength();
        if (count > 0)
            await _obs.RawRequestAsync("SetSceneItemIndex", new { sceneName, sceneItemId = itemId, sceneItemIndex = Math.Max(0, count - 1 - src.ZOrder) }, ct);

        if (!src.Enabled)
            await _obs.RawRequestAsync("SetSceneItemEnabled", new { sceneName, sceneItemId = itemId, sceneItemEnabled = false }, ct);

        // 占位提示
        if (src.Placeholder is not null)
            placeholders.Add($"{sceneName} / {src.Name}：{src.Placeholder.Hint}");
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

        var fileName = $"obshelper_{Slugify(tpl.Id)}_{DateTime.Now:yyyyMMdd}.json";
        var path = Path.Combine(dir, fileName);
        int n = 2;
        while (File.Exists(path)) path = Path.Combine(dir, $"obshelper_{Slugify(tpl.Id)}_{DateTime.Now:yyyyMMdd}_{n}.json");

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

    // ------------------------------------------------------------ 场景集合 JSON 生成（版本化陷阱）

    /// <summary>
    /// 生成标准 OBS 场景集合 JSON（一个集合含模板全部场景）。
    /// 关键点：<c>id</c> 是无版本 id，<c>versioned_id</c> 才带后缀；每个 item 的 <c>source_uuid</c>
    /// 必须与 sources 里对应 input 的 <c>uuid</c> 完全一致；<c>bounds_type</c> 在文件里是数字。
    /// </summary>
    private static JsonObject BuildSceneCollectionJson(SceneTemplate tpl, string collectionName)
    {
        var sources = new JsonArray();
        var scenes = new JsonArray();
        var sceneOrder = new JsonArray();
        var uuidByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int idCounter = 1;

        foreach (var scene in tpl.Scenes)
        {
            sceneOrder.Add(new JsonObject { ["name"] = scene.Name });
            var items = new JsonArray();
            int sceneItemId = 1;
            var sceneUuid = Guid.NewGuid().ToString("D").ToLowerInvariant();

            foreach (var src in scene.Sources.OrderBy(s => s.ZOrder))
            {
                if (!uuidByName.TryGetValue(src.Name, out var srcUuid))
                {
                    srcUuid = Guid.NewGuid().ToString("D").ToLowerInvariant();
                    uuidByName[src.Name] = srcUuid;
                    var (id, versionedId) = ResolveSourceId(src.InputKind);
                    sources.Add(new JsonObject
                    {
                        ["id"] = id,
                        ["versioned_id"] = versionedId,
                        ["name"] = src.Name,
                        ["uuid"] = srcUuid,
                        ["type"] = "input",
                        ["settings"] = src.Settings ?? new JsonObject()
                    });
                }

                items.Add(new JsonObject
                {
                    ["scene_item_id"] = sceneItemId,
                    ["source_uuid"] = srcUuid,
                    ["transform"] = BuildFileTransform(src.Transform)
                });
                sceneItemId++;
            }

            scenes.Add(new JsonObject
            {
                ["name"] = scene.Name,
                ["uuid"] = sceneUuid,
                ["items"] = items
            });
            idCounter = Math.Max(idCounter, sceneItemId);
        }

        var root = new JsonObject
        {
            ["type"] = "scene_collection",
            ["name"] = collectionName,
            ["sources"] = sources,
            ["scene_order"] = sceneOrder,
            ["scenes"] = scenes,
            ["id_counter"] = idCounter,
            ["source_types"] = new JsonArray(),
            ["current_scene"] = tpl.Scenes.Count > 0 ? tpl.Scenes[0].Name : "",
            ["current_program_scene"] = tpl.Scenes.Count > 0 ? tpl.Scenes[0].Name : "",
            ["current_preview_scene"] = tpl.Scenes.Count > 0 ? tpl.Scenes[0].Name : ""
        };
        return root;
    }

    private static JsonObject BuildFileTransform(TransformSpec? t)
    {
        var tf = new JsonObject
        {
            ["pos"] = new JsonObject { ["x"] = t?.PosX ?? 0, ["y"] = t?.PosY ?? 0 },
            ["scale"] = new JsonObject { ["x"] = t?.ScaleX ?? 1, ["y"] = t?.ScaleY ?? 1 },
            ["crop"] = new JsonObject { ["top"] = 0, ["bottom"] = 0, ["left"] = 0, ["right"] = 0 },
            ["alignment"] = t?.Alignment ?? 0
        };

        var boundsNone = t is null || string.IsNullOrEmpty(t.BoundsType) ||
                         string.Equals(t.BoundsType, "OBS_BOUNDS_NONE", StringComparison.OrdinalIgnoreCase);
        if (boundsNone)
        {
            tf["bounds"] = new JsonObject { ["type"] = 0, ["x"] = 0, ["y"] = 0 };
        }
        else
        {
            tf["bounds"] = new JsonObject
            {
                ["type"] = BoundsTypeToNumber(t!.BoundsType!),
                ["x"] = t.BoundsWidth ?? 0,
                ["y"] = t.BoundsHeight ?? 0
            };
        }
        return tf;
    }

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

    /// <summary>离线 JSON 自校验：能解析，且每个 source_uuid 都能在 sources 中找到对应 uuid。</summary>
    private static void SelfCheckCollection(JsonObject root)
    {
        using var doc = JsonDocument.Parse(root.ToJsonString());
        var uuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (doc.RootElement.TryGetProperty("sources", out var ss) && ss.ValueKind == JsonValueKind.Array)
            foreach (var s in ss.EnumerateArray())
                if (s.TryGetProperty("uuid", out var u) && u.ValueKind == JsonValueKind.String) uuids.Add(u.GetString() ?? "");

        if (doc.RootElement.TryGetProperty("scenes", out var sc) && sc.ValueKind == JsonValueKind.Array)
        {
            foreach (var scene in sc.EnumerateArray())
            {
                if (!scene.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) continue;
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
