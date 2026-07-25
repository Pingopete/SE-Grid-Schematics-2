using Keen.VRage.Library.Mathematics;

namespace GridProbe;

// Per-column depth analysis along one axis: for every (u,v) cell column,
// merge the occupied intervals and produce three channels:
//   Filled    - total occupied cells (same as the plain thickness sum)
//   Runs      - number of separate occupied runs (structural complexity)
//   Voids     - empty cells strictly between the first and last occupied cell
internal static class DepthChannels
{
    public static (int[,] Filled, int[,] Runs, int[,] Voids) Compute(List<BoundingBoxI> boxes, Vector3I min, Vector3I size, int depthAxis)
    {
        // u/v axes are the two axes other than depthAxis, in the same order the
        // scan arrays use: top(Y): u=X,v=Z; front(Z): u=X,v=Y; side(X): u=Y,v=Z.
        (int ua, int va) = depthAxis switch { 1 => (0, 2), 2 => (0, 1), _ => (1, 2) };
        int uw = Axis(size, ua), vh = Axis(size, va);

        // Pass 1: count intervals per column (counting sort layout).
        var counts = new int[uw * vh];
        foreach (var b in boxes)
        {
            var lo = b.Min - min;
            var hi = b.Max - min;
            int u0 = Axis(lo, ua), u1 = Math.Min(uw - 1, Axis(hi, ua));
            int v0 = Axis(lo, va), v1 = Math.Min(vh - 1, Axis(hi, va));
            for (int u = u0; u <= u1; u++)
                for (int v = v0; v <= v1; v++)
                    counts[u * vh + v]++;
        }
        var offsets = new int[uw * vh + 1];
        for (int i = 0; i < uw * vh; i++) offsets[i + 1] = offsets[i] + counts[i];
        int total = offsets[uw * vh];

        // Pass 2: place (start,end) depth intervals.
        var starts = new short[total];
        var ends = new short[total];
        var fill = new int[uw * vh];
        foreach (var b in boxes)
        {
            var lo = b.Min - min;
            var hi = b.Max - min;
            int u0 = Axis(lo, ua), u1 = Math.Min(uw - 1, Axis(hi, ua));
            int v0 = Axis(lo, va), v1 = Math.Min(vh - 1, Axis(hi, va));
            short d0 = (short)Axis(lo, depthAxis), d1 = (short)Axis(hi, depthAxis);
            for (int u = u0; u <= u1; u++)
                for (int v = v0; v <= v1; v++)
                {
                    int col = u * vh + v;
                    int slot = offsets[col] + fill[col]++;
                    starts[slot] = d0;
                    ends[slot] = d1;
                }
        }

        var filled = new int[uw, vh];
        var runs = new int[uw, vh];
        var voids = new int[uw, vh];
        var idx = new List<int>(16);
        for (int u = 0; u < uw; u++)
            for (int v = 0; v < vh; v++)
            {
                int col = u * vh + v;
                int n = counts[col];
                if (n == 0) continue;
                int off = offsets[col];

                idx.Clear();
                for (int i = 0; i < n; i++) idx.Add(off + i);
                idx.Sort((a, b) => starts[a].CompareTo(starts[b]));

                int runCount = 0, filledCells = 0;
                int curStart = starts[idx[0]], curEnd = ends[idx[0]];
                int first = curStart;
                for (int i = 1; i < n; i++)
                {
                    int s = starts[idx[i]], e = ends[idx[i]];
                    if (s <= curEnd + 1) { if (e > curEnd) curEnd = e; }
                    else
                    {
                        runCount++; filledCells += curEnd - curStart + 1;
                        curStart = s; curEnd = e;
                    }
                }
                runCount++; filledCells += curEnd - curStart + 1;
                int span = curEnd - first + 1;

                filled[u, v] = filledCells;
                runs[u, v] = runCount;
                voids[u, v] = Math.Max(0, span - filledCells);
            }
        return (filled, runs, voids);
    }

    private static int Axis(Vector3I v, int a) => a == 0 ? v.X : a == 1 ? v.Y : v.Z;
}
