using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using OBS_Helper.Wpf.Models.Plugins;

namespace OBS_Helper.Wpf.Services.Plugins;

/// <summary>安装 / 卸载进度（Stage 是稳定 key，界面取文案）。</summary>
public sealed record PluginInstallProgress(string Stage, string Detail);

/// <summary>安装 / 卸载结果。</summary>
public sealed record PluginInstallResult(bool Ok, string? ErrorCode, string? EntryId, IReadOnlyList<string> Notes);

/// <summary>一次安装请求（精选目录与 GitHub 导入共用）。</summary>
public sealed class PluginInstallRequest
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Version { get; init; } = "";
    public string SourceRepo { get; init; } = "";
    public required PluginReleaseAsset Asset { get; init; }

    /// <summary>user（默认，无需管理员）/ global（ProgramData，需要写权限）。</summary>
    public string Scope { get; init; } = "user";

    /// <summary>是否来自精选目录（false = GitHub 导入，界面必须强警告）。</summary>
    public bool Curated { get; init; }

    /// <summary>目录里给出的 SHA-256（有则必须匹配）。</summary>
    public string? ExpectedSha256 { get; init; }
}

/// <summary>
/// 插件事务安装（V3.3.0 / P0-5）：下载 → 校验 → 解压到 staging → 备份旧版本 → 替换 → 写清单。
///
/// 安全与可回滚是主线：
/// <list type="bullet">
///   <item>OBS 运行中不替换 DLL（<c>PLUGIN_OBS_RUNNING</c>），绝不强杀 OBS；</item>
///   <item>只从 GitHub 官方域 / 官方 Release 资产下载，HTTPS，大小上限 200 MB；</item>
///   <item>zip slip / 符号链接 / 可执行脚本在解压前拒绝；</item>
///   <item>所有 DLL 必须是 x64 或托管 AnyCPU；</item>
///   <item>替换前把旧目录移到备份，失败时移回来；卸载同样进备份（保留最近 2 份）。</item>
/// </list>
/// </summary>
public sealed class PluginInstallService : IDisposable
{
    private readonly PluginStateStore _store;
    private readonly HttpClient _http;
    private readonly Func<bool> _isObsRunning;
    private readonly Func<string, string>? _installRootResolver;
    private readonly SemaphoreSlim _single = new(1, 1);

    public PluginInstallService(
        PluginStateStore store,
        HttpClient? http = null,
        Func<bool>? isObsRunning = null,
        Func<string, string>? installRootResolver = null)
    {
        _store = store;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        _isObsRunning = isObsRunning ?? IsObsRunning;
        // 测试接缝：安装根目录可注入（真实运行时仍走 %LocalAppData% / ProgramData）
        _installRootResolver = installRootResolver;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("OBS-Helper/3.3 (+https://github.com/YYRMMAYO/OBS_Helper)");
    }

    public PluginStateStore Store => _store;

    /// <summary>清单里的已安装插件。</summary>
    public IReadOnlyList<PluginInstallEntry> Installed => _store.Load().Installed;

    /// <summary>OBS 是否在运行（安装 / 卸载前必须为 false）。</summary>
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

    /// <summary>本次安装使用的插件根目录（用户级优先）；测试可注入临时目录。</summary>
    public string ResolveInstallRoot(string scope)
        => _installRootResolver?.Invoke(scope) ?? PluginInstallCore.DefaultInstallRoot(
            scope,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));

    /// <summary>安装 / 更新：同一 id 再次安装会先备份旧目录。</summary>
    public async Task<PluginInstallResult> InstallAsync(
        PluginInstallRequest request, IProgress<PluginInstallProgress>? progress, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Id))
            return new PluginInstallResult(false, "PLUGIN_BAD_REQUEST", null, Array.Empty<string>());

        await _single.WaitAsync(ct).ConfigureAwait(false);
        string? staging = null;
        string? target = null;
        string? backup = null;
        var notes = new List<string>();

        try
        {
            if (_isObsRunning())
                return new PluginInstallResult(false, "PLUGIN_OBS_RUNNING", request.Id, notes);

            if (!TryValidateAssetUrl(request.Asset.Url, out var urlError))
            {
                FileLogger.Warn("Plugins", $"安装被拒绝：资产 URL 不在白名单（host={SafeHost(request.Asset.Url)}）");
                return new PluginInstallResult(false, urlError, request.Id, notes);
            }

            if (request.Asset.Size > PluginInstallCore.MaxAssetBytes)
            {
                FileLogger.Warn("Plugins", $"安装被拒绝：资产体积 {request.Asset.Size} 字节超过上限 {PluginInstallCore.MaxAssetBytes}");
                return new PluginInstallResult(false, "PLUGIN_ASSET_TOO_LARGE", request.Id, notes);
            }

            _store.EnsureDirectories();
            var slug = PluginInstallCore.SafeSlug(request.Id);
            var stamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            var zipPath = Path.Combine(_store.DownloadsDirectory, $"{slug}-{stamp}.zip");

            progress?.Report(new PluginInstallProgress("download", request.Asset.Name));
            var (sha, downloaded) = await DownloadAsync(request.Asset.Url, zipPath, ct).ConfigureAwait(false);
            if (!downloaded)
                return new PluginInstallResult(false, "PLUGIN_DOWNLOAD_FAILED", request.Id, notes);

            progress?.Report(new PluginInstallProgress("verify", sha));
            if (!string.IsNullOrWhiteSpace(request.ExpectedSha256)
                && !string.Equals(sha, NormalizeSha(request.ExpectedSha256), StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(zipPath);
                return new PluginInstallResult(false, "PLUGIN_HASH_MISMATCH", request.Id, notes);
            }

            progress?.Report(new PluginInstallProgress("extract", ""));
            using var archive = ZipFile.OpenRead(zipPath);
            var zipError = PluginInstallCore.ValidateArchive(archive, out _, out var normalized);
            if (zipError.Length > 0)
            {
                TryDelete(zipPath);
                return new PluginInstallResult(false, zipError, request.Id, notes);
            }

            var rootPrefix = PluginInstallCore.CommonRootPrefix(normalized);
            var mapped = normalized.Select(e => PluginInstallCore.MapEntry(e, rootPrefix)).ToList();

            var layoutError = PluginInstallCore.ValidateDllLayout(mapped);
            if (layoutError.Length > 0)
            {
                TryDelete(zipPath);
                return new PluginInstallResult(false, layoutError, request.Id, notes);
            }

            staging = Path.Combine(_store.DownloadsDirectory, $"{slug}-{stamp}-staging");
            Directory.CreateDirectory(staging);
            ExtractArchive(archive, rootPrefix, staging);

            // 架构复核：每个 DLL 都必须是 x64 或托管 AnyCPU
            foreach (var dll in mapped.Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
            {
                var full = Path.Combine(staging, dll.Replace('/', Path.DirectorySeparatorChar));
                if (!IsX64Dll(full))
                {
                    TryDeleteDirectory(staging);
                    TryDelete(zipPath);
                    return new PluginInstallResult(false, "PLUGIN_BAD_ARCH", request.Id, notes);
                }
            }

            progress?.Report(new PluginInstallProgress("install", ""));
            var installRoot = ResolveInstallRoot(request.Scope);
            target = Path.Combine(installRoot, slug);

            if (Directory.Exists(target))
            {
                var backupDir = Path.Combine(_store.BackupsDirectory, slug);
                Directory.CreateDirectory(backupDir);
                backup = Path.Combine(backupDir, stamp + "-before-update");
                Directory.Move(target, backup);
            }

            Directory.CreateDirectory(installRoot);
            Directory.Move(staging, target);
            staging = null;

            var entry = new PluginInstallEntry
            {
                Id = request.Id,
                Name = request.Name,
                Version = request.Version,
                InstalledAtUtc = DateTime.UtcNow.ToString("O"),
                SourceRepo = request.SourceRepo,
                AssetName = request.Asset.Name,
                Sha256 = sha,
                InstallDirectory = target,
                Scope = string.Equals(request.Scope, "global", StringComparison.OrdinalIgnoreCase) ? "global" : "user",
                Curated = request.Curated,
                Files = mapped,
                LastBackupDirectory = backup ?? ""
            };

            if (!_store.Upsert(entry))
            {
                // 清单写不进去：把新目录撤掉、旧目录放回去（回滚路径必须和安装一样可靠）
                TryDeleteDirectory(target);
                if (backup is not null && Directory.Exists(backup)) { try { Directory.Move(backup, target); } catch { /* 尽量恢复 */ } }
                return new PluginInstallResult(false, "PLUGIN_STATE_WRITE_FAILED", request.Id, notes);
            }

            _store.PruneBackups(request.Id);
            TryDelete(zipPath);
            notes.Add($"sha256={sha}");
            progress?.Report(new PluginInstallProgress("done", target));
            return new PluginInstallResult(true, null, request.Id, notes);
        }
        catch (OperationCanceledException)
        {
            return new PluginInstallResult(false, "PLUGIN_CANCELLED", request.Id, notes);
        }
        catch (Exception ex)
        {
            FileLogger.Error("Plugins", $"安装插件失败（{request.Id}）：{ex.Message}");
            // 失败回滚：新目录删掉，备份移回来
            try
            {
                if (target is not null && backup is not null && Directory.Exists(backup))
                {
                    TryDeleteDirectory(target);
                    Directory.Move(backup, target);
                }
            }
            catch (Exception rollbackEx)
            {
                FileLogger.Error("Plugins", $"安装失败后的回滚也未完成：{rollbackEx.Message}");
            }
            return new PluginInstallResult(false, "PLUGIN_INSTALL_FAILED", request.Id, new[] { ex.Message });
        }
        finally
        {
            if (staging is not null) TryDeleteDirectory(staging);
            _single.Release();
        }
    }

    /// <summary>卸载：只删除本应用管理的目录，并且先移进备份（保留最近 2 份）。</summary>
    public async Task<PluginInstallResult> UninstallAsync(string id, CancellationToken ct)
    {
        await _single.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_isObsRunning())
                return new PluginInstallResult(false, "PLUGIN_OBS_RUNNING", id, Array.Empty<string>());

            var entry = _store.Find(id);
            if (entry is null)
                return new PluginInstallResult(false, "PLUGIN_NOT_INSTALLED", id, Array.Empty<string>());

            // 清单是本机文件：只允许移动「本应用安装根」或已知 OBS 插件根之下的目录，
            // 防止被篡改的 installed.json 把任意目录当作插件目录移走（X1031Y2）。
            if (!PluginInstallCore.IsSafeManagedDirectory(entry.InstallDirectory, entry.Id, ResolveInstallRoot(entry.Scope)))
                return new PluginInstallResult(false, "PLUGIN_UNSAFE_PATH", id, Array.Empty<string>());

            var slug = PluginInstallCore.SafeSlug(id);
            var backupRoot = Path.Combine(_store.BackupsDirectory, slug);
            Directory.CreateDirectory(backupRoot);
            var backup = Path.Combine(backupRoot, DateTime.Now.ToString("yyyyMMddHHmmss") + "-uninstall");

            if (Directory.Exists(entry.InstallDirectory))
            {
                try { Directory.Move(entry.InstallDirectory, backup); }
                catch (Exception ex)
                {
                    FileLogger.Warn("Plugins", $"卸载时移动目录失败（{entry.InstallDirectory}）：{ex.Message}");
                    return new PluginInstallResult(false, "PLUGIN_UNINSTALL_FAILED", id, new[] { ex.Message });
                }
            }

            if (!_store.Remove(id))
                return new PluginInstallResult(false, "PLUGIN_STATE_WRITE_FAILED", id, Array.Empty<string>());

            _store.PruneBackups(id);
            return new PluginInstallResult(true, null, id, new[] { backup });
        }
        catch (Exception ex)
        {
            FileLogger.Error("Plugins", $"卸载插件失败（{id}）：{ex.Message}");
            return new PluginInstallResult(false, "PLUGIN_UNINSTALL_FAILED", id, new[] { ex.Message });
        }
        finally
        {
            _single.Release();
        }
    }

    /// <summary>回滚到最近一次备份（更新 / 卸载前自动生成）。</summary>
    public async Task<PluginInstallResult> RollbackAsync(string id, CancellationToken ct)
    {
        await _single.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_isObsRunning())
                return new PluginInstallResult(false, "PLUGIN_OBS_RUNNING", id, Array.Empty<string>());

            var entry = _store.Find(id);
            if (entry is null)
                return new PluginInstallResult(false, "PLUGIN_NOT_INSTALLED", id, Array.Empty<string>());

            if (!PluginInstallCore.IsSafeManagedDirectory(entry.InstallDirectory, entry.Id, ResolveInstallRoot(entry.Scope)))
                return new PluginInstallResult(false, "PLUGIN_UNSAFE_PATH", id, Array.Empty<string>());

            var backups = _store.ListBackups(id);
            if (backups.Count == 0)
                return new PluginInstallResult(false, "PLUGIN_NO_BACKUP", id, Array.Empty<string>());

            var target = entry.InstallDirectory;
            var latest = backups[0];
            var aside = Path.Combine(_store.BackupsDirectory, PluginInstallCore.SafeSlug(id),
                DateTime.Now.ToString("yyyyMMddHHmmss") + "-before-rollback");

            if (Directory.Exists(target)) Directory.Move(target, aside);
            try
            {
                Directory.Move(latest, target);
            }
            catch (Exception ex)
            {
                // 还原失败：把当前版本放回去，避免「回滚一下把插件弄没了」
                if (Directory.Exists(aside)) { try { Directory.Move(aside, target); } catch { /* 尽力 */ } }
                return new PluginInstallResult(false, "PLUGIN_ROLLBACK_FAILED", id, new[] { ex.Message });
            }

            entry.LastBackupDirectory = aside;
            _store.Upsert(entry);
            _store.PruneBackups(id);
            return new PluginInstallResult(true, null, id, new[] { "rolled-back" });
        }
        catch (Exception ex)
        {
            FileLogger.Error("Plugins", $"回滚插件失败（{id}）：{ex.Message}");
            return new PluginInstallResult(false, "PLUGIN_ROLLBACK_FAILED", id, new[] { ex.Message });
        }
        finally
        {
            _single.Release();
        }
    }

    // ---------------------------------------------------------------- 内部

    private static bool TryValidateAssetUrl(string? url, out string errorCode)
    {
        errorCode = "";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            errorCode = "PLUGIN_BAD_URL";
            return false;
        }
        if (!PluginInstallCore.IsAllowedDownloadHost(uri.Host))
        {
            errorCode = "PLUGIN_BAD_URL";
            return false;
        }
        return true;
    }

    private async Task<(string Sha256, bool Ok)> DownloadAsync(string url, string destination, CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                FileLogger.Warn("Plugins", $"下载插件失败：HTTP {(int)resp.StatusCode}，host={SafeHost(url)}");
                return ("", false);
            }

            // 重定向后的最终域名也要在白名单里
            var finalHost = resp.RequestMessage?.RequestUri?.Host;
            if (!PluginInstallCore.IsAllowedDownloadHost(finalHost))
            {
                FileLogger.Warn("Plugins", $"下载插件被拒绝：重定向后的域名不在白名单（host={finalHost ?? "?"}）");
                return ("", false);
            }

            if (resp.Content.Headers.ContentLength is { } len && len > PluginInstallCore.MaxAssetBytes)
            {
                FileLogger.Warn("Plugins", $"下载插件被拒绝：响应体 {len} 字节超过上限 {PluginInstallCore.MaxAssetBytes}");
                return ("", false);
            }

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using var input = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[128 * 1024];
            long total = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > PluginInstallCore.MaxAssetBytes)
                {
                    FileLogger.Warn("Plugins", $"下载插件被拒绝：实际体积超过上限 {PluginInstallCore.MaxAssetBytes}");
                    output.Close();
                    TryDelete(destination);
                    return ("", false);
                }
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            }

            return (Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), true);
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Plugins", $"下载插件失败（host={SafeHost(url)}）：{ex.GetType().Name}: {ex.Message}");
            TryDelete(destination);
            return ("", false);
        }
    }

    /// <summary>只记录主机名，绝不记录带签名参数的完整资产 URL。</summary>
    private static string SafeHost(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "?";

    private static void ExtractArchive(ZipArchive archive, string rootPrefix, string staging)
    {
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/')) continue;
            if (!PluginInstallCore.IsSafeEntryPath(entry.FullName, out var normalized)) continue;

            var relative = PluginInstallCore.MapEntry(normalized, rootPrefix);
            if (relative.Length == 0) continue;

            var target = Path.Combine(staging, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!IsUnder(staging, target)) continue;

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static bool IsX64Dll(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var head = new byte[4096];
            var read = fs.Read(head, 0, head.Length);
            return PluginInstallCore.IsX64OrManaged(head.AsSpan(0, read));
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string NormalizeSha(string sha)
    {
        var s = sha.Trim();
        if (s.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) s = s["sha256:".Length..];
        return s.ToLowerInvariant();
    }

    private static bool IsUnder(string root, string target)
    {
        var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var t = Path.GetFullPath(target);
        return t.StartsWith(r, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* 忽略 */ }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { /* 忽略 */ }
    }

    public void Dispose()
    {
        _http.Dispose();
        _single.Dispose();
    }
}
