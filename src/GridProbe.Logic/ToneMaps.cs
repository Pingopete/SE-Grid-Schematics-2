namespace GridProbe;

// Pure array->tone mappings for the three display modes. Kept dependency-free
// so they can be verified offline against synthetic ships.
//
// Thickness is measured in sixteenths of a cell, so it is already continuous
// and maps straight to a smooth ramp. Runs and voids are whole-cell COUNTS —
// small integers — and mapping those directly posterizes the image: each count
// owns a slab of the tone range, leaving unused gaps between them. They are
// therefore smoothed across neighbouring columns first, which turns a coarse
// count into a continuous field without inventing detail (a column crossing
// 3 layers beside one crossing 4 really is somewhere between the two).
//
// All ranges are set from percentiles rather than min/max, so a single outlier
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

    // Structural layers crossed by the view ray. The layer count carries the
    // meaning; thickness adds gentle interior texture within a layer so flat
    // regions still read as surfaces rather than blank plates.
    public static byte[,] BuildXRay(int[,] runs, int[,] thick)
    {
        int w = runs.GetLength(0), h = runs.GetLength(1);
        var smooth = SmoothMasked(runs, thick, 2);

        int maxT = 1;
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                if (thick[x, y] > maxT) maxT = thick[x, y];

        var raw = new float[w, h];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                if (thick[x, y] <= 0) continue;
                raw[x, y] = smooth[x, y] + 0.35f * (float)Math.Sqrt(thick[x, y] / (double)maxT);
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
    public static byte[,] BuildVoids(int[,] voids, int[,] thick)
    {
        int w = voids.GetLength(0), h = voids.GetLength(1);
        var smooth = SmoothMasked(voids, thick, 2);

        var (tLo, tHi) = Percentiles(thick, thick, 0.02, 0.98);
        float vHi = PercentileNonZero(smooth, 0.95);

        var tones = new byte[w, h];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                if (thick[x, y] <= 0) continue;
                double tn = Math.Clamp((thick[x, y] - tLo) / (tHi - tLo), 0.0, 1.0);
                double vn = vHi > 0 ? Math.Clamp(smooth[x, y] / vHi, 0.0, 1.0) : 0.0;
                double tone = 30 + 90.0 * Math.Sqrt(tn) + 135.0 * Math.Sqrt(vn);
                tones[x, y] = (byte)Math.Min(255.0, tone);
            }
        return tones;
    }

    // 3x3 tent average over OCCUPIED columns only. Skipping empty neighbours
    // matters: averaging across the silhouette would drag edge values toward
    // zero and eat the outline.
    private static float[,] SmoothMasked(int[,] src, int[,] occ, int passes)
    {
        int w = src.GetLength(0), h = src.GetLength(1);
        var cur = new float[w, h];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                cur[x, y] = src[x, y];

        var next = new float[w, h];
        for (int p = 0; p < passes; p++)
        {
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                {
                    if (occ[x, y] <= 0) { next[x, y] = 0f; continue; }
                    float total = 0f, weight = 0f;
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            int sx = x + dx, sy = y + dy;
                            if (sx < 0 || sy < 0 || sx >= w || sy >= h) continue;
                            if (occ[sx, sy] <= 0) continue;
                            float wgt = dx == 0 && dy == 0 ? 4f : (dx == 0 || dy == 0 ? 2f : 1f);
                            total += cur[sx, sy] * wgt;
                            weight += wgt;
                        }
                    next[x, y] = weight > 0f ? total / weight : cur[x, y];
                }
            (cur, next) = (next, cur);
        }
        return cur;
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
