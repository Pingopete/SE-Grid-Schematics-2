using Keen.Game2.Client.GameSystems.CameraSystems;
using Keen.Game2.Simulation.WorldObjects.CubeBlocks;
using Keen.Game2.Simulation.WorldObjects.CubeBlocks.Lcd;
using Keen.VRage.Core;
using Keen.VRage.Core.Game.Data;
using Keen.VRage.DCS.Accessors;
using Keen.VRage.DCS.Components;
using Keen.VRage.Library.Mathematics;

namespace GridProbe;

// Camera-ray -> tagged panel -> surface UV. Pure geometry, no physics API:
// transform the view ray into the grid frame and slab-test the LCD block's
// cell box; the entry face is the one facing the viewer.
internal static class CursorAim
{
    public const float CellSize = 0.25f;
    public const double MaxAimDistance = 25.0;
    public static volatile int UvMode = 6; // bit0 swap UV, bit1 flip U, bit2 flip V — tuned on-glass (6 = both axes flipped)

    // Glass calibration: the visible screen is inset from the block face (bezel),
    // so raw face UV needs an affine correction. Set from two corner aims.
    public static volatile float CalU0 = 0.223f, CalU1 = 0.970f, CalV0 = 0.282f, CalV1 = 0.888f; // measured on-glass 2026-07-23

    // The visible image plane sits behind the block's outer face; intersecting
    // the face causes angle-dependent parallax error toward panel edges.
    public static volatile float GlassDepth = 0.10f; // meters, tuned on-glass (0.03 too shallow: cursor pulled toward viewer when off-axis)

    public sealed class PanelRef
    {
        public LcdMultiPanelComponent Lcd;
        public CubeBlockComponent Block;
    }

    // shipKey -> tagged panel blocks (rebuilt by LcdProbe each scan pass)
    public static readonly System.Collections.Concurrent.ConcurrentDictionary<int, List<PanelRef>> Tagged = new();
    // shipKey -> aim point in surface UV [0,1]; absent when not aiming at that ship's panel
    public static readonly System.Collections.Concurrent.ConcurrentDictionary<int, (float U, float V)> Aim = new();

    private static CameraSystemComponent _camSys;
    private static Keen.VRage.DCS.Scenes.Scene _camScene;
    private static long _lastUpdate, _lastCamSearch, _lastAimLog;
    private static int _errs;

    public static void Update()
    {
        try
        {
            long now = Environment.TickCount64;
            if (now - _lastUpdate < 15) return;
            _lastUpdate = now;
            if (Tagged.IsEmpty) return;

            if (_camSys == null)
            {
                if (now - _lastCamSearch < 2000) return;
                _lastCamSearch = now;
                if (!TryFindCamera()) return;
            }

            var camEnt = _camSys.RenderCameraEntity;
            if (camEnt == null) return;
            var wt = EntityTransformFunctions.GetWorldTransform(new DEntityContext(_camScene, camEnt.DEntity));
            var camPos = wt.Position;
            var fwd = WorldTransform.TransformDirection(new Vector3(0f, 0f, -1f), wt);

            int bestKey = 0, bestAxis = -1, bestSign = 0;
            double bestT = double.MaxValue;
            float bestU = 0f, bestV = 0f;
            bool bestFinal = false; // v2 path already yields final calibrated uv
            PanelRef bestPanel = null;
            Span<double> lo0 = stackalloc double[3];
            Span<double> dir0 = stackalloc double[3];
            foreach (var kv in Tagged)
            {
                bool calibrating = Calibration.ActiveKey == kv.Key;
                var p0 = kv.Value.Count > 0 ? kv.Value[0] : null;
                if (p0 == null) continue;
                int panelKey = StablePanelKey(p0);
                if (!calibrating && Calibration.TryGetV2(panelKey, out var c2))
                {
                    // Solved screen plane: direct plane intersection + affine map.
                    var grid0 = p0.Block?.Grid;
                    if (grid0 == null) continue;
                    var gwt0 = grid0.GetWorldTransform(Vector3I.Zero);
                    var lp0 = WorldTransform.TransformInv(camPos, gwt0);
                    var ld0 = WorldTransform.TransformDirectionInv(fwd, gwt0);
                    lo0[0] = lp0.X; lo0[1] = lp0.Y; lo0[2] = lp0.Z;
                    dir0[0] = ld0.X; dir0[1] = ld0.Y; dir0[2] = ld0.Z;
                    int a2 = c2.Axis;
                    if (Math.Abs(dir0[a2]) < 1e-9) continue;
                    int ua2 = a2 == 0 ? 2 : 0, va2 = a2 == 1 ? 2 : 1;

                    // Block-relative calibration: rebuild absolute plane/affine from
                    // the block's CURRENT AABB, then sanity-check the plane actually
                    // lies within the block (stale calibrations are ignored).
                    var bb2 = p0.Block.AABB;
                    double bnAxis = Axis(bb2.Min, a2) * (double)CellSize;
                    double bxAxis = (Axis(bb2.Max, a2) + 1) * (double)CellSize;
                    double q = c2.Rel ? c2.Q + bnAxis : c2.Q;
                    double affB = c2.Rel ? c2.B - c2.A * (Axis(bb2.Min, ua2) * (double)CellSize) : c2.B;
                    double affE = c2.Rel ? c2.E - c2.C * (Axis(bb2.Min, va2) * (double)CellSize) : c2.E;
                    if (q < bnAxis - 0.3 || q > bxAxis + 0.3)
                    {
                        if (_staleCalLogs++ < 2) ProbeLog.Line($"Calibration for panel key {StablePanelKey(p0)} is stale (plane outside block) — recalibrate.");
                        // fall through to the uncalibrated slab path below
                    }
                    else
                    {
                        double tp = (q - lo0[a2]) / dir0[a2];
                        if (tp <= 1e-4 || tp > MaxAimDistance || tp >= bestT) continue;
                        double xu = lo0[ua2] + dir0[ua2] * tp;
                        double yv = lo0[va2] + dir0[va2] * tp;
                        double uu = c2.A * xu + affB, vv = c2.C * yv + affE;
                        if (uu < -0.3 || uu > 1.3 || vv < -0.3 || vv > 1.3) continue;
                        bestT = tp; bestKey = kv.Key;
                        bestU = (float)Math.Clamp(uu, 0, 1); bestV = (float)Math.Clamp(vv, 0, 1);
                        bestFinal = true; bestAxis = a2; bestSign = 0; bestPanel = p0;
                        continue;
                    }
                }
                var (lockAxis, lockSign) = calibrating ? (-1, 0) : Calibration.GetAxis(kv.Key);
                foreach (var p in kv.Value)
                {
                    if (TryHitPanel(p, camPos, fwd, lockAxis, lockSign, out var t, out var u, out var v, out var ax, out var sg) && t < bestT)
                    {
                        bestT = t; bestKey = kv.Key; bestU = u; bestV = v;
                        bestFinal = false; bestAxis = ax; bestSign = sg; bestPanel = p;
                    }
                }
            }

            foreach (var key in Aim.Keys)
                if (bestT == double.MaxValue || key != bestKey)
                    Aim.TryRemove(key, out _);
            if (bestT < double.MaxValue)
            {
                float cu, cv;
                if (bestFinal) { cu = bestU; cv = bestV; }
                else
                {
                    var cal = Calibration.Get(bestKey);
                    cu = Math.Clamp((bestU - cal.U0) / Math.Max(0.001f, cal.U1 - cal.U0), 0f, 1f);
                    cv = Math.Clamp((bestV - cal.V0) / Math.Max(0.001f, cal.V1 - cal.V0), 0f, 1f);
                }
                Aim[bestKey] = (cu, cv);
                if (now - _lastAimLog > 2000)
                {
                    _lastAimLog = now;
                    ProbeLog.Line($"Aim: key {bestKey} raw ({bestU:F3},{bestV:F3}) cal ({cu:F3},{cv:F3}) dist {bestT:F2}m{(bestFinal ? " [v2]" : "")}");
                }
                if (Calibration.ActiveKey == bestKey && bestPanel != null)
                    StashCalRay(bestPanel, camPos, fwd, bestAxis, bestSign);
                _lastAxis = bestAxis; _lastSign = bestSign;
                HandleClick(bestKey, cu, cv, bestU, bestV, bestPanel);
                HandleDrag(bestKey, cu, cv, now);

                // Ctrl+Shift+Alt + Middle click: cell inspector (names the blocks
                // in the hovered column — ground truth for stepped edges).
                bool mid = (GetAsyncKeyState(0x04) & 0x8000) != 0;
                if (mid && !_midWasDown && ModifiersHeld())
                    CellInspector.Inspect(bestKey, cu, cv);
                _midWasDown = mid;
            }
            else _clickWasDown = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
        }
        catch (Exception e) { if (_errs++ < 3) ProbeLog.Error("cursor aim", e); }
    }

    private static int _lastAxis = -1, _lastSign;
    private static int _staleCalLogs;
    private static int Axis(Vector3I v, int a) => a == 0 ? v.X : a == 1 ? v.Y : v.Z;

    // Stable across sessions and grid-enumeration order (the ship key is not:
    // it is derived from DEntity hashes that can change between passes).
    public static int StablePanelKey(PanelRef p)
    {
        var m = p.Block.AABB.Min;
        unchecked
        {
            int h = (int)2166136261;
            h = (h ^ m.X) * 16777619;
            h = (h ^ m.Y) * 16777619;
            h = (h ^ m.Z) * 16777619;
            return h & 0x7FFFFFFF;
        }
    }

    private static void StashCalRay(PanelRef p, Vector3D camPos, Vector3 fwd, int axis, int sign)
    {
        try
        {
            var grid = p.Block?.Grid;
            if (grid == null) return;
            var gwt = grid.GetWorldTransform(Vector3I.Zero);
            var lp = WorldTransform.TransformInv(camPos, gwt);
            var ld = WorldTransform.TransformDirectionInv(fwd, gwt);
            var bb = p.Block.AABB;
            Calibration.StashRay(
                new[] { lp.X, lp.Y, lp.Z },
                new[] { (double)ld.X, ld.Y, ld.Z },
                new[] { bb.Min.X * (double)CellSize, bb.Min.Y * (double)CellSize, bb.Min.Z * (double)CellSize },
                new[] { (bb.Max.X + 1) * (double)CellSize, (bb.Max.Y + 1) * (double)CellSize, (bb.Max.Z + 1) * (double)CellSize },
                axis, sign);
        }
        catch (Exception e) { if (_errs++ < 3) ProbeLog.Error("stash cal ray", e); }
    }

    private const int VK_LBUTTON = 0x01;
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
    private static bool _clickWasDown;
    private static bool _midWasDown;

    private static bool ModifiersHeld()
        => (GetAsyncKeyState(0x11) & 0x8000) != 0   // Ctrl
        && (GetAsyncKeyState(0x10) & 0x8000) != 0   // Shift
        && (GetAsyncKeyState(0x12) & 0x8000) != 0;  // Alt

    private static void HandleClick(int key, float u, float v, float rawU, float rawV, PanelRef panel)
    {
        bool down = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
        bool rising = down && !_clickWasDown;
        _clickWasDown = down;
        if (!rising) return;

        // Ctrl+Shift+Alt+LClick while aiming at a panel: force-start calibration
        // (cursor-independent — works even when the calibration is broken).
        if (ModifiersHeld())
        {
            if (Tagged.TryGetValue(key, out var pnls) && pnls.Count > 0)
                Calibration.Begin(key, StablePanelKey(pnls[0]));
            return;
        }

        if (Calibration.ActiveKey == key)
        {
            Calibration.RecordSample(key, rawU, rawV, _lastAxis, _lastSign, panel);
            return;
        }

        if (!VectorLcd.PanelRes.TryGetValue(key, out var res)) return;
        float px = u * res.W, py = v * res.H;
        var st = PanelState.Get(key);
        var hit = PanelUi.HitTest(res.W, res.H, px, py);
        ProbeLog.Line($"Click: key {key} at ({px:F0},{py:F0}) -> {hit}");
        switch (hit)
        {
            case PanelUi.Button.ZoomIn: st.Zoom = Math.Min(16f, st.Zoom * 1.25f); break;
            case PanelUi.Button.ZoomOut: st.Zoom = Math.Max(1f, st.Zoom / 1.25f); break;
            case PanelUi.Button.ViewTop: st.ViewAxis = PanelState.ViewTop; st.PanX = st.PanY = -1f; break;
            case PanelUi.Button.ViewSide: st.ViewAxis = PanelState.ViewSide; st.PanX = st.PanY = -1f; break;
            case PanelUi.Button.ViewFront: st.ViewAxis = PanelState.ViewFront; st.PanX = st.PanY = -1f; break;
            case PanelUi.Button.Mode: st.Mode = (st.Mode + 1) % 3; break;
            case PanelUi.Button.Conveyor:
            case PanelUi.Button.Power:
            case PanelUi.Button.Gas:
            {
                // One overlay at a time: pressing the active one clears it,
                // pressing another switches straight to it.
                int want = PanelUi.HighlightOf(hit);
                st.Highlight = st.Highlight == want ? PanelState.HighlightNone : want;
                ProbeLog.Line($"Highlight -> {PanelState.HighlightName(st.Highlight)}");
                break;
            }
            case PanelUi.Button.Refresh:
                VectorLcd.ForceRefresh(key);
                return;
            case PanelUi.Button.Calibrate:
                if (Tagged.TryGetValue(key, out var panels) && panels.Count > 0)
                    Calibration.Begin(key, StablePanelKey(panels[0]));
                return;
            default: return;
        }
        if (st.Mode != PanelState.ModeThickness) KickChannels(key, st);
        System.Threading.Interlocked.Increment(ref st.Version);
    }

    private static int _dragKey = -1;
    private static float _dragU, _dragV;

    private static void HandleDrag(int key, float u, float v, long now)
    {
        bool down = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
        if (!down) { _dragKey = -1; return; }

        if (_dragKey < 0)
        {
            // Start a drag only on the image area, not on buttons, not while
            // calibrating, and not on the calibration modifier-click.
            if (Calibration.ActiveKey == key || ModifiersHeld()) return;
            if (!VectorLcd.PanelRes.TryGetValue(key, out var res0)) return;
            if (PanelUi.HitTest(res0.W, res0.H, u * res0.W, v * res0.H) != PanelUi.Button.None) return;
            _dragKey = key; _dragU = u; _dragV = v;
            return;
        }
        if (_dragKey != key) return;

        var st = PanelState.Get(key);
        if (st.Zoom <= 1.001f || !VectorLcd.Scans.TryGetValue(key, out var scan)) return;
        if (!VectorLcd.PanelRes.TryGetValue(key, out var res)) return;
        (int vw, int vh) = st.ViewAxis switch
        {
            PanelState.ViewFront => (scan.Size.X, scan.Size.Y),
            PanelState.ViewSide => (scan.Size.Z, scan.Size.Y), // side arrays are rotated upright
            _ => (scan.Size.X, scan.Size.Z),
        };
        float du = u - _dragU, dv = v - _dragV;
        if (Math.Abs(du) < 0.002f && Math.Abs(dv) < 0.002f) return;

        // Content follows the cursor; the vector regime redraws live every frame,
        // so pan is pure state — nothing rebuilds.
        var win = PanelState.GetWindow(st, vw, vh, res.W, res.H);
        double cx = st.PanX >= 0 ? st.PanX : vw / 2.0;
        double cy = st.PanY >= 0 ? st.PanY : vh / 2.0;
        cx -= du * res.W * win.WinW / win.VpW;
        cy -= dv * res.H * win.WinH / win.VpH;
        cx = win.WinW >= vw ? vw / 2.0 : Math.Clamp(cx, win.WinW / 2, vw - win.WinW / 2);
        cy = win.WinH >= vh ? vh / 2.0 : Math.Clamp(cy, win.WinH / 2, vh - win.WinH / 2);
        st.PanX = (float)cx;
        st.PanY = (float)cy;
        _dragU = u; _dragV = v;
    }

    private static void KickChannels(int key, PanelState st)
    {
        if (!VectorLcd.Scans.TryGetValue(key, out var scan)) return;
        int axis = PanelState.DepthAxisOf(st.ViewAxis);
        if (scan.ChannelAxis == axis) return;
        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            try { scan.EnsureChannels(axis); System.Threading.Interlocked.Increment(ref st.Version); }
            catch (Exception e) { ProbeLog.Error("channel compute", e); }
        });
    }

    private static bool TryFindCamera()
    {
        foreach (var s in SceneLocator.Sessions.Keys)
        {
            var scene = s.Scene;
            if (scene == null) continue;
            try
            {
                foreach (var d in scene.EnumerateEntities())
                {
                    var e = Entity.TryGetFromDataEntity(new DEntityContext(scene, d));
                    var cs = e?.TryGet<CameraSystemComponent>();
                    if (cs != null)
                    {
                        _camSys = cs;
                        _camScene = scene;
                        ProbeLog.Line($"CameraSystemComponent found (scene '{scene.DebugName}').");
                        return true;
                    }
                }
            }
            catch { }
        }
        return false;
    }

    private static bool TryHitPanel(PanelRef p, Vector3D camPos, Vector3 fwd, int lockAxis, int lockSign,
        out double t, out float u, out float v, out int axis, out int sign)
    {
        t = 0; u = 0; v = 0; axis = -1; sign = 0;
        var block = p.Block;
        var grid = block?.Grid;
        if (grid == null) return false;

        var gwt = grid.GetWorldTransform(Vector3I.Zero);
        var lp = WorldTransform.TransformInv(camPos, gwt);
        var ld = WorldTransform.TransformDirectionInv(fwd, gwt);

        var bb = block.AABB;
        Span<double> lo = stackalloc double[] { lp.X, lp.Y, lp.Z };
        Span<double> dir = stackalloc double[] { ld.X, ld.Y, ld.Z };
        Span<double> bmin = stackalloc double[] { bb.Min.X * CellSize, bb.Min.Y * CellSize, bb.Min.Z * CellSize };
        Span<double> bmax = stackalloc double[] { (bb.Max.X + 1) * CellSize, (bb.Max.Y + 1) * CellSize, (bb.Max.Z + 1) * CellSize };

        if (lockAxis >= 0)
        {
            // Calibrated screen plane: intersect it directly. No entry-face
            // detection, so aiming near panel edges can't flip to a side face.
            axis = lockAxis; sign = lockSign;
            if (Math.Abs(dir[lockAxis]) < 1e-9) return false;
            double plane = lockSign > 0 ? bmax[lockAxis] - GlassDepth : bmin[lockAxis] + GlassDepth;
            double tp = (plane - lo[lockAxis]) / dir[lockAxis];
            if (tp <= 1e-4 || tp > MaxAimDistance) return false;
            Span<double> hp = stackalloc double[3];
            for (int a = 0; a < 3; a++) hp[a] = lo[a] + dir[a] * tp;
            int ul = lockAxis == 0 ? 2 : 0, vl = lockAxis == 1 ? 2 : 1;
            double du = (hp[ul] - bmin[ul]) / (bmax[ul] - bmin[ul]);
            double dv = (hp[vl] - bmin[vl]) / (bmax[vl] - bmin[vl]);
            const double margin = 0.30;
            if (du < -margin || du > 1 + margin || dv < -margin || dv > 1 + margin) return false;
            t = tp;
            ApplyUvMode((float)du, (float)dv, out u, out v);
            return true;
        }

        double tmin = 1e-4, tmax = MaxAimDistance;
        int hitAxis = -1;
        for (int a = 0; a < 3; a++)
        {
            if (Math.Abs(dir[a]) < 1e-9)
            {
                if (lo[a] < bmin[a] || lo[a] > bmax[a]) return false;
                continue;
            }
            double t1 = (bmin[a] - lo[a]) / dir[a];
            double t2 = (bmax[a] - lo[a]) / dir[a];
            double entry = Math.Min(t1, t2), exit = Math.Max(t1, t2);
            if (entry > tmin) { tmin = entry; hitAxis = a; }
            if (exit < tmax) tmax = exit;
            if (tmin > tmax) return false;
        }
        if (hitAxis < 0) return false; // camera inside the block box

        axis = hitAxis;
        sign = dir[hitAxis] > 0 ? -1 : 1; // outward normal of the entry face

        t = tmin;
        // Parallax correction: intersect the inset image plane, not the outer face.
        double faceCoord = dir[hitAxis] > 0 ? bmin[hitAxis] : bmax[hitAxis];
        double insetCoord = dir[hitAxis] > 0 ? faceCoord + GlassDepth : faceCoord - GlassDepth;
        double tInset = (insetCoord - lo[hitAxis]) / dir[hitAxis];
        if (tInset > 0) t = tInset;

        Span<double> hit = stackalloc double[3];
        for (int a = 0; a < 3; a++) hit[a] = lo[a] + dir[a] * t;

        int ua = hitAxis == 0 ? 2 : 0;             // X face -> U along Z, else U along X
        int va = hitAxis == 1 ? 2 : 1;             // Y face -> V along Z, else V along Y
        float ru = (float)((hit[ua] - bmin[ua]) / (bmax[ua] - bmin[ua]));
        float rv = (float)((hit[va] - bmin[va]) / (bmax[va] - bmin[va]));
        ApplyUvMode(ru, rv, out u, out v);
        return true;
    }

    private static void ApplyUvMode(float ru, float rv, out float u, out float v)
    {
        if ((UvMode & 1) != 0) (ru, rv) = (rv, ru);
        if ((UvMode & 2) != 0) ru = 1f - ru;
        if ((UvMode & 4) != 0) rv = 1f - rv;
        u = Math.Clamp(ru, 0f, 1f);
        v = Math.Clamp(rv, 0f, 1f);
    }
}
