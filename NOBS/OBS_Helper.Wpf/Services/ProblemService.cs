using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using OBS_Helper.Wpf.Models;
using OBS_Helper.Wpf.Services.Update;

namespace OBS_Helper.Wpf.Services;

/// <summary>
/// 问题库数据访问。数据源是嵌入到程序集里的 <c>Assets/problems.json</c>，
/// 这样单文件发布（SelfContained + PublishSingleFile）时不需要额外释放数据文件，
/// 也杜绝了用户误删 / 误改导致的启动失败。
/// </summary>
public sealed class ProblemService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private ProblemData? _data;
    private string? _guideMarkdown;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>当前缓存是在哪种语言下加载的（V2.9.3）。为空表示还没加载过。</summary>
    private string _loadedLanguage = "";

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
            LoadError = null;
            _loadedLanguage = "";
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
            _data = await Task.Run(LoadData).ConfigureAwait(false);
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
    /// </summary>
    private ProblemData LoadData()
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
        var q = query.Trim();
        var catTitles = data.Categories.ToDictionary(c => c.Id, c => c.Title);
        return data.Problems
            .Where(p => BuildText(p, catTitles.GetValueOrDefault(p.Category, "")).Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();
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
                .Concat(p.Symptoms)
                .Concat(p.Causes)
                .Concat(p.Steps.Select(s => s.Title + " " + s.Detail))
                .Concat(p.Tips)
                .Concat(p.Platforms));
    }
}
