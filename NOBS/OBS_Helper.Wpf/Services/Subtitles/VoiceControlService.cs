using OBS_Helper.Wpf.Services.Automation;
using OBS_Helper.Wpf.Services.Chat;
using OBS_Helper.Wpf.Services.Mobile;
using OBS_Helper.Wpf.Services.Obs;

namespace OBS_Helper.Wpf.Services.Subtitles;

/// <summary>
/// 语音控制（S03）：字幕文本 → 唤醒词 → 命令白名单 → 破坏性动作人工确认。
/// 只执行白名单动作，不开放任意 OBS 请求；默认关闭。
/// </summary>
public sealed class VoiceControlService : IDisposable
{
    private readonly AsrService _asr;
    private readonly ObsConnectionService _obs;
    private readonly string[] _wakeWords;

    public VoiceControlService(AsrService asr, ObsConnectionService obs, string[]? wakeWords = null)
    {
        _asr = asr;
        _obs = obs;
        _wakeWords = wakeWords ?? VoiceCommandCore.DefaultWakeWords;
        _asr.CaptionReady += OnCaption;
    }

    public bool Enabled { get; private set; }
    public string LastCommand { get; private set; } = "";
    public string LastCode { get; private set; } = "";
    public event Action<string>? Executed;

    /// <summary>破坏性动作确认回调；默认拒绝，由页面注入确认框。</summary>
    public Func<string, bool>? Confirm { get; set; }

    public void SetEnabled(bool enabled) => Enabled = enabled;

    private void OnCaption(string text, string display)
    {
        if (!Enabled) return;
        var (command, needsConfirm, code) = VoiceCommandCore.Parse(text, _wakeWords);
        if (command is null)
        {
            LastCode = code;
            return;
        }
        if (needsConfirm && !(Confirm?.Invoke($"语音命令「{command.Id}」需要确认，是否执行？") ?? false))
        {
            LastCode = FeatureErrors.VoiceCommandDenied;
            return;
        }
        _ = ExecuteAsync(command.Id);
    }

    private async Task ExecuteAsync(string id)
    {
        switch (id)
        {
            case "record.toggle":
                await _obs.ToggleRecordAsync().ConfigureAwait(false);
                break;
            case "stream.toggle":
                if (_obs.StreamStatus.Active) await _obs.StopStreamAsync().ConfigureAwait(false);
                else await _obs.StartStreamAsync().ConfigureAwait(false);
                break;
            case "scene.next":
            case "scene.prev":
            {
                var scenes = _obs.Scenes.Select(s => s.Name).ToList();
                if (scenes.Count == 0) break;
                var index = Math.Max(0, scenes.IndexOf(_obs.CurrentScene));
                var next = id == "scene.next"
                    ? scenes[(index + 1) % scenes.Count]
                    : scenes[(index - 1 + scenes.Count) % scenes.Count];
                await _obs.SetSceneAsync(next).ConfigureAwait(false);
                break;
            }
            case "mute.toggle":
                foreach (var input in _obs.AudioInputs.Where(a => a.IsAudio))
                    await _obs.SetMuteAsync(input.Name, !input.Muted).ConfigureAwait(false);
                break;
            case "brb":
            {
                var brb = _obs.Scenes.FirstOrDefault(s => s.Name.Contains("BRB") || s.Name.Contains("休息"));
                if (brb is not null) await _obs.SetSceneAsync(brb.Name).ConfigureAwait(false);
                break;
            }
            default:
                LastCode = FeatureErrors.VoiceCommandDenied;
                return;
        }
        LastCommand = id;
        LastCode = "";
        Executed?.Invoke(id);
    }

    public void Dispose() => _asr.CaptionReady -= OnCaption;
}
