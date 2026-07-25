using Keen.VRage.Library.Mathematics;

namespace GridProbe;

// Recovers each block definition's ANALYTIC solid from the game's own cell
// occupancy, with no hand-written shape library and no name matching.
//
// The engine voxelizes every block into 25 cm cells (a 2.5 m block is 10x10x10).
// A flat face cutting through that grid leaves a staircase — and that staircase
// still encodes the exact plane that produced it. So instead of guessing which
// canonical solid a block "looks like", we fit the half-spaces directly:
//
//   solid = AABB  intersect  { l : n_k . l <= d_k }   for a few planes k
//
// found greedily, each plane chosen to maximise IoU against the true cell set.
// This recovers 1:1 slopes, 1:2 long slopes, corners, tips, half slabs, inverted
// corners and mod blocks alike — anything convex — to the precision the cell
// data allows (~1/10 cell on a standard block). Blocks that are genuinely not
// convex (trusses, handrails, stairs, drills) fail the accuracy bar and fall
// back to their own cell boxes, which for those shapes is the honest answer.
//
// Fitting happens in the block's LOCAL frame, so one fit per definition serves
// every orientation.
internal static class BlockShapes
{
    public const int FracUnits = 16;

    private const double AcceptIoU = 0.97;   // an analytic solid should be near-exact
    private const double MinGain = 0.004;    // stop adding planes below this
    private const int MaxPlanes = 6;

    private sealed class DefShape
    {
        public float[] Planes;               // 4 floats per plane: nx, ny, nz, d (inside: n.l <= d)
        public string Label = "Full";
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<object, DefShape> _defs = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(object Def, int Basis, int X, int Y, int Z), byte[,,]> _stamps = new();
    // Integer normals up to +-2 cover the usual armour angles (faces, 45s,
    // 1:2 long slopes, corner diagonals); +-4 is the fallback for anything
    // built to a less common ratio.
    private static readonly float[][] NormalsCoarse = BuildNormals(2);
    private static readonly float[][] NormalsFine = BuildNormals(4);

    public static void ResetCaches()
    {
        _defs.Clear();
        _stamps.Clear();
        ProbeLog.Line("BlockShapes: caches cleared, shape recovery will re-run.");
    }

    public static string Describe(object def)
        => def != null && _defs.TryGetValue(def, out var d) ? d.Label : "unresolved";

    // Fractional stamp for this block, or null when no analytic solid was
    // recovered (caller uses the block's own cell boxes).
    public static byte[,,] GetStamp(object defKey, IntegerOrientation orient, Vector3I ext, Func<List<(Vector3I Lo, Vector3I Hi)>> ownBoxesRel)
    {
        if (defKey == null) return null;
        var basis = BasisOf(orient);
        if (!_defs.TryGetValue(defKey, out var info))
        {
            info = ResolveShape(defKey, ext, basis, ownBoxesRel());
            _defs[defKey] = info;
        }
        if (info.Planes == null) return null;
        var key = (defKey, BasisHash(basis), ext.X, ext.Y, ext.Z);
        return _stamps.GetOrAdd(key, _ => BuildStamp(info.Planes, basis, ext));
    }

    private static DefShape ResolveShape(object defKey, Vector3I ext, sbyte[] basis, List<(Vector3I Lo, Vector3I Hi)> boxesRel)
    {
        var result = new DefShape();
        try
        {
            int nx = ext.X, ny = ext.Y, nz = ext.Z;
            int total = nx * ny * nz;
            if (total < 8 || total > 64000 || boxesRel == null || boxesRel.Count == 0) return result;

            // Ground truth: the engine's own occupied cells.
            var truth = new bool[total];
            int truthCells = 0;
            foreach (var (lo, hi) in boxesRel)
                for (int x = Math.Max(0, lo.X); x <= Math.Min(nx - 1, hi.X); x++)
                    for (int y = Math.Max(0, lo.Y); y <= Math.Min(ny - 1, hi.Y); y++)
                        for (int z = Math.Max(0, lo.Z); z <= Math.Min(nz - 1, hi.Z); z++)
                        {
                            int i = (x * ny + y) * nz + z;
                            if (!truth[i]) { truth[i] = true; truthCells++; }
                        }
            // Only skip fitting for blocks that are essentially full. A loose
            // bar here silently swallows small chamfers and corner cuts — the
            // exact detail this recovery exists to capture.
            if (truthCells >= total * 0.995) { result.Label = "Solid"; return result; }

            // Cell centres in the block's local frame, normalised to [-0.5, 0.5].
            var lx = new float[total];
            var ly = new float[total];
            var lz = new float[total];
            for (int x = 0; x < nx; x++)
                for (int y = 0; y < ny; y++)
                    for (int z = 0; z < nz; z++)
                    {
                        float px = (x + 0.5f) / nx - 0.5f;
                        float py = (y + 0.5f) / ny - 0.5f;
                        float pz = (z + 0.5f) / nz - 0.5f;
                        var l = MulT(basis, px, py, pz);
                        int i = (x * ny + y) * nz + z;
                        lx[i] = l.X; ly[i] = l.Y; lz[i] = l.Z;
                    }

            // Common slope ratios first; escalate to finer angles when the
            // coarse family either fails or needs several planes where one
            // better-angled cut would do. Fewer planes is the simpler — and
            // for a real block, the correct — explanation.
            float cellSpan = Math.Max(1f / nx, Math.Max(1f / ny, 1f / nz));
            var (planes, iou) = SupportFit(NormalsCoarse, truth, truthCells, lx, ly, lz, total, cellSpan);
            if (iou < AcceptIoU || planes.Count > 1)
            {
                var (planes2, iou2) = SupportFit(NormalsFine, truth, truthCells, lx, ly, lz, total, cellSpan);
                bool better = iou2 >= AcceptIoU && iou >= AcceptIoU
                    ? planes2.Count < planes.Count
                    : iou2 > iou;
                if (better) { planes = planes2; iou = iou2; }
            }

            string defName = DefName(defKey);
            if (planes.Count > 0 && iou >= AcceptIoU)
            {
                var flat = new float[planes.Count * 4];
                for (int i = 0; i < planes.Count; i++) Array.Copy(planes[i], 0, flat, i * 4, 4);
                result.Planes = flat;
                result.Label = $"Convex({planes.Count}p IoU {iou:F3})";
                ProbeLog.Line($"Shape recovered: {planes.Count} planes IoU {iou:F3} vol {truthCells / (double)total:F3} :: {defName}");
            }
            else
            {
                result.Label = $"Irregular(IoU {iou:F2})";
                ProbeLog.Line($"Shape NOT convex (best IoU {iou:F3} with {planes.Count} planes, vol {truthCells / (double)total:F3}) -> cell boxes :: {defName}");
            }
        }
        catch (Exception e) { ProbeLog.Error("shape resolve", e); }
        return result;
    }

    // Recover the block's convex hull as an intersection of half-spaces.
    //
    // For each candidate direction, take the TIGHTEST plane that still contains
    // every occupied cell — its supporting plane. The intersection of all of
    // them is the convex hull expressed in that normal family: deterministic,
    // and free of the failure mode a greedy plane search has (an early cut it
    // cannot back out of). The result always contains the true cells, so a
    // block is convex exactly when the hull adds no extra volume.
    private static (List<float[]> Planes, double IoU) SupportFit(
        float[][] normals, bool[] truth, int truthCells, float[] lx, float[] ly, float[] lz,
        int total, float cellSpan)
    {
        var cand = new List<float[]>(normals.Length);
        foreach (var n in normals)
        {
            float m1 = float.MinValue;
            for (int i = 0; i < total; i++)
                if (truth[i])
                {
                    float v = n[0] * lx[i] + n[1] * ly[i] + n[2] * lz[i];
                    if (v > m1) m1 = v;
                }
            if (m1 == float.MinValue) continue;

            // The true surface lies between the outermost occupied cell centre
            // and the first empty one beyond it, so sit the plane midway. With
            // nothing beyond, the surface is the AABB face itself: push the
            // plane clear of the cell instead of slicing through its centre.
            float m2 = float.MaxValue;
            for (int i = 0; i < total; i++)
            {
                float v = n[0] * lx[i] + n[1] * ly[i] + n[2] * lz[i];
                if (v > m1 + 1e-6f && v < m2) m2 = v;
            }
            float d = m2 < float.MaxValue
                ? (m1 + m2) * 0.5f
                : m1 + 0.5f * (MathF.Abs(n[0]) + MathF.Abs(n[1]) + MathF.Abs(n[2])) * cellSpan;
            cand.Add(new[] { n[0], n[1], n[2], d });
        }

        // A supporting plane can never exclude real material, so the only
        // question is which empty cells each one accounts for. An empty cell is
        // "explained" when it lies outside a plane, or within half a cell of
        // one: cells straddling the surface are genuinely ambiguous, since the
        // engine's own voxelizer had to round them one way or the other.
        // Kept well under a cell: a surface passing near a centre makes that
        // cell's occupancy a coin flip, but anything a third of a cell clear of
        // the surface is real evidence about the shape.
        float tol = 0.3f * cellSpan;
        bool Explains(float[] p, int i) => p[0] * lx[i] + p[1] * ly[i] + p[2] * lz[i] - p[3] > -tol;

        // Prefer the SIMPLEST explanation. Several plane sets can reproduce the
        // same voxel data while implying different surfaces, so if one plane
        // accounts for every empty cell, that is the block's face — adding more
        // would carve detail the block does not have.
        var kept = new List<float[]>();
        float[] single = null;
        int singleStrict = -1;
        foreach (var p in cand)
        {
            bool all = true;
            int strict = 0;
            for (int i = 0; i < total; i++)
            {
                if (truth[i]) continue;
                if (!Explains(p, i)) { all = false; break; }
                if (p[0] * lx[i] + p[1] * ly[i] + p[2] * lz[i] > p[3]) strict++;
            }
            if (all && strict > singleStrict) { singleStrict = strict; single = p; }
        }

        int remaining;
        if (single != null)
        {
            kept.Add(single);
            remaining = 0;
        }
        else
        {
            // Otherwise cover the empty cells with as few planes as possible,
            // and here a cell only counts once a plane genuinely EXCLUDES it.
            // Allowing the half-cell tolerance per plane would let several
            // planes each claim cells none of them actually removes — which is
            // how a staircase masquerades as a convex solid.
            var covered = new bool[total];
            remaining = total - truthCells;
            while (remaining > 0 && kept.Count < MaxPlanes)
            {
                float[] best = null;
                int bestGain = 0;
                foreach (var p in cand)
                {
                    int gain = 0;
                    for (int i = 0; i < total; i++)
                    {
                        if (truth[i] || covered[i]) continue;
                        if (p[0] * lx[i] + p[1] * ly[i] + p[2] * lz[i] > p[3]) gain++;
                    }
                    if (gain > bestGain) { bestGain = gain; best = p; }
                }
                if (best == null || bestGain == 0) break;
                for (int i = 0; i < total; i++)
                {
                    if (truth[i] || covered[i]) continue;
                    if (best[0] * lx[i] + best[1] * ly[i] + best[2] * lz[i] > best[3])
                    { covered[i] = true; remaining--; }
                }
                kept.Add(best);
            }
        }

        // Empty cells no plane can account for are material the block genuinely
        // keeps inside its own hull — it is not convex.
        double score = 1.0 - remaining / (double)total;
        return (kept, score);
    }

    private static string DefName(object def)
    {
        try
        {
            var n = (def.GetType().GetProperty("DebugName")?.GetValue(def)
                 ?? def.GetType().GetProperty("DisplayName")?.GetValue(def)
                 ?? def.ToString())?.ToString() ?? "?";
            return n.Length > 70 ? n[^70..] : n;
        }
        catch { return "?"; }
    }

    // Sub-cell coverage by supersampling the recovered half-space intersection.
    private static byte[,,] BuildStamp(float[] planes, sbyte[] basis, Vector3I ext)
    {
        const int K = 4;
        int pc = planes.Length / 4;
        var stamp = new byte[ext.X, ext.Y, ext.Z];
        for (int cx = 0; cx < ext.X; cx++)
            for (int cy = 0; cy < ext.Y; cy++)
                for (int cz = 0; cz < ext.Z; cz++)
                {
                    int hits = 0;
                    for (int i = 0; i < K; i++)
                        for (int j = 0; j < K; j++)
                            for (int k = 0; k < K; k++)
                            {
                                float px = (cx + (i + 0.5f) / K) / ext.X - 0.5f;
                                float py = (cy + (j + 0.5f) / K) / ext.Y - 0.5f;
                                float pz = (cz + (k + 0.5f) / K) / ext.Z - 0.5f;
                                var l = MulT(basis, px, py, pz);
                                bool inside = true;
                                for (int p = 0; p < pc && inside; p++)
                                    if (planes[p * 4] * l.X + planes[p * 4 + 1] * l.Y + planes[p * 4 + 2] * l.Z > planes[p * 4 + 3])
                                        inside = false;
                                if (inside) hits++;
                            }
                    stamp[cx, cy, cz] = (byte)Math.Round(hits / (float)(K * K * K) * FracUnits);
                }
        return stamp;
    }

    // Integer normals up to +-range, gcd-reduced and de-duplicated.
    private static float[][] BuildNormals(int range)
    {
        var seen = new HashSet<(int, int, int)>();
        var list = new List<float[]>();
        for (int a = -range; a <= range; a++)
            for (int b = -range; b <= range; b++)
                for (int c = -range; c <= range; c++)
                {
                    if (a == 0 && b == 0 && c == 0) continue;
                    int g = Gcd(Gcd(Math.Abs(a), Math.Abs(b)), Math.Abs(c));
                    var key = (a / g, b / g, c / g);
                    if (!seen.Add(key)) continue;
                    float len = MathF.Sqrt(key.Item1 * key.Item1 + key.Item2 * key.Item2 + key.Item3 * key.Item3);
                    list.Add(new[] { key.Item1 / len, key.Item2 / len, key.Item3 / len });
                }
        return list.ToArray();
    }

    private static int Gcd(int a, int b) { while (b != 0) { (a, b) = (b, a % b); } return a == 0 ? 1 : a; }

    private static (float X, float Y, float Z) MulT(sbyte[] m, float x, float y, float z)
        => (m[0] * x + m[3] * y + m[6] * z,
            m[1] * x + m[4] * y + m[7] * z,
            m[2] * x + m[5] * y + m[8] * z);

    private static sbyte[] BasisOf(IntegerOrientation o)
    {
        var f = DirVec(o.Forward);
        var u = DirVec(o.Up);
        var r = (X: u.Y * f.Z - u.Z * f.Y, Y: u.Z * f.X - u.X * f.Z, Z: u.X * f.Y - u.Y * f.X);
        return new sbyte[] { (sbyte)r.X, (sbyte)u.X, (sbyte)f.X, (sbyte)r.Y, (sbyte)u.Y, (sbyte)f.Y, (sbyte)r.Z, (sbyte)u.Z, (sbyte)f.Z };
    }

    private static (int X, int Y, int Z) DirVec(object dir) => dir.ToString() switch
    {
        "Forward" => (0, 0, -1),
        "Backward" => (0, 0, 1),
        "Left" => (-1, 0, 0),
        "Right" => (1, 0, 0),
        "Up" => (0, 1, 0),
        "Down" => (0, -1, 0),
        _ => (0, 0, 1),
    };

    private static int BasisHash(sbyte[] b)
    {
        int h = 17;
        for (int i = 0; i < 9; i++) h = h * 31 + b[i];
        return h;
    }
}
