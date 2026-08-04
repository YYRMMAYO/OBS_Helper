using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OBS_Helper.Wpf.Models.ObsConfig;
using OBS_Helper.Wpf.Services.Host;

namespace OBS_Helper.Wpf.Services.ObsConfig;

/// <summary>
/// OBS 配置备份 / 导出 / 导入。
///
/// <b>零依赖</b>：zip 用框架自带 <see cref="System.IO.Compression"/>，JSON 用 <see cref="System.Text.Json"/>。
///
/// 设计要点（见技术计划 §备份 / 导入导出）：
/// <list type="bullet">
///   <item>打包必须手写流读取（<c>FileShare.ReadWrite | Delete</c>），不能用
///         <see cref="ZipFileExtensions.CreateEntryFromFile"/>——OBS 运行时会独占 <c>global.ini</c>，
///         内部 <c>FileShare.Read</c> 会直接炸掉整份备份。</item>
///   <item>推流密钥默认脱敏：删字段不丢文件（保留 <c>type/service/server/protocol</c> 等让导入方仍能识别平台）。</item>
///   <item>Zip Slip / Zip Bomb 防护：导入前先扫描，任何一条越界 / 炸弹特征直接拒绝整包。</item>
///   <item>导入强制先自动备份（含密钥 + plugin_config），失败即中止，绝不写任何东西。</item>
///   <item>合并导入不覆盖本机 global.ini/user.ini；脱敏包导入时把本机原有密钥回填，避免用户自己的密钥被搞丢。</item>
/// </list>
/// </summary>
public sealed class ObsBackupService
{
    private static readonly HashSet<string> RedactKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "key", "stream_key", "password", "username",
        "bearer_token", "token", "auth_token", "refresh_token",
        "connect_info", "whip_bearer_token"
    };

    private static readonly HashSet<string> AllowedExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json", ".ini", ".txt", ".bak", ".csv", ".lua", ".py", ".effect", ".qss", ".css"
    };

    private static readonly HashSet<string> ForbiddenExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".scr", ".com"
    };

    private const long MaxEntryBytes = 32L * 1024 * 1024;       // 单条 ≤ 32MB
    private const long MaxTotalBytes = 512L * 1024 * 1024;      // 总解压 ≤ 512MB
    private const int MaxEntries = 5000;
    private const double MaxRatio = 200.0;                       // 压缩比上限 200:1

    private readonly ObsPathService _paths;

    public ObsBackupService(ObsPathService paths) => _paths = paths;

    // ---------------------------------------------------------------- 备份 / 导出

    /// <summary>自动备份到应用私有备份目录。返回生成的 zip 路径。失败时抛异常（调用方据此中止导入 / 重置）。</summary>
    public async Task<string> CreateBackupAsync(string reason, bool includeKey, bool includePluginConfig, IProgress<string>? p = null)
    {
        var loc = await _paths.LocateAsync();
        if (!loc.Exists)
            throw new InvalidOperationException("未找到本机 OBS 配置目录，无法备份。");

        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var safeReason = SanitizeFileName(reason);
        var dir = ObsPathService.BackupsRoot;
        Directory.CreateDirectory(dir);

        var zipPath = Path.Combine(dir, $"obsconfig_{stamp}_{safeReason}.zip");
        for (int n = 1; File.Exists(zipPath); n++)
            zipPath = Path.Combine(dir, $"obsconfig_{stamp}_{safeReason}_{n}.zip");

        await Task.Run(() => BuildZip(zipPath, loc, includeKey, includePluginConfig, reason, p));
        PruneBackups();
        return zipPath;
    }

    /// <summary>导出到用户指定的 zip 路径（不计入自动备份列表，也不触发清理）。</summary>
    public async Task ExportToAsync(string zipPath, bool includeKey, bool includePluginConfig, IProgress<string>? p = null)
    {
        var loc = await _paths.LocateAsync();
        if (!loc.Exists)
            throw new InvalidOperationException("未找到本机 OBS 配置目录，无法导出。");

        if (File.Exists(zipPath)) File.Delete(zipPath);
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        await Task.Run(() => BuildZip(zipPath, loc, includeKey, includePluginConfig, "导出", p));
    }

    private void BuildZip(string zipPath, ObsConfigLocation loc, bool includeKey, bool includePluginConfig, string reason, IProgress<string>? p)
    {
        var scenes = new List<string>();
        var profiles = new List<string>();
        var redacted = new List<string>();
        int entryCount = 0;

        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var configDir = loc.ConfigDir;

            // 场景集合
            p?.Report("正在打包场景集合…");
            var scenesDir = Path.Combine(configDir, "basic", "scenes");
            if (Directory.Exists(scenesDir))
            {
                foreach (var file in Directory.GetFiles(scenesDir, "*.json"))
                {
                    AddFileRaw(zip, "config/basic/scenes/" + Path.GetFileName(file), file);
                    entryCount++;
                    var name = ReadSceneCollectionName(file);
                    if (name is not null) scenes.Add(name);
                }
            }

            // 配置文件（profiles）
            p?.Report("正在打包配置文件…");
            var profilesDir = Path.Combine(configDir, "basic", "profiles");
            if (Directory.Exists(profilesDir))
            {
                foreach (var profDir in Directory.GetDirectories(profilesDir))
                {
                    var profName = Path.GetFileName(profDir.TrimEnd(Path.DirectorySeparatorChar));
                    profiles.Add(profName);
                    foreach (var file in Directory.GetFiles(profDir, "*", SearchOption.AllDirectories))
                    {
                        var rel = "config/basic/profiles/" + profName + "/" +
                                  file.Substring(profDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                      .Replace(Path.DirectorySeparatorChar, '/');
                        if (!includeKey && Path.GetFileName(file).Equals("service.json", StringComparison.OrdinalIgnoreCase))
                        {
                            AddServiceJsonRedacted(zip, rel, file);
                            redacted.Add(rel);
                        }
                        else
                        {
                            AddFileRaw(zip, rel, file);
                        }
                        entryCount++;
                    }
                }
            }

            // global.ini / user.ini
            p?.Report("正在打包全局设置…");
            foreach (var ini in new[] { "global.ini", "user.ini" })
            {
                var p2 = Path.Combine(configDir, ini);
                if (File.Exists(p2))
                {
                    AddFileRaw(zip, "config/" + ini, p2);
                    entryCount++;
                }
            }

            // plugin_config（可选）
            if (includePluginConfig)
            {
                p?.Report("正在打包插件配置…");
                var pc = Path.Combine(configDir, "plugin_config");
                if (Directory.Exists(pc))
                {
                    foreach (var file in Directory.GetFiles(pc, "*", SearchOption.AllDirectories))
                    {
                        var rel = "config/plugin_config/" +
                                  file.Substring(pc.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                      .Replace(Path.DirectorySeparatorChar, '/');
                        AddFileRaw(zip, rel, file);
                        entryCount++;
                    }
                }
            }

            // 清单
            p?.Report("正在写入清单…");
            var manifest = new BackupManifestFile
            {
                schema = 1,
                app = "OBS_Helper",
                appVersion = HostBridge.AppVersion,
                createdAt = DateTime.Now.ToString("o"),
                portable = loc.IsPortable,
                includesPluginConfig = includePluginConfig,
                includesStreamKey = includeKey,
                redactedFiles = redacted.ToArray(),
                sceneCollections = scenes.ToArray(),
                profiles = profiles.ToArray(),
                entryCount = entryCount,
                reason = reason
            };
            var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            AddTextEntry(zip, "manifest.json", manifestJson, DateTime.Now);
        }
    }

    // ---------------------------------------------------------------- 列表 / 清理

    /// <summary>列出备份目录下的所有备份（按创建时间倒序）。</summary>
    public IReadOnlyList<BackupInfo> ListBackups()
    {
        var result = new List<BackupInfo>();
        var dir = ObsPathService.BackupsRoot;
        if (!Directory.Exists(dir)) return result;
        foreach (var file in Directory.GetFiles(dir, "obsconfig_*.zip").OrderByDescending(f => new FileInfo(f).CreationTimeUtc))
        {
            var info = new BackupInfo { ZipPath = file, CreatedAt = new FileInfo(file).CreationTime, Reason = "" };
            // 从文件名里抠出原因段（obsconfig_<stamp>_<reason>.zip）
            var name = Path.GetFileNameWithoutExtension(file);
            var parts = name.Split('_');
            if (parts.Length >= 3) info.Reason = string.Join("_", parts.Skip(2));
            // 从清单里补 includeKey / includePluginConfig
            try
            {
                using var zip = ZipFile.OpenRead(file);
                var m = zip.GetEntry("manifest.json");
                if (m is not null)
                {
                    var mf = JsonSerializer.Deserialize<BackupManifestFile>(ReadEntryText(m));
                    if (mf is not null)
                    {
                        info.IncludeKey = mf.includesStreamKey;
                        info.IncludePluginConfig = mf.includesPluginConfig;
                    }
                }
            }
            catch (Exception) { /* 清单缺失或损坏：保留文件名解析出的原因 */ }
            result.Add(info);
        }
        return result;
    }

    /// <summary>仅保留最近 keepLast 份自动备份。</summary>
    public void PruneBackups(int keepLast = 10)
    {
        try
        {
            var dir = ObsPathService.BackupsRoot;
            if (!Directory.Exists(dir)) return;
            var files = Directory.GetFiles(dir, "obsconfig_*.zip")
                .OrderByDescending(f => new FileInfo(f).CreationTimeUtc)
                .Skip(keepLast);
            foreach (var f in files) SafeDelete(f);
        }
        catch (Exception) { /* 清理失败不阻塞主流程 */ }
    }

    // ---------------------------------------------------------------- 预检 / 导入

    /// <summary>不解密、只统计地预检一个备份包。失败时 <see cref="BackupManifest.Ok"/> 为 false 且 <see cref="BackupManifest.Reason"/> 说明原因。</summary>
    public async Task<BackupManifest> InspectAsync(string zipPath)
    {
        return await Task.Run(() =>
        {
            if (!File.Exists(zipPath))
                return new BackupManifest(false, "文件不存在。", 0, 0, false, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

            var scan = ScanZip(zipPath);
            if (!scan.Ok)
                return new BackupManifest(false, scan.Reason, 0, 0, scan.IncludesStreamKey, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

            return new BackupManifest(
                true, null,
                scan.SceneCollections.Count, scan.Profiles.Count,
                scan.IncludesStreamKey,
                scan.SceneCollections, scan.Profiles, scan.Skipped);
        });
    }

    /// <summary>导入一个备份包。强制先自动备份；失败即中止。Overwrite 覆盖同名，Merge 改名 / 跳过以避免破坏本机配置。</summary>
    public async Task<ObsImportResult> ImportAsync(string zipPath, ObsImportMode mode, IProgress<string> p)
    {
        try
        {
            p.Report("正在预检备份包…");
            var scan = ScanZip(zipPath);
            if (!scan.Ok)
                return new ObsImportResult(false, scan.Reason, null, 0, 0);

            var loc = await _paths.LocateAsync();
            if (!loc.Exists)
                return new ObsImportResult(false, "未找到本机 OBS 配置目录，无法导入。", null, 0, 0);

            if (_paths.IsObsRunning())
                return new ObsImportResult(false, "OBS 正在运行，请先完全退出 OBS 后再导入（否则配置文件会被占用）。", null, 0, 0);

            p.Report("正在创建导入前自动备份（含密钥，以便可恢复）…");
            string? autoBackup = null;
            try
            {
                autoBackup = await CreateBackupAsync("导入前自动备份", includeKey: true, includePluginConfig: true, null);
            }
            catch (Exception ex)
            {
                return new ObsImportResult(false, $"导入前自动备份失败，已中止导入以保护现有配置：{ex.Message}", null, 0, 0);
            }

            int importedCollections = 0, importedProfiles = 0;
            var touchedProfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (var tx = new FileTx(ObsPathService.TrashRoot))
            {
                var configDir = loc.ConfigDir;

                // 记录本机现有 profile 的密钥，供脱敏包回填
                var machineKeys = ReadMachineProfileKeys(configDir);

                if (mode == ObsImportMode.Overwrite)
                {
                    p.Report("正在移走现有配置（保留在回收站可恢复）…");
                    var scenesDir = Path.Combine(configDir, "basic", "scenes");
                    if (Directory.Exists(scenesDir)) foreach (var f in Directory.GetFiles(scenesDir)) tx.StageMove(f);
                    var profDir = Path.Combine(configDir, "basic", "profiles");
                    if (Directory.Exists(profDir)) foreach (var d in Directory.GetDirectories(profDir)) tx.StageMove(d);
                    foreach (var ini in new[] { "global.ini", "user.ini" })
                    {
                        var ip = Path.Combine(configDir, ini);
                        if (File.Exists(ip)) tx.StageMove(ip);
                    }
                    var pc = Path.Combine(configDir, "plugin_config");
                    if (Directory.Exists(pc)) foreach (var d in Directory.GetDirectories(pc)) tx.StageMove(d);
                }

                using var zip = ZipFile.OpenRead(zipPath);
                foreach (var entry in zip.Entries)
                {
                    var rel = NormalizeEntryName(entry.FullName);
                    if (rel is null) continue;                 // slip：扫描阶段已拒绝整包
                    if (rel == "manifest.json") continue;
                    if (!rel.StartsWith("config/", StringComparison.OrdinalIgnoreCase)) continue;

                    if (rel.StartsWith("config/basic/scenes/", StringComparison.OrdinalIgnoreCase))
                    {
                        var fileName = Path.GetFileName(rel);
                        if (mode == ObsImportMode.Merge && File.Exists(Path.Combine(configDir, "basic", "scenes", fileName)))
                        {
                            ExtractSceneCollectionMerge(zip, entry, configDir);
                            p.Report($"已合并场景集合（重命名避免冲突）：{fileName}");
                        }
                        else
                        {
                            ExtractRaw(zip, entry, Path.Combine(configDir, "basic", "scenes", fileName), configDir);
                        }
                        importedCollections++;
                    }
                    else if (rel.StartsWith("config/basic/profiles/", StringComparison.OrdinalIgnoreCase))
                    {
                        var seg = rel.Split('/');
                        if (seg.Length < 5) continue;
                        var profName = seg[3];
                        if (mode == ObsImportMode.Merge && machineKeys.ContainsKey(profName))
                        {
                            p.Report($"已跳过同名配置「{profName}」（合并模式不覆盖）。");
                            continue;
                        }
                        var rest = string.Join("/", seg.Skip(4));
                        var dest = Path.Combine(configDir, "basic", "profiles", profName, rest);
                        ExtractProfileFile(zip, entry, dest, scan.IncludesStreamKey, machineKeys, profName);
                        touchedProfiles.Add(profName);
                    }
                    else if (rel == "config/global.ini" || rel == "config/user.ini")
                    {
                        if (mode == ObsImportMode.Overwrite)
                            ExtractRaw(zip, entry, Path.Combine(configDir, Path.GetFileName(rel)), configDir);
                    }
                    else if (rel.StartsWith("config/plugin_config/", StringComparison.OrdinalIgnoreCase))
                    {
                        if (mode == ObsImportMode.Overwrite)
                            ExtractRaw(zip, entry, Path.Combine(configDir, rel.Substring("config/".Length).Replace('/', Path.DirectorySeparatorChar)), configDir);
                    }
                }

                importedProfiles = touchedProfiles.Count;
                p.Report("正在提交…");
                tx.Commit();
            }

            return new ObsImportResult(true, null, autoBackup, importedCollections, importedProfiles);
        }
        catch (Exception ex)
        {
            return new ObsImportResult(false, $"导入失败：{ex.Message}", null, 0, 0);
        }
    }

    // ---------------------------------------------------------------- zip 读取 / 写入工具

    private sealed class ZipScan
    {
        public bool Ok;
        public string? Reason;
        public List<string> SceneCollections = new();
        public List<string> Profiles = new();
        public List<string> Skipped = new();
        public bool IncludesStreamKey;
        public int EntryCount;
    }

    private ZipScan ScanZip(string zipPath)
    {
        var scan = new ZipScan();
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            long total = 0;
            foreach (var entry in zip.Entries)
            {
                scan.EntryCount++;
                if (scan.EntryCount > MaxEntries) { scan.Ok = false; scan.Reason = "备份包条目过多（疑似异常）。"; return scan; }

                var rel = NormalizeEntryName(entry.FullName);
                if (rel is null) { scan.Ok = false; scan.Reason = $"条目路径非法（疑似路径穿越）：{entry.FullName}"; return scan; }
                if (rel == "manifest.json") continue;
                if (!rel.StartsWith("config/", StringComparison.OrdinalIgnoreCase)) { scan.Skipped.Add(rel); continue; }

                // 扩展名黑名单：直接拒绝整包
                var ext = Path.GetExtension(rel);
                if (ForbiddenExt.Contains(ext)) { scan.Ok = false; scan.Reason = $"备份包含危险文件类型（{ext}），已拒绝以防执行恶意代码。"; return scan; }

                // 炸弹防护
                if (entry.Length > MaxEntryBytes) { scan.Ok = false; scan.Reason = $"单条条目过大（{entry.Length / 1024 / 1024}MB），疑似压缩炸弹。"; return scan; }
                total += entry.Length;
                if (total > MaxTotalBytes) { scan.Ok = false; scan.Reason = "备份包解压后过大（>512MB），疑似压缩炸弹。"; return scan; }
                if (entry.CompressedLength > 0 && entry.Length / (double)entry.CompressedLength > MaxRatio)
                { scan.Ok = false; scan.Reason = "检测到异常压缩比，疑似压缩炸弹。"; return scan; }

                // 白名单外的扩展名：跳过（不拒绝）
                if (!AllowedExt.Contains(ext)) { scan.Skipped.Add(rel); continue; }

                // 归类统计
                if (rel.StartsWith("config/basic/scenes/", StringComparison.OrdinalIgnoreCase))
                {
                    var name = ReadSceneCollectionNameFromEntry(zip, entry);
                    if (name is not null) scan.SceneCollections.Add(name);
                }
                else if (rel.StartsWith("config/basic/profiles/", StringComparison.OrdinalIgnoreCase))
                {
                    var seg = rel.Split('/');
                    if (seg.Length >= 4) scan.Profiles.Add(seg[3]);
                }
            }

            // 从清单补 includesStreamKey
            var m = zip.GetEntry("manifest.json");
            if (m is not null)
            {
                try
                {
                    var mf = JsonSerializer.Deserialize<BackupManifestFile>(ReadEntryText(m));
                    if (mf is not null) scan.IncludesStreamKey = mf.includesStreamKey;
                }
                catch (Exception) { }
            }

            scan.Ok = true;
            return scan;
        }
        catch (Exception ex)
        {
            scan.Ok = false;
            scan.Reason = $"无法读取备份包：{ex.Message}";
            return scan;
        }
    }

    // ------------------------------------------------------------ 提取辅助

    private static void ExtractRaw(ZipArchive zip, ZipArchiveEntry entry, string dest, string root)
    {
        ObsSafePath.AssertWritable(dest, root);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        entry.ExtractToFile(dest, overwrite: true);
    }

    private static void ExtractSceneCollectionMerge(ZipArchive zip, ZipArchiveEntry entry, string configDir)
    {
        var destDir = Path.Combine(configDir, "basic", "scenes");
        Directory.CreateDirectory(destDir);

        var text = ReadEntryText(entry);
        JsonNode? node = JsonNode.Parse(text);
        string baseName;
        if (node is null)
        {
            baseName = Path.GetFileNameWithoutExtension(entry.Name);
        }
        else
        {
            var orig = node["name"]?.GetValue<string>() ?? Path.GetFileNameWithoutExtension(entry.Name);
            node["name"] = orig + " (导入)";
            text = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            baseName = Slugify(orig);
        }

        var dest = Path.Combine(destDir, baseName + "_imported.json");
        int n = 2;
        while (File.Exists(dest)) dest = Path.Combine(destDir, $"{baseName}_imported_{n++}.json");

        File.WriteAllText(dest, text, new UTF8Encoding(false));
    }

    private static void ExtractProfileFile(ZipArchive zip, ZipArchiveEntry entry, string dest, bool includesKey,
        Dictionary<string, (string? key, string? bearer)> machineKeys, string profName)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

        if (Path.GetFileName(dest).Equals("service.json", StringComparison.OrdinalIgnoreCase)
            && !includesKey
            && machineKeys.TryGetValue(profName, out var mk))
        {
            // 脱敏包：把本机原有密钥回填，避免用户自己的推流密钥被搞丢
            var text = ReadEntryText(entry);
            try
            {
                var node = JsonNode.Parse(text);
                if (node is not null)
                {
                    var settings = node["settings"] as JsonObject;
                    if (settings is null) { settings = new JsonObject(); node["settings"] = settings; }
                    if (mk.key is not null) settings["key"] = mk.key;
                    if (mk.bearer is not null) settings["bearer_token"] = mk.bearer;
                    File.WriteAllText(dest, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
                    return;
                }
            }
            catch (Exception) { /* 解析失败则退化为原样写入 */ }
        }

        entry.ExtractToFile(dest, overwrite: true);
    }

    // ------------------------------------------------------------ 打包辅助

    private static void AddFileRaw(ZipArchive zip, string entryName, string sourcePath)
    {
        try
        {
            var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            entry.LastWriteTime = File.GetLastWriteTime(sourcePath);
            using var src = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var dst = entry.Open();
            src.CopyTo(dst);
        }
        catch (Exception ex)
        {
            // 单文件失败（如被 OBS 占用）：只记警告，不毁整份备份
            Debug.WriteLine($"[ObsBackup] 跳过文件 {sourcePath}：{ex.Message}");
        }
    }

    private static void AddServiceJsonRedacted(ZipArchive zip, string entryName, string sourcePath)
    {
        try
        {
            var raw = File.ReadAllText(sourcePath, Encoding.UTF8);
            var redacted = RedactServiceJson(raw);
            if (redacted is null) { AddFileRaw(zip, entryName, sourcePath); return; }
            AddTextEntry(zip, entryName, redacted, File.GetLastWriteTime(sourcePath));
        }
        catch (Exception)
        {
            // 解析失败：原样兜底
            AddFileRaw(zip, entryName, sourcePath);
        }
    }

    private static void AddTextEntry(ZipArchive zip, string entryName, string text, DateTime lastWrite)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        entry.LastWriteTime = lastWrite;
        using var dst = entry.Open();
        using var sw = new StreamWriter(dst, new UTF8Encoding(false));
        sw.Write(text);
    }

    /// <summary>脱敏 = 删字段不丢文件。保留 type/service/server/protocol 等，让导入方能识别平台。</summary>
    private static string? RedactServiceJson(string json)
    {
        JsonNode? root = JsonNode.Parse(json);
        if (root is null) return null;

        RedactNode(root);
        if (root["settings"] is JsonObject settings) RedactNode(settings);

        // server 抹掉 ? 之后的 query（可能带临时 token）
        if (root["settings"]?["server"] is JsonValue sv && sv.TryGetValue(out string? serverStr) && !string.IsNullOrEmpty(serverStr))
        {
            var q = serverStr.IndexOf('?');
            if (q >= 0) root["settings"]!["server"] = serverStr.Substring(0, q);
        }
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static void RedactNode(JsonNode node)
    {
        if (node is not JsonObject obj) return;
        foreach (var key in RedactKeys)
            if (obj.ContainsKey(key)) obj.Remove(key);
    }

    // ------------------------------------------------------------ 通用辅助

    private static string? ReadSceneCollectionName(string file)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (doc.RootElement.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                return n.GetString();
        }
        catch (Exception) { }
        return null;
    }

    private static string? ReadSceneCollectionNameFromEntry(ZipArchive zip, ZipArchiveEntry entry)
    {
        try
        {
            using var doc = JsonDocument.Parse(ReadEntryBytes(entry));
            if (doc.RootElement.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                return n.GetString();
        }
        catch (Exception) { }
        return null;
    }

    private static Dictionary<string, (string? key, string? bearer)> ReadMachineProfileKeys(string configDir)
    {
        var dict = new Dictionary<string, (string?, string?)>(StringComparer.OrdinalIgnoreCase);
        var profDir = Path.Combine(configDir, "basic", "profiles");
        if (!Directory.Exists(profDir)) return dict;
        foreach (var d in Directory.GetDirectories(profDir))
        {
            var prof = Path.GetFileName(d.TrimEnd(Path.DirectorySeparatorChar));
            var svc = Path.Combine(d, "service.json");
            if (!File.Exists(svc)) continue;
            try
            {
                var node = JsonNode.Parse(File.ReadAllText(svc));
                var settings = node?["settings"] as JsonObject;
                var key = settings?["key"]?.GetValue<string>();
                var bearer = settings?["bearer_token"]?.GetValue<string>();
                dict[prof] = (key, bearer);
            }
            catch (Exception) { }
        }
        return dict;
    }

    /// <summary>解析 zip 条目名，拒绝路径穿越 / 绝对路径。返回规范化的相对路径（用 / 分隔），非法返回 null。</summary>
    private static string? NormalizeEntryName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return null;
        if (fullName.Contains(':') || fullName.StartsWith('/') || fullName.StartsWith('\\')) return null;

        var segments = fullName.Split('/', '\\');
        foreach (var seg in segments)
        {
            if (seg == ".." || seg == ".") return null;     // 防 a/../../b 绕过
        }

        var baseDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "obshelper_scan"));
        string full;
        try
        {
            full = Path.GetFullPath(Path.Combine(baseDir, fullName));
        }
        catch (Exception) { return null; }
        if (!full.StartsWith(baseDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;

        return string.Join("/", segments);
    }

    private static byte[] ReadEntryBytes(ZipArchiveEntry entry)
    {
        using var s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static string ReadEntryText(ZipArchiveEntry entry)
        => Encoding.UTF8.GetString(ReadEntryBytes(entry));

    private static string SanitizeFileName(string reason)
    {
        var s = (reason ?? "backup").Trim();
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == ' ')
                sb.Append(c <= 127 ? c : '_');
        }
        var r = sb.ToString().Replace(' ', '_').Trim('_');
        return string.IsNullOrEmpty(r) ? "backup" : r.Substring(0, Math.Min(r.Length, 40));
    }

    private static string Slugify(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s ?? "")
        {
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(char.ToLowerInvariant(c));
            else if (c == ' ') sb.Append('_');
        }
        var r = sb.ToString();
        return string.IsNullOrEmpty(r) ? "collection" : r;
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception) { }
    }

    // ------------------------------------------------------------ 清单模型（仅内部）

    private sealed class BackupManifestFile
    {
        public int schema { get; set; } = 1;
        public string app { get; set; } = "OBS_Helper";
        public string appVersion { get; set; } = "";
        public string createdAt { get; set; } = "";
        public bool portable { get; set; }
        public bool includesPluginConfig { get; set; }
        public bool includesStreamKey { get; set; }
        public string[]? redactedFiles { get; set; }
        public string[]? sceneCollections { get; set; }
        public string[]? profiles { get; set; }
        public int entryCount { get; set; }
        public string? reason { get; set; }
    }
}
