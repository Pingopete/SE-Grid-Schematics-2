using Keen.VRage.Library.Mathematics;
using Keen.VRage.Render.Contracts;

namespace GridProbe;

// Edge button bar: zoom, view angle (top/side/front), depth mode cycle.
// Layout is shared by the renderer (draw) and the aim system (click hit-test).
internal static class PanelUi
{
    public enum Button { None, ZoomIn, ZoomOut, ViewTop, ViewSide, ViewFront, Mode, Calibrate, Refresh }

    public struct Rect { public Button Id; public float X0, Y0, X1, Y1; }

    public static Rect[] Layout(float W, float H)
    {
        const float size = 56f, gap = 10f, right = 14f;
        float x0 = W - right - size, x1 = W - right;
        float y = 44f;
        var order = new[] { Button.ZoomIn, Button.ZoomOut, Button.ViewTop, Button.ViewSide, Button.ViewFront, Button.Mode, Button.Calibrate, Button.Refresh };
        var rects = new Rect[order.Length];
        for (int i = 0; i < order.Length; i++)
        {
            float extra = (order[i] == Button.ViewTop || order[i] == Button.Mode || order[i] == Button.Calibrate) ? 18f : 0f; // group separators
            y += extra;
            rects[i] = new Rect { Id = order[i], X0 = x0, Y0 = y, X1 = x1, Y1 = y + size };
            y += size + gap;
        }
        return rects;
    }

    public static Button HitTest(float W, float H, float px, float py)
    {
        foreach (var r in Layout(W, H))
            if (px >= r.X0 && px <= r.X1 && py >= r.Y0 && py <= r.Y1)
                return r.Id;
        return Button.None;
    }

    public static void Draw(IDrawBatch batch, float W, float H, PanelState st, Button hover)
    {
        var bg = new ColorSRGB((byte)16, (byte)22, (byte)28, (byte)185);
        var bgHover = new ColorSRGB((byte)45, (byte)60, (byte)75, (byte)220);
        var icon = new ColorSRGB((byte)215, (byte)225, (byte)235, (byte)255);
        var accent = new ColorSRGB((byte)90, (byte)190, (byte)255, (byte)255);

        foreach (var r in Layout(W, H))
        {
            bool active = (r.Id == Button.ViewTop && st.ViewAxis == PanelState.ViewTop)
                       || (r.Id == Button.ViewSide && st.ViewAxis == PanelState.ViewSide)
                       || (r.Id == Button.ViewFront && st.ViewAxis == PanelState.ViewFront);
            Fill(batch, r.X0, r.Y0, r.X1, r.Y1, r.Id == hover ? bgHover : bg);
            if (active) Border(batch, r.X0, r.Y0, r.X1, r.Y1, 3f, accent);

            float cx = (r.X0 + r.X1) / 2f, cy = (r.Y0 + r.Y1) / 2f;
            float s = (r.X1 - r.X0) * 0.30f;
            switch (r.Id)
            {
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
