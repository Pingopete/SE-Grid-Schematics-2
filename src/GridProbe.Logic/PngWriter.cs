using System.IO.Compression;

namespace GridProbe;

// Minimal PNG encoder. The engine's texture loader only accepts
// .png/.dds/.jpg/.slug, and a malformed image crashes the render thread,
// so this writes the most conservative layout possible: 8-bit RGBA.
internal static class PngWriter
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static void WriteGrayRgba(Stream s, byte[,] gray)
    {
        int w = gray.GetLength(0), h = gray.GetLength(1);
        var raw = new byte[h * (1 + w * 4)];
        int p = 0;
        for (int y = 0; y < h; y++)
        {
            raw[p++] = 0; // filter: none
            for (int x = 0; x < w; x++)
            {
                // White with tone in alpha: empty cells are fully transparent so
                // the LCD's glass shows through; thicker hull = more opaque.
                byte g = gray[x, y];
                raw[p++] = 255; raw[p++] = 255; raw[p++] = 255; raw[p++] = g;
            }
        }

        s.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        var ihdr = new byte[13];
        WriteBE(ihdr, 0, (uint)w);
        WriteBE(ihdr, 4, (uint)h);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // color type: RGBA
        WriteChunk(s, "IHDR", ihdr);

        byte[] idat;
        using (var ms = new MemoryStream())
        {
            ms.WriteByte(0x78); ms.WriteByte(0x9C); // zlib header
            using (var ds = new DeflateStream(ms, CompressionLevel.Fastest, leaveOpen: true))
                ds.Write(raw, 0, raw.Length);
            var adler = new byte[4];
            WriteBE(adler, 0, Adler32(raw));
            ms.Write(adler, 0, 4);
            idat = ms.ToArray();
        }
        WriteChunk(s, "IDAT", idat);
        WriteChunk(s, "IEND", Array.Empty<byte>());
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        WriteBE(len, 0, (uint)data.Length);
        s.Write(len, 0, 4);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes, 0, 4);
        s.Write(data, 0, data.Length);
        uint crc = 0xFFFFFFFF;
        foreach (var b in typeBytes) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        foreach (var b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        var crcBytes = new byte[4];
        WriteBE(crcBytes, 0, crc ^ 0xFFFFFFFF);
        s.Write(crcBytes, 0, 4);
    }

    private static void WriteBE(byte[] buf, int off, uint v)
    {
        buf[off] = (byte)(v >> 24); buf[off + 1] = (byte)(v >> 16);
        buf[off + 2] = (byte)(v >> 8); buf[off + 3] = (byte)v;
    }

    private static uint Adler32(byte[] data)
    {
        uint a = 1, b = 0;
        foreach (var d in data) { a = (a + d) % 65521; b = (b + a) % 65521; }
        return (b << 16) | a;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}
