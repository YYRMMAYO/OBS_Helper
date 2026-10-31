using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using OBS_Helper.Wpf.Services.Host;

namespace OBS_Helper.Wpf.Services.Subtitles;

/// <summary>
/// whisper.cpp 推荐模型一键下载（X1031Y3）。
///
/// 固定 URL + 固定 SHA-256 + 200 MB 上限；下载中边写边算哈希，校验失败删除临时文件，
/// 绝不覆盖已有可用模型。失败时保留「选择已下载模型」的手动路径。
/// </summary>
public sealed class WhisperModelService
{
    private readonly HttpClient _http;

    public WhisperModelService(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("OBS-Helper/1031.3 (+https://github.com/YYRMMAYO/OBS_Helper)");
    }

    public string TargetPath => Path.Combine(
        HostBridge.AppDataDirectory, "sidecars", "models", WhisperModelCore.FileName);

    public bool IsInstalled => WhisperModelCore.IsInstalled(TargetPath);

    public async Task<(bool Ok, string Code, string Path)> DownloadAsync(
        IProgress<(long Done, long Total)>? progress, CancellationToken ct)
    {
        if (IsInstalled) return (true, "", TargetPath);

        var dir = Path.GetDirectoryName(TargetPath)!;
        var tmp = TargetPath + ".download";
        try
        {
            Directory.CreateDirectory(dir);
            using var resp = await _http.GetAsync(
                WhisperModelCore.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                FileLogger.Warn("Subtitles", $"模型下载失败：HTTP {(int)resp.StatusCode}");
                return (false, "dep.error.download", "");
            }

            var finalHost = resp.RequestMessage?.RequestUri?.Host;
            if (!WhisperModelCore.IsAllowedHost(finalHost))
            {
                FileLogger.Warn("Subtitles", $"模型下载被拒绝：重定向域名不在白名单（host={finalHost ?? "?"}）");
                return (false, "dep.error.download", "");
            }

            if (resp.Content.Headers.ContentLength is { } total && total > WhisperModelCore.MaxBytes)
            {
                FileLogger.Warn("Subtitles", $"模型下载被拒绝：响应体 {total} 字节超过上限");
                return (false, "dep.error.download", "");
            }

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using var input = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var output = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[256 * 1024];
            long done = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                done += read;
                if (done > WhisperModelCore.MaxBytes)
                {
                    FileLogger.Warn("Subtitles", $"模型下载被拒绝：实际体积超过上限");
                    output.Close();
                    TryDelete(tmp);
                    return (false, "dep.error.download", "");
                }
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                progress?.Report((done, resp.Content.Headers.ContentLength ?? 0));
            }

            var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (!string.Equals(actual, WhisperModelCore.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                FileLogger.Warn("Subtitles", $"模型 SHA-256 不匹配：期望 {WhisperModelCore.ExpectedSha256}，实际 {actual}");
                TryDelete(tmp);
                return (false, "dep.error.hash", "");
            }

            File.Move(tmp, TargetPath, overwrite: true);
            return (true, "", TargetPath);
        }
        catch (OperationCanceledException)
        {
            TryDelete(tmp);
            return (false, "dep.error.download", "");
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Subtitles", $"模型下载失败：{ex.GetType().Name}: {ex.Message}");
            TryDelete(tmp);
            return (false, "dep.error.download", "");
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* 忽略 */ }
    }
}
