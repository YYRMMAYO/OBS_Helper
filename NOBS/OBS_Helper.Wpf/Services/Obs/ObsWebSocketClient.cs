using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OBS_Helper.Wpf.Models.Obs;

namespace OBS_Helper.Wpf.Services.Obs;

/// <summary>
/// obs-websocket 低层客户端：只负责「连接 / 握手鉴权 / 请求-响应关联 / 事件分发」，
/// 不含任何业务语义。上层语义封装见 <see cref="ObsConnectionService"/>。
///
/// <b>两代协议</b>（V3.0）：
/// <list type="bullet">
///   <item><b>5.x</b>（OBS 28+ 内置，默认端口 4455）：连上后服务端先推 Hello，再走 Identify/Authenticate；</item>
///   <item><b>4.x</b>（Win7 上 OBS 27 + obs-websocket 4.9 插件，默认端口 4444）：服务端<b>不主动说话</b>，
///         必须由客户端先发 <c>GetAuthRequired</c>，且消息里没有 <c>op</c> 字段。</item>
/// </list>
/// 旧协议只在 Win7 兼容构建里启用（见 <see cref="ObsLegacyV4Core.LegacyEnabled"/>）；
/// 翻译与归一化都在 <see cref="ObsLegacyV4Core"/> 里完成，因此本类之上的一层完全不知道连的是哪一代。
///
/// 运行环境说明：本类型运行在原生 .NET（WPF 桌面进程）中，<see cref="ClientWebSocket"/> 走
/// 完整的 System.Net.WebSockets 实现，Proxy / KeepAlive / 请求头等选项均可用。
/// 目标地址通常是回环 <c>ws://127.0.0.1:4455</c>，因此显式关闭系统代理，
/// 避免用户机器上的全局代理（PAC / 科学上网工具）把本地连接劫持到代理服务器上导致连不上。
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
    private TaskCompletionSource<bool>? _helloTcs;
    private string? _legacyPassword;

    /// <summary>
    /// 判定旧协议时愿意等「v5 的 Hello」多久。v5 服务端连上即推 Hello（毫秒级），
    /// 因此这 0.9 秒只是给「对面其实是 v4」留出的判定窗口。
    /// </summary>
    private const int LegacyProbeMs = 900;

    /// <summary>收到服务端事件时触发。</summary>
    public event Action<ObsEventMessage>? EventReceived;

    /// <summary>连接因任何原因断开时触发（含服务端主动关闭、网络错误）。</summary>
    public event Action<string>? Closed;

    public bool IsOpen => _socket?.State == WebSocketState.Open;

    /// <summary>握手协商后的 RPC 版本（v5 目前为 1；旧协议为 4）。</summary>
    public int NegotiatedRpcVersion { get; private set; }

    /// <summary>当前连接使用的协议代次（<see cref="ObsLegacyV4Core.ProtocolV5"/> / <see cref="ObsLegacyV4Core.ProtocolV4"/>）。</summary>
    public string Protocol { get; private set; } = ObsLegacyV4Core.ProtocolV5;

    /// <summary>当前连接是否走旧协议（obs-websocket 4.x）。</summary>
    public bool IsLegacyProtocol => Protocol == ObsLegacyV4Core.ProtocolV4;

    /// <summary>服务端是否要求密码。首次 Hello（或旧协议的 GetAuthRequired）后可读。</summary>
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

        // 回环地址不该走系统代理：很多用户装了全局代理工具，默认会把 127.0.0.1 之外的流量兜住，
        // 少数配置错误的 PAC 甚至连回环也代理，直接置空最稳。
        socket.Options.Proxy = null;
        // OBS 空闲时不会主动发心跳，加一个 20s 的 ping 让半开连接能被及时发现。
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

        // 注意：不调用 AddSubProtocol。obs-websocket 在未指定子协议时默认使用 JSON。
        await socket.ConnectAsync(new Uri(url), ct);

        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _identifyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _helloTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _legacyPassword = password;
        Protocol = ObsLegacyV4Core.ProtocolV5;
        NegotiatedRpcVersion = 0;
        AuthRequired = false;
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(socket, password, subscriptions, _loopCts.Token));

        // 旧协议判定（仅 Win7 兼容构建）：v5 服务端连上就推 Hello，v4 服务端什么都不说。
        // 所以先给一个很短的窗口等 Hello；没等到就按 v4 主动发起 GetAuthRequired 握手。
        if (ObsLegacyV4Core.LegacyEnabled)
        {
            var helloSeen = await WaitForHelloAsync(ct).ConfigureAwait(false);
            if (!helloSeen)
            {
                Protocol = ObsLegacyV4Core.ProtocolV4;
                await StartLegacyHandshakeAsync(ct).ConfigureAwait(false);
            }
        }

        // 等待 Hello → Identify → Identified（v5）或 GetAuthRequired → Authenticate（v4）全流程完成
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, ct);
        var identified = _identifyTcs.Task;
        var completed = await Task.WhenAny(identified, Task.Delay(Timeout.Infinite, linked.Token));
        if (completed != identified)
        {
            if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
            throw new TimeoutException(Strings.T("obs.ws.handshakeTimeout"));
        }
        await identified; // 传播握手失败异常（如密码错误）
    }

    /// <summary>等 v5 的 Hello；返回 false 表示窗口内没有收到（对面可能是旧协议）。</summary>
    private async Task<bool> WaitForHelloAsync(CancellationToken ct)
    {
        var helloTcs = _helloTcs;
        if (helloTcs is null) return false;

        using var probe = CancellationTokenSource.CreateLinkedTokenSource(ct);
        probe.CancelAfter(LegacyProbeMs);
        var done = await Task.WhenAny(helloTcs.Task, Task.Delay(Timeout.Infinite, probe.Token)).ConfigureAwait(false);
        return done == helloTcs.Task && helloTcs.Task.IsCompletedSuccessfully;
    }

    /// <summary>
    /// 旧协议（obs-websocket 4.x）握手：<c>GetAuthRequired</c> →（需要时）<c>Authenticate</c>。
    ///
    /// 鉴权**算式与 v5 相同**（<c>base64(sha256(base64(sha256(password + salt)) + challenge))</c>），
    /// 区别只在下发通道与请求形状：v5 由服务端 Hello 带出 salt/challenge、用 Identify.authentication；
    /// v4 要客户端先问 <c>GetAuthRequired</c>、再用 <c>Authenticate.auth</c>。
    /// </summary>
    private async Task StartLegacyHandshakeAsync(CancellationToken ct)
    {
        var required = await SendLegacyRequestAsync("GetAuthRequired", null, ct).ConfigureAwait(false);
        if (required is null)
        {
            _identifyTcs?.TrySetException(new InvalidOperationException(Strings.T("obs.ws.closed")));
            return;
        }

        var needsAuth = required.Value.TryGetProperty("authRequired", out var ar)
                        && ar.ValueKind == JsonValueKind.True;
        AuthRequired = needsAuth;

        if (needsAuth)
        {
            var salt = required.Value.TryGetProperty("salt", out var s) ? s.GetString() ?? "" : "";
            var challenge = required.Value.TryGetProperty("challenge", out var c) ? c.GetString() ?? "" : "";

            if (string.IsNullOrEmpty(_legacyPassword))
            {
                _identifyTcs?.TrySetException(new UnauthorizedAccessException(Strings.T("obs.ws.needPassword")));
                return;
            }

            var auth = ObsLegacyV4Core.BuildAuthResponse(_legacyPassword, salt, challenge);
            var res = await SendLegacyRequestAsync("Authenticate",
                new Dictionary<string, object?> { ["auth"] = auth }, ct).ConfigureAwait(false);

            var ok = res is { } r && r.TryGetProperty("status", out var st)
                     && string.Equals(st.GetString(), "ok", StringComparison.OrdinalIgnoreCase);
            if (!ok)
            {
                _identifyTcs?.TrySetException(new UnauthorizedAccessException(Strings.T("obs.ws.needPassword")));
                return;
            }
        }

        NegotiatedRpcVersion = 4;
        _identifyTcs?.TrySetResult(true);
    }

    /// <summary>发送一条请求并等待响应。</summary>
    public async Task<ObsRequestResult> RequestAsync(string requestType, object? requestData = null, CancellationToken ct = default)
    {
        if (!IsOpen) return ObsRequestResult.Fail(0, Strings.T("obs.ws.notConnected"));

        if (IsLegacyProtocol)
            return await LegacyRequestAsync(requestType, requestData, ct).ConfigureAwait(false);

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
            return ObsRequestResult.Fail(0, Strings.T("obs.ws.sendFailed", ex.Message));
        }

        return await AwaitResponseAsync(tcs, requestId, requestType, ct);
    }

    /// <summary>
    /// 用 obs-websocket 5.x 的 CallBatch（Request Batch）把多条只读请求合并成一次往返，
    /// 大幅降低连接后首次刷新 / 大量音频输入时的请求延迟。返回与入参同序的结果；
    /// 任一条子请求失败时仅该条 Ok=false，不抛异常（除非整体超时 / 断开）。
    /// </summary>
    public async Task<IReadOnlyList<ObsRequestResult>> CallBatchAsync(
        IReadOnlyList<ObsBatchRequest> requests,
        string executionType = "Parallel",
        bool haltOnFailure = false,
        CancellationToken ct = default)
    {
        if (requests.Count == 0) return Array.Empty<ObsRequestResult>();
        if (!IsOpen) return requests.Select(_ => ObsRequestResult.Fail(0, Strings.T("obs.ws.notConnected"))).ToArray();

        if (IsLegacyProtocol)
            return await LegacyBatchAsync(requests, haltOnFailure, ct).ConfigureAwait(false);

        var batchId = Guid.NewGuid().ToString("N");
        var items = requests.Select(r => new
        {
            requestType = r.RequestType,
            requestId = Guid.NewGuid().ToString("N"),
            requestData = r.RequestData
        }).ToList();

        // 子请求 id → 入参序号：并行执行时服务端可能乱序返回，这里回填到原顺序
        var order = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < items.Count; i++) order[items[i].requestId] = i;

        var tcs = new TaskCompletionSource<ObsRequestResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[batchId] = tcs;

        var payload = new
        {
            op = ObsOpCode.Request,
            d = new
            {
                requestType = "CallBatch",
                requestId = batchId,
                requestData = new
                {
                    haltOnFailure,
                    executionType,
                    requests = items
                }
            }
        };

        try
        {
            await SendJsonAsync(payload, ct);
        }
        catch (Exception ex)
        {
            _pending.TryRemove(batchId, out _);
            return requests.Select(_ => ObsRequestResult.Fail(0, Strings.T("obs.ws.batchSendFailed", ex.Message))).ToArray();
        }

        var batch = await AwaitResponseAsync(tcs, batchId, "CallBatch", ct);
        if (!batch.Ok)
            return requests.Select(_ => ObsRequestResult.Fail(batch.Code, batch.Comment ?? Strings.T("obs.ws.batchFailed"))).ToArray();

        var results = new ObsRequestResult[requests.Count];
        if (batch.Data is { } d && d.TryGetProperty("results", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in arr.EnumerateArray())
            {
                var rid = e.TryGetProperty("requestId", out var rp) ? rp.GetString() : null;
                if (rid is null || !order.TryGetValue(rid, out var idx)) continue;
                results[idx] = ParseRequestResponse(e);
            }
        }

        for (var i = 0; i < results.Length; i++)
            results[i] ??= ObsRequestResult.Fail(0, Strings.T("obs.ws.batchMissingResult"));

        return results;
    }

    // -----------------------------------------------------------------------
    // 旧协议（obs-websocket 4.x）适配：翻译在 ObsLegacyV4Core 里，这里只负责收发与归一化
    // -----------------------------------------------------------------------

    /// <summary>发一条 <b>v4 原生</b>请求（不经 v5 名映射），返回原始响应对象；失败返回 null。</summary>
    private async Task<JsonElement?> SendLegacyRequestAsync(string v4Type, Dictionary<string, object?>? data, CancellationToken ct)
    {
        if (!IsOpen) return null;

        var id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<ObsRequestResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        var payload = new Dictionary<string, object?> { ["request-type"] = v4Type, ["message-id"] = id };
        if (data is not null)
            foreach (var kv in data) payload[kv.Key] = kv.Value;

        try
        {
            await SendJsonAsync(payload, ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            _pending.TryRemove(id, out _);
            return null;
        }

        var r = await AwaitResponseAsync(tcs, id, v4Type, ct).ConfigureAwait(false);
        return r.Data;
    }

    /// <summary>把一条 v5 语义请求落到 v4 上，并把响应归一化回 v5 形状。</summary>
    private async Task<ObsRequestResult> LegacyRequestAsync(string requestType, object? requestData, CancellationToken ct)
    {
        // 「暂停」在 v4 里没有切换请求：先问当前是不是已暂停，再决定调 Pause 还是 Resume。
        //
        // 注意这里必须用**低层发送**（SendLegacyRequestAsync）而不是递归调用 LegacyRequestAsync：
        // PauseRecording / ResumeRecording 是 v4 原生请求名，再走一次 v5 名映射表会落空，
        // 变成「旧协议不支持该请求」（审查发现的缺陷）。
        if (requestType == "ToggleRecordPause")
        {
            var cur = await LegacyRequestAsync("GetRecordStatus", null, ct).ConfigureAwait(false);
            var paused = cur.Ok && cur.Data is { } cd
                         && cd.TryGetProperty("outputPaused", out var p) && p.ValueKind == JsonValueKind.True;

            var rawPause = await SendLegacyRequestAsync(paused ? "ResumeRecording" : "PauseRecording", null, ct)
                .ConfigureAwait(false);
            if (rawPause is null) return ObsRequestResult.Fail(0, Strings.T("obs.ws.notConnected"));

            var pauseOk = rawPause.Value.TryGetProperty("status", out var pauseStatus)
                          && string.Equals(pauseStatus.GetString(), "ok", StringComparison.OrdinalIgnoreCase);
            var pauseErr = rawPause.Value.TryGetProperty("error", out var pauseError) ? pauseError.GetString() : null;
            return ToLegacyResult(pauseOk, pauseErr, new JsonObject());
        }

        var dataEl = requestData is null ? (JsonElement?)null : JsonSerializer.SerializeToElement(requestData, JsonOpts);
        var mapped = ObsLegacyV4Core.MapRequest(requestType, dataEl);
        if (mapped is null)
            return ObsRequestResult.Fail(ObsRequestStatusCode.UnknownRequestType,
                Strings.T("obs.ws.legacyUnsupported", requestType));

        var raw = await SendLegacyRequestAsync(mapped.Type, mapped.Data, ct).ConfigureAwait(false);
        if (raw is null)
            return ObsRequestResult.Fail(0, Strings.T("obs.ws.notConnected"));

        var (ok, err, node) = NormalizeLegacyRaw(requestType, raw.Value);

        // v4 的 GetSceneItemList 不返回可见性：补一次批量查询（上层要拿它画来源开关）
        if (ok && requestType == "GetSceneItemList")
            await EnrichLegacySceneItemsAsync(node, ct).ConfigureAwait(false);

        return ToLegacyResult(ok, err, node);
    }

    /// <summary>把 v4 响应对象（status/error + 扁平字段）变成统一的 <see cref="ObsRequestResult"/>。</summary>
    private static (bool Ok, string? Error, JsonObject Node) NormalizeLegacyRaw(string v5Type, JsonElement raw)
    {
        var ok = raw.TryGetProperty("status", out var st)
                 && string.Equals(st.GetString(), "ok", StringComparison.OrdinalIgnoreCase);
        var err = raw.TryGetProperty("error", out var er) ? er.GetString() : null;
        return (ok, err, ObsLegacyV4Core.NormalizeResponse(v5Type, raw));
    }

    private static ObsRequestResult ToLegacyResult(bool ok, string? err, JsonObject node) => new()
    {
        Ok = ok,
        Code = ok ? ObsRequestStatusCode.Success : 500,
        Comment = err,
        Data = JsonSerializer.SerializeToElement(node)
    };

    private static ObsRequestResult NormalizeLegacyResult(string v5Type, JsonElement raw)
    {
        var (ok, err, node) = NormalizeLegacyRaw(v5Type, raw);
        return ToLegacyResult(ok, err, node);
    }

    /// <summary>
    /// 用 <c>ExecuteBatch</c> 批量补场景条目的可见性 / 锁定状态。
    ///
    /// 为什么必须补：v4 的 <c>GetSceneItemList</c> 只给 itemId / sourceName，而控制台页要靠
    /// <c>sceneItemEnabled</c> 画来源开关 —— 不补的话所有来源都会显示成「已开启」（与 OBS 实际不符）。
    /// </summary>
    private async Task EnrichLegacySceneItemsAsync(JsonObject normalized, CancellationToken ct)
    {
        if (normalized["sceneItems"] is not JsonArray arr || arr.Count == 0) return;

        var sceneName = normalized["sceneName"]?.GetValue<string>() ?? "";
        var requests = new JsonArray();
        foreach (var node in arr)
        {
            if (node is not JsonObject o) continue;
            var id = o["sceneItemId"]?.GetValue<long>() ?? 0;
            requests.Add(new JsonObject
            {
                ["request-type"] = "GetSceneItemProperties",
                ["message-id"] = id.ToString(CultureInfo.InvariantCulture),
                ["scene-name"] = sceneName,
                ["item"] = new JsonObject { ["id"] = id }
            });
        }
        if (requests.Count == 0) return;

        var resp = await SendLegacyRequestAsync("ExecuteBatch",
            new Dictionary<string, object?> { ["requests"] = requests }, ct).ConfigureAwait(false);
        if (resp is null || !resp.Value.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) return;

        foreach (var r in results.EnumerateArray())
        {
            var mid = r.TryGetProperty("message-id", out var m) ? m.GetString() : null;
            if (mid is null || !int.TryParse(mid, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) continue;

            foreach (var node in arr)
            {
                if (node is not JsonObject o || (o["sceneItemId"]?.GetValue<long>() ?? -1) != id) continue;
                if (r.TryGetProperty("visible", out var vis)
                    && (vis.ValueKind == JsonValueKind.True || vis.ValueKind == JsonValueKind.False))
                    o["sceneItemEnabled"] = vis.GetBoolean();
                if (r.TryGetProperty("locked", out var lk)
                    && (lk.ValueKind == JsonValueKind.True || lk.ValueKind == JsonValueKind.False))
                    o["sceneItemLocked"] = lk.GetBoolean();
                break;
            }
        }
    }

    /// <summary>旧协议的批量请求：v4 的 <c>ExecuteBatch</c>（串行执行）→ 逐条归一化成 v5 结果。</summary>
    private async Task<IReadOnlyList<ObsRequestResult>> LegacyBatchAsync(
        IReadOnlyList<ObsBatchRequest> requests, bool haltOnFailure, CancellationToken ct)
    {
        var results = new ObsRequestResult[requests.Count];
        var order = new Dictionary<string, int>(StringComparer.Ordinal);
        var batch = new JsonArray();

        for (var i = 0; i < requests.Count; i++)
        {
            var dataEl = requests[i].RequestData is null
                ? (JsonElement?)null
                : JsonSerializer.SerializeToElement(requests[i].RequestData, JsonOpts);
            var mapped = ObsLegacyV4Core.MapRequest(requests[i].RequestType, dataEl);
            if (mapped is null)
            {
                results[i] = ObsRequestResult.Fail(ObsRequestStatusCode.UnknownRequestType,
                    Strings.T("obs.ws.legacyUnsupported", requests[i].RequestType));
                continue;
            }

            var mid = i.ToString(CultureInfo.InvariantCulture);
            order[mid] = i;
            var o = new JsonObject { ["request-type"] = mapped.Type, ["message-id"] = mid };
            foreach (var kv in mapped.Data) o[kv.Key] = ToJsonNode(kv.Value);
            batch.Add(o);
        }

        if (batch.Count > 0)
        {
            var resp = await SendLegacyRequestAsync("ExecuteBatch", new Dictionary<string, object?>
            {
                ["requests"] = batch,
                ["abortOnFail"] = haltOnFailure
            }, ct).ConfigureAwait(false);

            if (resp is { } r && r.TryGetProperty("results", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in arr.EnumerateArray())
                {
                    var mid = e.TryGetProperty("message-id", out var m) ? m.GetString() : null;
                    if (mid is null || !order.TryGetValue(mid, out var idx)) continue;
                    results[idx] = NormalizeLegacyResult(requests[idx].RequestType, e);
                }
            }
        }

        for (var i = 0; i < results.Length; i++)
            results[i] ??= ObsRequestResult.Fail(0, Strings.T("obs.ws.batchMissingResult"));

        return results;
    }

    /// <summary>处理旧协议消息：v4 没有 <c>op</c> 字段，靠 <c>update-type</c> / <c>message-id</c> 区分事件与响应。</summary>
    private void HandleLegacyMessage(JsonElement root)
    {
        if (root.TryGetProperty("update-type", out _))
        {
            if (ObsLegacyV4Core.MapEvent(root) is { } mapped)
            {
                EventReceived?.Invoke(new ObsEventMessage
                {
                    EventType = mapped.Type,
                    Data = JsonSerializer.SerializeToElement(mapped.Data)
                });
            }
            return;
        }

        if (!root.TryGetProperty("message-id", out var mi)) return;
        var id = mi.GetString() ?? "";
        if (id.Length == 0 || !_pending.TryRemove(id, out var tcs)) return;

        var ok = root.TryGetProperty("status", out var st)
                 && string.Equals(st.GetString(), "ok", StringComparison.OrdinalIgnoreCase);
        var err = root.TryGetProperty("error", out var er) ? er.GetString() : null;
        tcs.TrySetResult(new ObsRequestResult
        {
            Ok = ok,
            Code = ok ? ObsRequestStatusCode.Success : 500,
            Comment = err,
            Data = root.Clone()
        });
    }

    /// <summary>
    /// 把映射表里的值（<c>object?</c> 装箱）转成 <see cref="JsonNode"/>。
    ///
    /// 为什么不能直接 <c>JsonValue.Create(obj)</c>：泛型推断会落到 <c>JsonValue.Create&lt;object&gt;</c>，
    /// 而 <c>JsonValue&lt;object&gt;</c> 在序列化时对装箱值并不安全（审查提出；这里按运行时类型分派，绕开整个问题）。
    /// </summary>
    private static JsonNode? ToJsonNode(object? value) => value switch
    {
        null => null,
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        int i => JsonValue.Create(i),
        long l => JsonValue.Create(l),
        double d => JsonValue.Create(d),
        float f => JsonValue.Create(f),
        // 嵌套对象（例如摊平后的 sceneItemTransform）需要递归转成 JsonObject
        Dictionary<string, object?> map => ToJsonObject(map),
        _ => JsonValue.Create(value.ToString()),
    };

    private static JsonObject ToJsonObject(Dictionary<string, object?> map)
    {
        var o = new JsonObject();
        foreach (var kv in map) o[kv.Key] = ToJsonNode(kv.Value);
        return o;
    }

    /// <summary>等待某请求的响应，统一处理超时与取消语义。</summary>
    private async Task<ObsRequestResult> AwaitResponseAsync(TaskCompletionSource<ObsRequestResult> tcs, string requestId, string requestType, CancellationToken ct)
    {
        using var timeout = new CancellationTokenSource(RequestTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, ct);
        var done = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, linked.Token));
        if (done != tcs.Task)
        {
            _pending.TryRemove(requestId, out _);
            // 调用方主动取消要如实抛 OperationCanceledException（重置/导入等上层按「已取消」处理），
            // 不能吞成「请求超时」——超时文案会误导用户以为是网络问题。
            if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
            return ObsRequestResult.Fail(0, Strings.T("obs.ws.requestTimeout", requestType, RequestTimeout.TotalSeconds));
        }
        return await tcs.Task;
    }

    /// <summary>从请求响应对象里解析出统一结果（单条请求与 CallBatch 子请求共用同一结构）。</summary>
    private static ObsRequestResult ParseRequestResponse(JsonElement d)
    {
        var ok = false;
        var code = 0;
        string? comment = null;
        if (d.TryGetProperty("requestStatus", out var status) && status.ValueKind == JsonValueKind.Object)
        {
            ok = status.TryGetProperty("result", out var r) && r.GetBoolean();
            code = status.TryGetProperty("code", out var c) ? c.GetInt32() : 0;
            comment = status.TryGetProperty("comment", out var cm) ? cm.GetString() : null;
        }
        JsonElement? data = d.TryGetProperty("responseData", out var rd) ? rd.Clone() : null;
        return new ObsRequestResult { Ok = ok, Code = code, Comment = comment, Data = data };
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
        string closeReason = Strings.T("obs.ws.closed");

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
                        closeReason = Strings.T("obs.ws.closedByObs", result.CloseStatus, result.CloseStatusDescription);
                        goto finished;
                    }
                    message.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                message.Position = 0;
                try
                {
                    await HandleMessageAsync(message, password, subs, ct);
                }
                catch (Exception)
                {
                    // 单条消息解析失败（畸形 JSON / 未知 op 等）不应断开整个连接：
                    // 跳过这条继续收下一条，避免被一条坏消息打掉连接触发重连。
                }
            }
        }
        catch (OperationCanceledException)
        {
            closeReason = Strings.T("obs.ws.closedLocally");
        }
        catch (Exception ex)
        {
            closeReason = Strings.T("obs.ws.error", ex.Message);
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

        // 仅当这个接收循环仍是「当前」连接时才广播 Closed：
        // 手动重连（DisposeSocketAsync → 新 ConnectAsync）会取消旧循环并换掉 _loopCts，
        // 旧循环的善后若照常广播，ObsConnectionService 会把它当成意外断开 → 刚连上又被拆掉重连。
        if (_loopCts is { } cts && cts.Token == ct)
            Closed?.Invoke(closeReason);
    }

    private async Task HandleMessageAsync(Stream json, string? password, ObsEventSubscription subs, CancellationToken ct)
    {
        using var doc = await JsonDocument.ParseAsync(json, cancellationToken: ct);
        var root = doc.RootElement;

        // 旧协议的消息没有 op 字段：事件靠 update-type，响应靠 message-id（见 ObsLegacyV4Core）
        if (!root.TryGetProperty("op", out var opEl))
        {
            HandleLegacyMessage(root);
            return;
        }

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
                    tcs.TrySetResult(ParseRequestResponse(d));
                    break;
                }
        }
    }

    private async Task HandleHelloAsync(JsonElement d, string? password, ObsEventSubscription subs, CancellationToken ct)
    {
        // 收到 Hello 说明对面是 v5：唤醒「协议判定」，让 ConnectAsync 不再走旧协议分支
        _helloTcs?.TrySetResult(true);

        string? authResponse = null;

        if (d.TryGetProperty("authentication", out var auth) && auth.ValueKind == JsonValueKind.Object)
        {
            AuthRequired = true;
            var salt = auth.TryGetProperty("salt", out var s) ? s.GetString() ?? "" : "";
            var challenge = auth.TryGetProperty("challenge", out var c) ? c.GetString() ?? "" : "";

            if (string.IsNullOrEmpty(password))
            {
                _identifyTcs?.TrySetException(new UnauthorizedAccessException(
                    Strings.T("obs.ws.needPassword")));
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
            throw new InvalidOperationException(Strings.T("obs.ws.notOpen"));

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
        // 先摘掉当前循环标记再取消：旧接收循环善后时会比对 _loopCts 是否还是自己，
        // 已被替换（手动重连拆旧连接）就不广播 Closed，避免触发上层的多余自动重连。
        var oldLoopCts = _loopCts;
        _loopCts = null;
        try { oldLoopCts?.Cancel(); } catch (Exception) { /* 已释放，忽略 */ }

        if (_receiveLoop is not null)
        {
            try { await _receiveLoop.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (Exception) { /* 接收循环退出超时：不阻塞重连 */ }
            _receiveLoop = null;
        }

        _socket?.Dispose();
        _socket = null;

        // V3.0 修复：这里原来写的是 `_loopCts?.Dispose()`，而上一行早已把 _loopCts 置空 ——
        // 等于每次重连都泄漏一个 CancellationTokenSource（还是 CreateLinkedTokenSource 出来的，
        // 父 token 上会留注册项）。要释放的是局部变量 oldLoopCts。
        try { oldLoopCts?.Dispose(); } catch (Exception) { /* 已释放，忽略 */ }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        _sendLock.Dispose();
    }

    /// <summary>把 UTF-8 文本安全解码为字符串（诊断日志用）。</summary>
    internal static string Utf8(ReadOnlySpan<byte> b) => Encoding.UTF8.GetString(b);
}
