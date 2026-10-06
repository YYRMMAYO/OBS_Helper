using System.Text.RegularExpressions;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// 内嵌资产「区域性后缀」守护（V3.0 / F5）。
///
/// 背景是一次真实的静默失效：`Assets\plugins.en-US.json` 这样的文件名里带了 `.en-US`，
/// MSBuild 会把它**当成区域性后缀**，默认编进**卫星程序集**
/// （`en-US\OBS_Helper.resources.dll`）而不是主程序集。而 <c>ContentAssets.ReadEmbedded</c>
/// 只查主程序集 —— 于是英文界面下知识库 / 插件目录 / 场景模板 / 排障指引
/// **全部静默回退成中文**，只在日志里留一条 WARN，界面上完全看不出来。
///
/// 这个测试直接读主工程 csproj 的文本，钉住两件事：
/// <list type="number">
///   <item>所有「基名含区域性后缀」的内嵌资产都必须带 <c>WithCulture="false"</c>；</item>
///   <item>四份英文资产确实都被声明为内嵌资源（少一份就会回退中文）。</item>
/// </list>
/// 纯文本断言，不需要编译产物，因此在任何环境下都能跑。
/// </summary>
public class EmbeddedAssetCultureTests
{
    private static string CsprojPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "OBS_Helper.slnx")))
                return Path.Combine(dir.FullName, "OBS_Helper.Wpf", "OBS_Helper.Wpf.csproj");
            dir = dir.Parent;
        }
        throw new InvalidOperationException("找不到主工程 csproj（OBS_Helper.slnx 不在祖先目录里）");
    }

    private static List<string> EmbeddedResourceLines()
    {
        var text = File.ReadAllText(CsprojPath());
        return Regex.Matches(text, @"<EmbeddedResource\b[^>]*>")
            .Select(m => m.Value)
            .ToList();
    }

    /// <summary>英文资产基名（与 <c>ContentAssets</c> 的拼法一致）。</summary>
    public static readonly string[] EnglishAssets =
    {
        "problems.en-US.json",
        "plugins.en-US.json",
        "scene_templates.en-US.json",
        "troubleshooting.en-US.md"
    };

    /// <summary>关键守护：带区域性后缀的内嵌资产必须显式留在主程序集里。</summary>
    [Fact]
    public void CultureSuffixedEmbeddedAssets_MustSetWithCultureFalse()
    {
        var offenders = new List<string>();

        foreach (var line in EmbeddedResourceLines())
        {
            // 只关心「文件名里带 x-Y 形式区域性后缀」的那些（.en-US. / .zh-Hant. …）
            if (!Regex.IsMatch(line, @"Assets\\[^""]*\.[a-z]{2}-[A-Za-z]{2,4}\.")) continue;
            if (line.Contains("WithCulture=\"false\"", StringComparison.OrdinalIgnoreCase)) continue;
            offenders.Add(line);
        }

        Assert.True(offenders.Count == 0,
            "以下内嵌资源缺少 WithCulture=\"false\"，会被编进卫星程序集、运行时读不到：\n"
            + string.Join("\n", offenders));
    }

    /// <summary>四份英文资产缺一不可：少一份就有一块内容在英文界面下回退成中文。</summary>
    [Theory]
    [InlineData("problems.en-US.json")]
    [InlineData("plugins.en-US.json")]
    [InlineData("scene_templates.en-US.json")]
    [InlineData("troubleshooting.en-US.md")]
    public void EveryEnglishAssetIsDeclared(string assetName)
    {
        var expectedLogicalName = "LogicalName=\"OBS_Helper.Wpf.Assets." + assetName + "\"";
        Assert.Contains(EmbeddedResourceLines(),
            l => l.Contains("Assets\\" + assetName, StringComparison.Ordinal)
              && l.Contains(expectedLogicalName, StringComparison.Ordinal));
    }

    /// <summary>LogicalName 必须与 ContentAssets 的拼法一致（前缀 + 文件名），否则运行时按名找不到。</summary>
    [Fact]
    public void LogicalNamesMatchContentAssetsConvention()
    {
        foreach (var line in EmbeddedResourceLines())
        {
            var file = Regex.Match(line, @"Include=""Assets\\([^""]+)""");
            var logical = Regex.Match(line, @"LogicalName=""([^""]+)""");
            if (!file.Success || !logical.Success) continue;

            Assert.Equal("OBS_Helper.Wpf.Assets." + file.Groups[1].Value, logical.Groups[1].Value);
        }
    }
}
