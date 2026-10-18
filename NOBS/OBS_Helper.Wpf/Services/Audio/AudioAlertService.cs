using System.Windows;
using System.Windows.Threading;
using OBS_Helper.Wpf.Localization;
using OBS_Helper.Wpf.Services.Obs;
using OBS_Helper.Wpf.Services.Shell;

namespace OBS_Helper.Wpf.Services.Audio;

/// <summary>
/// 录制 / 推流中的静音与音量过低提醒（V3.1.0 / §12.5.1 第 5 项）。
///
/// 只读 OBS 已经上报的状态，不发出任何写请求：不会自动取消静音、不会改音量。
/// 用托盘气泡提醒，不用置顶窗口，不打断录制。
/// </summary>
public sealed class AudioAlertService : IDisposable
{
    private readonly ObsConnectionService _obs;
    private readonly TrayService _tray;
    private readonly DispatcherTimer? _timer;

    /// <summary>每个输入「当前状态从何时开始」，用于持续时间判定。</summary>
    private readonly Dictionary<string, DateTime> _since = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>本次录制 / 推流会话内已经提醒过的输入。</summary>
    private readonly HashSet<string> _alerted = new(StringComparer.OrdinalIgnoreCase);

    private bool _wasOutputActive;
    private bool _disposed;

    public AudioAlertService(ObsConnectionService obs, TrayService tray)
    {
        _obs = obs;
        _tray = tray;

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _timer.Tick += (_, _) => Evaluate();
        }
    }

    /// <summary>是否启用（设置页开关）。</summary>
    public bool Enabled => _tray.Settings.AudioMuteAlertEnabled;

    /// <summary>按设置启动 / 停止计时器。</summary>
    public void ApplyEnabled()
    {
        if (_disposed) return;
        if (Enabled) _timer?.Start();
        else
        {
            _timer?.Stop();
            Reset();
        }
    }

    private void Evaluate()
    {
        if (_disposed || !Enabled) return;

        var recording = _obs.RecordStatus.Active;
        var streaming = _obs.StreamStatus.Active;
        var active = recording || streaming;
        var paused = recording && !streaming && _obs.RecordStatus.Paused;

        // 新会话：清空「已提醒」，让下一次录制还能收到提醒
        if (active && !_wasOutputActive) Reset();
        _wasOutputActive = active;
        if (!active)
        {
            Reset();
            return;
        }

        var now = DateTime.UtcNow;
        var alive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var input in _obs.AudioInputs)
        {
            if (string.IsNullOrWhiteSpace(input.Name)) continue;
            alive.Add(input.Name);

            var stateKey = input.Muted ? "muted" : "level";
            var key = input.Name + "|" + stateKey;

            if (!_since.TryGetValue(key, out var since))
            {
                _since[key] = now;
                continue;
            }

            var kind = AudioAlertCore.Evaluate(
                active, paused, input.Muted, input.VolumeDb, now - since,
                _alerted.Contains(input.Name));

            if (kind == AudioAlertKind.None) continue;

            _alerted.Add(input.Name);
            var (titleKey, messageKey) = AudioAlertCore.KeysFor(kind);
            var db = input.VolumeDb.ToString("0.#");
            _tray.Notify(Strings.T(titleKey), Strings.T(messageKey, input.Name, db));
        }

        // 设备列表变了：清掉不再存在的输入，避免字典无限增长
        foreach (var key in _since.Keys.ToList())
        {
            var name = key.Split('|')[0];
            if (!alive.Contains(name)) _since.Remove(key);
        }
    }

    private void Reset()
    {
        _since.Clear();
        _alerted.Clear();
        _wasOutputActive = false;
    }

    public void Dispose()
    {
        _disposed = true;
        _timer?.Stop();
        Reset();
    }
}
