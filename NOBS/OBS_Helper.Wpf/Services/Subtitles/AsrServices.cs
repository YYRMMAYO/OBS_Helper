using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using OBS_Helper.Wpf.Services.Mobile;
using OBS_Helper.Wpf.Services.Chat;
using OBS_Helper.Wpf.Services.Interop;
using OBS_Helper.Wpf.Services.Obs;

namespace OBS_Helper.Wpf.Services.Subtitles;

internal enum EDataFlow { Render, Capture, All }
internal enum ERole { Console, Multimedia, Communications }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IntPtr devices);
    int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    int RegisterEndpointNotificationCallback(IntPtr client);
    int UnregisterEndpointNotificationCallback(IntPtr client);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    int OpenPropertyStore(uint access, out IntPtr properties);
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    int GetState(out uint state);
}

[ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient
{
    int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity,
        ref WAVEFORMATEX format, IntPtr sessionGuid);
    int GetBufferSize(out uint frames);
    int GetStreamLatency(out long latency);
    int GetCurrentPadding(out uint frames);
    int IsFormatSupported(int shareMode, ref WAVEFORMATEX format, out IntPtr closest);
    int GetMixFormat(out IntPtr format);
    int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
    int Start();
    int Stop();
    int Reset();
    int SetEventHandle(IntPtr handle);
    int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
}

[ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioCaptureClient
{
    int GetBuffer(out IntPtr data, out uint frames, out uint flags, out long devicePosition, out long qpcPosition);
    int ReleaseBuffer(uint frames);
    int GetNextPacketSize(out uint frames);
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
internal struct WAVEFORMATEX
{
    public ushort FormatTag;
    public ushort Channels;
    public uint SamplesPerSec;
    public uint AvgBytesPerSec;
    public ushort BlockAlign;
    public ushort BitsPerSample;
    public ushort Size;
}

/// <summary>
/// WASAPI 采集（S01）：系统声音走 loopback，麦克风走 capture。
/// 只实现本应用需要的接口，零 NuGet；不支持/设备消失时给出 ASR_AUDIO_DEVICE。
/// </summary>
public sealed class WasapiCapture : IDisposable
{
    private const int AUDCLNT_SHAREMODE_SHARED = 0;
    private const uint AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
    private static readonly Guid IidAudioClient = new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
    private static readonly Guid IidCaptureClient = new("C8ADBD64-E71E-48A0-A4DE-185C395CD317");
    private static readonly Guid ClsidMMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");

    private readonly bool _loopback;
    private readonly AudioRingBuffer _ring;
    private Thread? _thread;
    private volatile bool _running;
    private IAudioClient? _client;
    private IAudioCaptureClient? _capture;
    private int _sampleRate;
    private int _channels;
    private bool _isFloat;

    public WasapiCapture(bool loopback, AudioRingBuffer ring)
    {
        _loopback = loopback;
        _ring = ring;
    }

    public bool IsCapturing => _running;
    public string LastError { get; private set; } = "";

    public bool Start()
    {
        if (_running) return true;
        try
        {
            var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidMMDeviceEnumerator)!)!;
            var flow = _loopback ? EDataFlow.Render : EDataFlow.Capture;
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(flow, ERole.Console, out var device));

            var iid = IidAudioClient;
            Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out var instance));
            var client = (IAudioClient)instance;
            _client = client;

            Marshal.ThrowExceptionForHR(client.GetMixFormat(out var formatPtr));
            var format = Marshal.PtrToStructure<WAVEFORMATEX>(formatPtr);
            _sampleRate = (int)format.SamplesPerSec;
            _channels = Math.Max(1, (int)format.Channels);
            _isFloat = format.FormatTag == 3;
            Marshal.FreeCoTaskMem(formatPtr);

            var streamFlags = _loopback ? AUDCLNT_STREAMFLAGS_LOOPBACK : 0u;
            var hr = client.Initialize(AUDCLNT_SHAREMODE_SHARED, streamFlags, 10_000_000, 0, ref format, IntPtr.Zero);
            if (hr < 0)
            {
                LastError = "initialize 0x" + hr.ToString("X8");
                return false;
            }

            var iidCapture = IidCaptureClient;
            Marshal.ThrowExceptionForHR(client.GetService(ref iidCapture, out var captureInstance));
            _capture = (IAudioCaptureClient)captureInstance;
            Marshal.ThrowExceptionForHR(client.Start());

            _running = true;
            _thread = new Thread(CaptureLoop) { IsBackground = true, Name = "WASAPI-Capture" };
            _thread.Start();
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.GetType().Name;
            Cleanup();
            return false;
        }
    }

    private void CaptureLoop()
    {
        var buffer = new List<float>(_sampleRate);
        while (_running)
        {
            try
            {
                if (_capture is null) break;
                var hr = _capture.GetNextPacketSize(out var packetFrames);
                if (hr < 0) break;
                while (packetFrames > 0)
                {
                    hr = _capture.GetBuffer(out var data, out var frames, out var flags, out _, out _);
                    if (hr < 0) break;
                    if (frames > 0 && data != IntPtr.Zero)
                    {
                        var samples = ReadSamples(data, (int)frames, flags);
                        var mono = SpeechSegmenter.DownmixToMono(samples, _channels);
                        buffer.AddRange(SpeechSegmenter.ResampleTo16k(mono, _sampleRate));
                        while (buffer.Count >= 320)
                        {
                            _ring.Write(buffer.GetRange(0, 320).ToArray());
                            buffer.RemoveRange(0, 320);
                        }
                    }
                    _capture.ReleaseBuffer(frames);
                    hr = _capture.GetNextPacketSize(out packetFrames);
                    if (hr < 0) break;
                }
            }
            catch (Exception)
            {
                break;
            }
            Thread.Sleep(50);
        }
    }

    private float[] ReadSamples(IntPtr data, int frames, uint flags)
    {
        const uint silent = 0x2;
        var count = frames * _channels;
        var result = new float[count];
        if ((flags & silent) != 0) return result;
        if (_isFloat)
        {
            var array = new float[count];
            Marshal.Copy(data, array, 0, count);
            return array;
        }
        var pcm = new short[count];
        Marshal.Copy(data, pcm, 0, count);
        for (var i = 0; i < count; i++) result[i] = pcm[i] / 32768f;
        return result;
    }

    public void Stop()
    {
        _running = false;
        try { _thread?.Join(500); } catch { }
        _thread = null;
        Cleanup();
    }

    private void Cleanup()
    {
        try { _client?.Stop(); } catch { }
        if (_capture is not null && Marshal.IsComObject(_capture)) Marshal.ReleaseComObject(_capture);
        if (_client is not null && Marshal.IsComObject(_client)) Marshal.ReleaseComObject(_client);
        _capture = null;
        _client = null;
    }

    public void Dispose() => Stop();
}

/// <summary>whisper.cpp 外部进程引擎（S01）：一次一段，参数数组传参，临时 WAV 即时删除。</summary>
public sealed class WhisperCppEngine
{
    private readonly string _executable;
    private readonly string _model;
    private readonly string _language;
    private readonly int _threads;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _single = new(1, 1);

    public WhisperCppEngine(string executable, string model, string language = "auto", int threads = 2, TimeSpan? timeout = null)
    {
        _executable = executable;
        _model = model;
        _language = language;
        _threads = threads;
        _timeout = timeout ?? TimeSpan.FromSeconds(60);
    }

    public bool IsReady => File.Exists(_executable) && File.Exists(_model);

    /// <summary>转写一段 16 kHz 单声道样本；失败返回 (空串, 错误码)。</summary>
    public async Task<(string Text, string Code)> TranscribeAsync(float[] samples, CancellationToken ct)
    {
        if (!IsReady) return ("", FeatureErrors.AsrModelMissing);
        if (!await _single.WaitAsync(0, ct).ConfigureAwait(false)) return ("", FeatureErrors.AsrModelMissing);
        var wav = Path.Combine(Path.GetTempPath(), "obshelper-asr-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            await File.WriteAllBytesAsync(wav, SpeechSegmenter.ToWav(samples), ct).ConfigureAwait(false);
            var info = new ProcessStartInfo(_executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var arg in WhisperArgs.Build(_model, wav, _language, _threads))
                info.ArgumentList.Add(arg);
            using var process = Process.Start(info);
            if (process is null) return ("", FeatureErrors.AsrModelMissing);
            var stdout = process.StandardOutput.ReadToEndAsync();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(_timeout);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            if (process.ExitCode != 0) return ("", FeatureErrors.AsrModelMissing);
            return (WhisperArgs.ParseOutput(await stdout.ConfigureAwait(false)), "");
        }
        catch (OperationCanceledException) { return ("", FeatureErrors.AsrModelMissing); }
        catch (Exception) { return ("", FeatureErrors.AsrModelMissing); }
        finally
        {
            try { File.Delete(wav); } catch { }
            _single.Release();
        }
    }
}

/// <summary>字幕发布（S01）：优先 OBS 文本源，失败时由 overlay 承担；300~500 ms 节流。</summary>
public sealed class CaptionPublisher
{
    private readonly ObsConnectionService _obs;
    private readonly SubtitleCore.Throttle _throttle = new();
    public string InputName { get; set; } = "实时字幕";

    public CaptionPublisher(ObsConnectionService obs) => _obs = obs;

    public async Task<(bool Ok, string Code)> PublishAsync(string text, CancellationToken ct = default)
    {
        if (!_throttle.TryAcquire()) return (true, "");
        if (!_obs.IsConnected) return (false, FeatureErrors.MobObsOffline);
        try
        {
            var scene = await _obs.GetCurrentProgramSceneAsync().ConfigureAwait(false);
            if (string.IsNullOrEmpty(scene)) return (false, FeatureErrors.MobObsOffline);
            var exists = await _obs.RawRequestAsync("GetInputSettings",
                new Dictionary<string, object?> { ["inputName"] = InputName }, ct).ConfigureAwait(false);
            if (!exists.Ok)
            {
                var created = await _obs.RawRequestAsync("CreateInput", new Dictionary<string, object?>
                {
                    ["sceneName"] = scene,
                    ["inputName"] = InputName,
                    ["inputKind"] = "text_gdiplus_v3",
                    ["inputSettings"] = new Dictionary<string, object?>
                    {
                        ["text"] = text,
                        ["font"] = new Dictionary<string, object?>
                        {
                            ["face"] = "Microsoft YaHei",
                            ["size"] = 42,
                            ["flags"] = 0,
                        },
                        ["color"] = unchecked((int)0xFFFFFFFF),
                    },
                    ["sceneItemEnabled"] = true,
                }, ct).ConfigureAwait(false);
                return created.Ok ? (true, "") : (false, FeatureErrors.MobObsOffline);
            }
            var updated = await _obs.RawRequestAsync("SetInputSettings", new Dictionary<string, object?>
            {
                ["inputName"] = InputName,
                ["inputSettings"] = new Dictionary<string, object?> { ["text"] = text },
                ["overlay"] = true,
            }, ct).ConfigureAwait(false);
            return updated.Ok ? (true, "") : (false, FeatureErrors.MobObsOffline);
        }
        catch (Exception)
        {
            return (false, FeatureErrors.MobObsOffline);
        }
    }
}

/// <summary>字幕管线（S01/S02）：采集 → VAD/分段 → whisper.cpp → 合并 → OBS/overlay 发布。</summary>
public sealed class AsrService : IDisposable
{
    private readonly ObsConnectionService _obs;
    private readonly AudioRingBuffer _ring = new();
    private WasapiCapture? _capture;
    private WhisperCppEngine? _engine;
    private CaptionPublisher? _publisher;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private readonly SpeechSegmenter _segmenter = new();
    private readonly List<string> _recent = new();
    private TranslationCore? _translator;

    public AsrService(ObsConnectionService obs, TranslationCore? translator = null)
    {
        _obs = obs;
        _translator = translator;
    }

    public bool IsRunning { get; private set; }

    /// <summary>设置/关闭翻译（双语字幕）；null 表示单语。</summary>
    public void SetTranslator(TranslationCore? translator) => _translator = translator;
    public bool Loopback { get; private set; } = true;
    public string LastText { get; private set; } = "";
    public string LastCode { get; private set; } = "";
    public double LatencyMs { get; private set; }
    public event Action<string, string>? CaptionReady;

    public (bool Ok, string Code) Configure(string whisperExe, string modelPath, string language = "auto", int threads = 2)
    {
        _engine = new WhisperCppEngine(whisperExe, modelPath, language, threads);
        return _engine.IsReady ? (true, "") : (false, FeatureErrors.AsrModelMissing);
    }

    public bool Start(bool loopback)
    {
        if (IsRunning) return true;
        if (_engine is null || !_engine.IsReady) { LastCode = FeatureErrors.AsrModelMissing; return false; }
        Loopback = loopback;
        _capture = new WasapiCapture(loopback, _ring);
        if (!_capture.Start()) { LastCode = FeatureErrors.AsrAudioDevice; _capture.Dispose(); _capture = null; return false; }
        _publisher = new CaptionPublisher(_obs);
        _cts = new CancellationTokenSource();
        IsRunning = true;
        _loop = Task.Run(() => LoopAsync(_cts.Token));
        return true;
    }

    public void Stop()
    {
        IsRunning = false;
        try { _cts?.Cancel(); } catch { }
        try { _loop?.Wait(1000); } catch { }
        _loop = null;
        _cts?.Dispose();
        _cts = null;
        _capture?.Dispose();
        _capture = null;
        _ring.Clear();
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var samples = _ring.Read(320 * 5);
                if (samples.Length == 0) { await Task.Delay(100, ct).ConfigureAwait(false); continue; }
                var frames = _segmenter.Push(samples, DateTimeOffset.Now);
                foreach (var (chunk, from, to) in frames)
                {
                    if (_engine is null) continue;
                    var started = clock.Elapsed;
                    var (text, code) = await _engine.TranscribeAsync(chunk, ct).ConfigureAwait(false);
                    if (code.Length > 0) { LastCode = code; continue; }
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    LatencyMs = (clock.Elapsed - started).TotalMilliseconds + (to - from).TotalMilliseconds;
                    LastCode = "";
                    LastText = text;
                    await PublishAsync(text, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { LastCode = ex.GetType().Name; await Task.Delay(500, ct).ConfigureAwait(false); }
        }
    }

    private async Task PublishAsync(string text, CancellationToken ct)
    {
        _recent.Add(text);
        if (_recent.Count > 20) _recent.RemoveAt(0);
        var line = SubtitleCore.Tail(_recent);
        var translated = "";
        if (_translator is not null)
        {
            var (source, target, code) = await _translator.TranslateAsync(text).ConfigureAwait(false);
            if (code.Length == 0) translated = target;
        }
        var display = translated.Length > 0 ? TranslationCore.BilingualLine(text, translated) : line;
        if (_publisher is not null)
        {
            var (ok, code) = await _publisher.PublishAsync(display, ct).ConfigureAwait(false);
            if (!ok) LastCode = code;
        }
        CaptionReady?.Invoke(text, display);
    }

    public void Dispose() => Stop();
}
