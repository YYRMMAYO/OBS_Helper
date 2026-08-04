using System.Buffers;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using OBS_Helper.Client.Models.Obs;

namespace OBS_Helper.Client.Services.Obs;

/// <summary>
/// obs-websocket 5.x 低层客户端：只负责「连接 / 握手鉴权 / 请求-响应关联 / 事件分发」，
/// 不含任何业务语义。上层语义封装见 <see cref="ObsConnectionService"/>。
///
/// 运行环境说明：本类型运行在 Blazor WebAssembly 中，<see cref="ClientWebSocket"/> 由浏览器
/// 的原生 WebSocket 实现支撑。因此**不能**设置 Proxy / 请求头 / KeepAlive 等选项
/// （在 WASM 下会抛 PlatformNotSupportedException），这里刻意全部不碰。
///
/// 关于混合内容：页面以 https 虚拟主机（Windows）或 tauri 自定义协议（macOS）加载，
/// 但目标是 <c>ws://127.0.0.1</c>。按 W3C「potentially trustworthy origin」规范，
/// 回环地址属于可信来源，Chromium / WebKit 均不会将其判定为混合内容而拦截。
/// </summary>
public sealed class ObsWebSocketClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>单条请求的默认超时。OBS 本地响应通常 &lt;50ms，10s 足够覆盖极端卡顿。</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private ClientWebSocket? _socket;
    private CancellationTokenSource? _loopCts;
    private Task? _receiveLoop;

    private readonly ConcurrentDictionary<string, TaskCompletionSource<ObsRequestResult>> _pending = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private TaskCompletionSource<bool>? _identifyTcs;

    /// <summary>收到服务端事件时触发。</summary>
    public event Action<ObsEventMessage>? EventReceived;

    /// <summary>连接因任何原因断开时触发（含服务端主动关闭、网络错误）。</summary>
    public event Action<string>? Closed;

    public bool IsOpen => _socket?.State == WebSocketState.Open;

    /// <summary>握手协商后的 RPC 版本（v5 目前为 1）。</summary>
    public int NegotiatedRpcVersion { get; private set; }

    /// <summary>服务端是否要求密码。首次 Hello 后可读。</summary>
    public bool AuthRequired { get; private set; }

    /// <summary>
    /// 建立连接并完成 Identify 握手。
    /// </summary>
    /// <param name="url">形如 ws://127.0.0.1:4455</param>
    /// <param name="password">obs-websocket 密码；服务端未开启鉴权时可为空。</param>
    /// <param name="subscriptions">事件订阅位掩码。</param>
    public async Task ConnectAsync(string url, string? password, ObsEventSubscription subscriptions, CancellationToken ct = default)
    {
        await DisposeSocketAsync();

        var socket = new ClientWebSocket();
        _socket = socket;

        // 注意：不调用 AddSubProtocol。obs-websocket 在未指定子协议时默认使用 JSON，
        // 而 WASM 下的子协议协商更容易出现兼容问题。
        await socket.ConnectAsync(new Uri(url), ct);

        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _identifyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(socket, password, subscriptions, _loopCts.Token));

        // 等待 Hello → Identify → Identified 全流程完成
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, ct);
        var identified = _identifyTcs.Task;
        var completed = await Task.WhenAny(identified, Task.Delay(Timeout.Infinite, linked.Token));
        if (completed != identified)
        {
            throw new TimeoutException("OBS 握手超时：已建立 TCP 连接但未收到 Identified 响应。");
        }
        await identified; // 传播握手失败异常（如密码错误）
    }

    /// <summary>发送一条请求并等待响应。</summary>
    public async Task<ObsRequestResult> RequestAsync(string requestType, object? requestData = null, CancellationToken ct = default)
    {
        if (!IsOpen) return ObsRequestResult.Fail(0, "未连接到 OBS。");

        var requestId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<ObsRequestResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = tcs;

        var payload = new
        {
            op = ObsOpCode.Request,
            d = new
            {
                requestType,
                requestId,
                requestData
            }
        };

        try
        {
            await SendJsonAsync(payload, ct);
        }
        catch (Exception ex)
        {
            _pending.TryRemove(requestId, out _);
            return ObsRequestResult.Fail(0, "发送请求失败：" + ex.Message);
        }

        using var timeout = new CancellationTokenSource(RequestTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, ct);
        var done = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, linked.Token));
        if (done != tcs.Task)
        {
            _pending.TryRemove(requestId, out _);
            return ObsRequestResult.Fail(0, $"请求 {requestType} 超时（>{RequestTimeout.TotalSeconds:0}s）。");
        }
        return await tcs.Task;
    }

    public async Task CloseAsync()
    {
        try
        {
            if (_socket is { State: WebSocketState.Open })
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "client closing", CancellationToken.None);
            }
        }
        catch (Exception)
        {
            // 关闭握手失败无需上报：后续 DisposeSocketAsync 会强制释放。
        }
        await DisposeSocketAsync();
    }

    // -----------------------------------------------------------------------

    private async Task ReceiveLoopAsync(ClientWebSocket socket, string? password, ObsEventSubscription subs, CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        var message = new MemoryStream();
        string closeReason = "连接已断开。";

        try
        {
            while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                message.SetLength(0);
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        closeReason = $"OBS 关闭了连接（{result.CloseStatus}）：{result.CloseStatusDescription}";
                        goto finished;
                    }
                    message.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                message.Position = 0;
                await HandleMessageAsync(message, password, subs, ct);
            }
        }
        catch (OperationCanceledException)
        {
            closeReason = "连接已主动关闭。";
        }
        catch (Exception ex)
        {
            closeReason = "连接异常：" + ex.Message;
            _identifyTcs?.TrySetException(new InvalidOperationException(closeReason));
        }

    finished:
        ArrayPool<byte>.Shared.Return(buffer);

        // 唤醒所有仍在等待的请求，避免调用方永久挂起
        foreach (var kv in _pending)
        {
            if (_pending.TryRemove(kv.Key, out var tcs))
                tcs.TrySetResult(ObsRequestResult.Fail(0, closeReason));
        }
        _identifyTcs?.TrySetException(new InvalidOperationException(closeReason));
        Closed?.Invoke(closeReason);
    }

    private async Task HandleMessageAsync(Stream json, string? password, ObsEventSubscription subs, CancellationToken ct)
    {
        using var doc = await JsonDocument.ParseAsync(json, cancellationToken: ct);
        var root = doc.RootElement;
        if (!root.TryGetProperty("op", out var opEl)) return;
        var op = opEl.GetInt32();
        if (!root.TryGetProperty("d", out var d)) return;

        switch (op)
        {
            case ObsOpCode.Hello:
                await HandleHelloAsync(d, password, subs, ct);
                break;

            case ObsOpCode.Identified:
                NegotiatedRpcVersion = d.TryGetProperty("negotiatedRpcVersion", out var rpc) ? rpc.GetInt32() : 1;
                _identifyTcs?.TrySetResult(true);
                break;

            case ObsOpCode.Event:
                {
                    var evt = new ObsEventMessage
                    {
                        EventType = d.TryGetProperty("eventType", out var et) ? et.GetString() ?? "" : "",
                        Data = d.TryGetProperty("eventData", out var ed) ? ed.Clone() : default
                    };
                    EventReceived?.Invoke(evt);
                    break;
                }

            case ObsOpCode.RequestResponse:
                {
                    var id = d.TryGetProperty("requestId", out var ri) ? ri.GetString() : null;
                    if (id is null || !_pending.TryRemove(id, out var tcs)) break;

                    var ok = false;
                    var code = 0;
                    string? comment = null;
                    if (d.TryGetProperty("requestStatus", out var status))
                    {
                        ok = status.TryGetProperty("result", out var r) && r.GetBoolean();
                        code = status.TryGetProperty("code", out var c) ? c.GetInt32() : 0;
                        comment = status.TryGetProperty("comment", out var cm) ? cm.GetString() : null;
                    }
                    JsonElement? data = d.TryGetProperty("responseData", out var rd) ? rd.Clone() : null;
                    tcs.TrySetResult(new ObsRequestResult { Ok = ok, Code = code, Comment = comment, Data = data });
                    break;
                }
        }
    }

    private async Task HandleHelloAsync(JsonElement d, string? password, ObsEventSubscription subs, CancellationToken ct)
    {
        string? authResponse = null;

        if (d.TryGetProperty("authentication", out var auth) && auth.ValueKind == JsonValueKind.Object)
        {
            AuthRequired = true;
            var salt = auth.TryGetProperty("salt", out var s) ? s.GetString() ?? "" : "";
            var challenge = auth.TryGetProperty("challenge", out var c) ? c.GetString() ?? "" : "";

            if (string.IsNullOrEmpty(password))
            {
                _identifyTcs?.TrySetException(new UnauthorizedAccessException(
                    "OBS 已开启 WebSocket 密码验证，但未提供密码。请在「设置 → 连接」中填写 OBS「工具 → obs-websocket 设置」里显示的密码。"));
                return;
            }
            authResponse = ObsAuth.BuildAuthResponse(password, salt, challenge);
        }
        else
        {
            AuthRequired = false;
        }

        var identify = new
        {
            op = ObsOpCode.Identify,
            d = new
            {
                rpcVersion = 1,
                authentication = authResponse,
                eventSubscriptions = (int)subs
            }
        };
        await SendJsonAsync(identify, ct);
    }

    private async Task SendJsonAsync(object payload, CancellationToken ct)
    {
        var socket = _socket;
        if (socket is null || socket.State != WebSocketState.Open)
            throw new InvalidOperationException("WebSocket 未处于打开状态。");

        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOpts);
        await _sendLock.WaitAsync(ct);
        try
        {
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task DisposeSocketAsync()
    {
        try { _loopCts?.Cancel(); } catch (Exception) { /* 已释放，忽略 */ }

        if (_receiveLoop is not null)
        {
            try { await _receiveLoop.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (Exception) { /* 接收循环退出超时：不阻塞重连 */ }
            _receiveLoop = null;
        }

        _socket?.Dispose();
        _socket = null;
        _loopCts?.Dispose();
        _loopCts = null;
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        _sendLock.Dispose();
    }

    /// <summary>把 UTF-8 文本安全解码为字符串（诊断日志用）。</summary>
    internal static string Utf8(ReadOnlySpan<byte> b) => Encoding.UTF8.GetString(b);
}
