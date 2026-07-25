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
    var fill = stamp.Fill;
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
                int got = fill[x, y, z];
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

// ---------------------------------------------------------------------------
// Projected coverage must be PROJECTED AREA, not volume.
//
// A slope viewed along the axis its face slices through is fully opaque even
// though every boundary cell is only half full by volume. Using volume here
// under-counts the edge and feathers a one-cell transition into a ramp.
Console.WriteLine("\n--- projected coverage (area, not volume) ---");
{
    int n = 10;
    // Wedge cut in the X-Y plane; Z is untouched, so viewed along X every cell
    // that holds any material projects as fully covered.
    var boxes = Voxelize((x, y, z) => x + y <= 0f, n);
    var orient = new IntegerOrientation(Base6Directions.Direction.Forward, Base6Directions.Direction.Up);
    var st = BlockShapes.GetStamp(new object(), orient, new Vector3I(n, n, n), () => boxes);
    if (st == null)
    {
        Console.WriteLine("FAIL projected coverage: wedge did not resolve");
        failures++;
    }
    else
    {
        // Union the projected masks down X, exactly as the scan does.
        int badEdge = 0, checkedCols = 0;
        for (int y = 0; y < n; y++)
            for (int z = 0; z < n; z++)
            {
                int mask = 0, volMax = 0;
                for (int x = 0; x < n; x++)
                {
                    mask |= st.MaskYZ[x, y, z];
                    if (st.Fill[x, y, z] > volMax) volMax = st.Fill[x, y, z];
                }
                if (mask == 0) continue;
                checkedCols++;
                int projected = System.Numerics.BitOperations.PopCount((uint)mask);
                // Any column with material somewhere along X is opaque here.
                if (projected != BlockShapes.FracUnits) badEdge++;
            }
        bool ok = badEdge == 0 && checkedCols > 0;
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")} occupied columns read as fully covered: {checkedCols - badEdge}/{checkedCols}");
        if (!ok) failures++;
    }
}

// ---------------------------------------------------------------------------
// Band rendering: holes in the structure must stay empty.
//
// Open framework — a truss with diagonal members — is the case that exposes
// hole handling, because every gap is bounded by diagonal edges whose boundary
// cells carry partial coverage.
Console.WriteLine("\n--- band holes (gaps must not fill) ---");

int W = 120, H = 60;
var cov = new byte[W, H];
var tone = new byte[W, H];
bool Solid(int x, int y)
{
    if (y < 6 || y >= H - 6) return true;                       // top and bottom decks
    int local = x % 30;
    int span = H - 12;
    int rel = y - 6;
    // Diagonal web members forming triangular voids, like ship girders.
    int a = (int)(rel * 15.0 / span);
    return local < 4 || Math.Abs(local - 15 - a) < 4 || Math.Abs(local - 15 + a) < 4;
}
for (int x = 0; x < W; x++)
    for (int y = 0; y < H; y++)
        if (Solid(x, y)) { cov[x, y] = (byte)BlockShapes.FracUnits; tone[x, y] = 200; }

var bands = ToneBands.Build(tone, cov);
Console.WriteLine($"     bands={bands.Bands.Count}");
foreach (var bd in bands.Bands)
{
    var areas = new List<string>();
    foreach (var lp in bd.Loops)
    {
        var p = lp.L[0];
        int m = p.Length / 2;
        double ar = 0;
        for (int i = 0; i < m; i++)
        {
            int j = (i + 1) % m;
            ar += p[i * 2] * p[j * 2 + 1] - p[j * 2] * p[i * 2 + 1];
        }
        areas.Add($"{ar / 2:F0}");
    }
    Console.WriteLine($"     band alpha={bd.Alpha} loops={bd.Loops.Count} areas=[{string.Join(", ", areas.Take(12))}]");
}

// Rasterize the bands the way the renderer does: nonzero winding per band.
bool Covered(int px, int py)
{
    float fx = px + 0.5f, fy = py + 0.5f;
    foreach (var band in bands.Bands)
    {
        int winding = 0;
        foreach (var loop in band.Loops)
        {
            var pts = loop.L[0];
            int m = pts.Length / 2;
            for (int i = 0; i < m; i++)
            {
                float ax = pts[i * 2], ay = pts[i * 2 + 1];
                float bx = pts[((i + 1) % m) * 2], by = pts[((i + 1) % m) * 2 + 1];
                if (ay <= fy)
                {
                    if (by > fy && (bx - ax) * (fy - ay) - (fx - ax) * (by - ay) > 0) winding++;
                }
                else if (by <= fy && (bx - ax) * (fy - ay) - (fx - ax) * (by - ay) < 0) winding--;
            }
        }
        if (winding != 0) return true;
    }
    return false;
}

int emptyFilled = 0, solidMissing = 0, emptyTotal = 0, solidTotal = 0;
for (int x = 2; x < W - 2; x++)
    for (int y = 2; y < H - 2; y++)
    {
        // Ignore cells adjacent to an edge: those are legitimately partial.
        bool s = Solid(x, y);
        bool nearEdge = false;
        for (int dx = -1; dx <= 1 && !nearEdge; dx++)
            for (int dy = -1; dy <= 1 && !nearEdge; dy++)
                if (Solid(x + dx, y + dy) != s) nearEdge = true;
        if (nearEdge) continue;
        if (s) { solidTotal++; if (!Covered(x, y)) solidMissing++; }
        else { emptyTotal++; if (Covered(x, y)) emptyFilled++; }
    }

double fillPct = emptyTotal == 0 ? 0 : 100.0 * emptyFilled / emptyTotal;
double missPct = solidTotal == 0 ? 0 : 100.0 * solidMissing / solidTotal;
bool holesOk = fillPct < 1.0;
bool solidOk = missPct < 1.0;
Console.WriteLine($"{(holesOk ? "ok  " : "FAIL")} truss gaps stay empty: {fillPct:F1}% of empty area filled ({emptyFilled}/{emptyTotal})");
Console.WriteLine($"{(solidOk ? "ok  " : "FAIL")} structure stays drawn: {missPct:F1}% of solid area missing ({solidMissing}/{solidTotal})");
if (!holesOk) failures++;
if (!solidOk) failures++;

Console.WriteLine(failures == 0 ? "\nALL GEOMETRY TESTS PASSED" : $"\n{failures} GEOMETRY TESTS FAILED");
return failures == 0 ? 0 : 1;
