namespace GridProbe;

// Pure array->tone mappings for the three display modes. Kept dependency-free
// so they can be verified offline against synthetic ships.
//
// All three modes are fed CONTINUOUS quantities measured from the recovered
// block geometry, so their gradients come from the shape itself rather than
// from any post-hoc smoothing. Thickness is material length along the ray;
// layers and voids come from the same sub-cell depth spans, where a slope's
// surface sits at a fractional depth and therefore slides smoothly from one
// column to the next.
//
// Ranges are set from percentiles rather than min/max, so a single outlier
// column — one deep shaft, one large bay — cannot compress everything else
// into a narrow band.
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

    // Structural layers crossed by the view ray, already continuous: gaps are
    // measured at sub-cell depth and weighted by how open they are, so the
    // value rises smoothly as a gap widens instead of stepping when it appears.
    // Thickness adds gentle interior texture so flat regions still read as
    // surfaces rather than blank plates.
    public static byte[,] BuildXRay(float[,] layers, int[,] thick)
    {
        int w = layers.GetLength(0), h = layers.GetLength(1);

        int maxT = 1;
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                if (thick[x, y] > maxT) maxT = thick[x, y];

        var raw = new float[w, h];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                if (thick[x, y] <= 0) continue;
                raw[x, y] = layers[x, y] + 0.35f * (float)Math.Sqrt(thick[x, y] / (double)maxT);
            }

        var (lo, hi) = Percentiles(raw, thick, 0.02, 0.98);
        var tones = new byte[w, h];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                if (thick[x, y] <= 0) continue;
                double n = Math.Clamp((raw[x, y] - lo) / (hi - lo), 0.0, 1.0);
                tones[x, y] = (byte)Math.Min(255.0, 45 + 210.0 * Math.Sqrt(n));
            }
        return tones;
    }

    // Enclosed empty space, emphasized over the structure that contains it.
    // Structure and voids share ONE continuous ramp rather than two disjoint
    // bands: the hull gets a real range to show its detail in, and void volume
    // adds brightness on top of it, so there is no step where the two meet.
    public static byte[,] BuildVoids(float[,] voids, int[,] thick)
    {
        int w = voids.GetLength(0), h = voids.GetLength(1);

        var (tLo, tHi) = Percentiles(thick, thick, 0.02, 0.98);
        float vHi = PercentileNonZero(voids, 0.95);

        var tones = new byte[w, h];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                if (thick[x, y] <= 0) continue;
                double tn = Math.Clamp((thick[x, y] - tLo) / (tHi - tLo), 0.0, 1.0);
                double vn = vHi > 0 ? Math.Clamp(voids[x, y] / vHi, 0.0, 1.0) : 0.0;
                double tone = 30 + 90.0 * Math.Sqrt(tn) + 135.0 * Math.Sqrt(vn);
                tones[x, y] = (byte)Math.Min(255.0, tone);
            }
        return tones;
    }

    private static (double Lo, double Hi) Percentiles(float[,] v, int[,] occ, double loP, double hiP)
    {
        var list = Collect(v, occ);
        if (list.Count == 0) return (0.0, 1.0);
        list.Sort();
        double lo = list[(int)(list.Count * loP)];
        double hi = list[Math.Min(list.Count - 1, (int)(list.Count * hiP))];
        return hi > lo ? (lo, hi) : (lo, lo + 1e-6);
    }

    private static (double Lo, double Hi) Percentiles(int[,] v, int[,] occ, double loP, double hiP)
    {
        int w = v.GetLength(0), h = v.GetLength(1);
        var f = new float[w, h];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                f[x, y] = v[x, y];
        return Percentiles(f, occ, loP, hiP);
    }

    // Upper percentile of the non-zero values only: most columns enclose no
    // void at all, so including them would drag the reference to zero.
    private static float PercentileNonZero(float[,] v, double p)
    {
        int w = v.GetLength(0), h = v.GetLength(1);
        var list = new List<float>();
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                if (v[x, y] > 0.001f) list.Add(v[x, y]);
        if (list.Count == 0) return 0f;
        list.Sort();
        return list[Math.Min(list.Count - 1, (int)(list.Count * p))];
    }

    private static List<float> Collect(float[,] v, int[,] occ)
    {
        int w = v.GetLength(0), h = v.GetLength(1);
        var list = new List<float>(1024);
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                if (occ[x, y] > 0) list.Add(v[x, y]);
        return list;
    }
}
