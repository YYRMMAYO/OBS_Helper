using OBS_Helper.Wpf.Services.Knowledge;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// 本地知识库的**恶意/手写输入**容忍度（V3.0 / D8，第三轮验证新增）。
///
/// 这一项的全部意义是「用户手写 JSON」——而手写就一定会出错。服务里的注释承诺
/// 「单条坏数据只跳过它，绝不因为一条写错就让整份本地知识库消失」，这里把这句话钉成断言。
/// </summary>
public class MyKnowledgeBaseRobustnessTests : IDisposable
{
    private readonly string _dir;

    public MyKnowledgeBaseRobustnessTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "obs-helper-kb-rb-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 清理失败无妨 */ }
    }

    private MyKnowledgeBaseService NewService() => new(_dir);

    private void Write(string json) => File.WriteAllText(Path.Combine(_dir, "my-problems.json"), json);

    /// <summary>一条条目的字段类型写错（手写常见），不能连累其它条目。</summary>
    [Fact]
    public void Load_OneEntryWithWrongFieldType_DoesNotKillTheFile()
    {
        Write("""
        {
          "problems": [
            { "id": "good-1", "title": "第一条好数据" },
            { "id": "bad", "title": "字段类型错的值", "symptoms": 12345 },
            { "id": "good-2", "title": "第二条好数据" }
          ]
        }
        """);

        var list = NewService().Load();

        Assert.Contains(list, p => p.Id == "good-1");
        Assert.Contains(list, p => p.Id == "good-2");
    }

    /// <summary>数组里混入 null / 非对象元素（手写多一个逗号、复制粘贴出错）也不能整份失败。</summary>
    [Fact]
    public void Load_NonObjectElementsAreSkipped()
    {
        Write("""
        { "problems": [ { "id": "good", "title": "好数据" }, null, 42, "字符串" ] }
        """);

        var list = NewService().Load();
        Assert.Contains(list, p => p.Id == "good");
    }

    /// <summary>超大文件（几千条）要能读出来且不抛。</summary>
    [Fact]
    public void Load_LargeFileIsHandled()
    {
        var items = string.Join(",", Enumerable.Range(0, 3000)
            .Select(i => $$"""{ "id": "my-{{i}}", "title": "条目 {{i}}" }"""));
        Write($$"""{ "problems": [ {{items}} ] }""");

        var list = NewService().Load();

        Assert.Equal(3000, list.Count);
    }

    /// <summary>
    /// 存储目录**本身是个文件**（用户误建 / 环境损坏）时，创建示例文件必须优雅失败而不是抛出来。
    /// 这条走的是真实的 <c>Directory.CreateDirectory</c> 异常路径。
    /// </summary>
    [Fact]
    public void WriteStarterFile_FailsGracefullyWhenDirectoryIsAFile()
    {
        var blocked = Path.Combine(_dir, "blocked");
        File.WriteAllText(blocked, "我是一个文件，不是目录");

        var service = new MyKnowledgeBaseService(blocked);

        Assert.False(service.WriteStarterFile());
        Assert.False(string.IsNullOrWhiteSpace(service.LastError));
        Assert.Empty(service.Load());   // 读也不会抛
    }

    /// <summary>路径不存在时，读操作一律返回空而不是抛。</summary>
    [Fact]
    public void Load_NonexistentDirectoryReturnsEmpty()
    {
        var service = new MyKnowledgeBaseService(Path.Combine(_dir, "does-not-exist", "deeper"));
        Assert.Empty(service.Load());
    }

    /// <summary>超长字段（用户粘了一大段）不能让加载失败。</summary>
    [Fact]
    public void Load_VeryLongFieldsAreHandled()
    {
        var longText = new string('长', 50_000);
        Write($$"""{ "problems": [ { "id": "my-long", "title": "{{longText}}" } ] }""");

        var list = NewService().Load();

        Assert.Single(list);
        Assert.Equal(50_000, list[0].Title.Length);
    }
}