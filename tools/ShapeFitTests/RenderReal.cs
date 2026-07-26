using GridProbe;

// Diagnostic: take a REAL coverage dump from the game, run it through the exact
// band pipeline, and rasterize the resulting polygons. Anything visible here is
// produced by the geometry; anything only visible in game is produced by the
// renderer. Run with:  dotnet run -- render <cov.bmp> <out.bmp> <x0> <y0> <w> <h> <scale>
internal static class RenderReal
{
    // dotnet run -- bands <mode_xxx.bmp>
    // Builds bands from a real exported TONE field and reports the geometry
    // cost, so band settings can be tuned without waiting on an in-game mode
    // switch (band sets are cached per view+mode).
    public static int Bands(string[] args)
    {
        var (img, iw, ih) = ReadGray8(args[1]);
        var tone = new byte[iw, ih];
        var cov = new byte[iw, ih];
        int occupied = 0;
        for (int x = 0; x < iw; x++)
            for (int y = 0; y < ih; y++)
            {
                byte v = img[y * iw + x];
                tone[x, y] = v;
                if (v > 0) { cov[x, y] = (byte)BlockShapes.FracUnits; occupied++; }
            }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var set = ToneBands.Build(tone, cov);
        int loops = 0;
        foreach (var b in set.Bands) loops += b.Loops.Count;
        Console.WriteLine($"{System.IO.Path.GetFileName(args[1])}: {iw}x{ih}, {occupied} occupied");
        Console.WriteLine($"  bands={set.Bands.Count} loops={loops} segs={string.Join("/", set.TotalSegs)} built in {sw.ElapsedMilliseconds} ms");
        return 0;
    }

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

    // Ground truth for the band stack: composite the bands exactly as the GPU
    // does — source-over, in draw order, over a black panel — and compare the
    // result against the tone field they were built from. Any drift between
    // "what the tone map asked for" and "what the stack actually paints" shows
    // up here, with no game running and nothing to eyeball.
    //   verify <tonefield.bmp>
    public static int Verify(string[] args)
    {
        var (img, w, h) = ReadGray8(args[1]);
        var tone = new byte[w, h];
        var cov = new byte[w, h];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                byte v = img[y * w + x];
                tone[x, y] = v;
                if (v > 0) cov[x, y] = BlockShapes.FracUnits;
            }

        var set = ToneBands.Build(tone, cov);
        Console.WriteLine($"{Path.GetFileName(args[1])}: {w}x{h}, {set.Bands.Count} bands");

        // Composite: A' = A + a(1-A), per pixel, bands in order.
        var acc = new double[w, h];
        foreach (var band in set.Bands)
        {
            double a = band.Alpha / 255.0;
            var inside = Rasterize(band, w, h);
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    if (inside[x, y]) acc[x, y] = acc[x, y] + a * (1.0 - acc[x, y]);
        }

        // Compare where the field says there is material.
        double sum = 0, maxErr = 0; int n = 0, gotMin = 255, wantMin = 255, gotMax = 0, wantMax = 0;
        var errHist = new int[64];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                int want = tone[x, y];
                if (want == 0) continue;
                int got = (int)Math.Round(acc[x, y] * 255.0);
                double e = Math.Abs(got - want);
                sum += e; if (e > maxErr) maxErr = e; n++;
                errHist[Math.Min(63, (int)(e / 4))]++;
                if (want < wantMin) wantMin = want;
                if (want > wantMax) wantMax = want;
                if (got < gotMin) gotMin = got;
                if (got > gotMax) gotMax = got;
            }
        Console.WriteLine($"  tone field  : [{wantMin}..{wantMax}]");
        Console.WriteLine($"  stack paints: [{gotMin}..{gotMax}]");
        Console.WriteLine($"  error: mean {sum / Math.Max(1, n):F1}, max {maxErr:F0}, over {n} px");

        // Where does the darkest material actually land?
        int dark = 0, darkPainted = 0;
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                if (tone[x, y] == 0 || tone[x, y] > wantMin + 8) continue;
                dark++; darkPainted += (int)Math.Round(acc[x, y] * 255.0);
            }
        if (dark > 0)
            Console.WriteLine($"  darkest {dark} px: field says ~{wantMin}, stack paints avg {darkPainted / (double)dark:F1}");
        return 0;
    }

    // Nonzero-winding scanline fill of one band's loops at full detail.
    private static bool[,] Rasterize(ToneBands.Band band, int w, int h)
    {
        var inside = new bool[w, h];
        var xs = new List<(float X, int Dir)>();
        for (int py = 0; py < h; py++)
        {
            float fy = py + 0.5f;
            xs.Clear();
            foreach (var loop in band.Loops)
            {
                var pts = loop.L[0];
                int m = pts.Length / 2;
                for (int i = 0; i < m; i++)
                {
                    int j = (i + 1) % m;
                    float ay = pts[i * 2 + 1], by = pts[j * 2 + 1];
                    if (ay == by) continue;
                    if (fy < Math.Min(ay, by) || fy >= Math.Max(ay, by)) continue;
                    float ax = pts[i * 2], bx = pts[j * 2];
                    xs.Add((ax + (bx - ax) * (fy - ay) / (by - ay), by > ay ? 1 : -1));
                }
            }
            if (xs.Count == 0) continue;
            xs.Sort((p, q) => p.X.CompareTo(q.X));
            int wind = 0;
            for (int i = 0; i < xs.Count - 1; i++)
            {
                wind += xs[i].Dir;
                if (wind == 0) continue;
                int x0 = Math.Max(0, (int)Math.Ceiling(xs[i].X - 0.5f));
                int x1 = Math.Min(w - 1, (int)Math.Floor(xs[i + 1].X - 0.5f));
                for (int x = x0; x <= x1; x++) inside[x, py] = true;
            }
        }
        return inside;
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
