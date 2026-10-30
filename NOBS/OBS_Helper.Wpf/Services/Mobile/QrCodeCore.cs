using System.Text;

namespace OBS_Helper.Wpf.Services.Mobile;

/// <summary>
/// 极简二维码编码器（V3.4.0 / M01 配对二维码）。
///
/// 为什么自己写：产品承诺「主程序零 NuGet」（PLAN_V3.4.0 §0.4 / §7.2）。
/// 这里只实现配对页真正需要的子集 —— **字节模式、纠错等级 M、版本 1~10**，
/// 不实现汉字模式、结构化追加、ECI、微二维码。输入超过版本 10 容量时明确失败，
/// 由调用方缩短 URL（配对 URL 实测在版本 3~4）。
///
/// 算法按 ISO/IEC 18004 实现，与常见参考实现（qrcode / Nayuki）逐位一致：
/// Reed-Solomon 纠错、8 种掩码 + 4 条罚分规则、格式信息 BCH(15,5)、
/// 版本 7+ 的版本信息 BCH(18,6)。单测用离线生成的参考矩阵逐位比对
/// （见 <c>QrCodeCoreTests</c>），避免「画出来像二维码但扫不出来」。
/// </summary>
public static class QrCodeCore
{
    /// <summary>编码结果：<c>Modules[y][x]</c> 为 true 表示该模块为深色。</summary>
    public sealed class QrCode
    {
        public int Version { get; init; }
        public int Size => 17 + 4 * Version;
        public required bool[][] Modules { get; init; }
        /// <summary>使用的掩码编号（0~7）。</summary>
        public int Mask { get; init; }
    }

    /// <summary>输入过长（超过版本 10 的容量）时抛出。</summary>
    public sealed class QrTooLongException : Exception
    {
        public QrTooLongException(string message) : base(message) { }
    }

    // 纠错等级 M 的分块结构：{ 版本, 每块纠错码字数, 组1块数, 组1数据码字数, 组2块数, 组2数据码字数 }
    private static readonly int[][] BlockTable =
    {
        new[] { 1, 10, 1, 16, 0, 0 },
        new[] { 2, 16, 1, 28, 0, 0 },
        new[] { 3, 26, 1, 44, 0, 0 },
        new[] { 4, 18, 2, 32, 0, 0 },
        new[] { 5, 24, 2, 43, 0, 0 },
        new[] { 6, 16, 4, 27, 0, 0 },
        new[] { 7, 18, 4, 31, 0, 0 },
        new[] { 8, 22, 2, 38, 2, 39 },
        new[] { 9, 22, 3, 36, 2, 37 },
        new[] { 10, 26, 4, 43, 1, 44 },
    };

    private static readonly int[][] AlignmentCenters =
    {
        Array.Empty<int>(),
        new[] { 6, 18 },
        new[] { 6, 22 },
        new[] { 6, 26 },
        new[] { 6, 30 },
        new[] { 6, 34 },
        new[] { 6, 22, 38 },
        new[] { 6, 24, 42 },
        new[] { 6, 26, 46 },
        new[] { 6, 28, 50 },
    };

    /// <summary>把文本按 UTF-8 编码成二维码矩阵，自动选择能装下的最小版本（1~10）。</summary>
    public static QrCode Encode(string text, int? forcedMask = null)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));
        if (forcedMask is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(forcedMask));
        var bytes = Encoding.UTF8.GetBytes(text);
        for (var version = 1; version <= 10; version++)
        {
            var capacityBits = DataCodewords(version) * 8;
            var lengthBits = version <= 9 ? 8 : 16;
            if (4 + lengthBits + bytes.Length * 8 <= capacityBits)
                return Build(bytes, version, forcedMask);
        }
        throw new QrTooLongException($"Text is too long for QR version 10 (M level): {bytes.Length} bytes.");
    }

    /// <summary>某个版本在 M 等级下的数据码字总数。</summary>
    public static int DataCodewords(int version)
    {
        if (version < 1 || version > 10) throw new ArgumentOutOfRangeException(nameof(version));
        var row = BlockTable[version - 1];
        return row[2] * row[3] + row[4] * row[5];
    }

    private static QrCode Build(byte[] data, int version, int? forcedMask)
    {
        var eccPerBlock = BlockTable[version - 1][1];
        var blocks = SplitIntoBlocks(AddPreambleAndPad(data, version), version);
        var allCodewords = Interleave(blocks, eccPerBlock);

        var size = 17 + 4 * version;
        var modules = NewMatrix(size);
        var isFunction = new bool[size][];
        for (var i = 0; i < size; i++) isFunction[i] = new bool[size];

        DrawFunctionPatterns(modules, isFunction, version);
        DrawCodewords(modules, isFunction, allCodewords);

        var bestMask = 0;
        var bestScore = int.MaxValue;
        bool[][]? best = null;
        var first = forcedMask ?? 0;
        var count = forcedMask.HasValue ? 1 : 8;
        for (var mask = first; mask < first + count; mask++)
        {
            var candidate = Clone(modules);
            ApplyMask(candidate, isFunction, mask);
            DrawFormatBits(candidate, version, mask);
            var score = Penalty(candidate);
            if (score < bestScore)
            {
                bestScore = score;
                bestMask = mask;
                best = candidate;
            }
        }

        // 掩码评分阶段已经写过格式信息；这里把最优解再写一次，保证返回的矩阵自洽。
        var result = best ?? throw new InvalidOperationException("QR mask selection produced no candidate.");
        DrawFormatBits(result, version, bestMask);
        return new QrCode { Version = version, Modules = result, Mask = bestMask };
    }

    // ---------------------------------------------------------------- 数据编码

    private static byte[] AddPreambleAndPad(byte[] data, int version)
    {
        var capacityBits = DataCodewords(version) * 8;
        var bits = new List<bool>(capacityBits);
        AppendBits(bits, 0b0100, 4); // 字节模式
        AppendBits(bits, data.Length, version <= 9 ? 8 : 16);
        foreach (var b in data) AppendBits(bits, b, 8);

        var terminator = Math.Min(4, capacityBits - bits.Count);
        for (var i = 0; i < terminator; i++) bits.Add(false);
        while (bits.Count % 8 != 0) bits.Add(false);

        var bytes = new byte[capacityBits / 8];
        for (var i = 0; i < bits.Count; i++)
        {
            if (bits[i]) bytes[i >> 3] |= (byte)(1 << (7 - (i & 7)));
        }
        for (int i = bits.Count / 8, pad = 0; i < bytes.Length; i++, pad++)
        {
            bytes[i] = pad % 2 == 0 ? (byte)0xEC : (byte)0x11;
        }
        return bytes;
    }

    private static void AppendBits(List<bool> bits, int value, int count)
    {
        for (var i = count - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0);
    }

    private static List<byte[]> SplitIntoBlocks(byte[] data, int version)
    {
        var row = BlockTable[version - 1];
        var blocks = new List<byte[]>();
        var offset = 0;
        for (var i = 0; i < row[2]; i++)
        {
            blocks.Add(data.AsSpan(offset, row[3]).ToArray());
            offset += row[3];
        }
        for (var i = 0; i < row[4]; i++)
        {
            blocks.Add(data.AsSpan(offset, row[5]).ToArray());
            offset += row[5];
        }
        return blocks;
    }

    private static byte[] Interleave(List<byte[]> blocks, int eccPerBlock)
    {
        var eccBlocks = blocks.Select(b => ReedSolomon(b, eccPerBlock)).ToList();
        var result = new List<byte>();
        var maxData = blocks.Max(b => b.Length);
        for (var i = 0; i < maxData; i++)
        {
            foreach (var b in blocks)
            {
                if (i < b.Length) result.Add(b[i]);
            }
        }
        for (var i = 0; i < eccPerBlock; i++)
        {
            foreach (var b in eccBlocks) result.Add(b[i]);
        }
        return result.ToArray();
    }

    // GF(256)，本原多项式 0x11D
    private static byte[]? _exp;
    private static byte[]? _log;

    private static void EnsureGalois()
    {
        if (_exp is not null) return;
        var exp = new byte[512];
        var log = new byte[256];
        var x = 1;
        for (var i = 0; i < 255; i++)
        {
            exp[i] = (byte)x;
            log[x] = (byte)i;
            x <<= 1;
            if ((x & 0x100) != 0) x ^= 0x11D;
        }
        for (var i = 255; i < 512; i++) exp[i] = exp[i - 255];
        _exp = exp;
        _log = log;
    }

    private static byte GfMul(byte a, byte b)
    {
        if (a == 0 || b == 0) return 0;
        EnsureGalois();
        return _exp![_log![a] + _log![b]];
    }

    private static byte[] ReedSolomon(byte[] data, int eccCount)
    {
        EnsureGalois();
        // 生成多项式：(x - a^0)(x - a^1)...(x - a^(ecc-1))
        var gen = new byte[eccCount];
        gen[eccCount - 1] = 1;
        var root = 1;
        for (var i = 0; i < eccCount; i++)
        {
            for (var j = 0; j < eccCount; j++)
            {
                gen[j] = GfMul(gen[j], (byte)root);
                if (j + 1 < eccCount) gen[j] ^= gen[j + 1];
            }
            root = GfMul((byte)root, 2);
        }

        var remainder = new byte[eccCount];
        foreach (var b in data)
        {
            var factor = (byte)(b ^ remainder[0]);
            Array.Copy(remainder, 1, remainder, 0, eccCount - 1);
            remainder[eccCount - 1] = 0;
            for (var i = 0; i < eccCount; i++) remainder[i] ^= GfMul(gen[i], factor);
        }
        return remainder;
    }

    // ---------------------------------------------------------------- 矩阵

    private static bool[][] NewMatrix(int size)
    {
        var m = new bool[size][];
        for (var i = 0; i < size; i++) m[i] = new bool[size];
        return m;
    }

    private static bool[][] Clone(bool[][] source) => source.Select(r => (bool[])r.Clone()).ToArray();

    private static void DrawFunctionPatterns(bool[][] m, bool[][] fn, int version)
    {
        var size = m.Length;

        // 定时图形
        for (var i = 0; i < size; i++)
        {
            SetFunction(m, fn, 6, i, i % 2 == 0);
            SetFunction(m, fn, i, 6, i % 2 == 0);
        }

        // 三个定位图形（含分隔符）
        foreach (var (cx, cy) in new[] { (3, 3), (size - 4, 3), (3, size - 4) })
        {
            for (var dy = -4; dy <= 4; dy++)
            {
                for (var dx = -4; dx <= 4; dx++)
                {
                    var x = cx + dx;
                    var y = cy + dy;
                    if (x < 0 || y < 0 || x >= size || y >= size) continue;
                    var dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    SetFunction(m, fn, x, y, dist != 2 && dist != 4);
                }
            }
        }

        // 校正图形
        var centers = AlignmentCenters[version - 1];
        foreach (var cy in centers)
        {
            foreach (var cx in centers)
            {
                if ((cx == 6 && cy == 6) || (cx == 6 && cy == size - 7) || (cx == size - 7 && cy == 6)) continue;
                for (var dy = -2; dy <= 2; dy++)
                {
                    for (var dx = -2; dx <= 2; dx++)
                    {
                        SetFunction(m, fn, cx + dx, cy + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
                    }
                }
            }
        }

        // 预留格式信息区域 + 固定深色模块
        for (var i = 0; i < 9; i++)
        {
            if (!fn[8][i]) { fn[8][i] = true; m[8][i] = false; }
            if (!fn[i][8]) { fn[i][8] = true; m[i][8] = false; }
        }
        for (var i = 0; i < 8; i++)
        {
            fn[8][size - 1 - i] = true;
            fn[size - 1 - i][8] = true;
        }
        fn[size - 8][8] = true;
        m[size - 8][8] = true;

        // 版本信息（版本 7+）
        if (version >= 7)
        {
            var rem = version;
            for (var i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            var bits = (version << 12) | rem;
            for (var i = 0; i < 18; i++)
            {
                var bit = ((bits >> i) & 1) != 0;
                var a = size - 11 + i % 3;
                var b = i / 3;
                SetFunction(m, fn, a, b, bit);
                SetFunction(m, fn, b, a, bit);
            }
        }
    }

    private static void SetFunction(bool[][] m, bool[][] fn, int x, int y, bool dark)
    {
        if (x < 0 || y < 0 || x >= m.Length || y >= m.Length) return;
        m[y][x] = dark;
        fn[y][x] = true;
    }

    private static void DrawCodewords(bool[][] m, bool[][] fn, byte[] codewords)
    {
        var size = m.Length;
        var i = 0;
        // 注意：这里必须用「基础列号」做循环变量。跳过第 6 列时列的偏移会改变，
        // 若直接修改循环变量，后续列会整体跳格（v1 矩阵会少掉 (5,4) 一对）。
        for (var baseRight = size - 1; baseRight >= 1; baseRight -= 2)
        {
            var right = baseRight <= 6 ? baseRight - 1 : baseRight;
            for (var vert = 0; vert < size; vert++)
            {
                for (var j = 0; j < 2; j++)
                {
                    var x = right - j;
                    var upward = ((right + 1) & 2) == 0;
                    var y = upward ? size - 1 - vert : vert;
                    if (fn[y][x] || i >= codewords.Length * 8) continue;
                    m[y][x] = ((codewords[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                    i++;
                }
            }
        }
    }

    private static void ApplyMask(bool[][] m, bool[][] fn, int mask)
    {
        var size = m.Length;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                if (fn[y][x]) continue;
                var invert = mask switch
                {
                    0 => (x + y) % 2 == 0,
                    1 => y % 2 == 0,
                    2 => x % 3 == 0,
                    3 => (x + y) % 3 == 0,
                    4 => (x / 3 + y / 2) % 2 == 0,
                    5 => x * y % 2 + x * y % 3 == 0,
                    6 => (x * y % 2 + x * y % 3) % 2 == 0,
                    7 => ((x + y) % 2 + x * y % 3) % 2 == 0,
                    _ => false,
                };
                if (invert) m[y][x] = !m[y][x];
            }
        }
    }

    private static void DrawFormatBits(bool[][] m, int version, int mask)
    {
        var size = m.Length;
        var data = (0b00 << 3) | mask; // 纠错等级 M = 00
        var rem = data;
        for (var i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        var bits = ((data << 10) | rem) ^ 0x5412;

        bool Bit(int i) => ((bits >> i) & 1) != 0;

        for (var i = 0; i <= 5; i++) m[i][8] = Bit(i);
        m[7][8] = Bit(6);
        m[8][8] = Bit(7);
        m[8][7] = Bit(8);
        for (var i = 9; i < 15; i++) m[8][14 - i] = Bit(i);

        for (var i = 0; i < 8; i++) m[8][size - 1 - i] = Bit(i);
        for (var i = 8; i < 15; i++) m[size - 15 + i][8] = Bit(i);
        m[size - 8][8] = true;
    }

    // ---------------------------------------------------------------- 罚分

    private static int Penalty(bool[][] m)
    {
        var size = m.Length;
        var score = 0;

        // 规则 1：同色连续 5 个以上
        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < size; i++)
            {
                var runColor = false;
                var run = 0;
                for (var j = 0; j < size; j++)
                {
                    var color = pass == 0 ? m[i][j] : m[j][i];
                    if (j == 0 || color != runColor)
                    {
                        if (run >= 5) score += 3 + (run - 5);
                        runColor = color;
                        run = 1;
                    }
                    else
                    {
                        run++;
                    }
                }
                if (run >= 5) score += 3 + (run - 5);
            }
        }

        // 规则 2：2x2 同色块
        for (var y = 0; y < size - 1; y++)
        {
            for (var x = 0; x < size - 1; x++)
            {
                var c = m[y][x];
                if (c == m[y][x + 1] && c == m[y + 1][x] && c == m[y + 1][x + 1]) score += 3;
            }
        }

        // 规则 3：类似定位图形的 1:1:3:1:1 + 4 空白
        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < size; i++)
            {
                for (var j = 0; j < size - 10; j++)
                {
                    bool A(int k) => pass == 0 ? m[i][j + k] : m[j + k][i];
                    if (A(0) && !A(1) && A(2) && A(3) && A(4) && !A(5) && A(6)
                        && !A(7) && !A(8) && !A(9) && !A(10)) score += 40;
                    if (!A(0) && !A(1) && !A(2) && !A(3) && A(4) && !A(5) && A(6)
                        && A(7) && A(8) && !A(9) && A(10)) score += 40;
                }
            }
        }

        // 规则 4：深色比例偏离 50%
        var dark = 0;
        foreach (var row in m) foreach (var c in row) if (c) dark++;
        var total = size * size;
        var k2 = Math.Abs(dark * 20 - total * 10) / total;
        score += k2 * 10;
        return score;
    }

    /// <summary>
    /// 生成 SVG（供桌面端直接预览 / 另存）。颜色不写死为黑白以外的值，quiet zone 默认 4 模块。
    /// </summary>
    public static string ToSvg(QrCode code, int quietZone = 4)
    {
        var size = code.Size;
        var total = size + quietZone * 2;
        var sb = new StringBuilder();
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 ")
          .Append(total).Append(' ').Append(total)
          .Append("\" shape-rendering=\"crispEdges\"><rect width=\"100%\" height=\"100%\" fill=\"#fff\"/><path fill=\"#000\" d=\"");
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                if (!code.Modules[y][x]) continue;
                sb.Append('M').Append(x + quietZone).Append(' ').Append(y + quietZone).Append("h1v1h-1z");
            }
        }
        sb.Append("\"/></svg>");
        return sb.ToString();
    }
}
