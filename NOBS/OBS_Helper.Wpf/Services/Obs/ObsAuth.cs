using System.Security.Cryptography;
using System.Text;

namespace OBS_Helper.Wpf.Services.Obs;

/// <summary>
/// obs-websocket 5.x 鉴权算法（纯函数，便于单元测试）。
///
/// 规范（protocol.md「Creating an authentication string」）：
/// <code>
///   secret        = base64( sha256( password + salt ) )
///   authResponse  = base64( sha256( secret + challenge ) )
/// </code>
/// 其中 salt / challenge 由服务端在 Hello 消息中下发，均为 base64 字符串，
/// 参与哈希时按「原始字符串」拼接（不解码）。
/// </summary>
public static class ObsAuth
{
    /// <summary>根据 Hello 中的 salt / challenge 与用户密码计算鉴权字符串。</summary>
    public static string BuildAuthResponse(string password, string salt, string challenge)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(salt);
        ArgumentNullException.ThrowIfNull(challenge);

        var secret = Sha256Base64(password + salt);
        return Sha256Base64(secret + challenge);
    }

    private static string Sha256Base64(string input)
        => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
}
