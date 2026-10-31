using System.Diagnostics;
using System.IO;
using OBS_Helper.Wpf.Models.Plugins;

namespace OBS_Helper.Wpf.Services.Plugins;

/// <summary>本机插件管理只需要清单的这一小部分能力：找一条 / 写一条。</summary>
public interface ILocalPluginStateStore
{
    string Root { get; }
    PluginInstallEntry? Find(string id);
    bool Upsert(PluginInstallEntry entry);
}

/// <summary>本机插件管理操作的结果（Code 是稳定错误码 / 空串）。</summary>
public sealed record LocalPluginActionResult(bool Ok, string Code, string Path, string Detail);

/// <summary>
/// 本机检测到的插件管理（X1031Y2）：卸载、迁移、接管管理。
///
/// 边界（比「能删文件」重要得多）：
/// <list type="bullet">
///   <item>OBS 运行中一律拒绝（DLL 被占用，移动 / 删除会失败或留下半截）；</item>
///   <item>只接受扫描器给出的插件单元，并且再次校验目录确实在插件根之下；</item>
///   <item>卸载不是硬删：整目录移进 <c>%LocalAppData%\OBS_Helper\plugins\local-backups</c>，可手动恢复；</item>
///   <item>扁平摆放（DLL 直接丢在插件根）只移 DLL 与同名 PDB，绝不移动整个插件根；</item>
///   <item>迁移只在一插件一目录布局之间进行（用户级 ⇄ 全局级 / 安装目录 → 用户级）。</item>
/// </list>
/// </summary>
public sealed class LocalPluginManageService
{
    private readonly ILocalPluginStateStore _store;
    private readonly string _localBackupRoot;
    private readonly Func<bool> _isObsRunning;
    private readonly Func<string, bool> _directoryExists;
    private readonly string _userPluginsRoot;
    private readonly string _globalPluginsRoot;

    public LocalPluginManageService(
        ILocalPluginStateStore store,
        string? localBackupRoot = null,
        Func<bool>? isObsRunning = null,
        Func<string, bool>? directoryExists = null,
        string? userPluginsRoot = null,
        string? globalPluginsRoot = null)
    {
        _store = store;
        _localBackupRoot = localBackupRoot ?? Path.Combine(store.Root, "local-backups");
        _isObsRunning = isObsRunning ?? IsObsRunning;
        _directoryExists = directoryExists ?? Directory.Exists;
        _userPluginsRoot = userPluginsRoot ?? UserPluginsRoot;
        _globalPluginsRoot = globalPluginsRoot ?? GlobalPluginsRoot;
    }

    /// <summary>OBS 是否在运行（卸载 / 迁移前必须为 false）。</summary>
    public static bool IsObsRunning()
    {
        try
        {
            return Process.GetProcesses().Any(p =>
            {
                try
                {
                    var n = p.ProcessName;
                    return n.Equals("obs64", StringComparison.OrdinalIgnoreCase)
                        || n.Equals("obs32", StringComparison.OrdinalIgnoreCase)
                        || n.Equals("obs", StringComparison.OrdinalIgnoreCase);
                }
                catch { return false; }
            });
        }
        catch (Exception)
        {
            return false;
        }
    }

    public string LocalBackupRoot => _localBackupRoot;

    /// <summary>用户级插件根：<c>%AppData%\obs-studio\plugins</c>。</summary>
    public static string UserPluginsRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obs-studio", "plugins");

    /// <summary>全局插件根：<c>%ProgramData%\obs-studio\plugins</c>。</summary>
    public static string GlobalPluginsRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "obs-studio", "plugins");

    /// <summary>插件单元（无法安全判定时为 null）。界面据此决定是否显示管理操作。</summary>
    public static LocalPluginUnit? Describe(InstalledPluginFile plugin) => plugin is null
        ? null
        : LocalPluginManageCore.Describe(plugin.FullPath, plugin.SourceLabel);

    /// <summary>
    /// 卸载本机插件：整目录移入本应用的备份区（不硬删）。
    /// 返回的 Path 是备份位置，便于界面告诉用户「想恢复去哪里找」。
    /// </summary>
    public async Task<LocalPluginActionResult> UninstallAsync(InstalledPluginFile plugin, CancellationToken ct)
    {
        await Task.Yield();
        ct.ThrowIfCancellationRequested();

        if (_isObsRunning())
            return new LocalPluginActionResult(false, "PLUGIN_OBS_RUNNING", "", "");

        var unit = Describe(plugin);
        if (unit is null || !IsSafeUnit(unit))
            return new LocalPluginActionResult(false, "PLUGIN_LOCAL_UNSAFE", "", "");

        try
        {
            Directory.CreateDirectory(_localBackupRoot);
            var stamp = LocalPluginManageCore.BackupName(unit.PluginName, DateTime.Now);
            var backup = UniquePath(Path.Combine(_localBackupRoot, stamp));

            if (unit.Kind == LocalPluginUnitKind.Nested)
            {
                if (!_directoryExists(unit.PluginDirectory))
                    return new LocalPluginActionResult(false, "PLUGIN_LOCAL_MISSING", unit.PluginDirectory, "");
                Directory.Move(unit.PluginDirectory, backup);
            }
            else
            {
                if (!File.Exists(unit.DllPath))
                    return new LocalPluginActionResult(false, "PLUGIN_LOCAL_MISSING", unit.DllPath, "");
                Directory.CreateDirectory(backup);
                var dllTarget = Path.Combine(backup, Path.GetFileName(unit.DllPath));
                File.Move(unit.DllPath, dllTarget);
                CopySiblingIfExists(unit.DllPath, ".pdb", backup);
                CopySiblingIfExists(unit.DllPath, ".xml", backup);
                backup = dllTarget;
            }

            return new LocalPluginActionResult(true, "", backup, unit.PluginName);
        }
        catch (Exception ex)
        {
            return new LocalPluginActionResult(false, "PLUGIN_LOCAL_UNINSTALL_FAILED", "", ex.Message);
        }
    }

    /// <summary>
    /// 迁移一插件一目录布局的插件：用户级 ⇄ 全局级；OBS 安装目录 → 用户级。
    /// 目标已存在 / 扁平布局 / 未知根都会拒绝，并返回可操作的错误码。
    /// </summary>
    public async Task<LocalPluginActionResult> MigrateAsync(InstalledPluginFile plugin, CancellationToken ct)
    {
        await Task.Yield();
        ct.ThrowIfCancellationRequested();

        if (_isObsRunning())
            return new LocalPluginActionResult(false, "PLUGIN_OBS_RUNNING", "", "");

        var unit = Describe(plugin);
        if (unit is null || !IsSafeUnit(unit))
            return new LocalPluginActionResult(false, "PLUGIN_LOCAL_UNSAFE", "", "");
        if (unit.Kind != LocalPluginUnitKind.Nested)
            return new LocalPluginActionResult(false, "PLUGIN_LOCAL_FLAT", "", "");

        var targetRoot = LocalPluginManageCore.MigrationTargetRoot(unit, _userPluginsRoot, _globalPluginsRoot);
        if (string.IsNullOrWhiteSpace(targetRoot))
            return new LocalPluginActionResult(false, "PLUGIN_LOCAL_NO_TARGET", "", "");

        var target = Path.Combine(targetRoot!, unit.PluginName);
        if (Directory.Exists(target) || File.Exists(target))
            return new LocalPluginActionResult(false, "PLUGIN_LOCAL_TARGET_EXISTS", target, "");

        try
        {
            Directory.CreateDirectory(targetRoot!);
            Directory.Move(unit.PluginDirectory, target);
            return new LocalPluginActionResult(true, "", target, unit.PluginName);
        }
        catch (UnauthorizedAccessException)
        {
            return new LocalPluginActionResult(false, "PLUGIN_LOCAL_NO_PERMISSION", target, "");
        }
        catch (IOException)
        {
            // 跨盘 / 文件被占用：跨盘时用复制 + 删除，占用则如实返回
            try
            {
                CopyDirectory(unit.PluginDirectory, target);
                Directory.Delete(unit.PluginDirectory, recursive: true);
                return new LocalPluginActionResult(true, "", target, unit.PluginName);
            }
            catch (Exception copyError)
            {
                return new LocalPluginActionResult(false, "PLUGIN_LOCAL_MIGRATE_FAILED", target, copyError.Message);
            }
        }
        catch (Exception ex)
        {
            return new LocalPluginActionResult(false, "PLUGIN_LOCAL_MIGRATE_FAILED", target, ex.Message);
        }
    }

    /// <summary>
    /// 接管管理：把本机检测到的一插件一目录插件写进 <c>installed.json</c>，
    /// 之后就能在「本应用管理」里回滚 / 卸载 / 用广场更新。
    ///
    /// 不移动文件：用户已经装好的东西，接管时只补登记，不制造一次「搬家」风险。
    /// </summary>
    public LocalPluginActionResult Adopt(InstalledPluginFile plugin, PluginCatalogData? catalog)
    {
        if (_isObsRunning())
            return new LocalPluginActionResult(false, "PLUGIN_OBS_RUNNING", "", "");

        var unit = Describe(plugin);
        if (unit is null || !IsSafeUnit(unit))
            return new LocalPluginActionResult(false, "PLUGIN_LOCAL_UNSAFE", "", "");
        if (unit.Kind != LocalPluginUnitKind.Nested)
            return new LocalPluginActionResult(false, "PLUGIN_LOCAL_FLAT", "", "");

        try
        {
            var entry = PluginCatalogCore.MatchByDll(catalog, plugin.FileName);
            var id = entry?.Id;
            if (string.IsNullOrWhiteSpace(id))
                id = "local-" + LocalPluginManageCore.SafeName(plugin.Stem);
            id = PluginInstallCore.SafeSlug(id);

            if (_store.Find(id) is not null)
                return new LocalPluginActionResult(false, "PLUGIN_LOCAL_ALREADY_MANAGED", unit.PluginDirectory, id);

            var files = EnumerateRelativeFiles(unit.PluginDirectory);
            var saved = _store.Upsert(new PluginInstallEntry
            {
                Id = id,
                Name = string.IsNullOrWhiteSpace(plugin.ProductName) ? plugin.Stem : plugin.ProductName,
                Version = plugin.FileVersion ?? "",
                InstalledAtUtc = DateTime.UtcNow.ToString("O"),
                SourceRepo = entry?.Repo ?? "",
                AssetName = "",
                Sha256 = "",
                InstallDirectory = unit.PluginDirectory,
                Scope = unit.RootKind is "global" ? "global" : "user",
                Curated = entry is not null,
                Files = files,
                LastBackupDirectory = "",
            });

            return saved
                ? new LocalPluginActionResult(true, "", unit.PluginDirectory, id)
                : new LocalPluginActionResult(false, "PLUGIN_STATE_WRITE_FAILED", unit.PluginDirectory, "");
        }
        catch (Exception ex)
        {
            return new LocalPluginActionResult(false, "PLUGIN_LOCAL_ADOPT_FAILED", unit.PluginDirectory, ex.Message);
        }
    }

    /// <summary>目标插件根对应的 scope 文案键（user / global）；没有目标返回 null。</summary>
    public string? MigrationTargetScope(InstalledPluginFile plugin)
    {
        var unit = Describe(plugin);
        var target = unit is null ? null : LocalPluginManageCore.MigrationTargetRoot(unit, _userPluginsRoot, _globalPluginsRoot);
        return LocalPluginManageCore.MigrationTargetScope(target);
    }

    /// <summary>迁移目标目录（用于确认弹窗展示）；没有目标返回 null。</summary>
    public string? MigrationTargetDirectory(InstalledPluginFile plugin)
    {
        var unit = Describe(plugin);
        if (unit is null) return null;
        var root = LocalPluginManageCore.MigrationTargetRoot(unit, _userPluginsRoot, _globalPluginsRoot);
        return string.IsNullOrWhiteSpace(root) ? null : Path.Combine(root!, unit.PluginName);
    }

    /// <summary>校验插件单元：委托给纯逻辑，便于单测覆盖。</summary>
    public static bool IsSafeUnit(LocalPluginUnit unit) => LocalPluginManageCore.IsSafeUnit(unit);

    private static string UniquePath(string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path)) return path;
        var n = 2;
        while (true)
        {
            var candidate = path + "-" + n;
            if (!Directory.Exists(candidate) && !File.Exists(candidate)) return candidate;
            n++;
        }
    }

    private static void CopySiblingIfExists(string dllPath, string extension, string destination)
    {
        try
        {
            var sibling = Path.ChangeExtension(dllPath, extension);
            if (File.Exists(sibling))
                File.Copy(sibling, Path.Combine(destination, Path.GetFileName(sibling)), overwrite: true);
        }
        catch (Exception)
        {
            // 附属文件复制失败不影响主 DLL 已备份的事实
        }
    }

    private static List<string> EnumerateRelativeFiles(string root)
    {
        var files = new List<string>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (relative.Length > 0) files.Add(relative);
            }
        }
        catch (Exception)
        {
            // 读不全就记录已读到的部分；清单只是用于展示与回滚参考
        }
        return files;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(source, destination));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(source, destination), overwrite: true);
    }
}
