using OBS_Helper.Wpf.Models;
using OBS_Helper.Wpf.Services.Knowledge;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// 本地知识库**真实文件读写**的回归测试（V3.0 / D8）。
///
/// 纯逻辑测试证明不了「用户把文件放到那个目录里，程序真的能读出来」——
/// 而那正是这一项的全部意义（用户手写 JSON，程序接住）。
/// 存储目录做成构造参数之后，这里用临时目录跑完整的读 / 写 / 往返路径。
/// </summary>
public class MyKnowledgeBaseServiceTests : IDisposable
{
    private readonly string _dir;

    public MyKnowledgeBaseServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "obs-helper-kb-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 清理失败无妨 */ }
    }

    private MyKnowledgeBaseService NewService() => new(_dir);

    // ---------------------------------------------------------------- 读

    [Fact]
    public void Load_MissingFile_ReturnsEmpty()
    {
        var list = NewService().Load();
        Assert.Empty(list);
        Assert.False(NewService().FileExists);
    }

    /// <summary>官方知识库那种「{problems:[...]}」写法。</summary>
    [Fact]
    public void Load_WrappedForm()
    {
        File.WriteAllText(Path.Combine(_dir, "my-problems.json"), """
        { "version": "1.0", "problems": [ { "id": "my-1", "title": "包着的写法", "category": "mine" } ] }
        """);

        var list = NewService().Load();

        Assert.Single(list);
        Assert.Equal("包着的写法", list[0].Title);
    }

    /// <summary>用户手写时更可能直接写一个数组。</summary>
    [Fact]
    public void Load_BareArrayForm()
    {
        File.WriteAllText(Path.Combine(_dir, "my-problems.json"),
            """[{"id":"my-2","title":"数组写法"}]""");

        var list = NewService().Load();

        Assert.Single(list);
        Assert.Equal("数组写法", list[0].Title);
    }

    /// <summary>手写 JSON 允许留注释与尾随逗号（很多人会写）。</summary>
    [Fact]
    public void Load_ToleratesCommentsAndTrailingCommas()
    {
        File.WriteAllText(Path.Combine(_dir, "my-problems.json"), """
        {
          // 我的踩坑笔记
          "problems": [
            { "id": "my-3", "title": "带注释" },
          ]
        }
        """);

        var list = NewService().Load();

        Assert.Single(list);
        Assert.Equal("带注释", list[0].Title);
    }

    /// <summary>坏文件不能让程序崩：返回空列表并留 WARN，用户改好文件再刷新即可。</summary>
    [Theory]
    [InlineData("{ 这不是 JSON")]
    [InlineData("")]
    [InlineData("   ")]
    public void Load_CorruptFile_ReturnsEmptyWithoutThrowing(string content)
    {
        File.WriteAllText(Path.Combine(_dir, "my-problems.json"), content);

        var list = NewService().Load();

        Assert.Empty(list);
    }

    [Fact]
    public void Load_EmptyProblemsArrayIsEmpty()
    {
        File.WriteAllText(Path.Combine(_dir, "my-problems.json"), """{ "problems": [] }""");
        Assert.Empty(NewService().Load());
    }

    // ---------------------------------------------------------------- 写示例文件

    [Fact]
    public void WriteStarterFile_CreatesParseableSample()
    {
        var service = NewService();

        Assert.True(service.WriteStarterFile());
        Assert.True(service.FileExists);

        var list = service.Load();
        Assert.Single(list);
        Assert.Equal(MyKnowledgeBaseCore.MineCategoryId, list[0].Category);
        Assert.False(string.IsNullOrWhiteSpace(list[0].Title));
    }

    /// <summary>示例文件只在缺失时创建：不能把用户改过的东西覆盖掉。</summary>
    [Fact]
    public void WriteStarterFile_DoesNotOverwriteExisting()
    {
        var service = NewService();
        File.WriteAllText(service.FilePath, """[{"id":"mine","title":"我自己的"}]""");

        Assert.False(service.WriteStarterFile());
        Assert.Equal("我自己的", service.Load()[0].Title);
    }

    /// <summary>示例条目本身必须能通过合并校验（分类是保留分类、有 id 与标题）。</summary>
    [Fact]
    public void StarterFile_MergesIntoKnowledgeBase()
    {
        var service = NewService();
        service.WriteStarterFile();

        var baseline = new ProblemData
        {
            Version = "2.2",
            Categories = new List<Category> { new() { Id = "audio", Title = "音频" } },
            Problems = new List<Problem> { new() { Id = "au-mic", Title = "麦克风没声音" } },
        };

        var merged = MyKnowledgeBaseCore.Merge(baseline, service.Load(), "我的条目");

        Assert.Equal(2, merged.Problems.Count);
        Assert.Contains(merged.Problems, p => p.IsLocal);
        Assert.Contains(merged.Categories, c => c.Id == MyKnowledgeBaseCore.MineCategoryId);
    }

    // ---------------------------------------------------------------- 往返

    /// <summary>完整闭环：写示例 → 用户改成自己的条目 → 读回来 → 并入知识库。</summary>
    [Fact]
    public void FullLoop_UserEditsFileAndItShowsUp()
    {
        var service = NewService();
        service.WriteStarterFile();

        // 用户照着示例改（这里直接覆盖成自己的两条）
        File.WriteAllText(service.FilePath, """
        {
          "version": "1.0",
          "problems": [
            { "id": "my-cam", "title": "我的摄像头裁剪参数", "category": "video", "symptoms": ["边缘有黑边"], "synonyms": ["裁剪"] },
            { "id": "my-bgm", "title": "BGM 卡顿的解决办法", "category": "不存在" }
          ]
        }
        """);

        var mine = service.Load();
        Assert.Equal(2, mine.Count);
        Assert.Contains("裁剪", mine[0].Synonyms!);

        var baseline = new ProblemData
        {
            Version = "2.2",
            Categories = new List<Category> { new() { Id = "video", Title = "画面" } },
            Problems = new List<Problem> { new() { Id = "vi-black", Title = "黑屏" } },
        };
        var merged = MyKnowledgeBaseCore.Merge(baseline, mine, "我的条目");

        Assert.Equal(3, merged.Problems.Count);
        Assert.Equal("video", merged.Problems.Single(p => p.Id == "my-cam").Category);
        // 分类名写错的那条归到保留分类，而不是消失
        Assert.Equal(MyKnowledgeBaseCore.MineCategoryId, merged.Problems.Single(p => p.Id == "my-bgm").Category);
        Assert.Contains(merged.Categories, c => c.Id == MyKnowledgeBaseCore.MineCategoryId);
    }
}
