namespace GridProbe;

// Marching squares with linear interpolation over the fractional coverage
// field. Edge cells carry sub-cell edge position in their fraction, so the
// interpolated iso-contour reconstructs the TRUE (analytic) silhouette —
// resolution-independent smooth ship outlines from cell data.
internal static class Contour
{
    // Returns polyline loops in cell coordinates (cell centers at x+0.5).
    // Region is clamped to [x0..x1) x [y0..y1); values outside read as 0.
    public static List<List<(float X, float Y)>> March(byte[,] cov, int x0, int y0, int x1, int y1, float iso, int maxSegs = 30000)
    {
        int w = cov.GetLength(0), h = cov.GetLength(1);
        x0 = Math.Max(-1, x0); y0 = Math.Max(-1, y0);
        x1 = Math.Min(w, x1); y1 = Math.Min(h, y1);

        float Sample(int x, int y) => x < 0 || y < 0 || x >= w || y >= h ? 0f
            : x < x0 + 0 || y < y0 + 0 || x >= x1 || y >= y1 ? 0f  // clip to window so loops close at its border
            : cov[x, y];

        // Edge id: (x, y, 0=horizontal edge between centers (x,y)-(x+1,y), 1=vertical (x,y)-(x,y+1))
        var points = new Dictionary<(int X, int Y, int D), (float X, float Y)>();
        var links = new Dictionary<(int X, int Y, int D), List<(int X, int Y, int D)>>();

        (float X, float Y) EdgePoint((int X, int Y, int D) e)
        {
            if (points.TryGetValue(e, out var p)) return p;
            float a = Sample(e.X, e.Y);
            float b = e.D == 0 ? Sample(e.X + 1, e.Y) : Sample(e.X, e.Y + 1);
            float t = Math.Abs(b - a) < 1e-6f ? 0.5f : (iso - a) / (b - a);
            t = Math.Clamp(t, 0f, 1f);
            p = e.D == 0 ? (e.X + 0.5f + t, e.Y + 0.5f) : (e.X + 0.5f, e.Y + 0.5f + t);
            points[e] = p;
            return p;
        }

        void Link((int, int, int) a, (int, int, int) b)
        {
            if (!links.TryGetValue(a, out var la)) links[a] = la = new List<(int, int, int)>(2);
            la.Add(b);
            if (!links.TryGetValue(b, out var lb)) links[b] = lb = new List<(int, int, int)>(2);
            lb.Add(a);
        }

        int segCount = 0;
        for (int y = y0 - 1; y < y1; y++)
            for (int x = x0 - 1; x < x1; x++)
            {
                int c = 0;
                if (Sample(x, y) >= iso) c |= 1;
                if (Sample(x + 1, y) >= iso) c |= 2;
                if (Sample(x + 1, y + 1) >= iso) c |= 4;
                if (Sample(x, y + 1) >= iso) c |= 8;
                if (c == 0 || c == 15) continue;

                var top = (x, y, 0);
                var bottom = (x, y + 1, 0);
                var left = (x, y, 1);
                var right = (x + 1, y, 1);
                switch (c)
                {
                    case 1: case 14: Link(top, left); break;
                    case 2: case 13: Link(top, right); break;
                    case 3: case 12: Link(left, right); break;
                    case 4: case 11: Link(right, bottom); break;
                    case 6: case 9: Link(top, bottom); break;
                    case 7: case 8: Link(left, bottom); break;
                    case 5: Link(top, left); Link(right, bottom); break;
                    case 10: Link(top, right); Link(left, bottom); break;
                }
                if (++segCount > maxSegs) return new List<List<(float, float)>>(); // safety bail on absurd complexity
            }

        // Chain edges into loops.
        var loops = new List<List<(float X, float Y)>>();
        var visited = new HashSet<(int, int, int)>();
        foreach (var start in links.Keys)
        {
            if (visited.Contains(start)) continue;
            var loop = new List<(float X, float Y)>();
            var prev = start;
            var cur = start;
            do
            {
                visited.Add(cur);
                loop.Add(EdgePoint(cur));
                var next = default((int, int, int));
                bool found = false;
                foreach (var n in links[cur])
                {
                    if (!n.Equals(prev) && !visited.Contains(n)) { next = n; found = true; break; }
                }
                if (!found)
                {
                    // close back to start if adjacent, else open path ends
                    foreach (var n in links[cur])
                        if (n.Equals(start) && loop.Count > 2) { found = false; break; }
                    break;
                }
                prev = cur;
                cur = next;
            } while (!cur.Equals(start) && loop.Count < 30000);
            if (loop.Count >= 3) loops.Add(loop);
        }
        loops.Sort((a, b) => b.Count.CompareTo(a.Count));
        return loops;
    }
}
