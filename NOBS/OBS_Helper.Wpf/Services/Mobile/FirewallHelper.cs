using System.Diagnostics;
using System.IO;

namespace OBS_Helper.Wpf.Services.Mobile;

/// <summary>
/// MediaMTX 防火墙放行（X1031Y3）：只允许对 sidecar 目录内的 mediamtx.exe 添加规则，
/// 不接受任意路径；需要 UAC 提权，用户取消时返回失败并回退到手动命令。
/// </summary>
public static class FirewallHelper
{
    public const string RuleName = "OBS Helper MediaMTX";
    public const string OutRuleName = "OBS Helper MediaMTX Out";

    /// <summary>只接受 sidecar 根目录内的 mediamtx.exe，防止被用来给任意程序放行。</summary>
    public static bool IsAllowedPath(string? exePath, string? sidecarRoot)
    {
        if (string.IsNullOrWhiteSpace(exePath) || string.IsNullOrWhiteSpace(sidecarRoot)) return false;
        try
        {
            var full = Path.GetFullPath(exePath);
            var root = Path.GetFullPath(sidecarRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetFileName(full), "mediamtx.exe", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>删除旧规则并添加私网入站 / 出站规则；UAC 提权由 Windows 处理。</summary>
    public static async Task<(bool Ok, string Error)> AllowMediamtxAsync(
        string exePath, string sidecarRoot, CancellationToken ct)
    {
        if (!IsAllowedPath(exePath, sidecarRoot)) return (false, "unsafe path");

        var arguments =
            $"/c netsh advfirewall firewall delete rule name=\"{RuleName}\" >nul 2>&1 & " +
            $"netsh advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow " +
            $"program=\"{exePath}\" enable=yes profile=private & " +
            $"netsh advfirewall firewall delete rule name=\"{OutRuleName}\" >nul 2>&1 & " +
            $"netsh advfirewall firewall add rule name=\"{OutRuleName}\" dir=out action=allow " +
            $"program=\"{exePath}\" enable=yes profile=private";

        var psi = new ProcessStartInfo("cmd.exe", arguments)
        {
            Verb = "runas",
            UseShellExecute = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        try
        {
            using var process = Process.Start(psi);
            if (process is null) return (false, "start failed");
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            return process.ExitCode == 0 ? (true, "") : (false, "exit " + process.ExitCode);
        }
        catch (OperationCanceledException)
        {
            return (false, "cancelled");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
