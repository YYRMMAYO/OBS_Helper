using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using OBS_Helper.Wpf.Services.Security;

namespace OBS_Helper.Wpf.Services.Mobile;

/// <summary>
/// 手机摄像头推流鉴权（V3.7.1）。
///
/// mediamtx 只把「path + 动作 + 协议 + 来源 IP」交给本机回环 HTTP 服务判断：
/// <list type="bullet">
///   <item>publish：只允许当前活动会话的 <c>live/&lt;key&gt;</c>，且来源必须是私网/回环；</item>
///   <item>read：只允许活动会话的 path（OBS 回环 RTSP / 手机 WebRTC 监看）；</item>
///   <item>api / metrics / pprof / playback：一律拒绝，避免把控制面暴露给推流端。</item>
/// </list>
/// 停止机位时从活动会话表移除，旧 key 立即不能再建立新连接；已建立的连接由
/// <see cref="MediaServerManager"/> 调 mediamtx API 踢掉。服务只绑 <c>127.0.0.1</c>。
/// </summary>
public static class PhoneCameraAuthCore
{
    public static bool IsAllowed(
        string? action, string? protocol, string? path, string? ip, bool pathIsActive)
    {
        if (!pathIsActive || string.IsNullOrWhiteSpace(path)) return false;
        if (!path.StartsWith("live/", StringComparison.Ordinal)) return false;
        if (!LanPolicy.IsPrivateAddress(ip)) return false;

        var normalizedAction = (action ?? "").Trim().ToLowerInvariant();
        var normalizedProtocol = (protocol ?? "").Trim().ToLowerInvariant();
        return normalizedAction switch
        {
            // 手机 SRT / RTMP / WHIP 推流
            "publish" => normalizedProtocol is "srt" or "rtmp" or "webrtc",
            // OBS 回环 RTSP 读流、手机 WebRTC 监看、兼容 RTMP/SRT 读
            "read" => normalizedProtocol is "rtsp" or "rtmp" or "srt" or "webrtc",
            _ => false,
        };
    }
}

/// <summary>极小的回环 HTTP 鉴权端点，只实现 mediamtx 需要的 POST /auth。</summary>
public sealed class PhoneCameraAuthServer : IDisposable
{
    private readonly Func<string, bool> _isActivePath;
    private readonly object _gate = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    public PhoneCameraAuthServer(Func<string, bool> isActivePath)
        => _isActivePath = isActivePath ?? throw new ArgumentNullException(nameof(isActivePath));

    public int Port { get; private set; }
    public bool IsRunning { get; private set; }

    public (bool Ok, string Code) Start()
    {
        lock (_gate)
        {
            if (IsRunning) return (true, "");
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                _listener = listener;
                _cts = new CancellationTokenSource();
                Port = ((IPEndPoint)listener.LocalEndpoint).Port;
                IsRunning = true;
                _ = Task.Run(() => AcceptLoopAsync(listener, _cts.Token));
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
        lock (_gate)
        {
            if (!IsRunning) return;
            IsRunning = false;
            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            _listener = null;
            _cts?.Dispose();
            _cts = null;
            Port = 0;
        }
    }

    public void Dispose() => Stop();

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { continue; }

            _ = Task.Run(async () =>
            {
                try { await HandleClientAsync(client, ct).ConfigureAwait(false); }
                catch (Exception) { }
                finally { try { client.Dispose(); } catch { } }
            }, ct);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var stream = client.GetStream();
        var request = await ReadRequestAsync(stream, timeout.Token).ConfigureAwait(false);
        if (request is null || request.Method != "POST" || request.Path != "/auth")
        {
            await WriteAsync(stream, 400, "Bad Request", "{\"ok\":false}").ConfigureAwait(false);
            return;
        }

        string action, protocol, path, ip;
        try
        {
            using var doc = JsonDocument.Parse(request.Body);
            var root = doc.RootElement;
            action = GetString(root, "action");
            protocol = GetString(root, "protocol");
            path = GetString(root, "path");
            ip = GetString(root, "ip");
        }
        catch (JsonException)
        {
            await WriteAsync(stream, 400, "Bad Request", "{\"ok\":false}").ConfigureAwait(false);
            return;
        }

        var allowed = PhoneCameraAuthCore.IsAllowed(action, protocol, path, ip, _isActivePath(path));
        await WriteAsync(stream, allowed ? 200 : 401,
            allowed ? "OK" : "Unauthorized",
            allowed ? "{}" : "{\"ok\":false}").ConfigureAwait(false);
    }

    private static string GetString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static Task WriteAsync(NetworkStream stream, int status, string reason, string json)
        => stream.WriteAsync(HttpWireCore.BuildResponse(
            status, reason, "application/json; charset=utf-8",
            Encoding.UTF8.GetBytes(json))).AsTask();

    private static async Task<HttpRequest?> ReadRequestAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[4096];
        var headEnd = -1;
        while (headEnd < 0)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(), ct).ConfigureAwait(false);
            if (read <= 0) return null;
            buffer.Write(chunk, 0, read);
            if (buffer.Length > HttpWireCore.MaxHeadBytes) return null;
            headEnd = HttpWireCore.FindHeadEnd(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
        }

        var all = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
        var head = Encoding.UTF8.GetString(all[..headEnd]);
        HttpRequest parsed;
        try { parsed = HttpWireCore.ParseHead(head); }
        catch (HttpFormatException) { return null; }

        var length = 0;
        if (parsed.Headers.TryGetValue("Content-Length", out var lengthHeader) &&
            !int.TryParse(lengthHeader, out length)) return null;
        if (length < 0 || length > 64 * 1024) return null;

        var body = new byte[length];
        var already = all.Length - headEnd - 4;
        if (already > 0)
            Buffer.BlockCopy(all[(headEnd + 4)..].ToArray(), 0, body, 0, Math.Min(already, length));
        var offset = Math.Max(0, already);
        while (offset < length)
        {
            var read = await stream.ReadAsync(body.AsMemory(offset, length - offset), ct).ConfigureAwait(false);
            if (read <= 0) return null;
            offset += read;
        }

        try { return HttpWireCore.ParseHead(head, body); }
        catch (HttpFormatException) { return null; }
    }
}
