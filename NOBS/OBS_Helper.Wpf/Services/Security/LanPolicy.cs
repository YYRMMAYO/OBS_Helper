using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace OBS_Helper.Wpf.Services.Security;

/// <summary>
/// 局域网能力的统一安全策略（V3.4.0 / 批次 0）。
///
/// 所有「对外暴露」的功能（手机联动、媒体服务器、远程嘉宾、灯光/硬件回环 API）
/// 都从这里取同一套判定：私网地址、token 生成、常量时间比较、限流、安全响应头。
/// 纯 BCL、无 WPF / 无网络 IO，因此可被单测工程直接链接。
///
/// 关键边界（PLAN_V3.4.0 §6.1 / §6.2）：
/// <list type="bullet">
///   <item>只接受私网来源；公网来源一律拒绝，不做 UPnP、不改防火墙。</item>
///   <item>配对 secret 一次性、TTL 120 秒；会话 token 只存 SHA-256。</item>
///   <item>错误不区分「不存在 / 无权限」，避免被用来探测。</item>
/// </list>
/// </summary>
public static class LanPolicy
{
    /// <summary>配对码有效期（秒）。</summary>
    public const int PairTtlSeconds = 120;

    /// <summary>会话默认有效期（小时）。</summary>
    public const int SessionHours = 12;

    /// <summary>会话空闲过期（分钟）。</summary>
    public const int IdleMinutes = 30;

    /// <summary>每台设备每秒命令上限。</summary>
    public const int CommandsPerSecond = 20;

    /// <summary>最多登记的配对的设备数。</summary>
    public const int MaxDevices = 5;

    /// <summary>最多并发会话（WS/SSE）数。</summary>
    public const int MaxSessions = 3;

    /// <summary>
    /// 是否属于「可以用于局域网能力」的地址。
    /// 回环、RFC1918、链路本地（169.254/16、fe80::/10）、IPv6 ULA（fc00::/7）都算私网；
    /// 其余（含运营商 NAT 的 100.64/10 之外的公网地址）一律不算。
    /// </summary>
    public static bool IsPrivateAddress(IPAddress? address)
    {
        if (address is null) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] switch
            {
                10 => true,
                127 => true,
                169 when b[1] == 254 => true,
                172 when b[1] >= 16 && b[1] <= 31 => true,
                192 when b[1] == 168 => true,
                _ => false,
            };
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            if (address.Equals(IPAddress.IPv6Loopback)) return true;
            var b = address.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC) return true; // fc00::/7
            if (b[0] == 0xFE && (b[1] & 0xC0) == 0x80) return true; // fe80::/10
            return false;
        }

        return false;
    }

    /// <summary>字符串形式的地址是否私网。解析失败一律 false。</summary>
    public static bool IsPrivateAddress(string? address)
        => IPAddress.TryParse((address ?? "").Trim().Trim('[', ']'), out var ip) && IsPrivateAddress(ip);

    /// <summary>
    /// 从本机网卡里挑一个用于局域网服务的地址：优先私网 IPv4（10 &gt; 192.168 &gt; 172.16），
    /// 跳过回环、链路本地、虚拟网卡常见的 169.254。没有可用地址返回 null。
    /// </summary>
    public static IPAddress? PickLanAddress(IEnumerable<IPAddress> candidates)
    {
        var best = (IPAddress?)null;
        var bestRank = int.MaxValue;
        foreach (var ip in candidates)
        {
            if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) continue;
            if (IPAddress.IsLoopback(ip)) continue;
            if (!IsPrivateAddress(ip)) continue;
            var b = ip.GetAddressBytes();
            if (b[0] == 169) continue;
            var rank = b[0] switch
            {
                10 => 0,
                192 when b[1] == 168 => 1,
                172 => 2,
                _ => 3,
            };
            if (rank < bestRank)
            {
                bestRank = rank;
                best = ip;
            }
        }
        return best;
    }

    /// <summary>生成 URL 安全（Base64Url、无填充）的随机 token。</summary>
    public static string NewToken(int bytes = 32)
    {
        if (bytes < 16) throw new ArgumentOutOfRangeException(nameof(bytes), "Token must be at least 128-bit.");
        var buffer = RandomNumberGenerator.GetBytes(bytes);
        return Base64Url(buffer);
    }

    /// <summary>生成 6 位数字短码（用于无法扫码时定位配对会话；仍需 secret 才能完成配对）。</summary>
    public static string NewShortCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    /// <summary>Base64Url（无填充），可安全放进 URL / URL fragment。</summary>
    public static string Base64Url(byte[] data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>SHA-256 哈希的 Base64Url 形式（服务端只存它，不存明文 token）。</summary>
    public static string HashToken(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token ?? ""));
        return Base64Url(hash);
    }

    /// <summary>SHA-256 十六进制（小写），用于 sidecar 哈希校验。</summary>
    public static string Sha256Hex(ReadOnlySpan<byte> data)
        => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    /// <summary>
    /// 常量时间比较。先各自哈希再比较，长度差异不会通过耗时泄露。
    /// </summary>
    public static bool FixedTimeEquals(string? a, string? b)
    {
        if (a is null || b is null) return false;
        var ha = SHA256.HashData(Encoding.UTF8.GetBytes(a));
        var hb = SHA256.HashData(Encoding.UTF8.GetBytes(b));
        return CryptographicOperations.FixedTimeEquals(ha, hb);
    }

    /// <summary>设备名清洗：去控制字符 / 零宽字符，压缩空白，限长 64；空则回退 <c>phone</c>。</summary>
    public static string SanitizeDeviceName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "phone";
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsControl(ch)) continue;
            if (ch is '\u200B' or '\u200C' or '\u200D' or '\uFEFF') continue;
            sb.Append(ch);
        }
        var cleaned = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        if (cleaned.Length > 64) cleaned = cleaned[..64];
        return cleaned.Length == 0 ? "phone" : cleaned;
    }

    /// <summary>
    /// 手机页面 / overlay 的 CSP：不允许任何远程资源、不允许内联脚本之外的执行。
    /// 页面脚本全部同源内联，因此保留 <c>'unsafe-inline'</c>，但 <c>connect-src 'self'</c> 限制回传目标。
    /// </summary>
    public const string PageCsp =
        "default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    /// <summary>overlay CSP：弹幕/字幕 overlay 只允许自身与 data 图片。</summary>
    public const string OverlayCsp =
        "default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; base-uri 'none'; frame-ancestors 'none'";

    /// <summary>每个响应都应带的头。CORS 一律不发（同源页面不需要，也避免被外部网页调用）。</summary>
    public static IReadOnlyDictionary<string, string> SecurityHeaders(string csp) => new Dictionary<string, string>
    {
        ["Content-Security-Policy"] = csp,
        ["X-Content-Type-Options"] = "nosniff",
        ["Referrer-Policy"] = "no-referrer",
        ["Cache-Control"] = "no-store",
        ["X-Frame-Options"] = "DENY",
    };

    /// <summary>令牌桶式限流：按 key（设备 / IP）统计每秒请求数，超出直接拒绝。</summary>
    public sealed class RateLimiter
    {
        private readonly int _limit;
        private readonly TimeSpan _window;
        private readonly Func<DateTimeOffset> _clock;
        private readonly Dictionary<string, Queue<long>> _hits = new(StringComparer.Ordinal);
        private readonly object _gate = new();

        public RateLimiter(int limit = CommandsPerSecond, TimeSpan? window = null, Func<DateTimeOffset>? clock = null)
        {
            _limit = Math.Max(1, limit);
            _window = window ?? TimeSpan.FromSeconds(1);
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
        }

        /// <summary>记一次请求；允许返回 true，超限返回 false（不阻塞）。</summary>
        public bool TryAcquire(string key)
        {
            var now = _clock().UtcTicks;
            lock (_gate)
            {
                if (!_hits.TryGetValue(key, out var queue))
                {
                    queue = new Queue<long>();
                    _hits[key] = queue;
                }
                var cutoff = now - _window.Ticks;
                while (queue.Count > 0 && queue.Peek() <= cutoff) queue.Dequeue();
                if (queue.Count >= _limit) return false;
                queue.Enqueue(now);
                return true;
            }
        }

        /// <summary>清空某 key 的计数（设备撤销 / 重新配对时调用）。</summary>
        public void Reset(string key)
        {
            lock (_gate) _hits.Remove(key);
        }
    }
}
