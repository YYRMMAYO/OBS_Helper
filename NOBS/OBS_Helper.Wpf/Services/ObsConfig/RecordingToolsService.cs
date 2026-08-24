using System.Diagnostics;
using System.IO;
using OBS_Helper.Wpf.Services.Obs;

namespace OBS_Helper.Wpf.Services.ObsConfig;

/// <summary>录像目录解析结果。</summary>
public sealed record RecordingDirResult(string? Dir, string Source, bool Exists)
{
    public static readonly RecordingDirResult NotFound =
        new(null, "未找到", false);
}

/// <summary>
/// 录像工具服务（V2.6 工具箱）：
/// 1) 从 OBS 配置解析当前录像保存目录（global.ini → Profile → basic.ini），一键在资源管理器打开；
/// 2) 探测 ffmpeg 并用 <c>-c copy</c> 把 MKV / Hybrid MP4 无损重封装为 MP4（不重编码、秒级完成）。
///
/// 只读定位 + 独立进程调用，绝不修改 OBS 配置；任何失败都返回可读信息而非抛异常。
/// </summary>
public sealed class RecordingToolsService
{
    private readonly ObsPathService _paths;

    public RecordingToolsService(ObsPathService paths) => _paths = paths;

    // ------------------------------------------------------------ 录像目录

    /// <summary>解析当前录像保存目录。解析不到时回退系统「视频」文件夹。</summary>
    public async Task<RecordingDirResult> TryGetRecordingDirAsync()
    {
        try
        {
            var loc = await _paths.LocateAsync().ConfigureAwait(false);
            if (!loc.Exists) return DefaultVideos();

            var globalIniText = TryRead(Path.Combine(loc.ConfigDir, "global.ini"));
            string? basicIniText = null;
            if (PreflightCheckCore.ParseIni(globalIniText ?? "").TryGetValue("basic.profiledir", out var profileDir) &&
                !string.IsNullOrWhiteSpace(profileDir))
            {
                basicIniText = TryRead(Path.Combine(
                    loc.ConfigDir, "basic", "profiles", profileDir!, "basic.ini"));
            }

            return ResolveRecordingDir(globalIniText, basicIniText);
        }
        catch (Exception ex)
        {
            FileLogger.Warn("RecordingTools", $"解析录像目录失败：{ex.Message}");
            return DefaultVideos();
        }
    }

    /// <summary>
    /// 从 INI 文本解析录像目录的纯逻辑：
    /// 高级模式 advout.recfilepath → 简单模式 simpleoutput.filepath → 系统「视频」文件夹。
    /// </summary>
    internal static RecordingDirResult ResolveRecordingDir(string? globalIniText, string? basicIniText)
    {
        _ = globalIniText; // 预留：便携模式等场景下的差异化处理
        var ini = PreflightCheckCore.ParseIni(basicIniText ?? "");
        foreach (var key in new[] { "advout.recfilepath", "simpleoutput.filepath" })
        {
            if (ini.TryGetValue(key, out var dir) && !string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                return new RecordingDirResult(dir, $"OBS 配置（{key}）", true);
        }
        return DefaultVideos();
    }

    private static RecordingDirResult DefaultVideos()
    {
        try
        {
            var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            if (!string.IsNullOrEmpty(videos)) return new RecordingDirResult(videos, "系统「视频」文件夹（OBS 默认）", Directory.Exists(videos));
        }
        catch (Exception) { }
        return RecordingDirResult.NotFound;
    }

    /// <summary>在资源管理器中打开目录。返回错误消息或 null（成功）。</summary>
    public static string? OpenInExplorer(string dir)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return "目录不存在。";
            using var _ = Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
            return null;
        }
        catch (Exception ex)
        {
            return $"打开失败：{ex.Message}";
        }
    }

    // ------------------------------------------------------------ MKV → MP4

    /// <summary>探测 ffmpeg.exe：PATH → 常见安装位置。找不到返回 null。</summary>
    public static string? FindFfmpeg()
    {
        foreach (var dir in CandidateDirs())
        {
            try
            {
                var path = Path.Combine(dir, "ffmpeg.exe");
                if (File.Exists(path)) return path;
            }
            catch (Exception) { }
        }
        return null;
    }

    private static IEnumerable<string> CandidateDirs()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var seg in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (seen.Add(seg)) yield return seg;
        }
        // 常见手动安装 / 下载解压位置
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            AppDomain.CurrentDomain.BaseDirectory,
        };
        foreach (var root in roots)
        {
            if (string.IsNullOrEmpty(root)) continue;
            yield return Path.Combine(root, "ffmpeg", "bin");
            yield return root;
        }
    }

    /// <summary>构造 ffmpeg 重封装参数（纯逻辑，供测试）：-c copy 不重编码，faststart 便于网络播放。</summary>
    internal static string[] BuildRemuxArgs(string input, string output) => new[]
    {
        "-y", "-i", input, "-c", "copy", "-movflags", "+faststart", output
    };

    /// <summary>生成与源文件同名的 .mp4 输出路径；已存在时追加时间戳避免覆盖。</summary>
    public static string BuildOutputPath(string inputFile)
    {
        var dir = Path.GetDirectoryName(inputFile) ?? ".";
        var name = Path.GetFileNameWithoutExtension(inputFile);
        var output = Path.Combine(dir, name + ".mp4");
        if (!File.Exists(output)) return output;
        return Path.Combine(dir, $"{name}_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
    }

    /// <summary>
    /// 无损重封装为 MP4。<paramref name="input"/> 需为存在的媒体文件；
    /// 返回 (是否成功, 用户可读结果说明)。
    /// </summary>
    public static async Task<(bool Ok, string Message)> RemuxToMp4Async(string input)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(input) || !File.Exists(input))
                return (false, "文件不存在。");

            var ffmpeg = FindFfmpeg();
            if (ffmpeg is null)
            {
                return (false,
                    "本机未找到 ffmpeg。\n" +
                    "替代方案：① OBS 内 文件 → 录像转封装（无需 ffmpeg）；" +
                    "② 从 ffmpeg.org 或 gyan.dev 下载后加入 PATH 再试。");
            }

            var output = BuildOutputPath(input);
            var psi = new ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = string.Join(' ', BuildRemuxArgs(input, output).Select(Quote)),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };

            using var proc = Process.Start(psi);
            if (proc is null) return (false, "无法启动 ffmpeg 进程。");

            var stderrTask = proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync().ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            if (proc.ExitCode != 0 || !File.Exists(output))
            {
                var tail = stderr.Length > 400 ? stderr[^400..] : stderr;
                FileLogger.Warn("RecordingTools", $"重封装失败 exit={proc.ExitCode}: {tail}");
                return (false, $"重封装失败（exit {proc.ExitCode}）。文件可能已损坏或不是有效媒体文件。\n{tail}");
            }

            return (true, $"完成：{output}");
        }
        catch (Exception ex)
        {
            FileLogger.Warn("RecordingTools", $"重封装异常：{ex.Message}");
            return (false, $"重封装异常：{ex.Message}");
        }
    }

    /// <summary>含空格路径的引号包裹（ffmpeg 命令行用）。</summary>
    private static string Quote(string s)
        => s.Contains(' ') ? "\"" + s.Replace("\"", "\\\"") + "\"" : s;

    private static string? TryRead(string file)
    {
        try { return File.Exists(file) ? File.ReadAllText(file) : null; }
        catch (Exception) { return null; }
    }
}
