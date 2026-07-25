namespace GridProbe;

internal static class BmpWriter
{
    public static void WriteGrayscale(string path, int[,] data) => WriteGrayscale(path, data, false);

    // Pre-toned 8-bit pixels, written as 24-bit BMP.
    public static void WriteGray8(string path, byte[,] gray, bool topRowFirst)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        WriteGray8(fs, gray, topRowFirst);
    }

    public static void WriteGray8(Stream fs, byte[,] gray, bool topRowFirst)
    {
        int w = gray.GetLength(0), h = gray.GetLength(1);
        int rowBytes = (w * 3 + 3) & ~3;
        int dataSize = rowBytes * h;
        using var bw = new BinaryWriter(fs);
        bw.Write((ushort)0x4D42);
        bw.Write(54 + dataSize);
        bw.Write(0);
        bw.Write(54);
        bw.Write(40);
        bw.Write(w);
        bw.Write(h);
        bw.Write((ushort)1);
        bw.Write((ushort)24);
        bw.Write(0);
        bw.Write(dataSize);
        bw.Write(2835); bw.Write(2835); bw.Write(0); bw.Write(0);
        var row = new byte[rowBytes];
        for (int i = 0; i < h; i++)
        {
            int y = topRowFirst ? h - 1 - i : i;
            Array.Clear(row);
            for (int x = 0; x < w; x++)
            {
                byte g = gray[x, y];
                row[x * 3] = g; row[x * 3 + 1] = g; row[x * 3 + 2] = g;
            }
            bw.Write(row);
        }
    }

    // topRowFirst=true puts data row 0 at the TOP of the displayed image (matches the panel's vector orientation).
    public static void WriteGrayscale(string path, int[,] data, bool topRowFirst)
    {
        int w = data.GetLength(0), h = data.GetLength(1);
        int maxV = 1;
        for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) if (data[x, y] > maxV) maxV = data[x, y];

        int rowBytes = (w * 3 + 3) & ~3;
        int dataSize = rowBytes * h;
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);
        bw.Write((ushort)0x4D42);
        bw.Write(54 + dataSize);
        bw.Write(0);
        bw.Write(54);
        bw.Write(40);
        bw.Write(w);
        bw.Write(h);
        bw.Write((ushort)1);
        bw.Write((ushort)24);
        bw.Write(0);
        bw.Write(dataSize);
        bw.Write(2835); bw.Write(2835); bw.Write(0); bw.Write(0);

        var row = new byte[rowBytes];
        for (int i = 0; i < h; i++)
        {
            int y = topRowFirst ? h - 1 - i : i;
            Array.Clear(row);
            for (int x = 0; x < w; x++)
            {
                var v = data[x, y];
                byte g = v <= 0 ? (byte)0 : (byte)(40 + 215.0 * Math.Sqrt((double)v / maxV));
                row[x * 3] = g; row[x * 3 + 1] = g; row[x * 3 + 2] = g;
            }
            bw.Write(row);
        }
    }
}
