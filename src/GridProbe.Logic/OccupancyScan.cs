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

    // Lazily computed per-column channels for one depth axis at a time.
    public volatile int ChannelAxis = -1;
    public int[,] ChFilled, ChRuns, ChVoids;


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
            var (fil, runs, voids) = DepthChannels.Compute(Boxes, Min, Size, depthAxis);
            if (depthAxis == 0) // side view: match the rotated Side/CovSide layout
            {
                fil = Rot90CCW(fil);
                runs = Rot90CCW(runs);
                voids = Rot90CCW(voids);
            }
            else if (depthAxis == 2) // front view: match the 180-rotated Top/CovTop layout
            {
                fil = Rot180(fil);
                runs = Rot180(runs);
                voids = Rot180(voids);
            }
            ChFilled = fil;
            ChRuns = runs;
            ChVoids = voids;
            ChannelAxis = depthAxis;
            ProbeLog.Line($"Depth channels axis {depthAxis}: {Boxes.Count} boxes in {sw.Elapsed.TotalMilliseconds:F1} ms.");
        }
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
        };

        // Depth sums accumulate in 1/16-cell units so shaped blocks can
        // contribute fractional coverage (smooth slopes IN the data).
        const int F = BlockShapes.FracUnits;
        foreach (var b in fullBoxes)
        {
            var lo = b.Min - min;
            var ext = b.Max - b.Min + new Vector3I(1, 1, 1);
            for (int x = lo.X; x < lo.X + ext.X && x < size.X; x++)
                for (int y = lo.Y; y < lo.Y + ext.Y && y < size.Y; y++)
                { scan.Top[x, y] += ext.Z * F; scan.CovTop[x, y] = F; }
            for (int y = lo.Y; y < lo.Y + ext.Y && y < size.Y; y++)
                for (int z = lo.Z; z < lo.Z + ext.Z && z < size.Z; z++)
                { scan.Side[y, z] += ext.X * F; scan.CovSide[y, z] = F; }
            for (int x = lo.X; x < lo.X + ext.X && x < size.X; x++)
                for (int z = lo.Z; z < lo.Z + ext.Z && z < size.Z; z++)
                { scan.Front[x, z] += ext.Y * F; scan.CovFront[x, z] = F; }
        }

        int stamped = 0;
        foreach (var s in shaped)
        {
            var aabb = s.Aabb;
            var ext = aabb.Max - aabb.Min + new Vector3I(1, 1, 1);
            byte[,,] stamp = null;
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
                    var lo2 = ob.Min - min;
                    var ex2 = ob.Max - ob.Min + new Vector3I(1, 1, 1);
                    for (int x = lo2.X; x < lo2.X + ex2.X && x < size.X; x++)
                        for (int y = lo2.Y; y < lo2.Y + ex2.Y && y < size.Y; y++)
                        { scan.Top[x, y] += ex2.Z * F; scan.CovTop[x, y] = F; }
                    for (int y = lo2.Y; y < lo2.Y + ex2.Y && y < size.Y; y++)
                        for (int z = lo2.Z; z < lo2.Z + ex2.Z && z < size.Z; z++)
                        { scan.Side[y, z] += ex2.X * F; scan.CovSide[y, z] = F; }
                    for (int x = lo2.X; x < lo2.X + ex2.X && x < size.X; x++)
                        for (int z = lo2.Z; z < lo2.Z + ex2.Z && z < size.Z; z++)
                        { scan.Front[x, z] += ex2.Y * F; scan.CovFront[x, z] = F; }
                }
                continue;
            }

            stamped++;
            var blo = aabb.Min - min;
            for (int cx = 0; cx < ext.X; cx++)
                for (int cy = 0; cy < ext.Y; cy++)
                    for (int cz = 0; cz < ext.Z; cz++)
                    {
                        int f = stamp[cx, cy, cz];
                        if (f == 0) continue;
                        int gx = blo.X + cx, gy = blo.Y + cy, gz = blo.Z + cz;
                        if (gx < 0 || gy < 0 || gz < 0 || gx >= size.X || gy >= size.Y || gz >= size.Z) continue;
                        scan.Top[gx, gy] += f;
                        scan.Side[gy, gz] += f;
                        scan.Front[gx, gz] += f;
                        if (f > scan.CovTop[gx, gy]) scan.CovTop[gx, gy] = (byte)f;
                        if (f > scan.CovSide[gy, gz]) scan.CovSide[gy, gz] = (byte)f;
                        if (f > scan.CovFront[gx, gz]) scan.CovFront[gx, gz] = (byte)f;
                    }
        }

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
