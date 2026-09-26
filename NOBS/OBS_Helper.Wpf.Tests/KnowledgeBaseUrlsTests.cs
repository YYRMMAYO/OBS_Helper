using OBS_Helper.Wpf.Services.Update;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// 知识库 raw 主通道地址的测试（V2.9.1）。
///
/// 背景：这两条地址此前漏了仓库里的 <c>NOBS/</c> 一层，恒返回 404，
/// 「raw 主通道」自上线起从未生效、一直靠 Release 资产兜底 —— 而且**没有任何编译期信号**。
/// 单测在这里做两件不联网也能做的事：
/// <list type="number">
///   <item>形状：https + raw.githubusercontent.com + master 分支 + 以仓库相对路径结尾；</item>
///   <item>落地：URL 的仓库相对路径在本机源码树里**真的存在**（源码树在就查，是这套布局的最强离线校验）。</item>
/// </list>
/// 真实网络可用性无法在单测里保证（CI 可能无外网），由发版前的人工联网复核确认（实测 HTTP 200）。
/// </summary>
public class KnowledgeBaseUrlsTests
{
    [Fact]
    public void RepoRelativePaths_PointAtAssetFiles()
    {
        Assert.Equal("NOBS/OBS_Helper.Wpf/Assets/problems.json", KnowledgeBaseUrls.RepoRelativeProblemsPath);
        Assert.Equal("NOBS/OBS_Helper.Wpf/Assets/plugins.json", KnowledgeBaseUrls.RepoRelativePluginsPath);
    }

    [Fact]
    public void RawUrls_AreWellFormed()
    {
        foreach (var url in new[] { KnowledgeBaseUrls.RawProblems, KnowledgeBaseUrls.RawPlugins })
        {
            Assert.StartsWith("https://raw.githubusercontent.com/", url);
            Assert.Contains("/" + KnowledgeBaseUrls.Branch + "/", url);
            Assert.DoesNotContain("//", url["https://".Length..]);   // 拼接后不该出现空路径段
            Assert.DoesNotContain(" ", url);
            Assert.EndsWith(".json", url);
            Assert.True(Uri.TryCreate(url, UriKind.Absolute, out var uri));
            Assert.Equal("raw.githubusercontent.com", uri.Host);
        }

        Assert.EndsWith(KnowledgeBaseUrls.RepoRelativeProblemsPath, KnowledgeBaseUrls.RawProblems);
        Assert.EndsWith(KnowledgeBaseUrls.RepoRelativePluginsPath, KnowledgeBaseUrls.RawPlugins);
    }

    [Fact]
    public void RawUrls_UseRepoSlugAndBranch()
    {
        Assert.Equal("YYRMMAYO/OBS_Helper", KnowledgeBaseUrls.RepoSlug);
        Assert.Equal("master", KnowledgeBaseUrls.Branch);
        Assert.Equal("https://github.com/YYRMMAYO/OBS_Helper", KnowledgeBaseUrls.RepoWebUrl);

        Assert.Equal(KnowledgeBaseUrls.RawBase + KnowledgeBaseUrls.RepoRelativeProblemsPath,
            KnowledgeBaseUrls.RawProblems);
        Assert.Equal(KnowledgeBaseUrls.RawBase + KnowledgeBaseUrls.RepoRelativePluginsPath,
            KnowledgeBaseUrls.RawPlugins);
    }

    /// <summary>
    /// 关键回归测试：URL 里的仓库相对路径必须能在本机源码树里找到对应文件。
    /// 少了 <c>NOBS/</c> 前缀的那一版（V2.9 及以前）会在这里直接失败。
    /// </summary>
    [Fact]
    public void RawUrls_MatchFilesInSourceTree()
    {
        var repoRoot = FindRepoRoot();
        Assert.NotNull(repoRoot);

        // 仓库根（含 NOBS/ 那一层）：源码树是 <repoRoot>\NOBS\OBS_Helper.Wpf\...
        var checkoutsRoot = Directory.GetParent(repoRoot)!.FullName;
        Assert.True(Directory.Exists(Path.Combine(checkoutsRoot, "NOBS")),
            $"仓库根下找不到 NOBS 目录：{checkoutsRoot}");

        foreach (var relativePath in new[]
                 {
                     KnowledgeBaseUrls.RepoRelativeProblemsPath,
                     KnowledgeBaseUrls.RepoRelativePluginsPath
                 })
        {
            var onDisk = Path.Combine(checkoutsRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(onDisk),
                $"raw 地址指向的文件在仓库里不存在：{relativePath}（实际查找路径 {onDisk}）"
                + " —— 这正是 V2.9 及以前「raw 主通道恒 404」的成因。");
        }
    }

    /// <summary>从测试输出目录向上找到含 OBS_Helper.slnx 的目录（即 NOBS/）。</summary>
    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "OBS_Helper.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }
}
