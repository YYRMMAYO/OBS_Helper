using System.IO;
using OBS_Helper.Wpf.Services.Subtitles;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>X1031Y3：whisper 推荐模型的固定 URL / 摘要 / 体积与安装判定。</summary>
public class WhisperModelCoreTests
{
    [Theory]
    [InlineData("huggingface.co", true)]
    [InlineData("cdn-lfs.huggingface.co", true)]
    [InlineData("cas-bridge.xethub.hf.co", true)]
    [InlineData("evil.example.com", false)]
    [InlineData("evil-huggingface.co", false)]
    [InlineData(null, false)]
    public void IsAllowedHost_AllowsOnlyWhisperModelHosts(string? host, bool expected)
        => Assert.Equal(expected, WhisperModelCore.IsAllowedHost(host));

    [Fact]
    public void ModelUrlAndDigestArePinned()
    {
        Assert.StartsWith("https://huggingface.co/", WhisperModelCore.DownloadUrl);
        Assert.Contains("ggerganov/whisper.cpp", WhisperModelCore.DownloadUrl);
        Assert.Equal(64, WhisperModelCore.ExpectedSha256.Length);
        Assert.True(WhisperModelCore.ExpectedSize > 100_000_000);
        Assert.True(WhisperModelCore.ExpectedSize < WhisperModelCore.MaxBytes);
    }

    [Fact]
    public void IsInstalled_RejectsMissingAndWrongSizeFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "obshelper-model-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, WhisperModelCore.FileName);
            Assert.False(WhisperModelCore.IsInstalled(file));
            File.WriteAllBytes(file, new byte[1]);
            Assert.False(WhisperModelCore.IsInstalled(file));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* 忽略 */ }
        }
    }
}
