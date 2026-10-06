using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using OBS_Helper.Wpf.Models;
using OBS_Helper.Wpf.Services.Knowledge;
using OBS_Helper.Wpf.Services.Search;
using OBS_Helper.Wpf.Services.Update;

namespace OBS_Helper.Wpf.Services;

/// <summary>
/// 问题库数据访问。数据源是嵌入到程序集里的 <c>Assets/problems.json</c>，
/// 这样单文件发布（SelfContained + PublishSingleFile）时不需要额外释放数据文件，
/// 也杜绝了用户误删 / 误改导致的启动失败。
/// </summary>
public sealed class ProblemService
{
    private readonly Services.Knowledge.MyKnowledgeBaseService _myKb;

    /// <summary>V3.0（D8）：本地知识库（%LocalAppData%\OBS_Helper\data\my-problems.json）。</summary>
    public ProblemService() => _myKb = new Services.Knowledge.MyKnowledgeBaseService(System.IO.Path.Combine(Services.Host.HostBridge.AppDataDirectory, "data"));

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private ProblemData? _data;
    private string? _guideMarkdown;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>当前缓存是在哪种语言下加载的（V2.9.3）。为空表示还没加载过。</summary>
    private string _loadedLanguage = "";

    /// <summary>
    /// 检索缓存（V3.0.0 F9）：<see cref="Problem"/> 对象 → <see cref="BuildText"/> 的小写形式。
    ///
    /// 搜索页每敲一个键、助手每问一句，过去都要把整库问题的全文重新拼一遍再转小写；
    /// 这段文本只跟数据与语言有关，知识库加载完成时算一次就够。用问题对象的引用做键而不是
    /// 用 id：即便数据里 id 有重名，每条问题也各自拿到自己的文本，不会互相覆盖。
    /// 数据与文本打包成一个对象整体替换，读侧不会看到「新数据配旧文本」的中间态。
    /// </summary>
    private sealed class SearchIndex
    {
        public SearchIndex(ProblemData owner, Dictionary<Problem, string> texts,
            List<SearchableProblem> fields)
        {
            Owner = owner;
            Texts = texts;
            Fields = fields;
        }

        public ProblemData Owner { get; }
        public Dictionary<Problem, string> Texts { get; }

        /// <summary>
        /// 结构化检索字段（V3.0 / C4）：标题 / 症状 / 原因 / 步骤 / 提示分开保留，
        /// 供字段加权打分使用。与 <see cref="Texts"/>（拼成一大串、用于「包含即命中」的旧路径）
        /// 同源构建、同时替换，不会出现两者不一致的中间态。
        /// </summary>
        public List<SearchableProblem> Fields { get; }
    }

    private SearchIndex? _searchIndex;

    /// <summary>数据加载失败时的错误信息（供 UI 展示报错码）。</summary>
    public string? LoadError { get; private set; }

    /// <summary>当前知识库版本号（problems.json 的 version 字段）；未加载或读取失败时为空串。</summary>
    public string Version => _data?.Version ?? "";

    /// <summary>当前数据源：外部覆盖文件（知识库分离更新后）或内置种子。</summary>
    public string DataSource => _usingExternal ? "external" : "embedded";

    private bool _usingExternal;

    /// <summary>
    /// 清除缓存，下次读取时重新加载。知识库分离更新完成后调用，
    /// 让已打开的页面（分类 / 详情 / 搜索）拿到新数据。
    /// </summary>
    public void Reload()
    {
        _lock.Wait();
        try
        {
            _data = null;
            _guideMarkdown = null;
            _usingExternal = false;
            _localCount = 0;   // 重载期间不能留着上一份的计数（否则界面会显示过期条数）
            LoadError = null;
            _loadedLanguage = "";
            // V3.0.0（F9）：数据没了，检索文本缓存必须一起失效，
            // 否则更新过知识库之后搜索还在拿旧文本匹配（语言切换走的也是这里）。
            _searchIndex = null;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 语言变了就丢弃缓存（V2.9.3）：知识库与指引各有中英两份，缓存必须跟着语言走。
    /// 由 <see cref="AppServices"/> 接在语言服务的事件上，页面下次取数据时自然重载。
    /// </summary>
    private void InvalidateOnLanguageChange()
    {
        if (_loadedLanguage.Length > 0
            && !string.Equals(_loadedLanguage, Strings.Current, StringComparison.Ordinal))
        {
            Reload();
        }
    }

    public async Task<ProblemData> GetDataAsync()
    {
        InvalidateOnLanguageChange();
        if (_data is not null) return _data;

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_data is not null) return _data;
            _data = await Task.Run(() =>
            {
                var loaded = LoadData();
                // V3.0.0（F9）：检索文本跟数据一起就绪，之后搜索 / 助手问答都不必再重拼整库；
                // 放在同一个后台任务里做，不在 UI 线程上拼几百条问题的全文。
                BuildSearchIndex(loaded);
                return loaded;
            }).ConfigureAwait(false);
            return _data;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 加载策略：优先读本地覆盖文件（%LocalAppData%\OBS_Helper\data\problems.json，
    /// 由知识库分离更新写入）；不存在 / 损坏时回退内嵌种子。两者都失败才返回空数据。
    ///
    /// V3.0（D8）：最后再把用户自己的 <c>my-problems.json</c> 并进来 ——
    /// 它排在最外层，因此**用户写的永远赢**（官方热更新也不会覆盖掉用户自己的笔记）。
    /// </summary>
    private ProblemData LoadData()
    {
        var baseline = LoadBaseline();
        return MergeMyKnowledge(baseline);
    }

    /// <summary>把本地条目并进基础知识库；本地文件坏了只影响它自己（并记录 WARN）。</summary>
    private ProblemData MergeMyKnowledge(ProblemData baseline)
    {
        try
        {
            var (mine, mineCategories) = _myKb.LoadDocument();
            // 本地文件坏了要留痕（用户看不到日志，但排查「我写的条目怎么没出现」时要靠它）
            if (_myKb.LastError is { } loadError) FileLogger.Warn("KB", loadError);

            if (mine.Count == 0 && mineCategories.Count == 0)
            {
                _localCount = 0;
                return baseline;
            }

            var merged = MyKnowledgeBaseCore.Merge(baseline, mine, Strings.T("kb.mine.categoryTitle"), mineCategories);
            // 只数**真正进了知识库**的本地条目：被 Merge 丢掉的（重复 id / 空 id / 空标题）不该算进去
            _localCount = merged.Problems.Count(p => p.IsLocal);

            var overridden = MyKnowledgeBaseCore.OverriddenIds(baseline, mine);
            if (overridden.Count > 0)
                FileLogger.Warn("KB", $"本地知识库覆盖了 {overridden.Count} 条官方条目：{string.Join(", ", overridden.Take(5))}");

            FileLogger.Info("KB", $"已并入本地知识库条目 {mine.Count} 条（{_myKb.FilePath}）");
            return merged;
        }
        catch (Exception ex)
        {
            FileLogger.Warn("KB", $"合并本地知识库失败（按无本地条目继续）：{ex.Message}");
            return baseline;
        }
    }

    /// <summary>本地知识库条目数（界面提示「我的条目」用）；未加载时为 0。</summary>
    public int LocalProblemCount => _localCount;

    private int _localCount;

    private ProblemData LoadBaseline()
    {
        // 外部覆盖文件优先（知识库可独立更新，不需要随应用发版）。
        // 外部文件不存在 / 损坏 / 读取失败都静默回退内置种子——
        // 内置可用就不算「加载失败」，不设置 LoadError（首页错误面板只在两者都失败时出现）。
        try
        {
            var externalPath = KnowledgeBaseUpdater.LocalDataFile(ContentAssets.Problems, Strings.Current);
            if (File.Exists(externalPath))
            {
                var raw = File.ReadAllText(externalPath);
                var data = JsonSerializer.Deserialize<ProblemData>(raw, JsonOpts);
                if (data is not null && data.Problems.Count > 0)
                {
                    _usingExternal = true;
                    _loadedLanguage = Strings.Current;
                    return data;
                }
                FileLogger.Warn("KB", "外部知识库文件损坏，回退内置：" + externalPath);
            }
        }
        catch (Exception ex)
        {
            FileLogger.Warn("KB", "外部知识库读取失败，回退内置：" + ex.Message);
        }

        var embedded = LoadEmbedded();
        _usingExternal = false;
        return embedded;
    }

    private ProblemData LoadEmbedded()
    {
        _loadedLanguage = Strings.Current;
        try
        {
            var raw = ContentAssets.ReadEmbeddedWithFallback(ContentAssets.Problems, Strings.Current, out var fellBack);
            if (fellBack)
            {
                FileLogger.Warn("KB", "英文知识库资产缺失，已回退中文随包内容："
                    + ContentAssets.FileName(ContentAssets.Problems, Strings.Current) + "（英文界面下会显示中文）");
            }
            if (raw is null)
            {
                LoadError = Errors.ErrorCodes.ResourceMissing;
                return new ProblemData();
            }
            var data = JsonSerializer.Deserialize<ProblemData>(raw, JsonOpts);
            if (data is null)
            {
                LoadError = Errors.ErrorCodes.DataParseFailed;
                return new ProblemData();
            }
            return data;
        }
        catch (JsonException)
        {
            LoadError = Errors.ErrorCodes.DataParseFailed;
            return new ProblemData();
        }
        catch (Exception)
        {
            LoadError = Errors.ErrorCodes.DataLoadFailed;
            return new ProblemData();
        }
    }

    /// <summary>读取内置的排障指引 Markdown 原文（随语言选资产）。</summary>
    public async Task<string> GetGuideMarkdownAsync()
    {
        InvalidateOnLanguageChange();
        if (_guideMarkdown is not null) return _guideMarkdown;
        _guideMarkdown = await Task.Run(() =>
        {
            var text = ContentAssets.ReadEmbeddedWithFallback(ContentAssets.Troubleshooting, Strings.Current, out var fellBack);
            if (fellBack)
            {
                FileLogger.Warn("Guide", "英文排障指引资产缺失，已回退中文随包内容："
                    + ContentAssets.FileName(ContentAssets.Troubleshooting, Strings.Current) + "（英文界面下会显示中文）");
            }
            return text ?? "";
        }).ConfigureAwait(false);
        return _guideMarkdown;
    }

    public async Task<List<Category>> GetCategoriesAsync() => (await GetDataAsync().ConfigureAwait(false)).Categories;

    public async Task<List<Problem>> GetProblemsAsync() => (await GetDataAsync().ConfigureAwait(false)).Problems;

    public async Task<Problem?> GetByIdAsync(string id)
    {
        var data = await GetDataAsync().ConfigureAwait(false);
        return data.Problems.FirstOrDefault(p => p.Id == id);
    }

    public async Task<Category?> GetCategoryAsync(string id)
    {
        var data = await GetDataAsync().ConfigureAwait(false);
        return data.Categories.FirstOrDefault(c => c.Id == id);
    }

    public async Task<List<Problem>> GetByCategoryAsync(string categoryId)
    {
        var data = await GetDataAsync().ConfigureAwait(false);
        return data.Problems.Where(p => p.Category == categoryId).ToList();
    }

    public async Task<List<Problem>> SearchAsync(string query)
    {
        var data = await GetDataAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(query)) return data.Problems;

        // V3.0.0（C4）：不再是「拼成一大串后 Contains」，而是分词 + 同义词 + 拼音首字母 + 字段加权排序。
        // 索引未就绪（知识库正在加载 / 构建失败）时退回旧的子串匹配路径，结果只会少不会错。
        var index = _searchIndex;
        if (index is not null && ReferenceEquals(index.Owner, data))
            return SearchQueryCore.Rank(index.Fields, query, int.MaxValue).Select(h => h.Problem).ToList();

        var q = query.Trim();
        return data.Problems
            .Where(p => GetSearchText(data, p).Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// 带「无结果建议」的检索（V3.0 / C4 改法 ④）：精确命中为空时给出最接近的几条，
    /// 而不是让用户面对一片空白。返回 (命中, 建议, 是否只是建议)。
    /// </summary>
    public async Task<(List<Problem> Hits, List<Problem> Suggestions)> SearchWithSuggestionsAsync(
        string query, int maxHits = 50, int maxSuggestions = 3)
    {
        var data = await GetDataAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(query)) return (data.Problems, new List<Problem>());

        var index = _searchIndex;
        if (index is null || !ReferenceEquals(index.Owner, data))
            return (await SearchAsync(query).ConfigureAwait(false), new List<Problem>());

        var hits = SearchQueryCore.Rank(index.Fields, query, maxHits).Select(h => h.Problem).ToList();
        if (hits.Count > 0) return (hits, new List<Problem>());

        var suggestions = SearchQueryCore.Suggest(index.Fields, query, maxSuggestions)
            .Select(h => h.Problem).ToList();
        return (hits, suggestions);
    }

    /// <summary>按分类统计问题数量，首页卡片用。</summary>
    public async Task<Dictionary<string, int>> GetCategoryCountsAsync()
    {
        var data = await GetDataAsync().ConfigureAwait(false);
        return data.Problems
            .GroupBy(p => p.Category)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    public static string BuildText(Problem p, string categoryTitle)
    {
        return string.Join(" ",
            new[] { p.Title, categoryTitle }
                // V3.0 第三轮验证：手写 JSON 里显式 null 会让字段初始化器失效，这里必须逐个兜底 ——
                // BuildText 是**全库检索**的入口，抛一次异常会让整个索引构建失败、搜索与助手全挂。
                .Concat(p.Symptoms ?? Array.Empty<string>())
                .Concat(p.Causes ?? Array.Empty<string>())
                .Concat((p.Steps ?? new List<Models.Step>()).Select(s => s.Title + " " + s.Detail))
                .Concat(p.Tips ?? Array.Empty<string>())
                .Concat(p.Platforms ?? Array.Empty<string>()));
    }

    // ------------------------------------------------------ 检索文本缓存（V3.0.0 F9）

    /// <summary>
    /// 取一条问题的规范化检索文本（<see cref="BuildText"/> 的小写形式）。
    ///
    /// 命中缓存时只是一次引用比较 + 一次字典查找；缓存还没建（知识库正在加载）、
    /// 刚被 <see cref="Reload"/> 清掉、或手上这份数据和缓存不是同一份时，
    /// 退回与旧实现逐字相同的即时构建 —— 结果永远等同于
    /// <c>BuildText(p, 分类标题)</c>，因此匹配结果与排序不会因为缓存而改变。
    /// </summary>
    internal string GetSearchText(ProblemData data, Problem p)
    {
        var index = _searchIndex;
        if (index is not null
            && ReferenceEquals(index.Owner, data)
            && index.Texts.TryGetValue(p, out var cached))
        {
            return cached;
        }
        return BuildSearchText(data, p);
    }

    /// <summary>
    /// 取结构化检索字段（V3.0 / C4）；索引未就绪时按当前数据即时构建一份等价列表
    /// （结果永远等同于缓存路径，只是慢一点 —— 与 <see cref="GetSearchText"/> 同一取舍）。
    /// </summary>
    internal List<SearchableProblem> GetSearchableProblems(ProblemData data)
    {
        var index = _searchIndex;
        if (index is not null && ReferenceEquals(index.Owner, data)) return index.Fields;

        var catTitles = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var c in data.Categories) catTitles[c.Id] = c.Title;

        return data.Problems.Select(p => new SearchableProblem(
            Problem: p,
            Title: p.Title ?? "",
            Category: catTitles.TryGetValue(p.Category, out var t) ? t : "",
            Symptoms: p.Symptoms ?? Array.Empty<string>(),
            Causes: p.Causes ?? Array.Empty<string>(),
            Steps: (p.Steps ?? new List<Step>()).Select(s => s.Title + " " + s.Detail).ToList(),
            Tips: p.Tips ?? Array.Empty<string>(),
            Synonyms: p.Synonyms ?? Array.Empty<string>())).ToList();
    }

    /// <summary>知识库加载完成后构建一次检索缓存（调用方持有 <see cref="_lock"/>）。</summary>
    private void BuildSearchIndex(ProblemData data)
    {
        try
        {
            var catTitles = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var c in data.Categories)
            {
                catTitles[c.Id] = c.Title;
            }

            var texts = new Dictionary<Problem, string>(data.Problems.Count);
            var fields = new List<SearchableProblem>(data.Problems.Count);
            foreach (var p in data.Problems)
            {
                var category = catTitles.TryGetValue(p.Category, out var title) ? title : "";
                texts[p] = BuildText(p, category).ToLowerInvariant();
                fields.Add(new SearchableProblem(
                    Problem: p,
                    Title: p.Title ?? "",
                    Category: category,
                    Symptoms: p.Symptoms ?? Array.Empty<string>(),
                    Causes: p.Causes ?? Array.Empty<string>(),
                    Steps: (p.Steps ?? new List<Step>()).Select(s => s.Title + " " + s.Detail).ToList(),
                    Tips: p.Tips ?? Array.Empty<string>(),
                    Synonyms: p.Synonyms ?? Array.Empty<string>()));
            }

            // 整对象一次替换：读侧要么看到旧的一整份，要么看到新的一整份
            _searchIndex = new SearchIndex(data, texts, fields);
        }
        catch (Exception ex)
        {
            // 缓存只是加速手段。数据里万一有意外字段（例如某个问题的 category 为 null）
            // 让整库预拼失败，宁可退回「逐条即时构建」的老路径 —— 那也是旧实现的行为 ——
            // 也不能把「加载知识库」这件事本身弄失败。
            _searchIndex = null;
            FileLogger.Warn("KB", "检索缓存构建失败，已退回逐条构建：" + ex.Message);
        }
    }

    /// <summary>即时构建一条问题的规范化检索文本（缓存不可用时的等价回退路径）。</summary>
    private static string BuildSearchText(ProblemData data, Problem p)
    {
        var title = "";
        foreach (var c in data.Categories)
        {
            if (string.Equals(c.Id, p.Category, StringComparison.Ordinal))
            {
                title = c.Title;
                break;
            }
        }
        return BuildText(p, title).ToLowerInvariant();
    }
}
