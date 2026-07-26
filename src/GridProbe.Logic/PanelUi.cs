using Keen.VRage.Library.Mathematics;
using Keen.VRage.Render.Contracts;

namespace GridProbe;

// Edge button bar: zoom, view angle (top/side/front), depth mode cycle.
// Layout is shared by the renderer (draw) and the aim system (click hit-test).
internal static class PanelUi
{
    // Shared with the render overlay so the button and the highlighted blocks
    // are unmistakably the same colour.
    public static readonly ColorSRGB Lime = new((byte)140, (byte)255, (byte)40, (byte)255);

    public enum Button
    {
        None, ZoomIn, ZoomOut, ViewTop, ViewSide, ViewFront, Mode, Calibrate, Refresh,
        Conveyor, Power, Gas,
    }

    public struct Rect { public Button Id; public float X0, Y0, X1, Y1; }

    public static Rect[] Layout(float W, float H)
    {
        const float size = 56f, gap = 10f, edge = 14f;
        var rects = new List<Rect>(12);

        // Right column: view and display controls.
        float rx0 = W - edge - size, rx1 = W - edge;
        float y = 44f;
        var right = new[] { Button.ZoomIn, Button.ZoomOut, Button.ViewTop, Button.ViewSide, Button.ViewFront, Button.Mode, Button.Calibrate, Button.Refresh };
        foreach (var id in right)
        {
            if (id == Button.ViewTop || id == Button.Mode || id == Button.Calibrate) y += 18f; // group separators
            rects.Add(new Rect { Id = id, X0 = rx0, Y0 = y, X1 = rx1, Y1 = y + size });
            y += size + gap;
        }

        // Left column: system highlight overlays.
        float lx0 = edge, lx1 = edge + size;
        y = 44f;
        foreach (var id in new[] { Button.Conveyor, Button.Power, Button.Gas })
        {
            rects.Add(new Rect { Id = id, X0 = lx0, Y0 = y, X1 = lx1, Y1 = y + size });
            y += size + gap;
        }
        return rects.ToArray();
    }

    // Which highlight a button selects, or HighlightNone if it isn't one.
    public static int HighlightOf(Button b) => b switch
    {
        Button.Conveyor => PanelState.HighlightConveyor,
        Button.Power => PanelState.HighlightPower,
        Button.Gas => PanelState.HighlightGas,
        _ => PanelState.HighlightNone,
    };

    public static Button HitTest(float W, float H, float px, float py)
    {
        foreach (var r in Layout(W, H))
            if (px >= r.X0 && px <= r.X1 && py >= r.Y0 && py <= r.Y1)
                return r.Id;
        return Button.None;
    }

    public static void Draw(IDrawBatch batch, float W, float H, PanelState st, Button hover)
    {
        // Translucent fills must be premultiplied — see VectorLcd.Premul.
        var bg = VectorLcd.Premul(16, 22, 28, 185);
        var bgHover = VectorLcd.Premul(45, 60, 75, 220);
        var icon = new ColorSRGB((byte)215, (byte)225, (byte)235, (byte)255);
        var accent = new ColorSRGB((byte)90, (byte)190, (byte)255, (byte)255);

        foreach (var r in Layout(W, H))
        {
            int hl = HighlightOf(r.Id);
            bool highlightOn = hl != PanelState.HighlightNone && st.Highlight == hl;
            bool active = highlightOn
                       || (r.Id == Button.ViewTop && st.ViewAxis == PanelState.ViewTop)
                       || (r.Id == Button.ViewSide && st.ViewAxis == PanelState.ViewSide)
                       || (r.Id == Button.ViewFront && st.ViewAxis == PanelState.ViewFront);
            Fill(batch, r.X0, r.Y0, r.X1, r.Y1, r.Id == hover ? bgHover : bg);
            // Selected overlays outline in the same lime they paint the ship in.
            if (active) Border(batch, r.X0, r.Y0, r.X1, r.Y1, 3f, highlightOn ? Lime : accent);

            float cx = (r.X0 + r.X1) / 2f, cy = (r.Y0 + r.Y1) / 2f;
            float s = (r.X1 - r.X0) * 0.30f;
            var sysIcon = highlightOn ? Lime : icon;
            switch (r.Id)
            {
                case Button.Conveyor:  // junction: a line with branches
                    Fill(batch, cx - s * 1.2f, cy - 3, cx + s * 1.2f, cy + 3, sysIcon);
                    Fill(batch, cx - 3, cy - s * 1.2f, cx + 3, cy + 3, sysIcon);
                    Fill(batch, cx - s * 1.25f, cy - s * 0.5f, cx - s * 0.75f, cy + s * 0.5f, sysIcon);
                    Fill(batch, cx + s * 0.75f, cy - s * 0.5f, cx + s * 1.25f, cy + s * 0.5f, sysIcon);
                    break;
                case Button.Power:     // lightning bolt, two offset wedges
                    Fill(batch, cx - s * 0.15f, cy - s * 1.2f, cx + s * 0.55f, cy, sysIcon);
                    Fill(batch, cx - s * 0.55f, cy, cx + s * 0.15f, cy + s * 1.2f, sysIcon);
                    break;
                case Button.Gas:       // canister: rounded body with a neck
                    Fill(batch, cx - s * 0.25f, cy - s * 1.25f, cx + s * 0.25f, cy - s * 0.85f, sysIcon);
                    Fill(batch, cx - s * 0.75f, cy - s * 0.85f, cx + s * 0.75f, cy + s * 1.15f, sysIcon);
                    Fill(batch, cx - s * 0.45f, cy - s * 0.45f, cx + s * 0.45f, cy + s * 0.25f, r.Id == hover ? bgHover : bg);
                    break;
                case Button.ZoomIn:
                    Fill(batch, cx - s, cy - 3, cx + s, cy + 3, icon);
                    Fill(batch, cx - 3, cy - s, cx + 3, cy + s, icon);
                    break;
                case Button.ZoomOut:
                    Fill(batch, cx - s, cy - 3, cx + s, cy + 3, icon);
                    break;
                case Button.ViewTop:   // wide flat slab = deck plan
                    Fill(batch, cx - s * 1.2f, cy - s * 0.35f, cx + s * 1.2f, cy + s * 0.35f, icon);
                    break;
                case Button.ViewSide:  // tall slab
                    Fill(batch, cx - s * 0.35f, cy - s * 1.2f, cx + s * 0.35f, cy + s * 1.2f, icon);
                    break;
                case Button.ViewFront: // square
                    Fill(batch, cx - s * 0.8f, cy - s * 0.8f, cx + s * 0.8f, cy + s * 0.8f, icon);
                    break;
                case Button.Mode:      // stacked layers; count shows active mode
                    int bars = st.Mode + 1;
                    for (int b = 0; b < 3; b++)
                    {
                        var c = b < bars ? accent : icon;
                        float by = cy - s + b * (s * 0.9f);
                        Fill(batch, cx - s, by, cx + s, by + s * 0.45f, c);
                    }
                    break;
                case Button.Calibrate: // crosshair
                    Fill(batch, cx - s * 1.1f, cy - 2, cx + s * 1.1f, cy + 2, icon);
                    Fill(batch, cx - 2, cy - s * 1.1f, cx + 2, cy + s * 1.1f, icon);
                    Border(batch, cx - s * 0.55f, cy - s * 0.55f, cx + s * 0.55f, cy + s * 0.55f, 2f, icon);
                    break;
                case Button.Refresh:   // broken ring + arrowhead
                    Border(batch, cx - s * 0.9f, cy - s * 0.9f, cx + s * 0.9f, cy + s * 0.9f, 4f, icon);
                    Fill(batch, cx, cy - s * 1.05f, cx + s * 1.1f, cy - s * 0.55f, bg);   // gap in the ring
                    Fill(batch, cx + s * 0.5f, cy - s * 1.15f, cx + s * 1.05f, cy - s * 0.45f, accent); // arrowhead block
                    break;
            }
        }
    }

    private static void Fill(IDrawBatch batch, float x0, float y0, float x1, float y1, ColorSRGB color)
    {
        Span<QuadraticBezier2> rect = stackalloc QuadraticBezier2[4];
        rect[0] = new QuadraticBezier2(new Vector2(x0, y0), new Vector2(x1, y0));
        rect[1] = new QuadraticBezier2(new Vector2(x1, y0), new Vector2(x1, y1));
        rect[2] = new QuadraticBezier2(new Vector2(x1, y1), new Vector2(x0, y1));
        rect[3] = new QuadraticBezier2(new Vector2(x0, y1), new Vector2(x0, y0));
        batch.DrawFill(rect, color, null, false);
    }

    private static void Border(IDrawBatch batch, float x0, float y0, float x1, float y1, float t, ColorSRGB c)
    {
        Fill(batch, x0, y0, x1, y0 + t, c);
        Fill(batch, x0, y1 - t, x1, y1, c);
        Fill(batch, x0, y0, x0 + t, y1, c);
        Fill(batch, x1 - t, y0, x1, y1, c);
    }
}
