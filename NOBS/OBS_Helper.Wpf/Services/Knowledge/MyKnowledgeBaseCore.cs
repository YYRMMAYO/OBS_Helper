using OBS_Helper.Wpf.Models;

namespace OBS_Helper.Wpf.Services.Knowledge;

/// <summary>
/// 本地知识库（「我的条目」）的合并核心（V3.0 / D8）。纯逻辑、零 IO，单测工程直接链接编译。
///
/// 为什么要有这一层：官方知识库只能被动接收热更新，用户端**没有回写路径** ——
/// 老用户自己的踩坑笔记（某个采集卡的怪毛病、某个平台的编码参数）只能留在聊天记录里。
/// 这里允许用户把条目写进 <c>%LocalAppData%\OBS_Helper\data\my-problems.json</c>，
/// 与热更新文件**同一套 schema、同一个目录**，加载时并进知识库。
///
/// 三条规则（都会影响「用户写的东西会不会消失/串位」，因此都有单测）：
/// <list type="number">
///   <item><b>同 id 时本地优先</b>：用户显然是想覆盖官方那条（改错了删掉文件即可恢复）；</item>
///   <item><b>本地条目的分类不存在时归到一个保留分类</b>：用户写错分类名不该让条目从界面上消失；</item>
///   <item><b>本地文件里的重复 id 只保留第一条</b>：手写 JSON 很容易复制粘贴出重复。</item>
/// </list>
/// </summary>
public static class MyKnowledgeBaseCore
{
    /// <summary>「我的条目」保留分类 id（与官方分类 id 不会撞：官方都用语义化英文短横线名）。</summary>
    public const string MineCategoryId = "mine";

    /// <summary>把本地条目并进基础知识库。返回新对象，不改动入参。</summary>
    public static ProblemData Merge(ProblemData baseData, IReadOnlyList<Problem>? mine, string mineCategoryTitle,
        IReadOnlyList<Category>? mineCategories = null)
    {
        // 基础库（官方 / 热更新 JSON）理论上不会出现 null 元素，但手写或第三方改过的数据完全可能；
        // 这里一并过滤，避免「一条 null」把整库加载打断（与本地条目的容错口径一致）。
        var categories = (baseData.Categories ?? new List<Category>()).Where(c => c is not null).ToList();
        var problems = (baseData.Problems ?? new List<Problem>()).Where(p => p is not null).ToList();

        // ⓪ 本地文件里自建的分类也并进来（同 id 以本地为准）：
        //    否则用户在 my-problems.json 里写的 categories 会被静默丢弃，
        //    他的条目还会被判成「分类不存在」而全部挤到保留分类里。
        var acceptedCategories = new List<Category>();
        foreach (var c in mineCategories ?? Array.Empty<Category>())
        {
            if (c is null || string.IsNullOrWhiteSpace(c.Id)) continue;
            if (string.IsNullOrWhiteSpace(c.Title)) c.Title = c.Id;
            if (string.Equals(c.Id, MineCategoryId, StringComparison.Ordinal)) continue;  // 保留分类由程序维护

            categories.RemoveAll(x => string.Equals(x.Id, c.Id, StringComparison.Ordinal));
            categories.Add(c);
            acceptedCategories.Add(c);
        }

        if (mine is null || mine.Count == 0)
        {
            return new ProblemData
            {
                Version = baseData.Version,
                Updated = baseData.Updated,
                Categories = categories,
                Problems = problems,
            };
        }

        // ① 本地条目去重（保留第一条）并归位分类
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var accepted = new List<Problem>();
        foreach (var p in mine)
        {
            if (p is null || string.IsNullOrWhiteSpace(p.Id)) continue;
            if (!seen.Add(p.Id)) continue;
            if (string.IsNullOrWhiteSpace(p.Title)) continue;

            // 深拷贝一份再改：契约是「返回新对象，不改动入参」，直接改会让复用同一批对象的调用方中招
            var copy = Copy(p);

            // ② 分类不存在 → 归到保留分类（宁可在「我的条目」里出现，也不要因为分类名写错而整条消失）
            if (string.IsNullOrWhiteSpace(copy.Category)
                || !categories.Any(c => string.Equals(c.Id, copy.Category, StringComparison.Ordinal)))
            {
                copy.Category = MineCategoryId;
            }

            copy.IsLocal = true;
            accepted.Add(copy);
        }

        if (accepted.Count == 0)
        {
            return new ProblemData
            {
                Version = baseData.Version,
                Updated = baseData.Updated,
                Categories = categories,
                Problems = problems,
            };
        }

        // ③ 同 id 本地优先：移除官方那条，再把本地条目追加到最后
        var localIds = new HashSet<string>(accepted.Select(p => p.Id), StringComparer.Ordinal);
        problems.RemoveAll(p => localIds.Contains(p.Id));
        problems.AddRange(accepted);

        // 保留分类只在**真的有条目落到它里面**时才加：
        // 否则首页会出现一张「我的条目 0 条」的空卡片，点进去什么都没有。
        if (accepted.Any(p => string.Equals(p.Category, MineCategoryId, StringComparison.Ordinal))
            && !categories.Any(c => string.Equals(c.Id, MineCategoryId, StringComparison.Ordinal)))
        {
            categories.Add(new Category
            {
                Id = MineCategoryId,
                Title = mineCategoryTitle,
                Icon = "📝",
                Description = "",
                Semantic = "violet",
            });
        }

        return new ProblemData
        {
            Version = baseData.Version,
            Updated = baseData.Updated,
            Categories = categories,
            Problems = problems,
        };
    }

    /// <summary>
    /// 浅拷贝一条条目，并把可能为 null 的集合归一化为空集合（V3.0 第三轮验证修复）。
    ///
    /// 为什么必须归一化：手写 JSON 里 <c>"symptoms": null</c> 会让 STJ 把属性**显式覆盖成 null**
    /// （字段初始化器不再生效）。而 <c>ProblemService.BuildText</c>、详情页的
    /// <c>problem.Platforms</c> / <c>Tips.Length</c> 都假设它们非 null ——
    /// 结果不只详情页空白，**整个检索索引构建都会抛异常**（搜索与助手全挂）。
    /// </summary>
    public static Problem Copy(Problem p) => new()
    {
        Id = p.Id,
        Category = p.Category,
        Title = p.Title,
        // 严重度必须一起复制（V3.0 第四轮验证修）：漏了它，用户 my-problems.json 里的
        // "severity":"严重" 会在合并后回落成默认「常见」—— 药丸配色、排序与 AI 分级全跟着偏，
        // 而且这是**深拷贝引入的回归**（改之前是就地改原对象，字段还在）。
        Severity = p.Severity,
        Platforms = p.Platforms ?? Array.Empty<string>(),
        Symptoms = p.Symptoms ?? Array.Empty<string>(),
        Causes = p.Causes ?? Array.Empty<string>(),
        Tips = p.Tips ?? Array.Empty<string>(),
        Related = p.Related ?? Array.Empty<string>(),
        Synonyms = p.Synonyms,
        Steps = p.Steps is null
            ? new List<Step>()
            : p.Steps.Where(s => s is not null).Select(s => new Step
            {
                Title = s.Title ?? "",
                Detail = s.Detail ?? "",
                Level = s.Level ?? "",
            }).ToList(),
        Links = p.Links is null
            ? new List<Link>()
            : p.Links.Where(l => l is not null).Select(l => new Link { Title = l.Title ?? "", Url = l.Url ?? "" }).ToList(),
        IsLocal = p.IsLocal,
    };

    /// <summary>本地条目里有没有 id 与基础库重复的（用于给用户一句「覆盖了官方 N 条」的提示）。</summary>
    public static IReadOnlyList<string> OverriddenIds(ProblemData baseData, IReadOnlyList<Problem>? mine)
    {
        if (mine is null || mine.Count == 0) return Array.Empty<string>();
        var baseIds = new HashSet<string>((baseData.Problems ?? new List<Problem>()).Select(p => p.Id), StringComparer.Ordinal);
        return mine.Where(p => p is not null && baseIds.Contains(p.Id)).Select(p => p.Id).ToList();
    }
}
