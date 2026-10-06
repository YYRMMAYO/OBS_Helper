using OBS_Helper.Wpf.Services.ObsConfig;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// 路径护栏（<see cref="ObsSafePath"/>）的回归测试。
///
/// 覆盖 V3.0 修掉的两条：
/// ① <c>basic.profiledir</c> 可被根化 / 含 <c>..</c>（而它来自可被「导入备份包」改写的 global.ini）→ <see cref="ObsSafePath.SafeProfileDir"/>；
/// ② 闸 3 以前只按「目录名是不是 obs-studio」判定，两头都不安全 → 现在按登记的可信根判定。
/// </summary>
public class ObsSafePathTests : IDisposable
{
    private readonly List<string> _created = new();

    private string NewDir(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), "obshelper_safepath_" + name + "_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _created.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        foreach (var d in _created)
        {
            try { Directory.Delete(d, recursive: true); } catch (Exception) { }
        }
    }

    // ---------------------------------------------------------------- 净化 profile 目录名

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("..")]
    public void SafeProfileDir_RejectsEmptyAndDotNames(string? raw)
        => Assert.Null(ObsSafePath.SafeProfileDir(raw));

    [Fact]
    public void SafeProfileDir_KeepsPlainName()
        => Assert.Equal("未命名", ObsSafePath.SafeProfileDir("未命名"));

    /// <summary>根化路径必须被削成最后一段 —— 否则 Path.Combine 会丢弃配置目录前缀，写到系统目录去。</summary>
    [Theory]
    [InlineData(@"C:\Windows\System32", "System32")]
    [InlineData(@"\\\\server\\share\\x", "x")]
    [InlineData(@"..\..\..\Windows", "Windows")]
    [InlineData(@"a/b/c", "c")]
    public void SafeProfileDir_ReducesToLeafName(string raw, string expected)
        => Assert.Equal(expected, ObsSafePath.SafeProfileDir(raw));

    // ---------------------------------------------------------------- 可信根

    [Fact]
    public void AssertWritable_UnderObsStudioNamedRoot_Passes()
    {
        var root = Path.Combine(NewDir("ok"), "obs-studio");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "basic"));

        ObsSafePath.AssertWritable(Path.Combine(root, "basic", "profiles", "未命名", "basic.ini"), root);
    }

    [Fact]
    public void AssertWritable_OutsideRoot_Throws()
    {
        var root = Path.Combine(NewDir("ok"), "obs-studio");
        Directory.CreateDirectory(root);

        Assert.Throws<ObsSafePathException>(() =>
            ObsSafePath.AssertWritable(Path.Combine(NewDir("outside"), "evil.ini"), root));
    }

    [Fact]
    public void AssertWritable_PathTraversal_Throws()
    {
        var root = Path.Combine(NewDir("ok"), "obs-studio");
        Directory.CreateDirectory(root);

        var escaped = Path.Combine(root, "basic", "..", "..", "..", "escaped.ini");
        Assert.Throws<ObsSafePathException>(() => ObsSafePath.AssertWritable(escaped, root));
    }

    /// <summary>
    /// 不为 obs-studio 的目录**默认不可信**（这正是「手动指定配置目录 + 彻底重置必然被拒」的原因，
    /// 见 M14）；一旦由 ObsPathService 登记（= 用户在设置里确认过），就必须放行。
    /// </summary>
    [Fact]
    public void AssertWritable_ArbitraryRoot_RequiresRegistration()
    {
        var root = NewDir("custom-cfg");
        var target = Path.Combine(root, "basic.ini");

        Assert.Throws<ObsSafePathException>(() => ObsSafePath.AssertWritable(target, root));

        ObsSafePath.RegisterTrustedRoot(root);
        Assert.True(ObsSafePath.IsTrustedRoot(root));
        ObsSafePath.AssertWritable(target, root);   // 登记后不再抛
    }

    [Fact]
    public void IsTrustedRoot_IsFalseForUnrelatedPath()
        => Assert.False(ObsSafePath.IsTrustedRoot(NewDir("unrelated")));

    // ---------------------------------------------------------------- 删除闸

    [Fact]
    public void AssertDeletable_RootItself_Throws()
    {
        var root = Path.Combine(NewDir("del"), "obs-studio");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "basic"));

        Assert.Throws<ObsSafePathException>(() => ObsSafePath.AssertDeletable(root, root));
    }

    [Fact]
    public void AssertDeletable_NeverTouchesLogsCrashesThemes()
    {
        var root = Path.Combine(NewDir("del2"), "obs-studio");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "basic"));
        foreach (var name in new[] { "logs", "crashes", "themes" })
        {
            var dir = Path.Combine(root, name);
            Directory.CreateDirectory(dir);
            Assert.Throws<ObsSafePathException>(() => ObsSafePath.AssertDeletable(dir, root));
        }
    }

    [Fact]
    public void AssertDeletable_OrdinarySubdir_Passes()
    {
        var root = Path.Combine(NewDir("del3"), "obs-studio");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "basic"));
        var scenes = Path.Combine(root, "basic", "scenes");
        Directory.CreateDirectory(scenes);

        ObsSafePath.AssertDeletable(scenes, root);
    }
}
