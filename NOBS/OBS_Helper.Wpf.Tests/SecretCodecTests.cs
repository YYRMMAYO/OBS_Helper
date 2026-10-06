using System.Text;
using OBS_Helper.Wpf.Services.Host;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// 机密值编解码（V3.0 / E2 拆出的 <see cref="SecretCodec"/>）的回归测试。
///
/// 这是「服务层第一次真正被测到」的那一类文件 —— 在它被拆出来之前，
/// 这段逻辑长在 <c>HostBridge</c> 里（依赖注册表 / DPAPI / 文件系统），
/// 测试工程链接不了，只能靠人工审阅。而它的失败后果很重：
/// 编错了会「密钥静默丢失」，解错了会「用户读不回自己的密钥」。
///
/// 因此每个分支都要有断言：正常往返、无密钥退化、旧版明文兼容、格式容错（绝不误删）、
/// 认证失败 fail-closed（绝不返回半个明文）。
/// </summary>
public class SecretCodecTests
{
    private static readonly byte[] Key = SecretCodec.DeriveKey("11111111-2222-3333-4444-555555555555")!;

    // ---------------------------------------------------------------- 密钥派生

    [Fact]
    public void DeriveKey_IsDeterministicForTheSameMachine()
    {
        var a = SecretCodec.DeriveKey("abc-123");
        var b = SecretCodec.DeriveKey("abc-123");
        Assert.NotNull(a);
        Assert.Equal(a, b);
        Assert.Equal(32, a!.Length);   // AES-256
    }

    [Fact]
    public void DeriveKey_DiffersAcrossMachines()
    {
        var a = SecretCodec.DeriveKey("machine-a");
        var b = SecretCodec.DeriveKey("machine-b");
        Assert.NotEqual(Convert.ToHexString(a!), Convert.ToHexString(b!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DeriveKey_ReturnsNullWithoutGuid(string? guid)
        => Assert.Null(SecretCodec.DeriveKey(guid));

    // ---------------------------------------------------------------- 往返

    [Theory]
    [InlineData("sk-abc123")]
    [InlineData("推流密钥-中文-🔑")]
    [InlineData("a")]
    [InlineData("line1\nline2\ttab")]
    public void EncryptDecrypt_RoundTripsAnyPlaintext(string plain)
    {
        var stored = SecretCodec.Encrypt(plain, Key);
        Assert.StartsWith(SecretCodec.V2Prefix, stored);
        Assert.Equal(plain, SecretCodec.Decrypt(stored, Key));
    }

    [Fact]
    public void Encrypt_UsesRandomNonceSoCiphertextDiffersEachTime()
    {
        var a = SecretCodec.Encrypt("same-value", Key);
        var b = SecretCodec.Encrypt("same-value", Key);
        Assert.NotEqual(a, b);
        Assert.Equal("same-value", SecretCodec.Decrypt(a, Key));
        Assert.Equal("same-value", SecretCodec.Decrypt(b, Key));
    }

    /// <summary>存储格式必须是「v2:nonce:tag:cipher」四段且各段长度固定 —— 这是跨版本读取的契约。</summary>
    [Fact]
    public void Encrypt_ProducesTheDocumentedStorageFormat()
    {
        var stored = SecretCodec.Encrypt("value", Key);
        var parts = stored.Split(':');
        Assert.Equal(4, parts.Length);
        Assert.Equal("v2", parts[0]);
        Assert.Equal(12, Convert.FromBase64String(parts[1]).Length);
        Assert.Equal(16, Convert.FromBase64String(parts[2]).Length);
        Assert.Equal(Encoding.UTF8.GetByteCount("value"), Convert.FromBase64String(parts[3]).Length);
    }

    // ---------------------------------------------------------------- 无机器密钥的退化路径

    [Fact]
    public void Encrypt_WithoutKey_StoresPlaintextUnchanged()
    {
        Assert.Equal("plain-secret", SecretCodec.Encrypt("plain-secret", key: null));
    }

    /// <summary>旧版明文（或仅 DPAPI 层）读取时必须原样返回，不能被当成损坏值丢掉。</summary>
    [Fact]
    public void Decrypt_LegacyPlaintextPassesThrough()
    {
        Assert.Equal("legacy-value", SecretCodec.Decrypt("legacy-value", Key));
        Assert.Equal("legacy-value", SecretCodec.Decrypt("legacy-value", key: null));
    }

    [Fact]
    public void Encrypt_EmptyValueStaysEmpty()
    {
        Assert.Equal("", SecretCodec.Encrypt("", Key));
        // 空串按「原样」处理（调用方本来就会跳过空值）：返回 "" 而不是 null，
        // 否则上层的「null = 解密失败 → 删除该条」逻辑会把空条目也删掉
        Assert.Equal("", SecretCodec.Decrypt("", Key));
        Assert.Equal("", SecretCodec.Decrypt("", null));
    }

    // ---------------------------------------------------------------- 格式容错：绝不误删

    [Theory]
    [InlineData("v2:only-two-parts")]
    [InlineData("v2:a:b:c:d")]                       // 段数过多
    [InlineData("v2:!!!:!!!:!!!")]                   // Base64 非法
    [InlineData("v2:AAAAAAAAAAAAAAAA:AAAAAAAAAAAAAAAAAAAAAAAAAA:")]  // 空 cipher
    [InlineData("v2:AAAA:BBBB:CCCC")]                // 长度都不对
    public void Decrypt_MalformedV2IsTreatedAsLegacyPlaintext(string stored)
    {
        // 关键：**原样返回**（当作「旧版明文恰好以 v2: 开头」），绝不返回 null 把它删掉
        Assert.Equal(stored, SecretCodec.Decrypt(stored, Key));
    }

    // ---------------------------------------------------------------- 认证失败：fail-closed

    [Fact]
    public void Decrypt_WithWrongKey_ReturnsNullInsteadOfGarbage()
    {
        var stored = SecretCodec.Encrypt("secret", Key);
        var otherKey = SecretCodec.DeriveKey("another-machine")!;

        Assert.Null(SecretCodec.Decrypt(stored, otherKey));
    }

    [Fact]
    public void Decrypt_WithMissingKeyOnAValidV2Value_ReturnsNull()
    {
        var stored = SecretCodec.Encrypt("secret", Key);
        Assert.Null(SecretCodec.Decrypt(stored, key: null));
    }

    [Fact]
    public void Decrypt_TamperedCiphertext_ReturnsNull()
    {
        var stored = SecretCodec.Encrypt("secret-value", Key);
        var parts = stored.Split(':');
        var cipher = Convert.FromBase64String(parts[3]);
        cipher[0] ^= 0xFF;   // 翻转一个字节
        var tampered = $"{parts[0]}:{parts[1]}:{parts[2]}:{Convert.ToBase64String(cipher)}";

        Assert.Null(SecretCodec.Decrypt(tampered, Key));
    }

    [Fact]
    public void Decrypt_TamperedTag_ReturnsNull()
    {
        var stored = SecretCodec.Encrypt("secret-value", Key);
        var parts = stored.Split(':');
        var tag = Convert.FromBase64String(parts[2]);
        tag[0] ^= 0xFF;
        var tampered = $"{parts[0]}:{parts[1]}:{Convert.ToBase64String(tag)}:{parts[3]}";

        Assert.Null(SecretCodec.Decrypt(tampered, Key));
    }

    /// <summary>长值（例如整段 JSON 密钥）也要能往返 —— 分块与长度前缀都别出偏差。</summary>
    [Fact]
    public void EncryptDecrypt_HandlesLongValues()
    {
        var longValue = new string('x', 20_000) + "-尾巴";
        var stored = SecretCodec.Encrypt(longValue, Key);
        Assert.Equal(longValue, SecretCodec.Decrypt(stored, Key));
    }
}
