using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OBS_Helper.Wpf.Services.Ai;

/// <summary>
/// 内置免费 AI（智谱 GLM-4.7-Flash）的密钥提供者。
///
/// 密钥**不进入代码仓库**：发布前由 <c>scripts/embed_free_ai_key.ps1</c> 读取真实 Key，
/// 加密后写入 <c>Assets/free_ai_key.json</c>（已 gitignore），再随发布一起打进安装包 /
/// 便携包的内嵌资源。源码仓库里只有「脚本 + 空资源」，任何人都拿不到真实密钥。
///
/// 存储形态（多重加密，防「拿到文件/安装包就一眼看到 Key」）：
/// <list type="bullet">
///   <item>L1：每次构建随机生成的 salt（文件内 x 字段），让每次构建的密文互不相同；</item>
///   <item>L2：PBKDF2-SHA256（20 万次迭代）从 pepper + salt 派生 64 字节密钥；
///         pepper 在代码里拆成两段存放，不在源码中整串出现；</item>
///   <item>L3：encrypt-then-MAC——AES-256-CBC（随机 IV）+ HMAC-SHA256 认证；
///         先验 HMAC 再解密，文件里只有 base64 密文，认证失败直接判「未内置」（fail-closed）；</item>
///   <item>L4：解密后明文只在进程内存里缓存，用完即弃的临时缓冲即时清零。</item>
/// </list>
///
/// 必须说明的边界：这是**混淆级**保护——安装包和源码都在用户手里，
/// 有决心的人总能逆向出 Key（开源应用的固有取舍）。真正的防线是：
/// ① 这是智谱免费档模型，烧也烧不了多少钱；② 应用内智谱通道每日 10 次 + 10 秒间隔的本地强限频。
/// 若 Key 被滥用导致额度异常，去智谱开放平台重新生成即可，换 Key 后重跑 embed 脚本重新出包。
/// </summary>
public sealed class FreeAiKeyProvider
{
    private const string ResourceName = "OBS_Helper.Wpf.Assets.free_ai_key.json";

    // pepper 拆两段存放（同一进程内拼接），避免源码里出现完整派生口令
    private static readonly byte[] PepperA = Encoding.UTF8.GetBytes("OBS_Helper.freeai.");
    private static readonly byte[] PepperB = Encoding.UTF8.GetBytes("glm-4.7-flash.2026.zp");
    private static readonly byte[] FixedSalt = Encoding.UTF8.GetBytes("obs-helper-freeai-v1");

    private readonly object _gate = new();
    private string? _cached;
    private bool _tried;

    /// <summary>返回解密后的内置密钥；发布包未内嵌密钥或解密失败时返回 null（调用方按「免费通道不可用」处理）。</summary>
    public string? GetKey()
    {
        if (_tried) return _cached;
        lock (_gate)
        {
            if (_tried) return _cached;
            _cached = TryLoadKey();
            _tried = true;
            return _cached;
        }
    }

    /// <summary>是否已内嵌可用密钥（设置页展示用，不泄露密钥本身）。</summary>
    public bool IsAvailable => GetKey() is not null;

    private static string? TryLoadKey()
    {
        try
        {
            var asm = typeof(FreeAiKeyProvider).Assembly;
            using var stream = asm.GetManifestResourceStream(ResourceName);
            if (stream is null) return null; // 发布包未内嵌密钥（开发机 / 未跑 embed 脚本）

            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var doc = JsonDocument.Parse(reader.ReadToEnd());
            var root = doc.RootElement;
            if (!root.TryGetProperty("d", out var d)
                || !root.TryGetProperty("i", out var i)
                || !root.TryGetProperty("m", out var m)
                || !root.TryGetProperty("x", out var x))
            {
                return null;
            }

            var perBuildSalt = Convert.FromBase64String(x.GetString() ?? "");
            var iv = Convert.FromBase64String(i.GetString() ?? "");
            var cipher = Convert.FromBase64String(d.GetString() ?? "");
            var mac = Convert.FromBase64String(m.GetString() ?? "");
            if (perBuildSalt.Length == 0 || iv.Length != 16 || cipher.Length == 0 || mac.Length != 32) return null;

            var key = DeriveKey(perBuildSalt); // 64 字节：前 32 AES，后 32 HMAC
            var keyAes = key.AsSpan(0, 32);
            var keyMac = key.AsSpan(32, 32);

            // 先验 HMAC（encrypt-then-MAC），再解密；认证失败直接判「未内置」，绝不碰密文当明文
            var expectedMac = HMACSHA256.HashData(keyMac, Concat(iv, cipher));
            if (!CryptographicOperations.FixedTimeEquals(mac, expectedMac)) return null;

            try
            {
                using var aes = Aes.Create();
                aes.Key = keyAes.ToArray();
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using var dec = aes.CreateDecryptor();
                var plain = dec.TransformFinalBlock(cipher, 0, cipher.Length);
                try
                {
                    var result = Encoding.UTF8.GetString(plain);
                    return string.IsNullOrWhiteSpace(result) ? null : result;
                }
                finally
                {
                    Array.Clear(plain, 0, plain.Length);
                }
            }
            catch (Exception)
            {
                // 数据损坏 / 密钥不符：fail-closed
                return null;
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static byte[] Concat(byte[] a, byte[] b)
    {
        var r = new byte[a.Length + b.Length];
        Buffer.BlockCopy(a, 0, r, 0, a.Length);
        Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
        return r;
    }

    private static byte[] DeriveKey(byte[] perBuildSalt)
    {
        var password = new byte[PepperA.Length + PepperB.Length];
        Buffer.BlockCopy(PepperA, 0, password, 0, PepperA.Length);
        Buffer.BlockCopy(PepperB, 0, password, PepperA.Length, PepperB.Length);

        var salt = new byte[FixedSalt.Length + perBuildSalt.Length];
        Buffer.BlockCopy(FixedSalt, 0, salt, 0, FixedSalt.Length);
        Buffer.BlockCopy(perBuildSalt, 0, salt, FixedSalt.Length, perBuildSalt.Length);

        try
        {
            return Rfc2898DeriveBytes.Pbkdf2(password, salt, 200_000, HashAlgorithmName.SHA256, 64);
        }
        finally
        {
            Array.Clear(password, 0, password.Length);
            Array.Clear(salt, 0, salt.Length);
        }
    }
}
