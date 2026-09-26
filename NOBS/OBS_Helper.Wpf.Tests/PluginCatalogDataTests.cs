using System.Text.Json;
using OBS_Helper.Wpf.Services.Plugins;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// 目录模型新增字段（V2.9 维护状态）的解析与展示语义。
/// </summary>
public class PluginCatalogMaintenanceTests
{
    private const string Json = """
    {
      "version": "1.4",
      "categories": [{ "key": "tool", "label": "工具", "icon": "" }],
      "plugins": [
        {
          "id": "active-one",
          "name": "Active",
          "category": "tool",
          "desc": "d",
          "url": "https://github.com/a/active",
          "repo": "a/active",
          "dlls": ["active"],
          "maintain": "active"
        },
        {
          "id": "slow-one",
          "name": "Slow",
          "category": "tool",
          "desc": "d",
          "url": "https://github.com/a/slow",
          "repo": "a/slow",
          "dlls": ["slow"],
          "maintain": "slow",
          "maintainNote": "最近提交 2024-01 · 暂无同类替代，仍可使用"
        },
        {
          "id": "legacy-one",
          "name": "Legacy",
          "category": "tool",
          "desc": "d",
          "url": "https://github.com/a/legacy",
          "repo": "a/legacy",
          "dlls": ["legacy"]
        }
      ]
    }
    """;

    [Fact]
    public void Parse_ReadsMaintenanceFields()
    {
        var data = PluginCatalogCore.Parse(Json)!;

        var active = data.Plugins.First(p => p.Id == "active-one");
        Assert.Equal("active", active.Maintain);
        Assert.False(active.IsMaintenanceSlow);
        Assert.Equal("活跃", active.MaintenanceText);
        // 「活跃且无补充说明」是常态：不占卡片版面
        Assert.False(active.HasMaintenanceInfo);

        var slow = data.Plugins.First(p => p.Id == "slow-one");
        Assert.True(slow.IsMaintenanceSlow);
        Assert.Equal("放缓 · 最近提交 2024-01 · 暂无同类替代，仍可使用", slow.MaintenanceText);
        Assert.True(slow.HasMaintenanceInfo);
    }

    [Fact]
    public void Parse_MissingMaintenanceField_TreatedAsActive()
    {
        // 旧目录 / 外部热更新文件没有该字段：不能因此被打上「维护放缓」的警示色
        var legacy = PluginCatalogCore.Parse(Json)!.Plugins.First(p => p.Id == "legacy-one");

        Assert.Equal("", legacy.Maintain);
        Assert.False(legacy.IsMaintenanceSlow);
        Assert.False(legacy.HasMaintenanceInfo);
        Assert.Equal("活跃", legacy.MaintenanceText);
    }

    [Fact]
    public void IsMaintenanceSlow_IsCaseInsensitive()
    {
        var data = PluginCatalogCore.Parse(Json)!;
        data.Plugins[0].Maintain = "SLOW";
        Assert.True(data.Plugins[0].IsMaintenanceSlow);
    }
}

/// <summary>
/// 随包发布的插件目录数据（OBS_Helper.Wpf/Assets/plugins.json）的自洽性校验（V2.9）。
///
/// 这类「数据文件」的错法编译期查不出来（id 撞车、分类写错、maintain 拼错、仓库名没归一化），
/// 但会直接表现为界面错乱、查新失效。这里对真实随包数据做校验，改数据时能立刻发现。
/// </summary>
public class PluginCatalogAssetTests
{
    private static PluginCatalogData LoadShippedCatalog()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "plugins.json");
        Assert.True(File.Exists(path), $"随包插件目录缺失：{path}");
        var data = PluginCatalogCore.Parse(File.ReadAllText(path));
        Assert.NotNull(data);
        return data!;
    }

    [Fact]
    public void ShippedCatalog_ParsesAndIsSubstantial()
    {
        var data = LoadShippedCatalog();
        Assert.False(string.IsNullOrWhiteSpace(data.Version));
        Assert.False(string.IsNullOrWhiteSpace(data.Updated));
        Assert.False(string.IsNullOrWhiteSpace(data.Note));
        Assert.True(data.Plugins.Count >= 50, $"条目数异常偏少：{data.Plugins.Count}");
        Assert.True(data.Categories.Count >= 6);
    }

    [Fact]
    public void ShippedCatalog_IdsAreUnique()
    {
        var data = LoadShippedCatalog();
        var ids = data.Plugins.Select(p => p.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain("", ids);
    }

    [Fact]
    public void ShippedCatalog_EveryEntryHasUsableFields()
    {
        foreach (var p in LoadShippedCatalog().Plugins)
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Name), $"{p.Id}: 缺 name");
            Assert.False(string.IsNullOrWhiteSpace(p.Desc), $"{p.Id}: 缺 desc");
            Assert.StartsWith("https://", p.Url);
            // repo 必须能归一化成 owner/repo，否则「下载」与版本角标都会失效
            Assert.False(string.IsNullOrWhiteSpace(p.Repo), $"{p.Id}: 缺 repo");
            Assert.Equal(p.Repo, PluginCatalogCore.NormalizeRepo(p.Repo));
            Assert.StartsWith("https://github.com/", p.Url);
        }
    }

    [Fact]
    public void ShippedCatalog_CategoriesAreDeclared()
    {
        var data = LoadShippedCatalog();
        var known = data.Categories.Select(c => c.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var p in data.Plugins)
        {
            Assert.Contains(p.Category, known);
        }
    }

    [Fact]
    public void ShippedCatalog_DllAliasesAreMatchable()
    {
        var data = LoadShippedCatalog();
        foreach (var p in data.Plugins)
        {
            foreach (var alias in p.Dlls)
            {
                // 别名要靠小写主干精确匹配，大小写或扩展名会直接让「本机体检」匹配不上
                Assert.Equal(alias, alias.Trim().ToLowerInvariant());
                Assert.DoesNotContain(".dll", alias);
                Assert.NotEqual("", alias);
            }
            Assert.Equal(p.Dlls.Count, p.Dlls.Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Fact]
    public void ShippedCatalog_MaintenanceValuesAreKnown()
    {
        foreach (var p in LoadShippedCatalog().Plugins)
        {
            Assert.Contains(p.Maintain, new[] { "active", "slow" });
            if (p.IsMaintenanceSlow)
            {
                // 标了「维护放缓」就必须给用户一个理由
                Assert.False(string.IsNullOrWhiteSpace(p.MaintainNote), $"{p.Id}: 维护放缓但缺 maintainNote");
            }
        }
    }

    [Fact]
    public void ShippedCatalog_DroppedDeadEntries_StayDropped()
    {
        // v1.4 复核剔除的条目：仓库已归档 / 长期停更且无发行包。防止被无意间加回来。
        var data = LoadShippedCatalog();
        foreach (var id in new[]
                 {
                     "spectralizer", "obs-detect", "win-capture-audio", "dynamic-delay",
                     "time-shift", "time-warp-scan", "device-switcher", "recursion-effect",
                 })
        {
            Assert.DoesNotContain(data.Plugins, p => p.Id == id);
        }
    }

    [Fact]
    public void ShippedCatalog_NoStaleRenamedOrg()
    {
        // occ-ai/* 已改名 royshil/*：继续指着旧名会多一次 302 跳转，也容易在改名失效后断链
        foreach (var p in LoadShippedCatalog().Plugins)
        {
            Assert.DoesNotContain("occ-ai/", p.Repo);
            Assert.DoesNotContain("occ-ai/", p.Url);
        }
    }

    [Fact]
    public void ShippedCatalog_JsonIsStableShape()
    {
        // 顶层字段名不能漂移：外部热更新通道与旧版本客户端都按这些字段名解析
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "plugins.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;

        foreach (var name in new[] { "version", "updated", "note", "categories", "plugins" })
            Assert.True(root.TryGetProperty(name, out _), $"顶层缺少字段：{name}");

        foreach (var entry in root.GetProperty("plugins").EnumerateArray())
        {
            foreach (var name in new[] { "id", "name", "category", "desc", "url", "repo" })
                Assert.True(entry.TryGetProperty(name, out _), $"插件条目缺少字段：{name}");
        }
    }
}
