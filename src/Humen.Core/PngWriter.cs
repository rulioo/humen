using System.IO.Compression;

namespace Humen.Core;

/// <summary>
/// 极简 PNG 编码器（8 位真彩，无 alpha，无调色板，无隔行）。
///
/// <b>为什么要自己写</b>：M2 的交付物是「把蓝星画出来」，而本机没有 Unity，
/// 也没引任何图像库。PNG 的最小可用子集其实很短 ——
/// 签名 + IHDR + IDAT + IEND，其中 IDAT 就是 zlib 压缩的扫描线，
/// 而 zlib 由 BCL 的 <see cref="ZLibStream"/> 直接提供（.NET 6+）。
/// 于是整条「渲染 → 出图」链路<b>不依赖任何第三方包</b>，
/// 与 §8.5「生成器可在 CI 中复现」的要求一致。
///
/// 不支持 alpha：渲染器已经在合成阶段把大气辉光混进 RGB 了，
/// 输出不需要透明通道（也省掉一半 IDAT 体积）。
/// </summary>
public static class PngWriter
{
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <param name="rgb">长度必须为 <c>width * height * 3</c>，行优先。</param>
    public static void Write(string path, int width, int height, byte[] rgb)
    {
        if (rgb.Length != width * height * 3)
            throw new ArgumentException($"RGB 缓冲长度应为 {width * height * 3}，实为 {rgb.Length}", nameof(rgb));

        using var fs = File.Create(path);
        fs.Write(Signature);

        // ── IHDR ──
        var ihdr = new byte[13];
        WriteBe(ihdr, 0, (uint)width);
        WriteBe(ihdr, 4, (uint)height);
        ihdr[8] = 8;    // 位深
        ihdr[9] = 2;    // 颜色类型 2 = 真彩 RGB
        ihdr[10] = 0;   // 压缩方法（固定 0）
        ihdr[11] = 0;   // 滤波方法（固定 0）
        ihdr[12] = 0;   // 隔行（0 = 无）
        WriteChunk(fs, "IHDR", ihdr);

        // ── IDAT ──
        // 扫描线格式：每行前面加一个滤波类型字节。这里统一用 0（None）——
        // 渲染图是连续色调，滤波带来的压缩收益有限，不值得为此增加出错面。
        int stride = width * 3;
        var raw = new byte[(stride + 1) * height];
        for (int y = 0; y < height; y++)
        {
            raw[y * (stride + 1)] = 0;                                   // filter = None
            Buffer.BlockCopy(rgb, y * stride, raw, y * (stride + 1) + 1, stride);
        }

        byte[] compressed;
        using (var ms = new MemoryStream())
        {
            using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                z.Write(raw, 0, raw.Length);
            compressed = ms.ToArray();
        }
        WriteChunk(fs, "IDAT", compressed);

        // ── IEND ──
        WriteChunk(fs, "IEND", Array.Empty<byte>());
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        WriteBe(len, 0, (uint)data.Length);
        s.Write(len);

        var typeBytes = new byte[4];
        for (int i = 0; i < 4; i++) typeBytes[i] = (byte)type[i];
        s.Write(typeBytes);
        s.Write(data);

        // CRC 覆盖「类型 + 数据」，不含长度字段
        uint crc = 0xFFFFFFFFu;
        crc = UpdateCrc(crc, typeBytes);
        crc = UpdateCrc(crc, data);
        crc ^= 0xFFFFFFFFu;

        Span<byte> crcBytes = stackalloc byte[4];
        WriteBe(crcBytes, 0, crc);
        s.Write(crcBytes);
    }

    private static void WriteBe(Span<byte> buf, int offset, uint v)
    {
        buf[offset] = (byte)(v >> 24);
        buf[offset + 1] = (byte)(v >> 16);
        buf[offset + 2] = (byte)(v >> 8);
        buf[offset + 3] = (byte)v;
    }

    private static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    private static uint UpdateCrc(uint crc, byte[] data)
    {
        for (int i = 0; i < data.Length; i++)
            crc = CrcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
        return crc;
    }
}
