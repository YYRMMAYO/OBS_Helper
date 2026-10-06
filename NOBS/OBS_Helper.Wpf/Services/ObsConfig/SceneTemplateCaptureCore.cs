using System.Text.Json.Nodes;
using OBS_Helper.Wpf.Localization;
using OBS_Helper.Wpf.Models.ObsConfig;

namespace OBS_Helper.Wpf.Services.ObsConfig;

/// <summary>从 OBS 读回来的一个来源（V3.0 / D6）。</summary>
public sealed record CapturedItem(
    string SourceName,
    string InputKind,
    bool Enabled,
    TransformSpec? Transform,
    JsonObject? Settings,
    IReadOnlyList<TemplateFilter> Filters,
    /// <summary>场景内的层级索引：0 = 最底（与 OBS 面板自上而下的显示顺序相反）。</summary>
    int ZOrder,
    /// <summary>
    /// 读不出来的原因（V3.0 第三轮验证）：例如「分组没能展开」「来源类型读不到」。
    /// 有值时模型会写成显式占位 —— 宁可让用户看到「这项需要手动重建」，
    /// 也不要留一个 inputKind 为空的坏来源假装捕获成功。
    /// </summary>
    string? UnsupportedReason = null);

/// <summary>从 OBS 读回来的一个场景。</summary>
public sealed record CapturedScene(
    string Name,
    string? Transition,
    int? TransitionDurationMs,
    IReadOnlyList<CapturedItem> Items);

/// <summary>从 OBS 读回来的画布设置。</summary>
public sealed record CapturedCanvas(int BaseWidth, int BaseHeight, int OutputWidth, int OutputHeight, int FpsNum, int FpsDen);

/// <summary>一次「反向捕获」的输入快照（纯数据，便于单测构造）。</summary>
public sealed record CapturedCollection(
    string CollectionName,
    CapturedCanvas Canvas,
    IReadOnlyList<CapturedScene> Scenes);

/// <summary>
/// 「我的模板」反向捕获核心（V3.0 / D6）。纯逻辑、零 IO，单测工程直接链接编译。
///
/// 背景：模板能力此前是**单向**的（官方 12 套 → 落地 / 导出）。用户自己调好的一套
/// （摄像头裁剪、滤镜、降噪）想换机或重装，只能整包备份配置，粒度太粗。
/// 这里把「当前场景集合」反向读成一份 <see cref="SceneTemplate"/> —— 与内置模板同一个 schema，
/// 因此落地器 / 离线导出**直接复用**，不需要为「我的模板」另写一套。
///
/// 三条关键取舍（都会影响换机后能不能用）：
/// <list type="number">
///   <item><b>机器相关设置一律不带走</b>：设备 id、本地文件路径、窗口句柄在另一台机器上必然无效，
///     带过去只会让落地「成功但没画面」。它们被换成 <see cref="PlaceholderSpec"/> 提示用户补齐。</item>
///   <item><b>跨场景复用的来源只创建一次</b>：同一路麦克风出现在多个场景里，
///     第二次必须走 <c>CreateSceneItem</c> 而不是 <c>CreateInput</c>（否则 OBS 会报重名或产生第二个设备占用）。</item>
///   <item><b>层级按 OBS 的显示顺序倒过来</b>：面板自上而下第 1 个是最上层，
///     而模板 schema 里 0 是最底 —— 不转换会导致整场画面层级颠倒。</item>
/// </list>
/// </summary>
public static class SceneTemplateCaptureCore
{
    /// <summary>「我的模板」的 id 前缀（UI 据此打「我的」角标；也用于删除时的白名单校验）。</summary>
    public const string MineIdPrefix = "mine-";

    /// <summary>机器相关、换机后必然无效的设置键 —— 捕获时一律剔除。</summary>
    private static readonly string[] MachineSpecificKeys =
    {
        // 设备类：dshow 用的是 video_device_id / audio_device_id（不是 device_id）
        "device_id", "device_name", "deviceId",
        "video_device_id", "audio_device_id",
        // 文件类：image_source 用 file、slideshow 用 files、文本源用 file（内置模板自己都写 "file": ""）
        "local_file", "playlist", "file", "files", "text_file",
        // 窗口 / 显示器类
        "window", "capture_window", "window_id",
        "monitor", "monitor_id", "display", "monitor_index",
    };

    /// <summary>
    /// 按来源类型推断「落地后需要用户补齐什么」。认不出来返回 null（不硬猜）。
    /// </summary>
    public static PlaceholderSpec? InferPlaceholder(string? inputKind)
    {
        if (string.IsNullOrWhiteSpace(inputKind)) return null;
        var k = inputKind.ToLowerInvariant();

        if (k.Contains("dshow") || k.Contains("wasapi") || k.Contains("alsa")
            || k.Contains("coreaudio") || k.Contains("audio_input") || k.Contains("audio_output")
            || k.Contains("av_capture"))
            return new PlaceholderSpec { Kind = "device", Hint = Strings.T("mytemplate.placeholder.device") };

        if (k.Contains("ffmpeg") || k.Contains("vlc") || k.Contains("media_source") || k.Contains("image_source"))
            return new PlaceholderSpec { Kind = "file", Hint = Strings.T("mytemplate.placeholder.file") };

        if (k.Contains("browser"))
            return new PlaceholderSpec { Kind = "url", Hint = Strings.T("mytemplate.placeholder.url") };

        if (k.Contains("window_capture") || k.Contains("game_capture") || k.Contains("display_capture")
            || k.Contains("monitor_capture") || k.Contains("screen_capture"))
            return new PlaceholderSpec { Kind = "window", Hint = Strings.T("mytemplate.placeholder.window") };

        if (k.StartsWith("text_", StringComparison.Ordinal) || k.Contains("text_source"))
            return new PlaceholderSpec { Kind = "text", Hint = Strings.T("mytemplate.placeholder.text") };

        return null;
    }

    /// <summary>清掉机器相关设置；没有可保留的键时返回 null（落地时让 OBS 用默认值）。</summary>
    public static JsonObject? SanitizeSettings(JsonObject? settings)
    {
        if (settings is null) return null;

        var keep = new JsonObject();
        foreach (var (key, value) in settings)
        {
            if (MachineSpecificKeys.Any(m => string.Equals(m, key, StringComparison.OrdinalIgnoreCase))) continue;
            // 用 Parse(ToJsonString) 做一次深拷贝：JsonNode 一个节点只能挂在一个父节点下，
            // 直接塞进去会在「同一个设置对象被两个来源共用」时抛异常。
            keep[key] = value is null ? null : JsonNode.Parse(value.ToJsonString());
        }
        return keep.Count > 0 ? keep : null;
    }

    /// <summary>
    /// 把一次捕获快照转成模板。
    /// <paramref name="title"/> 为空时用场景集合名；<paramref name="idSuffix"/> 用于保证 id 唯一。
    /// </summary>
    public static SceneTemplate Build(CapturedCollection captured, string? title, string idSuffix)
    {
        var effectiveTitle = string.IsNullOrWhiteSpace(title) ? captured.CollectionName : title.Trim();
        if (string.IsNullOrWhiteSpace(effectiveTitle)) effectiveTitle = Strings.T("mytemplate.defaultTitle");

        var seenInputs = new HashSet<string>(StringComparer.Ordinal);
        var scenes = new List<TemplateScene>();

        foreach (var scene in captured.Scenes)
        {
            var sources = new List<TemplateSource>();
            // CapturedItem.ZOrder 与模板同口径（0 = 最底），直接升序铺开并重排，
            // 保证模板里 0 一定是最底 —— 落地器按「0 最底」解释（见 ApplyZOrderAsync），
            // 这里弄反会让整场画面层级颠倒。
            foreach (var item in scene.Items.OrderBy(i => i.ZOrder))
            {
                var shared = !seenInputs.Add(item.SourceName);
                sources.Add(new TemplateSource
                {
                    Name = item.SourceName,
                    InputKind = item.InputKind ?? "",
                    ZOrder = sources.Count,
                    Enabled = item.Enabled,
                    Shared = shared,
                    // 复用的来源不该再带一份设置：落地时第二次走 CreateSceneItem，设置会被忽略
                    Settings = shared ? null : SanitizeSettings(item.Settings),
                    Transform = item.Transform,
                    Filters = shared ? new List<TemplateFilter>() : (item.Filters ?? Array.Empty<TemplateFilter>()).ToList(),
                    // 读不出来的来源给显式占位（分组无法展开、类型读不到）：
                    // 否则它会带着空 inputKind 进模板，落地时静默跳过，用户完全不知道少了什么。
                    Placeholder = item.UnsupportedReason is { Length: > 0 }
                        ? new PlaceholderSpec { Kind = "manual", Hint = item.UnsupportedReason }
                        : InferPlaceholder(item.InputKind),
                });
            }

            scenes.Add(new TemplateScene
            {
                Name = scene.Name,
                Transition = scene.Transition,
                TransitionDurationMs = scene.TransitionDurationMs,
                Sources = sources,
            });
        }

        return new SceneTemplate
        {
            Id = MineIdPrefix + idSuffix,
            Title = effectiveTitle,
            Summary = Strings.T("mytemplate.summary", captured.CollectionName, scenes.Count),
            Icon = "⭐",
            IsMine = true,
            Portrait = captured.Canvas.BaseHeight > captured.Canvas.BaseWidth,
            Notes = Strings.T("mytemplate.notes"),
            Canvas = new CanvasSpec
            {
                BaseWidth = captured.Canvas.BaseWidth,
                BaseHeight = captured.Canvas.BaseHeight,
                OutputWidth = captured.Canvas.OutputWidth,
                OutputHeight = captured.Canvas.OutputHeight,
                FpsNumerator = captured.Canvas.FpsNum <= 0 ? 30 : captured.Canvas.FpsNum,
                FpsDenominator = captured.Canvas.FpsDen <= 0 ? 1 : captured.Canvas.FpsDen,
            },
            Scenes = scenes,
        };
    }

    /// <summary>
    /// 生成 id 后缀：把标题转成安全 slug（只留字母数字与连字符），
    /// 与既有 id 冲突时依次追加 <c>-2</c> / <c>-3</c>…。返回的 id 一定不在 <paramref name="existingIds"/> 里。
    /// </summary>
    public static string MakeUniqueSuffix(string? title, IEnumerable<string>? existingIds)
    {
        var slug = Slug(title);
        if (slug.Length == 0) slug = "template";

        var existing = new HashSet<string>((existingIds ?? Array.Empty<string>())
            .Select(id => id.StartsWith(MineIdPrefix, StringComparison.Ordinal) ? id[MineIdPrefix.Length..] : id),
            StringComparer.OrdinalIgnoreCase);

        if (!existing.Contains(slug)) return slug;
        for (var i = 2; i < 1000; i++)
        {
            var candidate = slug + "-" + i;
            if (!existing.Contains(candidate)) return candidate;
        }
        return slug + "-" + DateTime.Now.ToString("HHmmss");
    }

    /// <summary>
    /// 把 <see cref="TransformSpec"/> 组装成 v5 的 <c>SetSceneItemTransform</c> 请求体（V3.0 第四轮验证新增）。
    ///
    /// 抽到纯核心里的原因有两层：一是这段字段名**极易写错且错了不报错**（obs-websocket 忽略未知字段），
    /// 放在服务类里就测不到；二是第四轮验证正是在这里抓到「裁剪写成嵌套 crop」的 bug ——
    /// v5 只认**顶层** <c>cropLeft/cropTop/cropRight/cropBottom</c>。
    /// 只负责位置 / 对齐 / 裁剪 / 旋转；scale 与 bounds 的取舍由调用方按模板语义决定。
    /// </summary>
    public static JsonObject BuildV5Transform(TransformSpec t) => new()
    {
        ["positionX"] = t.PosX ?? 0,
        ["positionY"] = t.PosY ?? 0,
        ["alignment"] = t.Alignment ?? 0,
        ["cropLeft"] = t.CropLeft ?? 0,
        ["cropTop"] = t.CropTop ?? 0,
        ["cropRight"] = t.CropRight ?? 0,
        ["cropBottom"] = t.CropBottom ?? 0,
        ["rotation"] = t.Rotation ?? 0,
    };

    /// <summary>
    /// 把 <see cref="TransformSpec"/> 组装成 v4 <c>SetSceneItemProperties</c> 需要的**嵌套**形式
    /// （v4 用 <c>crop</c> / <c>position</c> 子对象）。与 <see cref="BuildV5Transform"/> 一起构成
    /// 「扁平 ↔ 嵌套」的对照，两者的字段差异有单测钉住。
    /// </summary>
    public static JsonObject BuildV4TransformFields(TransformSpec t)
    {
        var o = new JsonObject
        {
            ["position"] = new JsonObject
            {
                ["x"] = t.PosX ?? 0,
                ["y"] = t.PosY ?? 0,
                ["alignment"] = t.Alignment ?? 0,
            },
            ["crop"] = new JsonObject
            {
                ["left"] = t.CropLeft ?? 0,
                ["top"] = t.CropTop ?? 0,
                ["right"] = t.CropRight ?? 0,
                ["bottom"] = t.CropBottom ?? 0,
            },
        };
        if (t.Rotation is { } rotation) o["rotation"] = rotation;
        return o;
    }

    /// <summary>把 v5 的**扁平**变换响应读成 <see cref="TransformSpec"/>（字段名与官方一致）。</summary>
    public static TransformSpec? ReadV5Transform(System.Text.Json.JsonElement t)
    {
        double? D(string name) => t.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number ? v.GetDouble() : null;
        int? I(string name) => t.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number ? v.GetInt32() : null;
        string? S(string name) => t.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : null;

        return ReadTransform(
            D("positionX"), D("positionY"), D("scaleX"), D("scaleY"),
            S("boundsType"), D("boundsWidth"), D("boundsHeight"), I("alignment"),
            D("cropLeft"), D("cropTop"), D("cropRight"), D("cropBottom"), D("rotation"));
    }

    /// <summary>
    /// 从 v5 的 <c>GetSceneItemTransform</c> 响应里取变换（响应把变换放在 <c>sceneItemTransform</c> 下）。
    /// 拿不到时返回 null —— 调用方据此**记一条说明**，而不是静默按默认值走。
    /// </summary>
    public static TransformSpec? ReadV5SceneItemTransform(System.Text.Json.JsonElement response)
        => response.TryGetProperty("sceneItemTransform", out var t) && t.ValueKind == System.Text.Json.JsonValueKind.Object
            ? ReadV5Transform(t)
            : null;

    /// <summary>标题 → 文件名/id 安全的 slug（保留中文：Windows 文件名允许，且用户能认出来）。</summary>
    public static string Slug(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var c in title.Trim())
        {
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            else if (c is ' ' or '-' or '_') sb.Append('-');
        }

        var s = sb.ToString().Trim('-');
        while (s.Contains("--", StringComparison.Ordinal)) s = s.Replace("--", "-");
        return s.Length > 60 ? s[..60].Trim('-') : s;
    }

    /// <summary>把 v5 的扁平变换字段读成 <see cref="TransformSpec"/>；缺字段一律留 null（用 OBS 默认值）。</summary>
    public static TransformSpec? ReadTransform(
        double? posX, double? posY, double? scaleX, double? scaleY,
        string? boundsType, double? boundsWidth, double? boundsHeight, int? alignment,
        double? cropLeft = null, double? cropTop = null, double? cropRight = null, double? cropBottom = null,
        double? rotation = null)
    {
        if (posX is null && posY is null && scaleX is null && scaleY is null
            && string.IsNullOrEmpty(boundsType) && boundsWidth is null && boundsHeight is null && alignment is null
            && cropLeft is null && cropTop is null && cropRight is null && cropBottom is null && rotation is null)
            return null;

        return new TransformSpec
        {
            PosX = posX,
            PosY = posY,
            ScaleX = scaleX,
            ScaleY = scaleY,
            BoundsType = string.IsNullOrWhiteSpace(boundsType) ? null : boundsType,
            BoundsWidth = boundsWidth,
            BoundsHeight = boundsHeight,
            Alignment = alignment,
            CropLeft = cropLeft,
            CropTop = cropTop,
            CropRight = cropRight,
            CropBottom = cropBottom,
            Rotation = rotation,
        };
    }

    /// <summary>模板能否落地：至少一个场景、且每个场景至少一个来源（与内置模板同一口径）。</summary>
    public static bool IsUsable(SceneTemplate template)
        => template.Scenes.Count > 0 && template.Scenes.All(s => s.Sources.Count > 0);
}
