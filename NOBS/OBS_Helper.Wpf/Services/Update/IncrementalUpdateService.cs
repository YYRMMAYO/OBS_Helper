using OBS_Helper.Wpf.Localization;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OBS_Helper.Wpf.Services.Host;

namespace OBS_Helper.Wpf.Services.Update;

/// <summary>
/// 增量更新的客户端编排：下载增量包 → 解压校验 → 交由自举进程（<see cref="UpdaterBootstrap"/>）应用。
///
/// 设计要点：
/// <list type="bullet">
///   <item>增量包由构建脚本（build.ps1）比对上一版本清单生成，只含变更文件，下载量远小于整包；</item>
///   <item>应用前逐文件校验 SHA-256，任一不匹配即放弃并回退完整安装包；</item>
///   <item>应用阶段统一走「重启自举」：旧进程退出后由 <c>--apply-update</c> 进程替换文件再拉起新进程，
///         规避「运行中的 exe / DLL 被锁定无法覆盖」问题。安装版（Program Files 不可写）自动提权，便携版直接执行。</item>
/// </list>
/// </summary>
public sealed class IncrementalUpdateService
{
    /// <summary>当前应用目录（自举进程与被更新目标均为这里）。</summary>
    public static string AppDir => AppContext.BaseDirectory;

    /// <summary>
    /// 增量包体积上限（V3.0）。完整安装包一直有 <c>MaxInstallerBytes</c> 上限，增量包原先没有 ——
    /// 异常 / 恶意响应可以把系统盘写满（增量包落在 %LocalAppData%）。
    /// 512MB 对「只含变更文件」的增量包而言已经非常宽松。
    /// </summary>
    private const long MaxDeltaBytes = 512L * 1024 * 1024;

    /// <summary>增量包暂存目录：%LocalAppData%\OBS_Helper\updates\pending\。</summary>
    public static string PendingDir => Path.Combine(HostBridge.AppDataDirectory, "updates", "pending");

    /// <summary>
    /// 最近一次准备失败是否属于**完整性校验未通过**（V3.0 审查修正）。
    ///
    /// 为什么要这个布尔：界面原先靠 `error.Contains("SHA-256")` 判断该上报哪个错误码 ——
    /// 拿本地化文案当判据，改一次措辞就会静默变成「下载失败」。结构化标志从根上避免这件事。
    /// </summary>
    public bool LastFailureWasIntegrity { get; private set; }

    /// <summary>暂存目录内的新文件根（保持发布目录的相对结构）。</summary>
    public static string PendingFilesDir => Path.Combine(PendingDir, "files");

    /// <summary>增量包临时下载文件：放应用私有目录（而非系统 %TEMP%），且命名避开
    /// 安装包清理器的识别模式（OBS_Helper_Update_*），不会被误删。</summary>
    private static string TempDownloadPath =>
        Path.Combine(HostBridge.AppDataDirectory, "updates", "dl_" + Guid.NewGuid().ToString("N") + ".zip");

    private static string ManifestPath => Path.Combine(PendingDir, "update_manifest.json");

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OBS_Helper.Wpf-Updater/1.0");
        return client;
    }

    /// <summary>
    /// 检测应用目录是否可写（便携版 = 可写；Program Files 安装版 = 不可写，需提权自举）。
    /// 用临时文件探测，避免依赖「是否管理员」等不精确判断。
    /// </summary>
    public static bool IsAppDirWritable()
    {
        try
        {
            var probe = Path.Combine(AppDir, $".write_test_{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 下载增量包并解压、校验，做好应用前的一切准备。失败时返回 Error 并清理暂存目录。
    /// </summary>
    public async Task<(UpdateManifest? Manifest, string? Error)> PrepareDeltaAsync(
        string assetUrl, IProgress<(long Received, long? Total)>? progress, CancellationToken ct = default,
        string? expectedSha256 = null)
    {
        LastFailureWasIntegrity = false;
        try
        {
            // 1) 下载 zip 到应用私有目录的临时文件
            var tmp = TempDownloadPath;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(tmp)!);
                using var resp = await Http.GetAsync(assetUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    var buffer = new byte[81920];
                    long received = 0;
                    var total = resp.Content.Headers.ContentLength;
                    // V3.0：增量包也要有体积上限（完整包一直有，增量包原先没有）—— 异常响应不该写满系统盘
                    if (total is > MaxDeltaBytes) return (null, Strings.T("delta.tooLarge"));
                    while (true)
                    {
                        var n = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
                        if (n == 0) break;
                        await fs.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                        received += n;
                        if (received > MaxDeltaBytes) return (null, Strings.T("delta.tooLarge"));
                        progress?.Report((received, total));
                    }
                }
            }
            catch (Exception ex)
            {
                return (null, Strings.T("delta.downloadFailed", ex.Message));
            }

            // V3.0：有 GitHub 摘要就逐字节校验（清单里的 SHA-256 只能证明「zip 与自带清单一致」，
            // 清单本身没有锚点；摘要由托管方给出，是独立于包体的那一个锚点）。
            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                try
                {
                    var actual = FileHasher.Sha256(tmp);
                    var want = UpdateService.NormalizeDigest(expectedSha256);
                    if (!string.Equals(actual, want, StringComparison.OrdinalIgnoreCase))
                    {
                        FileLogger.Error("Delta", $"增量包 SHA-256 与 GitHub 摘要不一致，已丢弃：期望 {want}，实际 {actual}");
                        LastFailureWasIntegrity = true;
                        try { File.Delete(tmp); } catch (Exception) { }
                        return (null, Strings.T("delta.packageHashMismatch"));
                    }
                }
                catch (Exception ex)
                {
                    return (null, Strings.T("delta.downloadFailed", ex.Message));
                }
            }
            else
            {
                FileLogger.Warn("Delta", "增量包没有 GitHub 摘要，仅做清单内逐文件校验");
            }

            // 2) 清空旧暂存并安全解压（逐条目校验路径，防 zip-slip）
            try
            {
                ClearPending();
                SafeExtractToDirectory(tmp, PendingDir);
            }
            catch (Exception ex)
            {
                return (null, Strings.T("delta.extractFailed", ex.Message));
            }
            finally
            {
                try { File.Delete(tmp); } catch (Exception) { }
            }

            // 3) 读取并校验清单
            if (!File.Exists(ManifestPath))
            {
                ClearPending();
                return (null, Strings.T("delta.noManifest"));
            }

            UpdateManifest? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<UpdateManifest>(
                    File.ReadAllText(ManifestPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException)
            {
                manifest = null;
            }

            if (manifest is null || manifest.Format != 1 || string.IsNullOrWhiteSpace(manifest.TargetVersion))
            {
                ClearPending();
                return (null, Strings.T("delta.invalidManifest"));
            }

            // 4) 兼容性：当前版本必须 ≥ 基准版本，否则说明跳版本，增量包不适用
            var current = typeof(UpdateService).Assembly.GetName().Version;
            var baseV = UpdateService.ParseVersion(manifest.BaseVersion);
            if (current is not null && baseV is not null && current < baseV)
            {
                ClearPending();
                return (null, Strings.T("delta.baseNewer", baseV));
            }

            // 5) 逐文件 SHA-256 校验
            var filesDir = PendingFilesDir;
            foreach (var entry in manifest.Files)
            {
                var rel = ToLocalPath(entry.Path);
                var src = Path.Combine(filesDir, rel);
                if (!File.Exists(src))
                {
                    ClearPending();
                    return (null, Strings.T("delta.missingFile", entry.Path));
                }
                try
                {
                    if (new FileInfo(src).Length != entry.Size
                        || !string.Equals(FileHasher.Sha256(src), entry.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        ClearPending();
                        return (null, Strings.T("delta.hashMismatch", entry.Path));
                    }
                }
                catch (Exception ex)
                {
                    ClearPending();
                    return (null, Strings.T("delta.fileUnreadable", entry.Path, ex.Message));
                }
            }

            FileLogger.Info("Delta", $"增量包就绪：{manifest.BaseVersion} → {manifest.TargetVersion}，{manifest.Files.Count} 个文件变更");
            return (manifest, null);
        }
        catch (Exception ex)
        {
            ClearPending();
            return (null, Strings.T("delta.prepFailed", ex.Message));
        }
    }

    /// <summary>
    /// 启动自举进程应用更新（不阻塞，立即返回）。安装版目录不可写时自动提权（触发一次 UAC）。
    /// 应用会在旧进程退出后由自举进程替换文件并拉起新进程。
    /// </summary>
    public (bool Launched, string? Error) LaunchBootstrap(UpdateManifest manifest)
    {
        var ownExe = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(ownExe) || !File.Exists(ownExe))
        {
            return (false, Strings.T("delta.noSelfPath"));
        }

        var args = string.Join(' ',
            UpdaterBootstrap.ArgFlag,
            "\"" + PendingDir + "\"",
            Environment.ProcessId);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ownExe,
                Arguments = args,
                UseShellExecute = true,
                WorkingDirectory = AppDir,
            };

            // 安装版（Program Files 等不可写目录）→ 请求提权；便携版直接以当前身份运行
            if (!IsAppDirWritable())
            {
                psi.Verb = "runas";
            }

            Process.Start(psi);
            FileLogger.Info("Delta", $"已启动自举进程（{args}），等待应用退出后完成替换");
            return (true, null);
        }
        catch (Exception ex)
        {
            // 常见原因：用户拒绝 UAC、目录权限异常
            return (false, Strings.T("delta.launchFailed", ex.Message));
        }
    }

    /// <summary>清空暂存目录（下载前、失败回退时调用）。</summary>
    public static void ClearPending()
    {
        try
        {
            if (Directory.Exists(PendingDir)) Directory.Delete(PendingDir, recursive: true);
        }
        catch (Exception)
        {
            // 个别文件被占用时清不掉，不阻塞主流程
        }
    }

    /// <summary>
    /// 安全解压：逐条目校验相对路径（拒绝 <c>..</c> / 根路径），防止被篡改的增量包 zip-slip。
    /// </summary>
    private static void SafeExtractToDirectory(string zipPath, string destDir)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.FullName)) continue;

            var rel = UpdatePaths.NormalizeRel(entry.FullName); // 非法路径直接抛 InvalidDataException
            var dst = Path.Combine(destDir, rel);

            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(dst);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            entry.ExtractToFile(dst, overwrite: true);
        }
    }

    /// <summary>把清单里的正斜杠相对路径转为本地路径（拒绝路径穿越）。</summary>
    internal static string ToLocalPath(string rel) => UpdatePaths.NormalizeRel(rel);
}
