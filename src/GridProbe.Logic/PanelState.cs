namespace GridProbe;

// Per-ship-panel UI state (view angle, depth interpretation, zoom).
internal sealed class PanelState
{
    public const int ViewTop = 0, ViewFront = 1, ViewSide = 2;
    public const int ModeThickness = 0, ModeComplexity = 1, ModeVoids = 2;

    public volatile int ViewAxis = ViewTop;
    public volatile int Mode = ModeThickness;
    public float Zoom = 1f;                 // 1 = fit
    public float PanX = -1f, PanY = -1f;    // window center in cell coords; <0 = image center
    public int Version;                     // bump on any change -> busts the image cache

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, PanelState> _states = new();
    public static PanelState Get(int key) => _states.GetOrAdd(key, _ => new PanelState());

    public static string ViewName(int v) => v switch { ViewTop => "top", ViewFront => "front", ViewSide => "side", _ => "?" };
    public static string ModeName(int m) => m switch { ModeThickness => "thickness", ModeComplexity => "complexity", ModeVoids => "voids", _ => "?" };

    // Depth axis collapsed by each UI view (grid is Y-up): top view collapses Y, front collapses Z, side collapses X.
    public static int DepthAxisOf(int viewAxis) => viewAxis switch { ViewTop => 1, ViewFront => 2, _ => 0 };

    public const float HeaderStrip = 32f;

    // Single source of truth for the view window. The viewport is ALWAYS the
    // full display below the header; the window over the cell data carries the
    // PANEL's aspect ratio (ship-aspect letterboxing lives in cell space), so
    // zoomed content always fills the whole screen — no baked-in borders.
    public static (double Wx0, double Wy0, double WinW, double WinH, float Vx0, float Vy0, float VpW, float VpH)
        GetWindow(PanelState st, int vw, int vh, float W, float H)
    {
        float vpW = W, vpH = H - HeaderStrip, vx0 = 0f, vy0 = HeaderStrip;
        double aspect = vpW / (double)vpH;
        double winH0 = Math.Max(vh, vw / aspect);   // fit: whole ship visible at panel aspect
        double winH = winH0 / st.Zoom, winW = winH * aspect;
        double cx = st.PanX >= 0 ? st.PanX : vw / 2.0;
        double cy = st.PanY >= 0 ? st.PanY : vh / 2.0;
        cx = winW >= vw ? vw / 2.0 : Math.Clamp(cx, winW / 2, vw - winW / 2);
        cy = winH >= vh ? vh / 2.0 : Math.Clamp(cy, winH / 2, vh - winH / 2);
        return (cx - winW / 2, cy - winH / 2, winW, winH, vx0, vy0, vpW, vpH);
    }
}
