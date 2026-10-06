using OBS_Helper.Wpf.Localization;
using System.Text;
using System.Text.RegularExpressions;

namespace OBS_Helper.Wpf.Services.Obs;

/// <summary>
/// 日志脱敏器。
///
/// OBS 日志里可能出现推流密钥、账号邮箱、家庭宽带公网 IP、Windows 用户名等
/// 个人信息。凡是要展示给用户之外的对象（尤其是「复制到剪贴板」和「发给云端
/// AI」两条路径），都必须先经过这里。
///
/// 实现原则：
/// <list type="bullet">
///   <item><b>宁可多脱一点</b>：误伤一段无关字符串，代价远小于泄露一个推流密钥。</item>
///   <item><b>保留可诊断性</b>：只替换敏感片段，保留行号、时间戳、错误码和上下文，
///         脱敏后的日志依然能用来定位问题。</item>
///   <item><b>纯函数</b>：不依赖任何服务，便于单元测试覆盖。</item>
/// </list>
///
/// V3.0.0（F4）在不改变任何脱敏结论的前提下减少正则调用：所有规则加
/// <see cref="RegexOptions.Compiled"/>，并在逐条替换之前用一遍字符扫描做廉价短路。
/// </summary>
public static class LogSanitizer
{
    private static string Mask => Strings.T("sanitizer.mask");

    /// <summary>
    /// 规则共用的匹配选项。加 <see cref="RegexOptions.Compiled"/>：这些正则都是静态字段、
    /// 每行日志都要跑一遍，编译成本一次摊完，换来的是每行少一轮解释执行。
    /// </summary>
    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    // rtmp(s)/srt/rist 推流地址：保留协议与主机，抹掉后面的应用名与串流密钥
    private static readonly Regex StreamUrl = new(
        @"\b(rtmps?|srt|rist|ws|wss|http|https)://([^\s/:]+)(?::\d+)?(/\S*)?",
        Opts);

    // key=xxx / streamkey: xxx / token=xxx / password=xxx / secret=xxx
    // V3.0.0（B1）补上 passphrase：SRT 的加密口令写作 passphrase=xxx，
    // 而 "srt://host:port?passphrase=xxx" 这种没有路径的写法正好从 StreamUrl 的
    // 路径组下面溜过去（路径组只吃以 / 开头的部分，query 原样保留），口令会整段留在日志里。
    private static readonly Regex KeyValueSecret = new(
        @"\b(stream[_-]?key|key|token|passwd|password|passphrase|secret|auth|api[_-]?key|bearer)\b\s*[:=]\s*[""']?([^\s""',;)]+)",
        Opts);

    // Authorization: Bearer <token>（V3.0.0 B1）：bearer 后面跟的是空格而不是 =/:，
    // 上面的键值规则匹配不上，于是 HTTP 头里那张现成的访问令牌会整段留下。
    // 捕获组 1 是 "Bearer " 前缀（连同空格），替换时保留它，只抹掉后面的令牌；
    // JWT 那种 "xxx.yyy.zzz" 的点分三段式也在被抹掉的那一段里。
    private static readonly Regex BearerToken = new(
        @"\b(bearer\s+)[""']?([^\s""',;)]+)",
        Opts);

    // 邮箱
    private static readonly Regex Email = new(
        @"\b[\w.+-]+@[\w-]+\.[\w.-]{2,}\b", Opts);

    // MAC 地址
    private static readonly Regex Mac = new(
        @"\b(?:[0-9a-f]{2}[:-]){5}[0-9a-f]{2}\b", Opts);

    // IPv4（本机/未指定地址保留，方便判断是不是连的本地）
    private static readonly Regex Ipv4 = new(
        @"\b(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})\b", Opts);

    // 公网 IPv6 的「候选段」（V3.0.0 B1）：一段只由十六进制字符与冒号组成、且至少含两个冒号的
    // 连续串。真正是不是地址、要不要保留，交给 IPAddress.TryParse 与 IsPublicIpv6 判定 ——
    // 正则只负责圈出可能是地址的一段，宁可多圈也不能漏。
    // 两侧的零宽断言只排除「词字符」：左边允许紧跟冒号（"host:2001:db8::1" 这种就是靠它才圈得到），
    // 右边允许紧跟小数点或括号（句末、"[…]" 里的地址同样要被抹掉）。左边粘了十六进制字符时
    // 整段会解析失败，那种情况由 MaskPublicIpv6 右移重试兜住。
    private static readonly Regex Ipv6Candidate = new(
        @"(?<!\w)(?=[0-9a-fA-F:]*:[0-9a-fA-F:]*:)[0-9a-fA-F:]{2,45}(?![\w:])",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // 标准 Base64 令牌（V3.0.0 B1）：LongToken 的字符集里没有 + / =，一段标准 Base64 会被它
    // 按「字母数字段」拆成若干碎片，碎片一短于 24 位就整个漏掉。这里用完整字符集整体匹配（≥24 位），
    // 并要求至少出现一个 + / =：纯字母数字的长串继续走 LongToken，连同它的白名单一起保留。
    // 必须排在 LongToken 之前 —— 否则长串已经被拆碎，这里就再也匹配不到完整令牌了。
    private static readonly Regex Base64Token = new(
        @"(?<![A-Za-z0-9+/=])(?=[A-Za-z0-9+/=]*[+/=])[A-Za-z0-9+/=]{24,}(?![A-Za-z0-9+/=])",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // Windows 用户目录
    private static readonly Regex WinUserPath = new(
        @"([A-Za-z]:\\Users\\)([^\\\s""']+)", Opts);

    // macOS / Linux 用户目录
    private static readonly Regex UnixUserPath = new(
        @"(/Users/|/home/)([^/\s""']+)", Opts);

    // 长串十六进制 / base64 样式的令牌（>= 24 位），常见于串流密钥
    private static readonly Regex LongToken = new(
        @"\b[A-Za-z0-9_\-]{24,}\b", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>长串令牌的长度下限（LongToken 与 Base64Token 里的量词都取这个值）。</summary>
    private const int TokenRunLength = 24;

    /// <summary>这些「长串」是 OBS 日志里的正常内容，不应被当成密钥抹掉。</summary>
    private static readonly string[] TokenAllowList =
    {
        "obs-studio", "libobs", "OBSBasic", "Microsoft", "NVIDIA", "AMD", "Intel",
        "GeForce", "Radeon", "Direct3D", "WindowsGraphicsCapture", "monitor_capture",
        "window_capture", "game_capture", "dshow_input", "wasapi_output_capture",
        "wasapi_input_capture", "browser_source", "text_gdiplus", "ffmpeg_muxer",
        "obs_x264", "jim_nvenc", "obs_qsv11", "h264_texture_amf", "av1_texture_amf",
        "screen_capture", "coreaudio_input_capture", "syphon-input", "mac-capture"
    };

    /// <summary>对整段日志做脱敏。</summary>
    public static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var sb = new StringBuilder(text.Length);
        foreach (var line in SplitLines(text))
        {
            sb.Append(SanitizeLine(line));
            sb.Append('\n');
        }
        // 去掉最后多加的换行
        if (sb.Length > 0 && sb[^1] == '\n') sb.Length--;
        return sb.ToString();
    }

    /// <summary>对单行做脱敏。顺序很重要：先处理结构化的，再处理泛化的。</summary>
    public static string SanitizeLine(string line)
    {
        if (string.IsNullOrEmpty(line)) return line;

        // V3.0.0（F4）廉价预筛：连一个「规则触发字符」都没有的行不可能被任何规则命中，原样返回。
        // 这一遍字符扫描无分配、无回溯，比让 11 条正则各跑一遍便宜一个数量级。
        var triggers = ScanTriggers(line);
        if (triggers == Triggers.None) return line;

        // 1) key=value 形式的密钥（最精确，先处理）
        if ((triggers & (Triggers.Colon | Triggers.Equal)) != 0)
            line = KeyValueSecret.Replace(line, m => $"{m.Groups[1].Value}={Mask}");

        // 2) Authorization: Bearer <token>：必须排在长串规则之前，否则 JWT 会先被 LongToken
        //    拆成几段，短的那几段就留在原处了。这里的 Contains 是绕过正则的快速否决。
        if (line.Contains("bearer", StringComparison.OrdinalIgnoreCase))
            line = BearerToken.Replace(line, m => m.Groups[1].Value + Mask);

        // 3) 推流 / 服务地址：保留主机名，抹掉路径（串流密钥通常在路径里）
        if ((triggers & Triggers.Scheme) != 0)
            line = StreamUrl.Replace(line, m =>
            {
                var scheme = m.Groups[1].Value;
                var host = m.Groups[2].Value;
                var path = m.Groups[3].Value;
                // 本地回环地址（跟 OBS 的 websocket 连接）完整保留，方便排查连接问题
                if (IsLoopbackHost(host)) return m.Value;
                return string.IsNullOrEmpty(path) || path == "/"
                    ? $"{scheme}://{host}"
                    : $"{scheme}://{host}/{Mask}";
            });

        // 4) 邮箱 / MAC
        if ((triggers & Triggers.At) != 0) line = Email.Replace(line, Mask);
        if ((triggers & (Triggers.Colon | Triggers.Dash)) != 0) line = Mac.Replace(line, Mask);

        // 5) 用户名路径
        if ((triggers & Triggers.Backslash) != 0)
            line = WinUserPath.Replace(line, m => m.Groups[1].Value + Strings.T("sanitizer.user"));
        if ((triggers & Triggers.Slash) != 0)
            line = UnixUserPath.Replace(line, m => m.Groups[1].Value + Strings.T("sanitizer.user"));

        // 6) 公网 IPv4（保留私网与回环，它们对排查网络问题有用且不算隐私）
        if ((triggers & Triggers.Dot) != 0)
            line = Ipv4.Replace(line, m => IsPrivateOrLoopbackIpv4(m) ? m.Value : "[IP]");

        // 7) 公网 IPv6（V3.0.0 B1）：与 IPv4 同口径 —— 回环 ::1、链路本地 fe80::/10、
        //    唯一本地 fc00::/7 都保留（它们说明「连的是本机或局域网」，对排障有用且不算隐私）。
        if ((triggers & Triggers.Colon) != 0)
            line = Ipv6Candidate.Replace(line, MaskPublicIpv6);

        // 8) 标准 Base64 令牌与剩下的超长令牌（长串类规则必须最后跑）
        if ((triggers & Triggers.LongRun) != 0)
        {
            line = Base64Token.Replace(line, Mask);
            line = LongToken.Replace(line, m => IsAllowedToken(m.Value) ? m.Value : Mask);
        }

        return line;
    }

    /// <summary>
    /// 一行里出现的「规则触发字符」（V3.0.0 F4）。
    ///
    /// 每条规则都有必须存在的字面字符（推流地址必须有 "://"、邮箱必须有 '@'……），
    /// 据此可以在调用正则之前先短路。标记只会多报、不会漏报，所以短路最多让某条正则
    /// 白跑一次，绝不会放过本该脱敏的内容。
    /// </summary>
    private enum Triggers
    {
        None = 0,
        Colon = 1 << 0,      // ':'   → 键值 / MAC / IPv6 候选
        Equal = 1 << 1,      // '='   → 键值
        Dash = 1 << 2,       // '-'   → MAC 的连字符写法
        At = 1 << 3,         // '@'   → 邮箱
        Dot = 1 << 4,        // '.'   → IPv4
        Backslash = 1 << 5,  // '\'   → Windows 用户目录
        Slash = 1 << 6,      // '/'   → Unix 用户目录 / 标准 Base64
        Scheme = 1 << 7,     // "://" → 推流地址
        LongRun = 1 << 8,    // ≥24 个令牌字符 → 长串 / 标准 Base64
    }

    /// <summary>
    /// 一遍扫出触发字符（无分配、无回溯）。
    ///
    /// 刻意把 ':' 也算进去：只看 "://"、"@" 和长串的精简版会把 "token: xxx"、
    /// Windows 用户目录、MAC 的连字符写法整类放过 —— 那不是优化，是漏脱敏。
    ///
    /// 扫描的是<b>原始行</b>：脱敏只会把命中片段换成不含这些字符的掩码（或原样重发匹配到的子串），
    /// 不可能凭空造出一个触发字符，所以原始行的标记对后续每一步都是超集。
    /// </summary>
    private static Triggers ScanTriggers(string line)
    {
        var triggers = Triggers.None;
        var run = 0;
        char prev1 = '\0', prev2 = '\0';

        foreach (var ch in line)
        {
            switch (ch)
            {
                case ':': triggers |= Triggers.Colon; break;
                case '=': triggers |= Triggers.Equal; break;
                case '-': triggers |= Triggers.Dash; break;
                case '@': triggers |= Triggers.At; break;
                case '.': triggers |= Triggers.Dot; break;
                case '\\': triggers |= Triggers.Backslash; break;
                case '/':
                    triggers |= Triggers.Slash;
                    // "://" 是推流地址规则的必要条件，只有它出现才值得跑那条正则
                    if (prev2 == ':' && prev1 == '/') triggers |= Triggers.Scheme;
                    break;
            }

            // 长串与 Base64 的长度下限：字符集取两者的并集，凑够就放行这两条规则
            if (IsTokenChar(ch))
            {
                if (++run >= TokenRunLength) triggers |= Triggers.LongRun;
            }
            else
            {
                run = 0;
            }

            prev2 = prev1;
            prev1 = ch;
        }

        return triggers;
    }

    /// <summary>长串令牌字符集的并集（LongToken ∪ 标准 Base64），只用于预筛长度。</summary>
    private static bool IsTokenChar(char c)
        => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9')
            or '_' or '-' or '+' or '/' or '=';

    private static bool IsLoopbackHost(string host)
        => host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || host == "127.0.0.1"
        || host == "::1"
        || host == "[::1]";

    private static bool IsPrivateOrLoopbackIpv4(Match m)
    {
        if (!int.TryParse(m.Groups[1].Value, out var a) ||
            !int.TryParse(m.Groups[2].Value, out var b) ||
            !int.TryParse(m.Groups[3].Value, out var c) ||
            !int.TryParse(m.Groups[4].Value, out var d))
            return true; // 解析不出来说明不是 IP（比如版本号），保留原样

        if (a > 255 || b > 255 || c > 255 || d > 255) return true; // 版本号之类

        if (a == 127 || a == 10 || a == 0) return true;
        if (a == 192 && b == 168) return true;
        if (a == 172 && b >= 16 && b <= 31) return true;
        if (a == 169 && b == 254) return true;
        if (a == 255) return true; // 子网掩码
        return false;
    }

    /// <summary>
    /// 这段文本是不是「需要抹掉的公网 IPv6」。不是地址、或属于回环 / 链路本地 / 唯一本地
    /// 的都返回 false（保留原文），保留口径与上面的 IPv4 规则一致。
    /// </summary>
    private static bool IsPublicIpv6(System.Net.IPAddress ip)
    {
        if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6) return false;
        if (System.Net.IPAddress.IsLoopback(ip)) return false;     // ::1
        if (ip.IsIPv6LinkLocal) return false;                      // fe80::/10
        if (ip.Equals(System.Net.IPAddress.IPv6Any)) return false;  // ::（未指定地址，等同 IPv4 的 0.0.0.0）
        return (ip.GetAddressBytes()[0] & 0xFE) != 0xFC;           // fc00::/7（唯一本地地址）保留
    }

    /// <summary>
    /// IPv6 候选的替换（V3.0.0 B1）。候选左边可能粘着十六进制字符（哈希片段、MAC 尾巴之类，
    /// 光看字符集分不出来），那种情况下整段解析失败，于是逐字右移再试，直到剩下的部分是一个
    /// 合法地址：私有就整段保留，公网就把地址那一段抹掉、前缀原样留下。
    /// </summary>
    private static string MaskPublicIpv6(Match m)
    {
        var text = m.Value;

        // 整段就是一个地址：公网抹掉，私有 / 回环保留（这里不做右移，避免把 "fd00::1"
        // 这种合法私有地址从中间切开、反而把后半截 "d00::1" 当成公网地址抹了）。
        if (System.Net.IPAddress.TryParse(text, out var ip))
            return IsPublicIpv6(ip) ? "[IP]" : text;

        // 合法 IPv6 要么含 "::"，要么是 8 组（7 个冒号）；都不满足就不必再试。
        // OBS 每行都有的时间戳 "19:41:42" 正是靠这一步免于逐个后缀做解析。
        if (text.IndexOf("::", StringComparison.Ordinal) < 0 && CountColons(text) < 7) return text;

        for (var i = 1; i < text.Length; i++)
        {
            if (!System.Net.IPAddress.TryParse(text[i..], out var sub)) continue;
            if (sub.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6) continue;
            return IsPublicIpv6(sub) ? text[..i] + "[IP]" : text;
        }
        return text;
    }

    private static int CountColons(string s)
    {
        var n = 0;
        foreach (var c in s)
        {
            if (c == ':') n++;
        }
        return n;
    }

    private static bool IsAllowedToken(string token)
    {
        // 纯数字（时间戳、字节数）不是密钥
        if (token.All(Services.Compat.Compat.IsAsciiDigit)) return true;

        // 版本号 / 已知标识符
        foreach (var allowed in TokenAllowList)
        {
            if (token.Contains(allowed, StringComparison.OrdinalIgnoreCase)) return true;
        }

        // 全是字母且含有明显的英文单词分隔（下划线/连字符占比高）→ 多半是标识符而非密钥
        var separators = token.Count(ch => ch is '_' or '-');
        if (separators >= 2 && !token.Any(Services.Compat.Compat.IsAsciiDigit)) return true;

        return false;
    }

    /// <summary>按行切分，不为整份日志分配一个巨大的字符串数组。</summary>
    public static IEnumerable<string> SplitLines(string text)
    {
        int start = 0;
        while (start <= text.Length)
        {
            int idx = text.IndexOf('\n', start);
            if (idx < 0)
            {
                if (start < text.Length) yield return text[start..].TrimEnd('\r');
                yield break;
            }
            yield return text[start..idx].TrimEnd('\r');
            start = idx + 1;
        }
    }
}
