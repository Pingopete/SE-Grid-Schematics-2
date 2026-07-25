using GridProbe;
using Keen.VRage.Library.Mathematics;

// Ground-truth test for analytic shape recovery.
//
// Each case defines a TRUE continuous solid, voxelizes it the way the engine
// would (a cell is occupied if its centre is inside), hands ONLY those cells to
// BlockShapes, and then checks the recovered stamp against the true solid
// sampled at sub-cell resolution. That is the real question: does the fractional
// coverage we render match the actual block surface?

int failures = 0;

// A cell is "occupied" when its centre is inside the solid — the same rule the
// engine's voxelizer uses.
List<(Vector3I, Vector3I)> Voxelize(Func<float, float, float, bool> inside, int n)
{
    var boxes = new List<(Vector3I, Vector3I)>();
    for (int x = 0; x < n; x++)
        for (int y = 0; y < n; y++)
            for (int z = 0; z < n; z++)
            {
                float px = (x + 0.5f) / n - 0.5f;
                float py = (y + 0.5f) / n - 0.5f;
                float pz = (z + 0.5f) / n - 0.5f;
                if (inside(px, py, pz))
                    boxes.Add((new Vector3I(x, y, z), new Vector3I(x, y, z)));
            }
    return boxes;
}

void Case(string name, Func<float, float, float, bool> inside, int n, bool expectFit)
{
    var boxes = Voxelize(inside, n);
    var ext = new Vector3I(n, n, n);
    var orient = new IntegerOrientation(Base6Directions.Direction.Forward, Base6Directions.Direction.Up);
    var key = new object();
    var stamp = BlockShapes.GetStamp(key, orient, ext, () => boxes);

    if (!expectFit)
    {
        bool ok = stamp == null;
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {name}: expected no analytic fit, got {(stamp == null ? "none" : "a fit")}  [{BlockShapes.Describe(key)}]");
        if (!ok) failures++;
        return;
    }
    if (stamp == null)
    {
        Console.WriteLine($"FAIL {name}: expected an analytic fit, got none");
        failures++;
        return;
    }

    // Compare recovered sub-cell coverage against the true solid, sampled 4^3.
    const int K = 4;
    double err = 0; int cells = 0; int worstCell = 0;
    for (int x = 0; x < n; x++)
        for (int y = 0; y < n; y++)
            for (int z = 0; z < n; z++)
            {
                int hits = 0;
                for (int i = 0; i < K; i++)
                    for (int j = 0; j < K; j++)
                        for (int k = 0; k < K; k++)
                        {
                            float px = (x + (i + 0.5f) / K) / n - 0.5f;
                            float py = (y + (j + 0.5f) / K) / n - 0.5f;
                            float pz = (z + (k + 0.5f) / K) / n - 0.5f;
                            if (inside(px, py, pz)) hits++;
                        }
                int want = (int)Math.Round(hits / (float)(K * K * K) * BlockShapes.FracUnits);
                int got = stamp[x, y, z];
                int d = Math.Abs(want - got);
                if (d > worstCell) worstCell = d;
                err += d;
                cells++;
            }
    // Cell data localises a surface to about half a cell, so sub-cell coverage
    // can never be exact for every case; this bar is "visually indistinguishable".
    double mean = err / cells / BlockShapes.FracUnits;
    bool pass = mean < 0.04 && worstCell <= 5;
    Console.WriteLine($"{(pass ? "ok  " : "FAIL")} {name}: mean coverage error {mean * 100:F2}%, worst cell {worstCell}/16  [{BlockShapes.Describe(key)}]");
    if (!pass) failures++;
}

Console.WriteLine("--- analytic solids (must be recovered) ---");
// 1:1 slope — the classic armour wedge.
Case("wedge 1:1", (x, y, z) => y + z <= 0f, 10, true);
// 1:2 long slope — impossible for the old fixed library.
Case("long slope 1:2", (x, y, z) => y * 2f + z <= 0.5f, 10, true);
// 2:1 steep slope.
Case("steep slope 2:1", (x, y, z) => y + z * 2f <= 0.5f, 10, true);
// Corner tetrahedron (three cuts).
Case("corner tetra", (x, y, z) => x + y + z <= -0.5f + 1.0f, 10, true);
// Inverted corner: cube with one corner tetra removed.
Case("inverted corner", (x, y, z) => x + y + z <= 1.0f, 10, true);
// Half slab.
Case("half slab", (x, y, z) => y <= 0f, 10, true);
// Trapezoid profile: a sloped face over a full-width base. Convex, and the
// shape real armour slopes actually have.
Case("trapezoid slope", (x, y, z) => y + z <= 0f && y - z <= 0.5f, 10, true);
// Awkward ratio (3:4) — not in the coarse normal family, must escalate.
Case("slope 3:4 ratio", (x, y, z) => y * 0.75f + z <= 0.2f, 10, true);
// Two cuts meeting at an edge (a tip).
Case("corner tip", (x, y, z) => x + y <= 0.3f && y + z <= 0.3f, 10, true);
// Non-cube grid size, off-axis normal.
Case("long slope 1:2 @ n=8", (x, y, z) => y * 2f + z <= 0.5f, 8, true);

Console.WriteLine("--- genuinely non-convex (must be rejected) ---");
// Truss-like lattice: open frame, no convex solid can represent it.
Case("truss lattice", (x, y, z) =>
    Math.Abs(x) > 0.35f || Math.Abs(y) > 0.35f || Math.Abs(z) > 0.35f, 10, false);
// Staircase: genuinely stepped, cell boxes are the honest answer.
Case("staircase", (x, y, z) =>
{
    int step = (int)Math.Floor((z + 0.5f) * 5f);
    return y + 0.5f <= (step + 1) * 0.2f;
}, 10, false);

Console.WriteLine(failures == 0 ? "\nALL SHAPE TESTS PASSED" : $"\n{failures} SHAPE TESTS FAILED");
return failures == 0 ? 0 : 1;
