using OBS_Helper.Client.Services.Obs;

namespace OBS_Helper.Tests;

public class LogSanitizerTests
{
    [Fact]
    public void KeyValueSecret_IsMasked()
    {
        var outLine = LogSanitizer.SanitizeLine("password=supersecret123");
        Assert.DoesNotContain("supersecret123", outLine);
        Assert.Contains("password=[已隐藏]", outLine);
    }

    [Fact]
    public void StreamUrl_HostPreserved_PathMasked()
    {
        var outLine = LogSanitizer.SanitizeLine("rtmp://live.example.com/app/streamkey123");
        Assert.Contains("rtmp://live.example.com", outLine);
        Assert.DoesNotContain("streamkey123", outLine);
        Assert.Contains("[已隐藏]", outLine);
    }

    [Fact]
    public void StreamUrl_LoopbackFullyPreserved()
    {
        var outLine = LogSanitizer.SanitizeLine("ws://127.0.0.1:4455");
        Assert.Equal("ws://127.0.0.1:4455", outLine);
    }

    [Fact]
    public void Email_IsMasked()
    {
        var outLine = LogSanitizer.SanitizeLine("联系 me@example.com 获取支持");
        Assert.DoesNotContain("me@example.com", outLine);
        Assert.Contains("[已隐藏]", outLine);
    }

    [Fact]
    public void PublicIp_IsMasked_PrivateIpPreserved()
    {
        var pub = LogSanitizer.SanitizeLine("connected to 203.0.113.45:1935");
        Assert.Contains("[IP]", pub);

        var priv = LogSanitizer.SanitizeLine("local 192.168.1.10 ok");
        Assert.Contains("192.168.1.10", priv);
    }

    [Fact]
    public void MacAddress_IsMasked()
    {
        var outLine = LogSanitizer.SanitizeLine("mac 00:1B:44:11:3A:B7 detected");
        Assert.DoesNotContain("00:1B:44:11:3A:B7", outLine);
        Assert.Contains("[已隐藏]", outLine);
    }

    [Fact]
    public void LongRandomToken_IsMasked()
    {
        var token = "aZ3kP9qW2mX8vL5nB7tR4cY1uD6eF0hJ";
        var outLine = LogSanitizer.SanitizeLine("key=" + token);
        Assert.DoesNotContain(token, outLine);
    }

    [Fact]
    public void KnownIdentifier_TokenPreserved()
    {
        var outLine = LogSanitizer.SanitizeLine("loaded obs-studio encoder module");
        Assert.Contains("obs-studio", outLine);
    }

    [Fact]
    public void Sanitize_FullText_KeepsLineStructure()
    {
        var text = "line1 with password=abc\nline2 rtmp://a.com/b/c\nline3 normal";
        var outText = LogSanitizer.Sanitize(text);
        Assert.Equal(3, outText.Split('\n').Length);
        Assert.DoesNotContain("password=abc", outText);
        Assert.Contains("line3 normal", outText);
    }
}
