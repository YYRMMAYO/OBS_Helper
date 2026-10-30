using System.Diagnostics;
using System.IO;
using System.Text;
using OBS_Helper.Wpf.Services.Automation;
using OBS_Helper.Wpf.Services.Interop;
using OBS_Helper.Wpf.Services.Mobile;
using OBS_Helper.Wpf.Services.Obs;
using OBS_Helper.Wpf.Services.Streaming;

namespace OBS_Helper.Wpf.Services.Recording;

/// <summary>
/// 录制自动归档（Q05）：rclone sidecar 复制 → 校验 → 重试 → 可撤销。
/// 凭据由用户在 rclone config 中维护，应用不读取、不保存；只接受本地绝对路径与远端名。
/// </summary>
public sealed class ArchiveService
{
    private readonly string _rclonePath;

    public ArchiveService(string? rclonePath = null) => _rclonePath = rclonePath ?? "";

    public string LastError { get; private set; } = "";

    public async Task<(bool Ok, string Code, string Detail)> UploadAsync(string file, string remote, int bandwidthLimitKbps = 0, int retries = 3, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_rclonePath) || !File.Exists(_rclonePath)) return (false, FeatureErrors.ArchiveRemoteMissing, "");
        var plan = new ArchiveCore.Plan
        {
            LocalPath = file,
            RemoteName = Path.GetFileName(file),
            BandwidthLimitKbps = bandwidthLimitKbps,
            MaxRetries = retries,
        };
        var check = ArchiveCore.Validate(plan, _rclonePath, remote);
        if (!check.Ok) return (false, check.Code, "");
        var args = ArchiveCore.BuildArgs(plan, remote);
        var result = await RunAsync(args, TimeSpan.FromHours(2), ct).ConfigureAwait(false);
        if (!result.Ok) LastError = result.Detail;
        return result;
    }

    public async Task<(bool Ok, string Code, string Detail)> VerifyAsync(string file, string remote, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_rclonePath)) return (false, FeatureErrors.ArchiveRemoteMissing, "");
        var args = new[] { "check", file, remote.TrimEnd('/') + "/" + Path.GetFileName(file), "--size-only" };
        return await RunAsync(args, TimeSpan.FromMinutes(30), ct).ConfigureAwait(false);
    }

    /// <summary>撤销归档：只删远端副本，不删本地录制。</summary>
    public async Task<(bool Ok, string Code, string Detail)> DeleteRemoteAsync(string remote, string fileName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_rclonePath)) return (false, FeatureErrors.ArchiveRemoteMissing, "");
        if (fileName.Contains("..") || fileName.Contains('/') || fileName.Contains('\\')) return (false, FeatureErrors.ArchiveUploadFailed, "");
        var args = new[] { "deletefile", remote.TrimEnd('/') + "/" + fileName };
        return await RunAsync(args, TimeSpan.FromMinutes(10), ct).ConfigureAwait(false);
    }

    private async Task<(bool Ok, string Code, string Detail)> RunAsync(IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            var info = new ProcessStartInfo(_rclonePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var arg in args) info.ArgumentList.Add(arg);
            using var process = Process.Start(info);
            if (process is null) return (false, FeatureErrors.ArchiveRemoteMissing, "");
            var error = process.StandardError.ReadToEndAsync();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(timeout);
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            var detail = await error.ConfigureAwait(false);
            return process.ExitCode == 0
                ? (true, "", detail.Length > 500 ? detail[^500..] : detail)
                : (false, FeatureErrors.ArchiveUploadFailed, detail.Length > 500 ? detail[^500..] : detail);
        }
        catch (OperationCanceledException) { return (false, FeatureErrors.ArchiveUploadFailed, "timeout"); }
        catch (Exception ex) { return (false, FeatureErrors.ArchiveUploadFailed, ex.GetType().Name); }
    }
}
