using System.IO;
using System.Security.Cryptography;

namespace OBS_Helper.Wpf.Services.Update;

/// <summary>文件 SHA-256 计算（构建脚本比对清单、客户端校验下载均用同一实现）。</summary>
public static class FileHasher
{
    /// <summary>计算文件 SHA-256，返回 64 位小写十六进制；文件不可读时抛出异常由调用方处理。</summary>
    public static string Sha256(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }
}
