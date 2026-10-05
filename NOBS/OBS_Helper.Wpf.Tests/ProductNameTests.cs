using System.Text.RegularExpressions;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// 产品标识的一致性体检（V2.9.6）。
///
/// 软件名、版本号、图标这三样东西分散在四处，**任何一处漏改都不会编译报错**，
/// 而且都只在「用户装上之后」才看得见：
/// <list type="number">
///   <item>csproj 的 <c>&lt;Product&gt;</c> / <c>&lt;AssemblyTitle&gt;</c> —— exe 属性、诊断报告页脚；</item>
///   <item>Inno Setup 脚本的 <c>MyAppName</c> —— 安装向导、快捷方式、卸载项（改名时最容易漏的一处）；</item>
///   <item>文案表的 <c>app.name</c> —— 窗口标题、托盘 ToolTip、通知；</item>
///   <item><c>Assets/appicon.ico</c> —— 窗口 / 任务栏 / 托盘 / 安装包图标，缺哪一档尺寸，
///     对应位置就退回系统默认图标（小尺寸糊掉是看不出来的，只能靠尺寸断言钉住）。</item>
/// </list>
///
/// 这几条断言是 V2.9.6「更名 + 换图标」的回归闸门：改名字时**三处必须一起改**，
/// 改版本号时 csproj 与 .iss 必须一致。
/// </summary>
public class ProductNameTests
{
    /// <summary>界面上显示的产品名（V2.9.6 起）。英文界面仍是 <c>OBS Helper</c>，不随中文名变化。</summary>
    private const string ProductNameZh = "OBS帮助助手";
    private const string ProductNameEn = "OBS Helper";

    /// <summary>旧产品名（V2.9.5 及以前）。改名后文案表里**不允许**再出现它。</summary>
    private const string LegacyProductName = "排障助手";

    /// <summary>Windows Shell 会取的图标尺寸；缺一档就会在对应位置露出默认图标。</summary>
    private static readonly int[] RequiredIconSizes = [16, 24, 32, 48, 64, 128, 256];

    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "OBS_Helper.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("找不到源码根目录（OBS_Helper.slnx）");
    }

    private static string ProjectFile() => Path.Combine(SourceRoot(), "OBS_Helper.Wpf", "OBS_Helper.Wpf.csproj");
    private static string InstallerFile() => Path.Combine(SourceRoot(), "OBS_Helper.Wpf", "OBS_Helper_Setup.iss");
    private static string IconFile() => Path.Combine(SourceRoot(), "OBS_Helper.Wpf", "Assets", "appicon.ico");
    private static string Table(string lang) =>
        Path.Combine(SourceRoot(), "OBS_Helper.Wpf", "Localization", $"StringTable{lang}.cs");

    private static string Match(string text, string pattern, string what)
    {
        var m = Regex.Match(text, pattern);
        Assert.True(m.Success, $"没能从{what}里解析出目标值（正则：{pattern}）");
        return m.Groups["v"].Value.Trim();
    }

    /// <summary>显示名必须「csproj = 安装包 = 中文文案表」，英文侧固定为 OBS Helper。</summary>
    [Fact]
    public void DisplayName_IsConsistentAcrossProjectInstallerAndStringTables()
    {
        var csproj = File.ReadAllText(ProjectFile());
        var product = Match(csproj, @"<Product>(?<v>[^<]+)</Product>", "csproj");
        var assemblyTitle = Match(csproj, @"<AssemblyTitle>(?<v>[^<]+)</AssemblyTitle>", "csproj");
        var installerName = Match(File.ReadAllText(InstallerFile()), @"#define\s+MyAppName\s+""(?<v>[^""]+)""", ".iss");

        Assert.Equal(ProductNameZh, product);
        Assert.Equal(ProductNameZh, assemblyTitle);
        // 安装包显示名漏改的话，用户看到的安装向导 / 快捷方式 / 卸载项还是旧名 —— 编译与单测都不会报
        Assert.Equal(product, installerName);

        var zh = File.ReadAllText(Table("ZhHans"));
        Assert.Equal(ProductNameZh, Match(zh, @"\[""app\.name""\]\s*=\s*""(?<v>[^""]*)""", "中文文案表"));

        var en = File.ReadAllText(Table("EnUs"));
        Assert.Equal(ProductNameEn, Match(en, @"\[""app\.name""\]\s*=\s*""(?<v>[^""]*)""", "英文文案表"));
    }

    /// <summary>文案表里不能再出现旧产品名：改名漏一条，界面就会出现「顶栏新名、托盘旧名」。</summary>
    [Fact]
    public void StringTables_DoNotUseTheLegacyProductName()
    {
        foreach (var lang in new[] { "ZhHans", "EnUs" })
        {
            var text = File.ReadAllText(Table(lang));
            Assert.False(
                text.Contains(LegacyProductName, StringComparison.Ordinal),
                $"{lang} 文案表里仍有旧产品名「{LegacyProductName}」");
        }
    }

    /// <summary>版本号在 csproj 与安装包脚本之间必须一致（增量更新 / 更新检查都按版本号比较）。</summary>
    [Fact]
    public void Version_IsConsistentBetweenProjectAndInstaller()
    {
        var csproj = File.ReadAllText(ProjectFile());
        var version = Match(csproj, @"<Version>(?<v>[^<]+)</Version>", "csproj");
        var installerVersion = Match(
            File.ReadAllText(InstallerFile()), @"#define\s+MyAppVersion\s+""(?<v>[^""]+)""", ".iss");

        Assert.Equal(version, installerVersion);
        // 四段版本号（文件 / 程序集）必须以三段版本号开头，否则资源管理器里显示的还是旧版本
        Assert.StartsWith(version, Match(csproj, @"<FileVersion>(?<v>[^<]+)</FileVersion>", "csproj"));
        Assert.StartsWith(version, Match(csproj, @"<AssemblyVersion>(?<v>[^<]+)</AssemblyVersion>", "csproj"));
        Assert.True(Version.TryParse(version, out _), $"版本号无法解析：{version}");
    }

    /// <summary>应用图标必须真的存在，且含 Windows Shell 会取的全部尺寸。</summary>
    [Fact]
    public void AppIcon_ContainsAllShellSizes()
    {
        var path = IconFile();
        Assert.True(File.Exists(path), $"应用图标缺失：{path}");

        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 1024, $"图标文件过小（{bytes.Length} 字节），疑似占位文件");

        static int U16(byte[] b, int offset) => b[offset] | (b[offset + 1] << 8);

        Assert.Equal(0, U16(bytes, 0));   // reserved
        Assert.Equal(1, U16(bytes, 2));   // type = icon
        var count = U16(bytes, 4);
        Assert.True(count >= RequiredIconSizes.Length, $"图标条目只有 {count} 个，少于 {RequiredIconSizes.Length} 档尺寸");

        var sizes = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var w = bytes[6 + i * 16];      // 0 表示 256
            sizes.Add(w == 0 ? 256 : w);
        }

        foreach (var size in RequiredIconSizes)
            Assert.Contains(size, sizes);

        // 三个引用点都要在：exe 图标（ApplicationIcon）、托盘（EmbeddedResource 流式读取）、WPF 资源
        var csproj = File.ReadAllText(ProjectFile());
        Assert.True(
            Regex.Matches(csproj, @"appicon\.ico").Count >= 3,
            "csproj 里 appicon.ico 的引用少于 3 处（ApplicationIcon / EmbeddedResource / Resource）");
    }
}
