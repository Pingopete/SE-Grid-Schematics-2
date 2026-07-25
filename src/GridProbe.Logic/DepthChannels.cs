using Keen.VRage.Library.Mathematics;

namespace GridProbe;

// Per-column depth analysis along one axis, at SUB-CELL precision.
//
// Every column of the projection is a ray through the ship. Along it, material
// occupies a set of intervals. Those intervals come from the recovered block
// geometry with fractional endpoints — a slope entering a cell a third of the
// way through starts its interval a third of the way through — so every
// quantity derived from them varies continuously as the geometry does.
//
// That continuity is the whole point. Counting whole cells makes these values
// small integers, and small integers posterize: each count owns a slab of the
// tone range with unused gaps between. Fractional intervals give genuine
// gradients that follow the shape, not a blur applied afterwards.
//
//   Filled  - total material length (cells, fractional)
//   Layers  - structural layers crossed, gaps weighted by how open they are
//   Voids   - enclosed empty length between first and last material
internal static class DepthChannels
{
    // A gap this wide (in cells) counts as a full extra layer; narrower gaps
    // count proportionally less. Without this a hairline seam between two
    // plates would read as a whole extra structural layer, and the value would
    // jump by one the instant the seam opened.
    private const float GapScale = 1.5f;

    public readonly struct Span
    {
        public readonly int Col;
        public readonly float D0, D1;
        public Span(int col, float d0, float d1) { Col = col; D0 = d0; D1 = d1; }
    }

    public static (float[,] Filled, float[,] Layers, float[,] Voids) Compute(
        List<Span> spans, int uw, int vh)
    {
        var filled = new float[uw, vh];
        var layers = new float[uw, vh];
        var voids = new float[uw, vh];
        if (spans == null || spans.Count == 0) return (filled, layers, voids);

        // Counting sort into per-column runs.
        int cols = uw * vh;
        var counts = new int[cols];
        foreach (var s in spans) counts[s.Col]++;
        var offsets = new int[cols + 1];
        for (int i = 0; i < cols; i++) offsets[i + 1] = offsets[i] + counts[i];

        var d0 = new float[spans.Count];
        var d1 = new float[spans.Count];
        var fill = new int[cols];
        foreach (var s in spans)
        {
            int slot = offsets[s.Col] + fill[s.Col]++;
            d0[slot] = s.D0;
            d1[slot] = s.D1;
        }

        var order = new int[32];
        for (int u = 0; u < uw; u++)
            for (int v = 0; v < vh; v++)
            {
                int col = u * vh + v;
                int n = counts[col];
                if (n == 0) continue;
                int off = offsets[col];

                if (order.Length < n) order = new int[Math.Max(n, order.Length * 2)];
                for (int i = 0; i < n; i++) order[i] = off + i;
                Array.Sort(order, 0, n, Comparer<int>.Create((a, b) => d0[a].CompareTo(d0[b])));

                float curStart = d0[order[0]], curEnd = d1[order[0]];
                float first = curStart;
                float material = 0f;
                float layerSum = 1f;   // the first run is always one layer

                for (int i = 1; i < n; i++)
                {
                    float s = d0[order[i]], e = d1[order[i]];
                    if (s <= curEnd + 1e-4f)
                    {
                        if (e > curEnd) curEnd = e;
                    }
                    else
                    {
                        material += curEnd - curStart;
                        // Weight the gap by how open it is, so the value moves
                        // smoothly as a gap widens instead of stepping.
                        float gap = s - curEnd;
                        layerSum += 1f - MathF.Exp(-gap / GapScale);
                        curStart = s; curEnd = e;
                    }
                }
                material += curEnd - curStart;
                float span = curEnd - first;

                filled[u, v] = material;
                layers[u, v] = layerSum;
                voids[u, v] = Math.Max(0f, span - material);
            }
        return (filled, layers, voids);
    }
}
