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
// Every mode ends in the SAME step: Ramp() fits the display range to that
// mode's own measured field. Nothing downstream rescales — ToneBands places
// its iso-levels across whatever range arrives and renders each band at its
// absolute tone — so if a mode hands over a field that only spans 40..110,
// the panel only ever shows 40..110. Getting the range right is this file's
// job and nowhere else's.
internal static class ToneMaps
{
    // Dimmest tone an occupied column may take. Not zero: a column that holds
    // material has to stay distinguishable from empty space on a black panel.
    private const double Floor = 36.0;
    private const double Range = 255.0 - Floor;

    // Total material along the view ray.
    public static byte[,] BuildThickness(int[,] v)
    {
        int w = v.GetLength(0), h = v.GetLength(1);
        var raw = new float[w, h];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                raw[x, y] = v[x, y];
        return Ramp(raw, v, 0.01, 0.99, "thickness");
    }

    // Structural layers crossed by the view ray, already continuous: gaps are
    // measured at sub-cell depth and weighted by how open they are, so the
    // value rises smoothly as a gap widens instead of stepping when it appears.
    // Thickness adds gentle interior texture so flat regions still read as
    // surfaces rather than blank plates.
    public static byte[,] BuildXRay(float[,] layers, int[,] thick)
    {
        int w = layers.GetLength(0), h = layers.GetLength(1);
        var tn = Unit(thick, thick, 0.02, 0.98);

        var raw = new float[w, h];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                if (thick[x, y] <= 0) continue;
                raw[x, y] = layers[x, y] + 0.35f * (float)Math.Sqrt(tn[x, y]);
            }
        return Ramp(raw, thick, 0.02, 0.98, "xray");
    }

    // Enclosed empty space, emphasized over the structure that contains it.
    //
    // The hull is a dim ghost spanning Floor..HullTop; the void reading then
    // carries the column the REST of the way to white. Written as a blend
    // rather than a sum, so the emptiest column reaches 255 whatever its hull
    // thickness happens to be. Summing two ramps (as this used to) meant white
    // required a column to sit at the thickness maximum AND the void maximum
    // at once, which almost never happens — the mode topped out around 240 and
    // its brightest quarter went unused.
    //
    // There is still no step where the two meet: at zero void the blend sits
    // exactly on the hull tone.
    private const double HullTop = 120.0;

    public static byte[,] BuildVoids(float[,] voids, int[,] thick)
    {
        int w = voids.GetLength(0), h = voids.GetLength(1);
        var tn = Unit(thick, thick, 0.02, 0.98);

        // Voids are measured against the non-zero columns only: most columns
        // enclose nothing at all, so including them drags the reference to zero.
        float vHi = PercentileNonZero(voids, 0.95);

        var tones = new byte[w, h];
        int used = 0, tMin = 255, tMax = 0;
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                if (thick[x, y] <= 0) continue;
                double hull = Floor + (HullTop - Floor) * Math.Sqrt(tn[x, y]);
                double vn = vHi > 0 ? Math.Clamp(voids[x, y] / vHi, 0.0, 1.0) : 0.0;
                int t = (int)Math.Round(hull + (255.0 - hull) * Math.Sqrt(vn));
                tones[x, y] = (byte)Math.Clamp(t, 1, 255);
                used++;
                if (t < tMin) tMin = t;
                if (t > tMax) tMax = t;
            }
        if (used == 0) { tMin = 0; tMax = 0; }
        ProbeLog.Line($"Tone voids: {used} cols, void ref {vHi:F2} cells -> tone [{tMin}..{tMax}].");
        return tones;
    }

    // THE range control for every mode. A mode builds a raw continuous field;
    // this decides what black and white mean by fitting the ramp to that
    // field's own distribution, so the panel uses its full contrast whatever
    // the ship is and whichever way it is viewed.
    //
    // The ends come from percentiles rather than the outright min and max
    // because one freak column — a single shaft down the hull's long axis, one
    // deep bay — would otherwise own the top of the range and squash every
    // other column into the bottom of it. At 1%/99% the ends ARE the true min
    // and max for any ship whose extremes are more than a handful of columns
    // wide; values outside them clamp, so nothing is lost, it is just not
    // allowed to set the scale on its own.
    //
    // The sqrt is perceptual, not corrective: equal steps in tone then read as
    // roughly equal steps in brightness.
    private static byte[,] Ramp(float[,] raw, int[,] occ, double loP, double hiP, string tag)
    {
        int w = raw.GetLength(0), h = raw.GetLength(1);
        var (lo, hi) = Percentiles(raw, occ, loP, hiP);
        double span = hi - lo;

        var tones = new byte[w, h];
        int used = 0, tMin = 255, tMax = 0;
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                if (occ[x, y] <= 0) continue;
                double n = span > 0 ? Math.Clamp((raw[x, y] - lo) / span, 0.0, 1.0) : 1.0;
                int t = (int)Math.Round(Floor + Range * Math.Sqrt(n));
                tones[x, y] = (byte)Math.Clamp(t, 1, 255);
                used++;
                if (t < tMin) tMin = t;
                if (t > tMax) tMax = t;
            }
        if (used == 0) { tMin = 0; tMax = 0; }
        ProbeLog.Line($"Tone {tag}: {used} cols, raw [{lo:F2}..{hi:F2}] -> tone [{tMin}..{tMax}].");
        return tones;
    }

    // Field rescaled to 0..1 by its own percentiles, for use as a term inside
    // a larger expression (the final fit still happens in Ramp).
    private static float[,] Unit(int[,] v, int[,] occ, double loP, double hiP)
    {
        int w = v.GetLength(0), h = v.GetLength(1);
        var (lo, hi) = Percentiles(v, occ, loP, hiP);
        double span = hi - lo;
        var u = new float[w, h];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                if (occ[x, y] <= 0) continue;
                u[x, y] = span > 0 ? (float)Math.Clamp((v[x, y] - lo) / span, 0.0, 1.0) : 1f;
            }
        return u;
    }

    private static (double Lo, double Hi) Percentiles(float[,] v, int[,] occ, double loP, double hiP)
    {
        var list = Collect(v, occ);
        if (list.Count == 0) return (0.0, 1.0);
        list.Sort();
        double lo = list[Math.Clamp((int)(list.Count * loP), 0, list.Count - 1)];
        double hi = list[Math.Clamp((int)(list.Count * hiP), 0, list.Count - 1)];
        // Degenerate (a flat plate: every column identical) is reported as-is.
        // Callers read hi == lo as "no variation" and light the whole field at
        // full tone, which is the honest answer — a fudged epsilon range would
        // instead push it all to the dark floor.
        return hi > lo ? (lo, hi) : (lo, lo);
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
