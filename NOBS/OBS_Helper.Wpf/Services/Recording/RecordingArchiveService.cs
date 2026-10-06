using System.Diagnostics;
using System.IO;
using System.Text;
using OBS_Helper.Wpf.Services.Host;
using OBS_Helper.Wpf.Services.ObsConfig;

namespace OBS_Helper.Wpf.Services.Recording;

/// <summary>
/// 录制档案与收尾流水线（V3.0 / D3）。
///
/// 结构：档案存在 <c>prefs.json</c>（复用 <see cref="LocalStore"/>，键 <c>recording_archive</c>），
/// 每次停止录制写一条：时间 / 时长 / 大小 / 路径 / 丢帧率 / 是否分段 / 打点。
/// 围绕它提供四件事（与提案一致）：
/// <list type="number">
///   <item>最近 N 次录制的清单（一眼看出哪次是空文件）；</item>
///   <item>按规则重命名（日期 / 场景 / 预设前缀）；</item>
///   <item>批量转 MP4（复用既有 ffmpeg 重封装，**不重编码**）；</item>
///   <item>录制中打点 → 停止后导出章节（<c>.ffmetadata</c> + 可直接贴简介的 <c>.chapters.txt</c>）。</item>
/// </list>
///
/// 边界：本服务只在自己的档案里记录与操作**录制产物**，绝不碰 OBS 配置目录
/// （那条路径有独立的护栏与事务，见 <see cref="ObsSafePath"/> / <see cref="FileTx"/>）。
/// </summary>
public sealed class RecordingArchiveService
{
    /// <summary>档案上限：只留最近这么多条（够回溯，也不会让 prefs.json 无限增长）。</summary>
    public const int MaxEntries = 200;

    private const string StoreKey = "recording_archive";

    private readonly LocalStore _store;
    private readonly object _gate = new();

    public RecordingArchiveService(LocalStore store) => _store = store;

    /// <summary>读全部档案（最新的在前）。失败时返回空列表 —— 档案是辅助信息，不能因此让界面报错。</summary>
    public IReadOnlyList<RecordingArchiveEntry> All()
    {
        try
        {
            var list = _store.GetObject<List<RecordingArchiveEntry>>(StoreKey);
            if (list is null) return Array.Empty<RecordingArchiveEntry>();
            // 反序列化后 Markers 可能为 null（旧版本记录 / 手改过 prefs.json）
            return list.Select(e => e with { Markers = e.Markers ?? Array.Empty<RecordingMarker>() })
                       .OrderByDescending(e => e.StartedLocal)
                       .ToList();
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Archive", $"读取录制档案失败（按空处理）：{ex.Message}");
            return Array.Empty<RecordingArchiveEntry>();
        }
    }

    /// <summary>最近 <paramref name="count"/> 条。</summary>
    public IReadOnlyList<RecordingArchiveEntry> Recent(int count = 10)
        => All().Take(Math.Max(0, count)).ToList();

    /// <summary>
    /// 登记一条录制产物。
    ///
    /// 去重口径：同一路径 + 同一开始时刻视为同一条（重复停止事件 / 手动刷新都不会写两遍）。
    /// </summary>
    public void Add(RecordingArchiveEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Path)) return;

        lock (_gate)
        {
            try
            {
                var list = All().ToList();
                list.RemoveAll(e => string.Equals(e.Path, entry.Path, StringComparison.OrdinalIgnoreCase)
                                    && e.StartedLocal == entry.StartedLocal);
                list.Insert(0, entry);

                if (list.Count > MaxEntries) list = list.Take(MaxEntries).ToList();
                _store.SetObject(StoreKey, list);
            }
            catch (Exception ex)
            {
                FileLogger.Warn("Archive", $"写入录制档案失败：{ex.Message}");
            }
        }
    }

    /// <summary>把某条档案的路径改成新路径（重命名之后调用）。</summary>
    public void UpdatePath(RecordingArchiveEntry entry, string newPath)
    {
        lock (_gate)
        {
            try
            {
                var list = All().ToList();
                var index = list.FindIndex(e => string.Equals(e.Path, entry.Path, StringComparison.OrdinalIgnoreCase)
                                                && e.StartedLocal == entry.StartedLocal);
                if (index < 0) return;
                list[index] = list[index] with { Path = newPath };
                _store.SetObject(StoreKey, list);
            }
            catch (Exception ex)
            {
                FileLogger.Warn("Archive", $"更新档案路径失败：{ex.Message}");
            }
        }
    }

    // ---------------------------------------------------------------- 重命名

    /// <summary>
    /// 按规则重命名录像文件。返回新路径；失败返回 null 并给出原因。
    ///
    /// 安全口径：**绝不覆盖**已存在的文件（同名时自动加序号），且只改文件名、不移动目录 ——
    /// 用户的录像可能在异盘 / 网络盘上，移动的代价与风险都远大于收益。
    /// </summary>
    public (string? NewPath, string? Error) Rename(RecordingArchiveEntry entry, RenameRule rule)
    {
        try
        {
            if (!File.Exists(entry.Path)) return (null, Strings.T("d3.rename.fileMissing"));

            var dir = Path.GetDirectoryName(entry.Path);
            if (string.IsNullOrEmpty(dir)) return (null, Strings.T("d3.rename.fileMissing"));

            var target = Path.Combine(dir, RecordingArchiveCore.BuildNewFileName(entry, rule));
            var n = 2;
            while (File.Exists(target) && !string.Equals(target, entry.Path, StringComparison.OrdinalIgnoreCase))
            {
                var ext = Path.GetExtension(target);
                target = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(entry.Path)}_{SafeRuleName(entry, rule)}_{n++}{ext}");
            }

            if (string.Equals(target, entry.Path, StringComparison.OrdinalIgnoreCase)) return (entry.Path, null);

            File.Move(entry.Path, target);
            UpdatePath(entry, target);
            return (target, null);
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Archive", $"重命名录像失败：{ex.Message}");
            return (null, ex.Message);
        }
    }

    private static string SafeRuleName(RecordingArchiveEntry entry, RenameRule rule)
        => RecordingArchiveCore.Sanitize(RecordingArchiveCore.BuildNewFileName(entry, rule))
            .Replace(Path.GetExtension(entry.Path), "");

    // ---------------------------------------------------------------- 章节导出

    /// <summary>
    /// 导出章节文件（<c>.ffmetadata</c> 给转码用，<c>.chapters.txt</c> 给贴简介用）。
    ///
    /// 没有打点时返回 (false, 说明)，不生成空文件 —— 免得用户以为「生成了但内容是空的」。
    /// </summary>
    public (bool Ok, string Message) ExportChapters(RecordingArchiveEntry entry)
    {
        if (entry.Markers.Count == 0) return (false, Strings.T("d3.chapters.noMarkers"));

        try
        {
            if (!File.Exists(entry.Path)) return (false, Strings.T("d3.rename.fileMissing"));

            var title = Path.GetFileNameWithoutExtension(entry.Path);
            var ffPath = RecordingArchiveCore.ChapterPathFor(entry.Path, ffmpegFormat: true);
            var txtPath = RecordingArchiveCore.ChapterPathFor(entry.Path, ffmpegFormat: false);

            var ff = RecordingArchiveCore.BuildFfMetadata(entry.Markers, entry.Duration, title);
            if (ff.Length == 0) return (false, Strings.T("d3.chapters.noMarkers"));

            File.WriteAllText(ffPath, ff, new UTF8Encoding(false));
            File.WriteAllText(txtPath, RecordingArchiveCore.BuildChapterList(entry.Markers, title), new UTF8Encoding(false));

            return (true, Strings.T("d3.chapters.exported", Path.GetFileName(txtPath)));
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Archive", $"导出章节失败：{ex.Message}");
            return (false, ex.Message);
        }
    }

    // ---------------------------------------------------------------- 批量转 MP4

    /// <summary>
    /// 批量转 MP4（顺序执行，带进度回调）。返回成功 / 失败条数与逐条说明。
    ///
    /// 顺序而不是并发：ffmpeg 是 CPU/IO 密集型，同时跑多个只会互相拖慢，
    /// 而且用户往往是在同一块盘上操作。
    /// </summary>
    public async Task<(int Ok, int Failed, IReadOnlyList<string> Messages)> BatchRemuxAsync(
        IEnumerable<RecordingArchiveEntry> entries, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        int ok = 0, failed = 0;
        var messages = new List<string>();

        foreach (var entry in entries)
        {
            if (ct.IsCancellationRequested) break;

            var name = Path.GetFileName(entry.Path);
            progress?.Report(Strings.T("d3.remux.progress", name));

            if (!File.Exists(entry.Path))
            {
                failed++;
                messages.Add(Strings.T("d3.remux.missing", name));
                continue;
            }

            // 已经打过点的：把章节一起写进 MP4（省得用户再跑一遍 ffmpeg）
            var result = entry.Markers.Count > 0
                ? await RemuxWithChaptersAsync(entry).ConfigureAwait(false)
                : await RecordingToolsService.RemuxToMp4Async(entry.Path).ConfigureAwait(false);

            if (result.Ok) { ok++; messages.Add(Strings.T("d3.remux.done", name)); }
            else { failed++; messages.Add(Strings.T("d3.remux.failed", name, result.Message)); }
        }

        return (ok, failed, messages);
    }

    /// <summary>带章节的重封装：先落 ffmetadata，再交给 ffmpeg <c>-map_metadata</c>。</summary>
    private async Task<(bool Ok, string Message)> RemuxWithChaptersAsync(RecordingArchiveEntry entry)
    {
        var ffmpeg = RecordingToolsService.FindFfmpeg();
        if (ffmpeg is null) return (false, Strings.T("recording.remux.noFfmpegShort"));

        var metaPath = RecordingArchiveCore.ChapterPathFor(entry.Path, ffmpegFormat: true);
        try
        {
            var meta = RecordingArchiveCore.BuildFfMetadata(entry.Markers, entry.Duration,
                Path.GetFileNameWithoutExtension(entry.Path));
            File.WriteAllText(metaPath, meta, new UTF8Encoding(false));

            var output = RecordingToolsService.BuildOutputPath(entry.Path);
            var args = RecordingToolsService.BuildRemuxArgsWithChapters(entry.Path, metaPath, output);

            var psi = new ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = string.Join(' ', args.Select(Quote)),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };

            using var proc = Process.Start(psi);
            if (proc is null) return (false, Strings.T("recording.remux.startFailed"));

            var stderrTask = proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync().ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            if (proc.ExitCode != 0 || !File.Exists(output))
            {
                var tail = stderr.Length > 400 ? stderr[^400..] : stderr;
                FileLogger.Warn("Archive", $"带章节转 MP4 失败 exit={proc.ExitCode}: {tail}");
                return (false, Strings.T("recording.remux.failed", proc.ExitCode, tail));
            }

            return (true, Strings.T("recording.remux.done", output));
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Archive", $"带章节转 MP4 异常：{ex.Message}");
            return (false, Strings.T("recording.remux.exception", ex.Message));
        }
        finally
        {
            // 章节文件按需保留：转完就删中间产物（.chapters.txt 仍留给用户贴简介）
            try { if (File.Exists(metaPath)) File.Delete(metaPath); } catch (Exception) { }
        }
    }

    private static string Quote(string arg)
        => arg.Length > 0 && arg.Contains(' ') ? "\"" + arg + "\"" : arg;
}
