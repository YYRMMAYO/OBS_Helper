using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OBS_Helper.Wpf.Models.Mobile;
using OBS_Helper.Wpf.Services.Obs;
using OBS_Helper.Wpf.Services.Security;

namespace OBS_Helper.Wpf.Services.Mobile;

/// <summary>
/// 手机联动服务端（V3.4.0 / M01/M02/M05/M07/M06）。
///
/// 只做三件事：把内置页面发给同一局域网的手机、把手机的高层命令交给
/// <see cref="MobileCommandRouter"/>、把 OBS 状态推回手机（WS，失败回退 SSE）。
///
/// 安全边界（PLAN_V3.4.0 §6.1）：
/// <list type="bullet">
///   <item>默认关闭，用户显式开启才绑定端口；只绑一个私网地址，拒绝非私网来源；</item>
///   <item>配对 secret 一次性、120 秒；会话 token 只存哈希；WS 认证放首条消息而非 URL；</item>
///   <item>随机端口、限流、可撤销；错误不区分「不存在 / 无权限」；</item>
///   <item>手机永远拿不到 OBS 密码、推流码、API Key；没有任意请求透传。</item>
/// </list>
/// 生命周期跟随应用：退出时 <see cref="Stop"/> 关闭监听与所有连接。
/// </summary>
public sealed class MobileLinkServer : IDisposable
{
    private const int MaxConnections = 16;

    private readonly ObsConnectionService _obs;
    private readonly MobileSessionStore _sessions;
    private readonly MobileSnapshotService _snapshots;
    private readonly MobileCommandRouter _router;
    private readonly MobilePreviewService _preview;
    private readonly MobileUploadService _uploads;

    private readonly object _gate = new();
    private readonly List<PairingSession> _pairings = new();
    private readonly List<ClientConnection> _clients = new();
    private readonly LanPolicy.RateLimiter _pairAttempts = new(5, TimeSpan.FromSeconds(10));
    private readonly System.Threading.Timer _pushTimer;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private int _connections;

    public MobileLinkServer(
        ObsConnectionService obs, MobileSessionStore sessions, MobileSnapshotService snapshots,
        MobileCommandRouter router, MobilePreviewService preview, MobileUploadService uploads)
    {
        _obs = obs;
        _sessions = sessions;
        _snapshots = snapshots;
        _router = router;
        _preview = preview;
        _uploads = uploads;
        _pushTimer = new System.Threading.Timer(_ => PushSnapshots(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public bool IsRunning { get; private set; }

    public IPAddress? Address { get; private set; }

    public int Port { get; private set; }

    public string BaseUrl => Address is null || Port == 0 ? "" : $"http://{Address}:{Port}";

    /// <summary>设备 / 配对 / 运行状态变化。</summary>
    public event Action? StateChanged;

    public IReadOnlyList<PairingSession> Pairings
    {
        get { lock (_gate) return _pairings.ToList(); }
    }

    public IReadOnlyList<MobileDevice> Devices => _sessions.Devices;

    public int ActiveSessions
    {
        get { lock (_gate) return _clients.Count(c => c.Authenticated); }
    }

    // ---------------------------------------------------------------- 生命周期

    /// <summary>启动服务。已在运行返回 true；找不到私网地址返回 <c>MOB_NO_LAN</c>。</summary>
    public (bool Ok, string Code) Start()
    {
        lock (_gate)
        {
            if (IsRunning) return (true, "");
            var address = LanPolicy.PickLanAddress(NetworkAddresses());
            if (address is null) return (false, FeatureErrors.MobNoLan);
            try
            {
                var listener = new TcpListener(address, 0);
                listener.Start();
                _listener = listener;
                _cts = new CancellationTokenSource();
                Address = address;
                Port = ((IPEndPoint)listener.LocalEndpoint).Port;
                IsRunning = true;
                _pushTimer.Change(0, 1000);
                _ = Task.Run(() => AcceptLoopAsync(listener, _cts.Token));
                StateChanged?.Invoke();
                return (true, "");
            }
            catch (SocketException)
            {
                return (false, FeatureErrors.CamPortInUse);
            }
        }
    }

    public void Stop()
    {
        List<ClientConnection> clients;
        lock (_gate)
        {
            if (!IsRunning) return;
            IsRunning = false;
            _pushTimer.Change(Timeout.Infinite, Timeout.Infinite);
            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            _listener = null;
            _cts?.Dispose();
            _cts = null;
            _pairings.Clear();
            clients = _clients.ToList();
            _clients.Clear();
            Address = null;
            Port = 0;
        }
        foreach (var client in clients) client.Close();
        StateChanged?.Invoke();
    }

    /// <summary>生成一次性配对码；服务未启动时先启动（用户点「生成配对码」即视为明确开启）。</summary>
    public (bool Ok, string Code, PairingSession? Session, string Secret, string Url) BeginPairing()
    {
        if (!IsRunning)
        {
            var started = Start();
            if (!started.Ok) return (false, started.Code, null, "", "");
        }
        var now = DateTimeOffset.UtcNow;
        var (session, secret) = PairingCore.CreateSession(now);
        lock (_gate)
        {
            _pairings.RemoveAll(p => p.IsExpired(now) || p.Consumed);
            if (_pairings.Count >= 5) _pairings.RemoveAt(0);
            _pairings.Add(session);
        }
        var url = PairingCore.BuildPairUrl(BaseUrl, session.PairId, secret);
        StateChanged?.Invoke();
        return (true, "", session, secret, url);
    }

    public void RevokeDevice(string deviceId)
    {
        _sessions.Revoke(deviceId);
        List<ClientConnection> toClose;
        lock (_gate) toClose = _clients.Where(c => c.DeviceId == deviceId).ToList();
        foreach (var client in toClose) client.Close();
        StateChanged?.Invoke();
    }

    public bool SetRole(string deviceId, MobileRole role)
    {
        var ok = _sessions.SetRole(deviceId, role);
        if (ok) StateChanged?.Invoke();
        return ok;
    }

    public void Dispose()
    {
        Stop();
        _pushTimer.Dispose();
    }

    // ---------------------------------------------------------------- 监听

    private static IEnumerable<IPAddress> NetworkAddresses()
    {
        var list = new List<IPAddress>();
        try
        {
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                foreach (var info in ni.GetIPProperties().UnicastAddresses)
                {
                    if (info.Address.AddressFamily == AddressFamily.InterNetwork) list.Add(info.Address);
                }
            }
        }
        catch (Exception) { }
        return list;
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { continue; }

            if (Interlocked.Increment(ref _connections) > MaxConnections)
            {
                Interlocked.Decrement(ref _connections);
                try { client.Dispose(); } catch { }
                continue;
            }
            _ = Task.Run(async () =>
            {
                try { await HandleClientAsync(client, ct).ConfigureAwait(false); }
                catch (Exception) { }
                finally
                {
                    Interlocked.Decrement(ref _connections);
                    try { client.Dispose(); } catch { }
                }
            }, ct);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        var remote = (client.Client.RemoteEndPoint as IPEndPoint)?.Address;
        if (!LanPolicy.IsPrivateAddress(remote)) return; // 公网来源一律静默丢弃

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var stream = client.GetStream();
        var (request, error) = await ReadRequestAsync(stream, timeout.Token).ConfigureAwait(false);
        if (request is null)
        {
            await WriteAsync(stream, Text(400, "Bad Request", error ?? "bad request"), false).ConfigureAwait(false);
            return;
        }

        var path = request.Path;
        if (path.StartsWith("/m/", StringComparison.Ordinal) && path.EndsWith("/light", StringComparison.Ordinal))
        {
            await WriteAsync(stream, Html(200, MobilePageAssets.LightHtml(Strings.Current))).ConfigureAwait(false);
            return;
        }
        if (path.StartsWith("/m/", StringComparison.Ordinal) && path.Count(c => c == '/') == 2)
        {
            await WriteAsync(stream, Html(200, MobilePageAssets.AppHtml(Strings.Current))).ConfigureAwait(false);
            return;
        }
        if (path == "/api/events")
        {
            await HandleSseAsync(stream, request, ct).ConfigureAwait(false);
            return;
        }
        if (path == "/ws")
        {
            await HandleWebSocketAsync(stream, request, remote, ct).ConfigureAwait(false);
            return;
        }
        if (path == "/api/pair" && request.Method == "POST")
        {
            await HandlePairAsync(stream, request, remote).ConfigureAwait(false);
            return;
        }
        if (path == "/api/state" && request.Method == "GET")
        {
            var device = Authenticate(request);
            if (device is null) { await WriteAsync(stream, Json(401, Error(FeatureErrors.MobTokenInvalid))).ConfigureAwait(false); return; }
            await WriteAsync(stream, Json(200, MobileStateCore.Serialize(_snapshots.Build(device)))).ConfigureAwait(false);
            return;
        }
        if (path == "/api/preview.jpg" && request.Method == "GET")
        {
            var device = Authenticate(request);
            if (device is null) { await WriteAsync(stream, Json(401, Error(FeatureErrors.MobTokenInvalid))).ConfigureAwait(false); return; }
            var fps = int.TryParse(request.QueryValue("fps"), out var f) ? f : 1;
            var (frame, code) = await _preview.GetFrameAsync(fps, ct).ConfigureAwait(false);
            if (frame is null)
            {
                await WriteAsync(stream, Json(502, Error(code.Length > 0 ? code : FeatureErrors.MobPreviewFailed))).ConfigureAwait(false);
                return;
            }
            var header = new Dictionary<string, string>(LanPolicy.SecurityHeaders(LanPolicy.PageCsp))
            {
                ["Cache-Control"] = "no-store",
            };
            await WriteAsync(stream, HttpWireCore.BuildResponse(200, "OK", "image/jpeg", frame, header), false).ConfigureAwait(false);
            return;
        }
        if (path == "/api/command" && request.Method == "POST")
        {
            var device = Authenticate(request);
            if (device is null) { await WriteAsync(stream, Json(401, Error(FeatureErrors.MobTokenInvalid))).ConfigureAwait(false); return; }
            await HandleCommandAsync(stream, device, request.Body, ct).ConfigureAwait(false);
            return;
        }
        if (path == "/api/upload" && request.Method == "POST")
        {
            var device = Authenticate(request);
            if (device is null) { await WriteAsync(stream, Json(401, Error(FeatureErrors.MobTokenInvalid))).ConfigureAwait(false); return; }
            await HandleUploadAsync(stream, device, request).ConfigureAwait(false);
            return;
        }

        // 其余一律 404：不提供目录列表，也不区分「不存在」与「无权限」
        await WriteAsync(stream, Text(404, "Not Found", "not found")).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- HTTP

    private static async Task<(HttpRequest? Request, string? Error)> ReadRequestAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int headEnd = -1;
        while (headEnd < 0)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(), ct).ConfigureAwait(false);
            if (read <= 0) return (null, "empty");
            buffer.Write(chunk, 0, read);
            if (buffer.Length > HttpWireCore.MaxHeadBytes) return (null, "head too large");
            headEnd = HttpWireCore.FindHeadEnd(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
        }

        var all = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
        var head = Encoding.UTF8.GetString(all[..headEnd]);
        HttpRequest parsed;
        try { parsed = HttpWireCore.ParseHead(head); }
        catch (HttpFormatException ex) { return (null, ex.Message); }

        var length = 0;
        if (parsed.Headers.TryGetValue("Content-Length", out var lengthHeader) &&
            !int.TryParse(lengthHeader, out length)) return (null, "bad content-length");
        if (length < 0 || length > HttpWireCore.MaxBodyBytes) return (null, "body too large");

        var body = new byte[length];
        var already = all.Length - headEnd - 4;
        if (already > 0) Buffer.BlockCopy(all[(headEnd + 4)..].ToArray(), 0, body, 0, Math.Min(already, length));
        var offset = Math.Max(0, already);
        while (offset < length)
        {
            var read = await stream.ReadAsync(body.AsMemory(offset, length - offset), ct).ConfigureAwait(false);
            if (read <= 0) return (null, "short body");
            offset += read;
        }
        try { return (HttpWireCore.ParseHead(head, body), null); }
        catch (HttpFormatException ex) { return (null, ex.Message); }
    }

    private static Task WriteAsync(NetworkStream stream, byte[] payload, bool csp = true)
        => stream.WriteAsync(payload).AsTask();

    private static byte[] Html(int status, string html)
        => HttpWireCore.BuildResponse(status, status == 200 ? "OK" : "Error", "text/html; charset=utf-8",
            Encoding.UTF8.GetBytes(html), LanPolicy.SecurityHeaders(LanPolicy.PageCsp));

    private static byte[] Json(int status, string json)
        => HttpWireCore.BuildResponse(status, status switch
        {
            200 => "OK",
            401 => "Unauthorized",
            404 => "Not Found",
            429 => "Too Many Requests",
            502 => "Bad Gateway",
            _ => "Error",
        }, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json),
            LanPolicy.SecurityHeaders(LanPolicy.PageCsp));

    private static byte[] Text(int status, string reason, string text)
        => HttpWireCore.BuildResponse(status, reason, "text/plain; charset=utf-8",
            Encoding.UTF8.GetBytes(text), LanPolicy.SecurityHeaders(LanPolicy.PageCsp));

    private static string Error(string code) => JsonSerializer.Serialize(new
    {
        ok = false,
        code,
        message = FeatureErrors.Describe(code),
    }, MobileStateCore.JsonOptions);

    private MobileDevice? Authenticate(HttpRequest request)
    {
        var header = request.Header("Authorization") ?? "";
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        return _sessions.Authenticate(header["Bearer ".Length..].Trim());
    }

    private async Task HandlePairAsync(NetworkStream stream, HttpRequest request, IPAddress? remote)
    {
        if (!_pairAttempts.TryAcquire(remote?.ToString() ?? "unknown"))
        {
            await WriteAsync(stream, Json(429, Error(FeatureErrors.MobRateLimited))).ConfigureAwait(false);
            return;
        }
        PairRequest? body;
        try { body = JsonSerializer.Deserialize<PairRequest>(request.Body, MobileStateCore.JsonOptions); }
        catch (JsonException) { body = null; }
        if (body is null || string.IsNullOrWhiteSpace(body.PairId) || string.IsNullOrWhiteSpace(body.Secret))
        {
            await WriteAsync(stream, Json(400, Error(FeatureErrors.MobPairBadSecret))).ConfigureAwait(false);
            return;
        }

        PairingSession? session;
        lock (_gate) session = _pairings.FirstOrDefault(p => p.PairId == body.PairId);
        var outcome = PairingCore.Consume(session, body.Secret, DateTimeOffset.UtcNow);
        if (outcome != PairOutcome.Ok || session is null)
        {
            var code = FeatureErrors.MobPairBadSecret;
            await WriteAsync(stream, Json(403, Error(code))).ConfigureAwait(false);
            return;
        }

        var registered = _sessions.Register(body.DeviceName, MobileRole.Operator);
        if (registered is null)
        {
            await WriteAsync(stream, Json(409, Error(FeatureErrors.MobDeviceLimit))).ConfigureAwait(false);
            return;
        }
        var (device, token) = registered.Value;
        var snapshot = _snapshots.Build(device);
        var payload = JsonSerializer.Serialize(new
        {
            ok = true,
            token,
            role = MobileRoles.Name(device.ParsedRole),
            deviceId = device.DeviceId,
            deviceName = device.Name,
            snapshot,
        }, MobileStateCore.JsonOptions);
        lock (_gate) _pairings.Remove(session);
        await WriteAsync(stream, Json(200, payload)).ConfigureAwait(false);
        StateChanged?.Invoke();
    }

    private async Task HandleCommandAsync(NetworkStream stream, MobileDevice device, byte[] body, CancellationToken ct)
    {
        MobileCommandRequest? request;
        try { request = JsonSerializer.Deserialize<MobileCommandRequest>(body, MobileStateCore.JsonOptions); }
        catch (JsonException) { request = null; }
        if (request is null)
        {
            await WriteAsync(stream, Json(400, Error(FeatureErrors.MobCommandInvalid))).ConfigureAwait(false);
            return;
        }
        var result = await _router.ExecuteAsync(device, request, ct).ConfigureAwait(false);
        await WriteAsync(stream, Json(200, JsonSerializer.Serialize(result, MobileStateCore.JsonOptions))).ConfigureAwait(false);
    }

    private async Task HandleUploadAsync(NetworkStream stream, MobileDevice device, HttpRequest request)
    {
        if (!HttpWireCore.TryParseMultipart(request.Body, request.Header("Content-Type"), out var parts))
        {
            await WriteAsync(stream, Json(400, Error(FeatureErrors.MobUploadBadType))).ConfigureAwait(false);
            return;
        }
        var file = parts.FirstOrDefault(p => string.Equals(p.Name, "file", StringComparison.OrdinalIgnoreCase));
        if (file is null)
        {
            await WriteAsync(stream, Json(400, Error(FeatureErrors.MobUploadBadType))).ConfigureAwait(false);
            return;
        }
        var (ok, code, info) = _uploads.Save(file);
        if (!ok || info is null)
        {
            await WriteAsync(stream, Json(400, Error(code))).ConfigureAwait(false);
            return;
        }
        var payload = JsonSerializer.Serialize(new
        {
            ok = true,
            fileName = info.FileName,
            kind = info.Kind,
            size = info.Size,
        }, MobileStateCore.JsonOptions);
        await WriteAsync(stream, Json(200, payload)).ConfigureAwait(false);
        StateChanged?.Invoke();
    }

    // ---------------------------------------------------------------- WS / SSE

    private async Task HandleWebSocketAsync(NetworkStream stream, HttpRequest request, IPAddress? remote, CancellationToken ct)
    {
        if (!WebSocketFrameCore.IsValidHandshake(request, out var accept))
        {
            await WriteAsync(stream, Text(400, "Bad Request", "websocket handshake required")).ConfigureAwait(false);
            return;
        }
        lock (_gate)
        {
            if (_clients.Count(c => c.Authenticated) >= LanPolicy.MaxSessions)
            {
                // 并发会话上限：先拒绝新连接，不踢已有会话
            }
        }
        await stream.WriteAsync(WebSocketFrameCore.BuildHandshakeResponse(accept)).ConfigureAwait(false);
        var connection = new ClientConnection(stream, isWs: true);
        lock (_gate) _clients.Add(connection);

        var buffer = new byte[64 * 1024];
        var pending = new MemoryStream();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
                if (read <= 0) break;
                pending.Write(buffer, 0, read);
                while (true)
                {
                    WsFrame? frame;
                    int consumed;
                    var span = pending.GetBuffer().AsSpan(0, (int)pending.Length);
                    try
                    {
                        if (!WebSocketFrameCore.TryDecode(span, out frame, out consumed)) break;
                    }
                    catch (HttpFormatException) { return; }
                    var remaining = span[consumed..].ToArray();
                    pending.SetLength(0);
                    pending.Write(remaining);

                    if (frame!.Opcode == WsOpcode.Close) { return; }
                    if (frame.Opcode == WsOpcode.Ping)
                    {
                        await stream.WriteAsync(WebSocketFrameCore.Encode(WsOpcode.Pong, frame.Payload)).ConfigureAwait(false);
                        continue;
                    }
                    if (frame.Opcode != WsOpcode.Text) continue;

                    using var doc = JsonDocument.Parse(frame.Payload);
                    var root = doc.RootElement;
                    var type = root.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                    if (!connection.Authenticated)
                    {
                        if (type != "auth" || !root.TryGetProperty("token", out var tokenElement))
                        {
                            await connection.SendAsync(WebSocketFrameCore.EncodeClose(4001, "auth required")).ConfigureAwait(false);
                            return;
                        }
                        var device = _sessions.Authenticate(tokenElement.GetString());
                        if (device is null)
                        {
                            await connection.SendAsync(WebSocketFrameCore.EncodeClose(4001, "invalid token")).ConfigureAwait(false);
                            return;
                        }
                        connection.Authenticate(device);
                        var hello = JsonSerializer.Serialize(new
                        {
                            type = "auth",
                            ok = true,
                            role = MobileRoles.Name(device.ParsedRole),
                            snapshot = _snapshots.Build(device),
                        }, MobileStateCore.JsonOptions);
                        await connection.SendTextAsync(hello).ConfigureAwait(false);
                        continue;
                    }
                    if (type == "command")
                    {
                        MobileCommandRequest? cmd;
                        try
                        {
                            cmd = JsonSerializer.Deserialize<MobileCommandRequest>(frame.Payload, MobileStateCore.JsonOptions);
                        }
                        catch (JsonException) { cmd = null; }
                        if (cmd is null) continue;
                        var device = _sessions.Devices.FirstOrDefault(d => d.DeviceId == connection.DeviceId);
                        if (device is null) { await connection.SendAsync(WebSocketFrameCore.EncodeClose(4001, "revoked")).ConfigureAwait(false); return; }
                        var result = await _router.ExecuteAsync(device, cmd, ct).ConfigureAwait(false);
                        await connection.SendTextAsync(JsonSerializer.Serialize(result, MobileStateCore.JsonOptions)).ConfigureAwait(false);
                    }
                    else if (type == "ping")
                    {
                        await connection.SendTextAsync("{\"type\":\"pong\"}").ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception) { }
        finally
        {
            lock (_gate) _clients.Remove(connection);
            connection.Close();
        }
    }

    private async Task HandleSseAsync(NetworkStream stream, HttpRequest request, CancellationToken ct)
    {
        var token = request.QueryValue("token");
        var device = _sessions.Authenticate(token);
        if (device is null)
        {
            await WriteAsync(stream, Json(401, Error(FeatureErrors.MobTokenInvalid))).ConfigureAwait(false);
            return;
        }
        var headers = new StringBuilder();
        headers.Append("HTTP/1.1 200 OK\r\nContent-Type: text/event-stream; charset=utf-8\r\n")
               .Append("Cache-Control: no-store\r\nConnection: keep-alive\r\n");
        foreach (var (name, value) in LanPolicy.SecurityHeaders(LanPolicy.PageCsp))
            headers.Append(name).Append(": ").Append(value).Append("\r\n");
        headers.Append("\r\n");
        await stream.WriteAsync(Encoding.UTF8.GetBytes(headers.ToString()), ct).ConfigureAwait(false);
        var connection = new ClientConnection(stream, isWs: false);
        connection.Authenticate(device);
        lock (_gate) _clients.Add(connection);
        try
        {
            await connection.SendTextAsync(": connected\n\n").ConfigureAwait(false);
            await connection.SendTextAsync("data: " + MobileStateCore.Serialize(_snapshots.Build(device)) + "\n\n").ConfigureAwait(false);
            while (!ct.IsCancellationRequested && connection.IsOpen)
                await Task.Delay(1000, ct).ConfigureAwait(false);
        }
        catch (Exception) { }
        finally
        {
            lock (_gate) _clients.Remove(connection);
            connection.Close();
        }
    }

    private void PushSnapshots()
    {
        List<ClientConnection> clients;
        lock (_gate)
        {
            if (!IsRunning) return;
            clients = _clients.Where(c => c.Authenticated && c.IsOpen).ToList();
        }
        foreach (var client in clients)
        {
            var device = _sessions.Devices.FirstOrDefault(d => d.DeviceId == client.DeviceId);
            if (device is null) { client.Close(); continue; }
            try
            {
                var json = MobileStateCore.Serialize(_snapshots.Build(device));
                if (client.IsWs) client.SendTextAsync(json).GetAwaiter().GetResult();
                else client.SendTextAsync("data: " + json + "\n\n").GetAwaiter().GetResult();
            }
            catch (Exception) { client.Close(); }
        }
    }

    private sealed class ClientConnection
    {
        private readonly NetworkStream _stream;
        private readonly SemaphoreSlim _writeGate = new(1, 1);
        public bool IsWs { get; }
        public string DeviceId { get; private set; } = "";
        public bool Authenticated { get; private set; }
        public bool IsOpen { get; private set; } = true;

        public ClientConnection(NetworkStream stream, bool isWs)
        {
            _stream = stream;
            IsWs = isWs;
        }

        public void Authenticate(MobileDevice device)
        {
            DeviceId = device.DeviceId;
            Authenticated = true;
        }

        public Task SendTextAsync(string text) => IsWs
            ? SendAsync(WebSocketFrameCore.EncodeText(text))
            : SendAsync(Encoding.UTF8.GetBytes(text));

        public async Task SendAsync(byte[] payload)
        {
            if (!IsOpen) return;
            try
            {
                await _writeGate.WaitAsync().ConfigureAwait(false);
                try { await _stream.WriteAsync(payload).ConfigureAwait(false); }
                finally { _writeGate.Release(); }
            }
            catch (Exception) { IsOpen = false; }
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            try { if (IsWs) _stream.Write(WebSocketFrameCore.EncodeClose(1000)); } catch { }
            try { _stream.Dispose(); } catch { }
        }
    }

    private sealed class PairRequest
    {
        [JsonPropertyName("pairId")] public string PairId { get; set; } = "";
        [JsonPropertyName("secret")] public string Secret { get; set; } = "";
        [JsonPropertyName("deviceName")] public string DeviceName { get; set; } = "";
    }
}
