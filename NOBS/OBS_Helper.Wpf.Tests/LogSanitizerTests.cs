using OBS_Helper.Wpf.Services.Obs;

namespace OBS_Helper.Wpf.Tests;

public class LogSanitizerTests
{
    private const string Mask = "[已隐藏]";

    [Theory]
    [InlineData("streamkey=abc123secret")]
    [InlineData("token: abc123secret")]
    [InlineData("password=hunter2")]
    [InlineData("api_key = sk-abcdef123456")]
    public void KeyValueSecret_IsMasked(string line)
    {
        var output = LogSanitizer.SanitizeLine(line);
        Assert.Contains(Mask, output);
        Assert.DoesNotContain("abc123secret", output);
        Assert.DoesNotContain("hunter2", output);
    }

    [Fact]
    public void StreamUrl_PathMasked_HostKept()
    {
        var output = LogSanitizer.SanitizeLine("rtmp://live.example.com/live/streamkey123456");
        Assert.StartsWith("rtmp://live.example.com/", output);
        Assert.EndsWith(Mask, output);
    }

    [Fact]
    public void StreamUrl_Loopback_KeptVerbatim()
    {
        const string line = "ws://127.0.0.1:4455";
        Assert.Equal(line, LogSanitizer.SanitizeLine(line));
    }

    [Fact]
    public void Email_IsMasked()
    {
        var output = LogSanitizer.SanitizeLine("mail: someone@example.com");
        Assert.Contains(Mask, output);
        Assert.DoesNotContain("someone@example.com", output);
    }

    [Fact]
    public void Mac_IsMasked()
    {
        var output = LogSanitizer.SanitizeLine("mac=aa:bb:cc:dd:ee:ff");
        Assert.Contains(Mask, output);
    }

    [Theory]
    [InlineData("8.8.8.8", "[IP]")]
    [InlineData("203.0.113.7", "[IP]")]
    [InlineData("192.168.1.10", "192.168.1.10")]
    [InlineData("127.0.0.1", "127.0.0.1")]
    [InlineData("10.0.0.5", "10.0.0.5")]
    public void Ipv4_PublicMasked_PrivateKept(string ip, string expected)
    {
        Assert.Equal("ip=" + expected, LogSanitizer.SanitizeLine("ip=" + ip));
    }

    [Fact]
    public void WindowsUserPath_UsernameMasked()
    {
        var output = LogSanitizer.SanitizeLine(@"C:\Users\Alice\AppData");
        Assert.Equal(@"C:\Users\[用户]\AppData", output);
    }

    [Fact]
    public void LongToken_Masked_WhenNotAllowListed()
    {
        Assert.Equal(Mask, LogSanitizer.SanitizeLine("abcdefghijklmnopqrstuvwxyz123456"));
    }

    [Fact]
    public void LongToken_AllowListed_Kept()
    {
        Assert.Equal("obs-studio", LogSanitizer.SanitizeLine("obs-studio"));
        Assert.Equal("NVIDIA GeForce", LogSanitizer.SanitizeLine("NVIDIA GeForce"));
    }

    [Fact]
    public void Sanitize_WholeLog_PreservesLineCount()
    {
        var input = "line1\nstreamkey=SECRET\nline3";
        var output = LogSanitizer.Sanitize(input);
        Assert.Equal(3, output.Split('\n').Length);
        Assert.DoesNotContain("SECRET", output);
    }

    [Fact]
    public void Sanitize_EmptyAndNull_ReturnsEmpty()
    {
        Assert.Equal("", LogSanitizer.Sanitize(null));
        Assert.Equal("", LogSanitizer.Sanitize(""));
    }

    // ------------------ V3.0.0 B1：四类脱敏补漏（SRT 口令 / 公网 IPv6 / 标准 Base64 / Bearer） ------------------

    /// <summary>
    /// SRT 口令：<c>passphrase=</c> / <c>passphrase:</c>。
    /// 用「没有路径」的写法，因为 <c>srt://host:port?passphrase=xxx</c> 里 query 不归
    /// StreamUrl 的路径组管，旧实现会把口令原样留下；口令又往往短于 24 位，长串规则也接不住。
    /// </summary>
    [Theory]
    [InlineData("srt://ingest.example.com:9000?passphrase=ShortPw12", "ShortPw12")]
    [InlineData("srt://ingest.example.com:9000?passphrase=Ab3xY9zQ", "Ab3xY9zQ")]
    [InlineData("passphrase: Ab3xY9zQwEr", "Ab3xY9zQwEr")]
    [InlineData("passphrase=Zx9Qw2Er", "Zx9Qw2Er")]
    public void Passphrase_IsMasked_EvenWhenShort(string line, string secret)
    {
        var output = LogSanitizer.SanitizeLine(line);
        Assert.Contains(Mask, output);
        Assert.DoesNotContain(secret, output);
    }

    /// <summary>公网 IPv6 抹掉；回环 / 链路本地 / 唯一本地与 IPv4 同口径保留。</summary>
    [Theory]
    [InlineData("2001:db8::1", "[IP]")]
    [InlineData("2400:cb00:2048:1::c629:d7a2", "[IP]")]
    [InlineData("::1", "::1")]
    [InlineData("fe80::a1b2:c3d4", "fe80::a1b2:c3d4")]
    [InlineData("fc00::1", "fc00::1")]
    [InlineData("fd12:3456:789a::1", "fd12:3456:789a::1")]
    public void Ipv6_PublicMasked_LoopbackAndPrivateKept(string ip, string expected)
    {
        Assert.Equal("ip=" + expected, LogSanitizer.SanitizeLine("ip=" + ip));
    }

    /// <summary>
    /// IPv6 规则不能误伤 OBS 每行都带的时间戳（<c>19:41:42.564: </c>）：
    /// 日志头部解析全部锚定在行首时间戳上，一旦被抹掉，CPU / 内存 / 帧率会集体解析失败。
    /// </summary>
    [Fact]
    public void Ipv6Rule_DoesNotTouchTimestampPrefix()
    {
        const string line = "19:41:42.564: CPU Name: AMD Ryzen 9 3900X";
        Assert.Equal(line, LogSanitizer.SanitizeLine(line));
    }

    /// <summary>
    /// 标准 Base64（含 + / =）必须整段抹掉：长串规则的字符集不含这三个字符，
    /// 会把令牌拆碎成若干短片段，短片段又够不到 24 位下限，于是漏脱敏。
    /// </summary>
    [Fact]
    public void Base64Token_MaskedWhole_NotSplitIntoFragments()
    {
        const string token = "QUJDREVGR0hJSktMTU5PUFFSU1RVVldYWVo+/=";
        Assert.Equal("blob " + Mask + " end", LogSanitizer.SanitizeLine("blob " + token + " end"));
    }

    /// <summary>Bearer 后面是空格，键值规则匹配不上；短令牌与 JWT 三段式都要求被抹掉。</summary>
    [Theory]
    [InlineData("Authorization: Bearer abc123", "abc123")]
    [InlineData("authorization: bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.sig", "eyJhbGciOiJIUzI1NiJ9")]
    public void BearerToken_IsMasked(string line, string secret)
    {
        var output = LogSanitizer.SanitizeLine(line);
        Assert.Contains(Mask, output);
        Assert.DoesNotContain(secret, output);
    }
}
