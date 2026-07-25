namespace GridProbe;

// Resolution-independent display geometry. The per-mode tone field is turned
// ONCE into nested iso-band polygons (marching squares with interpolation, in
// cell space). Stacked translucent fills reproduce continuous depth shading;
// every zoom level then draws the SAME cached geometry through a pure
// transform — no regimes, no per-cell rectangles, no zoom-dependent look.
//
// Alpha math: band k fills the region tone >= L_k. Stacking white fills of
// alpha a over cumulative A gives A' = A + a*(1-A); each band's a is solved
// so the stack lands exactly on L_k/255 — identical compositing to the old
// single-pass texture (white at BlitBrightness, tone in alpha).
//
// Corner sharpening: marching squares cuts every sharp corner with a
// half-cell chamfer (one chord between two edge crossings — the cell data
// cannot say "corner" on its own). The FIELD can: where the boundary
// gradients at a chord's two endpoints diverge, the true silhouette has a
// vertex at the intersection of the two boundary lines. Parallel gradients
// (a genuine slope) are left untouched, so ramps stay exact diagonals.
//
// LODs: each loop is simplified (Douglas-Peucker) at fixed cell-space error
// bounds; the renderer picks the coarsest LOD whose error stays under ~1/3
// of an on-screen pixel. Same geometry, same rule at every zoom — just never
// more vertices than the screen can show.
internal static class ToneBands
{
    public const int Levels = 40;             // tone quantization steps
    private const int MaxSegsPerBand = 15000;
    // Cell-space error bound per detail tier. The renderer picks the coarsest
    // tier whose error stays under a third of an on-screen pixel, so the
    // simplification is invisible by construction at every zoom.
    public static readonly float[] LodTol = { 0f, 0.05f, 0.15f, 0.5f };

    public struct Loop
    {
        public float[][] L;                   // L[tier]; empty array = dropped here
        public float MinX, MinY, MaxX, MaxY;
    }

    public sealed class Band
    {
        public byte Alpha;                    // incremental alpha (stacked rendering)
        public byte Level;                    // absolute tone of this band's region
        public List<Loop> Loops = new();
        public int Segs0;                     // full-detail count (buffer sizing)
    }

    public sealed class BandSet
    {
        public List<Band> Bands = new();
        public int[] TotalSegs = new int[4];
    }

    // Cheap first-paint geometry: the silhouette alone (one band, ~5 ms). Shown
    // while the full band set builds so a panel never boots blank — and never
    // falls back to the tens-of-thousands-of-rects legacy path.
    public static BandSet BuildSilhouette(byte[,] tones, byte[,] cov)
    {
        var set = new BandSet();
        if (cov == null) return set;
        int tMin = 255;
        int w = tones.GetLength(0), h = tones.GetLength(1);
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                if (tones[x, y] > 0 && tones[x, y] < tMin) tMin = tones[x, y];
        if (tMin == 255) tMin = 120;
        double cum = 0;
        AddBand(set, cov, BlockShapes.FracUnits / 2f, Math.Min(1.0, tMin * 2.0 / 255.0), ref cum);
        return set;
    }

    // tones: display alpha field (0 = empty). cov: fractional footprint 0..16
    // whose interpolated contour is the TRUE silhouette; the display field is
    // tone*cov so shading feathers out to that same edge.
    public static BandSet Build(byte[,] tones, byte[,] cov)
    {
        int w = tones.GetLength(0), h = tones.GetLength(1);
        var set = new BandSet();

        int tMin = 255, fMax = 0;
        var f = new byte[w, h];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                int t = tones[x, y];
                if (t > 0 && t < tMin) tMin = t;
                int cf = cov == null ? (t > 0 ? BlockShapes.FracUnits : 0) : cov[x, y];
                int v = t * cf / BlockShapes.FracUnits;
                f[x, y] = (byte)v;
                if (v > fMax) fMax = v;
            }
        if (fMax == 0) return set;
        if (tMin > fMax) tMin = fMax;

        // Base band: the exact coverage silhouette at the tone floor.
        double cum = 0;
        if (cov != null)
            AddBand(set, cov, BlockShapes.FracUnits / 2f, tMin / 255.0, ref cum);
        else
            AddBand(set, f, Math.Max(1f, tMin * 0.5f), tMin / 255.0, ref cum);

        for (int k = 1; k < Levels; k++)
        {
            float iso = tMin + k * (fMax - tMin) / (float)(Levels - 1);
            if (iso <= tMin + 0.5f || iso > fMax) continue;
            AddBand(set, f, iso, iso / 255.0, ref cum);
        }
        return set;
    }

    private static void AddBand(BandSet set, byte[,] field, float iso, double target, ref double cum)
    {
        int a = (int)Math.Round(255.0 * (target - cum) / Math.Max(1e-6, 1.0 - cum));
        if (a <= 0) return;
        var loops = Contour.March(field, 0, 0, field.GetLength(0), field.GetLength(1), iso, 80000);
        if (loops.Count == 0) return;
        SharpenCorners(loops, field, iso);
        Orient(loops);

        var band = new Band
        {
            Alpha = (byte)Math.Min(255, a),
            Level = (byte)Math.Clamp((int)Math.Round(target * 255.0), 0, 255),
        };
        float eps = 0.02f;
        while (true)
        {
            band.Loops.Clear();
            band.Segs0 = 0;
            foreach (var loop in loops)
            {
                var pts = Decimate(loop, eps);
                if (pts == null) continue;
                var l = new Loop { L = new float[LodTol.Length][], MinX = float.MaxValue, MinY = float.MaxValue, MaxX = float.MinValue, MaxY = float.MinValue };
                l.L[0] = pts;
                for (int i = 0; i < pts.Length; i += 2)
                {
                    if (pts[i] < l.MinX) l.MinX = pts[i];
                    if (pts[i] > l.MaxX) l.MaxX = pts[i];
                    if (pts[i + 1] < l.MinY) l.MinY = pts[i + 1];
                    if (pts[i + 1] > l.MaxY) l.MaxY = pts[i + 1];
                }
                band.Segs0 += pts.Length / 2;
                band.Loops.Add(l);
            }
            if (band.Segs0 <= MaxSegsPerBand || eps > 0.3f) break;
            eps *= 3f;
        }
        if (band.Segs0 == 0) return;

        // Screen-error-bounded LOD ladder, each tier simplified from the last.
        for (int i = 0; i < band.Loops.Count; i++)
        {
            var l = band.Loops[i];
            set.TotalSegs[0] += l.L[0].Length / 2;
            for (int t = 1; t < LodTol.Length; t++)
            {
                var src = l.L[t - 1].Length >= 6 ? l.L[t - 1] : l.L[0];
                l.L[t] = BuildLod(l, src, LodTol[t]);
                set.TotalSegs[t] += l.L[t].Length / 2;
            }
            band.Loops[i] = l;
        }
        set.Bands.Add(band);
        cum += band.Alpha / 255.0 * (1.0 - cum);
    }

    private static float[] BuildLod(Loop l, float[] src, float tol)
    {
        // A loop smaller than the error bound contributes nothing at this LOD.
        if (l.MaxX - l.MinX < tol * 3f && l.MaxY - l.MinY < tol * 3f) return Array.Empty<float>();
        if (src.Length < 6) return Array.Empty<float>();
        return SimplifyDP(src, tol);
    }

    // Insert true corner vertices: for short chords whose endpoint boundary
    // normals (field gradients) diverge, add the intersection of the two
    // boundary lines. Binary block corners snap exactly; smooth ramps have
    // parallel normals and are skipped.
    private static void SharpenCorners(List<List<(float X, float Y)>> loops, byte[,] field, float iso)
    {
        int w = field.GetLength(0), h = field.GetLength(1);

        float At(int i, int j) => i < 0 || j < 0 || i >= w || j >= h ? 0f : field[i, j];
        float Sample(float x, float y)
        {
            float fx = x - 0.5f, fy = y - 0.5f;
            int i = (int)MathF.Floor(fx), j = (int)MathF.Floor(fy);
            float tx = fx - i, ty = fy - j;
            return At(i, j) * (1 - tx) * (1 - ty) + At(i + 1, j) * tx * (1 - ty)
                 + At(i, j + 1) * (1 - tx) * ty + At(i + 1, j + 1) * tx * ty;
        }
        (float X, float Y) Grad(float x, float y)
        {
            const float e = 0.35f;
            return (Sample(x + e, y) - Sample(x - e, y), Sample(x, y + e) - Sample(x, y - e));
        }

        var buf = new List<(float X, float Y)>(256);
        foreach (var loop in loops)
        {
            int n = loop.Count;
            if (n < 3) continue;
            buf.Clear();
            for (int i = 0; i < n; i++)
            {
                var a = loop[i];
                var b = loop[(i + 1) % n];
                buf.Add(a);
                float dx = b.X - a.X, dy = b.Y - a.Y;
                float len2 = dx * dx + dy * dy;
                if (len2 > 1.21f || len2 < 1e-6f) continue; // chamfer chords are sub-cell

                var ga = Grad(a.X, a.Y);
                var gb = Grad(b.X, b.Y);
                float la = MathF.Sqrt(ga.X * ga.X + ga.Y * ga.Y);
                float lb = MathF.Sqrt(gb.X * gb.X + gb.Y * gb.Y);
                if (la < 1e-4f || lb < 1e-4f) continue;
                float dot = (ga.X * gb.X + ga.Y * gb.Y) / (la * lb);
                if (dot > 0.82f) continue;                  // near-parallel: genuine slope

                float det = ga.X * gb.Y - ga.Y * gb.X;
                if (Math.Abs(det) < 1e-6f) continue;
                float c1 = ga.X * a.X + ga.Y * a.Y;
                float c2 = gb.X * b.X + gb.Y * b.Y;
                float px = (c1 * gb.Y - c2 * ga.Y) / det;
                float py = (ga.X * c2 - gb.X * c1) / det;
                float da = (px - a.X) * (px - a.X) + (py - a.Y) * (py - a.Y);
                float db = (px - b.X) * (px - b.X) + (py - b.Y) * (py - b.Y);
                if (da > 1f || db > 1f) continue;           // implausible intersection
                buf.Add((px, py));
            }
            loop.Clear();
            loop.AddRange(buf);
        }
    }

    // Drop collinear points: marching squares emits a vertex per cell edge,
    // but straight runs (and sharpened corners' flanks) collapse to single
    // segments — most of the per-frame draw cost disappears here.
    private static float[] Decimate(List<(float X, float Y)> loop, float eps)
    {
        int n = loop.Count;
        if (n < 3) return null;
        var keep = new List<(float X, float Y)>(n);
        for (int i = 0; i < n; i++)
        {
            var a = keep.Count > 0 ? keep[^1] : loop[(i - 1 + n) % n];
            var b = loop[i];
            var c = loop[(i + 1) % n];
            float cross = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
            if (Math.Abs(cross) > eps) keep.Add(b);
        }
        if (keep.Count < 3) return null;
        var pts = new float[keep.Count * 2];
        for (int i = 0; i < keep.Count; i++)
        {
            pts[i * 2] = keep[i].X;
            pts[i * 2 + 1] = keep[i].Y;
        }
        return pts;
    }

    // Douglas-Peucker on a closed ring (anchors: point 0 and the farthest
    // point from it). Returns the original array when nothing simplifies.
    private static float[] SimplifyDP(float[] pts, float tol)
    {
        int m = pts.Length / 2;
        if (m <= 4) return pts;
        float ax = pts[0], ay = pts[1];
        int far = m / 2;
        float best = -1f;
        for (int i = 1; i < m; i++)
        {
            float dx = pts[i * 2] - ax, dy = pts[i * 2 + 1] - ay;
            float d = dx * dx + dy * dy;
            if (d > best) { best = d; far = i; }
        }
        var keep = new bool[m];
        keep[0] = keep[far] = true;
        var stack = new Stack<(int A, int B)>();
        stack.Push((0, far));
        stack.Push((far, m)); // wraps: index m == index 0
        float tol2 = tol * tol;
        while (stack.Count > 0)
        {
            var (s0, s1) = stack.Pop();
            if (s1 - s0 < 2) continue;
            float x0 = pts[(s0 % m) * 2], y0 = pts[(s0 % m) * 2 + 1];
            float x1 = pts[(s1 % m) * 2], y1 = pts[(s1 % m) * 2 + 1];
            float ex = x1 - x0, ey = y1 - y0;
            float el2 = ex * ex + ey * ey;
            int worst = -1;
            float wd = tol2;
            for (int i = s0 + 1; i < s1; i++)
            {
                float px = pts[(i % m) * 2] - x0, py = pts[(i % m) * 2 + 1] - y0;
                float d2;
                if (el2 < 1e-9f) d2 = px * px + py * py;
                else
                {
                    float t = Math.Clamp((px * ex + py * ey) / el2, 0f, 1f);
                    float qx = px - t * ex, qy = py - t * ey;
                    d2 = qx * qx + qy * qy;
                }
                if (d2 > wd) { wd = d2; worst = i; }
            }
            if (worst >= 0)
            {
                keep[worst % m] = true;
                stack.Push((s0, worst));
                stack.Push((worst, s1));
            }
        }
        int kept = 0;
        for (int i = 0; i < m; i++) if (keep[i]) kept++;
        if (kept < 3) return Array.Empty<float>();
        if (kept == m) return pts;
        var outPts = new float[kept * 2];
        int n = 0;
        for (int i = 0; i < m; i++)
            if (keep[i])
            {
                outPts[n * 2] = pts[i * 2];
                outPts[n * 2 + 1] = pts[i * 2 + 1];
                n++;
            }
        return outPts;
    }

    // Orient loops for nonzero-winding fills: solid-enclosing loops positive,
    // holes negative — one DrawFill per band then renders solids with punched
    // holes.
    //
    // Solidity is decided by NESTING, not by sampling the field. Contour loops
    // never cross, so material alternates with depth: a loop directly inside an
    // odd number of others bounds a hole. Reading the field near a loop instead
    // is unreliable — the cells there carry partial coverage by definition, and
    // one misread flips a loop's winding so its gap silently fills in.
    private static void Orient(List<List<(float X, float Y)>> loops)
    {
        int n = loops.Count;
        var rep = new (float X, float Y)[n];
        var has = new bool[n];
        var minX = new float[n]; var maxX = new float[n];
        var minY = new float[n]; var maxY = new float[n];

        for (int i = 0; i < n; i++)
        {
            var loop = loops[i];
            if (loop.Count < 3) continue;
            float lo = float.MaxValue, hi = float.MinValue, lox = float.MaxValue, hix = float.MinValue;
            foreach (var p in loop)
            {
                if (p.Y < lo) lo = p.Y;
                if (p.Y > hi) hi = p.Y;
                if (p.X < lox) lox = p.X;
                if (p.X > hix) hix = p.X;
            }
            minY[i] = lo; maxY[i] = hi; minX[i] = lox; maxX[i] = hix;

            // A point guaranteed strictly inside: crossings along the loop's
            // mid-height alternate outside/inside, so the centre of the first
            // interior span always lies within it.
            float my = (lo + hi) * 0.5f;
            var xs = new List<float>(8);
            for (int k = 0; k < loop.Count; k++)
            {
                var a = loop[k];
                var b = loop[(k + 1) % loop.Count];
                if ((a.Y <= my) == (b.Y <= my)) continue;
                xs.Add(a.X + (b.X - a.X) * (my - a.Y) / (b.Y - a.Y));
            }
            if (xs.Count < 2) continue;
            xs.Sort();
            // Just INSIDE the loop's own boundary, not the middle of the span:
            // a polygon's interior also spans its holes, so a mid-span point can
            // land inside a nested loop and report the wrong nesting depth.
            float eps = MathF.Min(0.01f, (xs[1] - xs[0]) * 0.25f);
            rep[i] = (xs[0] + eps, my);
            has[i] = true;
        }

        for (int i = 0; i < n; i++)
        {
            var loop = loops[i];
            if (loop.Count < 3) continue;
            double area = 0;
            for (int k = 0; k < loop.Count; k++)
            {
                var a = loop[k];
                var b = loop[(k + 1) % loop.Count];
                area += a.X * b.Y - b.X * a.Y;
            }

            bool solid = true;
            if (has[i])
            {
                int depth = 0;
                for (int j = 0; j < n; j++)
                {
                    if (j == i || loops[j].Count < 3) continue;
                    if (rep[i].X < minX[j] || rep[i].X > maxX[j] || rep[i].Y < minY[j] || rep[i].Y > maxY[j]) continue;
                    if (Contains(loops[j], rep[i].X, rep[i].Y)) depth++;
                }
                solid = (depth & 1) == 0;
            }
            if (solid != (area > 0)) loop.Reverse();
        }
    }

    private static bool Contains(List<(float X, float Y)> loop, float px, float py)
    {
        bool inside = false;
        for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
        {
            var a = loop[i];
            var b = loop[j];
            if (a.Y > py != b.Y > py &&
                px < (b.X - a.X) * (py - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }

    // Is this loop the boundary of solid material, or of a hole in it?
    //
    // Cast a scanline at the loop's mid height and sample the field at the
    // MIDDLE of the first span inside the loop. Sampling just past the first
    // crossing instead lands in the boundary cell, whose coverage is partial by
    // definition — on a diagonal edge it reads as solid about half the time,
    // which flips a hole's winding so it never punches and the gap fills in.
    private static bool EnclosesSolid(List<(float X, float Y)> loop, byte[,] field, float iso)
    {
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var p in loop) { if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y; }
        float my = (minY + maxY) / 2f;

        var xs = new List<float>(8);
        for (int i = 0; i < loop.Count; i++)
        {
            var a = loop[i];
            var b = loop[(i + 1) % loop.Count];
            if ((a.Y <= my) == (b.Y <= my)) continue;
            xs.Add(a.X + (b.X - a.X) * (my - a.Y) / (b.Y - a.Y));
        }
        if (xs.Count < 2) return true;
        xs.Sort();

        // Crossings alternate outside/inside, so [xs0, xs1] is interior. Sample
        // its centre — the point furthest from either edge.
        float sx = (xs[0] + xs[1]) * 0.5f;
        int cx = Math.Clamp((int)sx, 0, field.GetLength(0) - 1);
        int cy = Math.Clamp((int)my, 0, field.GetLength(1) - 1);
        return field[cx, cy] >= iso;
    }
}
