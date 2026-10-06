using System.Security.Cryptography;
using System.Text;

namespace OBS_Helper.Wpf.Services.Host;

/// <summary>
/// 机密值的第二层加密编解码（V3.0 / E2）：从 <see cref="HostBridge"/> 里拆出来的**可测单元**。
///
/// 为什么拆：这段逻辑（机器绑定密钥派生 + AES-256-GCM 编解码 + 旧版明文兼容 + 认证失败 fail-closed）
/// 是机密存储里最容易出错、后果最重的一段（错了会「密钥静默丢失」或「读不回来」），
/// 却因为原先长在 <see cref="HostBridge"/> 这个依赖注册表 / DPAPI / 文件系统的类里，
/// **结构上无法被单测覆盖**（测试工程不引用主工程）。
///
/// 拆分原则：本类**只做纯计算**，不碰注册表、不碰文件、不碰 DPAPI ——
/// 机器 GUID 由调用方读好传进来，因此整条编解码链路可以在普通测试进程里跑。
///
/// 存储格式：<c>v2:&lt;nonce_b64&gt;:&lt;tag_b64&gt;:&lt;cipher_b64&gt;</c>；
/// 旧版明文值（不以 <c>v2:</c> 开头）读取时原样返回，下次写入自动升级为 v2。
/// </summary>
public static class SecretCodec
{
    /// <summary>v2 存储格式前缀。</summary>
    public const string V2Prefix = "v2:";

    /// <summary>PBKDF2 迭代次数（与既有实现保持一致，改动会让旧值读不回来）。</summary>
    public const int Pbkdf2Iterations = 100_000;

    /// <summary>派生盐（固定值：它不是机密，只用于域分离）。</summary>
    public static readonly byte[] Salt = Encoding.UTF8.GetBytes("OBS_Helper.SecretStore.v2.salt");

    /// <summary>
    /// 由本机 MachineGuid 派生 AES-256-GCM 密钥（32 字节）。
    /// <paramref name="machineGuid"/> 为空 / 全空白时返回 null —— 调用方据此退化为「仅 DPAPI」，
    /// 保证读不到 GUID 的精简系统上功能仍然可用。
    /// </summary>
    public static byte[]? DeriveKey(string? machineGuid)
    {
        if (string.IsNullOrWhiteSpace(machineGuid)) return null;
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes("OBS_Helper.v2:" + machineGuid.Trim()),
            Salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            32);
    }

    /// <summary>
    /// 把明文机密值加密为 v2 存储格式。
    /// <paramref name="key"/> 为 null 时**原样返回明文**（仅剩外层 DPAPI 保护），
    /// 与既有行为一致：读不到机器 GUID 时宁可不加这一层，也不能让功能不可用。
    /// </summary>
    public static string Encrypt(string plain, byte[]? key)
    {
        if (string.IsNullOrEmpty(plain) || key is null) return plain;

        var nonce = RandomNumberGenerator.GetBytes(12);
        var plainBytes = Encoding.UTF8.GetBytes(plain);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[16];
        try
        {
            using (var aes = Services.Compat.Compat.CreateAesGcm(key))
            {
                aes.Encrypt(nonce, plainBytes, cipher, tag);
            }
            return V2Prefix
                + Convert.ToBase64String(nonce) + ":"
                + Convert.ToBase64String(tag) + ":"
                + Convert.ToBase64String(cipher);
        }
        finally
        {
            Array.Clear(plainBytes, 0, plainBytes.Length);
            Array.Clear(cipher, 0, cipher.Length);
        }
    }

    /// <summary>
    /// 解密 v2 存储值。规则（每条都有对应单测）：
    /// <list type="number">
    ///   <item>不以 <c>v2:</c> 开头 → 旧版明文，原样返回；</item>
    ///   <item>以 <c>v2:</c> 开头但格式不严格合法（段数 / 长度 / Base64 错误）→ 视为「旧版明文恰好以 v2: 开头」，
    ///         原样返回，**绝不误删** —— 真实 v2 一定出自本代码，格式必然严格合法；</item>
    ///   <item>格式合法但拿不到机器密钥 → 返回 null（调用方按「不存在」处理，fail-closed）；</item>
    ///   <item>格式合法但 GCM 认证失败（密钥不符 / 数据被改）→ 返回 null（fail-closed）。</item>
    /// </list>
    /// </summary>
    public static string? Decrypt(string stored, byte[]? key)
    {
        if (string.IsNullOrEmpty(stored)) return stored;
        if (!stored.StartsWith(V2Prefix, StringComparison.Ordinal)) return stored;

        byte[] nonce, tag, cipher;
        try
        {
            var parts = stored.Substring(V2Prefix.Length).Split(':');
            if (parts.Length != 3) return stored;
            nonce = Convert.FromBase64String(parts[0]);
            if (nonce.Length != 12) return stored;
            tag = Convert.FromBase64String(parts[1]);
            if (tag.Length != 16) return stored;
            cipher = Convert.FromBase64String(parts[2]);
            if (cipher.Length == 0) return stored;
        }
        catch (FormatException)
        {
            return stored; // Base64 解码失败 → 旧版明文，原样返回
        }

        if (key is null) return null;

        var plain = new byte[cipher.Length];
        try
        {
            using (var aes = Services.Compat.Compat.CreateAesGcm(key))
            {
                aes.Decrypt(nonce, cipher, tag, plain);
            }
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception)
        {
            // 格式合法但认证失败：fail-closed，视为不存在（用户重填即可），绝不返回半个明文
            return null;
        }
        finally
        {
            Array.Clear(plain, 0, plain.Length);
        }
    }
}
