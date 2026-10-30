using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace OBS_Helper.Wpf.Services.Security;

/// <summary>WebSocket 帧操作码（RFC 6455 §5.2）。</summary>
public enum WsOpcode : byte
{
    Continuation = 0x0,
    Text = 0x1,
    Binary = 0x2,
    Close = 0x8,
    Ping = 0x9,
    Pong = 0xA,
}

/// <summary>解析出的一个 WebSocket 帧。</summary>
public sealed class WsFrame
{
    public bool Fin { get; init; }
    public WsOpcode Opcode { get; init; }
    public bool Masked { get; init; }
    public byte[] Payload { get; init; } = Array.Empty<byte>();
    public string Text => Encoding.UTF8.GetString(Payload);
}

/// <summary>
/// RFC 6455 帧编解码（V3.4.0 / 批次 0）。
///
/// 只实现服务端需要的部分：接受客户端掩码帧、发送不掩码帧、Ping/Pong/Close。
/// 限制单帧 1 MB、拒绝 64 位长度中的超限值、拒绝客户端不掩码的帧（协议要求），
/// 这些检查都是纯函数，可单测（见 <c>WebSocketFrameCoreTests</c>）。
/// </summary>
public static class WebSocketFrameCore
{
    /// <summary>RFC 6455 固定 GUID。</summary>
    private const string Guid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    /// <summary>单帧上限（1 MB）；配对消息与预览请求都远小于它。</summary>
    public const int MaxFrameBytes = 1024 * 1024;

    /// <summary>计算 <c>Sec-WebSocket-Accept</c>。</summary>
    public static string AcceptKey(string clientKey)
    {
        var hash = SHA1.HashData(Encoding.ASCII.GetBytes((clientKey ?? "").Trim() + Guid));
        return Convert.ToBase64String(hash);
    }

    /// <summary>握手响应报文（不含正文）。</summary>
    public static byte[] BuildHandshakeResponse(string acceptKey)
        => Encoding.ASCII.GetBytes(
            "HTTP/1.1 101 Switching Protocols\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Accept: " + acceptKey + "\r\n\r\n");

    /// <summary>校验握手请求：必须是 GET、Upgrade: websocket、带 16 字节 base64 key。</summary>
    public static bool IsValidHandshake(HttpRequest request, out string acceptKey)
    {
        acceptKey = "";
        if (!string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals(request.Header("Upgrade"), "websocket", StringComparison.OrdinalIgnoreCase)) return false;
        var connection = request.Header("Connection") ?? "";
        if (!connection.Contains("Upgrade", StringComparison.OrdinalIgnoreCase)) return false;
        var key = request.Header("Sec-WebSocket-Key");
        if (string.IsNullOrWhiteSpace(key)) return false;
        try
        {
            if (Convert.FromBase64String(key.Trim()).Length != 16) return false;
        }
        catch (FormatException)
        {
            return false;
        }
        if (!string.Equals(request.Header("Sec-WebSocket-Version"), "13", StringComparison.OrdinalIgnoreCase))
            return false;
        acceptKey = AcceptKey(key);
        return true;
    }

    /// <summary>
    /// 尝试从缓冲区解出一帧。返回 false 表示数据还不完整（<paramref name="consumed"/>=0）；
    /// 数据非法时抛 <see cref="HttpFormatException"/>（调用方应直接关闭连接）。
    /// </summary>
    public static bool TryDecode(ReadOnlySpan<byte> buffer, out WsFrame? frame, out int consumed)
    {
        frame = null;
        consumed = 0;
        if (buffer.Length < 2) return false;

        var b0 = buffer[0];
        var b1 = buffer[1];
        var fin = (b0 & 0x80) != 0;
        var rsv = b0 & 0x70;
        if (rsv != 0) throw new HttpFormatException("RSV bits must be zero.");
        var opcode = (WsOpcode)(b0 & 0x0F);
        if (!Enum.IsDefined(opcode)) throw new HttpFormatException("Unknown opcode.");

        var masked = (b1 & 0x80) != 0;
        long length = b1 & 0x7F;
        var offset = 2;

        if (length == 126)
        {
            if (buffer.Length < offset + 2) return false;
            length = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(offset, 2));
            offset += 2;
        }
        else if (length == 127)
        {
            if (buffer.Length < offset + 8) return false;
            var raw = BinaryPrimitives.ReadUInt64BigEndian(buffer.Slice(offset, 8));
            if (raw > long.MaxValue) throw new HttpFormatException("Frame too large.");
            length = (long)raw;
            offset += 8;
        }

        if (length > MaxFrameBytes) throw new HttpFormatException("Frame exceeds limit.");
        if (!masked) throw new HttpFormatException("Client frames must be masked.");
        if (buffer.Length < offset + 4) return false;
        var mask = buffer.Slice(offset, 4).ToArray();
        offset += 4;
        if (buffer.Length < offset + length) return false;

        var payload = new byte[length];
        for (var i = 0; i < length; i++) payload[i] = (byte)(buffer[offset + i] ^ mask[i & 3]);
        consumed = offset + (int)length;
        frame = new WsFrame { Fin = fin, Opcode = opcode, Masked = masked, Payload = payload };
        return true;
    }

    /// <summary>服务端发送帧（不掩码）。</summary>
    public static byte[] Encode(WsOpcode opcode, ReadOnlySpan<byte> payload)
    {
        var length = payload.Length;
        var header = length switch
        {
            < 126 => 2,
            <= ushort.MaxValue => 4,
            _ => 10,
        };
        var result = new byte[header + length];
        result[0] = (byte)(0x80 | (byte)opcode);
        if (length < 126)
        {
            result[1] = (byte)length;
        }
        else if (length <= ushort.MaxValue)
        {
            result[1] = 126;
            BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(2, 2), (ushort)length);
        }
        else
        {
            result[1] = 127;
            BinaryPrimitives.WriteUInt64BigEndian(result.AsSpan(2, 8), (ulong)length);
        }
        payload.CopyTo(result.AsSpan(header));
        return result;
    }

    /// <summary>文本帧。</summary>
    public static byte[] EncodeText(string text) => Encode(WsOpcode.Text, Encoding.UTF8.GetBytes(text));

    /// <summary>关闭帧：<paramref name="code"/> 用 RFC 定义的状态码（1000 正常关闭）。</summary>
    public static byte[] EncodeClose(int code = 1000, string reason = "")
    {
        var reasonBytes = Encoding.UTF8.GetBytes(reason);
        var payload = new byte[2 + reasonBytes.Length];
        BinaryPrimitives.WriteUInt16BigEndian(payload, (ushort)code);
        reasonBytes.CopyTo(payload, 2);
        return Encode(WsOpcode.Close, payload);
    }

    /// <summary>解析关闭帧的状态码；解析不出返回 1000。</summary>
    public static int CloseCode(WsFrame frame)
    {
        if (frame.Opcode != WsOpcode.Close || frame.Payload.Length < 2) return 1000;
        return BinaryPrimitives.ReadUInt16BigEndian(frame.Payload);
    }
}
