using System.Text.Json.Nodes;

namespace OBS_Helper.Wpf.Models.ObsConfig;

/// <summary>一套直播间场景模板（含画布与若干场景，每个场景含若干来源）。</summary>
public sealed class SceneTemplate
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Icon { get; set; } = "";
    /// <summary>是否为竖屏模板（切到 OBS 全局画布为 1080×1920）。</summary>
    public bool Portrait { get; set; }
    public string Notes { get; set; } = "";
    public CanvasSpec Canvas { get; set; } = new();
    /// <summary>默认场景切换过渡名称（如「淡入淡出」），落地时设置为当前过渡。</summary>
    /// <summary>
    /// 默认过渡名。这是**逻辑值**而不是展示文案（V2.9.2）：它经
    /// <c>SceneTemplateService.PickTransitionName</c> 映射到 OBS 的过渡名，别名表同时认
    /// 「淡入淡出」/「直接切换」与 OBS 英文界面里的 Fade / Cut。
    ///
    /// V3.0（E5）：默认值改为取文案键而不是写死中文 —— 内置模板数据里都带显式 <c>transition</c>，
    /// 这里只影响「程序自己构造的模板」（例如 D6 反向捕获时本地尚未读到过渡信息的情况）。
    /// 英文环境下新建的模板会记成 "Fade"，而别名表两种写法都认，因此跨语言共用模板不会失效。
    /// </summary>
    public string Transition { get; set; } = Localization.Strings.T("template.transition.default");
    /// <summary>默认场景切换过渡时长（毫秒）。</summary>
    public int TransitionDurationMs { get; set; } = 300;
    public List<TemplateScene> Scenes { get; set; } = new();

    /// <summary>
    /// 是否为用户自己存的「我的模板」（V3.0 / D6）。
    ///
    /// 内置模板没有这个字段（读出来是 false）。这个标记可以安全地随 JSON 一起序列化：
    /// 用户模板落在 <c>%LocalAppData%\OBS_Helper\templates</c>，内置模板仍在程序集里，
    /// 但两者的 schema **完全一致** —— 落地器与离线导出因此共用同一条代码路径。
    /// </summary>
    public bool IsMine { get; set; }

    /// <summary>推荐 / 依赖的插件（V2.2 P2-2）：落地前对照本机体检结果标注是否已装，缺失给跳转。</summary>
    public List<TemplatePluginRequirement> RequiresPlugins { get; set; } = new();
}

/// <summary>模板对一个插件的依赖（当前均为可选推荐：缺失不阻断落地，仅提示）。</summary>
public sealed class TemplatePluginRequirement
{
    public string Id { get; set; } = "";
    public string Reason { get; set; } = "";
}

public sealed class CanvasSpec
{
    public int BaseWidth { get; set; } = 1920;
    public int BaseHeight { get; set; } = 1080;
    public int OutputWidth { get; set; } = 1920;
    public int OutputHeight { get; set; } = 1080;
    public int FpsNumerator { get; set; } = 30;
    public int FpsDenominator { get; set; } = 1;
}

/// <summary>模板里的一个场景：名称、可选过渡覆盖、快捷键与来源列表。</summary>
public sealed class TemplateScene
{
    public string Name { get; set; } = "";
    /// <summary>可选：本场景的过渡覆盖名称（如「淡入淡出」）；为空时用模板默认过渡。</summary>
    public string? Transition { get; set; }
    /// <summary>可选：本场景的过渡覆盖时长（毫秒）；为空时用模板默认时长。</summary>
    public int? TransitionDurationMs { get; set; }
    /// <summary>可选：切换本场景的快捷键，如「Ctrl+1」；落地时写入 OBS 快捷键，离线导出写入 hotkeys。</summary>
    public string? Hotkey { get; set; }
    public List<TemplateSource> Sources { get; set; } = new();
}

public sealed class TemplateSource
{
    public string Name { get; set; } = "";
    /// <summary>首选来源类型（obs-websocket inputKind），如 text_gdiplus_v3 / image_source。</summary>
    public string InputKind { get; set; } = "";
    /// <summary>层级：0 最底，数字越大越靠上。</summary>
    public int ZOrder { get; set; }
    public bool Enabled { get; set; } = true;
    /// <summary>跨场景复用的输入（如麦克风 / 等待音乐）：第二次遇到走 CreateSceneItem 而非重新 CreateInput。</summary>
    public bool Shared { get; set; }
    /// <summary>首选类型不可用时依次尝试的回退类型。</summary>
    public List<string> FallbackKinds { get; set; } = new();
    /// <summary>透传给 obs-websocket CreateInput 的该来源设置（设备 / 文件 / URL 留空，落地后由用户补齐）。</summary>
    public JsonObject? Settings { get; set; }
    public TransformSpec? Transform { get; set; }
    /// <summary>落地后仍需用户在 OBS 里手动补齐的项（设备 / 文件 / URL 等）。</summary>
    public PlaceholderSpec? Placeholder { get; set; }

    /// <summary>
    /// 该来源上挂的滤镜（V3.0 / D6）。
    ///
    /// 内置模板走空列表；「我的模板」反向捕获时会把降噪 / 色键 / 锐化这类**与画面内容有关、
    /// 与机器无关**的滤镜一并带走 —— 这正是用户换机后最不想重配的东西。
    /// 滤镜设置里机器相关的键在捕获阶段就已剔除（见 <c>SceneTemplateCaptureCore.SanitizeSettings</c>）。
    /// </summary>
    public List<TemplateFilter> Filters { get; set; } = new();
}

/// <summary>模板里的一个滤镜（V3.0 / D6）。</summary>
public sealed class TemplateFilter
{
    public string Name { get; set; } = "";
    /// <summary>滤镜种类 id（obs-websocket filterKind），如 <c>noise_suppress_filter</c>。</summary>
    public string Kind { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public JsonObject? Settings { get; set; }
}

public sealed class TransformSpec
{
    public double? PosX { get; set; }
    public double? PosY { get; set; }
    public double? ScaleX { get; set; }
    public double? ScaleY { get; set; }
    /// <summary>obs-websocket 字符串枚举：OBS_BOUNDS_NONE / STRETCH / SCALE_INNER / SCALE_OUTER / TO_WIDTH / TO_HEIGHT / MAX_ONLY。</summary>
    public string? BoundsType { get; set; }
    public double? BoundsWidth { get; set; }
    public double? BoundsHeight { get; set; }
    /// <summary>对齐：0 正中，5 左上，6 右上，9 左下，10 右下 等。</summary>
    public int? Alignment { get; set; }

    // ---------------- 裁剪与旋转（V3.0 第三轮验证补）----------------
    //
    // 为什么必须补：D6「我的模板」写明的动机就是「摄像头裁剪、滤镜、降噪换机不重配」，
    // 而模型里没有裁剪/旋转 → 每次捕获都静默丢掉「Alt+拖动裁掉黑边」这件事，
    // 落地后画面回正、黑边回来，且状态是成功（用户只能自己发现）。
    public double? CropLeft { get; set; }
    public double? CropTop { get; set; }
    public double? CropRight { get; set; }
    public double? CropBottom { get; set; }
    /// <summary>旋转角度（度）。</summary>
    public double? Rotation { get; set; }
}

public sealed class PlaceholderSpec
{
    /// <summary>device | file | url | window | text</summary>
    public string Kind { get; set; } = "";
    public string Hint { get; set; } = "";
}
