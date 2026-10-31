using OBS_Helper.Wpf.Services.Plugins;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// X1031Y2：本机插件管理的纯逻辑。
///
/// 这些断言保护的是一条底线：**管理操作只能动插件自己的目录**。
/// 识别错一层就可能把整个插件根（甚至 OBS 安装目录）移进备份，
/// 所以扁平 / 一插件一目录 / 用户级 / 全局级 / 安装目录都要逐条钉住。
/// </summary>
public class LocalPluginManageTests
{
    private static string Root(string name) => Path.Combine(Path.GetTempPath(), "obs-helper-tests", name);

    private static string PluginsRoot(string name) => Path.Combine(Root(name), "obs-studio", "plugins");

    [Fact]
    public void Describe_NestedUserPlugin_ReturnsPluginDirectory()
    {
        var root = PluginsRoot("user");
        var pluginDir = Path.Combine(root, "obs-helper-dock");
        var dll = Path.Combine(pluginDir, "bin", "64bit", "obs-helper-dock.dll");

        var unit = LocalPluginManageCore.Describe(dll, "user");

        Assert.NotNull(unit);
        Assert.Equal(LocalPluginUnitKind.Nested, unit!.Kind);
        Assert.Equal(pluginDir, unit.PluginDirectory);
        Assert.Equal(root, unit.RootDirectory);
        Assert.Equal("obs-helper-dock", unit.PluginName);
    }

    [Fact]
    public void Describe_NestedInstallPlugin_ClassifiesInstallRoot()
    {
        var obs = Path.Combine(Path.GetTempPath(), "obs-helper-tests", "obs-studio");
        var dll = Path.Combine(obs, "obs-plugins", "obs-control-studio-bridge", "bin", "64bit",
            "obs-control-studio-bridge.dll");

        var unit = LocalPluginManageCore.Describe(dll, "install");

        Assert.NotNull(unit);
        Assert.Equal(LocalPluginUnitKind.Nested, unit!.Kind);
        Assert.Equal("install", unit.RootKind);
        Assert.Equal(Path.Combine(obs, "obs-plugins", "obs-control-studio-bridge"), unit.PluginDirectory);
    }

    [Fact]
    public void Describe_FlatObsPlugins64Bit_OnlyTargetsDllDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "obs-helper-tests", "obs-studio", "obs-plugins");
        var dll = Path.Combine(root, "64bit", "old-plugin.dll");

        var unit = LocalPluginManageCore.Describe(dll, "install");

        Assert.NotNull(unit);
        Assert.Equal(LocalPluginUnitKind.Flat, unit!.Kind);
        Assert.Equal(Path.Combine(root, "64bit"), unit.PluginDirectory);
        Assert.Equal(root, unit.RootDirectory);
        Assert.Equal("old-plugin", unit.PluginName);
    }

    [Fact]
    public void Describe_FlatPluginRoot_OnlyTargetsRootButIsStillFlat()
    {
        var root = PluginsRoot("flat-root");
        var dll = Path.Combine(root, "loose.dll");

        var unit = LocalPluginManageCore.Describe(dll, "user");

        Assert.NotNull(unit);
        Assert.Equal(LocalPluginUnitKind.Flat, unit!.Kind);
        Assert.Equal(root, unit.PluginDirectory);
        Assert.Equal(root, unit.RootDirectory);
    }

    [Fact]
    public void Describe_UnrelatedPath_IsRejected()
    {
        Assert.Null(LocalPluginManageCore.Describe(@"C:\Windows\System32\evil.dll", "install"));
        Assert.Null(LocalPluginManageCore.Describe("", "user"));
        Assert.Null(LocalPluginManageCore.Describe(null, "user"));
    }

    [Fact]
    public void MigrationTarget_UserGoesToGlobal_AndBack()
    {
        var user = PluginsRoot("user-root");
        var global = PluginsRoot("global-root");
        var dll = Path.Combine(user, "p1", "bin", "64bit", "p1.dll");
        var unit = LocalPluginManageCore.Describe(dll, "user");

        Assert.Equal(global, LocalPluginManageCore.MigrationTargetRoot(unit, user, global));

        var globalDll = Path.Combine(global, "p1", "bin", "64bit", "p1.dll");
        var globalUnit = LocalPluginManageCore.Describe(globalDll, "global");
        Assert.Equal(user, LocalPluginManageCore.MigrationTargetRoot(globalUnit, user, global));
    }

    [Fact]
    public void MigrationTarget_InstallGoesToUser()
    {
        var user = PluginsRoot("user-root");
        var installRoot = Path.Combine(Path.GetTempPath(), "obs-helper-tests", "obs-studio", "obs-plugins");
        var dll = Path.Combine(installRoot, "p1", "bin", "64bit", "p1.dll");
        var unit = LocalPluginManageCore.Describe(dll, "install");

        Assert.Equal(user, LocalPluginManageCore.MigrationTargetRoot(unit, user, null));
    }

    [Fact]
    public void MigrationTarget_FlatHasNoTarget()
    {
        var root = PluginsRoot("flat-root");
        var unit = LocalPluginManageCore.Describe(Path.Combine(root, "loose.dll"), "user");

        Assert.Null(LocalPluginManageCore.MigrationTargetRoot(unit, PluginsRoot("user-root"), PluginsRoot("global-root")));
    }

    [Fact]
    public void SafePluginDirectory_RejectsRootItselfAndSiblings()
    {
        var root = PluginsRoot("safe-root");
        var child = Path.Combine(root, "plugin-a");

        Assert.True(LocalPluginManageCore.IsSafePluginDirectory(child, new[] { root }));
        Assert.False(LocalPluginManageCore.IsSafePluginDirectory(root, new[] { root }));
        Assert.False(LocalPluginManageCore.IsSafePluginDirectory(Path.Combine(PluginsRoot("other"), "plugin-a"), new[] { root }));
        Assert.False(LocalPluginManageCore.IsSafePluginDirectory(@"C:\Windows", new[] { root }));
    }

    [Fact]
    public void SafeUnit_RejectsTamperedPluginName()
    {
        var root = PluginsRoot("safe-root");
        var dll = Path.Combine(root, "plugin-a", "bin", "64bit", "plugin-a.dll");
        var unit = LocalPluginManageCore.Describe(dll, "user")!;

        Assert.True(LocalPluginManageCore.IsSafeUnit(unit));

        var tampered = unit with { PluginName = ".." };
        Assert.False(LocalPluginManageCore.IsSafeUnit(tampered));
    }

    [Fact]
    public void SafeName_NeverProducesPathSeparators()
    {
        Assert.Equal("plugin", LocalPluginManageCore.SafeName(""));
        Assert.Equal("a-b", LocalPluginManageCore.SafeName(@"a\b"));
        Assert.DoesNotContain('/', LocalPluginManageCore.SafeName("a/b"));
        Assert.DoesNotContain('\\', LocalPluginManageCore.SafeName("a\\b"));
        Assert.True(LocalPluginManageCore.SafeName(new string('x', 200)).Length <= 80);
    }

    [Fact]
    public void IsSafeManagedDirectory_AllowsConfiguredRootOnly()
    {
        var root = Root("install-root");
        var dir = Path.Combine(root, "my-plugin");

        Assert.True(PluginInstallCore.IsSafeManagedDirectory(dir, "my-plugin", root));
        Assert.False(PluginInstallCore.IsSafeManagedDirectory(Path.Combine(Root("elsewhere"), "my-plugin"), "my-plugin", root));
        Assert.False(PluginInstallCore.IsSafeManagedDirectory(root, "my-plugin", root));
    }

    [Fact]
    public void IsSafeManagedDirectory_RejectsMismatchedLeaf()
    {
        var root = Root("install-root");
        var dir = Path.Combine(root, "other-plugin");

        Assert.False(PluginInstallCore.IsSafeManagedDirectory(dir, "my-plugin", root));
    }
}
