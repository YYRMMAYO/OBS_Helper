using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using OBS_Helper.Wpf.Models.Live;

namespace OBS_Helper.Wpf.Services.Live;

/// <summary>
/// VTube Studio 本地 WebSocket 客户端（V3.3.0 / P0-3）。
///
/// 只使用 VTS 的**公开 API**：<c>ws://127.0.0.1:8001</c>，按协议发 JSON，不注入 DLL、
/// 不读进程内存、不改 VTS 配置。请求串行化（同一时刻只有一条在途），响应按 requestID 匹配，
/// 事件消息直接忽略 —— 这样 VTS 的鼠标 / 热键事件不会把请求-响应配对打乱。
///
/// 超时策略：普通请求 12 秒；鉴权 60 秒（首次需要用户在 VTS 窗口里点「允许」）。
/// 超时后主动断开，避免半死连接把后续步骤拖成连环超时。
/// </summary>
public sealed class VTubeStudioClient : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ClientWebSocket? _ws;
    private int _counter;
    private readonly string _pluginName = "OBS Help Assistant";
    private readonly string _developer = "OBS Helper";

    /// <summary>最近一次失败的错误码（<c>L2D_VTS_*</c>）。</summary>
    public string LastErrorCode { get; private set; } = "";

    /// <summary>连接状态（VTS 关闭 / 端口不对时为 false）。</summary>
    public bool IsConnected => _ws?.State == WebSocketState.Open;

    /// <summary>当前连接端口（连接失败时仍记录尝试的端口，供日志）。</summary>
    public int Port { get; private set; } = VtsProtocolCore.DefaultPort;

    /// <summary>连接到本地 VTS。失败不抛异常，返回 false 并设置 <see cref="LastErrorCode"/>。</summary>
    public async Task<bool> ConnectAsync(int port, CancellationToken ct)
    {
        Port = port is > 0 and < 65536 ? port : VtsProtocolCore.DefaultPort;
        await CloseSocketAsync().ConfigureAwait(false);

        var ws = new ClientWebSocket();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{Port}"), timeout.Token).ConfigureAwait(false);
            _ws = ws;
            LastErrorCode = "";
            return true;
        }
        catch (Exception ex)
        {
            LastErrorCode = "L2D_VTS_CONNECT_FAILED";
            FileLogger.Warn("Live2D", $"连接 VTube Studio 失败（端口 {Port}）：{ex.Message}");
            try { ws.Dispose(); } catch { /* 忽略 */ }
            return false;
        }
    }

    /// <summary>发一条请求并等待匹配的响应；返回 null 表示失败（错误码在 <see cref="LastErrorCode"/>）。</summary>
    public async Task<JsonObject?> RequestAsync(string apiName, JsonObject? data, CancellationToken ct, int timeoutSeconds = 12)
    {
        if (!IsConnected)
        {
            LastErrorCode = "L2D_VTS_NOT_RUNNING";
            return null;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var requestId = $"nobs-{Interlocked.Increment(ref _counter)}";
            var payload = VtsProtocolCore.BuildRequest(apiName, requestId, data);
            var bytes = Encoding.UTF8.GetBytes(payload.ToJsonString());

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            try
            {
                await _ws!.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, timeout.Token)
                    .ConfigureAwait(false);

                while (!timeout.IsCancellationRequested)
                {
                    var text = await ReceiveTextAsync(timeout.Token).ConfigureAwait(false);
                    if (text is null) break;

                    var response = VtsProtocolCore.ParseResponse(text);
                    if (response is null) continue;

                    var rid = response["requestID"]?.GetValue<string>() ?? "";
                    if (!string.Equals(rid, requestId, StringComparison.Ordinal)) continue;

                    LastErrorCode = "";
                    return response;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                LastErrorCode = "L2D_VTS_TIMEOUT";
                FileLogger.Warn("Live2D", $"VTS 请求超时：{apiName}（{timeoutSeconds}s）");
                // 半死连接：主动断开，下一次调用重新连接
                await CloseSocketAsync().ConfigureAwait(false);
                return null;
            }
            catch (Exception ex)
            {
                LastErrorCode = "L2D_VTS_REQUEST_FAILED";
                FileLogger.Warn("Live2D", $"VTS 请求失败：{apiName}（{ex.Message}）");
                await CloseSocketAsync().ConfigureAwait(false);
                return null;
            }

            LastErrorCode = "L2D_VTS_REQUEST_FAILED";
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 鉴权：有 token 先试 <c>AuthenticationRequest</c>；失败或没有 token 时发
    /// <c>AuthenticationTokenRequest</c>（VTS 内弹窗）。返回最新 token（调用方负责存进 secrets.dat）。
    /// </summary>
    public async Task<(bool Ok, string? Token, string ErrorCode)> AuthenticateAsync(string? knownToken, CancellationToken ct)
    {
        if (!IsConnected)
        {
            LastErrorCode = "L2D_VTS_NOT_RUNNING";
            return (false, null, LastErrorCode);
        }

        if (!string.IsNullOrWhiteSpace(knownToken))
        {
            var auth = await RequestAsync("AuthenticationRequest", new JsonObject
            {
                ["authenticationToken"] = knownToken,
                ["pluginName"] = _pluginName,
                ["pluginDeveloper"] = _developer
            }, ct, timeoutSeconds: 20).ConfigureAwait(false);

            if (VtsProtocolCore.IsAuthSuccess(auth))
                return (true, knownToken, "");

            // token 失效（VTS 重装 / 撤销授权）：继续走首次授权，不要直接判死
            if (auth is not null && VtsProtocolCore.IsError(auth))
                FileLogger.Warn("Live2D", $"VTS 旧 token 未通过，改为重新申请授权：{VtsProtocolCore.MapError(auth)}");
        }

        var resp = await RequestAsync("AuthenticationTokenRequest", new JsonObject
        {
            ["pluginName"] = _pluginName,
            ["pluginDeveloper"] = _developer
        }, ct, timeoutSeconds: 60).ConfigureAwait(false);

        if (resp is null)
            return (false, null, LastErrorCode.Length > 0 ? LastErrorCode : "L2D_VTS_API_OFF");

        if (VtsProtocolCore.IsError(resp))
        {
            var code = VtsProtocolCore.MapError(resp);
            LastErrorCode = code;
            return (false, null, code);
        }

        var token = VtsProtocolCore.ExtractToken(resp);
        if (token is null)
        {
            LastErrorCode = "L2D_VTS_AUTH_DENIED";
            return (false, null, LastErrorCode);
        }

        return (true, token, "");
    }

    /// <summary>读取模型列表。</summary>
    public async Task<IReadOnlyList<VtsModel>> GetModelsAsync(CancellationToken ct)
    {
        var resp = await RequestAsync("ModelsRequest", null, ct).ConfigureAwait(false);
        return VtsProtocolCore.ParseModels(resp);
    }

    /// <summary>加载指定 modelID 的模型。</summary>
    public async Task<bool> LoadModelAsync(string modelId, CancellationToken ct)
    {
        var resp = await RequestAsync("ModelLoadRequest", new JsonObject { ["modelID"] = modelId }, ct)
            .ConfigureAwait(false);
        if (resp is null) return false;
        if (VtsProtocolCore.IsError(resp))
        {
            LastErrorCode = VtsProtocolCore.MapError(resp);
            return false;
        }
        return true;
    }

    /// <summary>读取 VTS 的模型目录名（告诉用户「把模型放到哪里」）。</summary>
    public async Task<string?> GetModelsFolderAsync(CancellationToken ct)
    {
        var resp = await RequestAsync("VTSFolderInfoRequest", null, ct).ConfigureAwait(false);
        return VtsProtocolCore.ParseModelsFolder(resp);
    }

    private async Task<string?> ReceiveTextAsync(CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        using var ms = new MemoryStream();
        while (true)
        {
            var result = await _ws!.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buffer, 0, result.Count);
            if (result.EndOfMessage) break;
            if (ms.Length > 4 * 1024 * 1024) return null; // 防御异常大消息
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private async Task CloseSocketAsync()
    {
        var ws = _ws;
        _ws = null;
        if (ws is null) return;
        try
        {
            if (ws.State == WebSocketState.Open)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", cts.Token).ConfigureAwait(false);
            }
        }
        catch { /* 关闭失败无妨 */ }
        finally
        {
            try { ws.Dispose(); } catch { /* 忽略 */ }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseSocketAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
