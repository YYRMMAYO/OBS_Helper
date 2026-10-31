using OBS_Helper.Wpf.Models.Plugins;
using OBS_Helper.Wpf.Services.Plugins;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// X1031Y2：本机插件管理服务（卸载 / 迁移 / 接管）。
///
/// 全部在临时目录里跑真实文件操作：卸载必须把整目录移进备份而不是硬删；
/// 扁平布局只能移 DLL；迁移只在两个显式插件根之间发生；接管只登记、不搬文件。
/// </summary>
public class LocalPluginManageServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "obs-helper-manage-tests", Guid.NewGuid().ToString("N"));

    private readonly string _userRoot;
    private readonly string _globalRoot;
    private readonly FakeStore _store;
    private readonly LocalPluginManageService _service;

    public LocalPluginManageServiceTests()
    {
        _userRoot = Path.Combine(_root, "roaming", "obs-studio", "plugins");
        _globalRoot = Path.Combine(_root, "programdata", "obs-studio", "plugins");
        _store = new FakeStore(Path.Combine(_root, "state"));
        _service = new LocalPluginManageService(
            _store,
            Path.Combine(_root, "backups"),
            isObsRunning: () => false,
            userPluginsRoot: _userRoot,
            globalPluginsRoot: _globalRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (Exception) { }
    }

    private string CreateNested(string root, string name)
    {
        var pluginDir = Path.Combine(root, name);
        var dllDir = Path.Combine(pluginDir, "bin", "64bit");
        Directory.CreateDirectory(dllDir);
        File.WriteAllText(Path.Combine(dllDir, name + ".dll"), "test");
        return Path.Combine(dllDir, name + ".dll");
    }

    private static InstalledPluginFile PluginFile(string dll, string label = "user") => new()
    {
        FullPath = dll,
        FileName = Path.GetFileName(dll),
        Stem = Path.GetFileNameWithoutExtension(dll),
        SourceLabel = label,
    };

    [Fact]
    public async Task Uninstall_Nested_MovesWholePluginDirectoryToBackup()
    {
        var dll = CreateNested(_userRoot, "plugin-a");

        var result = await _service.UninstallAsync(PluginFile(dll), CancellationToken.None);

        Assert.True(result.Ok, result.Code);
        Assert.False(Directory.Exists(Path.Combine(_userRoot, "plugin-a")));
        Assert.True(Directory.Exists(result.Path));
        Assert.True(File.Exists(Path.Combine(result.Path, "bin", "64bit", "plugin-a.dll")));
    }

    [Fact]
    public async Task Uninstall_Flat_OnlyMovesTheDll()
    {
        var flatRoot = Path.Combine(_root, "obs-studio", "obs-plugins", "64bit");
        Directory.CreateDirectory(flatRoot);
        var dll = Path.Combine(flatRoot, "old-plugin.dll");
        File.WriteAllText(dll, "test");
        File.WriteAllText(Path.Combine(flatRoot, "keep-me.dll"), "keep");

        var result = await _service.UninstallAsync(PluginFile(dll, "install"), CancellationToken.None);

        Assert.True(result.Ok, result.Code);
        Assert.False(File.Exists(dll));
        Assert.True(File.Exists(Path.Combine(flatRoot, "keep-me.dll")));
    }

    [Fact]
    public async Task Migrate_UserToGlobal_MovesDirectory()
    {
        var dll = CreateNested(_userRoot, "plugin-a");

        var result = await _service.MigrateAsync(PluginFile(dll), CancellationToken.None);

        Assert.True(result.Ok, result.Code);
        Assert.False(Directory.Exists(Path.Combine(_userRoot, "plugin-a")));
        Assert.True(File.Exists(Path.Combine(_globalRoot, "plugin-a", "bin", "64bit", "plugin-a.dll")));
    }

    [Fact]
    public async Task Migrate_TargetExists_IsRejected()
    {
        var dll = CreateNested(_userRoot, "plugin-a");
        Directory.CreateDirectory(Path.Combine(_globalRoot, "plugin-a"));

        var result = await _service.MigrateAsync(PluginFile(dll), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("PLUGIN_LOCAL_TARGET_EXISTS", result.Code);
    }

    [Fact]
    public void Adopt_RegistersEntryWithoutMovingFiles()
    {
        var dll = CreateNested(_userRoot, "plugin-a");

        var result = _service.Adopt(PluginFile(dll), new PluginCatalogData());

        Assert.True(result.Ok, result.Code);
        Assert.True(File.Exists(dll));
        var entry = _store.Find("local-plugin-a");
        Assert.NotNull(entry);
        Assert.Equal(Path.Combine(_userRoot, "plugin-a"), entry!.InstallDirectory);
        Assert.Contains("bin/64bit/plugin-a.dll", entry.Files);
    }

    [Fact]
    public async Task Uninstall_ObsRunning_IsRejected()
    {
        var dll = CreateNested(_userRoot, "plugin-a");
        var service = new LocalPluginManageService(
            _store,
            Path.Combine(_root, "backups"),
            isObsRunning: () => true,
            userPluginsRoot: _userRoot,
            globalPluginsRoot: _globalRoot);

        var result = await service.UninstallAsync(PluginFile(dll), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("PLUGIN_OBS_RUNNING", result.Code);
        Assert.True(File.Exists(dll));
    }

    [Fact]
    public async Task Uninstall_UnrecognizedPath_IsRejected()
    {
        var outside = Path.Combine(_root, "not-a-plugin-root");
        Directory.CreateDirectory(outside);
        var dll = Path.Combine(outside, "evil.dll");
        File.WriteAllText(dll, "test");

        var result = await _service.UninstallAsync(PluginFile(dll, "install"), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("PLUGIN_LOCAL_UNSAFE", result.Code);
        Assert.True(File.Exists(dll));
    }

    private sealed class FakeStore : ILocalPluginStateStore
    {
        private readonly Dictionary<string, PluginInstallEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
        public FakeStore(string root) => Root = root;
        public string Root { get; }
        public PluginInstallEntry? Find(string id) => _entries.TryGetValue(id, out var e) ? e : null;
        public bool Upsert(PluginInstallEntry entry)
        {
            _entries[entry.Id] = entry;
            return true;
        }
    }
}
