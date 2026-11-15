using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace OBS_Helper.Wpf.Services.Timing;

/// <summary>
/// 计时器浏览器叠加层（V3.6.0 / T10）。
///
/// 只做一件事：把当前计时快照通过本地回环 HTTP 提供给 OBS 的 <c>browser_source</c>。
/// 安全边界与 RoomOverlayServer 一致：
/// <list type="bullet">
///   <item>只绑定 <c>127.0.0.1</c>，外部网络不可达；</item>
///   <item>端口随机，路径带随机 token；token 不对一律 404；</item>
///   <item>只读、只服务内置页面与状态 JSON，不读用户文件；</item>
///   <item>页面无外部资源，CSP 禁止远程请求。</item>
/// </list>
/// 生命周期：用户点「复制浏览器叠加地址」才启动；应用退出时 Dispose。
/// </summary>
public sealed class TimerOverlayServer : IDisposable
{
    private const int MaxUrlLength = 512;
    private static readonly byte[] NotFoundBody = Encoding.UTF8.GetBytes("not found");

    private readonly Func<TimerSnapshot> _snapshot;
    private readonly object _gate = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    public TimerOverlayServer(Func<TimerSnapshot> snapshot) => _snapshot = snapshot;

    /// <summary>服务根地址（形如 <c>http://127.0.0.1:54321/</c>）；未启动时为空串。</summary>
    public string BaseUrl { get; private set; } = "";

    /// <summary>一次性路径 token（每次启动重新生成）。</summary>
    public string Token { get; private set; } = "";

    public bool IsRunning
    {
        get { lock (_gate) return _listener is not null; }
    }

    /// <summary>给 OBS 浏览器源用的页面地址；未启动时为空串。</summary>
    public string PageUrl => IsRunning ? $"{BaseUrl}t/{Token}/" : "";

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
                Token = Guid.NewGuid().ToString("N");
                BaseUrl = $"http://127.0.0.1:{port}/";
                _cts = new CancellationTokenSource();
                _listener = listener;

                var ct = _cts.Token;
                _ = Task.Run(() => AcceptLoopAsync(listener, ct), CancellationToken.None);
                FileLogger.Info("TimerOverlay", $"计时器叠加服务已启动：{BaseUrl}");
                return true;
            }
            catch (Exception ex)
            {
                FileLogger.Warn("TimerOverlay", $"计时器叠加服务启动失败：{ex.Message}");
                StopCore();
                return false;
            }
        }
    }

    private void StopCore()
    {
        try { _listener?.Stop(); } catch (Exception) { /* 已停止 */ }
        try { _cts?.Cancel(); } catch (Exception) { /* 已释放 */ }
        try { _cts?.Dispose(); } catch (Exception) { /* 忽略 */ }
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
                FileLogger.Warn("TimerOverlay", $"接受连接失败：{ex.Message}");
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

                var parts = requestLine.Split(' ');
                if (parts.Length < 2)
                {
                    await WriteResponseAsync(stream, 400, "Bad Request", "text/plain; charset=utf-8",
                        Encoding.UTF8.GetBytes("bad request"), headOnly: false, withCsp: false, ct).ConfigureAwait(false);
                    return;
                }

                var method = parts[0];
                var path = parts[1];
                if (path.Length > MaxUrlLength)
                {
                    await WriteResponseAsync(stream, 414, "URI Too Long", "text/plain; charset=utf-8",
                        Encoding.UTF8.GetBytes("uri too long"), headOnly: false, withCsp: false, ct).ConfigureAwait(false);
                    return;
                }

                var headOnly = string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase);
                if (!headOnly && !string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteResponseAsync(stream, 405, "Method Not Allowed", "text/plain; charset=utf-8",
                        Encoding.UTF8.GetBytes("method not allowed"), headOnly: false, withCsp: false, ct).ConfigureAwait(false);
                    return;
                }

                var query = path.IndexOf('?');
                if (query >= 0) path = path[..query];

                var prefix = $"/t/{Token}";
                if (path == prefix || path == prefix + "/")
                {
                    await WriteResponseAsync(stream, 200, "OK", "text/html; charset=utf-8",
                        Encoding.UTF8.GetBytes(Html), headOnly, withCsp: true, ct).ConfigureAwait(false);
                    return;
                }

                if (path == prefix + "/state")
                {
                    var json = BuildStateJson();
                    await WriteResponseAsync(stream, 200, "OK", "application/json; charset=utf-8",
                        Encoding.UTF8.GetBytes(json), headOnly, withCsp: false, ct).ConfigureAwait(false);
                    return;
                }

                await WriteResponseAsync(stream, 404, "Not Found", "text/plain; charset=utf-8",
                    NotFoundBody, headOnly, withCsp: false, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                FileLogger.Warn("TimerOverlay", $"处理请求失败：{ex.Message}");
            }
        }
    }

    private string BuildStateJson()
    {
        var s = _snapshot();
        return JsonSerializer.Serialize(new
        {
            display = s.Display,
            secondary = s.Secondary,
            state = s.State.ToString().ToLowerInvariant(),
            mode = s.Mode == TimerMode.CountDown ? "countdown" : "countup",
            paused = s.IsPaused,
            overtime = s.IsOvertime,
            overtimeSeconds = (int)Math.Floor(s.Overtime.TotalSeconds),
        });
    }

    private static async Task WriteResponseAsync(
        Stream stream, int status, string reason, string contentType, byte[] body,
        bool headOnly, bool withCsp, CancellationToken ct)
    {
        var header = new StringBuilder();
        header.Append("HTTP/1.1 ").Append(status).Append(' ').Append(reason).Append("\r\n");
        header.Append("Connection: close\r\n");
        header.Append("Content-Type: ").Append(contentType).Append("\r\n");
        header.Append("Content-Length: ").Append(body.Length).Append("\r\n");
        header.Append("Cache-Control: no-store\r\n");
        header.Append("X-Content-Type-Options: nosniff\r\n");
        if (withCsp)
            header.Append("Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; connect-src 'self'\r\n");
        header.Append("\r\n");

        await stream.WriteAsync(Encoding.ASCII.GetBytes(header.ToString()), ct).ConfigureAwait(false);
        if (!headOnly) await stream.WriteAsync(body, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private const string Html = """
<!doctype html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>OBS Helper Timer</title>
<style>
html,body{margin:0;height:100%;background:#0b0f14;color:#e5e7eb;font-family:"Segoe UI",system-ui,sans-serif;overflow:hidden}
#wrap{height:100%;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:2vh}
#t{font-variant-numeric:tabular-nums;font-weight:700;font-size:min(22vw,22vh);line-height:1;letter-spacing:.02em;color:#e5e7eb}
#s{font-size:min(4vw,4vh);opacity:.75}
#badge{font-size:min(2.6vw,2.6vh);opacity:.55;letter-spacing:.08em}
.running #t{color:#38bdf8}
.paused #t{color:#f59e0b}
.overtime #t{color:#ef4444}
</style>
</head>
<body>
<div id="wrap" class="idle">
  <div id="t">--:--</div>
  <div id="s"></div>
  <div id="badge">OBS HELPER TIMER</div>
</div>
<script>
const wrap = document.getElementById('wrap');
const t = document.getElementById('t');
const s = document.getElementById('s');
async function tick() {
  try {
    const r = await fetch('./state', { cache: 'no-store' });
    if (!r.ok) throw new Error('http');
    const j = await r.json();
    t.textContent = j.display || '--:--';
    s.textContent = j.secondary || '';
    wrap.className = j.overtime ? 'overtime'
      : j.state === 'paused' ? 'paused'
      : j.state === 'running' ? 'running' : 'idle';
  } catch (e) {
    wrap.className = 'idle';
    s.textContent = '连接中断 / disconnected';
  }
}
setInterval(tick, 500);
tick();
</script>
</body>
</html>
""";

    public void Dispose()
    {
        lock (_gate) StopCore();
    }
}
