using System.Net;
using System.Text;
using OBS_Helper.Wpf.Services.Security;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>局域网安全策略：私网判定 / 限流 / token / 常量时间比较 / 设备名清洗。</summary>
public class LanPolicyTests
{
    [Theory]
    [InlineData("10.0.0.1", true)]
    [InlineData("172.16.5.4", true)]
    [InlineData("172.31.255.254", true)]
    [InlineData("192.168.1.10", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("169.254.10.2", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("0.0.0.0", false)]
    public void IPv4PrivateRanges(string address, bool expected)
        => Assert.Equal(expected, LanPolicy.IsPrivateAddress(address));

    [Theory]
    [InlineData("::1", true)]
    [InlineData("fd12:3456::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("2001:4860:4860::8888", false)]
    [InlineData("not-an-ip", false)]
    [InlineData("", false)]
    public void IPv6PrivateRanges(string address, bool expected)
        => Assert.Equal(expected, LanPolicy.IsPrivateAddress(address));

    [Fact]
    public void IPv4MappedAddressIsClassifiedByItsV4Form()
        => Assert.True(LanPolicy.IsPrivateAddress(IPAddress.Parse("::ffff:192.168.0.2")));

    [Fact]
    public void PickLanAddressPrefersPrivateNonLinkLocal()
    {
        var picked = LanPolicy.PickLanAddress(new[]
        {
            IPAddress.Parse("127.0.0.1"),
            IPAddress.Parse("169.254.1.1"),
            IPAddress.Parse("192.168.1.7"),
            IPAddress.Parse("10.1.2.3"),
        });
        Assert.Equal(IPAddress.Parse("10.1.2.3"), picked);
        Assert.Null(LanPolicy.PickLanAddress(new[] { IPAddress.Parse("127.0.0.1") }));
    }

    [Fact]
    public void TokensAreUrlSafeAndLongEnough()
    {
        var token = LanPolicy.NewToken();
        Assert.True(token.Length >= 32);
        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain('=', token);
        Assert.NotEqual(token, LanPolicy.NewToken());
    }

    [Fact]
    public void ShortCodeIsSixDigits()
    {
        for (var i = 0; i < 50; i++)
        {
            var code = LanPolicy.NewShortCode();
            Assert.Equal(6, code.Length);
            Assert.True(int.TryParse(code, out var value) && value is >= 0 and < 1_000_000);
        }
    }

    [Fact]
    public void FixedTimeEqualsComparesContentNotIdentity()
    {
        Assert.True(LanPolicy.FixedTimeEquals("secret", "secret"));
        Assert.False(LanPolicy.FixedTimeEquals("secret", "secreT"));
        Assert.False(LanPolicy.FixedTimeEquals("secret", "secret-longer"));
        Assert.False(LanPolicy.FixedTimeEquals(null, "secret"));
    }

    [Fact]
    public void TokenHashIsStableAndNotTheToken()
    {
        var hash = LanPolicy.HashToken("abc");
        Assert.Equal(hash, LanPolicy.HashToken("abc"));
        Assert.NotEqual("abc", hash);
        Assert.DoesNotContain('=', hash);
    }

    [Theory]
    [InlineData(null, "phone")]
    [InlineData("", "phone")]
    [InlineData("  ", "phone")]
    [InlineData("  小明的手机  ", "小明的手机")]
    [InlineData("a\u0000b\u200Bc", "abc")]
    [InlineData("  multiple   spaces  ", "multiple spaces")]
    public void DeviceNameIsSanitized(string? input, string expected)
        => Assert.Equal(expected, LanPolicy.SanitizeDeviceName(input));

    [Fact]
    public void DeviceNameIsTruncatedTo64()
    {
        var name = LanPolicy.SanitizeDeviceName(new string('x', 200));
        Assert.Equal(64, name.Length);
    }

    [Fact]
    public void CspForbidsRemoteResources()
    {
        Assert.Contains("default-src 'none'", LanPolicy.PageCsp);
        Assert.Contains("connect-src 'self'", LanPolicy.PageCsp);
        Assert.DoesNotContain("http:", LanPolicy.PageCsp);
        Assert.Contains("frame-ancestors 'none'", LanPolicy.OverlayCsp);
    }

    [Fact]
    public void RateLimiterRejectsBurstAboveLimit()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var limiter = new LanPolicy.RateLimiter(limit: 3, window: TimeSpan.FromSeconds(1), clock: () => now);
        Assert.True(limiter.TryAcquire("dev"));
        Assert.True(limiter.TryAcquire("dev"));
        Assert.True(limiter.TryAcquire("dev"));
        Assert.False(limiter.TryAcquire("dev"));
        Assert.True(limiter.TryAcquire("other"));

        now = now.AddSeconds(2);
        Assert.True(limiter.TryAcquire("dev"));
    }

    [Fact]
    public void SecurityHeadersDoNotEnableCors()
    {
        var headers = LanPolicy.SecurityHeaders(LanPolicy.PageCsp);
        Assert.False(headers.ContainsKey("Access-Control-Allow-Origin"));
        Assert.Equal("nosniff", headers["X-Content-Type-Options"]);
        Assert.Equal("no-referrer", headers["Referrer-Policy"]);
    }

    [Fact]
    public void DefaultLimitsMatchPlan()
    {
        Assert.Equal(120, LanPolicy.PairTtlSeconds);
        Assert.Equal(12, LanPolicy.SessionHours);
        Assert.Equal(30, LanPolicy.IdleMinutes);
        Assert.Equal(20, LanPolicy.CommandsPerSecond);
        Assert.Equal(5, LanPolicy.MaxDevices);
        Assert.Equal(3, LanPolicy.MaxSessions);
    }
}

/// <summary>手写 HTTP 报文解析 / multipart / 响应构造。</summary>
public class HttpWireCoreTests
{
    [Fact]
    public void ParseGetRequestWithQuery()
    {
        var request = HttpWireCore.ParseHead(
            "GET /api/preview.jpg?fps=2&scene=%E6%88%91 HTTP/1.1\r\nHost: 192.168.1.2:8080\r\nContent-Length: 0");
        Assert.Equal("GET", request.Method);
        Assert.Equal("/api/preview.jpg", request.Path);
        Assert.Equal("2", request.QueryValue("fps"));
        Assert.Equal("我", request.QueryValue("scene"));
        Assert.Equal("192.168.1.2:8080", request.Header("host"));
    }

    [Fact]
    public void ParsePostWithBodyAndHeaders()
    {
        var request = HttpWireCore.ParseHead(
            "POST /api/pair HTTP/1.1\r\nContent-Type: application/json\r\nContent-Length: 2",
            Encoding.UTF8.GetBytes("{}"));
        Assert.Equal("POST", request.Method);
        Assert.Equal(2, request.Body.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("GET")]
    [InlineData("GET / HTTP/2.0")]
    [InlineData("GET / HTTP/1.1\r\nBadHeader")]
    [InlineData("PUT / HTTP/1.1")]
    public void RejectsMalformedRequests(string head)
        => Assert.Throws<HttpFormatException>(() => HttpWireCore.ParseHead(head));

    [Fact]
    public void RejectsChunkedRequests()
        => Assert.Throws<HttpFormatException>(() => HttpWireCore.ParseHead(
            "POST / HTTP/1.1\r\nTransfer-Encoding: chunked"));

    [Theory]
    [InlineData("/a/b", "/a/b")]
    [InlineData("/a/%2e%2e/b", null)]
    [InlineData("/a/%00b", null)]
    public void PathTraversalIsRejected(string path, string? expected)
    {
        if (expected is null)
            Assert.Throws<HttpFormatException>(() => HttpWireCore.ParseHead("GET " + path + " HTTP/1.1"));
        else
            Assert.Equal(expected, HttpWireCore.ParseHead("GET " + path + " HTTP/1.1").Path);
    }

    [Fact]
    public void BuildResponseIncludesHeadersAndBody()
    {
        var response = HttpWireCore.BuildResponse(200, "OK", "application/json",
            Encoding.UTF8.GetBytes("{\"ok\":true}"),
            new Dictionary<string, string> { ["X-Content-Type-Options"] = "nosniff" });
        var text = Encoding.UTF8.GetString(response);
        Assert.StartsWith("HTTP/1.1 200 OK\r\n", text);
        Assert.Contains("Content-Length: 11\r\n", text);
        Assert.Contains("X-Content-Type-Options: nosniff\r\n", text);
        Assert.EndsWith("{\"ok\":true}", text);
    }

    [Fact]
    public void BuildResponseHeadOnlyForHeadRequests()
    {
        var response = HttpWireCore.BuildResponse(200, "OK", "text/plain",
            Encoding.UTF8.GetBytes("hello"), includeBody: false);
        Assert.EndsWith("\r\n\r\n", Encoding.UTF8.GetString(response));
    }

    [Fact]
    public void QueryDecodingToleratesBadPercentEncoding()
    {
        var query = HttpWireCore.ParseQuery("a=1&b=%zz&c=x+y");
        Assert.Equal("1", query["a"]);
        Assert.Equal("%zz", query["b"]);
        Assert.Equal("x y", query["c"]);
    }

    [Fact]
    public void MultipartRoundTrip()
    {
        const string boundary = "----obshelperboundary";
        var body = BuildMultipart(boundary,
            ("field", null, "text/plain", Encoding.UTF8.GetBytes("value")),
            ("file", "photo.jpg", "image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF, 0x00, 0x10 }));
        Assert.True(HttpWireCore.TryParseMultipart(body,
            "multipart/form-data; boundary=" + boundary, out var parts));
        Assert.Equal(2, parts.Count);
        Assert.Equal("field", parts[0].Name);
        Assert.Equal("value", Encoding.UTF8.GetString(parts[0].Data));
        Assert.Equal("photo.jpg", parts[1].FileName);
        Assert.Equal(new byte[] { 0xFF, 0xD8, 0xFF, 0x00, 0x10 }, parts[1].Data);
    }

    [Fact]
    public void MultipartRejectsMissingBoundary()
        => Assert.False(HttpWireCore.TryParseMultipart(new byte[] { 1, 2, 3 }, "multipart/form-data", out _));

    [Fact]
    public void MultipartRejectsTruncatedBody()
    {
        var body = Encoding.ASCII.GetBytes("--b\r\nContent-Disposition: form-data; name=\"x\"\r\n\r\nabc");
        Assert.False(HttpWireCore.TryParseMultipart(body, "multipart/form-data; boundary=b", out _));
    }

    internal static byte[] BuildMultipart(string boundary, params (string Name, string? FileName, string Type, byte[] Data)[] parts)
    {
        using var stream = new MemoryStream();
        foreach (var part in parts)
        {
            var header = $"--{boundary}\r\nContent-Disposition: form-data; name=\"{part.Name}\"" +
                         (part.FileName is null ? "" : $"; filename=\"{part.FileName}\"") +
                         $"\r\nContent-Type: {part.Type}\r\n\r\n";
            var bytes = Encoding.UTF8.GetBytes(header);
            stream.Write(bytes);
            stream.Write(part.Data);
            stream.Write(Encoding.ASCII.GetBytes("\r\n"));
        }
        stream.Write(Encoding.ASCII.GetBytes($"--{boundary}--\r\n"));
        return stream.ToArray();
    }
}

/// <summary>RFC 6455 帧编解码。</summary>
public class WebSocketFrameCoreTests
{
    private static byte[] MaskedFrame(byte[] payload, int opcode = 1, bool fin = true)
    {
        var mask = new byte[] { 0x11, 0x22, 0x33, 0x44 };
        var headerLength = payload.Length < 126 ? 2 : payload.Length <= ushort.MaxValue ? 4 : 10;
        var frame = new byte[headerLength + 4 + payload.Length];
        frame[0] = (byte)((fin ? 0x80 : 0) | opcode);
        if (payload.Length < 126) frame[1] = (byte)(0x80 | payload.Length);
        else if (payload.Length <= ushort.MaxValue)
        {
            frame[1] = (byte)(0x80 | 126);
            frame[2] = (byte)(payload.Length >> 8);
            frame[3] = (byte)payload.Length;
        }
        else
        {
            frame[1] = (byte)(0x80 | 127);
            for (var i = 0; i < 8; i++) frame[2 + i] = (byte)((long)payload.Length >> (56 - i * 8));
        }
        Array.Copy(mask, 0, frame, headerLength, 4);
        for (var i = 0; i < payload.Length; i++) frame[headerLength + 4 + i] = (byte)(payload[i] ^ mask[i & 3]);
        return frame;
    }

    [Fact]
    public void AcceptKeyMatchesRfcExample()
        => Assert.Equal("s3pPLMBiTxaQ9kYGzzhZRbK+xOo=", WebSocketFrameCore.AcceptKey("dGhlIHNhbXBsZSBub25jZQ=="));

    [Fact]
    public void HandshakeRequiresUpgradeAndVersion()
    {
        var good = HttpWireCore.ParseHead(
            "GET /ws HTTP/1.1\r\nHost: x\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
            "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nSec-WebSocket-Version: 13");
        Assert.True(WebSocketFrameCore.IsValidHandshake(good, out var accept));
        Assert.Equal("s3pPLMBiTxaQ9kYGzzhZRbK+xOo=", accept);

        var bad = HttpWireCore.ParseHead("GET /ws HTTP/1.1\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: short");
        Assert.False(WebSocketFrameCore.IsValidHandshake(bad, out _));
    }

    [Fact]
    public void DecodesMaskedTextFrame()
    {
        var frame = MaskedFrame(Encoding.UTF8.GetBytes("hello"));
        Assert.True(WebSocketFrameCore.TryDecode(frame, out var decoded, out var consumed));
        Assert.NotNull(decoded);
        Assert.Equal("hello", decoded!.Text);
        Assert.Equal(frame.Length, consumed);
    }

    [Fact]
    public void IncompleteFrameReturnsFalse()
    {
        var frame = MaskedFrame(Encoding.UTF8.GetBytes("hello"));
        Assert.False(WebSocketFrameCore.TryDecode(frame.AsSpan(0, frame.Length - 2), out _, out _));
    }

    [Fact]
    public void RejectsUnmaskedClientFrame()
    {
        var frame = WebSocketFrameCore.EncodeText("x");
        Assert.Throws<HttpFormatException>(() => WebSocketFrameCore.TryDecode(frame, out _, out _));
    }

    [Fact]
    public void RejectsOversizedPayload()
    {
        var huge = new byte[WebSocketFrameCore.MaxFrameBytes + 1];
        var frame = MaskedFrame(huge);
        Assert.Throws<HttpFormatException>(() => WebSocketFrameCore.TryDecode(frame, out _, out _));
    }

    [Fact]
    public void ServerFramesAreNotMaskedAndRoundTrip()
    {
        var encoded = WebSocketFrameCore.EncodeText("{\"ok\":true}");
        Assert.Equal(0x81, encoded[0]);
        Assert.Equal(0, encoded[1] & 0x80);
        // 服务端帧没有掩码：用一个测试专用解码器验证长度与载荷
        Assert.Equal(11, encoded[1]);
        Assert.Equal("{\"ok\":true}", Encoding.UTF8.GetString(encoded, 2, encoded.Length - 2));
    }

    [Fact]
    public void MediumPayloadUses16BitLength()
    {
        var payload = new string('a', 300);
        var encoded = WebSocketFrameCore.EncodeText(payload);
        Assert.Equal(126, encoded[1]);
        Assert.Equal(300, (encoded[2] << 8) | encoded[3]);
    }

    [Fact]
    public void CloseFrameCarriesCode()
    {
        var frame = MaskedFrame(WebSocketFrameCore.EncodeClose(4001, "bye").AsSpan(2).ToArray(), opcode: 8);
        Assert.True(WebSocketFrameCore.TryDecode(frame, out var decoded, out _));
        Assert.Equal(4001, WebSocketFrameCore.CloseCode(decoded!));
    }
}
