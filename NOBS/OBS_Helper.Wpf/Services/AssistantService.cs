using OBS_Helper.Wpf.Models;
using OBS_Helper.Wpf.Services.Search;

namespace OBS_Helper.Wpf.Services;

/// <summary>一次知识库匹配结果（问题 + 相关度评分 + 理由）。</summary>
public class AssistantMatch
{
    public Problem Problem { get; set; } = new();
    public int Score { get; set; }
    public string Reason { get; set; } = "";
}

/// <summary>助手对话服务：在知识库里做关键词匹配，返回建议问题与回答。</summary>
public class AssistantService
{
    private readonly ProblemService _problemService;

    public AssistantService(ProblemService problemService) => _problemService = problemService;

    /// <summary>
    /// 助手页的示例问句（V2.9.2）。做成属性而不是静态字段：文案取自文案表，
    /// 语言切换后要能立刻换一套，而不是把首次访问时的语言冻在这里。
    /// </summary>
    public IReadOnlyList<string> Suggestions => new[]
    {
        Strings.T("assistant.suggestion.1"),
        Strings.T("assistant.suggestion.2"),
        Strings.T("assistant.suggestion.3"),
        Strings.T("assistant.suggestion.4"),
        Strings.T("assistant.suggestion.5"),
        Strings.T("assistant.suggestion.6"),
        Strings.T("assistant.suggestion.7"),
        Strings.T("assistant.suggestion.8"),
    };

    public async Task<List<AssistantMatch>> AskAsync(string query)
    {
        var data = await _problemService.GetDataAsync();
        var q = SearchQueryCore.Normalize(query);
        if (string.IsNullOrWhiteSpace(q)) return new();

        // V3.0.0（C4）：与搜索页共用同一套检索核心 —— 分词、同义词、拼音首字母（`hp` → 黑屏）、
        // 字段加权（标题 > 症状 > 原因 > 步骤 > 提示）。过去这里只做「包含即得分」，
        // 于是拼音首字母与近义词一律零结果，同一句话在搜索页与助手里的结果还可能不一致。
        var searchable = _problemService.GetSearchableProblems(data);
        var hits = SearchQueryCore.Rank(searchable, q, 8);

        // 一条都没命中时给最接近的几条（提案改法 ④）：助手不该只回一句「没有找到」，
        // 那等于把用户丢回原点；给几条相关条目至少能继续点下去。
        if (hits.Count == 0)
        {
            hits = SearchQueryCore.Suggest(searchable, q, 3)
                .Select(h => h with { Reason = Strings.T("search.reason.suggestion") })
                .ToList();
        }

        return hits
            .Select(h => new AssistantMatch { Problem = h.Problem, Score = h.Score, Reason = h.Reason })
            .ToList();
    }
}