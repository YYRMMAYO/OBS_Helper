using OBS_Helper.Client.Models;

namespace OBS_Helper.Client.Services;

public class AssistantMatch
{
    public Problem Problem { get; set; } = new();
    public int Score { get; set; }
    public string Reason { get; set; } = "";
}

/// <summary>
/// 离线知识库检索助手。
///
/// <b>为什么不是简单的「包含即得分」：</b>
/// 知识库扩到上百条之后，朴素关键词匹配会迅速失效——用户问「直播黑屏怎么办」，
/// 「直播」两个字几乎每条都有，于是几十条都拿到分数，真正相关的那条被淹没。
/// 因此这里做了三件事：
/// <list type="number">
///   <item><b>停用词过滤</b>：把「怎么 / 如何 / 问题 / obs」这类零信息量的词直接丢掉；</item>
///   <item><b>IDF 加权</b>：一个词出现在越少的条目里，命中它就越能说明问题
///         （「黑屏」比「直播」有价值得多）；</item>
///   <item><b>同义词归一</b>：用户说的是口语（「卡」「花屏」「连不上」），
///         知识库写的是术语（「掉帧」「撕裂」「连接超时」），中间需要一层映射。</item>
/// </list>
///
/// 文档频率与归一化文本都只在首次检索时计算一次并缓存——知识库是静态资源，
/// 没必要每次输入一个字符就全量重算。
/// </summary>
public class AssistantService
{
    /// <summary>低于这个占比（相对最高分）的结果直接丢弃，避免弱相关结果稀释注意力。</summary>
    private const double RelativeCutoff = 0.25;

    private readonly ProblemService _problemService;

    // —— 首次检索时构建的缓存 ——
    private List<IndexedProblem>? _index;
    private Dictionary<string, int>? _docFreq;
    private int _docCount;

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

    /// <summary>
    /// 停用词：出现频率极高但几乎不携带定位信息的词。
    /// 注意「录制」「推流」这类词虽然常见，但仍有区分度，不能进这张表。
    /// </summary>
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "怎么", "怎样", "如何", "为什么", "为啥", "什么", "哪里", "可以", "能否", "是否",
        "问题", "情况", "办法", "解决", "处理", "求助", "帮忙", "一下", "这个", "那个",
        "obs", "studio", "软件", "电脑", "设置", "出现", "发生", "导致", "遇到", "总是",
        "一直", "老是", "有时", "偶尔", "现在", "以后", "之后", "开始", "结束", "使用"
    };

    /// <summary>
    /// 口语 → 术语映射。用户不会说「渲染滞后」，只会说「卡」。
    /// 映射是「扩展」而非「替换」：原词保留，额外补上术语一起参与匹配。
    /// </summary>
    private static readonly Dictionary<string, string[]> Synonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["卡"] = new[] { "掉帧", "卡顿", "延迟" },
        ["卡顿"] = new[] { "掉帧", "滞后", "渲染" },
        ["卡死"] = new[] { "无响应", "崩溃", "假死" },
        ["花屏"] = new[] { "撕裂", "画面异常", "显示" },
        ["黑屏"] = new[] { "捕获", "画面", "显示" },
        ["没声音"] = new[] { "静音", "音频", "麦克风" },
        ["无声"] = new[] { "静音", "音频" },
        ["杂音"] = new[] { "噪音", "电流声", "音频" },
        ["连不上"] = new[] { "连接失败", "超时", "推流" },
        ["断流"] = new[] { "断开", "重连", "推流" },
        ["掉线"] = new[] { "断开", "重连", "网络" },
        ["打不开"] = new[] { "损坏", "无法播放", "录制" },
        ["崩了"] = new[] { "崩溃", "闪退" },
        ["闪退"] = new[] { "崩溃", "退出" },
        ["糊"] = new[] { "模糊", "画质", "码率" },
        ["不同步"] = new[] { "音画", "延迟", "偏移" },
        ["录不了"] = new[] { "录制失败", "无法录制" },
        ["虚拟摄像头"] = new[] { "虚拟相机", "virtualcam" },
        ["绿屏"] = new[] { "色度键", "抠像" },
        ["转播"] = new[] { "推流", "转推" }
    };

    /// <summary>一条预处理好的知识库条目。</summary>
    private sealed class IndexedProblem
    {
        public required Problem Problem { get; init; }
        /// <summary>归一化后的全文，用于子串匹配。</summary>
        public required string Text { get; init; }
        /// <summary>归一化后的标题，命中标题应显著加分。</summary>
        public required string Title { get; init; }
        /// <summary>该条目包含的去重词集合，用于文档频率统计。</summary>
        public required HashSet<string> Terms { get; init; }
    }

    public async Task<List<AssistantMatch>> AskAsync(string query)
    {
        var q = Normalize(query);
        if (string.IsNullOrWhiteSpace(q)) return new();

        await EnsureIndexAsync();
        if (_index is null || _docFreq is null) return new();

        var tokens = ExpandTokens(Tokenize(q));
        if (tokens.Count == 0) return new();

        var results = new List<AssistantMatch>();

        foreach (var item in _index)
        {
            double score = 0;
            var hits = new List<string>();

            foreach (var t in tokens)
            {
                if (!item.Text.Contains(t, StringComparison.OrdinalIgnoreCase)) continue;

                // IDF：出现在越少条目里的词，命中价值越高。
                // +1 平滑，避免只出现在一条里的词把分数拉到失真。
                int df = _docFreq.GetValueOrDefault(t, 1);
                double idf = Math.Log((_docCount + 1.0) / (df + 1.0)) + 1.0;

                // 长词通常更具体（「音画不同步」vs「音画」），额外给一点权重
                double lengthBoost = t.Length >= 4 ? 1.5 : 1.0;

                score += idf * lengthBoost;
                hits.Add(t);
            }

            if (score <= 0) continue;

            // 标题命中是强信号：用户描述与条目标题重合，基本就是要找的那条
            if (item.Title.Contains(q, StringComparison.OrdinalIgnoreCase)) score += 8;
            else if (tokens.Any(t => t.Length >= 2 && item.Title.Contains(t, StringComparison.OrdinalIgnoreCase))) score += 2;

            results.Add(new AssistantMatch
            {
                Problem = item.Problem,
                Score = (int)Math.Round(score * 10),
                Reason = string.Join("、", hits.OrderByDescending(h => h.Length).Distinct().Take(4))
            });
        }

        if (results.Count == 0) return results;

        results.Sort((a, b) => b.Score.CompareTo(a.Score));

        // 相对阈值过滤：只保留与最佳结果同一量级的条目。
        // 用相对值而非绝对值，是因为不同提问的绝对分数差异很大（长句天然得分高）。
        int cutoff = (int)(results[0].Score * RelativeCutoff);
        return results.Where(r => r.Score >= cutoff).Take(8).ToList();
    }

    // ------------------------------------------------------------------ 索引构建

    private async Task EnsureIndexAsync()
    {
        if (_index is not null) return;

        var data = await _problemService.GetDataAsync();
        var catTitles = data.Categories.ToDictionary(c => c.Id, c => c.Title);

        var index = new List<IndexedProblem>(data.Problems.Count);
        var docFreq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in data.Problems)
        {
            var text = Normalize(ProblemService.BuildText(p, catTitles.GetValueOrDefault(p.Category, "")));
            var terms = new HashSet<string>(Tokenize(text), StringComparer.OrdinalIgnoreCase);

            index.Add(new IndexedProblem
            {
                Problem = p,
                Text = text,
                Title = Normalize(p.Title),
                Terms = terms
            });

            foreach (var t in terms)
                docFreq[t] = docFreq.GetValueOrDefault(t) + 1;
        }

        _index = index;
        _docFreq = docFreq;
        _docCount = index.Count;
    }

    // ------------------------------------------------------------------ 分词

    private static string Normalize(string s) => s.ToLowerInvariant();

    /// <summary>把口语词扩展出对应的术语，一起参与匹配。</summary>
    private static List<string> ExpandTokens(List<string> tokens)
    {
        var set = new HashSet<string>(tokens, StringComparer.OrdinalIgnoreCase);
        foreach (var t in tokens)
        {
            if (Synonyms.TryGetValue(t, out var syns))
            {
                foreach (var s in syns) set.Add(Normalize(s));
            }
        }
        return set.ToList();
    }

    private static List<string> Tokenize(string s)
    {
        var raw = new List<string>();
        var sb = new System.Text.StringBuilder();
        foreach (var c in s)
        {
            if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c))
            {
                if (sb.Length > 0) { raw.Add(sb.ToString()); sb.Clear(); }
            }
            else sb.Append(c);
        }
        if (sb.Length > 0) raw.Add(sb.ToString());

        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in raw)
        {
            if (t.Length >= 2 && !StopWords.Contains(t)) tokens.Add(t);

            // 中文没有空格分词，用 bigram 兜底：「音画不同步」→ 音画/画不/不同/同步
            if (t.Length > 2 && IsCjk(t))
            {
                for (var i = 0; i < t.Length - 1; i++)
                {
                    var bg = t.Substring(i, 2);
                    if (!StopWords.Contains(bg)) tokens.Add(bg);
                }
            }
        }
        return tokens.ToList();
    }

    private static bool IsCjk(string s)
    {
        foreach (var c in s)
            if (c is >= '\u4e00' and <= '\u9fff') return true;
        return false;
    }
}
