using Keen.Game2.Simulation.WorldObjects.CubeBlocks;
using Keen.VRage.Library.Mathematics;

namespace GridProbe;

// Ctrl+Shift+Alt+Middle-click on the panel: names every block in the hovered
// cell's view column — the ground truth for "why is this edge stepped".
internal static class CellInspector
{
    public static void Inspect(int key, float u, float v)
    {
        try
        {
            if (!VectorLcd.Scans.TryGetValue(key, out var scan)) { ProbeLog.Line("Inspect: no scan for key."); return; }
            if (!VectorLcd.PanelRes.TryGetValue(key, out var res)) return;
            var st = PanelState.Get(key);
            (int vw, int vh) = st.ViewAxis switch
            {
                PanelState.ViewFront => (scan.Size.X, scan.Size.Y),
                PanelState.ViewSide => (scan.Size.Z, scan.Size.Y),
                _ => (scan.Size.X, scan.Size.Z),
            };
            var win = PanelState.GetWindow(st, vw, vh, res.W, res.H);
            double px = u * res.W, py = v * res.H;
            int du = (int)Math.Floor(win.Wx0 + (px - win.Vx0) * win.WinW / win.VpW);
            int dv = (int)Math.Floor(win.Wy0 + (py - win.Vy0) * win.WinH / win.VpH);
            if (du < 0 || dv < 0 || du >= vw || dv >= vh) { ProbeLog.Line($"Inspect k{key}: cell ({du},{dv}) outside image."); return; }

            var size = scan.Size;
            var min = scan.Min;
            int gx = int.MinValue, gy = int.MinValue, gz = int.MinValue;
            switch (st.ViewAxis)
            {
                case PanelState.ViewSide:  // displayed [Z,Y] = Rot90CCW of [Y,Z]
                    gy = min.Y + (size.Y - 1 - dv);
                    gz = min.Z + du;
                    break;
                case PanelState.ViewFront: // displayed [X,Y] = Rot180 of [X,Y]
                    gx = min.X + (size.X - 1 - du);
                    gy = min.Y + (size.Y - 1 - dv);
                    break;
                default:                   // top: [X,Z] unrotated
                    gx = min.X + du;
                    gz = min.Z + dv;
                    break;
            }
            ProbeLog.Line($"Inspect k{key} {PanelState.ViewName(st.ViewAxis)} cell ({du},{dv}) -> grid x={Fmt(gx)} y={Fmt(gy)} z={Fmt(gz)}");

            if (!CursorAim.Tagged.TryGetValue(key, out var panels) || panels.Count == 0) return;
            var grid = panels[0].Block?.Grid;
            if (grid == null) return;
            ListColumn(grid, st.ViewAxis, gx, gy, gz);
        }
        catch (Exception e) { ProbeLog.Error("inspect", e); }
    }

    private static string Fmt(int v) => v == int.MinValue ? "*" : v.ToString();

    // Lists every block whose AABB covers the given column (an int.MinValue
    // coordinate is the collapsed axis). Shared by manual and automatic audits.
    public static void ListColumn(Keen.Game2.Simulation.WorldObjects.CubeGrids.CubeGridComponent grid, int viewAxis, int gx, int gy, int gz)
    {
        int logged = 0;
        grid.VisitAllBlocksWithComponent<CubeBlockComponent>(b =>
        {
            if (logged >= 12) return;
            try
            {
                var bb = b.AABB;
                bool hit = viewAxis switch
                {
                    PanelState.ViewSide => gy >= bb.Min.Y && gy <= bb.Max.Y && gz >= bb.Min.Z && gz <= bb.Max.Z,
                    PanelState.ViewFront => gx >= bb.Min.X && gx <= bb.Max.X && gy >= bb.Min.Y && gy <= bb.Max.Y,
                    _ => gx >= bb.Min.X && gx <= bb.Max.X && gz >= bb.Min.Z && gz <= bb.Max.Z,
                };
                if (!hit) return;
                string kind = "?";
                try { kind = BlockShapes.Describe(b.Definition); } catch { }
                string name = "?";
                try
                {
                    var d = b.Definition;
                    name = (d?.GetType().GetProperty("DebugName")?.GetValue(d) ?? d?.ToString())?.ToString() ?? "?";
                }
                catch { }
                if (name.Length > 70) name = name[^70..];
                var ext = bb.Max - bb.Min + new Vector3I(1, 1, 1);
                logged++;
                ProbeLog.Line($"  block[{logged}] kind={kind} ext={ext.X}x{ext.Y}x{ext.Z} min=({bb.Min.X},{bb.Min.Y},{bb.Min.Z}) F={b.BlockOrientation.Forward} U={b.BlockOrientation.Up} :: {name}");
            }
            catch { }
        }, includeSubgrids: true);
        ProbeLog.Line($"  column listing done: {logged} block(s).");
    }
}
