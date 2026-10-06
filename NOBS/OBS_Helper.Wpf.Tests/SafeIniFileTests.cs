using OBS_Helper.Wpf.Services.ObsConfig;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// 受护栏保护的 OBS 配置原子写入（<see cref="SafeIniFile"/>）的回归测试。
///
/// 这层是 V3.0 收口出来的唯一写法：先过 <see cref="ObsSafePath"/> 护栏，再留 .bak，最后临时文件 + 原子替换。
/// 审查指出它此前没有任何单测，而「写入 OBS 配置」恰恰是风险最高的动作类别。
/// </summary>
public class SafeIniFileTests : IDisposable
{
    private readonly string _root;
    private readonly string _configDir;

    public SafeIniFileTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "obshelper_safeini_" + Guid.NewGuid().ToString("N"));
        _configDir = Path.Combine(_root, "obs-studio");
        Directory.CreateDirectory(Path.Combine(_configDir, "basic", "profiles", "未命名"));
        _createdRoots.Add(_root);
    }

    private readonly List<string> _createdRoots = new();

    public void Dispose()
    {
        foreach (var r in _createdRoots)
        {
            try { Directory.Delete(r, recursive: true); } catch (Exception) { }
        }
    }

    private string IniPath => Path.Combine(_configDir, "basic", "profiles", "未命名", "basic.ini");

    [Fact]
    public void Write_CreatesFileWithContent()
    {
        SafeIniFile.Write(IniPath, "[Output]\nMode=Advanced\n", _configDir);

        Assert.True(File.Exists(IniPath));
        Assert.Equal("[Output]\nMode=Advanced\n", File.ReadAllText(IniPath));
        // 新文件没有「原内容」，不该凭空造一个 .bak
        Assert.False(File.Exists(IniPath + SafeIniFile.BackupSuffix));
    }

    [Fact]
    public void Write_KeepsPreviousContentInBak()
    {
        SafeIniFile.Write(IniPath, "original", _configDir);
        SafeIniFile.Write(IniPath, "updated", _configDir);

        Assert.Equal("updated", File.ReadAllText(IniPath));
        Assert.Equal("original", File.ReadAllText(IniPath + SafeIniFile.BackupSuffix));
    }

    [Fact]
    public void Write_LeavesNoTempFile()
    {
        SafeIniFile.Write(IniPath, "x", _configDir);
        Assert.False(File.Exists(IniPath + ".obshelper.tmp"));
    }

    /// <summary>越界写入必须被护栏拦住，且**一个文件都不能被创建**（fail-closed）。</summary>
    [Fact]
    public void Write_OutsideTrustedRoot_ThrowsAndCreatesNothing()
    {
        var outside = Path.Combine(_root, "outside", "basic.ini");

        Assert.Throws<ObsSafePathException>(() => SafeIniFile.Write(outside, "x", _configDir));
        Assert.False(File.Exists(outside));
    }

    /// <summary>目标落在配置目录之外（绝对路径逃逸）同样要被拦住。</summary>
    [Fact]
    public void Write_AbsoluteEscape_Throws()
    {
        var escaped = Path.Combine(_configDir, "basic", "..", "..", "..", "escaped.ini");
        Assert.Throws<ObsSafePathException>(() => SafeIniFile.Write(escaped, "x", _configDir));
        Assert.False(File.Exists(Path.Combine(_root, "escaped.ini")));
    }

    /// <summary>覆盖写后内容完整（原子替换不会留下半截文件）。</summary>
    [Fact]
    public void Write_OverwriteReplacesWholeContent()
    {
        SafeIniFile.Write(IniPath, new string('a', 5000), _configDir);
        SafeIniFile.Write(IniPath, "short", _configDir);

        Assert.Equal("short", File.ReadAllText(IniPath));
        Assert.Equal(5000, File.ReadAllText(IniPath + SafeIniFile.BackupSuffix).Length);
    }
}
