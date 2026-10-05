using System.Linq;
using OBS_Helper.Wpf.Services.ObsConfig;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// V2.9.4：OBS 可执行文件定位的纯逻辑（解析与探测分离）。
///
/// 这一块最怕的错法：
/// <list type="bullet">
///   <item><c>DisplayIcon</c> 里的引号 / <c>,0</c> 尾巴没剥干净，或者按逗号硬切把带逗号的目录名切坏；</item>
///   <item>装根目录拼出双反斜杠、或把不是 exe 的路径（.txt / 无扩展名）当成可执行文件；</item>
///   <item>候选去重不忽略大小写（同一个 exe 拉两次），或把「先注册表、后安装目录」的优先级顺序打乱；</item>
///   <item>自己去读磁盘判断存在性 —— 那样单测在无 OBS 的机器上就钉不住行为。</item>
/// </list>
///
/// 以下口径由生产端 <c>ObsLaunchCore</c> 确认：
/// <list type="number">
///   <item><c>ExeFromDisplayIcon</c>：先去首尾空白；带引号则取到「第二个引号」为止（引号外的图标索引整段丢弃）；
///     不带引号时只剥「末尾的 ,数字」；最后必须看起来是 <c>.exe</c>，否则 null；空 / 空白 → null；</item>
///   <item><c>ExeFromInstallDir</c>：纯字符串推导、<b>不查磁盘</b>，返回优先级最高的候选
///     （现行布局恒为 <c>根\bin\64bit\obs64.exe</c>）；空 / 空白 → null。
///     32 位 / 便携 / 历史布局的存在性判断走 <c>ResolveCandidates</c> 的注入谓词；</item>
///   <item><c>ResolveCandidates</c>：保序、大小写不敏感去重（首个胜出）、
///     只保留注入 <c>exists</c> 认账的项，且 <c>exists</c> 抛异常按「不存在」处理；</item>
///   <item>非 <c>.exe</c> / 空白路径在调用 <c>exists</c> 之前就被丢掉。</item>
/// </list>
///
/// 本文件只用纯字符串与注入谓词：不碰注册表、不碰文件系统、不启动进程
/// （唯一涉及路径的一处是「断言实现没有自己探测磁盘」，用的是临时目录下的随机不存在路径）。
/// </summary>
public class ObsLaunchCoreTests
{
    // ------------------------------------------------------------ DisplayIcon

    [Fact]
    public void ExeFromDisplayIcon_AcceptsAllFourRealWorldShapes()
    {
        const string expected = @"C:\a\obs64.exe";

        Assert.Equal(expected, ObsLaunchCore.ExeFromDisplayIcon(@"""C:\a\obs64.exe"",0"));
        Assert.Equal(expected, ObsLaunchCore.ExeFromDisplayIcon(@"""C:\a\obs64.exe"""));
        Assert.Equal(expected, ObsLaunchCore.ExeFromDisplayIcon(@"C:\a\obs64.exe,0"));
        Assert.Equal(expected, ObsLaunchCore.ExeFromDisplayIcon(@"C:\a\obs64.exe"));
    }

    [Fact]
    public void ExeFromDisplayIcon_KeepsCommasAndSpacesThatBelongToThePath()
    {
        const string expected = @"D:\OBS, backup\bin\64bit\obs64.exe";

        // 带引号时引号内的内容整段保留
        Assert.Equal(expected, ObsLaunchCore.ExeFromDisplayIcon(@"""D:\OBS, backup\bin\64bit\obs64.exe"",0"));
        // 不带引号时只能剥「末尾的 ,数字」，不能按第一个逗号硬切
        Assert.Equal(expected, ObsLaunchCore.ExeFromDisplayIcon(@"D:\OBS, backup\bin\64bit\obs64.exe,0"));
    }

    [Fact]
    public void ExeFromDisplayIcon_TrimsOuterWhitespace()
        => Assert.Equal(
            @"C:\a\obs64.exe",
            ObsLaunchCore.ExeFromDisplayIcon("  \"C:\\a\\obs64.exe\",0  "));

    [Fact]
    public void ExeFromDisplayIcon_ReturnsNullWhenTheValueIsMissingOrNotAnExe()
    {
        Assert.Null(ObsLaunchCore.ExeFromDisplayIcon(null));
        Assert.Null(ObsLaunchCore.ExeFromDisplayIcon(""));
        Assert.Null(ObsLaunchCore.ExeFromDisplayIcon("   "));
        Assert.Null(ObsLaunchCore.ExeFromDisplayIcon(@"C:\a\readme.txt"));
        Assert.Null(ObsLaunchCore.ExeFromDisplayIcon(@"C:\a\obs64"));               // 没有扩展名
        Assert.Null(ObsLaunchCore.ExeFromDisplayIcon(@"C:\a\obs64.exe,notanumber")); // 尾巴不是图标索引 → 整体不是 exe
    }

    // ------------------------------------------------------------ 安装根目录推导

    [Fact]
    public void ExeFromInstallDir_UsesTheCurrent64BitLayout()
    {
        Assert.Equal(
            @"C:\Program Files\obs-studio\bin\64bit\obs64.exe",
            ObsLaunchCore.ExeFromInstallDir(@"C:\Program Files\obs-studio"));
    }

    [Fact]
    public void ExeFromInstallDir_DoesNotDoubleTheSeparatorOrKeepQuotes()
    {
        Assert.Equal(
            @"C:\Program Files\obs-studio\bin\64bit\obs64.exe",
            ObsLaunchCore.ExeFromInstallDir(@"C:\Program Files\obs-studio\"));
        Assert.Equal(
            @"C:\Program Files\obs-studio\bin\64bit\obs64.exe",
            ObsLaunchCore.ExeFromInstallDir(@"""C:\Program Files\obs-studio"""));
    }

    [Fact]
    public void ExeFromInstallDir_ReturnsNullWhenTheDirectoryIsUnknown()
    {
        Assert.Null(ObsLaunchCore.ExeFromInstallDir(null));
        Assert.Null(ObsLaunchCore.ExeFromInstallDir(""));
        Assert.Null(ObsLaunchCore.ExeFromInstallDir("   "));
    }

    // ------------------------------------------------------------ 候选汇总（探测与判定分离）

    [Fact]
    public void ResolveCandidates_DropsCandidatesThePredicateRejects()
    {
        var registry = new ObsInstallCandidate(@"C:\OBS\bin\64bit\obs64.exe", ObsLaunchCore.SourceRegistry, true);
        var stale = new ObsInstallCandidate(@"C:\Stale\obs64.exe", ObsLaunchCore.SourceInstallDir, false);
        var portable = new ObsInstallCandidate(@"D:\Portable\obs64.exe", "portable", false);

        var resolved = ObsLaunchCore.ResolveCandidates(new[] { registry, stale, portable }, p => p != stale.ExePath);

        Assert.Equal(new[] { registry.ExePath, portable.ExePath }, resolved.Select(r => r.ExePath));
    }

    [Fact]
    public void ResolveCandidates_PreservesTheCallersPriorityOrder()
    {
        var root = @"C:\OBS";
        var candidates = new[]
        {
            new ObsInstallCandidate(Path.Combine(root, "bin", "64bit", "obs64.exe"), ObsLaunchCore.SourceInstallDir, false),
            new ObsInstallCandidate(Path.Combine(root, "bin", "64bit", "obs32.exe"), ObsLaunchCore.SourceInstallDir, false),
            new ObsInstallCandidate(Path.Combine(root, "obs64.exe"), ObsLaunchCore.SourceInstallDir, false),
            new ObsInstallCandidate(Path.Combine(root, "bin", "obs64.exe"), ObsLaunchCore.SourceInstallDir, false),
        };

        var resolved = ObsLaunchCore.ResolveCandidates(candidates, _ => true);

        // 顺序即优先级：谁先被找到就用谁，不能被排序或哈希顺序打乱
        Assert.Equal(candidates.Select(c => c.ExePath), resolved.Select(c => c.ExePath));
    }

    [Fact]
    public void ResolveCandidates_KeepsMetadataVerbatim()
    {
        var only = new ObsInstallCandidate(@"C:\OBS\bin\64bit\obs64.exe", ObsLaunchCore.SourceRegistry, true);

        var resolved = ObsLaunchCore.ResolveCandidates(new[] { only }, _ => true);

        Assert.Single(resolved);
        Assert.Equal(only, resolved[0]);           // 路径、来源、是否注册表来源都不能丢
        Assert.Equal(ObsLaunchCore.SourceRegistry, resolved[0].Source);
        Assert.True(resolved[0].FromRegistry);
    }

    [Fact]
    public void ResolveCandidates_DeduplicatesCaseInsensitivelyKeepingTheFirstOccurrence()
    {
        var registry = new ObsInstallCandidate(@"C:\OBS\bin\64bit\obs64.exe", ObsLaunchCore.SourceRegistry, true);
        var installDir = new ObsInstallCandidate(@"c:\obs\BIN\64bit\OBS64.EXE", ObsLaunchCore.SourceInstallDir, false);

        var resolved = ObsLaunchCore.ResolveCandidates(new[] { registry, installDir }, _ => true);

        // Windows 路径大小写不敏感：同一个 exe 不能出现两次（否则会拉起两次 / 归属来源混乱）
        Assert.Single(resolved);
        Assert.Equal(ObsLaunchCore.SourceRegistry, resolved[0].Source);
        Assert.True(resolved[0].FromRegistry);
    }

    [Fact]
    public void ResolveCandidates_DoesNotMergeDifferentPaths()
    {
        var a = new ObsInstallCandidate(@"C:\OBS\bin\64bit\obs64.exe", ObsLaunchCore.SourceRegistry, true);
        var b = new ObsInstallCandidate(@"D:\OBS\bin\64bit\obs64.exe", ObsLaunchCore.SourceInstallDir, false);

        Assert.Equal(2, ObsLaunchCore.ResolveCandidates(new[] { a, b }, _ => true).Count);
    }

    [Fact]
    public void ResolveCandidates_DropsNonExePathsWithoutAskingThePredicate()
    {
        var probed = new List<string>();
        var raw = new[]
        {
            new ObsInstallCandidate(@"C:\OBS\readme.txt", ObsLaunchCore.SourceInstallDir, false),
            new ObsInstallCandidate("   ", ObsLaunchCore.SourceInstallDir, false),
            new ObsInstallCandidate(@"C:\OBS\bin\64bit\obs64.exe", ObsLaunchCore.SourceInstallDir, false),
        };

        var resolved = ObsLaunchCore.ResolveCandidates(raw, p => { probed.Add(p); return true; });

        Assert.Single(resolved);
        Assert.Equal(new[] { @"C:\OBS\bin\64bit\obs64.exe" }, probed);   // 非 exe / 空白不探测
    }

    [Fact]
    public void ResolveCandidates_TreatsAPredicateThatThrowsAsNotFound()
    {
        var raw = new[] { new ObsInstallCandidate(@"C:\OBS\bin\64bit\obs64.exe", ObsLaunchCore.SourceRegistry, true) };

        // exists 来自注册表 / 目录探测，抛异常不能把「开始简单录像」整条路带崩
        var resolved = ObsLaunchCore.ResolveCandidates(raw, _ => throw new InvalidOperationException("probe failed"));

        Assert.Empty(resolved);
    }

    [Fact]
    public void ResolveCandidates_TrustsTheInjectedPredicateInsteadOfProbingTheDisk()
    {
        // 这个路径不存在（临时目录下的随机子目录）：只要注入的 exists 说「在」，就必须留下 ——
        // 说明实现没有自己去 File.Exists，否则单测在无 OBS 的机器上根本钉不住行为
        var ghost = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "obs64.exe");
        var raw = new[] { new ObsInstallCandidate(ghost, ObsLaunchCore.SourceRegistry, true) };

        var resolved = ObsLaunchCore.ResolveCandidates(raw, _ => true);

        Assert.Single(resolved);
        Assert.Equal(ghost, resolved[0].ExePath);
    }

    [Fact]
    public void ResolveCandidates_ReturnsNothingForNoInput()
        => Assert.Empty(ObsLaunchCore.ResolveCandidates(Array.Empty<ObsInstallCandidate>(), _ => true));
}
