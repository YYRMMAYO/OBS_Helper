using OBS_Helper.Wpf.Services.Diagnostics;

namespace OBS_Helper.Wpf.Tests;

/// <summary>诊断包纯逻辑（V3.6.0 / G08）：必须含版本/自检，且默认不带路径与密钥。</summary>
public class DiagnosticPackageCoreTests
{
    [Fact]
    public void BuildMarkdown_RedactsPathsAndSecrets()
    {
        var input = new DiagnosticPackageInput
        {
            AppVersion = "3.6.0",
            OsDescription = "Windows 10.0.19045",
            GeneratedAt = new DateTime(2026, 11, 15, 12, 30, 0),
            ObsState = "Failed",
            ObsLastError = "connect C:\\Users\\alice\\obs.txt failed",
            Recording = false,
            Streaming = true,
            Checks = new[] { new DiagnosticCheck("录像格式", "通过", "mkv") },
            RecentToasts = new[] { "stream_key=abc123" },
            LogTail = "C:\\Users\\alice\\secret.txt\nAuthorization: Bearer eyJhbGciOiJIUzI1NiJ9.payload.sig\nerror at D:\\recordings\\a.mkv",
        };

        var text = DiagnosticPackageCore.BuildMarkdown(input);

        Assert.Contains("3.6.0", text);
        Assert.Contains("录像格式", text);
        Assert.Contains("日志尾部（已脱敏）", text);
        Assert.DoesNotContain("alice", text);
        Assert.DoesNotContain("abc123", text);
        Assert.DoesNotContain("eyJhbGci", text);
        Assert.DoesNotContain("C:\\Users", text);
        Assert.Contains("<path>", text);
    }

    [Fact]
    public void SanitizeForDiagnostics_HandlesNullAndEmpty()
    {
        Assert.Equal("", DiagnosticPackageCore.SanitizeForDiagnostics(null));
        Assert.Equal("", DiagnosticPackageCore.SanitizeForDiagnostics(""));
    }

    [Fact]
    public void BuildFileName_UsesTimestamp()
    {
        var name = DiagnosticPackageCore.BuildFileName(new DateTime(2026, 11, 15, 12, 30, 45));
        Assert.Equal("diagnostic_20261115_123045.md", name);
    }
}
