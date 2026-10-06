using System.IO;
using System.Text.Json;
using OBS_Helper.Wpf.Localization;
using OBS_Helper.Wpf.Models;

namespace OBS_Helper.Wpf.Services.Knowledge;

/// <summary>
/// 「我的知识库」文件读写（V3.0 / D8）。
///
/// 位置与热更新文件**同目录**（<c>%LocalAppData%\OBS_Helper\data\</c>），
/// 但文件名叫 <c>my-problems.json</c>，因此不会被知识库热更新覆盖掉 ——
/// 用户自己写的东西必须比自动更新更"硬"。
///
/// 读策略：单条坏数据只跳过它，绝不因为一条写错就让整份本地知识库消失
/// （手写 JSON 出错是常态，用户看不到自己写的东西会以为程序坏了）。
/// </summary>
public sealed class MyKnowledgeBaseService
{
    /// <summary>
    /// 存储目录由调用方给出（默认是 <c>%LocalAppData%\OBS_Helper\data</c>）。
    ///
    /// 做成构造参数而不是内部写死：一是**可测**（单测用临时目录就能覆盖真实的读写路径，
    /// 不再只能测纯逻辑）；二是同一个进程将来若要读写别的知识库目录（例如便携模式）不必改这里。
    /// </summary>
    public MyKnowledgeBaseService(string dataDirectory)
    {
        Directory = dataDirectory;
        FilePath = Path.Combine(dataDirectory, "my-problems.json");
    }

    /// <summary>知识库数据目录。</summary>
    public string Directory { get; }

    /// <summary>本地条目文件路径。</summary>
    public string FilePath { get; }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,      // 允许手写文件里留注释
        AllowTrailingCommas = true,
        WriteIndented = true,
        // 写出的字段名与官方知识库（camelCase）一致：用户从仓库里抄一条改，格式不会有出入
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>读取本地条目；文件不存在返回空列表。</summary>
    public List<Problem> Load() => LoadDocument().Problems;

    /// <summary>
    /// 读取本地知识库文档（条目 **+ 用户自建分类**）。
    ///
    /// 分类也要读：用户在文件里写 <c>"categories":[{"id":"my-hw","title":"我的硬件"}]</c>
    /// 是他的正当用法，只读 problems 会让那段配置形同不存在、条目还被挤进保留分类。
    /// </summary>
    public (List<Problem> Problems, List<Category> Categories) LoadDocument()
    {
        // 每次进入都先清空：否则「先写坏文件记了一条错误、改好后重载」会在日志里重复出现旧错误，误导排查
        LastError = null;

        try
        {
            if (!File.Exists(FilePath)) return (new List<Problem>(), new List<Category>());

            var raw = File.ReadAllText(FilePath);
            if (string.IsNullOrWhiteSpace(raw)) return (new List<Problem>(), new List<Category>());

            // 容忍两种写法：{ "problems": [...] } 或直接 [...]
            var problems = TryReadProblems(raw, out var categories);
            if (problems is null)
            {
                LastError = "本地知识库格式无法解析（期望 {problems:[...]} 或 [...]）：" + FilePath;
                return (new List<Problem>(), new List<Category>());
            }
            return (problems, categories);
        }
        catch (Exception ex)
        {
            LastError = $"读取本地知识库失败：{ex.Message}";
            return (new List<Problem>(), new List<Category>());
        }
    }

    /// <summary>
    /// 逐条解析（V3.0 第三轮验证修复）。
    ///
    /// 原先直接整体反序列化：**一条**条目的字段类型写错（手写 JSON 的常见错误，例如
    /// <c>"symptoms": 12345</c>）或数组里混进 <c>null</c>，就会让整份本地知识库读成空 ——
    /// 与「单条坏数据只跳过它」的承诺正好相反，用户会以为自己的笔记全丢了。
    /// 现在先解析成 JSON 文档取到数组，再**逐元素**反序列化，坏的那条跳过并计数。
    /// </summary>
    private List<Problem>? TryReadProblems(string raw, out List<Category> categories)
    {
        categories = new List<Category>();
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(raw, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch (JsonException) { return null; }

        using (doc)
        {
            var root = doc.RootElement;

            JsonElement array;
            if (root.ValueKind == JsonValueKind.Array)
            {
                array = root;
            }
            else if (root.ValueKind == JsonValueKind.Object && TryGetPropertyIgnoreCase(root, "problems", out var inner)
                     && inner.ValueKind == JsonValueKind.Array)
            {
                array = inner;
            }
            else
            {
                return null;
            }

            // 用户自建的分类（可选）：与条目一样逐元素容错
            if (root.ValueKind == JsonValueKind.Object && TryGetPropertyIgnoreCase(root, "categories", out var cats)
                && cats.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in cats.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.Object) continue;
                    try
                    {
                        var category = element.Deserialize<Category>(JsonOpts);
                        if (category is not null) categories.Add(category);
                    }
                    catch (JsonException) { /* 单条分类写坏不影响条目 */ }
                }
            }

            var list = new List<Problem>();
            var skipped = 0;
            foreach (var element in array.EnumerateArray())
            {
                // null / 数字 / 字符串这些非对象元素直接跳过（不是条目）
                if (element.ValueKind != JsonValueKind.Object) { skipped++; continue; }

                try
                {
                    var problem = element.Deserialize<Problem>(JsonOpts);
                    if (problem is not null) list.Add(problem);
                    else skipped++;
                }
                catch (JsonException) { skipped++; }   // 单条类型错误：跳过它，保住其余条目
            }

            if (skipped > 0)
                LastError = $"本地知识库有 {skipped} 条格式不对，已跳过（其余 {list.Count} 条正常载入）：{FilePath}";

            return list;
        }
    }

    /// <summary>
    /// 大小写不敏感地取属性。
    ///
    /// 手写文件里 <c>Problems</c> / <c>problems</c> 都有人写（从仓库抄的是 camelCase，
    /// 从 C# 模型抄的是 PascalCase），而 <c>JsonElement.TryGetProperty</c> 是**大小写敏感**的 ——
    /// 只认一种写法会让另一种写法静默读成空。
    /// </summary>
    private static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    /// <summary>
    /// 最近一次读 / 写失败的原因（成功时为 null）。
    ///
    /// 由调用方决定怎么记（当前是 <c>ProblemService</c> 写进日志）：存储类自己闷头写日志的话，
    /// 测试里就没法观察失败原因，上层也无从判断「是文件坏了还是压根没文件」。
    /// </summary>
    public string? LastError { get; private set; }

    /// <summary>本地条目是否已经存在（界面据此决定「创建示例文件」按钮要不要显示）。</summary>
    public bool FileExists => File.Exists(FilePath);

    /// <summary>
    /// 写一份带示例条目的起始文件（已存在则不动，返回 false）。
    ///
    /// 给示例而不是给空文件：JSON 没有注释，用户打开空文件不知道字段叫什么；
    /// 一条完整示例是最短的「说明书」。
    /// </summary>
    public bool WriteStarterFile()
    {
        try
        {
            if (File.Exists(FilePath)) return false;

            System.IO.Directory.CreateDirectory(Directory);

            var sample = new ProblemData
            {
                Version = "1.0",
                Updated = DateTime.Now.ToString("yyyy-MM-dd"),
                Categories = new List<Category>
                {
                    new() { Id = MyKnowledgeBaseCore.MineCategoryId, Title = Strings.T("kb.mine.categoryTitle"), Icon = "📝", Semantic = "violet" },
                },
                Problems = new List<Problem>
                {
                    new()
                    {
                        Id = "my-sample",
                        Category = MyKnowledgeBaseCore.MineCategoryId,
                        Title = Strings.T("kb.mine.sampleTitle"),
                        Symptoms = new[] { Strings.T("kb.mine.sampleSymptom") },
                        Causes = new[] { Strings.T("kb.mine.sampleCause") },
                        Steps = new List<Step>
                        {
                            new() { Title = Strings.T("kb.mine.sampleStepTitle"), Detail = Strings.T("kb.mine.sampleStepDetail"), Level = Strings.T("kb.mine.sampleStepLevel") },
                        },
                        Tips = new[] { Strings.T("kb.mine.sampleTip") },
                        Platforms = new[] { "Windows" },
                    },
                },
            };

            File.WriteAllText(FilePath, JsonSerializer.Serialize(sample, JsonOpts));
            return true;
        }
        catch (Exception ex)
        {
            LastError = $"创建本地知识库示例文件失败：{ex.Message}";
            return false;
        }
    }
}
