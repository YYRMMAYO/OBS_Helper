using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace OBS_Helper.Wpf.Services.Live;

/// <summary>
/// 本地 overlay HTTP 服务（V3.3.0 / P0-2）。
///
/// 只做一件事：把 <see cref="RoomOverlayAssets"/> 里的静态页面提供给 OBS 的 <c>browser_source</c>。
/// 安全边界（PLAN_V3.3.0 §3.5）：
/// <list type="bullet">
///   <item>只绑定 <c>127.0.0.1</c>（回环），外部网络不可达；</item>
///   <item>端口随机，路径带一次性 token；token 不对一律 404（不区分「不存在」与「无权限」，避免探测）；</item>
///   <item>只读、只服务内置页面，防目录穿越；</item>
///   <item>响应带 CSP / nosniff / no-referrer，页面不能发远程请求。</item>
/// </list>
///
/// 生命周期跟随应用：<see cref="Start"/> 幂等，退出时 <see cref="Dispose"/> 释放端口。
/// </summary>
public sealed class RoomOverlayServer : IDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly object _gate = new();

    /// <summary>服务根地址（形如 <c>http://127.0.0.1:54321/</c>）；未启动时为空串。</summary>
    public string BaseUrl { get; private set; } = "";

    /// <summary>一次性路径 token（每次启动重新生成）。</summary>
    public string Token { get; private set; } = "";

    public bool IsRunning
    {
        get { lock (_gate) return _listener is not null; }
    }

    /// <summary>启动服务；已启动或启动失败都返回当前是否可用（不抛异常给调用方）。</summary>
    public bool Start()
    {
        lock (_gate)
        {
            if (_listener is not null) return true;

            try
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Token = RoomOverlayCore.NewToken();
                BaseUrl = $"http://127.0.0.1:{port}/";
                _cts = new CancellationTokenSource();
                _listener = listener;

                var ct = _cts.Token;
                _ = Task.Run(() => AcceptLoopAsync(listener, ct), CancellationToken.None);
                FileLogger.Info("RoomOverlay", $"本地 overlay 服务已启动：{BaseUrl}");
                return true;
            }
            catch (Exception ex)
            {
                FileLogger.Warn("RoomOverlay", $"本地 overlay 服务启动失败（聊天/装饰页将不可用）：{ex.Message}");
                StopCore();
                return false;
            }
        }
    }

    private void StopCore()
    {
        try { _listener?.Stop(); } catch { /* 已停止 */ }
        try { _cts?.Cancel(); } catch { /* 已释放 */ }
        try { _cts?.Dispose(); } catch { /* 忽略 */ }
        _listener = null;
        _cts = null;
        BaseUrl = "";
        Token = "";
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { break; }
            catch (Exception ex)
            {
                FileLogger.Warn("RoomOverlay", $"接受连接失败：{ex.Message}");
                continue;
            }

            _ = Task.Run(() => HandleClientAsync(client, ct), CancellationToken.None);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 5000;
                client.SendTimeout = 5000;
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);

                var requestLine = await reader.ReadLineAsync().ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(requestLine)) return;

                // GET /<token>/<page>?query HTTP/1.1
                var parts = requestLine.Split(' ');
                if (parts.Length < 2) { await WriteStatusAsync(stream, 400, "Bad Request", ct).ConfigureAwait(false); return; }
                if (!string.Equals(parts[0], "GET", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(parts[0], "HEAD", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteStatusAsync(stream, 405, "Method Not Allowed", ct).ConfigureAwait(false);
                    return;
                }

                var path = parts[1];
                if (path.Length > RoomOverlayCore.MaxUrlLength)
                {
                    await WriteStatusAsync(stream, 414, "URI Too Long", ct).ConfigureAwait(false);
                    return;
                }

                var prefix = "/" + Token + "/";
                if (!path.StartsWith(prefix, StringComparison.Ordinal))
                {
                    await WriteStatusAsync(stream, 404, "Not Found", ct).ConfigureAwait(false);
                    return;
                }

                var pageAndQuery = path[prefix.Length..];
                var q = pageAndQuery.IndexOf('?');
                var page = q >= 0 ? pageAndQuery[..q] : pageAndQuery;
                var query = q >= 0 ? pageAndQuery[(q + 1)..] : "";

                if (!RoomOverlayCore.IsKnownPage(page) || !RoomOverlayCore.IsSafeQuery(query))
                {
                    await WriteStatusAsync(stream, 404, "Not Found", ct).ConfigureAwait(false);
                    return;
                }

                var html = RoomOverlayAssets.TryGet(page);
                if (html is null)
                {
                    await WriteStatusAsync(stream, 404, "Not Found", ct).ConfigureAwait(false);
                    return;
                }

                await WriteHtmlAsync(stream, html, headOnly: parts[0].Equals("HEAD", StringComparison.OrdinalIgnoreCase), ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException)
            {
                // 客户端提前断开 / 超时：正常情况（OBS 刷新页面时经常发生），不写日志刷屏
            }
            catch (Exception ex)
            {
                FileLogger.Warn("RoomOverlay", $"处理请求失败：{ex.Message}");
            }
        }
    }

    private static async Task WriteStatusAsync(Stream stream, int code, string reason, CancellationToken ct)
    {
        var body = $"<!doctype html><meta charset=\"utf-8\"><title>{code}</title><p>{code} {reason}</p>";
        var bytes = Encoding.UTF8.GetBytes(body);
        var head = new StringBuilder()
            .Append("HTTP/1.1 ").Append(code).Append(' ').Append(reason).Append("\r\n")
            .Append("Content-Type: text/html; charset=utf-8\r\n")
            .Append("Content-Length: ").Append(bytes.Length).Append("\r\n")
            .Append("Cache-Control: no-store\r\n")
            .Append("X-Content-Type-Options: nosniff\r\n")
            .Append("Connection: close\r\n\r\n")
            .ToString();
        var headBytes = Encoding.ASCII.GetBytes(head);
        await stream.WriteAsync(headBytes, ct).ConfigureAwait(false);
        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
    }

    private static async Task WriteHtmlAsync(Stream stream, string html, bool headOnly, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(html);
        var head = new StringBuilder()
            .Append("HTTP/1.1 200 OK\r\n")
            .Append("Content-Type: text/html; charset=utf-8\r\n")
            .Append("Content-Length: ").Append(bytes.Length).Append("\r\n")
            .Append("Cache-Control: no-store\r\n")
            .Append("X-Content-Type-Options: nosniff\r\n")
            .Append("Referrer-Policy: no-referrer\r\n")
            // 页面内联 <style>/<script>；除此之外不允许任何外部资源（default-src 'none'）
            .Append("Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; img-src data:\r\n")
            .Append("Connection: close\r\n\r\n")
            .ToString();
        var headBytes = Encoding.ASCII.GetBytes(head);
        await stream.WriteAsync(headBytes, ct).ConfigureAwait(false);
        if (!headOnly) await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_listener is null) return;
            StopCore();
            FileLogger.Info("RoomOverlay", "本地 overlay 服务已停止。");
        }
    }
}
