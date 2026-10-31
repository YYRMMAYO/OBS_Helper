using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using OBS_Helper.Wpf.Models;
using OBS_Helper.Wpf.Services.Host;
using OBS_Helper.Wpf.Services.Security;

namespace OBS_Helper.Wpf.Services.Chat;

public enum ChatConnectionState { Disconnected, Connecting, Connected, Failed }

/// <summary>弹幕适配器统一接口（C01）：连接、断线、消息。适配器只读公开数据。</summary>
public interface IChatAdapter : IAsyncDisposable
{
    ChatPlatform Platform { get; }
    string Room { get; }
    ChatConnectionState State { get; }
    event Action<ChatMessage>? MessageReceived;
    event Action<ChatConnectionState>? StateChanged;
    /// <summary>平台公开的人气/观看数（拿不到的平台不触发）。</summary>
    event Action<int>? ViewerCountChanged;
    Task<bool> ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();
}

/// <summary>B 站公开弹幕连接：取 token → WS 认证 → 心跳 → 解压/解析；指数退避重连。</summary>
public sealed class BilibiliChatAdapter : IChatAdapter
{
    private readonly int _roomId;
    private readonly HttpClient _http;
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public BilibiliChatAdapter(int roomId, HttpClient? http = null)
    {
        _roomId = roomId;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    public ChatPlatform Platform => ChatPlatform.Bilibili;
    public string Room => _roomId.ToString();
    public ChatConnectionState State { get; private set; } = ChatConnectionState.Disconnected;

    public event Action<ChatMessage>? MessageReceived;
    public event Action<ChatConnectionState>? StateChanged;
    public event Action<int>? ViewerCountChanged;

    private void SetState(ChatConnectionState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }

    public Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        if (_loop is not null) return Task.FromResult(true);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loop = Task.Run(() => RunAsync(_cts.Token), CancellationToken.None);
        return Task.FromResult(true);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var delaySeconds = new[] { 1, 2, 5, 10, 30 };
        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            SetState(ChatConnectionState.Connecting);
            try
            {
                var (host, port, token) = await ResolveAsync(ct).ConfigureAwait(false);
                using var socket = new ClientWebSocket();
                _socket = socket;
                await socket.ConnectAsync(new Uri($"wss://{host}:{port}/sub"), ct).ConfigureAwait(false);
                await socket.SendAsync(BilibiliPacketCore.BuildAuthPacket(_roomId, token), WebSocketMessageType.Binary, true, ct).ConfigureAwait(false);
                SetState(ChatConnectionState.Connected);
                attempt = 0;
                using var heartbeat = new CancellationTokenSource();
                _ = Task.Run(async () =>
                {
                    while (!heartbeat.IsCancellationRequested && socket.State == WebSocketState.Open)
                    {
                        try
                        {
                            await socket.SendAsync(BilibiliPacketCore.BuildHeartbeatPacket(), WebSocketMessageType.Binary, true, heartbeat.Token).ConfigureAwait(false);
                            await Task.Delay(30000, heartbeat.Token).ConfigureAwait(false);
                        }
                        catch (Exception) { break; }
                    }
                }, CancellationToken.None);

                await ReceiveLoopAsync(socket, ct).ConfigureAwait(false);
                heartbeat.Cancel();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception) { SetState(ChatConnectionState.Failed); }

            if (ct.IsCancellationRequested) break;
            var wait = delaySeconds[Math.Min(attempt, delaySeconds.Length - 1)];
            attempt++;
            try { await Task.Delay(TimeSpan.FromSeconds(wait), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
        SetState(ChatConnectionState.Disconnected);
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        var pending = new MemoryStream();
        while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close) break;
            pending.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage) continue;
            var data = pending.ToArray();
            pending.SetLength(0);
            foreach (var packet in BilibiliPacketCore.SplitPackets(data) ?? new List<BilibiliPacketCore.Packet>())
            {
                var unpacked = BilibiliPacketCore.Decompress(packet);
                if (unpacked.Operation == BilibiliPacketCore.OpHeartbeatReply)
                {
                    var popularity = BilibiliPacketCore.ParsePopularity(unpacked.Body);
                    if (popularity > 0) ViewerCountChanged?.Invoke(popularity);
                    continue;
                }
                if (unpacked.Operation != BilibiliPacketCore.OpMessage) continue;
                if (unpacked.Version is 2 or 3)
                {
                    // 压缩包解压后仍然是完整的包头序列
                    foreach (var inner in BilibiliPacketCore.SplitPackets(unpacked.Body) ?? new List<BilibiliPacketCore.Packet>())
                    {
                        if (inner.Operation != BilibiliPacketCore.OpMessage) continue;
                        foreach (var message in BilibiliPacketCore.ParseMessages(Room, inner.Body)) MessageReceived?.Invoke(message);
                    }
                }
                else
                {
                    foreach (var message in BilibiliPacketCore.ParseMessages(Room, unpacked.Body)) MessageReceived?.Invoke(message);
                }
            }
        }
    }

    private async Task<(string Host, int Port, string? Token)> ResolveAsync(CancellationToken ct)
    {
        try
        {
            var json = await _http.GetStringAsync(BilibiliPacketCore.DanmuInfoUrl(_roomId), ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var data))
            {
                var token = data.TryGetProperty("token", out var t) ? t.GetString() : null;
                if (data.TryGetProperty("host_list", out var hosts) && hosts.ValueKind == JsonValueKind.Array && hosts.GetArrayLength() > 0)
                {
                    var first = hosts[0];
                    var host = first.TryGetProperty("host", out var h) ? h.GetString() ?? "" : "";
                    var port = first.TryGetProperty("wss_port", out var p) && p.TryGetInt32(out var pv) ? pv : 443;
                    if (host.Length > 0) return (host, port, token);
                }
            }
        }
        catch (Exception) { }
        return ("broadcastlv.chat.bilibili.com", 443, null);
    }

    public async Task DisconnectAsync()
    {
        try { _cts?.Cancel(); } catch { }
        try
        {
            if (_socket is { State: WebSocketState.Open })
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception) { }
        _socket?.Dispose();
        _socket = null;
        _loop = null;
        _cts?.Dispose();
        _cts = null;
        SetState(ChatConnectionState.Disconnected);
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync().ConfigureAwait(false);
}

/// <summary>Twitch 匿名 IRC over WebSocket：只读、不回消息、不带 OAuth。</summary>
public sealed class TwitchChatAdapter : IChatAdapter
{
    private readonly string _channel;
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public TwitchChatAdapter(string channel) => _channel = TwitchIrcCore.NormalizeChannel(channel);

    public ChatPlatform Platform => ChatPlatform.Twitch;
    public string Room => _channel;
    public ChatConnectionState State { get; private set; } = ChatConnectionState.Disconnected;

    public event Action<ChatMessage>? MessageReceived;
    public event Action<ChatConnectionState>? StateChanged;
    // Twitch 公开 IRC 不提供观看数：显式实现空事件，保持统一接口。
    public event Action<int>? ViewerCountChanged { add { } remove { } }

    private void SetState(ChatConnectionState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }

    public Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        if (_channel.Length == 0) return Task.FromResult(false);
        if (_loop is not null) return Task.FromResult(true);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loop = Task.Run(() => RunAsync(_cts.Token), CancellationToken.None);
        return Task.FromResult(true);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var delays = new[] { 1, 2, 5, 10, 30 };
        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            SetState(ChatConnectionState.Connecting);
            try
            {
                using var socket = new ClientWebSocket();
                _socket = socket;
                await socket.ConnectAsync(new Uri(TwitchIrcCore.ServerUrl), ct).ConfigureAwait(false);
                foreach (var command in TwitchIrcCore.JoinCommands(_channel))
                    await SendAsync(socket, command, ct).ConfigureAwait(false);
                SetState(ChatConnectionState.Connected);
                attempt = 0;
                await ReceiveLoopAsync(socket, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception) { SetState(ChatConnectionState.Failed); }
            if (ct.IsCancellationRequested) break;
            try { await Task.Delay(TimeSpan.FromSeconds(delays[Math.Min(attempt++, delays.Length - 1)]), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
        SetState(ChatConnectionState.Disconnected);
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        var pending = new StringBuilder();
        while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close) break;
            pending.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            var text = pending.ToString();
            var lines = text.Split('\n');
            pending.Clear();
            if (!text.EndsWith('\n')) pending.Append(lines[^1]);
            for (var i = 0; i < (text.EndsWith('\n') ? lines.Length : lines.Length - 1); i++)
            {
                var line = lines[i].TrimEnd('\r');
                var response = TwitchIrcCore.ResponseFor(line);
                if (response is not null)
                {
                    await SendAsync(socket, response, ct).ConfigureAwait(false);
                    continue;
                }
                var message = TwitchIrcCore.ParseLine(line, _channel);
                if (message is not null) MessageReceived?.Invoke(message);
            }
        }
    }

    private static Task SendAsync(ClientWebSocket socket, string text, CancellationToken ct)
        => socket.SendAsync(Encoding.UTF8.GetBytes(text + "\r\n"), WebSocketMessageType.Text, true, ct);

    public async Task DisconnectAsync()
    {
        try { _cts?.Cancel(); } catch { }
        try
        {
            if (_socket is { State: WebSocketState.Open })
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception) { }
        _socket?.Dispose();
        _socket = null;
        _loop = null;
        _cts?.Dispose();
        _cts = null;
        SetState(ChatConnectionState.Disconnected);
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync().ConfigureAwait(false);
}

/// <summary>弹幕聚合中心（C01）：归一化 → 去重 → 限流 → 审核 → 广播（UI / overlay / TTS）。</summary>
public sealed class ChatHub : IAsyncDisposable
{
    private readonly List<IChatAdapter> _adapters = new();
    private readonly ChatCore.Deduper _deduper = new();
    private readonly ChatModerationCore.Detector _moderation = new();
    private readonly Dictionary<ChatPlatform, ChatCore.RateWindow> _uiWindows = new();
    private readonly Dictionary<ChatPlatform, ChatCore.RateWindow> _overlayWindows = new();
    private readonly ChatTtsCore _tts = new();
    private readonly object _gate = new();

    public event Action<ChatMessage>? MessageAccepted;
    public event Action<ChatModerationCore.Alert>? AlertRaised;
    public event Action<ChatConnectionState>? StateChanged;
    public event Action<int>? ViewerCountChanged;
    public event Action<string>? TtsRequested;

    public bool Enabled { get; private set; } = true;
    public ChatTtsCore Tts => _tts;
    public IReadOnlyList<IChatAdapter> Adapters { get { lock (_gate) return _adapters.ToList(); } }
    public long UiDropped { get; private set; }
    public long OverlayDropped { get; private set; }

    public BilibiliChatAdapter? StartBilibili(int roomId)
    {
        var adapter = new BilibiliChatAdapter(roomId);
        Attach(adapter);
        _ = adapter.ConnectAsync();
        return adapter;
    }

    public TwitchChatAdapter? StartTwitch(string channel)
    {
        var adapter = new TwitchChatAdapter(channel);
        Attach(adapter);
        _ = adapter.ConnectAsync();
        return adapter;
    }

    private void Attach(IChatAdapter adapter)
    {
        adapter.MessageReceived += OnMessage;
        adapter.StateChanged += state => StateChanged?.Invoke(state);
        adapter.ViewerCountChanged += count => ViewerCountChanged?.Invoke(count);
        lock (_gate) _adapters.Add(adapter);
    }

    private void OnMessage(ChatMessage message)
    {
        if (!Enabled) return;
        if (_deduper.IsDuplicate(message)) return;
        var alert = _moderation.Check(message);
        if (alert is not null) AlertRaised?.Invoke(alert);

        if (!WindowFor(_uiWindows, message.Platform, ChatCore.UiPerSecondPerPlatform).TryAccept())
        {
            UiDropped++;
            return;
        }
        var display = alert is null ? message : new ChatMessage
        {
            Platform = message.Platform, Room = message.Room, UserId = message.UserId,
            UserName = message.UserName, Text = message.Text, Kind = message.Kind,
            Value = message.Value, At = message.At, IsModerated = true,
        };
        MessageAccepted?.Invoke(display);

        if (WindowFor(_overlayWindows, message.Platform, ChatCore.OverlayPerSecond).TryAccept())
        {
            if (_tts.TryEnqueue(display, out var spoken)) TtsRequested?.Invoke(spoken);
        }
        else
        {
            OverlayDropped++;
        }
    }

    private static ChatCore.RateWindow WindowFor(Dictionary<ChatPlatform, ChatCore.RateWindow> map, ChatPlatform platform, int limit)
    {
        if (!map.TryGetValue(platform, out var window))
        {
            window = new ChatCore.RateWindow(limit);
            map[platform] = window;
        }
        return window;
    }

    public void SetEnabled(bool enabled) => Enabled = enabled;
    public void MuteTts(bool muted) => _tts.Muted = muted;
    public string ExportAlerts() => _moderation.ExportCsv();

    public async Task StopAsync()
    {
        List<IChatAdapter> adapters;
        lock (_gate)
        {
            adapters = _adapters.ToList();
            _adapters.Clear();
        }
        foreach (var adapter in adapters)
        {
            adapter.MessageReceived -= OnMessage;
            await adapter.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}

/// <summary>弹幕 TTS（C03）：Windows SAPI（COM 后绑定），失败不影响弹幕本身。</summary>
public sealed class ChatTtsService : IDisposable
{
    private readonly ChatHub _hub;
    private readonly ChatTtsCore _core;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    private object? _voice;

    public ChatTtsService(ChatHub hub)
    {
        _hub = hub;
        _core = hub.Tts;
        _hub.TtsRequested += OnTts;
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    public bool Available { get; private set; } = true;
    public string LastError { get; private set; } = "";

    private void OnTts(string text) => _ = text; // 队列由 ChatTtsCore 持有，这里只负责消费

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_core.TryDequeue(out var text)) Speak(text);
                await Task.Delay(250, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Available = false;
                LastError = ex.GetType().Name;
                await Task.Delay(1000, ct).ConfigureAwait(false);
            }
        }
    }

    private void Speak(string text)
    {
        try
        {
            _voice ??= CreateVoice();
            if (_voice is null) { Available = false; return; }
            _voice.GetType().InvokeMember("Speak", System.Reflection.BindingFlags.InvokeMethod, null, _voice,
                new object[] { text, 1 });
        }
        catch (Exception)
        {
            Available = false;
        }
    }

    private static object? CreateVoice()
    {
        var type = Type.GetTypeFromProgID("SAPI.SpVoice");
        return type is null ? null : Activator.CreateInstance(type);
    }

    public void Dispose()
    {
        _hub.TtsRequested -= OnTts;
        _cts.Cancel();
        try { _loop.Wait(500); } catch { }
        _cts.Dispose();
    }
}

/// <summary>弹幕 overlay 页面（C02）：无远程资源、textContent 渲染、最多 50 条。</summary>
public static class ChatOverlayAssets
{
    public static string Html(string theme = "dark")
    {
        var css = theme switch
        {
            "light" => "--bg:transparent;--fg:#1c1d2b;--name:#1a6b5a;--bubble:rgba(255,255,255,.78)",
            "high" => "--bg:transparent;--fg:#fff;--name:#ffd166;--bubble:rgba(0,0,0,.85)",
            _ => "--bg:transparent;--fg:#fff;--name:#8fd6ff;--bubble:rgba(0,0,0,.55)",
        };
        return """
<!DOCTYPE html>
<html lang="zh"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>OBS Helper Chat</title>
<style>
:root{__CSS__;--max:50}
*{box-sizing:border-box}
html,body{margin:0;height:100%;background:var(--bg);color:var(--fg);font:22px/1.4 "Microsoft YaHei",system-ui,sans-serif;overflow:hidden}
#list{position:fixed;left:0;right:0;bottom:0;display:flex;flex-direction:column;gap:6px;padding:16px;max-height:100%;overflow:hidden;justify-content:flex-end}
.msg{background:var(--bubble);border-radius:10px;padding:6px 10px;word-break:break-word;white-space:pre-line;animation:in .18s ease-out}
.name{color:var(--name);font-weight:700;margin-right:8px}
@keyframes in{from{opacity:0;transform:translateY(6px)}to{opacity:1;transform:none}}
</style></head>
<body><div id="list" aria-live="polite"></div>
<script>
"use strict";
const list = document.getElementById("list");
const token = new URLSearchParams(location.search).get("token") || "";
let source = null, retry = 0;
function add(m) {
  const row = document.createElement("div"); row.className = "msg";
  const name = document.createElement("span"); name.className = "name"; name.textContent = m.userName || "";
  const text = document.createElement("span"); text.textContent = m.text || "";
  row.appendChild(name); row.appendChild(text); list.appendChild(row);
  while (list.children.length > 50) list.removeChild(list.firstChild);
}
function connect() {
  source = new EventSource("/chat/events?token=" + encodeURIComponent(token));
  source.onmessage = ev => { try { add(JSON.parse(ev.data)); } catch (e) {} };
  source.onerror = () => { if (source) source.close(); setTimeout(connect, Math.min(10000, 1000 * Math.pow(2, retry++))); };
  source.onopen = () => { retry = 0; };
}
connect();
</script></body></html>
""".Replace("__CSS__", css);
    }
}

/// <summary>overlay 服务（C02）：只绑回环、随机端口、一次性 token、SSE 推送。</summary>
public sealed class ChatOverlayServer : IDisposable
{
    private readonly string _theme;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly List<NetworkStream> _clients = new();
    private readonly object _gate = new();

    public ChatOverlayServer(string theme = "dark") => _theme = theme;

    public string BaseUrl { get; private set; } = "";
    public string Token { get; private set; } = "";
    public bool IsRunning => _listener is not null;

    public string? Start()
    {
        if (_listener is not null) return BuildUrl();
        try
        {
            Token = LanPolicy.NewToken(24);
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            BaseUrl = $"http://127.0.0.1:{port}";
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => AcceptAsync(_listener, _cts.Token));
            return BuildUrl();
        }
        catch (SocketException)
        {
            _listener = null;
            return null;
        }
    }

    private string BuildUrl() => BaseUrl + "/chat-live.html?token=" + Token;

    private async Task AcceptAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false); }
            catch (Exception) { break; }
            _ = Task.Run(() => HandleAsync(client, ct), ct);
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        using var _ = client;
        var stream = client.GetStream();
        var buffer = new byte[8192];
        var read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
        if (read <= 0) return;
        var head = Encoding.UTF8.GetString(buffer, 0, read);
        var line = head.Split("\r\n")[0];
        var parts = line.Split(' ');
        if (parts.Length < 2) return;
        var path = parts[1];
        var q = path.IndexOf('?');
        var route = q >= 0 ? path[..q] : path;
        var query = HttpWireCore.ParseQuery(q >= 0 ? path[(q + 1)..] : "");
        if (route == "/chat-live.html")
        {
            var html = ChatOverlayAssets.Html(_theme);
            var response = HttpWireCore.BuildResponse(200, "OK", "text/html; charset=utf-8",
                Encoding.UTF8.GetBytes(html), LanPolicy.SecurityHeaders(LanPolicy.OverlayCsp));
            await stream.WriteAsync(response, ct).ConfigureAwait(false);
            return;
        }
        if (route == "/chat/events")
        {
            query.TryGetValue("token", out var token);
            if (!LanPolicy.FixedTimeEquals(token, Token)) return;
            var headers = "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream; charset=utf-8\r\nCache-Control: no-store\r\nConnection: keep-alive\r\n";
            foreach (var (name, value) in LanPolicy.SecurityHeaders(LanPolicy.OverlayCsp))
                headers += name + ": " + value + "\r\n";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(headers + "\r\n"), ct).ConfigureAwait(false);
            lock (_gate) _clients.Add(stream);
            try
            {
                while (!ct.IsCancellationRequested && client.Connected)
                    await Task.Delay(2000, ct).ConfigureAwait(false);
            }
            catch (Exception) { }
            finally { lock (_gate) _clients.Remove(stream); }
        }
    }

    public void Broadcast(ChatMessage message)
    {
        var payload = JsonSerializer.Serialize(new
        {
            userName = message.UserName,
            text = message.Text,
            kind = message.Kind,
        });
        var bytes = Encoding.UTF8.GetBytes("data: " + payload + "\n\n");
        List<NetworkStream> clients;
        lock (_gate) clients = _clients.ToList();
        foreach (var client in clients)
        {
            try { client.Write(bytes); }
            catch (Exception) { lock (_gate) _clients.Remove(client); }
        }
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        lock (_gate)
        {
            foreach (var client in _clients) { try { client.Dispose(); } catch { } }
            _clients.Clear();
        }
        _listener = null;
        _cts?.Dispose();
        _cts = null;
    }
}
