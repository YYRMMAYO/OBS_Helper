using System.Text.Json;
using OBS_Helper.Wpf.Models;
using OBS_Helper.Wpf.Services.Search;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// 基于**真实知识库**（<c>Assets/problems.json</c>，212 条）的检索端到端检查（V3.0 / C4）。
///
/// 为什么要有这一层：纯函数的单测只能证明「算法对」，证明不了「接到真数据上真的能查到」——
/// 而拼音首字母与同义词这两条路径**完全依赖数据**（标题里有没有那两个字、条目有没有别名）。
/// 知识库换一版、标题改个词，功能就可能静默失效；这里用真实文件把它钉住。
/// </summary>
public class SearchOnRealKnowledgeBaseTests
{
    private static List<SearchableProblem> LoadRealProblems()
    {
        var path = Path.Combine(SourceRoot(), "OBS_Helper.Wpf", "Assets", "problems.json");
        Assert.True(File.Exists(path), $"知识库文件缺失：{path}");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var problems = new List<SearchableProblem>();

        // 分类标题：id → 标题
        var categories = new Dictionary<string, string>(StringComparer.Ordinal);
        if (doc.RootElement.TryGetProperty("categories", out var cats))
        {
            foreach (var c in cats.EnumerateArray())
                categories[c.GetProperty("id").GetString() ?? ""] = c.GetProperty("title").GetString() ?? "";
        }

        foreach (var p in doc.RootElement.GetProperty("problems").EnumerateArray())
        {
            var id = p.GetProperty("id").GetString() ?? "";
            var category = p.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "";
            problems.Add(new SearchableProblem(
                Problem: new Problem { Id = id, Title = p.GetProperty("title").GetString() ?? "" },
                Title: p.GetProperty("title").GetString() ?? "",
                Category: categories.TryGetValue(category, out var t) ? t : "",
                Symptoms: Strings(p, "symptoms"),
                Causes: Strings(p, "causes"),
                Steps: p.TryGetProperty("steps", out var steps) && steps.ValueKind == JsonValueKind.Array
                    ? steps.EnumerateArray()
                        .Select(s => (s.TryGetProperty("title", out var st) ? st.GetString() : "") + " " +
                                     (s.TryGetProperty("detail", out var sd) ? sd.GetString() : ""))
                        .ToList()
                    : new List<string>(),
                Tips: Strings(p, "tips"),
                Synonyms: Strings(p, "synonyms")));
        }

        Assert.True(problems.Count > 100, $"知识库条目太少（{problems.Count}），测试前提不成立");
        return problems;

        static List<string> Strings(JsonElement p, string name)
            => p.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array
                ? arr.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToList()
                : new List<string>();
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

    /// <summary>拼音首字母查询必须能在真实知识库里查到条目（`hp` → 黑屏相关）。</summary>
    [Theory]
    [InlineData("hp", "黑屏")]
    [InlineData("dz", "掉帧")]
    [InlineData("kd", "卡顿")]
    public void PinyinQuery_FindsEntriesInRealKnowledgeBase(string pinyin, string expectedWord)
    {
        var problems = LoadRealProblems();
        var hits = SearchQueryCore.Rank(problems, pinyin, 10);

        Assert.NotEmpty(hits);
        Assert.Contains(hits, h => h.Problem.Title.Contains(expectedWord, StringComparison.Ordinal));
    }

    /// <summary>口语同义词必须命中书面词条目（「落帧」→ 掉帧）。</summary>
    [Fact]
    public void ColloquialSynonym_FindsWrittenEntryInRealKnowledgeBase()
    {
        var problems = LoadRealProblems();
        var hits = SearchQueryCore.Rank(problems, "落帧", 10);

        Assert.NotEmpty(hits);
        Assert.Contains(hits, h => h.Problem.Title.Contains("掉帧", StringComparison.Ordinal));
    }

    /// <summary>数据侧别名（<c>synonyms</c>）必须真的生效：用只出现在别名里的词去查。</summary>
    [Fact]
    public void DataProvidedSynonym_FindsItsEntry()
    {
        var problems = LoadRealProblems();

        // 「沙沙声」只出现在 au-mic-noise 的 synonyms 里（标题与症状都不含这三个字）
        Assert.DoesNotContain(problems, p => p.Title.Contains("沙沙声", StringComparison.Ordinal)
                                          || p.Symptoms.Any(s => s.Contains("沙沙声", StringComparison.Ordinal)));

        var hits = SearchQueryCore.Rank(problems, "沙沙声", 10);
        Assert.Contains(hits, h => h.Problem.Id == "au-mic-noise");
    }

    /// <summary>带空格的多词查询在真数据上也能命中（过去整串 Contains 必然失败）。</summary>
    [Fact]
    public void MultiWordQuery_WorksOnRealData()
    {
        var problems = LoadRealProblems();
        Assert.NotEmpty(SearchQueryCore.Rank(problems, "推流 编码器", 10));
    }

    /// <summary>查不到时给建议，而不是空列表（真数据上也要成立）。</summary>
    [Fact]
    public void NonsenseQuery_StillReturnsSuggestions()
    {
        var problems = LoadRealProblems();
        var suggestions = SearchQueryCore.Suggest(problems, "黑屏闪一下", 3);

        Assert.NotEmpty(suggestions);
        Assert.Equal(3, suggestions.Count);
    }

    /// <summary>每一条问题都必须能被自己的标题查到（「搜得到自己」是最低要求）。</summary>
    [Fact]
    public void EveryProblem_IsFoundByItsOwnTitle()
    {
        var problems = LoadRealProblems();
        var missed = new List<string>();

        foreach (var p in problems)
        {
            var hits = SearchQueryCore.Rank(problems, p.Title, 5);
            if (hits.Count == 0 || !hits.Any(h => h.Problem.Id == p.Problem.Id)) missed.Add(p.Title);
        }

        Assert.True(missed.Count == 0, $"用自己的标题搜不到的条目：{string.Join(" / ", missed.Take(5))}");
    }
}
