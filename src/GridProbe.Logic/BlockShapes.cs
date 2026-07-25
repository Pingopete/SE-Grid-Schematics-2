using Keen.VRage.Library.Mathematics;

namespace GridProbe;

// Geometry-driven block shapes: ANY block whose occupied cells do not fill its
// AABB is shape-fitted (per definition, once) against canonical solids in all
// 24 orientations, using the block's own cell boxes as ground truth. A good
// fit yields fractional stamps (smooth slopes in the data); a poor fit means
// genuinely complex geometry (railings, tapered masts) -> boxes, tier-3 later.
// No name lists: ramps, armor slopes, wedge windows and mod blocks all qualify
// by their geometry alone.
internal static class BlockShapes
{
    public const int FracUnits = 16;
    private const double FitThreshold = 0.85;   // fit must be excellent...
    private const double SolidMargin = 0.05;    // ...and beat the near-solid baseline

    private delegate bool InsideFn(float x, float y, float z);
    private static readonly (string Name, InsideFn Inside)[] Shapes =
    {
        ("Wedge",       (x, y, z) => y + z <= 1.0001f),
        ("WedgeLip",    (x, y, z) => y <= 0.2801f || (y - 0.28f) / 0.72f + z <= 1.0001f),
        ("Tetra",       (x, y, z) => x + y + z <= 1.0001f),
        ("PrismCorner", (x, y, z) => z <= 1.0001f - Math.Max(x, y)),
        ("InvCorner",   (x, y, z) => x + y + z <= 2.0001f),
        ("HalfSlab",    (x, y, z) => y <= 0.5001f),
    };

    private sealed class DefShape
    {
        public int ShapeIdx = -1;
        public int Rot;
        public string Label = "Full";
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<object, DefShape> _defs = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(object Def, int Basis, int X, int Y, int Z), byte[,,]> _stamps = new();
    private static readonly sbyte[][] Rots = BuildRotations();

    public static void ResetCaches()
    {
        _defs.Clear();
        _stamps.Clear();
        ProbeLog.Line("BlockShapes: caches cleared, shape fitting will re-run.");
    }

    public static string Describe(object def)
        => def != null && _defs.TryGetValue(def, out var d) ? d.Label : "unresolved";

    // Returns the fractional stamp for this block, or null when the definition
    // resolved to Full (caller uses the plain cell boxes).
    public static byte[,,] GetStamp(object defKey, IntegerOrientation orient, Vector3I ext, Func<List<(Vector3I Lo, Vector3I Hi)>> ownBoxesRel)
    {
        if (defKey == null) return null;
        var basis = BasisOf(orient);
        if (!_defs.TryGetValue(defKey, out var info))
        {
            info = ResolveShape(defKey, ext, basis, ownBoxesRel());
            _defs[defKey] = info;
        }
        if (info.ShapeIdx < 0) return null;
        var key = (defKey, BasisHash(basis), ext.X, ext.Y, ext.Z);
        return _stamps.GetOrAdd(key, _ => BuildStamp(info.ShapeIdx, basis, Rots[info.Rot], ext));
    }

    private static DefShape ResolveShape(object defKey, Vector3I ext, sbyte[] basis, List<(Vector3I Lo, Vector3I Hi)> boxesRel)
    {
        var result = new DefShape();
        try
        {
            long total = (long)ext.X * ext.Y * ext.Z;
            if (total < 8 || total > 64000 || boxesRel == null || boxesRel.Count == 0) return result;

            var truth = new bool[ext.X, ext.Y, ext.Z];
            long truthCells = 0;
            foreach (var (lo, hi) in boxesRel)
                for (int x = Math.Max(0, lo.X); x <= Math.Min(ext.X - 1, hi.X); x++)
                    for (int y = Math.Max(0, lo.Y); y <= Math.Min(ext.Y - 1, hi.Y); y++)
                        for (int z = Math.Max(0, lo.Z); z <= Math.Min(ext.Z - 1, hi.Z); z++)
                            if (!truth[x, y, z]) { truth[x, y, z] = true; truthCells++; }
            if (truthCells >= total * 0.98) { result.Label = "Solid"; return result; }

            int bestShape = -1, bestRot = 0;
            double bestIoU = 0;
            for (int s = 0; s < Shapes.Length; s++)
                for (int r = 0; r < Rots.Length; r++)
                {
                    int inter = 0, union = 0;
                    for (int cx = 0; cx < ext.X; cx++)
                        for (int cy = 0; cy < ext.Y; cy++)
                            for (int cz = 0; cz < ext.Z; cz++)
                            {
                                float px = (cx + 0.5f) / ext.X - 0.5f;
                                float py = (cy + 0.5f) / ext.Y - 0.5f;
                                float pz = (cz + 0.5f) / ext.Z - 0.5f;
                                var l = MulT(basis, px, py, pz);
                                var c = MulT(Rots[r], l.X, l.Y, l.Z);
                                bool inside = Shapes[s].Inside(c.X + 0.5f, c.Y + 0.5f, c.Z + 0.5f);
                                bool t = truth[cx, cy, cz];
                                if (inside && t) inter++;
                                if (inside || t) union++;
                            }
                    double iou = union == 0 ? 0 : inter / (double)union;
                    if (iou > bestIoU) { bestIoU = iou; bestShape = s; bestRot = r; }
                }

            string defName = DefName(defKey);
            // A near-solid block "fitting" a high-volume solid (e.g. a refinery
            // matching InvCorner) is volume correlation, not shape — stamping it
            // would fabricate geometry. Require clear superiority over solid.
            double iouSolid = truthCells / (double)total;
            if (bestIoU >= FitThreshold && bestIoU >= iouSolid + SolidMargin)
            {
                result.ShapeIdx = bestShape;
                result.Rot = bestRot;
                result.Label = $"{Shapes[bestShape].Name}(IoU {bestIoU:F2})";
                ProbeLog.Line($"Shape fit: {Shapes[bestShape].Name} rot {bestRot} IoU {bestIoU:F3} vol {truthCells / (double)total:F3} :: {defName}");
            }
            else
            {
                result.Label = $"Complex(best {Shapes[Math.Max(0, bestShape)].Name} {bestIoU:F2})";
                ProbeLog.Line($"Shape fit FAILED (best {(bestShape >= 0 ? Shapes[bestShape].Name : "-")} IoU {bestIoU:F3}, vol {truthCells / (double)total:F3}) -> boxes :: {defName}");
            }
        }
        catch (Exception e) { ProbeLog.Error("shape resolve", e); }
        return result;
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

    private static byte[,,] BuildStamp(int shapeIdx, sbyte[] basis, sbyte[] classRot, Vector3I ext)
    {
        const int K = 4;
        var inside = Shapes[shapeIdx].Inside;
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
                                var c = MulT(classRot, l.X, l.Y, l.Z);
                                if (inside(c.X + 0.5f, c.Y + 0.5f, c.Z + 0.5f)) hits++;
                            }
                    stamp[cx, cy, cz] = (byte)Math.Round(hits / (float)(K * K * K) * FracUnits);
                }
        return stamp;
    }

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

    private static sbyte[][] BuildRotations()
    {
        var axes = new (int X, int Y, int Z)[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) };
        var list = new List<sbyte[]>();
        foreach (var a in axes)
            foreach (var b in axes)
            {
                if (a.X * b.X + a.Y * b.Y + a.Z * b.Z != 0) continue;
                var c = (X: a.Y * b.Z - a.Z * b.Y, Y: a.Z * b.X - a.X * b.Z, Z: a.X * b.Y - a.Y * b.X);
                list.Add(new sbyte[] { (sbyte)a.X, (sbyte)b.X, (sbyte)c.X, (sbyte)a.Y, (sbyte)b.Y, (sbyte)c.Y, (sbyte)a.Z, (sbyte)b.Z, (sbyte)c.Z });
            }
        return list.ToArray();
    }
}
