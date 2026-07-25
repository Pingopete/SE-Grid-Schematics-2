using Keen.Game2.Simulation.WorldObjects.CubeGrids;
using Keen.Game2.Simulation.WorldObjects.CubeGrids.Damage;
using Keen.VRage.Core;
using Keen.VRage.Core.Game.GameSystems.Queries;
using Keen.VRage.Library.Mathematics;
using Keen.VRage.Physics;
using Keen.VRage.Physics.Queries;

namespace GridProbe;

// Physics ray-probe service: measures real sub-block geometry that block data
// cannot provide. First client: LCD screen-surface discovery (plane + glass
// rect). The same machinery is the tier-3 path for odd-block shape stamps.
internal static class RayProber
{
    private static IPhysics _physics;
    private static bool _acquireLogged;

    public sealed class SurfaceProbeResult
    {
        public int Axis, Sign;
        public double PlaneRel;                  // surface plane depth from block AABB min along axis (m)
        public double U0, V0, U1, V1;            // measured surface rect, relative to block min (m)
        public int Rays, Hits, GlassHits;
        public double BezelDepth, GlassDepth;    // measured depths from the face (m)
    }

    private static bool TryGetPhysics(CubeGridComponent grid)
    {
        if (_physics != null) return true;
        try
        {
            var comp = grid?.Entity?.TryGet<GridDamageReceiverComponent>();
            var f = comp?.GetType().GetField("_physics", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            _physics = f?.GetValue(comp) as IPhysics;
        }
        catch (Exception e) { ProbeLog.Error("physics acquire", e); }
        if (!_acquireLogged)
        {
            _acquireLogged = true;
            ProbeLog.Line($"RayProber: IPhysics {(_physics != null ? "acquired via GridDamageReceiverComponent" : "NOT FOUND")}.");
        }
        return _physics != null;
    }

    // Cast a KxK grid of rays inward against the block face; separate bezel
    // hits from recessed screen hits by depth clustering.
    public static SurfaceProbeResult ProbeSurface(CursorAim.PanelRef p, int axis, int sign)
    {
        try
        {
            var grid = p?.Block?.Grid;
            if (grid == null || axis < 0 || !TryGetPhysics(grid)) return null;

            const double CS = CursorAim.CellSize;
            var gwt = grid.GetWorldTransform(Vector3I.Zero);
            var bb = p.Block.AABB;
            Span<double> bmin = stackalloc double[] { bb.Min.X * CS, bb.Min.Y * CS, bb.Min.Z * CS };
            Span<double> bmax = stackalloc double[] { (bb.Max.X + 1) * CS, (bb.Max.Y + 1) * CS, (bb.Max.Z + 1) * CS };
            int ua = axis == 0 ? 2 : 0, va = axis == 1 ? 2 : 1;
            double faceCoord = sign > 0 ? bmax[axis] : bmin[axis];

            const int K = 9;
            const double startOff = 0.35, castDepth = 0.75;
            var depths = new List<(double D, double U, double V)>(K * K);
            int rays = 0, hitCount = 0;

            using var hitBuf = new Keen.VRage.Library.Memory.Buffer<SweepQueryHit>(256, Keen.VRage.Library.Memory.Allocator.Heap, "gsSurfaceProbe");
            for (int i = 0; i < K; i++)
                for (int j = 0; j < K; j++)
                {
                    rays++;
                    double u = bmin[ua] + (bmax[ua] - bmin[ua]) * (j + 0.5) / K;
                    double v = bmin[va] + (bmax[va] - bmin[va]) * (i + 0.5) / K;
                    Span<double> from = stackalloc double[3];
                    from[axis] = faceCoord + sign * startOff;
                    from[ua] = u;
                    from[va] = v;
                    var fromG = new Vector3D(from[0], from[1], from[2]);
                    Span<double> to = stackalloc double[3];
                    to[axis] = faceCoord - sign * castDepth;
                    to[ua] = u;
                    to[va] = v;
                    var toG = new Vector3D(to[0], to[1], to[2]);

                    var fromW = WorldTransform.Transform(in fromG, in gwt);
                    var toW = WorldTransform.Transform(in toG, in gwt);
                    var args = RayCastArgs.CreateFromTo(fromW, toW);
                    // The engine copies hits into the buffer's SPAN — it must have
                    // writable length, not just capacity ("Destination is too short").
                    hitBuf.Resize(192);
                    var memClear = hitBuf.AsMemory();
                    memClear.Span.Clear();
                    _physics.CastRay(hitBuf, ref args, CollisionPreset.Closest);

                    SweepQueryHit best = default;
                    bool got = false;
                    var mem = hitBuf.AsMemory();
                    for (int hi = 0; hi < mem.Length; hi++)
                    {
                        var h = mem.Span[hi];
                        if (h.Fraction <= 0f) continue; // cleared slot or invalid
                        if (!got || h.Fraction < best.Fraction) { best = h; got = true; }
                    }
                    if (!got) continue;
                    hitCount++;
                    var hitG = WorldTransform.TransformInv(best.Position, gwt);
                    double axisCoord = axis == 0 ? hitG.X : axis == 1 ? hitG.Y : hitG.Z;
                    double depth = sign > 0 ? faceCoord - axisCoord : axisCoord - faceCoord;
                    if (depth < -0.02 || depth > castDepth) continue;
                    depths.Add((Math.Max(0, depth), u, v));
                }

            if (depths.Count < 5)
            {
                ProbeLog.Line($"Surface probe: only {depths.Count}/{rays} usable hits — inconclusive.");
                return null;
            }

            double minD = double.MaxValue;
            foreach (var d in depths) if (d.D < minD) minD = d.D;
            const double sep = 0.004;
            var glass = depths.Where(d => d.D > minD + sep).ToList();
            double glassDepth;
            List<(double D, double U, double V)> surface;
            if (glass.Count >= 4)
            {
                glass.Sort((a, b) => a.D.CompareTo(b.D));
                glassDepth = glass[glass.Count / 2].D;
                surface = glass;
            }
            else
            {
                glassDepth = minD; // flush screen: no recess measurable
                surface = depths;
            }

            var res = new SurfaceProbeResult
            {
                Axis = axis,
                Sign = sign,
                PlaneRel = (faceCoord - sign * glassDepth) - bmin[axis],
                U0 = surface.Min(s => s.U) - bmin[ua],
                U1 = surface.Max(s => s.U) - bmin[ua],
                V0 = surface.Min(s => s.V) - bmin[va],
                V1 = surface.Max(s => s.V) - bmin[va],
                Rays = rays,
                Hits = hitCount,
                GlassHits = glass.Count,
                BezelDepth = minD,
                GlassDepth = glassDepth,
            };
            ProbeLog.Line($"Surface probe: {hitCount}/{rays} hits, bezel {res.BezelDepth * 100:F1} cm, screen {res.GlassDepth * 100:F1} cm behind face ({res.GlassHits} recessed hits), rect U {res.U0:F3}..{res.U1:F3} V {res.V0:F3}..{res.V1:F3} m.");
            return res;
        }
        catch (Exception e)
        {
            ProbeLog.Error("surface probe", e);
            return null;
        }
    }
}
