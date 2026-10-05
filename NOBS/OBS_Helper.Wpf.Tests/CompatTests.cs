using System.Security.Cryptography;
using System.Text;
using OBS_Helper.Wpf.Services.Compat;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// V2.9.3：跨 TFM 兼容层的等价性。
///
/// 引入 Windows 7 兼容构建（net6.0-windows）后，源码里有一批「新 API / 新语法」需要
/// 用等价实现兜住。这里对<b>能测的部分</b>给出机器证据：兼容实现在当前 TFM 上
/// 与被替换掉的原实现**逐字符 / 逐字节等价**。
///
/// ⚠️ 证据边界（已写进审校记录）：测试工程本身只跑 net10.0，所以这些断言证明的是
/// 「Compat 在 net10 上与原实现等价」，**不能**直接证明 net6 那条分支也等价 ——
/// net6 分支的等价性来自「同一份语义、只是构造函数重载不同」的推理 + 双目标编译通过。
/// </summary>
public class CompatTests
{
    // ------------------------------------------------------------ ASCII 判定

    [Fact]
    public void IsAsciiDigit_MatchesFrameworkImplementation()
    {
        for (var c = (char)0; c < char.MaxValue; c++)
        {
            // 逐字符全量比对（65536 次，毫秒级）：不抽样，避免「正好没抽到」的假通过
            Assert.Equal(char.IsAsciiDigit(c), Compat.IsAsciiDigit(c));
        }
    }

    [Fact]
    public void IsAsciiLetterOrDigit_MatchesFrameworkImplementation()
    {
        for (var c = (char)0; c < char.MaxValue; c++)
        {
            Assert.Equal(char.IsAsciiLetterOrDigit(c), Compat.IsAsciiLetterOrDigit(c));
        }
    }

    [Fact]
    public void AsciiHelpers_RejectFullWidthAndNonAscii()
    {
        // 全角数字 / 全角字母 / 中文数字都必须判为「不是 ASCII」——
        // 这正是输入过滤（端口、码率）会用到这两个函数的地方。
        foreach (var c in new[] { '０', '１', 'Ａ', 'ａ', '一', '、', '　' })
        {
            Assert.False(Compat.IsAsciiDigit(c));
            Assert.False(Compat.IsAsciiLetterOrDigit(c));
        }
    }

    // ------------------------------------------------------------ AES-GCM

    /// <summary>
    /// 兼容版构造出来的 AES-GCM 必须与 .NET 8 起的新构造函数**产生完全相同的密文与认证标签**。
    ///
    /// 这一条直接对应审校记录里的「`Compat.CreateAesGcm` 跨 TFM 密文一致性」——
    /// 在这里它是**实测**（net10 上两条路径对拍），而不是推理。
    /// </summary>
    [Fact]
    public void CreateAesGcm_ProducesIdenticalCiphertextToTheDirectConstructor()
    {
        var key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var nonce = Enumerable.Range(100, 12).Select(i => (byte)i).ToArray();
        var plain = Encoding.UTF8.GetBytes("OBS帮助助手 · secrets.dat 往返对拍 v2.9.3");

        var expectedCipher = new byte[plain.Length];
        var expectedTag = new byte[16];
        using (var direct = new AesGcm(key, 16))
        {
            direct.Encrypt(nonce, plain, expectedCipher, expectedTag);
        }

        var actualCipher = new byte[plain.Length];
        var actualTag = new byte[16];
        using (var viaCompat = Compat.CreateAesGcm(key))
        {
            viaCompat.Encrypt(nonce, plain, actualCipher, actualTag);
        }

        Assert.Equal(expectedCipher, actualCipher);
        Assert.Equal(expectedTag, actualTag);

        // 反向也要成立：兼容版加密、原版解密，能拿回原文
        using (var direct = new AesGcm(key, 16))
        {
            var roundTrip = new byte[plain.Length];
            direct.Decrypt(nonce, actualCipher, actualTag, roundTrip);
            Assert.Equal(plain, roundTrip);
        }
    }

    /// <summary>标签长度固定 16 字节：这是历史密文能被读回来的前提。</summary>
    [Fact]
    public void CreateAesGcm_UsesSixteenByteTag()
    {
        var key = new byte[32];
        var nonce = new byte[12];
        var plain = new byte[8];

        using var aes = Compat.CreateAesGcm(key);
        var cipher = new byte[8];

        // 15 字节的标签缓冲区应当被拒绝（库会抛 ArgumentException），16 字节则正常
        Assert.ThrowsAny<ArgumentException>(() =>
            aes.Encrypt(nonce, plain, cipher, new byte[15]));

        aes.Encrypt(nonce, plain, cipher, new byte[16]);
    }
}
