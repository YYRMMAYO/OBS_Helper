using System.Text;

namespace OBS_Helper.Wpf.Services.Security;

/// <summary>解析后的 HTTP/1.1 请求（只保留本应用用得到的部分）。</summary>
public sealed class HttpRequest
{
    public string Method { get; init; } = "";
    public string Target { get; init; } = "";
    public string Path { get; init; } = "";
    public string QueryString { get; init; } = "";
    public Dictionary<string, string> Query { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public byte[] Body { get; init; } = Array.Empty<byte>();

    public string? Header(string name) => Headers.TryGetValue(name, out var v) ? v : null;
    public string? QueryValue(string name) => Query.TryGetValue(name, out var v) ? v : null;
}

/// <summary>畸形请求（调用方据此返回 400，并**不**把细节回显给客户端）。</summary>
public sealed class HttpFormatException : Exception
{
    public HttpFormatException(string message) : base(message) { }
}

/// <summary>multipart/form-data 的一个分片。</summary>
public sealed class MultipartPart
{
    public string Name { get; init; } = "";
    public string? FileName { get; init; }
    public string? ContentType { get; init; }
    public byte[] Data { get; init; } = Array.Empty<byte>();
}

/// <summary>
/// 手写 HTTP/1.1 报文处理（V3.4.0 / 批次 0）。
///
/// 为什么不用 ASP.NET Core：主程序承诺零第三方 NuGet，而 ASP.NET Core 需要额外的
/// FrameworkReference 与宿主；这里只需要「极小的请求解析 + 固定响应 + multipart」，
/// `TcpListener` + 纯函数足够，也便于把协议细节做成可单测的纯逻辑。
///
/// 只支持：GET / POST、Content-Length（不支持 chunked 请求）、HTTP/1.0 与 1.1。
/// 超出上限的请求由调用方在读取阶段拒绝（这里仍会二次校验）。
/// </summary>
public static class HttpWireCore
{
    /// <summary>请求头上限（8 KB）。</summary>
    public const int MaxHeadBytes = 8 * 1024;

    /// <summary>请求体上限（上传 50 MB + multipart 开销）。</summary>
    public const int MaxBodyBytes = 52 * 1024 * 1024;

    /// <summary>在缓冲区里找 <c>\r\n\r\n</c>；找不到返回 -1。</summary>
    public static int FindHeadEnd(ReadOnlySpan<byte> buffer)
    {
        for (var i = 0; i + 3 < buffer.Length; i++)
        {
            if (buffer[i] == (byte)'\r' && buffer[i + 1] == (byte)'\n'
                && buffer[i + 2] == (byte)'\r' && buffer[i + 3] == (byte)'\n') return i;
        }
        return -1;
    }

    /// <summary>解析请求行 + 头（<paramref name="head"/> 不含末尾空行）。</summary>
    public static HttpRequest ParseHead(string head, byte[]? body = null)
    {
        var lines = head.Replace("\r\n", "\n").Split('\n');
        if (lines.Length == 0) throw new HttpFormatException("Empty request.");

        var requestLine = lines[0].Split(' ');
        if (requestLine.Length != 3) throw new HttpFormatException("Bad request line.");
        var method = requestLine[0].ToUpperInvariant();
        if (method is not ("GET" or "POST" or "HEAD" or "DELETE" or "OPTIONS"))
            throw new HttpFormatException("Method not allowed.");
        if (!requestLine[2].StartsWith("HTTP/1.", StringComparison.Ordinal))
            throw new HttpFormatException("Unsupported protocol.");

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0) continue;
            var colon = line.IndexOf(':');
            if (colon <= 0) throw new HttpFormatException("Bad header line.");
            var name = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (name.Length == 0) throw new HttpFormatException("Bad header name.");
            // 同名头合并（Cookie 之外的场景够用；本应用不读 Cookie）
            headers[name] = headers.TryGetValue(name, out var existing) ? existing + ", " + value : value;
        }

        if (headers.TryGetValue("Transfer-Encoding", out var te) &&
            te.Contains("chunked", StringComparison.OrdinalIgnoreCase))
            throw new HttpFormatException("Chunked requests are not supported.");

        var target = requestLine[1];
        var q = target.IndexOf('?');
        var path = q >= 0 ? target[..q] : target;
        var query = q >= 0 ? target[(q + 1)..] : "";
        return new HttpRequest
        {
            Method = method,
            Target = target,
            Path = UrlDecodePath(path),
            QueryString = query,
            Query = ParseQuery(query),
            Headers = headers,
            Body = body ?? Array.Empty<byte>(),
        };
    }

    /// <summary>解析 <c>a=1&amp;b=2</c>；重复键取最后一个。</summary>
    public static Dictionary<string, string> ParseQuery(string? query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query)) return result;
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var key = eq >= 0 ? pair[..eq] : pair;
            var value = eq >= 0 ? pair[(eq + 1)..] : "";
            var decodedKey = UrlDecode(key);
            if (decodedKey.Length == 0) continue;
            result[decodedKey] = UrlDecode(value);
        }
        return result;
    }

    /// <summary>URL 解码（+ 视为空格）；非法百分号编码按字面保留，不抛异常。</summary>
    public static string UrlDecode(string value)
    {
        if (string.IsNullOrEmpty(value) || value.IndexOf('%') < 0 && value.IndexOf('+') < 0) return value;
        var bytes = new List<byte>(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (ch == '+') { bytes.Add((byte)' '); continue; }
            if (ch == '%' && i + 2 < value.Length &&
                Uri.IsHexDigit(value[i + 1]) && Uri.IsHexDigit(value[i + 2]))
            {
                bytes.Add((byte)Convert.ToInt32(value.Substring(i + 1, 2), 16));
                i += 2;
                continue;
            }
            var encoded = Encoding.UTF8.GetBytes(ch.ToString());
            bytes.AddRange(encoded);
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    /// <summary>路径解码；拒绝 <c>..</c> 段与 NUL，防目录穿越。</summary>
    public static string UrlDecodePath(string path)
    {
        var decoded = UrlDecode(path);
        if (decoded.Contains('\0')) throw new HttpFormatException("Bad path.");
        foreach (var segment in decoded.Split('/', '\\'))
        {
            if (segment == "..") throw new HttpFormatException("Bad path.");
        }
        return decoded;
    }

    /// <summary>构造响应报文。HEAD 请求由调用方传 <paramref name="includeBody"/>=false。</summary>
    public static byte[] BuildResponse(
        int status, string reason, string? contentType, byte[]? body,
        IReadOnlyDictionary<string, string>? headers = null, bool includeBody = true)
    {
        var payload = body ?? Array.Empty<byte>();
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 ").Append(status).Append(' ').Append(reason).Append("\r\n");
        if (!string.IsNullOrEmpty(contentType))
            sb.Append("Content-Type: ").Append(contentType).Append("\r\n");
        sb.Append("Content-Length: ").Append(payload.Length).Append("\r\n");
        sb.Append("Connection: close\r\n");
        if (headers is not null)
        {
            foreach (var (name, value) in headers)
            {
                if (string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "Connection", StringComparison.OrdinalIgnoreCase)) continue;
                sb.Append(name).Append(": ").Append(value).Append("\r\n");
            }
        }
        sb.Append("\r\n");
        var head = Encoding.UTF8.GetBytes(sb.ToString());
        if (!includeBody || payload.Length == 0) return head;
        var result = new byte[head.Length + payload.Length];
        head.CopyTo(result, 0);
        payload.CopyTo(result, head.Length);
        return result;
    }

    /// <summary>简单 JSON 响应体。</summary>
    public static byte[] Json(string json) => Encoding.UTF8.GetBytes(json);

    /// <summary>
    /// 解析 multipart/form-data。返回 false 表示 Content-Type 不含 boundary 或结构不完整。
    /// 分片数据原样返回（不做解码），由调用方按字段名取值。
    /// </summary>
    public static bool TryParseMultipart(byte[] body, string? contentType, out List<MultipartPart> parts)
    {
        parts = new List<MultipartPart>();
        if (body.Length == 0 || string.IsNullOrWhiteSpace(contentType)) return false;
        var boundary = ExtractBoundary(contentType);
        if (string.IsNullOrEmpty(boundary)) return false;

        var delimiter = Encoding.ASCII.GetBytes("--" + boundary);
        var positions = new List<int>();
        var index = 0;
        while (index <= body.Length - delimiter.Length)
        {
            var found = IndexOf(body, delimiter, index);
            if (found < 0) break;
            positions.Add(found);
            index = found + delimiter.Length;
        }
        if (positions.Count < 2) return false;

        for (var p = 0; p < positions.Count - 1; p++)
        {
            var start = positions[p] + delimiter.Length;
            // 结束分隔符 --boundary--
            if (start + 1 < body.Length && body[start] == (byte)'-' && body[start + 1] == (byte)'-') break;
            if (start + 1 < body.Length && body[start] == (byte)'\r' && body[start + 1] == (byte)'\n') start += 2;
            else if (start < body.Length && body[start] == (byte)'\n') start += 1;
            else return false;

            var partEnd = positions[p + 1];
            var slice = new byte[partEnd - start];
            Buffer.BlockCopy(body, start, slice, 0, slice.Length);
            var part = ParsePart(slice);
            if (part is null) return false;
            parts.Add(part);
        }
        return parts.Count > 0;
    }

    private static MultipartPart? ParsePart(byte[] slice)
    {
        var headEnd = FindHeadEnd(slice);
        if (headEnd < 0) return null;
        var head = Encoding.UTF8.GetString(slice, 0, headEnd);
        var dataStart = headEnd + 4;
        var dataLength = Math.Max(0, slice.Length - dataStart - 2); // 去掉结尾 \r\n
        var data = new byte[dataLength];
        if (dataLength > 0) Buffer.BlockCopy(slice, dataStart, data, 0, dataLength);

        string? name = null, fileName = null, partType = null;
        foreach (var line in head.Replace("\r\n", "\n").Split('\n'))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var headerName = line[..colon].Trim();
            var headerValue = line[(colon + 1)..].Trim();
            if (string.Equals(headerName, "Content-Disposition", StringComparison.OrdinalIgnoreCase))
            {
                name = Attribute(headerValue, "name");
                fileName = Attribute(headerValue, "filename");
            }
            else if (string.Equals(headerName, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                partType = headerValue;
            }
        }
        if (string.IsNullOrEmpty(name)) return null;
        return new MultipartPart { Name = name, FileName = fileName, ContentType = partType, Data = data };
    }

    private static string? Attribute(string header, string name)
    {
        var marker = name + "=\"";
        var at = header.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return null;
        var start = at + marker.Length;
        var end = header.IndexOf('"', start);
        return end < 0 ? null : header[start..end];
    }

    private static string? ExtractBoundary(string contentType)
    {
        if (!contentType.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase)) return null;
        foreach (var piece in contentType.Split(';'))
        {
            var trimmed = piece.Trim();
            if (!trimmed.StartsWith("boundary=", StringComparison.OrdinalIgnoreCase)) continue;
            var value = trimmed["boundary=".Length..].Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') value = value[1..^1];
            return value.Length is > 0 and <= 70 ? value : null;
        }
        return null;
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int start)
    {
        for (var i = start; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] == needle[j]) continue;
                match = false;
                break;
            }
            if (match) return i;
        }
        return -1;
    }
}
