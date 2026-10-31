using System.IO;
using OBS_Helper.Wpf.Services.Host;
using OBS_Helper.Wpf.Services.Obs;

namespace OBS_Helper.Wpf.Services.Recording;

/// <summary>
/// 录制结束自动归档（X1031Y3 / Q05）：监听 OBS 录制状态从 active → inactive，
/// 读取用户显式开启的自动归档开关与远端目录，调用现有 <see cref="ArchiveService"/> 上传。
///
/// 只在「用户主动勾选」时工作；没有 rclone / 远端目录 / 文件不存在都静默跳过，不打扰录制。
/// </summary>
public sealed class AutoArchiveService : IDisposable
{
    private readonly ObsConnectionService _obs;
    private readonly ArchiveService _archive;
    private readonly LocalStore _store;
    private bool _lastActive;

    public AutoArchiveService(ObsConnectionService obs, ArchiveService archive, LocalStore store)
    {
        _obs = obs;
        _archive = archive;
        _store = store;
        _lastActive = obs.RecordStatus.Active;
        _obs.StateChanged += OnStateChanged;
    }

    private void OnStateChanged()
    {
        var active = _obs.RecordStatus.Active;
        if (_lastActive && !active) TryArchive();
        _lastActive = active;
    }

    private void TryArchive()
    {
        try
        {
            if (_store.GetItem("stream.archive.auto") != "1") return;
            var remote = (_store.GetItem("stream.archive.remote") ?? "").Trim();
            var file = _obs.LastRecordFile;
            if (remote.Length == 0 || string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return;

            _ = Task.Run(async () =>
            {
                var result = await _archive.UploadAsync(file, remote).ConfigureAwait(false);
                if (result.Ok)
                    FileLogger.Info("Archive", $"自动归档完成：{file} -> {remote}");
                else
                    FileLogger.Warn("Archive", $"自动归档失败（{file}）：{result.Code} {result.Detail}");
            });
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Archive", $"自动归档异常：{ex.Message}");
        }
    }

    public void Dispose() => _obs.StateChanged -= OnStateChanged;
}
