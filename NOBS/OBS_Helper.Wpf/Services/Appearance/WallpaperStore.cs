using System.IO;
using System.Text;

namespace OBS_Helper.Wpf.Services.Appearance;

/// <summary>
/// 壁纸库的文件层（V3.1.0 / W1）。
///
/// 目录布局：
/// <code>
/// %LocalAppData%\OBS_Helper\wallpapers\
/// ├── manifest.json
/// ├── &lt;sha256&gt;.jpg
/// └── ...
/// </code>
///
/// 设计口径：
/// <list type="bullet">
///   <item>文件一律复制进库，不再依赖用户原始路径（原路径被移动/删除也不影响）；</item>
///   <item>同内容按 SHA-256 去重，重复导入直接复用，不产生副本；</item>
///   <item>manifest 损坏时扫描目录重建，不阻塞启动，也不删除任何图片文件；</item>
///   <item>写入走「临时文件 → 原子替换」；文件操作失败通过回调记日志，不抛到 UI。</item>
/// </list>
///
/// 本类不依赖 WPF，单测工程直接链接编译。
/// </summary>
public sealed class WallpaperStore
{
    private readonly string _root;
    private readonly Action<string>? _log;
    private readonly object _gate = new();

    public WallpaperStore(string root, Action<string>? log = null)
    {
        _root = Path.GetFullPath(root);
        _log = log;
    }

    /// <summary>壁纸库目录（绝对路径）。</summary>
    public string Root => _root;

    /// <summary>manifest 路径。</summary>
    public string ManifestPath => Path.Combine(_root, "manifest.json");

    /// <summary>
    /// 读取壁纸库。manifest 缺失 / 损坏时扫描目录重建；manifest 中指向已不存在文件的条目会被剔除。
    /// 永不抛异常（磁盘不可读时返回空列表）。
    /// </summary>
    public List<WallpaperItem> Load()
    {
        lock (_gate)
        {
            try
            {
                if (!Directory.Exists(_root)) Directory.CreateDirectory(_root);
            }
            catch (Exception ex)
            {
                Log($"创建壁纸目录失败：{ex.Message}");
                return new List<WallpaperItem>();
            }

            WallpaperManifest? manifest = null;
            try
            {
                if (File.Exists(ManifestPath))
                    manifest = WallpaperCore.TryParseManifest(File.ReadAllText(ManifestPath, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                Log($"读取 manifest 失败，将扫描目录重建：{ex.Message}");
            }

            if (manifest is null)
            {
                manifest = RebuildFromDirectory();
                TrySave(manifest);
                return manifest.Items;
            }

            // 剔除已丢失的文件（不删 manifest 之外的任何东西）
            var before = manifest.Items.Count;
            manifest.Items = manifest.Items.Where(i => SafeExists(i.FullPath(_root))).ToList();
            if (manifest.Items.Count != before)
                TrySave(manifest);

            return manifest.Items;
        }
    }

    /// <summary>
    /// 导入一张图片：校验 → 哈希去重 → 复制进库 → 更新 manifest。
    /// 解码可读性由服务层在调用前完成（这里只信文件本身）。
    /// </summary>
    public WallpaperImportResult Import(string sourcePath, int pixelWidth = 0, int pixelHeight = 0)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !SafeExists(sourcePath))
                return new WallpaperImportResult(WallpaperImportError.Missing, null, false);

            long bytes;
            try { bytes = new FileInfo(sourcePath).Length; }
            catch (Exception ex)
            {
                Log($"读取图片信息失败：{ex.Message}");
                return new WallpaperImportResult(WallpaperImportError.Missing, null, false);
            }

            var invalid = WallpaperCore.Validate(sourcePath, bytes, pixelWidth, pixelHeight);
            if (invalid != WallpaperImportError.None)
                return new WallpaperImportResult(invalid, null, false);

            string id;
            try { id = WallpaperCore.HashFile(sourcePath); }
            catch (Exception ex)
            {
                Log($"计算图片哈希失败：{ex.Message}");
                return new WallpaperImportResult(WallpaperImportError.Corrupt, null, false);
            }

            var extension = WallpaperCore.NormalizeExtension(sourcePath);
            if (extension.Length == 0)
                return new WallpaperImportResult(WallpaperImportError.Unsupported, null, false);

            var manifest = ReadManifestForWrite();

            var existing = manifest.Items.FirstOrDefault(i =>
                string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
            var destName = WallpaperCore.FileNameFor(id, extension);
            var dest = Path.Combine(_root, destName);

            if (existing is not null && SafeExists(dest))
            {
                // 同内容已存在：只补全尺寸信息，不复制
                if (pixelWidth > 0 && existing.Width != pixelWidth) existing.Width = pixelWidth;
                if (pixelHeight > 0 && existing.Height != pixelHeight) existing.Height = pixelHeight;
                if (string.IsNullOrWhiteSpace(existing.Name)) existing.Name = Path.GetFileName(sourcePath);
                TrySave(manifest);
                return new WallpaperImportResult(WallpaperImportError.None, existing, true);
            }

            try
            {
                Directory.CreateDirectory(_root);
                if (!SafeExists(dest))
                {
                    var tmp = dest + ".tmp-" + Guid.NewGuid().ToString("N");
                    File.Copy(sourcePath, tmp, overwrite: true);
                    try
                    {
                        if (File.Exists(dest)) File.Delete(dest);
                        File.Move(tmp, dest);
                    }
                    catch
                    {
                        try { File.Delete(tmp); } catch { /* 清理失败无妨 */ }
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"保存壁纸文件失败：{ex.Message}");
                return new WallpaperImportResult(WallpaperImportError.WriteFailed, null, false,
                    ex.Message);
            }

            var item = new WallpaperItem
            {
                Id = id,
                File = destName,
                Name = Path.GetFileName(sourcePath),
                AddedAt = DateTimeOffset.Now,
                Width = Math.Max(0, pixelWidth),
                Height = Math.Max(0, pixelHeight),
                Bytes = bytes
            };

            manifest.Items.RemoveAll(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
            manifest.Items.Add(item);
            if (!TrySave(manifest))
                return new WallpaperImportResult(WallpaperImportError.WriteFailed, null, false);

            return new WallpaperImportResult(WallpaperImportError.None, item, false);
        }
    }

    /// <summary>
    /// 删除一张壁纸：先从 manifest 移除，再删除文件。
    /// 文件删除失败时仍保留 manifest 变更（下次启动扫描会清掉孤儿文件），返回 false 供 UI 提示。
    /// </summary>
    public bool Delete(string id)
    {
        if (!WallpaperCore.IsValidId(id)) return false;

        lock (_gate)
        {
            var manifest = ReadManifestForWrite();
            var item = manifest.Items.FirstOrDefault(i =>
                string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
            if (item is null) return true;   // 已经不在库里

            manifest.Items.RemoveAll(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
            TrySave(manifest);

            try
            {
                var path = item.FullPath(_root);
                if (SafeExists(path)) File.Delete(path);
                return true;
            }
            catch (Exception ex)
            {
                Log($"删除壁纸文件失败（已从清单移除）：{ex.Message}");
                return false;
            }
        }
    }

    /// <summary>把已解码得到的尺寸写回 manifest（旧配置导入时补全）。</summary>
    public void UpdateDimensions(string id, int width, int height)
    {
        if (!WallpaperCore.IsValidId(id) || width <= 0 || height <= 0) return;
        lock (_gate)
        {
            var manifest = ReadManifestForWrite();
            var item = manifest.Items.FirstOrDefault(i =>
                string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
            if (item is null || (item.Width == width && item.Height == height)) return;
            item.Width = width;
            item.Height = height;
            TrySave(manifest);
        }
    }

    /// <summary>清理未被 manifest 引用的孤儿图片（默认不启用，留给后续版本）。</summary>
    public int CleanupOrphans(TimeSpan? olderThan = null)
    {
        lock (_gate)
        {
            if (!Directory.Exists(_root)) return 0;
            var manifest = ReadManifestForWrite();
            var referenced = new HashSet<string>(
                manifest.Items.Select(i => i.File), StringComparer.OrdinalIgnoreCase);

            var cutoff = DateTime.UtcNow - (olderThan ?? TimeSpan.FromDays(7));
            var removed = 0;
            foreach (var file in Directory.EnumerateFiles(_root))
            {
                var name = Path.GetFileName(file);
                if (name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)) continue;
                if (!WallpaperCore.IsSupportedExtension(name)) continue;
                if (referenced.Contains(name)) continue;
                try
                {
                    if (File.GetLastWriteTimeUtc(file) > cutoff) continue;
                    File.Delete(file);
                    removed++;
                }
                catch (Exception ex)
                {
                    Log($"清理孤儿壁纸失败：{ex.Message}");
                }
            }
            return removed;
        }
    }

    // ------------------------------------------------------------ 内部

    private WallpaperManifest ReadManifestForWrite()
    {
        try
        {
            if (File.Exists(ManifestPath))
            {
                var parsed = WallpaperCore.TryParseManifest(File.ReadAllText(ManifestPath, Encoding.UTF8));
                if (parsed is not null) return parsed;
            }
        }
        catch (Exception ex)
        {
            Log($"读取 manifest 失败，将重建：{ex.Message}");
        }
        return RebuildFromDirectory();
    }

    /// <summary>扫描目录重建 manifest：文件名（哈希）即 id，尺寸留空，后续按需补全。</summary>
    private WallpaperManifest RebuildFromDirectory()
    {
        var manifest = new WallpaperManifest();
        if (!Directory.Exists(_root)) return manifest;

        try
        {
            foreach (var file in Directory.EnumerateFiles(_root))
            {
                var name = Path.GetFileName(file);
                if (!WallpaperCore.IsSupportedExtension(name)) continue;

                var id = Path.GetFileNameWithoutExtension(name);
                if (!WallpaperCore.IsValidId(id)) continue;

                long bytes = 0;
                try { bytes = new FileInfo(file).Length; } catch { /* 读不到按 0 */ }

                manifest.Items.Add(new WallpaperItem
                {
                    Id = id,
                    File = name,
                    Name = name,
                    AddedAt = SafeLastWrite(file),
                    Bytes = bytes
                });
            }
        }
        catch (Exception ex)
        {
            Log($"扫描壁纸目录失败：{ex.Message}");
        }

        manifest.Items.Sort((a, b) => b.AddedAt.CompareTo(a.AddedAt));
        return manifest;
    }

    private bool TrySave(WallpaperManifest manifest)
    {
        try
        {
            Directory.CreateDirectory(_root);
            var json = WallpaperCore.SerializeManifest(manifest);
            var tmp = ManifestPath + ".tmp";
            File.WriteAllText(tmp, json, new UTF8Encoding(false));
            try
            {
                if (File.Exists(ManifestPath))
                    File.Replace(tmp, ManifestPath, null);
                else
                    File.Move(tmp, ManifestPath);
            }
            catch
            {
                try { File.Delete(tmp); } catch { /* 清理失败无妨 */ }
                throw;
            }
            return true;
        }
        catch (Exception ex)
        {
            Log($"写入 manifest 失败：{ex.Message}");
            return false;
        }
    }

    private static bool SafeExists(string path)
    {
        try { return !string.IsNullOrWhiteSpace(path) && File.Exists(path); }
        catch (Exception) { return false; }
    }

    private static DateTimeOffset SafeLastWrite(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.LastWriteTimeUtc : DateTimeOffset.UtcNow;
        }
        catch (Exception)
        {
            return DateTimeOffset.UtcNow;
        }
    }

    private void Log(string message) => _log?.Invoke(message);

    /// <summary>确认路径仍在库目录内（manifest 被手改时不越界删文件）。</summary>
    public bool IsInsideRoot(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            return full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(full, _root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
