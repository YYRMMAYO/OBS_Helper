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
}
