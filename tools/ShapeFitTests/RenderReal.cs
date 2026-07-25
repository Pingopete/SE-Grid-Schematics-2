using GridProbe;

// Diagnostic: take a REAL coverage dump from the game, run it through the exact
// band pipeline, and rasterize the resulting polygons. Anything visible here is
// produced by the geometry; anything only visible in game is produced by the
// renderer. Run with:  dotnet run -- render <cov.bmp> <out.bmp> <x0> <y0> <w> <h> <scale>
internal static class RenderReal
{
    public static int Run(string[] args)
    {
        string src = args[1], dst = args[2];
        int x0 = int.Parse(args[3]), y0 = int.Parse(args[4]);
        int w = int.Parse(args[5]), h = int.Parse(args[6]);
        int scale = int.Parse(args[7]);

        var (img, iw, ih) = ReadGray8(src);
        Console.WriteLine($"loaded {iw}x{ih} from {src}");

        var cov = new byte[w, h];
        var tone = new byte[w, h];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                int sx = x0 + x, sy = y0 + y;
                int v = sx < 0 || sy < 0 || sx >= iw || sy >= ih ? 0 : img[sy * iw + sx];
                int c = (int)Math.Round(v / 255.0 * BlockShapes.FracUnits);
                cov[x, y] = (byte)c;
                tone[x, y] = (byte)(c > 0 ? 200 : 0);
            }

        var bands = ToneBands.Build(tone, cov);
        Console.WriteLine($"bands={bands.Bands.Count} segs(lod0)={bands.TotalSegs[0]}");

        // Nonzero-winding rasterization at sub-cell resolution.
        var outImg = new byte[w * scale, h * scale];
        for (int py = 0; py < h * scale; py++)
            for (int px = 0; px < w * scale; px++)
            {
                float fx = (px + 0.5f) / scale, fy = (py + 0.5f) / scale;
                int shade = 0;
                foreach (var band in bands.Bands)
                {
                    int winding = 0;
                    foreach (var loop in band.Loops)
                    {
                        var pts = loop.L[0];
                        int m = pts.Length / 2;
                        for (int i = 0; i < m; i++)
                        {
                            int j = (i + 1) % m;
                            float ax = pts[i * 2], ay = pts[i * 2 + 1];
                            float bx = pts[j * 2], by = pts[j * 2 + 1];
                            if (ay <= fy)
                            {
                                if (by > fy && (bx - ax) * (fy - ay) - (fx - ax) * (by - ay) > 0) winding++;
                            }
                            else if (by <= fy && (bx - ax) * (fy - ay) - (fx - ax) * (by - ay) < 0) winding--;
                        }
                    }
                    if (winding != 0) shade = Math.Max(shade, band.Alpha);
                }
                outImg[px, py] = (byte)shade;
            }

        BmpWriter.WriteGray8(dst, outImg, topRowFirst: true);
        Console.WriteLine($"wrote {dst} ({w * scale}x{h * scale})");
        return 0;
    }

    private static (byte[] Data, int W, int H) ReadGray8(string path)
    {
        var bytes = File.ReadAllBytes(path);
        int offset = BitConverter.ToInt32(bytes, 10);
        int w = BitConverter.ToInt32(bytes, 18);
        int h = BitConverter.ToInt32(bytes, 22);
        int bpp = BitConverter.ToUInt16(bytes, 28);
        bool bottomUp = h > 0;
        h = Math.Abs(h);
        int bytesPerPx = bpp / 8;
        int stride = (w * bytesPerPx + 3) & ~3;
        var data = new byte[w * h];
        for (int row = 0; row < h; row++)
        {
            int srcRow = bottomUp ? h - 1 - row : row;
            for (int x = 0; x < w; x++)
                data[row * w + x] = bytes[offset + srcRow * stride + x * bytesPerPx];
        }
        return (data, w, h);
    }
}
