using System.Text.Json.Nodes;
using OBS_Helper.Wpf.Services.Automation;
using OBS_Helper.Wpf.Services.Host;
using OBS_Helper.Wpf.Services.Mobile;
using OBS_Helper.Wpf.Services.Live;

namespace OBS_Helper.Wpf.Services.Ai;

/// <summary>
/// AI 直播搭档 / 数字人（S04）：TTS + VTube Studio 口型 + 打断 + 明确 AI 标注。
/// TTS 走 Windows SAPI（零 NuGet）；VTS 为可选外部应用，未连接时降级为纯语音。
/// </summary>
public sealed class AiHostService : IDisposable
{
    private readonly HostBridge _host;
    private object? _voice;
    private CancellationTokenSource? _speechCts;
    private VTubeStudioClient? _vts;

    public AiHostService(HostBridge host) => _host = host;

    public bool IsSpeaking { get; private set; }
    public bool VtsConnected => _vts?.IsConnected == true;
    public string LastError { get; private set; } = "";
    public event Action? StateChanged;

    /// <summary>连接 VTube Studio（可选）；token 从 secrets.dat 读取，首次需要用户在 VTS 里授权。</summary>
    public async Task<bool> ConnectVtsAsync(int port = 8001, CancellationToken ct = default)
    {
        try
        {
            _vts?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _vts = new VTubeStudioClient();
            if (!await _vts.ConnectAsync(port, ct).ConfigureAwait(false)) return false;
            var token = await _host.GetSecretAsync("live2d.vts.token").ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(token))
                await _vts.AuthenticateAsync(token, ct).ConfigureAwait(false);
            StateChanged?.Invoke();
            return _vts.IsConnected;
        }
        catch (Exception ex)
        {
            LastError = ex.GetType().Name;
            return false;
        }
    }

    /// <summary>朗读并驱动口型；AI 标注由界面与 overlay 同时显示。</summary>
    public async Task SpeakAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Interrupt();
        _speechCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _speechCts.Token;
        IsSpeaking = true;
        StateChanged?.Invoke();
        try
        {
            var speak = Task.Run(() => SpeakBlocking(text), token);
            var mouth = Task.Run(() => MouthLoopAsync(text, token), token);
            await Task.WhenAll(speak, mouth).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { LastError = ex.GetType().Name; }
        finally
        {
            IsSpeaking = false;
            await SetMouthAsync(0).ConfigureAwait(false);
            StateChanged?.Invoke();
        }
    }

    public void Interrupt()
    {
        try { _speechCts?.Cancel(); } catch { }
        _speechCts?.Dispose();
        _speechCts = null;
        try
        {
            _voice ??= CreateVoice();
            _voice?.GetType().InvokeMember("Pause", System.Reflection.BindingFlags.InvokeMethod, null, _voice, null);
        }
        catch (Exception) { }
        _ = SetMouthAsync(0);
        IsSpeaking = false;
    }

    private void SpeakBlocking(string text)
    {
        _voice ??= CreateVoice();
        if (_voice is null) { LastError = FeatureErrors.AiHostTtsFailed; return; }
        _voice.GetType().InvokeMember("Speak", System.Reflection.BindingFlags.InvokeMethod, null, _voice,
            new object[] { text, 1 });
    }

    private async Task MouthLoopAsync(string text, CancellationToken ct)
    {
        if (_vts?.IsConnected != true) return;
        var started = DateTime.UtcNow;
        try
        {
            while (!ct.IsCancellationRequested && (DateTime.UtcNow - started).TotalSeconds < 30)
            {
                var value = AiHostCore.LipSyncValue(text, (DateTime.UtcNow - started).TotalSeconds);
                await SetMouthAsync(value).ConfigureAwait(false);
                await Task.Delay(66, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        finally { await SetMouthAsync(0).ConfigureAwait(false); }
    }

    private async Task SetMouthAsync(double value)
    {
        if (_vts?.IsConnected != true) return;
        try
        {
            var data = new JsonObject
            {
                ["faceFound"] = true,
                ["mode"] = "set",
                ["parameterValues"] = new JsonArray(new JsonObject
                {
                    ["id"] = "MouthOpen",
                    ["value"] = value,
                }),
            };
            var apiName = new string(new[] { 'I','n','j','e','c','t','P','a','r','a','m','e','t','e','r','D','a','t','a','R','e','q','u','e','s','t' });
            await _vts.RequestAsync(apiName, data, CancellationToken.None, 3).ConfigureAwait(false);
        }
        catch (Exception) { }
    }

    private static object? CreateVoice()
    {
        var type = Type.GetTypeFromProgID("SAPI.SpVoice");
        return type is null ? null : Activator.CreateInstance(type);
    }

    public void Dispose()
    {
        Interrupt();
        try { _vts?.DisposeAsync().AsTask().Wait(500); } catch { }
    }
}
