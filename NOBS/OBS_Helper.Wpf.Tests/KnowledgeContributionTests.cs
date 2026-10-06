using System.Text.Json;
using OBS_Helper.Wpf.Models;
using OBS_Helper.Wpf.Services.Knowledge;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// 知识库贡献闭环（V3.0 / D8）的回归测试：本地条目合并 + 回执 Markdown。
///
/// 这一项的错误后果都是「静默」的：合并时把官方条目顶掉、用户写的东西不出现、
/// 回执里没有条目 id（维护者收到「这条没用」却不知道说的是哪条）。因此每条规则都要有断言。
/// </summary>
public class KnowledgeContributionTests
{
    private static Problem Official(string id, string title, string category = "audio")
        => new() { Id = id, Title = title, Category = category };

    private static Problem Mine(string id, string title, string category = "audio")
        => new() { Id = id, Title = title, Category = category };

    private static ProblemData Base()
        => new()
        {
            Version = "2.2",
            Updated = "2026-08-26",
            Categories = new List<Category>
            {
                new() { Id = "audio", Title = "音频" },
                new() { Id = "video", Title = "画面" },
            },
            Problems = new List<Problem> { Official("au-mic", "麦克风没声音"), Official("vi-black", "黑屏") },
        };

    // ---------------------------------------------------------------- 合并

    [Fact]
    public void Merge_NoLocalEntries_ReturnsEquivalentData()
    {
        var merged = MyKnowledgeBaseCore.Merge(Base(), null, "我的条目");

        Assert.Equal(2, merged.Problems.Count);
        Assert.Equal(2, merged.Categories.Count);
        Assert.Equal("2.2", merged.Version);
    }

    [Fact]
    public void Merge_AppendsLocalEntriesAndMarksThem()
    {
        var merged = MyKnowledgeBaseCore.Merge(Base(), new[] { Mine("my-note", "我的踩坑笔记") }, "我的条目");

        Assert.Equal(3, merged.Problems.Count);
        var mine = merged.Problems.Single(p => p.Id == "my-note");
        Assert.True(mine.IsLocal);
        Assert.False(merged.Problems.Single(p => p.Id == "au-mic").IsLocal);
    }

    /// <summary>同 id 时本地优先（用户显然是想覆盖官方那条，改错了删文件即可恢复）。</summary>
    [Fact]
    public void Merge_LocalWinsOnSameId()
    {
        var data = Base();
        var merged = MyKnowledgeBaseCore.Merge(data, new[] { Mine("au-mic", "麦克风没声音（我的修正版）") }, "我的条目");

        Assert.Equal(2, merged.Problems.Count);   // 覆盖而非追加
        var entry = merged.Problems.Single(p => p.Id == "au-mic");
        Assert.Equal("麦克风没声音（我的修正版）", entry.Title);
        Assert.True(entry.IsLocal);

        // 原数据不被改动（LoadData 之后还要按需重载）
        Assert.Equal("麦克风没声音", data.Problems.Single(p => p.Id == "au-mic").Title);
    }

    /// <summary>分类名写错不该让条目从界面上消失，而是归到保留分类。</summary>
    [Fact]
    public void Merge_UnknownCategoryFallsBackToMineCategory()
    {
        var merged = MyKnowledgeBaseCore.Merge(Base(), new[] { Mine("my-note", "我的笔记", "打错分类") }, "我的条目");

        Assert.Equal(MyKnowledgeBaseCore.MineCategoryId, merged.Problems.Single(p => p.Id == "my-note").Category);
        Assert.Contains(merged.Categories, c => c.Id == MyKnowledgeBaseCore.MineCategoryId);
    }

    [Fact]
    public void Merge_KnownCategoryIsKept()
    {
        var merged = MyKnowledgeBaseCore.Merge(Base(), new[] { Mine("my-note", "我的笔记", "video") }, "我的条目");

        Assert.Equal("video", merged.Problems.Single(p => p.Id == "my-note").Category);
    }

    /// <summary>手写 JSON 很容易复制粘贴出重复 id：只保留第一条。</summary>
    [Fact]
    public void Merge_DuplicateLocalIdsKeepFirst()
    {
        var merged = MyKnowledgeBaseCore.Merge(Base(),
            new[] { Mine("dup", "第一条"), Mine("dup", "第二条") }, "我的条目");

        Assert.Equal(3, merged.Problems.Count);
        Assert.Equal("第一条", merged.Problems.Single(p => p.Id == "dup").Title);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Merge_SkipsEntriesWithoutUsableFields(string id)
    {
        var blank = new Problem { Id = id, Title = "有标题但没 id" };
        var noTitle = new Problem { Id = "has-id", Title = "" };

        var merged = MyKnowledgeBaseCore.Merge(Base(), new[] { blank, noTitle }, "我的条目");
        Assert.Equal(2, merged.Problems.Count);   // 两条都不可用
    }

    [Fact]
    public void OverriddenIds_ReportsOnlyRealCollisions()
    {
        var overridden = MyKnowledgeBaseCore.OverriddenIds(Base(),
            new[] { Mine("au-mic", "改过的"), Mine("my-new", "新条目") });

        Assert.Equal(new[] { "au-mic" }, overridden);
        Assert.Empty(MyKnowledgeBaseCore.OverriddenIds(Base(), null));
    }

    // ---------------------------------------------------------------- 回执

    private static ProblemFeedback Feedback(bool solved, string note = "")
        => new(
            Problem: new Problem { Id = "enc-drop", Title = "输出分辨率过高导致卡顿" },
            Solved: solved,
            UserNote: note,
            KbVersion: "2.2",
            AppVersion: "3.0.0",
            UsingExternalKb: true,
            ObsVersion: "31.0.2",
            Platform: "Windows 11");

    /// <summary>标题必须带条目 id 与知识库版本：否则维护者不知道该看哪条、哪一版。</summary>
    [Fact]
    public void BuildTitle_CarriesIdAndKbVersion()
    {
        var title = ProblemFeedbackCore.BuildTitle(Feedback(solved: false));

        Assert.Contains("enc-drop", title);
        Assert.Contains("2.2", title);
    }

    [Fact]
    public void BuildTitle_DistinguishesSolvedFromUnsolved()
    {
        var solved = ProblemFeedbackCore.BuildTitle(Feedback(solved: true));
        var unsolved = ProblemFeedbackCore.BuildTitle(Feedback(solved: false));

        Assert.NotEqual(solved, unsolved);
    }

    [Fact]
    public void BuildMarkdown_ContainsProblemIdentityAndEnvironment()
    {
        var md = ProblemFeedbackCore.BuildMarkdown(Feedback(solved: false, note: "第一步就找不到那个选项"));

        Assert.Contains("知识库条目回执", md);
        Assert.Contains("enc-drop", md);
        Assert.Contains("输出分辨率过高导致卡顿", md);
        Assert.Contains("2.2", md);
        Assert.Contains("热更新版", md);
        Assert.Contains("3.0.0", md);
        Assert.Contains("31.0.2", md);
        Assert.Contains("Windows 11", md);
        Assert.Contains("第一步就找不到那个选项", md);
        Assert.Contains("未上传", md);   // 明确告诉用户：没有自动上传
    }

    /// <summary>用户补充的多行说明要逐行引用，避免 Markdown 把换行吃成一团。</summary>
    [Fact]
    public void BuildMarkdown_QuotesEveryLineOfUserNote()
    {
        var md = ProblemFeedbackCore.BuildMarkdown(Feedback(solved: false, note: "第一行\r\n第二行"));

        Assert.Contains("> 第一行", md);
        Assert.Contains("> 第二行", md);
    }

    /// <summary>
    /// 条目 id / 标题是**用户可控**的（本地知识库）：里面的换行会把回执的 Markdown 切开
    /// （裸 --- 会插一条分隔线、# 会插标题），也会把换行带进 Issue 标题（%0A）。
    /// </summary>
    [Fact]
    public void BuildMarkdown_FoldsNewlinesInProblemIdentity()
    {
        var feedback = new ProblemFeedback(
            Problem: new Problem { Id = "my-x", Title = "第一行\n---\n# 第二行" },
            Solved: false, UserNote: "", KbVersion: "2.2", AppVersion: "3.0.0",
            UsingExternalKb: false, ObsVersion: "", Platform: "");

        var md = ProblemFeedbackCore.BuildMarkdown(feedback);
        var title = ProblemFeedbackCore.BuildTitle(feedback);

        // 标题里的换行被折叠：条目行是**一行**，不会插出分隔线 / 标题
        var lines = md.Replace("\r", "").Split('\n');
        Assert.Contains(lines, l => l.Contains("第一行 --- # 第二行"));
        Assert.DoesNotContain("# 第二行", lines);                              // 不能出现独立标题行
        Assert.DoesNotContain(lines, l => l.Contains("第一行") && l.Trim() == "---");   // 也不能把标题劈成多行

        // Issue 标题必须是单行
        Assert.DoesNotContain("\n", title);
        Assert.DoesNotContain("\r", title);
        // 原始信息不丢（只是折叠成一行）
        Assert.Contains("第一行", title);
        Assert.Contains("第二行", title);
    }

    [Fact]
    public void BuildMarkdown_NoNoteSaysSoExplicitly()
    {
        var md = ProblemFeedbackCore.BuildMarkdown(Feedback(solved: true));
        Assert.Contains("未补充说明", md);
    }

    [Fact]
    public void BuildMarkdown_SolvedAndUnsolvedDifferInConclusion()
    {
        Assert.Contains("解决了", ProblemFeedbackCore.BuildMarkdown(Feedback(solved: true)));
        Assert.Contains("没解决", ProblemFeedbackCore.BuildMarkdown(Feedback(solved: false)));
    }

    /// <summary>Issue 链接只带标题与提示，正文由用户粘贴（长正文会被 URL 截断）。</summary>
    [Fact]
    public void BuildIssueUrl_TargetsIssueFormWithEncodedTitle()
    {
        var url = ProblemFeedbackCore.BuildIssueUrl("YYRMMAYO/OBS_Helper", Feedback(solved: false));

        Assert.StartsWith("https://github.com/YYRMMAYO/OBS_Helper/issues/new?", url);
        Assert.Contains("title=", url);
        Assert.Contains("labels=knowledge-base", url);
        Assert.DoesNotContain(" ", url);              // 已转义
        Assert.Contains("enc-drop", Uri.UnescapeDataString(url));
    }

    // ---------------------------------------------------------------- 本地文件 schema

    /// <summary>
    /// 本地文件的 schema 必须与知识库同一份：<c>IsLocal</c> 是加载器置位的运行期标记，
    /// **不能写进文件**（否则用户手写的文件里会莫名多出一个字段，而他并不知道那是什么）。
    /// </summary>
    [Fact]
    public void LocalFileSchema_DoesNotSerializeRuntimeFlag()
    {
        var data = new ProblemData
        {
            Version = "1.0",
            Categories = new List<Category> { new() { Id = "mine", Title = "我的条目" } },
            Problems = new List<Problem>
            {
                new() { Id = "my-sample", Title = "示例", Category = "mine", IsLocal = true },
            },
        };

        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });

        Assert.DoesNotContain("IsLocal", json);
        Assert.DoesNotContain("isLocal", json);

        // 读回来能正常还原（本地文件与热更新文件用同一套解析）
        var back = JsonSerializer.Deserialize<ProblemData>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(back);
        Assert.Single(back!.Problems);
        Assert.Equal("my-sample", back.Problems[0].Id);
    }

    /// <summary>本地文件读的是「{problems:[...]}」与「[...]」两种写法，容错解析由服务负责；
    /// 这里钉住数组写法能直接被模型接住（手写文件的常见形态）。</summary>
    [Fact]
    public void LocalFileSchema_AcceptsBareArrayForm()
    {
        var json = """[{"id":"my-1","title":"手写条目","category":"mine"}]""";
        var list = JsonSerializer.Deserialize<List<Problem>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(list);
        Assert.Single(list!);
        Assert.Equal("手写条目", list![0].Title);
    }
}
