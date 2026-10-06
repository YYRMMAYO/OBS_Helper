using System.Text.Json;
using System.Text.Json.Nodes;
using OBS_Helper.Wpf.Models;
using OBS_Helper.Wpf.Models.ObsConfig;
using OBS_Helper.Wpf.Services;
using OBS_Helper.Wpf.Services.Knowledge;
using OBS_Helper.Wpf.Services.ObsConfig;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// 「我的模板」反向捕获核心（V3.0 / D6）的回归测试。
///
/// 这一项的价值全在**换机后还能用**，因此三条最容易出错、且错了不会报错的规则各有一组断言：
/// <list type="number">
///   <item>机器相关设置（设备 id / 本地文件 / 窗口）必须被剔除，换成占位提示；</item>
///   <item>跨场景复用的来源只能带一次设置（第二次走 CreateSceneItem）；</item>
///   <item>层级必须换算正确（OBS 面板自上而下 vs 模板 0 = 最底）。</item>
/// </list>
/// </summary>
public class SceneTemplateCaptureCoreTests
{
    private static CapturedItem Item(string name, string kind, int z, JsonObject? settings = null,
        bool enabled = true, IReadOnlyList<TemplateFilter>? filters = null)
        => new(name, kind, enabled, null, settings, filters ?? Array.Empty<TemplateFilter>(), z);

    private static CapturedCollection Collection(params CapturedScene[] scenes)
        => new("我的直播间", new CapturedCanvas(1920, 1080, 1920, 1080, 30, 1), scenes);

    // ---------------------------------------------------------------- 占位推断

    [Theory]
    [InlineData("dshow_input", "device")]
    [InlineData("wasapi_input_capture", "device")]
    [InlineData("av_capture_input", "device")]
    [InlineData("ffmpeg_source", "file")]
    [InlineData("image_source", "file")]
    [InlineData("vlc_source", "file")]
    [InlineData("browser_source", "url")]
    [InlineData("window_capture", "window")]
    [InlineData("game_capture", "window")]
    [InlineData("display_capture", "window")]
    [InlineData("text_gdiplus_v3", "text")]
    public void InferPlaceholder_MapsKnownKinds(string kind, string expected)
        => Assert.Equal(expected, SceneTemplateCaptureCore.InferPlaceholder(kind)?.Kind);

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("some_future_kind")]
    public void InferPlaceholder_UnknownReturnsNull(string? kind)
        => Assert.Null(SceneTemplateCaptureCore.InferPlaceholder(kind));

    // ---------------------------------------------------------------- 设置清洗

    /// <summary>机器相关的键必须剔除：带过去只会让落地「成功但没画面」，比没有更糟。</summary>
    [Fact]
    public void SanitizeSettings_DropsMachineSpecificKeys()
    {
        var input = new JsonObject
        {
            ["device_id"] = "USB\\VID_1234",
            ["device_name"] = "罗技 C920",
            ["local_file"] = @"D:\bgm.mp3",
            ["playlist"] = new JsonArray { "a.mp3" },
            ["window"] = "记事本",
            ["resolution"] = "1920x1080",
            ["custom_key"] = 42,
        };

        var clean = SceneTemplateCaptureCore.SanitizeSettings(input);

        Assert.NotNull(clean);
        Assert.False(clean!.ContainsKey("device_id"));
        Assert.False(clean.ContainsKey("device_name"));
        Assert.False(clean.ContainsKey("local_file"));
        Assert.False(clean.ContainsKey("playlist"));
        Assert.False(clean.ContainsKey("window"));
        Assert.Equal("1920x1080", (string)clean["resolution"]!);
        Assert.Equal(42, (int)clean["custom_key"]!);
    }

    [Fact]
    public void SanitizeSettings_AllMachineSpecificBecomesNull()
    {
        var input = new JsonObject { ["device_id"] = "x", ["local_file"] = "y" };
        Assert.Null(SceneTemplateCaptureCore.SanitizeSettings(input));
        Assert.Null(SceneTemplateCaptureCore.SanitizeSettings(null));
    }

    /// <summary>清洗是深拷贝：同一个设置对象被两个来源引用时不能因为共享节点而抛异常。</summary>
    [Fact]
    public void SanitizeSettings_ProducesIndependentCopies()
    {
        var input = new JsonObject { ["nested"] = new JsonObject { ["a"] = 1 } };

        var first = SceneTemplateCaptureCore.SanitizeSettings(input);
        var second = SceneTemplateCaptureCore.SanitizeSettings(input);

        Assert.NotNull(first);
        Assert.NotNull(second);
        // 两个结果都能挂到同一个父节点下（不做深拷贝时第二次会抛 InvalidOperationException）
        var parent = new JsonObject { ["one"] = first, ["two"] = second };
        Assert.Equal(2, parent.Count);
    }

    // ---------------------------------------------------------------- 层级换算

    /// <summary>模板里 0 必须是最底：分层弄反会让整场画面层级颠倒。</summary>
    [Fact]
    public void Build_AssignsZOrderBottomFirst()
    {
        // CapturedItem.ZOrder 已是「0 = 最底」口径
        var template = SceneTemplateCaptureCore.Build(Collection(
            new CapturedScene("主场景", null, null, new[]
            {
                Item("底部背景", "image_source", 0),
                Item("中间画面", "game_capture", 1),
                Item("上层摄像头", "dshow_input", 2),
            })), "测试", "t1");

        var sources = template.Scenes[0].Sources;
        Assert.Equal(new[] { "底部背景", "中间画面", "上层摄像头" }, sources.Select(s => s.Name));
        Assert.Equal(new[] { 0, 1, 2 }, sources.Select(s => s.ZOrder));
    }

    // ---------------------------------------------------------------- 跨场景复用

    [Fact]
    public void Build_MarksSharedInputsAndOnlyKeepsSettingsOnce()
    {
        var micSettings = new JsonObject { ["volume"] = 1.0 };
        var filters = new[] { new TemplateFilter { Name = "降噪", Kind = "noise_suppress_filter" } };

        var template = SceneTemplateCaptureCore.Build(Collection(
            new CapturedScene("开场", null, null, new[] { Item("麦克风", "wasapi_input_capture", 0, micSettings, filters: filters) }),
            new CapturedScene("游戏", null, null, new[] { Item("麦克风", "wasapi_input_capture", 0, micSettings, filters: filters) })),
            "测试", "t1");

        var first = template.Scenes[0].Sources[0];
        var second = template.Scenes[1].Sources[0];

        Assert.False(first.Shared);
        Assert.NotNull(first.Settings);
        Assert.Single(first.Filters);

        // 第二次出现：只引用，不带设置与滤镜（否则 OBS 会报重名或重复占设备）
        Assert.True(second.Shared);
        Assert.Null(second.Settings);
        Assert.Empty(second.Filters);
    }

    // ---------------------------------------------------------------- 模板元数据

    [Fact]
    public void Build_UsesCollectionNameWhenTitleMissing()
    {
        var template = SceneTemplateCaptureCore.Build(Collection(
            new CapturedScene("主场景", null, null, new[] { Item("背景", "image_source", 0) })), "  ", "t1");

        Assert.Equal("我的直播间", template.Title);
        Assert.StartsWith(SceneTemplateCaptureCore.MineIdPrefix, template.Id);
        Assert.True(template.IsMine);
    }

    [Fact]
    public void Build_KeepsCanvasAndDetectsPortrait()
    {
        var portrait = new CapturedCollection("竖屏", new CapturedCanvas(1080, 1920, 1080, 1920, 60, 1),
            new[] { new CapturedScene("主", null, null, new[] { Item("背景", "image_source", 0) }) });

        var template = SceneTemplateCaptureCore.Build(portrait, "竖屏模板", "t1");

        Assert.True(template.Portrait);
        Assert.Equal(1080, template.Canvas.BaseWidth);
        Assert.Equal(1920, template.Canvas.BaseHeight);
        Assert.Equal(60, template.Canvas.FpsNumerator);
    }

    /// <summary>异常 fps（旧协议拿不到 / 值为 0）必须退回 30/1，不能写出 0 fps 的模板。</summary>
    [Fact]
    public void Build_RepairsInvalidFps()
    {
        var captured = new CapturedCollection("x", new CapturedCanvas(1920, 1080, 1920, 1080, 0, 0),
            new[] { new CapturedScene("主", null, null, new[] { Item("背景", "image_source", 0) }) });

        var template = SceneTemplateCaptureCore.Build(captured, "x", "t1");
        Assert.Equal(30, template.Canvas.FpsNumerator);
        Assert.Equal(1, template.Canvas.FpsDenominator);
    }

    [Fact]
    public void Build_CopiesSceneTransitionOverride()
    {
        var template = SceneTemplateCaptureCore.Build(Collection(
            new CapturedScene("主", "淡入淡出", 500, new[] { Item("背景", "image_source", 0) })), "x", "t1");

        Assert.Equal("淡入淡出", template.Scenes[0].Transition);
        Assert.Equal(500, template.Scenes[0].TransitionDurationMs);
    }

    // ---------------------------------------------------------------- id / slug

    [Theory]
    [InlineData("我的直播间", "我的直播间")]
    [InlineData("My  Stream", "my-stream")]
    [InlineData("a--b", "a-b")]
    [InlineData("  !!  ", "template")]
    public void MakeUniqueSuffix_SlugsTitle(string title, string expected)
        => Assert.Equal(expected, SceneTemplateCaptureCore.MakeUniqueSuffix(title, Array.Empty<string>()));

    [Fact]
    public void MakeUniqueSuffix_AvoidsCollisions()
    {
        var existing = new[] { "mine-test", "mine-test-2", "other-template" };

        var suffix = SceneTemplateCaptureCore.MakeUniqueSuffix("test", existing);

        Assert.Equal("test-3", suffix);
        Assert.DoesNotContain("mine-" + suffix, existing);
    }

    [Fact]
    public void Slug_TruncatesVeryLongTitles()
    {
        var slug = SceneTemplateCaptureCore.Slug(new string('长', 200));
        Assert.True(slug.Length <= 60);
    }

    // ---------------------------------------------------------------- 变换读取

    [Fact]
    public void ReadTransform_AllNullReturnsNull()
        => Assert.Null(SceneTemplateCaptureCore.ReadTransform(null, null, null, null, null, null, null, null));

    [Fact]
    public void ReadTransform_KeepsProvidedFields()
    {
        var t = SceneTemplateCaptureCore.ReadTransform(10, 20, 0.5, 0.5, "OBS_BOUNDS_SCALE_INNER", 1280, 720, 5);

        Assert.NotNull(t);
        Assert.Equal(10, t!.PosX);
        Assert.Equal(0.5, t.ScaleX);
        Assert.Equal("OBS_BOUNDS_SCALE_INNER", t.BoundsType);
        Assert.Equal(1280, t.BoundsWidth);
        Assert.Equal(5, t.Alignment);
    }

    // ---------------------------------------------------------------- 可用性

    [Fact]
    public void IsUsable_RequiresScenesWithSources()
    {
        Assert.False(SceneTemplateCaptureCore.IsUsable(new SceneTemplate()));

        var empty = new SceneTemplate { Scenes = { new TemplateScene { Name = "空场景" } } };
        Assert.False(SceneTemplateCaptureCore.IsUsable(empty));

        var ok = SceneTemplateCaptureCore.Build(Collection(
            new CapturedScene("主", null, null, new[] { Item("背景", "image_source", 0) })), "x", "t1");
        Assert.True(SceneTemplateCaptureCore.IsUsable(ok));
    }

    // ---------------------------------------------------------------- 落盘格式

    /// <summary>
    /// 「我的模板」存成 JSON 再读回来必须一模一样（V3.0 / D6）。
    ///
    /// 这条不是在测序列化库，而是在钉住**文件格式契约**：用户模板要能被内置模板的落地器直接吃下去，
    /// 所以 IsMine / Filters / Settings（JsonObject 嵌套）这些字段一个都不能在往返中丢掉。
    /// 落盘用的是与内置模板相同的 System.Text.Json 默认选项 + 大小写不敏感读取。
    /// </summary>
    [Fact]
    public void Template_SurvivesJsonRoundTrip()
    {
        var original = SceneTemplateCaptureCore.Build(Collection(
            new CapturedScene("主场景", "淡入淡出", 400, new[]
            {
                Item("摄像头", "dshow_input", 0,
                    new JsonObject { ["resolution"] = "1920x1080", ["buffering"] = true },
                    filters: new[]
                    {
                        new TemplateFilter
                        {
                            Name = "降噪",
                            Kind = "noise_suppress_filter",
                            Enabled = true,
                            Settings = new JsonObject { ["method"] = "rnnoise", ["suppress_level"] = -30 },
                        },
                        new TemplateFilter { Name = "色键", Kind = "chroma_key_filter_v2", Enabled = false },
                    }),
                Item("BGM", "ffmpeg_source", 1),
            })), "我的直播间模板", "roundtrip");

        var options = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        var json = System.Text.Json.JsonSerializer.Serialize(original, options);
        var restored = System.Text.Json.JsonSerializer.Deserialize<SceneTemplate>(json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(restored);
        Assert.True(restored!.IsMine);
        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.Title, restored.Title);
        Assert.Equal(original.Canvas.FpsNumerator, restored.Canvas.FpsNumerator);

        var scene = restored.Scenes[0];
        Assert.Equal("主场景", scene.Name);
        Assert.Equal("淡入淡出", scene.Transition);
        Assert.Equal(400, scene.TransitionDurationMs);
        Assert.Equal(2, scene.Sources.Count);

        var cam = scene.Sources[0];
        Assert.Equal("dshow_input", cam.InputKind);
        Assert.Equal("1920x1080", (string)cam.Settings!["resolution"]!);
        Assert.True((bool)cam.Settings!["buffering"]!);
        Assert.Equal(2, cam.Filters.Count);
        Assert.Equal("noise_suppress_filter", cam.Filters[0].Kind);
        Assert.Equal("rnnoise", (string)cam.Filters[0].Settings!["method"]!);
        Assert.Equal(-30, (int)cam.Filters[0].Settings!["suppress_level"]!);
        Assert.False(cam.Filters[1].Enabled);
        Assert.Null(cam.Filters[1].Settings);
    }
    // ---------------------------------------------------------------- 第三轮验证新增

    /// <summary>裁剪与旋转必须能进模型（该功能的动机就是「摄像头裁剪换机不重配」）。</summary>
    [Fact]
    public void ReadTransform_KeepsCropAndRotation()
    {
        var t = SceneTemplateCaptureCore.ReadTransform(
            10, 20, 1, 1, null, null, null, 5,
            cropLeft: 4, cropTop: 8, cropRight: 12, cropBottom: 16, rotation: 90);

        Assert.NotNull(t);
        Assert.Equal(4, t!.CropLeft);
        Assert.Equal(8, t.CropTop);
        Assert.Equal(12, t.CropRight);
        Assert.Equal(16, t.CropBottom);
        Assert.Equal(90, t.Rotation);
    }

    /// <summary>只有裁剪（没有位置/缩放）时也要生成 Transform —— 否则「只裁了一下」这件事会被整条丢掉。</summary>
    [Fact]
    public void ReadTransform_CropAloneIsEnough()
    {
        var t = SceneTemplateCaptureCore.ReadTransform(
            null, null, null, null, null, null, null, null, cropLeft: 5, cropTop: 0, cropRight: 0, cropBottom: 0);

        Assert.NotNull(t);
        Assert.Equal(5, t!.CropLeft);
    }

    /// <summary>读不出来的来源要有显式占位（分组展不开、类型读不到），不能静默留一个空 kind。</summary>
    [Fact]
    public void Build_UnsupportedItemGetsExplicitPlaceholder()
    {
        var item = new CapturedItem("分组 1", "", true, null, null,
            Array.Empty<TemplateFilter>(), 0, UnsupportedReason: "分组没能展开");

        var template = SceneTemplateCaptureCore.Build(Collection(
            new CapturedScene("主场景", null, null, new[] { item })), "测试", "t1");

        var src = template.Scenes[0].Sources[0];
        Assert.NotNull(src.Placeholder);
        Assert.Equal("manual", src.Placeholder!.Kind);
        Assert.Contains("分组没能展开", src.Placeholder.Hint);
    }

    /// <summary>机器相关键必须覆盖真实键名（dshow 用 video_device_id，image_source 用 file）。</summary>
    [Fact]
    public void SanitizeSettings_DropsRealDeviceAndFileKeys()
    {
        var input = new JsonObject
        {
            ["video_device_id"] = "USB\\VID_1",
            ["audio_device_id"] = "麦克风阵列",
            ["file"] = @"D:\pic.png",
            ["files"] = new JsonArray { @"D:\a.png" },
            ["text_file"] = @"D:\note.txt",
            ["color"] = 123,
        };

        var clean = SceneTemplateCaptureCore.SanitizeSettings(input);

        Assert.NotNull(clean);
        Assert.False(clean!.ContainsKey("video_device_id"));
        Assert.False(clean.ContainsKey("audio_device_id"));
        Assert.False(clean.ContainsKey("file"));
        Assert.False(clean.ContainsKey("files"));
        Assert.False(clean.ContainsKey("text_file"));
        Assert.Equal(123, (int)clean["color"]!);
    }

    /// <summary>本地条目里的 null 数组必须被归一化成空集合（否则详情页与全库检索都会抛）。</summary>
    [Fact]
    public void Merge_NormalizesNullCollections()
    {
        var mine = new List<Problem>
        {
            new() { Id = "my-null", Title = "字段是 null 的条目", Symptoms = null!, Causes = null!, Tips = null!,
                    Platforms = null!, Steps = null!, Links = null! },
        };

        var merged = MyKnowledgeBaseCore.Merge(new ProblemData { Version = "2.2" }, mine, "我的条目");
        var p = merged.Problems.Single(x => x.Id == "my-null");

        Assert.Empty(p.Symptoms);
        Assert.Empty(p.Causes);
        Assert.Empty(p.Tips);
        Assert.Empty(p.Platforms);
        Assert.Empty(p.Steps);
        Assert.Empty(p.Links);
        // 归一化之后的条目要能安全地参与检索文本拼接（null 数组最危险的爆点就在那里）
        var searchText = string.Join(" ", new[] { p.Title, "分类" }
            .Concat(p.Symptoms).Concat(p.Causes).Concat(p.Tips).Concat(p.Platforms)
            .Concat(p.Steps.Select(s => s.Title + " " + s.Detail)));
        Assert.False(string.IsNullOrWhiteSpace(searchText));
    }

    /// <summary>Merge 不得改动入参对象（契约写的是「返回新对象」）。</summary>
    [Fact]
    public void Merge_DoesNotMutateInput()
    {
        var entry = new Problem { Id = "my-x", Title = "条目", Category = "不存在的分类" };

        MyKnowledgeBaseCore.Merge(new ProblemData { Version = "2.2" }, new[] { entry }, "我的条目");

        Assert.Equal("不存在的分类", entry.Category);
        Assert.False(entry.IsLocal);
    }

    /// <summary>没有任何条目录入保留分类时不该凭空多出一个空分类（首页会出现 0 条的空卡片）。</summary>
    [Fact]
    public void Merge_DoesNotCreateEmptyMineCategory()
    {
        var baseline = new ProblemData
        {
            Version = "2.2",
            Categories = new List<Category> { new() { Id = "video", Title = "画面" } },
        };
        var mine = new[] { new Problem { Id = "my-v", Title = "用合法分类", Category = "video" } };

        var merged = MyKnowledgeBaseCore.Merge(baseline, mine, "我的条目");

        Assert.DoesNotContain(merged.Categories, c => c.Id == MyKnowledgeBaseCore.MineCategoryId);
    }

    /// <summary>本地文件里自建的分类要被并进来，否则用户写的 categories 形同不存在。</summary>
    [Fact]
    public void Merge_AcceptsUserDefinedCategories()
    {
        var baseline = new ProblemData { Version = "2.2" };
        var mine = new[] { new Problem { Id = "my-hw", Title = "我的硬件笔记", Category = "my-hw" } };
        var cats = new List<Category> { new() { Id = "my-hw", Title = "我的硬件" } };

        var merged = MyKnowledgeBaseCore.Merge(baseline, mine, "我的条目", cats);

        Assert.Contains(merged.Categories, c => c.Id == "my-hw" && c.Title == "我的硬件");
        Assert.Equal("my-hw", merged.Problems.Single(p => p.Id == "my-hw").Category);
    }
    // ------------------------------------------------ 第四轮验证：crop 字段形状（曾整条白修）

    /// <summary>
    /// v5 的裁剪是**扁平**字段 cropLeft/cropTop/cropRight/cropBottom。
    /// 上一版写成嵌套 crop{...}，obs-websocket 直接忽略未知字段 → 「摄像头裁剪」落地后消失且返回成功。
    /// </summary>
    [Fact]
    public void BuildV5Transform_UsesFlatCropFields()
    {
        var tf = SceneTemplateCaptureCore.BuildV5Transform(new TransformSpec
        {
            PosX = 10, PosY = 20, Alignment = 5,
            CropLeft = 4, CropTop = 8, CropRight = 12, CropBottom = 16, Rotation = 90,
        });

        Assert.Equal(4.0, (double)tf["cropLeft"]!);
        Assert.Equal(8.0, (double)tf["cropTop"]!);
        Assert.Equal(12.0, (double)tf["cropRight"]!);
        Assert.Equal(16.0, (double)tf["cropBottom"]!);
        Assert.Equal(90.0, (double)tf["rotation"]!);
        Assert.False(tf.ContainsKey("crop"));       // 嵌套形态会被静默忽略
    }

    /// <summary>v4 反过来要**嵌套** crop 对象 —— 与 v5 的扁平形状形成对照，两边都有断言。</summary>
    [Fact]
    public void BuildV4TransformFields_UsesNestedCrop()
    {
        var tf = SceneTemplateCaptureCore.BuildV4TransformFields(new TransformSpec
        {
            PosX = 10, PosY = 20, Alignment = 5,
            CropLeft = 4, CropTop = 8, CropRight = 12, CropBottom = 16, Rotation = 45,
        });

        var crop = Assert.IsType<JsonObject>(tf["crop"]);
        Assert.Equal(4.0, (double)crop["left"]!);
        Assert.Equal(8.0, (double)crop["top"]!);
        Assert.Equal(12.0, (double)crop["right"]!);
        Assert.Equal(16.0, (double)crop["bottom"]!);
        Assert.Equal(45.0, (double)tf["rotation"]!);
    }

    /// <summary>用**真实形状的 v5 响应**（扁平，不是直接调参）验证捕获侧读得到裁剪。</summary>
    [Fact]
    public void ReadV5Transform_ReadsFlatCropFromResponse()
    {
        var response = JsonDocument.Parse("""
        {
          "sceneItemTransform": {
            "positionX": 10.5, "positionY": 20.5,
            "scaleX": 1.5, "scaleY": 1.5,
            "alignment": 5, "rotation": 90.0,
            "cropLeft": 4, "cropTop": 8, "cropRight": 12, "cropBottom": 16,
            "boundsType": "OBS_BOUNDS_SCALE_INNER", "boundsWidth": 1920, "boundsHeight": 1080
          }
        }
        """).RootElement;

        var spec = SceneTemplateCaptureCore.ReadV5SceneItemTransform(response);

        Assert.NotNull(spec);
        Assert.Equal(10.5, spec!.PosX);
        Assert.Equal(4.0, spec.CropLeft);
        Assert.Equal(16.0, spec.CropBottom);
        Assert.Equal(90.0, spec.Rotation);
        Assert.Equal("OBS_BOUNDS_SCALE_INNER", spec.BoundsType);
    }

    /// <summary>裁剪全为 0（用户没裁）时也不该丢掉「有变换」这个事实。</summary>
    [Fact]
    public void ReadV5Transform_ZeroCropStillProducesTransform()
    {
        var response = JsonDocument.Parse("""
        { "sceneItemTransform": { "positionX": 1, "cropLeft": 0, "cropTop": 0, "cropRight": 0, "cropBottom": 0 } }
        """).RootElement;

        var spec = SceneTemplateCaptureCore.ReadV5SceneItemTransform(response);

        Assert.NotNull(spec);
        Assert.Equal(0.0, spec!.CropLeft);
    }

    /// <summary>响应里没有变换对象时返回 null（调用方据此记说明，而不是当作「全 0 的变换」）。</summary>
    [Fact]
    public void ReadV5SceneItemTransform_MissingObjectReturnsNull()
        => Assert.Null(SceneTemplateCaptureCore.ReadV5SceneItemTransform(
            JsonDocument.Parse("""{ "sourceName": "摄像头" }""").RootElement));

    // ------------------------------------------------ 第四轮验证：深拷贝必须保全所有字段

    /// <summary>
    /// <c>Copy</c> 漏字段是**深拷贝引入的回归**：Severity 被丢掉后，用户写的「严重」
    /// 会静默回落成默认「常见」，药丸配色、排序与 AI 分级全跟着偏。
    /// 这里逐字段核对，避免以后再漏。
    /// </summary>
    [Fact]
    public void Copy_PreservesEveryField()
    {
        var src = new Problem
        {
            Id = "my-1", Category = "video", Title = "标题", Severity = "严重",
            Platforms = new[] { "Windows" }, Symptoms = new[] { "花屏" }, Causes = new[] { "线材" },
            Tips = new[] { "换线" }, Related = new[] { "p2" }, Synonyms = new[] { "马赛克" },
            Steps = new List<Step> { new() { Title = "步骤1", Detail = "细节", Level = "easy" } },
            Links = new List<Link> { new() { Title = "链接", Url = "https://example.com" } },
            IsLocal = true,
        };

        var copy = MyKnowledgeBaseCore.Copy(src);

        Assert.Equal(src.Id, copy.Id);
        Assert.Equal(src.Category, copy.Category);
        Assert.Equal(src.Title, copy.Title);
        Assert.Equal("严重", copy.Severity);               // ← 曾经丢掉
        Assert.Equal(src.Platforms, copy.Platforms);
        Assert.Equal(src.Symptoms, copy.Symptoms);
        Assert.Equal(src.Causes, copy.Causes);
        Assert.Equal(src.Tips, copy.Tips);
        Assert.Equal(src.Related, copy.Related);
        Assert.Equal(src.Synonyms, copy.Synonyms);
        Assert.Equal(src.Steps[0].Title, copy.Steps[0].Title);
        Assert.Equal(src.Steps[0].Detail, copy.Steps[0].Detail);
        Assert.Equal(src.Steps[0].Level, copy.Steps[0].Level);
        Assert.Equal(src.Links[0].Url, copy.Links[0].Url);
        Assert.True(copy.IsLocal);
    }

    /// <summary>合并后严重度不能被改写（端到端地看这件事）。</summary>
    [Fact]
    public void Merge_KeepsLocalSeverity()
    {
        var mine = new[] { new Problem { Id = "my-s", Title = "严重问题", Severity = "严重" } };

        var merged = MyKnowledgeBaseCore.Merge(new ProblemData { Version = "2.2" }, mine, "我的条目");

        Assert.Equal("严重", merged.Problems.Single(p => p.Id == "my-s").Severity);
    }
}