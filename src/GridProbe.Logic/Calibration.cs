using System.Globalization;

namespace GridProbe;

// On-panel cursor calibration: three crosshair targets at known panel
// positions; the user aims and clicks each in sequence. Solves the affine
// glass-inset correction per panel key and persists it across sessions.
internal static class Calibration
{
    private const string SavePath = @"D:\SE2Probe\gs_calibration.txt";

    public static volatile int ActiveKey = -1;
    public static volatile int Step; // 0..5: two passes over the 3 targets from two standpoints
    public static readonly (float X, float Y)[] Targets = { (0.12f, 0.12f), (0.88f, 0.12f), (0.88f, 0.88f) };

    // Full ray capture per click (grid-local), so the solver can recover the
    // actual screen plane depth — unobservable from a single standpoint.
    private static readonly double[][] _sLo = new double[6][];
    private static readonly double[][] _sDir = new double[6][];
    private static double[] _rayLo, _rayDir, _rayBmin, _rayBmax;
    private static int _rayAxis = -1, _raySign;
    private static double[] _bminAt0, _bmaxAt0;
    private static int _axisAt0, _signAt0;

    // Rel=true: Q/B/E stored relative to the panel block's AABB min (meters),
    // so the calibration survives world reloads that re-origin the grid.
    public struct CalV2 { public int Axis; public double Q, A, B, C, E; public bool Rel; }
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, CalV2> _cals2 = new();
    public static bool TryGetV2(int key, out CalV2 cal) => _cals2.TryGetValue(key, out cal);

    public static void StashRay(double[] lo, double[] dir, double[] bmin, double[] bmax, int axis, int sign)
    {
        _rayLo = lo; _rayDir = dir; _rayBmin = bmin; _rayBmax = bmax; _rayAxis = axis; _raySign = sign;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, (float U0, float U1, float V0, float V1, int Axis, int Sign)> _cals = new();
    private static int _pendingAxis = -1, _pendingSign;

    static Calibration() { Load(); }

    public static (float U0, float U1, float V0, float V1) Get(int key)
        => _cals.TryGetValue(key, out var c) ? (c.U0, c.U1, c.V0, c.V1) : (CursorAim.CalU0, CursorAim.CalU1, CursorAim.CalV0, CursorAim.CalV1);

    public static (int Axis, int Sign) GetAxis(int key)
        => _cals.TryGetValue(key, out var c) ? (c.Axis, c.Sign) : (-1, 0);

    private static int _activePanelKey;

    public static void Begin(int shipKey, int panelKey)
    {
        ActiveKey = shipKey;
        _activePanelKey = panelKey;
        Step = 0;
        ProbeLog.Line($"Calibration v2 started (ship {shipKey}, panel {panelKey}): click 3 targets, step to the side, click the same 3 again.");
    }

    private static RayProber.SurfaceProbeResult _probe;

    public static void RecordSample(int key, float rawU, float rawV, int axis, int sign, CursorAim.PanelRef panel)
    {
        if (key != ActiveKey || Step > 5) return;
        if (_rayLo == null || _rayAxis < 0) { ProbeLog.Line("Calibration: no ray stashed, click ignored."); return; }
        if (Step == 0)
        {
            _axisAt0 = _rayAxis; _signAt0 = _raySign; _bminAt0 = _rayBmin; _bmaxAt0 = _rayBmax;
            // Tier-3 testbed: measure the physical screen surface with rays at
            // calibration start; compared against the click-solved plane below.
            _probe = RayProber.ProbeSurface(panel, axis, sign);
        }
        _sLo[Step] = _rayLo;
        _sDir[Step] = _rayDir;
        ProbeLog.Line($"Calibration sample {Step + 1}/6 (pass {Step / 3 + 1}): raw ({rawU:F3},{rawV:F3}) target ({Targets[Step % 3].X:F2},{Targets[Step % 3].Y:F2})");
        Step++;
        if (Step == 3) ProbeLog.Line("Calibration: pass 1 done — MOVE 1-2m to the side, then click the same 3 targets.");
        if (Step >= 6) FinishV2(key);
    }

    private static void FinishV2(int key)
    {
        try
        {
            int axis = _axisAt0;
            int ua = axis == 0 ? 2 : 0, va = axis == 1 ? 2 : 1;
            double qLo = _bminAt0[axis] - 0.01, qHi = _bmaxAt0[axis] + 0.01;
            double bestSse = double.MaxValue, worstSse = 0;
            var best = default(CalV2);
            var xs = new double[6];
            var ys = new double[6];
            for (int i = 0; i <= 400; i++)
            {
                double q = qLo + (qHi - qLo) * i / 400.0;
                bool ok = true;
                for (int s = 0; s < 6; s++)
                {
                    double da = _sDir[s][axis];
                    if (Math.Abs(da) < 1e-9) { ok = false; break; }
                    double t = (q - _sLo[s][axis]) / da;
                    if (t <= 0) { ok = false; break; }
                    xs[s] = _sLo[s][ua] + _sDir[s][ua] * t;
                    ys[s] = _sLo[s][va] + _sDir[s][va] * t;
                }
                if (!ok) continue;
                FitLine(xs, s => Targets[s % 3].X, out double a, out double b, out double sseU);
                FitLine(ys, s => Targets[s % 3].Y, out double c, out double e, out double sseV);
                double sse = sseU + sseV;
                if (sse < bestSse) { bestSse = sse; best = new CalV2 { Axis = axis, Q = q, A = a, B = b, C = c, E = e }; }
                if (sse > worstSse) worstSse = sse;
            }
            if (bestSse == double.MaxValue) { ProbeLog.Line("Calibration v2 FAILED: no valid plane found."); ActiveKey = -1; return; }
            int ua2 = axis == 0 ? 2 : 0, va2 = axis == 1 ? 2 : 1;
            best = new CalV2
            {
                Axis = axis,
                Q = best.Q - _bminAt0[axis],
                A = best.A,
                B = best.B + best.A * _bminAt0[ua2],
                C = best.C,
                E = best.E + best.C * _bminAt0[va2],
                Rel = true,
            };
            _cals2[_activePanelKey] = best;
            Save();
            double depthRel = _signAt0 > 0 ? (_bmaxAt0[axis] - _bminAt0[axis]) - best.Q : best.Q;
            ProbeLog.Line($"Calibration v2 done (ship {key}, panel {_activePanelKey}): axis {axis}, image plane depth {depthRel * 100:F1} cm behind face (block-relative), rms {Math.Sqrt(bestSse / 12):F4}.");
            if (_probe != null && _probe.Axis == axis)
            {
                double probedDepth = _signAt0 > 0 ? (_bmaxAt0[axis] - _bminAt0[axis]) - _probe.PlaneRel : _probe.PlaneRel;
                ProbeLog.Line($"Probe-vs-clicks: ray-measured screen depth {probedDepth * 100:F1} cm vs click-solved {depthRel * 100:F1} cm (delta {(probedDepth - depthRel) * 100:F1} cm).");
            }
            if (worstSse > 0 && bestSse / worstSse > 0.5)
                ProbeLog.Line("Calibration WARNING: depth weakly observable — the two standpoints were too similar; redo with a bigger sideways step.");
        }
        catch (Exception e) { ProbeLog.Error("calibration v2 finish", e); }
        ActiveKey = -1;
    }

    private static void FitLine(double[] x, Func<int, double> target, out double a, out double b, out double sse)
    {
        int n = x.Length;
        double sx = 0, st = 0, sxx = 0, sxt = 0;
        for (int i = 0; i < n; i++) { double t = target(i); sx += x[i]; st += t; sxx += x[i] * x[i]; sxt += x[i] * t; }
        double denom = n * sxx - sx * sx;
        if (Math.Abs(denom) < 1e-12) { a = 0; b = st / n; }
        else { a = (n * sxt - sx * st) / denom; b = (st - a * sx) / n; }
        sse = 0;
        for (int i = 0; i < n; i++) { double r = a * x[i] + b - target(i); sse += r * r; }
    }

    private static void Load()
    {
        try
        {
            if (!File.Exists(SavePath)) return;
            foreach (var line in File.ReadAllLines(SavePath))
            {
                var p = line.Split(' ');
                if (p.Length == 8 && (p[0] == "P2" || p[0] == "P3") && int.TryParse(p[1], out var k2))
                    _cals2[k2] = new CalV2
                    {
                        Axis = int.Parse(p[2]),
                        Q = double.Parse(p[3], CultureInfo.InvariantCulture),
                        A = double.Parse(p[4], CultureInfo.InvariantCulture),
                        B = double.Parse(p[5], CultureInfo.InvariantCulture),
                        C = double.Parse(p[6], CultureInfo.InvariantCulture),
                        E = double.Parse(p[7], CultureInfo.InvariantCulture),
                        Rel = p[0] == "P3",
                    };
                else if (p.Length >= 5 && int.TryParse(p[0], out var k))
                    _cals[k] = (float.Parse(p[1], CultureInfo.InvariantCulture), float.Parse(p[2], CultureInfo.InvariantCulture),
                                float.Parse(p[3], CultureInfo.InvariantCulture), float.Parse(p[4], CultureInfo.InvariantCulture),
                                p.Length >= 7 ? int.Parse(p[5]) : -1, p.Length >= 7 ? int.Parse(p[6]) : 0);
            }
            if (!_cals.IsEmpty || !_cals2.IsEmpty) ProbeLog.Line($"Loaded {_cals.Count} legacy + {_cals2.Count} v2 panel calibration(s).");
        }
        catch (Exception e) { ProbeLog.Error("calibration load", e); }
    }

    private static void Save()
    {
        try
        {
            var lines = _cals.Select(kv => string.Create(CultureInfo.InvariantCulture,
                    $"{kv.Key} {kv.Value.U0} {kv.Value.U1} {kv.Value.V0} {kv.Value.V1} {kv.Value.Axis} {kv.Value.Sign}"))
                .Concat(_cals2.Select(kv => string.Create(CultureInfo.InvariantCulture,
                    $"{(kv.Value.Rel ? "P3" : "P2")} {kv.Key} {kv.Value.Axis} {kv.Value.Q} {kv.Value.A} {kv.Value.B} {kv.Value.C} {kv.Value.E}")));
            File.WriteAllLines(SavePath, lines);
        }
        catch (Exception e) { ProbeLog.Error("calibration save", e); }
    }
}
