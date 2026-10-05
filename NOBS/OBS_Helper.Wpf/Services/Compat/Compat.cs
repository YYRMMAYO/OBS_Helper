using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace OBS_Helper.Wpf.Services.Compat;

/// <summary>
/// 跨 TFM 的小工具（V2.9.3）。
///
/// 引入 Windows 7 兼容构建（net6.0-windows）之后，源码里用到的一批「新语法糖 / 新 API」
/// 在老目标框架上不存在。这里有两条处理原则：
/// <list type="number">
///   <item><b>语义完全等价的，抽成静态帮助方法</b>（例如 <c>char.IsAsciiDigit</c>），
///         调用点只改名字，不改变行为；</item>
///   <item><b>API 形态不同但能力等价的，用 <c>#if</c> 分支</b>（例如文件夹选择对话框），
///         两条分支都保留完整实现。</item>
/// </list>
/// 刻意不做的事：为了让老框架编过而降低功能（比如把文件夹选择换成手输路径）。
/// </summary>
internal static class Compat
{
    /// <summary>ASCII 数字（等价于 .NET 7+ 的 <c>char.IsAsciiDigit</c>）。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';

    /// <summary>ASCII 字母或数字（等价于 .NET 7+ 的 <c>char.IsAsciiLetterOrDigit</c>）。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsAsciiLetterOrDigit(char c)
        => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

    /// <summary>
    /// 构造 AES-GCM。.NET 8 起构造函数要求显式给出认证标签长度
    /// （<c>AesGcm(key, tagSizeInBytes)</c>，旧的单参重载已标记过时），
    /// 而 net6.0 只有单参重载、标签固定 16 字节。这里统一成 16 字节，
    /// 两条分支产生的密文**完全一致**（格式与兼容性都不变）。
    /// </summary>
    public static AesGcm CreateAesGcm(byte[] key)
    {
#if NET8_0_OR_GREATER
        return new AesGcm(key, 16);
#else
        return new AesGcm(key);
#endif
    }
}
