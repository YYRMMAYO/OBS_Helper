using System;
using OBS_Helper.Wpf.Services.Automation;
using OBS_Helper.Wpf.Services.Obs;
using OBS_Helper.Wpf.Services.Streaming;

namespace OBS_Helper.Wpf.Services.Shell;

/// <summary>
/// 直播安全哨兵（Q06）：周期性检查静音/掉帧/断流，给出建议；执行动作必须人工确认并审计。
/// 黑屏检测依赖画面采样，当前版本先不做（避免误判），在状态文档中记录。
/// </summary>
public sealed class PanicService : IDisposable
{
    private readonly ObsConnectionService _obs;
    private readonly System.Threading.Timer _timer;
    private readonly PanicCore.Auditor _auditor = new();
    private readonly object _gate = new();

    public PanicService(ObsConnectionService obs)
    {
        _obs = obs;
        _timer = new System.Threading.Timer(_ => Check(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public bool Enabled { get; private set; }
    public IReadOnlyList<PanicCore.Action> LastSuggestion { get; private set; } = Array.Empty<PanicCore.Action>();
    public IReadOnlyList<PanicCore.AuditEntry> AuditLog => _auditor.Entries;
    public event Action<IReadOnlyList<PanicCore.Action>, string>? Suggestion;

    public void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        _timer.Change(enabled ? 0 : Timeout.Infinite, enabled ? 5000 : Timeout.Infinite);
    }

    private void Check()
    {
        if (!Enabled || !_obs.IsConnected) return;
        var silent = _obs.AudioInputs.Any(a => a.IsAudio) && _obs.AudioInputs.Where(a => a.IsAudio).All(a => a.Muted);
        var dropping = _obs.StreamStatus.Active && _obs.Stats.OutputSkipRatio > 0.02;
        var reconnecting = _obs.StreamStatus.Reconnecting;
        var actions = PanicCore.Suggest(false, silent, dropping, false);
        if (reconnecting) actions = actions.Concat(new[] { new PanicCore.Action("brb", "切到 BRB 场景", true) }).ToList();
        LastSuggestion = actions;
        if (actions.Count > 0)
        {
            var reason = silent ? "全部音频已静音" : dropping ? "输出掉帧偏高" : "推流重连中";
            Suggestion?.Invoke(actions, reason);
        }
    }

    /// <summary>执行安全动作（调用方必须先确认）。</summary>
    public async Task ExecuteAsync(bool stopStream, CancellationToken ct = default)
    {
        lock (_gate)
        {
            foreach (var input in _obs.AudioInputs.Where(i => i.IsAudio && !i.Muted))
                _ = _obs.SetMuteAsync(input.Name, true);
        }
        _auditor.Record("mute", "panic", true);
        var brb = _obs.Scenes.FirstOrDefault(s =>
            s.Name.Contains("BRB", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("休息", StringComparison.Ordinal));
        if (brb is not null)
        {
            await _obs.SetSceneAsync(brb.Name).ConfigureAwait(false);
            _auditor.Record("brb", "panic", true);
        }
        if (stopStream)
        {
            await _obs.StopStreamAsync().ConfigureAwait(false);
            _auditor.Record("stop", "panic", true);
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
    }
}
