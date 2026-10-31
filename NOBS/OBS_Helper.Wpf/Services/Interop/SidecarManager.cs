using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using OBS_Helper.Wpf.Services.Host;
using OBS_Helper.Wpf.Services.Mobile;

namespace OBS_Helper.Wpf.Services.Interop;

/// <summary>
/// 外部进程护栏（V3.4.0 / 批次 0）。
///
/// 约束：应用退出 / 崩溃 / 用户关闭功能时杀掉整棵进程树，不留后台服务；
/// 不注册系统服务、不开机自启；标准输出限流（只保留末尾 200 行用于排障）。
/// </summary>
public sealed class ProcessGuard : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Process> _processes = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    /// <summary>进程退出（含被本类杀掉）时触发：id, 退出码。</summary>
    public event Action<string, int>? Exited;

    public bool IsRunning(string id)
    {
        lock (_gate)
        {
            if (!_processes.TryGetValue(id, out var process)) return false;
            try { return !process.HasExited; }
            catch (InvalidOperationException) { return false; }
        }
    }

    /// <summary>启动进程（参数数组传参，不经过 shell）。</summary>
    public bool Start(string id, string executable, IEnumerable<string>? args = null, string? workingDirectory = null)
    {
        if (!File.Exists(executable)) return false;
        lock (_gate)
        {
            if (_processes.TryGetValue(id, out var existing))
            {
                try { if (!existing.HasExited) return true; } catch (InvalidOperationException) { }
                _processes.Remove(id);
            }

            var info = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(executable) ?? "",
            };
            foreach (var arg in args ?? Array.Empty<string>()) info.ArgumentList.Add(arg);

            try
            {
                var process = new Process { StartInfo = info, EnableRaisingEvents = true };
                process.OutputDataReceived += (_, e) => Log(id, e.Data);
                process.ErrorDataReceived += (_, e) => Log(id, e.Data);
                process.Exited += (_, _) =>
                {
                    int code = -1;
                    try { code = process.ExitCode; } catch (InvalidOperationException) { }
                    Exited?.Invoke(id, code);
                };
                if (!process.Start()) return false;
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                _processes[id] = process;
                return true;
            }
            catch (Exception ex)
            {
                FileLogger.Warn("Sidecar", $"start {id} failed: {ex.GetType().Name}");
                return false;
            }
        }
    }

    /// <summary>停止进程树；超时后强杀。</summary>
    public void Stop(string id, int graceMs = 3000)
    {
        Process? process;
        lock (_gate)
        {
            if (!_processes.TryGetValue(id, out process)) return;
            _processes.Remove(id);
        }
        try
        {
            if (process.HasExited) return;
            try
            {
                process.CloseMainWindow();
                if (process.WaitForExit(graceMs)) return;
            }
            catch (InvalidOperationException) { return; }
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Sidecar", $"stop {id} failed: {ex.GetType().Name}");
        }
    }

    public void StopAll()
    {
        List<string> ids;
        lock (_gate) ids = _processes.Keys.ToList();
        foreach (var id in ids) Stop(id);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopAll();
    }

    private static void Log(string id, string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        FileLogger.Info("Sidecar", $"[{id}] {line.Trim()}");
    }
}

/// <summary>
/// sidecar 管理器（V3.4.0 / 批次 0）：下载 → SHA-256 校验 → 安全解压 → 版本目录 → 启停。
///
/// 安全边界：只从清单允许的 https 域名下载；哈希不符即删除；解压拒绝目录穿越；
/// 二进制不入库、不静默安装/更新；用户必须在界面上确认来源、版本、体积、许可证与哈希。
/// </summary>
public sealed class SidecarManager : IDisposable
{
    private readonly string _root;
    private readonly ProcessGuard _guard;
    private readonly HttpClient _http;
    private readonly Func<Version> _osVersion;
    private SidecarManifest _manifest = new();

    public SidecarManager(string? root = null, ProcessGuard? guard = null, HttpClient? http = null, Func<Version>? osVersion = null)
    {
        _root = root ?? Path.Combine(HostBridge.AppDataDirectory, "sidecars");
        _guard = guard ?? new ProcessGuard();
        _http = http ?? CreateHttpClient();
        _osVersion = osVersion ?? (() => Environment.OSVersion.Version);
    }

    public event Action? Changed;

    public SidecarManifest Manifest => _manifest;

    public string Root => _root;

    /// <summary>加载随包的清单（<c>Assets/sidecars.json</c>），校验失败返回错误列表。</summary>
    public List<string> LoadManifest(string json)
    {
        var (manifest, errors) = SidecarCore.Parse(json);
        if (errors.Count == 0)
        {
            _manifest = manifest;
            Changed?.Invoke();
        }
        return errors;
    }

    public string? InstalledExecutable(SidecarEntry entry)
    {
        var dir = SidecarCore.InstallDirectory(_root, entry);
        if (dir is null) return null;
        var relative = SidecarCore.SafeRelativePath(
            string.IsNullOrEmpty(entry.InstallSubdir) ? entry.Executable : entry.InstallSubdir + "/" + entry.Executable);
        if (relative is null) return null;
        var full = Path.GetFullPath(Path.Combine(dir, relative));
        return File.Exists(full) ? full : null;
    }

    public bool IsInstalled(SidecarEntry entry) => InstalledExecutable(entry) is not null;

    /// <summary>本机是否满足该 sidecar 的运行条件（OS / 架构）。</summary>
    public bool IsSupported(SidecarEntry entry)
    {
        if (!SidecarCore.MeetsMinOs(entry.MinOs, _osVersion())) return false;
        var arch = SidecarCore.CurrentArch;
        return entry.Arch is "any" || string.Equals(entry.Arch, arch, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 下载并安装。进度回调参数为已下载字节 / 总字节（总字节未知时为 0）。
    /// 失败原因返回稳定错误码字符串，由界面映射文案。
    /// </summary>
    public async Task<(bool Ok, string Code, string Path)> InstallAsync(
        SidecarEntry entry, IProgress<(long Done, long Total)>? progress = null, CancellationToken ct = default)
    {
        if (entry.Manual) return (false, FeatureErrors.CamSidecarMissing, "");
        if (!IsSupported(entry)) return (false, FeatureErrors.CamUnsupportedWin7, "");
        if (!Uri.TryCreate(entry.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            return (false, FeatureErrors.CamSidecarStartFailed, "");
        if (!SidecarCore.AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
            return (false, FeatureErrors.CamSidecarStartFailed, "");

        var installDir = SidecarCore.InstallDirectory(_root, entry);
        if (installDir is null) return (false, FeatureErrors.CamSidecarMissing, "");

        var tempDir = Path.Combine(Path.GetTempPath(), "obshelper-sidecar-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var archivePath = Path.Combine(tempDir, "download.zip");
            using (var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode) return (false, FeatureErrors.CamSidecarMissing, "");
                var total = response.Content.Headers.ContentLength ?? entry.Size;
                await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var target = File.Create(archivePath);
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    done += read;
                    progress?.Report((done, total));
                }
            }

            // 固定上限：sidecar 都是几十 MB 级，超过 1 GB 直接拒绝。
            try
            {
                if (new FileInfo(archivePath).Length > 1024L * 1024 * 1024)
                    return (false, FeatureErrors.CamSidecarMissing, "");
            }
            catch (Exception) { return (false, FeatureErrors.CamSidecarMissing, ""); }

            var hash = await HashFileAsync(archivePath, ct).ConfigureAwait(false);
            if (!string.Equals(hash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(archivePath); } catch { }
                return (false, FeatureErrors.CamSidecarStartFailed, "");
            }

            Directory.CreateDirectory(installDir);
            if (!SafeExtract(archivePath, installDir)) return (false, FeatureErrors.CamSidecarStartFailed, "");
            Changed?.Invoke();
            return (true, "", InstalledExecutable(entry) ?? "");
        }
        catch (OperationCanceledException) { return (false, FeatureErrors.CamSidecarStartFailed, ""); }
        catch (Exception ex)
        {
            FileLogger.Warn("Sidecar", $"install failed: {ex.GetType().Name}");
            return (false, FeatureErrors.CamSidecarStartFailed, "");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    /// <summary>启动 sidecar；返回是否已启动（已在运行也返回 true）。</summary>
    public bool Start(SidecarEntry entry, IEnumerable<string>? extraArgs = null)
    {
        var exe = InstalledExecutable(entry);
        if (exe is null) return false;
        var args = entry.Args.Concat(extraArgs ?? Array.Empty<string>()).ToArray();
        return _guard.Start(entry.Id, exe, args, Path.GetDirectoryName(exe));
    }

    public void Stop(SidecarEntry entry) => _guard.Stop(entry.Id);

    public bool IsRunning(SidecarEntry entry) => _guard.IsRunning(entry.Id);

    public void Dispose()
    {
        _guard.Dispose();
        _http.Dispose();
    }

    /// <summary>安全解压：拒绝绝对路径 / <c>..</c> / 符号链接（zip 中通过属性判断）。</summary>
    public static bool SafeExtract(string archivePath, string destination)
    {
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);

            // X1031Y2 安全加固：压缩包解压也要有上限，避免 zip bomb 把磁盘塞满。
            const int maxEntries = 20_000;
            const long maxEntryBytes = 1024L * 1024 * 1024;
            const long maxTotalBytes = 2L * 1024 * 1024 * 1024;
            if (archive.Entries.Count > maxEntries) return false;

            var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
            long totalBytes = 0;
            foreach (var entry in archive.Entries)
            {
                var relative = SidecarCore.SafeRelativePath(entry.FullName);
                if (relative is null) return false;
                var target = Path.GetFullPath(Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(target.TrimEnd(Path.DirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                    return false;
                // 只允许普通文件与目录；拒绝外部属性为符号链接的条目
                var unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
                if (unixMode == 0xA000) return false;
                if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }

                totalBytes += entry.Length;
                if (entry.Length > maxEntryBytes || totalBytes > maxTotalBytes) return false;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }
            return true;
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Sidecar", $"extract failed: {ex.GetType().Name}");
            return false;
        }
    }

    public static async Task<string> HashFileAsync(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OBS-Helper/3.4");
        return client;
    }
}
