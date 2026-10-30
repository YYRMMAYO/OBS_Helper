using System.IO;
using System.Text;
using System.Text.Json;
using OBS_Helper.Wpf.Models.Plugins;

namespace OBS_Helper.Wpf.Services.Plugins;

/// <summary>
/// 插件安装状态（V3.3.0）：<c>%LocalAppData%\OBS_Helper\plugins\</c> 下的清单、备份与下载临时区。
///
/// 只记录本应用装过的插件；未管理的插件永远不进清单，因此卸载 / 回滚不会误删用户文件。
/// 写入走「临时文件 + 替换」，避免崩溃留下半截 JSON。
/// </summary>
public sealed class PluginStateStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly object _gate = new();

    public PluginStateStore(string rootDirectory)
    {
        Root = rootDirectory;
        DownloadsDirectory = Path.Combine(rootDirectory, "downloads");
        BackupsDirectory = Path.Combine(rootDirectory, "backups");
        ManifestPath = Path.Combine(rootDirectory, "installed.json");
    }

    public string Root { get; }
    public string DownloadsDirectory { get; }
    public string BackupsDirectory { get; }
    public string ManifestPath { get; }

    /// <summary>读清单；损坏时保留副本并以空清单启动。</summary>
    public PluginInstallManifest Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(ManifestPath)) return new PluginInstallManifest();
                var json = File.ReadAllText(ManifestPath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json)) return new PluginInstallManifest();

                var data = JsonSerializer.Deserialize<PluginInstallManifest>(json, JsonOpts);
                if (data is null) return new PluginInstallManifest();
                data.Installed ??= new List<PluginInstallEntry>();
                return data;
            }
            catch (Exception ex)
            {
                FileLogger.Warn("Plugins", $"读取 installed.json 失败（按空清单处理）：{ex.Message}");
                try
                {
                    if (File.Exists(ManifestPath))
                        File.Copy(ManifestPath, ManifestPath + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss"), false);
                }
                catch { /* 副本也存不下就算了 */ }
                return new PluginInstallManifest();
            }
        }
    }

    /// <summary>保存清单（原子替换）。</summary>
    public bool Save(PluginInstallManifest manifest)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Root);
                var json = JsonSerializer.Serialize(manifest, JsonOpts);
                var tmp = ManifestPath + ".tmp";
                File.WriteAllText(tmp, json, new UTF8Encoding(false));
                if (File.Exists(ManifestPath))
                    File.Replace(tmp, ManifestPath, null);
                else
                    File.Move(tmp, ManifestPath);
                return true;
            }
            catch (Exception ex)
            {
                FileLogger.Error("Plugins", $"写入 installed.json 失败：{ex.Message}");
                return false;
            }
        }
    }

    public PluginInstallEntry? Find(string id)
        => Load().Installed.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>新增或替换一条记录。</summary>
    public bool Upsert(PluginInstallEntry entry)
    {
        var data = Load();
        var idx = data.Installed.FindIndex(e => string.Equals(e.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0) data.Installed[idx] = entry;
        else data.Installed.Add(entry);
        return Save(data);
    }

    /// <summary>删除一条记录。</summary>
    public bool Remove(string id)
    {
        var data = Load();
        data.Installed.RemoveAll(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
        return Save(data);
    }

    /// <summary>确保下载 / 备份目录存在。</summary>
    public void EnsureDirectories()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(DownloadsDirectory);
        Directory.CreateDirectory(BackupsDirectory);
    }

    /// <summary>列出某插件的备份目录（按时间倒序）。</summary>
    public IReadOnlyList<string> ListBackups(string id)
    {
        var dir = Path.Combine(BackupsDirectory, PluginInstallCore.SafeSlug(id));
        if (!Directory.Exists(dir)) return Array.Empty<string>();
        try
        {
            return Directory.GetDirectories(dir)
                .OrderByDescending(p => Path.GetFileName(p), StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>只保留最近 <paramref name="keep"/> 份备份。</summary>
    public void PruneBackups(string id, int keep = 2)
    {
        var backups = ListBackups(id);
        foreach (var old in backups.Skip(Math.Max(0, keep)))
        {
            try { Directory.Delete(old, recursive: true); }
            catch (Exception ex) { FileLogger.Warn("Plugins", $"清理旧备份失败（{old}）：{ex.Message}"); }
        }
    }
}
