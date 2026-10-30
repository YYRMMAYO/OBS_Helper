using System.Text.Json;
using System.Text.Json.Nodes;
using OBS_Helper.Wpf.Models.ObsConfig;
using OBS_Helper.Wpf.Services.Live;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// 虚拟直播间（V3.3.0 / P0-2）：overlay 页面安全、URL 拼接、浏览器源设置、检查清单与模板校验。
///
/// 这些规则的错误形态都是「静默」的：页面拼错 → OBS 里一片空白；token 校验写松 → 变成目录穿越；
/// 检查清单漏项 → 用户以为配好了、开播才发现没麦。所以全部钉在单测里。
/// </summary>
public class RoomOverlayCoreTests
{
    // ---------------------------------------------------------------- 页面白名单

    [Theory]
    [InlineData("chat.html")]
    [InlineData("frame.html?theme=cream")]
    [InlineData("BRB.HTML")]
    [InlineData("timer.html?minutes=10&theme=star")]
    public void KnownPages_AreAccepted(string page)
        => Assert.True(RoomOverlayCore.IsKnownPage(page), page);

    [Theory]
    [InlineData("")]
    [InlineData("index.html")]
    [InlineData("../chat.html")]
    [InlineData("/chat.html")]
    [InlineData(@"C:\chat.html")]
    [InlineData("http://evil/chat.html")]
    [InlineData("chat/../chat.html")]
    [InlineData("chat.html;rm -rf")]
    public void UnsafePages_AreRejected(string page)
        => Assert.False(RoomOverlayCore.IsKnownPage(page), page);

    [Fact]
    public void PageNameOf_StripsQuery()
    {
        Assert.Equal("frame.html", RoomOverlayCore.PageNameOf("frame.html?theme=cream"));
        Assert.Equal("", RoomOverlayCore.PageNameOf(null));
    }

    [Theory]
    [InlineData("theme=cream", true)]
    [InlineData("minutes=10&theme=star", true)]
    [InlineData("a=<script>", false)]
    [InlineData("a=\"x\"", false)]
    [InlineData(@"a=\b", false)]
    public void Query_IsValidated(string query, bool expected)
        => Assert.Equal(expected, RoomOverlayCore.IsSafeQuery(query));

    // ---------------------------------------------------------------- URL

    [Fact]
    public void BuildUrl_IncludesTokenAndLanguage()
    {
        var url = RoomOverlayCore.BuildUrl("http://127.0.0.1:5000/", "tok123", "chat.html", "en-US");

        Assert.Equal("http://127.0.0.1:5000/tok123/chat.html?lang=en", url);
    }

    [Fact]
    public void BuildUrl_KeepsTemplateQueryAndAppendsLanguage()
    {
        var url = RoomOverlayCore.BuildUrl("http://127.0.0.1:5000", "t", "frame.html?theme=cream", "zh-Hans");

        Assert.Equal("http://127.0.0.1:5000/t/frame.html?theme=cream&lang=zh", url);
    }

    [Fact]
    public void BuildUrl_RejectsUnknownPageOrMissingToken()
    {
        Assert.Null(RoomOverlayCore.BuildUrl("http://127.0.0.1:5000/", "t", "evil.html"));
        Assert.Null(RoomOverlayCore.BuildUrl("http://127.0.0.1:5000/", "", "chat.html"));
        Assert.Null(RoomOverlayCore.BuildUrl("", "t", "chat.html"));
    }

    // ---------------------------------------------------------------- 浏览器源设置

    [Fact]
    public void BrowserSettings_ClampSizeAndCarryCss()
    {
        var spec = new BrowserSpec { Page = "chat.html", Width = 10, Height = 90000, Css = "body{margin:0}", RerouteAudio = true };
        var settings = RoomOverlayCore.BuildBrowserSettings(spec, "http://127.0.0.1:1/t/chat.html");

        Assert.Equal("http://127.0.0.1:1/t/chat.html", settings["url"]!.GetValue<string>());
        Assert.Equal(64, settings["width"]!.GetValue<int>());
        Assert.Equal(4320, settings["height"]!.GetValue<int>());
        Assert.Equal("body{margin:0}", settings["css"]!.GetValue<string>());
        Assert.True(settings["reroute_audio"]!.GetValue<bool>());
    }

    // ---------------------------------------------------------------- 检查清单

    [Fact]
    public void Checklist_ListsOnlyRolesPresent_AndKeepsRequiredFlags()
    {
        var template = BuildTemplate(new[]
        {
            Source("虚拟形象", "avatar"),
            Source("聊天框", "chat"),
            Source("我的麦克风", "audio"),
            Source("装饰边框", "frame")
        });

        var items = RoomOverlayCore.BuildChecklist(template);

        Assert.Equal(new[] { "avatar", "chat", "audio", "frame", "hotkey", "preflight" },
            items.Select(i => i.Role).ToArray());
        Assert.True(items.Single(i => i.Role == "avatar").Required);
        Assert.True(items.Single(i => i.Role == "audio").Required);
        Assert.False(items.Single(i => i.Role == "chat").Required);
        Assert.All(items, i => Assert.Contains("room.check.", i.TitleKey));
    }

    [Fact]
    public void Checklist_EmptyForNullTemplate()
        => Assert.Empty(RoomOverlayCore.BuildChecklist(null));

    // ---------------------------------------------------------------- 模板校验

    [Fact]
    public void Validate_AcceptsHealthyTemplate()
    {
        var t = BuildTemplate(new[] { Source("形象", "avatar"), Source("聊天框", "chat") });
        Assert.Empty(RoomOverlayCore.Validate(t));
    }

    [Fact]
    public void Validate_FlagsMissingAvatar_ForVtuberTemplates()
    {
        var t = BuildTemplate(new[] { Source("聊天框", "chat") }, tags: new[] { "vtuber" });
        Assert.Contains(RoomOverlayCore.Validate(t), i => i.Code == "ROOM_NO_AVATAR");
    }

    [Fact]
    public void Validate_FlagsDuplicateScenesAndBadPages()
    {
        var t = BuildTemplate(new[]
        {
            Source("形象", "avatar"),
            new TemplateSource
            {
                Name = "聊天框",
                InputKind = "browser_source",
                Role = "chat",
                Browser = new BrowserSpec { Page = "http://evil/chat.html" }
            }
        });
        t.Scenes.Add(new TemplateScene { Name = t.Scenes[0].Name });

        var issues = RoomOverlayCore.Validate(t);
        Assert.Contains(issues, i => i.Code == "ROOM_DUPLICATE_SCENE");
        Assert.Contains(issues, i => i.Code == "ROOM_BAD_PAGE");
    }

    [Fact]
    public void Validate_FlagsFarOutOfCanvasTransforms()
    {
        var src = Source("形象", "avatar");
        src.Transform = new TransformSpec { PosX = 99999, PosY = 0 };
        var t = BuildTemplate(new[] { src });

        Assert.Contains(RoomOverlayCore.Validate(t), i => i.Code == "ROOM_TRANSFORM_OUT_OF_CANVAS");
    }

    [Fact]
    public void FilterByTag_IsCaseInsensitive()
    {
        var all = new[]
        {
            BuildTemplate(new[] { Source("形象", "avatar") }, tags: new[] { "vtuber", "chat" }),
            BuildTemplate(new[] { Source("游戏", "bg") }, tags: new[] { "game" })
        };

        Assert.Single(RoomOverlayCore.FilterByTag(all, "VTUBER"));
        Assert.Equal(2, RoomOverlayCore.FilterByTag(all, null).Count);
        Assert.True(RoomOverlayCore.IsVtuber(all[0]));
    }

    // ---------------------------------------------------------------- 随包模板资产

    [Fact]
    public void ShippedTemplates_ContainSixValidVtuberRooms_InBothLanguages()
    {
        var zh = LoadTemplates("scene_templates.json");
        var en = LoadTemplates("scene_templates.en-US.json");

        Assert.Equal(18, zh.Count);
        Assert.Equal(18, en.Count);
        Assert.Equal(zh.Select(t => t.Id).OrderBy(x => x), en.Select(t => t.Id).OrderBy(x => x));

        var vtuber = zh.Where(RoomOverlayCore.IsVtuber).ToList();
        Assert.Equal(6, vtuber.Count);
        Assert.Contains(vtuber, t => t.Id == "vtuber-vertical" && t.Portrait && t.Canvas.BaseWidth == 1080);

        foreach (var t in vtuber)
        {
            var issues = RoomOverlayCore.Validate(t);
            Assert.True(issues.Count == 0, $"{t.Id}: {string.Join("; ", issues.Select(i => i.Code + ":" + i.Detail))}");
            if (!t.Tags.Contains("pack"))
                Assert.Contains(t.Scenes.SelectMany(s => s.Sources), s => s.Role == "avatar");
            Assert.All(t.Scenes.SelectMany(s => s.Sources).Where(s => s.Browser is not null),
                s => Assert.True(RoomOverlayCore.IsKnownPage(s.Browser!.Page), $"{t.Id}/{s.Name}: {s.Browser!.Page}"));
        }

        // 英文版不能残留中文场景名 / 来源名（数据资产也要真翻译）
        foreach (var t in en.Where(RoomOverlayCore.IsVtuber))
        {
            foreach (var name in t.Scenes.Select(s => s.Name).Concat(t.Scenes.SelectMany(s => s.Sources).Select(s => s.Name)))
                Assert.DoesNotMatch(@"[\u4e00-\u9fff]", name);
            Assert.DoesNotMatch(@"[\u4e00-\u9fff]", t.Title + t.Summary + t.Notes);
        }
    }

    // ---------------------------------------------------------------- helpers

    private static List<SceneTemplate> LoadTemplates(string file)
    {
        var path = Path.Combine(SourceRoot(), "OBS_Helper.Wpf", "Assets", file);
        Assert.True(File.Exists(path), path);
        var list = JsonSerializer.Deserialize<List<SceneTemplate>>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(list);
        return list!;
    }

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

    private static TemplateSource Source(string name, string role) => new()
    {
        Name = name,
        InputKind = role == "audio" ? "wasapi_input_capture" : "image_source",
        Role = role
    };

    private static SceneTemplate BuildTemplate(IEnumerable<TemplateSource> sources, IEnumerable<string>? tags = null)
    {
        var t = new SceneTemplate
        {
            Id = "test-room",
            Title = "Test",
            Tags = tags?.ToList() ?? new List<string> { "vtuber" }
        };
        t.Scenes.Add(new TemplateScene { Name = "Main", Sources = sources.ToList() });
        return t;
    }
}
