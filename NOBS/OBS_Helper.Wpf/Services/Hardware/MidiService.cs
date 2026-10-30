using System.Runtime.InteropServices;
using OBS_Helper.Wpf.Services.Automation;
using OBS_Helper.Wpf.Services.Obs;
using OBS_Helper.Wpf.Services.Shell;

namespace OBS_Helper.Wpf.Services.Hardware;

/// <summary>
/// MIDI / 脚踏板（E01）：WinMM midiIn P/Invoke，映射到白名单动作。
/// 只监听输入，不接管设备；映射冲突在保存前校验；可整体关闭。
/// </summary>
public sealed class MidiService : IDisposable
{
    private const uint CallbackFunction = 0x00030000;
    private const uint MidiIoStatusData = 0x3C0;
    private delegate void MidiInProc(IntPtr handle, uint message, IntPtr instance, IntPtr param1, IntPtr param2);

    [DllImport("winmm.dll")] private static extern uint midiInGetNumDevs();
    [DllImport("winmm.dll")] private static extern uint midiInOpen(out IntPtr handle, uint deviceId, MidiInProc callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] private static extern uint midiInStart(IntPtr handle);
    [DllImport("winmm.dll")] private static extern uint midiInStop(IntPtr handle);
    [DllImport("winmm.dll")] private static extern uint midiInClose(IntPtr handle);

    private readonly ObsConnectionService _obs;
    private readonly TeleprompterService _prompter;
    private readonly MidiInProc _callback;
    private IntPtr _handle = IntPtr.Zero;
    private List<MidiCore.Mapping> _mappings = new();

    public MidiService(ObsConnectionService obs, TeleprompterService prompter)
    {
        _obs = obs;
        _prompter = prompter;
        _callback = OnMidiMessage;
    }

    public bool IsRunning => _handle != IntPtr.Zero;
    public string LastAction { get; private set; } = "";
    public event Action<string>? ActionTriggered;
    public event Action<string>? Error;

    public static int DeviceCount => (int)midiInGetNumDevs();

    public void SetMappings(IEnumerable<MidiCore.Mapping> mappings)
    {
        var list = mappings.ToList();
        var (ok, code, detail) = MidiCore.Validate(list);
        if (!ok) Error?.Invoke(detail.Length > 0 ? detail : code);
        else _mappings = list;
    }

    public bool Start(uint deviceId = 0)
    {
        if (IsRunning) return true;
        var result = midiInOpen(out _handle, deviceId, _callback, IntPtr.Zero, CallbackFunction);
        if (result != 0) { _handle = IntPtr.Zero; Error?.Invoke("midiInOpen " + result); return false; }
        result = midiInStart(_handle);
        if (result != 0) { midiInClose(_handle); _handle = IntPtr.Zero; Error?.Invoke("midiInStart " + result); return false; }
        return true;
    }

    public void Stop()
    {
        if (_handle == IntPtr.Zero) return;
        try { midiInStop(_handle); } catch { }
        try { midiInClose(_handle); } catch { }
        _handle = IntPtr.Zero;
    }

    private void OnMidiMessage(IntPtr handle, uint message, IntPtr instance, IntPtr param1, IntPtr param2)
    {
        try
        {
            if ((message & 0xF0) != MidiIoStatusData) return;
            var status = (int)(param1.ToInt64() & 0xFF);
            var data1 = (int)((param1.ToInt64() >> 8) & 0xFF);
            var data2 = (int)((param1.ToInt64() >> 16) & 0xFF);
            var channel = (status & 0x0F) + 1;
            var kind = status & 0xF0;
            int? note = kind == 0x90 && data2 > 0 ? data1 : null;
            int? controller = kind == 0xB0 ? data1 : null;
            if (note is null && controller is null) return;
            var mapping = MidiCore.Match(_mappings, "default", channel, note, controller);
            if (mapping is null) return;
            LastAction = mapping.Action;
            ActionTriggered?.Invoke(mapping.Action);
            _ = ExecuteAsync(mapping);
        }
        catch (Exception)
        {
        }
    }

    private async Task ExecuteAsync(MidiCore.Mapping mapping)
    {
        switch (mapping.Action)
        {
            case "record.toggle": await _obs.ToggleRecordAsync().ConfigureAwait(false); break;
            case "stream.toggle": await _obs.ToggleStreamAsync().ConfigureAwait(false); break;
            case "virtualcam.toggle": await _obs.ToggleVirtualCamAsync().ConfigureAwait(false); break;
            case "replay.save": await _obs.SaveReplayBufferAsync().ConfigureAwait(false); break;
            case "studio.toggle": await _obs.ToggleStudioModeAsync().ConfigureAwait(false); break;
            case "mute.toggle":
                foreach (var input in _obs.AudioInputs.Where(a => a.IsAudio))
                    await _obs.SetMuteAsync(input.Name, !input.Muted).ConfigureAwait(false);
                break;
            case "scene.set":
                if (mapping.Argument.Length > 0) await _obs.SetSceneAsync(mapping.Argument).ConfigureAwait(false);
                break;
            case "scene.next":
            case "scene.prev":
            {
                var scenes = _obs.Scenes.Select(s => s.Name).ToList();
                if (scenes.Count == 0) break;
                var index = Math.Max(0, scenes.IndexOf(_obs.CurrentScene));
                var next = mapping.Action == "scene.next"
                    ? scenes[(index + 1) % scenes.Count]
                    : scenes[(index - 1 + scenes.Count) % scenes.Count];
                await _obs.SetSceneAsync(next).ConfigureAwait(false);
                break;
            }
            case "teleprompter.playpause": _prompter.TogglePlayPause(); break;
        }
    }

    public void Dispose() => Stop();
}
