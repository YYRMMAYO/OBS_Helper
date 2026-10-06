using System.Text;
using OBS_Helper.Wpf.Localization;
using OBS_Helper.Wpf.Models;

namespace OBS_Helper.Wpf.Services.Search;

/// <summary>一条可检索的问题（字段分开保留，便于按字段加权）。</summary>
public sealed record SearchableProblem(
    Problem Problem,
    string Title,
    string Category,
    IReadOnlyList<string> Symptoms,
    IReadOnlyList<string> Causes,
    IReadOnlyList<string> Steps,
    IReadOnlyList<string> Tips,
    /// <summary>数据侧补充的别名 / 同义词（来自知识库热更新，不改程序也能补）。</summary>
    IReadOnlyList<string> Synonyms);

/// <summary>一条命中结果。</summary>
public sealed record SearchHit(Problem Problem, int Score, string Reason);

/// <summary>
/// 检索核心（V3.0 / C4）。纯 BCL、零 IO，单测工程直接链接编译。
///
/// 现状问题（提案原文）：搜索与助手都只做**子串包含** —— 无分词、无权重、无模糊，于是
/// 拼音首字母（`hp` → 黑屏）零结果、「落帧」查不到「掉帧」、带空格的多词查询整串匹配失败。
///
/// 四条改法（与提案一一对应）：
/// <list type="number">
///   <item><b>数据侧同义词</b>：<see cref="SearchableProblem.Synonyms"/> 来自知识库，走已有热更新通道；</item>
///   <item><b>查询侧拼音首字母</b>：<see cref="PinyinInitials"/>，纯 BCL、无外部依赖；</item>
///   <item><b>字段加权排序</b>：标题 &gt; 症状 &gt; 原因 &gt; 步骤 &gt; 提示；</item>
///   <item><b>无结果时给建议</b>：<see cref="Suggest"/> 返回最接近的几条，而不是一片空白。</item>
/// </list>
///
/// 关于拼音的**边界**（必须如实说明）：这里用的是**内置的常见字表**，不是完整拼音库 ——
/// 全量表要上万条数据、与「零第三方依赖」的目标不符。未收录的字直接跳过（不会算错），
/// 因此拼音匹配只是一条**加分路径**，子串匹配始终照常工作；查不到时也不会比原来更差。
/// </summary>
public static class SearchQueryCore
{
    // ---- 字段权重：标题最重，提示最轻 ----
    public const int TitleWeight = 12;
    public const int SynonymWeight = 9;
    public const int SymptomWeight = 6;
    public const int CauseWeight = 4;
    public const int StepWeight = 3;
    public const int CategoryWeight = 2;
    public const int TipWeight = 1;

    /// <summary>整串（用户原样输入）命中标题时的额外加成。</summary>
    public const int ExactTitleBonus = 15;

    /// <summary>
    /// 标题**恰好等于**查询词时的额外加成（在 <see cref="ExactTitleBonus"/> 之上再给）。
    ///
    /// 为什么需要单独一档：只判「包含」的话，「黑屏且花屏」（标题含「黑屏」且别名命中）
    /// 会压过标题就叫「黑屏」的那一条 —— 而后者几乎必然是用户想要的。
    /// </summary>
    public const int ExactTitleEqualsBonus = 25;

    /// <summary>拼音首字母命中的权重（低于同权重的中文命中：它是「近似」，不该压过精确匹配）。</summary>
    public const int PinyinBonus = 4;

    // ---------------------------------------------------------------- 分词与规范化

    public static string Normalize(string? s) => (s ?? "").Trim().ToLowerInvariant();

    /// <summary>
    /// 切词：先按空白/标点切，再对中文串补**二元组**。
    ///
    /// 二元组是为了让「没有完整词边界」的中文也能命中（「编码器过载」→ 编码/码器/器过/过载），
    /// 这是 CJK 检索在没有分词器时最实用的近似；不引入词典，避免又一个需要维护的数据源。
    /// </summary>
    public static IReadOnlyList<string> Tokenize(string query)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();

        void Flush()
        {
            if (sb.Length == 0) return;
            tokens.Add(sb.ToString());
            sb.Clear();
        }

        foreach (var c in Normalize(query))
        {
            if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c)) Flush();
            else sb.Append(c);
        }
        Flush();

        foreach (var t in tokens.ToList())
        {
            if (t.Length <= 2 || !HasCjk(t)) continue;
            for (var i = 0; i < t.Length - 1; i++) tokens.Add(t.Substring(i, 2));
        }

        return tokens.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>是否只由 ASCII 字母数字组成（拼音首字母查询的判据）。</summary>
    public static bool IsAsciiQuery(string query)
    {
        var q = Normalize(query);
        if (q.Length == 0) return false;
        return q.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or ' ');
    }

    private static bool HasCjk(string s)
    {
        foreach (var c in s)
            if (c is >= '\u4e00' and <= '\u9fff') return true;
        return false;
    }

    // ---------------------------------------------------------------- 同义词扩展

    /// <summary>
    /// 内置同义词组（与具体问题无关的**通用口语对照**）。
    ///
    /// 这些是用户真实会打的词，而知识库里写的是书面词：不说出来，「落帧」永远查不到「掉帧」。
    /// 数据侧还可以再按条目补充别名（见 <see cref="SearchableProblem.Synonyms"/>），两者叠加。
    /// </summary>
    private static readonly string[][] BuiltInSynonyms =
    {
        new[] { "掉帧", "丢帧", "落帧", "掉帧率", "丢帧率" },
        new[] { "黑屏", "黑画面", "没有画面", "无画面", "黑屏闪" },
        new[] { "蓝屏", "崩溃", "闪退", "报错退出" },
        new[] { "卡顿", "卡", "很卡", "卡住", "不流畅" },
        new[] { "延迟", "延时", "高延迟", "ping" },
        new[] { "麦克风", "话筒", "mic", "麦" },
        new[] { "没声音", "无声", "没有声音", "听不到声音" },
        new[] { "花屏", "画面撕裂", "马赛克", "画屏" },
        new[] { "录像", "录制", "录屏", "录视频" },
        new[] { "推流", "直播", "开播" },
        new[] { "摄像头", "相机", "webcam" },
        new[] { "编码器", "编码", "encoder" },
        new[] { "采样率", "采样频率", "samplerate" },
        new[] { "分辨率", "画质", "清晰度" },
        new[] { "显卡", "GPU", "显示卡" },
        new[] { "驱动", "driver" },
        new[] { "音频", "声音", "音轨" },
        new[] { "游戏捕获", "游戏源", "game capture" },
        new[] { "窗口捕获", "窗口源" },
        new[] { "显示器捕获", "屏幕捕获", "全屏捕获" },
        new[] { "虚拟摄像头", "虚拟相机", "vcam" },
        new[] { "回放缓存", "回放", "回放缓冲" },
    };

    private static readonly Dictionary<string, string[]> SynonymIndex = BuildSynonymIndex();

    private static Dictionary<string, string[]> BuildSynonymIndex()
    {
        var map = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var group in BuiltInSynonyms)
        {
            foreach (var word in group)
            {
                // 同一个词出现在多组时以先声明的为准（组表里没有重复词）
                if (!map.ContainsKey(word)) map[word] = group;
            }
        }
        return map;
    }

    /// <summary>
    /// 把查询词扩展成「原词 + 同义词 + 数据侧别名」。查不出同义词时原样返回。
    /// </summary>
    public static IReadOnlyList<string> ExpandWithSynonyms(
        IEnumerable<string> tokens, IReadOnlyList<string>? extraSynonyms = null)
    {
        var result = new List<string>();

        foreach (var t in tokens)
        {
            if (t.Length == 0) continue;
            if (!result.Contains(t, StringComparer.Ordinal)) result.Add(t);

            // 整词命中内置表
            if (SynonymIndex.TryGetValue(t, out var group))
            {
                foreach (var w in group)
                    if (!result.Contains(w, StringComparer.Ordinal)) result.Add(w);
            }

            // 长词里的**子串**命中也要扩展（「严重掉帧」→ 掉帧 → 丢帧/落帧）
            foreach (var kv in SynonymIndex)
            {
                if (kv.Key.Length < 2 || !t.Contains(kv.Key, StringComparison.Ordinal)) continue;
                foreach (var w in kv.Value)
                    if (!result.Contains(w, StringComparer.Ordinal)) result.Add(w);
            }
        }

        // 数据侧别名：作为整体补充（不做子串扩展 —— 那是数据作者的自由文本）
        foreach (var s in extraSynonyms ?? Array.Empty<string>())
        {
            var word = Normalize(s);
            if (word.Length > 0 && !result.Contains(word, StringComparer.Ordinal)) result.Add(word);
        }

        // 上限：避免同义词把查询膨胀到影响性能（每条只多几十次 Contains）
        return result.Take(40).ToList();
    }

    // ---------------------------------------------------------------- 打分

    /// <summary>
    /// 给一条问题打分。返回 null 表示未命中（分数为 0）。
    /// </summary>
    public static SearchHit? Score(SearchableProblem p, string query)
    {
        var q = Normalize(query);
        if (q.Length == 0) return null;

        var tokens = Tokenize(q);
        if (tokens.Count == 0) return null;

        var expanded = ExpandWithSynonyms(tokens, p.Synonyms);
        var reason = new List<string>();
        var score = 0;

        foreach (var t in expanded)
        {
            if (t.Length < 2) continue;

            var hit = false;
            hit |= AddFromText(p.Title, t, TitleWeight);
            hit |= AddFromList(p.Synonyms, t, SynonymWeight);
            hit |= AddFromList(p.Symptoms, t, SymptomWeight);
            hit |= AddFromList(p.Causes, t, CauseWeight);
            hit |= AddFromList(p.Steps, t, StepWeight);
            hit |= AddFromText(p.Category, t, CategoryWeight);
            hit |= AddFromList(p.Tips, t, TipWeight);

            if (hit)
            {
                // 长词更具体：长度加权（4 字以上给双倍），避免「器」这种单字噪声压过整词
                score += t.Length >= 4 ? 2 : 1;
                if (reason.Count < 4) reason.Add(t);
            }

            bool AddFromText(string? field, string token, int weight)
            {
                if (string.IsNullOrEmpty(field)) return false;
                if (!field.Contains(token, StringComparison.OrdinalIgnoreCase)) return false;
                score += weight;
                return true;
            }

            bool AddFromList(IReadOnlyList<string> fields, string token, int weight)
            {
                var any = false;
                foreach (var f in fields)
                {
                    if (string.IsNullOrEmpty(f)) continue;
                    if (!f.Contains(token, StringComparison.OrdinalIgnoreCase)) continue;
                    score += weight;
                    any = true;
                }
                return any;
            }
        }

        // 整串命中标题：最强信号（用户打的就是标题里的词）
        if (p.Title.Contains(q, StringComparison.OrdinalIgnoreCase))
        {
            score += ExactTitleBonus;
            if (reason.Count == 0) reason.Add(q);
        }

        // 标题**就是**查询词：再给一档，确保它排在任何「沾边但还带别名/症状」的条目之前
        if (string.Equals(p.Title.Trim(), q, StringComparison.OrdinalIgnoreCase))
        {
            score += ExactTitleEqualsBonus;
            if (reason.Count == 0) reason.Add(q);
        }

        // 拼音首字母：只在查询是纯 ASCII 时尝试（中文查询没有「首字母」可言）
        if (IsAsciiQuery(q))
        {
            foreach (var word in new[] { q }.Concat(expanded.Where(IsAsciiQuery)))
            {
                var letters = word.Replace(" ", "");
                if (letters.Length < 2 || letters.Length > 8) continue;

                if (StartsWithPinyin(p.Title, letters) || StartsWithPinyin(p.Symptoms, letters))
                {
                    score += PinyinBonus * letters.Length;
                    if (reason.Count < 4) reason.Add(Strings.T("search.reason.pinyin", letters));
                }
            }
        }

        return score > 0 ? new SearchHit(p.Problem, score, string.Join("、", reason)) : null;
    }

    /// <summary>某段文本的拼音首字母是否以给定串开头（「黑屏」→ hp）。</summary>
    private static bool StartsWithPinyin(string text, string letters)
        => PinyinInitials(text).StartsWith(letters, StringComparison.OrdinalIgnoreCase);

    private static bool StartsWithPinyin(IReadOnlyList<string> fields, string letters)
    {
        foreach (var f in fields)
            if (StartsWithPinyin(f, letters)) return true;
        return false;
    }

    /// <summary>按分数降序返回命中（同分按标题稳定排序，保证两次结果一致）。</summary>
    public static IReadOnlyList<SearchHit> Rank(IEnumerable<SearchableProblem> items, string query, int max = 20)
    {
        var hits = new List<SearchHit>();
        foreach (var p in items)
        {
            var hit = Score(p, query);
            if (hit is not null) hits.Add(hit);
        }

        return hits
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.Problem.Title, StringComparer.Ordinal)
            .Take(Math.Max(0, max))
            .ToList();
    }

    /// <summary>
    /// 无结果时的建议（提案改法 ④）。
    ///
    /// 口径：用**查询的每个字**做宽松匹配，取重叠度最高的几条 —— 目的是「别让用户面对一片空白」，
    /// 因此宁可给不算精准但相关的条目，也不返回空列表。仍然按分数排序，并如实标注是「建议」。
    /// </summary>
    public static IReadOnlyList<SearchHit> Suggest(IEnumerable<SearchableProblem> items, string query, int max = 3)
    {
        var q = Normalize(query);
        if (q.Length == 0) return Array.Empty<SearchHit>();

        // 单字（含 CJK）作松散信号；ASCII 查询则整体与拼音首字母比对
        var chars = q.Where(c => !char.IsWhiteSpace(c) && !char.IsPunctuation(c))
                     .Select(c => c.ToString())
                     .Distinct(StringComparer.Ordinal)
                     .ToList();
        var ascii = IsAsciiQuery(q);

        var results = new List<SearchHit>();
        foreach (var p in items)
        {
            var score = 0;
            foreach (var c in chars)
            {
                if (p.Title.Contains(c, StringComparison.OrdinalIgnoreCase)) score += 3;
                else if (p.Symptoms.Any(s => s.Contains(c, StringComparison.OrdinalIgnoreCase))) score += 2;
                else if (p.Category.Contains(c, StringComparison.OrdinalIgnoreCase)) score += 1;
            }

            if (ascii && startsWithAny(p, q)) score += 6;

            if (score > 0)
                results.Add(new SearchHit(p.Problem, score, Strings.T("search.reason.suggestion")));
        }

        return results
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.Problem.Title, StringComparer.Ordinal)
            .Take(Math.Max(0, max))
            .ToList();

        static bool startsWithAny(SearchableProblem p, string letters)
            => StartsWithPinyin(p.Title, letters) || StartsWithPinyin(p.Symptoms, letters);
    }

    // ---------------------------------------------------------------- 拼音首字母

    /// <summary>
    /// 内置常见字表（按声母分组）。<b>不是完整拼音库</b>：未收录的字直接跳过。
    ///
    /// 覆盖范围：知识库标题 / 症状里出现的高频字（那正是用户会用首字母去查的对象）。
    /// 多音字按最常见读音收录（如「行」收 x、「重」收 z），因此首字母匹配是**近似信号**，
    /// 权重也低于中文精确匹配。
    ///
    /// 注意声明顺序：`InitialTable` 依赖本数组，静态字段按声明顺序初始化 ——
    /// 把本数组放在前面，否则 `BuildInitialTable` 会读到 null（这个坑踩过一次）。
    /// </summary>
    private static readonly (char Initial, string Chars)[] InitialGroups =
    {
        ('a', "阿啊"),
        ('b', "八把白百办半帮包保报北被本比笔必边变别的病波不步部播编败标表并捕辨"),
        ('c', "才采参操测层插查差产长场常超车成程持充出初除处触窗创此从存错藏"),
        ('d', "答大打代带单但当导到得的等低底地第点电调掉迭定丢动读短对多顿"),
        ('e', "而"),
        ('f', "发法反返方防房放飞非分风封否服府复副"),
        ('g', "该改概干感刚高告格各给根更工功共够估古故挂关观管光广规过"),
        ('h', "哈还海含汉好号合何和黑很红后候呼湖互户花化画话换回会活获或"),
        ('j', "击机基及即几计记技既加家价架间检简见件建将交角叫接街节结解界今金仅尽进近经警静就局举据决绝均"),
        ('k', "卡开看考科可克刻客课空口扣苦快宽况困扩"),
        ('l', "拉来蓝览老乐类冷离理力例连联量两列临流录率乱论落"),
        ('m', "码马吗买卖麦满慢忙么没每美门面秒民名明命模某母目"),
        ('n', "拿哪那内能你年念您宁牛农弄女"),
        ('o', "哦偶"),
        ('p', "怕排派判配朋批片漂品平评屏破普"),
        ('q', "其奇起气器前钱强切且亲轻清情请区取全缺确群"),
        ('r', "然让热人认任日容如软"),
        ('s', "三色筛上少设身深什声生失时识实使世事视是手受书输数刷双水说思死四送速算虽随所摄"),
        ('t', "他它她台太态谈特提题体天条听停通同统头图土推退"),
        ('w', "外完万网往望危为微维位文问我无五物务"),
        ('x', "下先显现限线相想向项像消小效些写谢心新信性修需许序宣选学雪"),
        ('y', "压言严研眼演验样要也业一已以意因音应英用优由有右于余与语预元原源远院月越云运"),
        ('z', "杂再在早造则怎增扎展站张找折这真正整政之支知直职止只纸至制质中终种重周主注专转装状准资子字自总走阻组最左作坐做帧"),
    };

    private static readonly Dictionary<char, char> InitialTable = BuildInitialTable();

    private static Dictionary<char, char> BuildInitialTable()
    {
        var map = new Dictionary<char, char>();
        foreach (var (initial, chars) in InitialGroups)
        {
            foreach (var c in chars)
            {
                if (!map.ContainsKey(c)) map[c] = initial;   // 重复时以先声明者为准
            }
        }
        return map;
    }

    /// <summary>
    /// 取一段文本的拼音首字母：中文字取内置表里的声母，ASCII 字母原样保留，其余字符跳过。
    /// 「黑屏」→ <c>hp</c>；「掉帧严重」→ <c>dzyz</c>；未收录的字不产生字符（不影响子串匹配）。
    /// </summary>
    public static string PinyinInitials(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                sb.Append(char.ToLowerInvariant(c));
            }
            else if (InitialTable.TryGetValue(c, out var initial))
            {
                sb.Append(initial);
            }
        }
        return sb.ToString();
    }
}
