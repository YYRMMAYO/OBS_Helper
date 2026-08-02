using OBS_Helper.Client.Models;

namespace OBS_Helper.Client.Services;

public class AssistantMatch
{
    public Problem Problem { get; set; } = new();
    public int Score { get; set; }
    public string Reason { get; set; } = "";
}

public class AssistantService
{
    private readonly ProblemService _problemService;

    public AssistantService(ProblemService problemService) => _problemService = problemService;

    public List<string> Suggestions { get; } = new()
    {
        "直播黑屏怎么办",
        "推流失败连接超时",
        "音画不同步怎么调",
        "麦克风没声音",
        "编码过载掉帧",
        "怎么搭建B站直播",
        "录制文件打不开",
        "OBS更新后崩溃"
    };

    public async Task<List<AssistantMatch>> AskAsync(string query)
    {
        var data = await _problemService.GetDataAsync();
        var catTitles = data.Categories.ToDictionary(c => c.Id, c => c.Title);
        var q = Normalize(query);
        if (string.IsNullOrWhiteSpace(q)) return new();

        var tokens = Tokenize(q);
        var results = new List<AssistantMatch>();

        foreach (var p in data.Problems)
        {
            var text = Normalize(ProblemService.BuildText(p, catTitles.GetValueOrDefault(p.Category, "")));
            int score = 0;
            var hits = new List<string>();
            foreach (var t in tokens)
            {
                if (t.Length < 2) continue;
                if (text.Contains(t, StringComparison.OrdinalIgnoreCase))
                {
                    score += t.Length >= 4 ? 3 : 2;
                    hits.Add(t);
                }
            }
            if (Normalize(p.Title).Contains(q)) score += 5;
            if (score > 0)
                results.Add(new AssistantMatch { Problem = p, Score = score, Reason = string.Join("、", hits.Take(4)) });
        }

        results.Sort((a, b) => b.Score.CompareTo(a.Score));
        return results.Take(8).ToList();
    }

    private static string Normalize(string s) => s.ToLowerInvariant();

    private static List<string> Tokenize(string s)
    {
        var tokens = new List<string>();
        var sb = new System.Text.StringBuilder();
        foreach (var c in s)
        {
            if (char.IsWhiteSpace(c) || char.IsPunctuation(c))
            {
                if (sb.Length > 0) { tokens.Add(sb.ToString()); sb.Clear(); }
            }
            else sb.Append(c);
        }
        if (sb.Length > 0) tokens.Add(sb.ToString());

        foreach (var t in tokens.ToList())
        {
            if (t.Length > 2 && IsCjk(t))
            {
                for (var i = 0; i < t.Length - 1; i++) tokens.Add(t.Substring(i, 2));
            }
        }
        return tokens;
    }

    private static bool IsCjk(string s)
    {
        foreach (var c in s)
            if (c is >= '\u4e00' and <= '\u9fff') return true;
        return false;
    }
}
