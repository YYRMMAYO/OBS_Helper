using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using OBS_Helper.Wpf.Models.Plugins;
using OBS_Helper.Wpf.Services.Plugins;

namespace OBS_Helper.Wpf.Services.Dependencies;

/// <summary>一键配置的结果：Code 为稳定错误码（空串表示成功）。</summary>
public sealed record DependencyInstallResult(
    bool Ok, string Code, string Path, string Version, bool DigestVerified);

/// <summary>
/// 开源依赖的「一键配置」服务（X1031Y2）：
/// 查最新 Release → 挑 Windows x64 zip → 下载 → SHA-256 校验 → 安全解压 → 定位 exe → 落到应用数据目录。
///
/// 为什么不用 sidecars.json 的固定 URL：FFmpeg / rclone 更新频繁，固定 URL 很快过期；
/// 这里改为读 GitHub 最新 Release，并用 GitHub 返回的 <c>digest</c> 做完整性校验。
/// 若上游没有提供 digest，仍然只从 GitHub 官方 HTTPS 资产域下载，并在结果里标记未校验。
/// </summary>
public sealed class DependencyInstallService
{
    private readonly string _root;
    private readonly GitHubSearchService _lookup;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public DependencyInstallService(string root, GitHubSearchService? lookup = null, HttpClient? http = null)
    {
        _root = Path.GetFullPath(root);
        _lookup = lookup ?? new GitHubSearchService();
        _ownsHttp = http is null;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        if (_ownsHttp)
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("OBS-Helper/1031 (+https://github.com/YYRMMAYO/OBS_Helper)");
    }

    public string Root => _root;

    /// <summary>下载并安装到应用数据目录；不修改系统 PATH、不写注册表。</summary>
    public async Task<DependencyInstallResult> InstallAsync(
        DependencySpec spec, IProgress<(long Done, long Total)>? progress, CancellationToken ct)
    {
        if (spec is null || string.IsNullOrWhiteSpace(spec.GitHubRepo))
            return new DependencyInstallResult(false, "dep.error.invalid", "", "", false);

        string? tempRoot = null;
        try
        {
            var (version, assets, lookupError) = await _lookup
                .GetLatestReleaseAsync(spec.GitHubRepo, ct).ConfigureAwait(false);
            if (lookupError.Length > 0)
                return new DependencyInstallResult(false, "dep.error.offline", "", "", false);

            var asset = DependencyInstallCore.SelectAsset(spec, assets);
            if (asset is null)
                return new DependencyInstallResult(false, "dep.error.noAsset", "", version, false);

            if (!DependencyInstallCore.IsAllowedAssetUrl(asset.Url) ||
                asset.Size > DependencyInstallCore.MaxAssetBytes)
                return new DependencyInstallResult(false, "dep.error.download", "", version, false);

            tempRoot = Path.Combine(Path.GetTempPath(), "obshelper-dep-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            var zipPath = Path.Combine(tempRoot, "asset.zip");

            var download = await DownloadAsync(asset.Url, zipPath, progress, ct).ConfigureAwait(false);
            if (!download.Ok)
                return new DependencyInstallResult(false, "dep.error.download", "", version, false);

            var expected = DependencyInstallCore.NormalizeDigest(asset.Digest);
            var verified = expected.Length > 0;
            if (verified && !string.Equals(download.Sha256, expected, StringComparison.OrdinalIgnoreCase))
                return new DependencyInstallResult(false, "dep.error.hash", "", version, false);

            var extractDir = Path.Combine(tempRoot, "extract");
            Directory.CreateDirectory(extractDir);
            var extractError = DependencyInstallCore.ExtractZip(zipPath, extractDir);
            if (extractError.Length > 0)
                return new DependencyInstallResult(false, extractError, "", version, verified);

            var exe = DependencyInstallCore.FindExecutable(extractDir, spec.Executable);
            if (exe is null)
                return new DependencyInstallResult(false, "dep.error.noExe", "", version, verified);

            var relative = Path.GetRelativePath(extractDir, exe);
            var finalDir = DependencyInstallCore.InstallDirectory(_root, spec.Id, version);
            if (finalDir is null || !IsUnderRoot(finalDir))
                return new DependencyInstallResult(false, "dep.error.path", "", version, verified);

            Directory.CreateDirectory(Path.GetDirectoryName(finalDir)!);
            if (Directory.Exists(finalDir))
                Directory.Delete(finalDir, recursive: true);

            try
            {
                Directory.Move(extractDir, finalDir);
            }
            catch (IOException)
            {
                CopyDirectory(extractDir, finalDir);
                Directory.Delete(extractDir, recursive: true);
            }

            var finalExe = Path.GetFullPath(Path.Combine(finalDir, relative));
            if (!File.Exists(finalExe))
                return new DependencyInstallResult(false, "dep.error.noExe", "", version, verified);

            PruneOldVersions(spec.Id, finalDir);
            return new DependencyInstallResult(true, "", finalExe, version, verified);
        }
        catch (OperationCanceledException)
        {
            return new DependencyInstallResult(false, "dep.error.download", "", "", false);
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Dependency", $"一键配置失败（{spec.Id}）：{ex.GetType().Name}: {ex.Message}");
            return new DependencyInstallResult(false, "dep.error.download", "", "", false);
        }
        finally
        {
            if (tempRoot is not null)
            {
                try { Directory.Delete(tempRoot, recursive: true); } catch (Exception) { }
            }
        }
    }

    private async Task<(bool Ok, string Sha256)> DownloadAsync(
        string url, string destination, IProgress<(long Done, long Total)>? progress, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return (false, "");

            if (!DependencyInstallCore.IsAllowedAssetUrl(response.RequestMessage?.RequestUri?.AbsoluteUri))
                return (false, "");

            var total = response.Content.Headers.ContentLength ?? 0;
            if (total > DependencyInstallCore.MaxAssetBytes) return (false, "");

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[128 * 1024];
            long done = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                done += read;
                if (done > DependencyInstallCore.MaxAssetBytes)
                {
                    output.Close();
                    TryDelete(destination);
                    return (false, "");
                }
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                progress?.Report((done, total));
            }

            return (true, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return (false, "");
        }
    }

    private bool IsUnderRoot(string path)
    {
        var full = Path.GetFullPath(path);
        var root = _root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private void PruneOldVersions(string id, string keep)
    {
        try
        {
            var safeId = DependencyInstallCore.SafeSegment(id);
            if (safeId.Length == 0) return;
            var parent = Path.Combine(_root, safeId);
            if (!Directory.Exists(parent)) return;
            foreach (var dir in Directory.GetDirectories(parent))
            {
                if (string.Equals(Path.GetFullPath(dir), Path.GetFullPath(keep), StringComparison.OrdinalIgnoreCase))
                    continue;
                try { Directory.Delete(dir, recursive: true); } catch (Exception) { }
            }
        }
        catch (Exception)
        {
            // 清理旧版本失败不影响本次安装
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { }
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
