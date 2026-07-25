namespace GridProbe;

// Pure array->tone mappings for the three display modes. Kept dependency-free
// so they can be verified offline against synthetic ships.
internal static class ToneMaps
{
    public static byte[,] BuildThickness(int[,] v)
    {
        int w = v.GetLength(0), h = v.GetLength(1);
        int minV = int.MaxValue, maxV = 1;
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                int t = v[x, y];
                if (t <= 0) continue;
                if (t < minV) minV = t;
                if (t > maxV) maxV = t;
            }
        if (minV == int.MaxValue) minV = 0;
        double range = maxV - minV;
        var tones = new byte[w, h];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                int t = v[x, y];
                if (t <= 0) continue;
                double n = range > 0 ? (t - minV) / range : 1.0;
                tones[x, y] = (byte)Math.Min(255.0, 40 + 215.0 * Math.Sqrt(n));
            }
        return tones;
    }

    // Every occupied column is a readable ghost; brightness steps up with each
    // additional structure layer the view ray crosses (solid<->void transitions).
    // The continuous thickness signal is blended in within each layer band so
    // the image keeps smooth interior shading instead of flat posterized zones.
    public static byte[,] BuildXRay(int[,] runs, int[,] thick)
    {
        int w = runs.GetLength(0), h = runs.GetLength(1);
        int maxR = 1, maxT = 1;
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                if (runs[x, y] > maxR) maxR = runs[x, y];
                if (thick[x, y] > maxT) maxT = thick[x, y];
            }
        var tones = new byte[w, h];
        int layers = Math.Max(1, maxR);
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                int r = runs[x, y];
                int t = thick[x, y];
                if (r <= 0 && t <= 0) continue;
                if (r <= 0) r = 1; // occupied but channel missed it: ghost layer
                // Fully continuous X-ray: layer count and material depth blend
                // into one 45..255 ramp, so shading stays smooth everywhere.
                double v = (r - 1 + Math.Sqrt(t / (double)maxT)) / layers;
                tones[x, y] = (byte)Math.Min(255.0, 45 + 210.0 * Math.Sqrt(v));
            }
        return tones;
    }

    // Enclosed empty space bright over a dim hull ghost. Void brightness ramps
    // smoothly with enclosed volume; the ghost carries thickness detail so the
    // surrounding structure stays readable.
    public static byte[,] BuildVoids(int[,] voids, int[,] thick)
    {
        int w = voids.GetLength(0), h = voids.GetLength(1);
        int maxV = 1, maxT = 1;
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                if (voids[x, y] > maxV) maxV = voids[x, y];
                if (thick[x, y] > maxT) maxT = thick[x, y];
            }
        var tones = new byte[w, h];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                int vd = voids[x, y];
                int t = thick[x, y];
                if (vd > 0)
                    tones[x, y] = (byte)Math.Min(255.0, 110 + 145.0 * Math.Sqrt(vd / (double)maxV));
                else if (t > 0)
                    tones[x, y] = (byte)(35 + 40.0 * Math.Sqrt(t / (double)maxT)); // 35..75 ghost with structure detail
            }
        return tones;
    }
}
