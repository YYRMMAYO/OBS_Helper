using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using OBS_Helper.Wpf.Localization;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// V2.9.3：随包离线内容的「中英对等」校验。
///
/// 这些是**编译器看不见、只能靠对读发现**的错误：译漏一条、中英数组长度错位、
/// 英文里残留汉字、把逻辑键（severity / level / repo / dlls）一起译了……
/// 机器可查的部分全部钉在这里；查不出来的部分（用词是否地道、OBS 菜单路径是否与英文版一致）
/// 见 docs/reviews 里的三遍人工审校记录。
///
/// 资产文件由测试工程以 <c>CopyToOutputDirectory</c> 拷到输出目录的 Assets/ 下。
/// </summary>
public class EnglishContentTests
{
    private static readonly Regex Cjk = new(@"[\u4e00-\u9fff]", RegexOptions.Compiled);
    private static readonly Regex FullWidth = new(@"[\u3000-\u303f\uff01-\uff5e]", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    private static string AssetPath(string name) => Path.Combine(AppContext.BaseDirectory, "Assets", name);

    private static T Load<T>(string name)
        => JsonSerializer.Deserialize<T>(File.ReadAllText(AssetPath(name)), Opts)!;

    private static string Raw(string name) => File.ReadAllText(AssetPath(name));

    // ------------------------------------------------------------ 文件级

    [Theory]
    [InlineData("problems.en-US.json")]
    [InlineData("plugins.en-US.json")]
    [InlineData("scene_templates.en-US.json")]
    [InlineData("troubleshooting.en-US.md")]
    public void EnglishAssets_Exist(string name) => Assert.True(File.Exists(AssetPath(name)), name);

    [Theory]
    [InlineData("problems.en-US.json")]
    [InlineData("plugins.en-US.json")]
    [InlineData("scene_templates.en-US.json")]
    [InlineData("troubleshooting.en-US.md")]
    public void EnglishAssets_ContainNoChinese(string name)
    {
        var text = Raw(name);
        Assert.False(Cjk.IsMatch(text), $"英文资产里仍有汉字：{name}");
        Assert.False(FullWidth.IsMatch(text), $"英文资产里仍有全角标点：{name}");
    }

    /// <summary>
    /// 中文资产是**老通道的契约**：文件名不能带语言后缀，内容也不能被英译误改。
    /// </summary>
    [Theory]
    [InlineData("problems.json")]
    [InlineData("plugins.json")]
    [InlineData("scene_templates.json")]
    [InlineData("troubleshooting.md")]
    public void ChineseAssets_KeepOriginalNames(string name)
        => Assert.True(File.Exists(AssetPath(name)), $"{name} 必须保持原名（raw 地址 / Release 资产名 / 本机缓存都依赖它）");

    // ------------------------------------------------------------ 知识库

    [Fact]
    public void Problems_ShapeMatchesExactly()
    {
        var zh = Load<ProblemDataShape>("problems.json");
        var en = Load<ProblemDataShape>("problems.en-US.json");

        Assert.Equal(zh.Version, en.Version);
        Assert.Equal(zh.Categories.Select(c => c.Id), en.Categories.Select(c => c.Id));
        Assert.Equal(zh.Problems.Select(p => p.Id), en.Problems.Select(p => p.Id));

        foreach (var (a, b) in zh.Categories.Zip(en.Categories))
        {
            Assert.Equal(a.Id, b.Id);
            Assert.Equal(a.Icon, b.Icon);
            Assert.Equal(a.Semantic, b.Semantic);
            Assert.NotEqual("", b.Title);
            Assert.NotEqual("", b.Description);
        }

        foreach (var (a, b) in zh.Problems.Zip(en.Problems))
        {
            // 逻辑字段一字不动
            Assert.Equal(a.Category, b.Category);
            Assert.Equal(a.Platforms, b.Platforms);
            Assert.Equal(a.Related, b.Related);

            // 展示数组逐条等长（长度不同 zip 会静默截断，这里显式钉住）
            Assert.Equal(a.Symptoms.Length, b.Symptoms.Length);
            Assert.Equal(a.Causes.Length, b.Causes.Length);
            Assert.Equal(a.Tips.Length, b.Tips.Length);
            Assert.Equal(a.Steps.Count, b.Steps.Count);
            Assert.Equal(a.Links.Count, b.Links.Count);

            Assert.Equal(a.Links.Select(l => l.Url), b.Links.Select(l => l.Url));
            Assert.NotEqual("", b.Title);
        }
    }

    /// <summary>
    /// 英文侧 severity / level 的取值必须落在 <see cref="DataValues"/> 认识的那几个串上，
    /// 否则卡片颜色与排序会退化成默认值 —— 这是「翻译看着没问题、界面却不对」的经典坑。
    /// </summary>
    [Fact]
    public void Problems_EnglishSeverityAndLevelAreRecognized()
    {
        var allowedSeverity = new[]
        {
            "Critical", "Common", "Occasional", "Advanced", "Rare",
            "Intermittent", "Basic", "Beginner", "Common tip", "Niche"
        };
        var allowedLevel = new[] { "Basic", "Advanced", "Fallback", "Note" };

        var en = Load<ProblemDataShape>("problems.en-US.json");
        foreach (var p in en.Problems)
        {
            Assert.Contains(p.Severity, allowedSeverity);
            foreach (var s in p.Steps) Assert.Contains(s.Level, allowedLevel);
        }

        // 「进阶 / 严重」这两个有语义分支的取值必须与 DataValues 的认识一致，
        // 不然详情页的强调样式与卡片配色会静默失效。
        Assert.True(DataValues.IsAdvancedLevel("Advanced"));
        Assert.Equal(DataValues.SeverityKind.Critical, DataValues.ClassifySeverity("Critical"));
        Assert.Equal(DataValues.SeverityKind.Common, DataValues.ClassifySeverity("Common"));
        Assert.Equal(DataValues.SeverityKind.Normal, DataValues.ClassifySeverity("Occasional"));
        Assert.Equal(DataValues.SeverityKind.Advanced, DataValues.ClassifySeverity("Advanced"));
    }

    // ------------------------------------------------------------ 插件目录

    [Fact]
    public void Plugins_ShapeMatchesExactly()
    {
        var zh = Load<PluginShape>("plugins.json");
        var en = Load<PluginShape>("plugins.en-US.json");

        Assert.Equal(zh.Version, en.Version);
        Assert.Equal(zh.Updated, en.Updated);
        Assert.Equal(zh.Categories.Select(c => c.Key), en.Categories.Select(c => c.Key));
        Assert.Equal(zh.Plugins.Select(p => p.Id), en.Plugins.Select(p => p.Id));

        foreach (var (a, b) in zh.Plugins.Zip(en.Plugins))
        {
            Assert.Equal(a.Category, b.Category);
            Assert.Equal(a.Url, b.Url);
            Assert.Equal(a.Repo, b.Repo);
            Assert.Equal(a.Dlls, b.Dlls);
            Assert.Equal(a.Maintain, b.Maintain);
        }
    }

    /// <summary>角标是跨语言展示值：中文「热门」对应英文必须就是 <c>Popular</c>（否则角标消失）。</summary>
    [Fact]
    public void Plugins_EnglishBadgesFollowDataValues()
    {
        var en = Load<PluginShape>("plugins.en-US.json");
        foreach (var p in en.Plugins)
        {
            if (p.Badge is null) continue;
            Assert.Contains(p.Badge, new[] { "Popular", "Recommended", "AI" });
        }
        Assert.True(DataValues.IsHotBadge("Popular"));
    }

    // ------------------------------------------------------------ 场景模板

    [Fact]
    public void SceneTemplates_ShapeMatchesExactly()
    {
        var zh = Load<List<SceneTemplateShape>>("scene_templates.json");
        var en = Load<List<SceneTemplateShape>>("scene_templates.en-US.json");

        Assert.Equal(zh.Count, en.Count);
        foreach (var (a, b) in zh.Zip(en))
        {
            Assert.Equal(a.Id, b.Id);
            Assert.Equal(a.Canvas.BaseWidth, b.Canvas.BaseWidth);
            Assert.Equal(a.Canvas.FpsNumerator, b.Canvas.FpsNumerator);
            Assert.Equal(a.Scenes.Count, b.Scenes.Count);
            Assert.Equal(a.Portrait, b.Portrait);

            foreach (var (sa, sb) in a.Scenes.Zip(b.Scenes))
            {
                Assert.Equal(sa.Sources.Count, sb.Sources.Count);
                foreach (var (xa, xb) in sa.Sources.Zip(sb.Sources))
                {
                    Assert.Equal(xa.InputKind, xb.InputKind);
                    Assert.Equal(xa.ZOrder, xb.ZOrder);
                    Assert.Equal(xa.Enabled, xb.Enabled);
                    Assert.Equal(xa.Shared, xb.Shared);
                    Assert.Equal(xa.Transform?.PosX, xb.Transform?.PosX);
                    Assert.Equal(xa.Transform?.BoundsWidth, xb.Transform?.BoundsWidth);
                }
            }
        }
    }

    /// <summary>
    /// 场景 / 来源名会被**写进 OBS**：同一个中文名必须译成同一个英文名
    /// （否则 shared 来源会被重复创建、占住摄像头），不同中文名也不能撞名。
    /// 这是唯一一处「译错会导致功能坏掉」的地方。
    /// </summary>
    [Fact]
    public void SceneTemplates_SourceNamesAreConsistentAndDistinct()
    {
        var zh = Load<List<SceneTemplateShape>>("scene_templates.json");
        var en = Load<List<SceneTemplateShape>>("scene_templates.en-US.json");

        var zhToEn = new Dictionary<string, string>(StringComparer.Ordinal);
        var enToZh = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (a, b) in zh.Zip(en))
            foreach (var (sa, sb) in a.Scenes.Zip(b.Scenes))
                foreach (var (xa, xb) in sa.Sources.Zip(sb.Sources))
                {
                    if (zhToEn.TryGetValue(xa.Name, out var prev))
                        Assert.Equal(prev, xb.Name);
                    else
                        zhToEn[xa.Name] = xb.Name;

                    if (enToZh.TryGetValue(xb.Name, out var back))
                        Assert.Equal(back, xa.Name);
                    else
                        enToZh[xb.Name] = xa.Name;
                }

        Assert.NotEmpty(zhToEn);
    }

    /// <summary>过渡名会被映射到 OBS 的过渡上，英文取值必须落在别名表认识的那几个里。</summary>
    [Fact]
    public void SceneTemplates_EnglishTitlesAndTransitionsAreUsable()
    {
        var en = Load<List<SceneTemplateShape>>("scene_templates.en-US.json");
        foreach (var t in en)
        {
            Assert.NotEqual("", t.Title);
            Assert.NotEqual("", t.Summary);
            Assert.Equal("Fade", t.Transition);
        }
    }

    // ------------------------------------------------------------ 排障指引

    /// <summary>
    /// 指引的 Markdown 结构必须与中文版一一对应：标题层级一变，页面右侧目录就乱。
    /// </summary>
    [Fact]
    public void Troubleshooting_MarkdownStructureMatches()
    {
        var zh = Raw("troubleshooting.md");
        var en = Raw("troubleshooting.en-US.md");

        static int CountHeading(string text, string prefix)
            => text.Split('\n').Count(l => l.StartsWith(prefix, StringComparison.Ordinal));

        Assert.Equal(CountHeading(zh, "# "), CountHeading(en, "# "));
        Assert.Equal(CountHeading(zh, "## "), CountHeading(en, "## "));

        static int CountFences(string text)
            => text.Split("```").Length - 1;
        Assert.Equal(0, CountFences(en) % 2);
        Assert.Equal(CountFences(zh), CountFences(en));

        // 链接 URL 不动：只译显示文字
        static IEnumerable<string> Urls(string text)
            => Regex.Matches(text, @"\]\((.*?)\)").Select(m => m.Groups[1].Value).OrderBy(x => x, StringComparer.Ordinal);
        Assert.Equal(Urls(zh), Urls(en));
    }

    // ------------------------------------------------------------ 形状 DTO

    private sealed class ProblemDataShape
    {
        public string Version { get; set; } = "";
        public List<CategoryShape> Categories { get; set; } = new();
        public List<ProblemShape> Problems { get; set; } = new();
    }

    private sealed class CategoryShape
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Icon { get; set; } = "";
        public string Description { get; set; } = "";
        public string Semantic { get; set; } = "";
    }

    private sealed class ProblemShape
    {
        public string Id { get; set; } = "";
        public string Category { get; set; } = "";
        public string Title { get; set; } = "";
        public string Severity { get; set; } = "";
        public string[] Platforms { get; set; } = Array.Empty<string>();
        public string[] Symptoms { get; set; } = Array.Empty<string>();
        public string[] Causes { get; set; } = Array.Empty<string>();
        public string[] Tips { get; set; } = Array.Empty<string>();
        public string[] Related { get; set; } = Array.Empty<string>();
        public List<StepShape> Steps { get; set; } = new();
        public List<LinkShape> Links { get; set; } = new();
    }

    private sealed class StepShape
    {
        public string Title { get; set; } = "";
        public string Detail { get; set; } = "";
        public string Level { get; set; } = "";
    }

    private sealed class LinkShape
    {
        public string Title { get; set; } = "";
        public string Url { get; set; } = "";
    }

    private sealed class PluginShape
    {
        public string Version { get; set; } = "";
        public string Updated { get; set; } = "";
        public List<PluginCategoryShape> Categories { get; set; } = new();
        public List<PluginEntryShape> Plugins { get; set; } = new();
    }

    private sealed class PluginCategoryShape
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
    }

    private sealed class PluginEntryShape
    {
        public string Id { get; set; } = "";
        public string Category { get; set; } = "";
        public string Url { get; set; } = "";
        public string Repo { get; set; } = "";
        public string? Badge { get; set; }
        public string Maintain { get; set; } = "";
        public string[] Dlls { get; set; } = Array.Empty<string>();
    }

    private sealed class SceneTemplateShape
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Summary { get; set; } = "";
        public bool Portrait { get; set; }
        public string Transition { get; set; } = "";
        public CanvasShape Canvas { get; set; } = new();
        public List<SceneShape> Scenes { get; set; } = new();
    }

    private sealed class CanvasShape
    {
        public int BaseWidth { get; set; }
        public int BaseHeight { get; set; }
        public int FpsNumerator { get; set; }
    }

    private sealed class SceneShape
    {
        public string Name { get; set; } = "";
        public List<SourceShape> Sources { get; set; } = new();
    }

    private sealed class SourceShape
    {
        public string Name { get; set; } = "";
        public string InputKind { get; set; } = "";
        public int ZOrder { get; set; }
        public bool Enabled { get; set; }
        public bool Shared { get; set; }
        public TransformShape? Transform { get; set; }
    }

    private sealed class TransformShape
    {
        public double PosX { get; set; }
        public double BoundsWidth { get; set; }
    }
}
