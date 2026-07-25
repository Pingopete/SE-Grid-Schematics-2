using System.Diagnostics;
using Keen.Game2.Simulation.WorldObjects.CubeBlocks;
using Keen.Game2.Simulation.WorldObjects.CubeGrids;
using Keen.VRage.Library.Mathematics;

namespace GridProbe;

internal sealed class OccupancyScan
{
    public int[,] Top;
    public int[,] Side;
    public int[,] Front;
    // Projected footprint coverage per view (0..16): fractional edge cells let
    // the renderer reconstruct true silhouettes via interpolated contours.
    public byte[,] CovTop, CovSide, CovFront;
    public Vector3I Min;
    public Vector3I Size;
    public int BlockCount;
    public int CellBoxCount;
    public double TotalMs;
    public string StatsLine;
    public long ViewHash; // lazy FNV of Front, for panel image caching
    public Dictionary<(int ViewAxis, int Mode), byte[,]> ToneCache; // display tone fields per mode
    public List<BoundingBoxI> Boxes; // retained for depth-channel analysis
    // Blocks whose analytic solid was recovered, kept so depth analysis can
    // read their sub-cell profile instead of their whole-cell boxes.
    public List<(BoundingBoxI Aabb, BlockShapes.Stamp Stamp)> Shaped;
    // Cell boxes of everything NOT covered by a recovered solid, so depth
    // analysis never counts a block twice.
    public List<BoundingBoxI> UnshapedBoxes;

    // Lazily computed per-column channels for one depth axis at a time.
    public volatile int ChannelAxis = -1;
    public float[,] ChFilled, ChLayers, ChVoids;


    // Cached iso-band vector geometry per (view, mode) — the single renderer
    // for all zoom levels. Built on worker threads only.
    public readonly System.Collections.Concurrent.ConcurrentDictionary<(int ViewAxis, int Mode), ToneBands.BandSet> BandCache = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(int, int), bool> _bandBuilding = new();

    public ToneBands.BandSet GetBands(int viewAxis, int mode)
        => BandCache.TryGetValue((viewAxis, mode), out var b) ? b : null;

    public ToneBands.BandSet EnsureBands(int viewAxis, int mode)
    {
        if (BandCache.TryGetValue((viewAxis, mode), out var have)) return have;
        if (mode != PanelState.ModeThickness) EnsureChannels(PanelState.DepthAxisOf(viewAxis));
        var tones = ToneFields.Get(this, viewAxis, mode);
        if (tones == null) return null;
        var cov = viewAxis switch
        {
            PanelState.ViewFront => CovTop,
            PanelState.ViewSide => CovSide,
            _ => CovFront,
        };
        var sw = Stopwatch.StartNew();
        // Publish the silhouette immediately so the panel paints this frame,
        // then replace it with the full shaded set.
        if (cov != null && !BandCache.ContainsKey((viewAxis, mode)))
        {
            var quick = ToneBands.BuildSilhouette(tones, cov);
            if (quick.Bands.Count > 0) BandCache[(viewAxis, mode)] = quick;
        }
        var built = ToneBands.Build(tones, cov);
        BandCache[(viewAxis, mode)] = built;
        ProbeLog.Line($"ToneBands view {viewAxis} mode {mode}: {built.Bands.Count} bands, segs {string.Join("/", built.TotalSegs)} (lod tiers), {sw.Elapsed.TotalMilliseconds:F0} ms.");
        return built;
    }

    // Render-thread-safe: kicks one background build, repaints when done.
    public void RequestBands(int viewAxis, int mode, int panelKey)
    {
        if (BandCache.ContainsKey((viewAxis, mode))) return;
        if (!_bandBuilding.TryAdd((viewAxis, mode), true)) return;
        System.Threading.ThreadPool.QueueUserWorkItem(state =>
        {
            try
            {
                EnsureBands(viewAxis, mode);
                VectorLcd.RepaintRequest[panelKey] = true;
            }
            catch (Exception e) { ProbeLog.Error("band build", e); }
            finally { _bandBuilding.TryRemove((viewAxis, mode), out _); }
        });
    }

    // True when the other scan captured identical geometry: the caller can
    // keep the old scan object and every cache hanging off it.
    public bool ContentEquals(OccupancyScan o)
    {
        if (o == null || !o.Min.Equals(Min) || !o.Size.Equals(Size)
            || o.BlockCount != BlockCount || o.CellBoxCount != CellBoxCount) return false;
        return SameField(Front, o.Front) && SameField(Top, o.Top) && SameField(Side, o.Side);
    }

    private static bool SameField(int[,] a, int[,] b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a == null || b == null) return false;
        int w = a.GetLength(0), h = a.GetLength(1);
        if (w != b.GetLength(0) || h != b.GetLength(1)) return false;
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                if (a[x, y] != b[x, y]) return false;
        return true;
    }

    private readonly object _chLock = new();

    public void EnsureChannels(int depthAxis)
    {
        if (Boxes == null) return;
        // Serialized and assigned atomically: scans are long-lived now (reused
        // across passes), so a torn axis-A/axis-B interleave would persist.
        lock (_chLock)
        {
            if (ChannelAxis == depthAxis) return;
            var sw = Stopwatch.StartNew();
            var (spans, uw, vh) = BuildSpans(depthAxis);
            var (fil, layers, voids) = DepthChannels.Compute(spans, uw, vh);
            if (depthAxis == 0) // side view: match the rotated Side/CovSide layout
            {
                fil = Rot90CCW(fil);
                layers = Rot90CCW(layers);
                voids = Rot90CCW(voids);
            }
            else if (depthAxis == 2) // front view: match the 180-rotated Top/CovTop layout
            {
                fil = Rot180(fil);
                layers = Rot180(layers);
                voids = Rot180(voids);
            }
            ChFilled = fil;
            ChLayers = layers;
            ChVoids = voids;
            ChannelAxis = depthAxis;
            ProbeLog.Line($"Depth channels axis {depthAxis}: {spans.Count} spans ({Shaped?.Count ?? 0} shaped blocks) in {sw.Elapsed.TotalMilliseconds:F1} ms.");
        }
    }

    // Per-column material spans along the depth axis.
    //
    // Blocks whose analytic solid was recovered contribute FRACTIONAL spans read
    // from their stamp: the first and last occupied cell in the column are only
    // partly filled, and that fraction says where the surface actually sits. A
    // slope therefore produces spans that slide smoothly from column to column,
    // which is what makes the derived shading a real gradient rather than a
    // stepped count. Everything else contributes its whole cell boxes.
    private (List<DepthChannels.Span> Spans, int UW, int VH) BuildSpans(int depthAxis)
    {
        (int ua, int va) = depthAxis switch { 1 => (0, 2), 2 => (0, 1), _ => (1, 2) };
        int uw = Axis(Size, ua), vh = Axis(Size, va);
        var spans = new List<DepthChannels.Span>(Boxes.Count * 4);
        if (Shaped != null)
        {
            const float F = BlockShapes.FracUnits;
            foreach (var (aabb, stamp) in Shaped)
            {
                var lo = aabb.Min - Min;
                var ext = aabb.Max - aabb.Min + new Vector3I(1, 1, 1);
                int du = Axis(ext, ua), dv = Axis(ext, va), dd = Axis(ext, depthAxis);
                for (int a = 0; a < du; a++)
                    for (int b = 0; b < dv; b++)
                    {
                        int u = Axis(lo, ua) + a, v = Axis(lo, va) + b;
                        if (u < 0 || v < 0 || u >= uw || v >= vh) continue;

                        // Walk the column through the stamp for first/last material.
                        int firstK = -1, lastK = -1;
                        float firstFill = 0f, lastFill = 0f;
                        for (int k = 0; k < dd; k++)
                        {
                            int f = StampAt(stamp, ua, va, depthAxis, a, b, k);
                            if (f <= 0) continue;
                            if (firstK < 0) { firstK = k; firstFill = f / F; }
                            lastK = k; lastFill = f / F;
                        }
                        if (firstK < 0) continue;

                        // Partial end cells are filled from the inside out, so the
                        // surface sits that fraction in from the cell boundary.
                        int baseD = Axis(lo, depthAxis);
                        float d0 = baseD + firstK + (1f - firstFill);
                        float d1 = baseD + lastK + lastFill;
                        if (d1 <= d0) d1 = d0 + 0.02f;
                        spans.Add(new DepthChannels.Span(u * vh + v, d0, d1));
                    }
            }
        }

        // Everything not covered by a recovered solid, using its cell boxes.
        foreach (var b in UnshapedBoxes ?? Boxes)
        {
            var lo = b.Min - Min;
            var hi = b.Max - Min;
            int u0 = Math.Max(0, Axis(lo, ua)), u1 = Math.Min(uw - 1, Axis(hi, ua));
            int v0 = Math.Max(0, Axis(lo, va)), v1 = Math.Min(vh - 1, Axis(hi, va));
            float d0 = Axis(lo, depthAxis), d1 = Axis(hi, depthAxis) + 1f;
            for (int u = u0; u <= u1; u++)
                for (int v = v0; v <= v1; v++)
                    spans.Add(new DepthChannels.Span(u * vh + v, d0, d1));
        }
        return (spans, uw, vh);
    }

    private static int StampAt(BlockShapes.Stamp s, int ua, int va, int da, int a, int b, int k)
    {
        int x = 0, y = 0, z = 0;
        Set(ref x, ref y, ref z, ua, a);
        Set(ref x, ref y, ref z, va, b);
        Set(ref x, ref y, ref z, da, k);
        if (x < 0 || y < 0 || z < 0
            || x >= s.Fill.GetLength(0) || y >= s.Fill.GetLength(1) || z >= s.Fill.GetLength(2)) return 0;
        return s.Fill[x, y, z];
    }

    private static void Set(ref int x, ref int y, ref int z, int axis, int value)
    {
        if (axis == 0) x = value; else if (axis == 1) y = value; else z = value;
    }

    private static int Axis(Vector3I v, int a) => a == 0 ? v.X : a == 1 ? v.Y : v.Z;

    private static float[,] Rot90CCW(float[,] src)
    {
        int w = src.GetLength(0), h = src.GetLength(1);
        var dst = new float[h, w];
        for (int x = 0; x < h; x++)
            for (int y = 0; y < w; y++)
                dst[x, y] = src[w - 1 - y, x];
        return dst;
    }

    private static float[,] Rot180(float[,] src)
    {
        int w = src.GetLength(0), h = src.GetLength(1);
        var dst = new float[w, h];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                dst[x, y] = src[w - 1 - x, h - 1 - y];
        return dst;
    }

    private static int[,] Rot90CCW(int[,] src)
    {
        int w = src.GetLength(0), h = src.GetLength(1);
        var dst = new int[h, w];
        for (int x = 0; x < h; x++)
            for (int y = 0; y < w; y++)
                dst[x, y] = src[w - 1 - y, x];
        return dst;
    }

    private static byte[,] Rot90CCW(byte[,] src)
    {
        int w = src.GetLength(0), h = src.GetLength(1);
        var dst = new byte[h, w];
        for (int x = 0; x < h; x++)
            for (int y = 0; y < w; y++)
                dst[x, y] = src[w - 1 - y, x];
        return dst;
    }

    private static int[,] Rot180(int[,] src)
    {
        int w = src.GetLength(0), h = src.GetLength(1);
        var dst = new int[w, h];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                dst[x, y] = src[w - 1 - x, h - 1 - y];
        return dst;
    }

    private static byte[,] Rot180(byte[,] src)
    {
        int w = src.GetLength(0), h = src.GetLength(1);
        var dst = new byte[w, h];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                dst[x, y] = src[w - 1 - x, h - 1 - y];
        return dst;
    }

    private static bool _censusDone;
    private static bool _kindsLogged;

    public static OccupancyScan Run(CubeGridComponent grid)
    {
        var sw = Stopwatch.StartNew();
        var boxes = new List<BoundingBoxI>(4096);          // all blocks (channels/analysis)
        var fullBoxes = new List<BoundingBoxI>(4096);      // solid blocks (thickness sums)
        var shaped = new List<(object Def, IntegerOrientation Orient, BoundingBoxI Aabb, List<BoundingBoxI> Own)>();
        int blocks = 0;
        var census = _censusDone ? null : new SortedDictionary<string, int>();
        try
        {
            grid.VisitAllBlocksWithComponent<CubeBlockComponent>(b =>
            {
                blocks++;
                // Geometry prefilter: blocks whose cell boxes fill their AABB are
                // solid; anything partial goes to shape fitting (name-agnostic).
                var own = new List<BoundingBoxI>(8);
                long vol = 0;
                foreach (var box in b.GetTransformedOccupiedCellGroups())
                {
                    boxes.Add(box);
                    own.Add(box);
                    var e = box.Max - box.Min + new Vector3I(1, 1, 1);
                    vol += (long)e.X * e.Y * e.Z;
                }
                bool partial = false;
                try
                {
                    var ab = b.AABB;
                    var ae = ab.Max - ab.Min + new Vector3I(1, 1, 1);
                    long total = (long)ae.X * ae.Y * ae.Z;
                    partial = vol < total && total >= 8 && total <= 64000;
                    if (partial) shaped.Add((b.Definition, b.BlockOrientation, ab, own));
                }
                catch { partial = false; }
                if (!partial) fullBoxes.AddRange(own);
                if (census != null)
                {
                    string name = "?";
                    try
                    {
                        var d = b.Definition;
                        name = d == null ? "null" : (d.GetType().GetProperty("DebugName")?.GetValue(d)
                            ?? d.GetType().GetProperty("DisplayName")?.GetValue(d) ?? d.ToString())?.ToString() ?? "?";
                    }
                    catch { }
                    census[name] = census.TryGetValue(name, out var n) ? n + 1 : 1;
                }
            }, includeSubgrids: true);
            if (census != null && census.Count > 0)
            {
                _censusDone = true;
                var sb = new System.Text.StringBuilder($"Block definition census ({census.Count} distinct):\n");
                foreach (var kv in census) sb.AppendLine($"  {kv.Value,5}x {kv.Key}");
                ProbeLog.Line(sb.ToString());
            }
            if (!_kindsLogged && blocks > 100)
            {
                _kindsLogged = true;
                ProbeLog.Line($"Shape prefilter: {blocks} blocks -> {shaped.Count} partial-occupancy candidates ({fullBoxes.Count} solid boxes) this scan.");
            }
        }
        catch (Exception e)
        {
            ProbeLog.Error("block visit", e);
            return null;
        }
        var visitMs = sw.Elapsed.TotalMilliseconds;
        if (boxes.Count == 0) { ProbeLog.Line($"Scan: {blocks} blocks but 0 cell boxes — investigate cell-group API."); return null; }

        var min = boxes[0].Min; var max = boxes[0].Max;
        foreach (var b in boxes) { min = Vector3I.Min(min, b.Min); max = Vector3I.Max(max, b.Max); }
        foreach (var s in shaped) { min = Vector3I.Min(min, s.Aabb.Min); max = Vector3I.Max(max, s.Aabb.Max); }
        var size = max - min + new Vector3I(1, 1, 1);
        const int MaxDim = 4096;
        if (size.X > MaxDim || size.Y > MaxDim || size.Z > MaxDim)
        {
            ProbeLog.Line($"Scan: implausible bounds {size} — skipping (Max exclusive/inclusive semantics?).");
            return null;
        }

        var scan = new OccupancyScan
        {
            Min = min,
            Size = size,
            Top = new int[size.X, size.Y],
            Side = new int[size.Y, size.Z],
            Front = new int[size.X, size.Z],
            CovTop = new byte[size.X, size.Y],
            CovSide = new byte[size.Y, size.Z],
            CovFront = new byte[size.X, size.Z],
            BlockCount = blocks,
            CellBoxCount = boxes.Count,
            Boxes = boxes,
            Shaped = new List<(BoundingBoxI, BlockShapes.Stamp)>(shaped.Count),
            UnshapedBoxes = new List<BoundingBoxI>(fullBoxes),
        };

        // Depth sums accumulate in 1/16-cell units so shaped blocks can
        // contribute fractional coverage (smooth slopes IN the data).
        //
        // Coverage is built as a UNION of projected sub-cell masks, not a max of
        // per-cell volume fractions. Volume is the wrong quantity to project: a
        // cell sliced by a slope is half full yet fully opaque along the slice,
        // and two blocks can cover complementary halves of the same cell. Taking
        // a max of volumes under-counts both, which feathers what should be a
        // one-cell-wide edge into a multi-cell ramp.
        const int F = BlockShapes.FracUnits;
        const ushort FullMask = 0xFFFF;
        var mTop = new ushort[size.X, size.Y];
        var mSide = new ushort[size.Y, size.Z];
        var mFront = new ushort[size.X, size.Z];

        foreach (var b in fullBoxes)
        {
            var lo = b.Min - min;
            var ext = b.Max - b.Min + new Vector3I(1, 1, 1);
            for (int x = lo.X; x < lo.X + ext.X && x < size.X; x++)
                for (int y = lo.Y; y < lo.Y + ext.Y && y < size.Y; y++)
                { scan.Top[x, y] += ext.Z * F; mTop[x, y] = FullMask; }
            for (int y = lo.Y; y < lo.Y + ext.Y && y < size.Y; y++)
                for (int z = lo.Z; z < lo.Z + ext.Z && z < size.Z; z++)
                { scan.Side[y, z] += ext.X * F; mSide[y, z] = FullMask; }
            for (int x = lo.X; x < lo.X + ext.X && x < size.X; x++)
                for (int z = lo.Z; z < lo.Z + ext.Z && z < size.Z; z++)
                { scan.Front[x, z] += ext.Y * F; mFront[x, z] = FullMask; }
        }

        int stamped = 0;
        foreach (var s in shaped)
        {
            var aabb = s.Aabb;
            var ext = aabb.Max - aabb.Min + new Vector3I(1, 1, 1);
            BlockShapes.Stamp stamp = null;
            try
            {
                stamp = BlockShapes.GetStamp(s.Def, s.Orient, ext, () =>
                {
                    var rel = new List<(Vector3I, Vector3I)>(s.Own.Count);
                    foreach (var ob in s.Own) rel.Add((ob.Min - aabb.Min, ob.Max - aabb.Min));
                    return rel;
                });
            }
            catch (Exception e) { ProbeLog.Error("shape stamp", e); }

            if (stamp == null)
            {
                // No analytic solid recovered (genuinely open/irregular geometry):
                // use the block's own cell boxes, which are solid cells and must
                // set coverage exactly like any other solid box. Omitting the
                // coverage write here made every such block invisible, because
                // the display field is tone * coverage.
                foreach (var ob in s.Own)
                {
                    scan.UnshapedBoxes.Add(ob);
                    var lo2 = ob.Min - min;
                    var ex2 = ob.Max - ob.Min + new Vector3I(1, 1, 1);
                    for (int x = lo2.X; x < lo2.X + ex2.X && x < size.X; x++)
                        for (int y = lo2.Y; y < lo2.Y + ex2.Y && y < size.Y; y++)
                        { scan.Top[x, y] += ex2.Z * F; mTop[x, y] = FullMask; }
                    for (int y = lo2.Y; y < lo2.Y + ex2.Y && y < size.Y; y++)
                        for (int z = lo2.Z; z < lo2.Z + ex2.Z && z < size.Z; z++)
                        { scan.Side[y, z] += ex2.X * F; mSide[y, z] = FullMask; }
                    for (int x = lo2.X; x < lo2.X + ex2.X && x < size.X; x++)
                        for (int z = lo2.Z; z < lo2.Z + ex2.Z && z < size.Z; z++)
                        { scan.Front[x, z] += ex2.Y * F; mFront[x, z] = FullMask; }
                }
                continue;
            }

            stamped++;
            scan.Shaped.Add((aabb, stamp));
            var blo = aabb.Min - min;
            for (int cx = 0; cx < ext.X; cx++)
                for (int cy = 0; cy < ext.Y; cy++)
                    for (int cz = 0; cz < ext.Z; cz++)
                    {
                        int f = stamp.Fill[cx, cy, cz];
                        if (f == 0) continue;
                        int gx = blo.X + cx, gy = blo.Y + cy, gz = blo.Z + cz;
                        if (gx < 0 || gy < 0 || gz < 0 || gx >= size.X || gy >= size.Y || gz >= size.Z) continue;
                        scan.Top[gx, gy] += f;
                        scan.Side[gy, gz] += f;
                        scan.Front[gx, gz] += f;
                        mTop[gx, gy] |= stamp.MaskXY[cx, cy, cz];
                        mSide[gy, gz] |= stamp.MaskYZ[cx, cy, cz];
                        mFront[gx, gz] |= stamp.MaskXZ[cx, cy, cz];
                    }
        }

        // Collapse the unioned sub-masks into 0..16 coverage.
        for (int x = 0; x < size.X; x++)
            for (int y = 0; y < size.Y; y++)
                scan.CovTop[x, y] = (byte)System.Numerics.BitOperations.PopCount(mTop[x, y]);
        for (int y = 0; y < size.Y; y++)
            for (int z = 0; z < size.Z; z++)
                scan.CovSide[y, z] = (byte)System.Numerics.BitOperations.PopCount(mSide[y, z]);
        for (int x = 0; x < size.X; x++)
            for (int z = 0; z < size.Z; z++)
                scan.CovFront[x, z] = (byte)System.Numerics.BitOperations.PopCount(mFront[x, z]);

        // Side view reads rotated 90 degrees CW on the panel; rotate all its
        // products CCW at the source so every consumer sees the upright ship.
        // Front view reads upside down; rotate 180.
        scan.Side = Rot90CCW(scan.Side);
        scan.CovSide = Rot90CCW(scan.CovSide);
        scan.Top = Rot180(scan.Top);
        scan.CovTop = Rot180(scan.CovTop);

        var totalMs = sw.Elapsed.TotalMilliseconds;
        scan.TotalMs = totalMs;
        scan.StatsLine = $"Scan: {blocks} blocks ({shaped.Count} shaped, {stamped} stamped), {boxes.Count} cell boxes, bounds {size.X}x{size.Y}x{size.Z} cells, visit {visitMs:F1} ms, total {totalMs:F1} ms.";
        return scan;
    }

    public void WriteBmps(string basePath)
    {
        BmpWriter.WriteGrayscale(basePath + "_top.bmp", Top);
        BmpWriter.WriteGrayscale(basePath + "_side.bmp", Side);
        BmpWriter.WriteGrayscale(basePath + "_front.bmp", Front);
    }
}
