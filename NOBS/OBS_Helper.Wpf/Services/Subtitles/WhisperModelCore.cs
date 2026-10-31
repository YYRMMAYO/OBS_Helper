using System.IO;

namespace OBS_Helper.Wpf.Services.Subtitles;

/// <summary>
/// whisper.cpp 推荐模型（X1031Y3）：固定文件名 / URL / 体积 / SHA-256。
///
/// 模型权重不在 GitHub Release 里，来自 whisper.cpp 官方 Hugging Face 仓库；
/// URL 与摘要都是代码常量，不接受用户输入。下载后必须匹配 SHA-256 才落盘。
/// </summary>
public static class WhisperModelCore
{
    public const string FileName = "ggml-base.bin";
    public const string DownloadUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin";
    public const long ExpectedSize = 147_951_465;
    public const string ExpectedSha256 = "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe";

    /// <summary>模型体积上限：给异常响应留余量，但不允许无限写盘。</summary>
    public const long MaxBytes = 200L * 1024 * 1024;

    /// <summary>是否指向 whisper.cpp 官方模型托管域（含 LFS/CDN 重定向）。</summary>
    public static bool IsAllowedHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        var h = host.Trim().ToLowerInvariant();
        return h == "huggingface.co"
            || h.EndsWith(".huggingface.co", StringComparison.Ordinal)
            || h == "hf.co"
            || h.EndsWith(".hf.co", StringComparison.Ordinal);
    }

    /// <summary>文件存在且体积精确匹配时视为已安装（完整 SHA-256 在下载时校验）。</summary>
    public static bool IsInstalled(string? path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path)
                && File.Exists(path)
                && new FileInfo(path).Length == ExpectedSize;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
