namespace GridProbe;

// Per-mode display tone fields (alpha bytes, 0 = empty/transparent), built in
// scan space and cached on the scan. All modes read the same interval data;
// each maps it to a deliberate, player-useful visual:
//   Thickness — total material along the view ray (sqrt ramp, min-max)
//   XRay      — structural transitions (solid<->void boundaries): every
//               occupied column shows as a readable ghost, brightness steps
//               up with each additional structure layer crossed
//   Voids     — enclosed empty space made bright over a dim hull ghost
internal static class ToneFields
{
    public static byte[,] Get(OccupancyScan scan, int viewAxis, int mode)
    {
        var cache = scan.ToneCache;
        if (cache == null)
        {
            cache = new Dictionary<(int, int), byte[,]>();
            var prev = System.Threading.Interlocked.CompareExchange(ref scan.ToneCache, cache, null);
            if (prev != null) cache = prev;
        }
        lock (cache)
        {
            if (cache.TryGetValue((viewAxis, mode), out var t)) return t;
            var built = Build(scan, viewAxis, mode);
            if (built != null) cache[(viewAxis, mode)] = built;
            return built;
        }
    }

    private static byte[,] Build(OccupancyScan scan, int viewAxis, int mode)
    {
        int depthAxis = PanelState.DepthAxisOf(viewAxis);
        var thick = viewAxis switch
        {
            PanelState.ViewFront => scan.Top,
            PanelState.ViewSide => scan.Side,
            _ => scan.Front,
        };
        if (mode == PanelState.ModeThickness) return ToneMaps.BuildThickness(thick);
        if (scan.ChannelAxis != depthAxis) return null; // channels not computed yet
        var runs = scan.ChRuns;
        var voids = scan.ChVoids;
        // Torn channel state (axis says match, dims say otherwise) from an old
        // race: force a recompute instead of indexing out of bounds forever.
        if (runs == null || voids == null
            || runs.GetLength(0) != thick.GetLength(0) || runs.GetLength(1) != thick.GetLength(1))
        {
            ProbeLog.Line($"ToneFields: channel dims mismatch for axis {depthAxis} — resetting channels.");
            scan.ChannelAxis = -1;
            return null;
        }
        return mode == PanelState.ModeComplexity
            ? ToneMaps.BuildXRay(runs, thick)
            : ToneMaps.BuildVoids(voids, thick);
    }
}
