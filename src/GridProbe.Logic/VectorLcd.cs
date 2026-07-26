using Keen.Game2.Client.WorldObjects.CubeBlocks.Render.Lcd;
using Keen.VRage.Library.Filesystem;
using Keen.VRage.Library.Mathematics;
using Keen.VRage.Library.Utils;
using Keen.VRage.Render.Contracts;

namespace GridProbe;

internal static class VectorLcd
{
    public static volatile OccupancyScan CurrentScan;
    public static readonly System.Collections.Concurrent.ConcurrentDictionary<int, OccupancyScan> Scans = new();
    public static readonly System.Collections.Concurrent.ConcurrentDictionary<int, (int W, int H)> PanelRes = new();
    public static readonly System.Collections.Concurrent.ConcurrentDictionary<int, bool> PendingRealloc = new();
    private static int _errorCount;
    private static bool _loggedFirstDraw;
    private static bool _reconDone;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, bool> _loggedGeometry = new();
    private static int _drawLogCount;
    private static int _ctxLogs;
    private static int _reallocLogs;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, bool> _rtResets = new();
    public static readonly System.Collections.Concurrent.ConcurrentDictionary<int, bool> NeedsContentCycle = new();
    public static volatile bool UseImageBlit = true;
    // Peak value handed to the panel, set to the panel's WHITE POINT.
    //
    // The final panel value is BlitBrightness * alpha / 255, and the panel
    // saturates around 100/255 — past that, every value is the same white. So
    // this is not "how bright to draw": it is the top of the range the panel
    // can actually distinguish, and alpha's 255 steps get spread across it.
    //
    // Set it too high and alpha's steps pile up past saturation where they all
    // look the same, which is what 255 did. Set it too low and the top of the
    // range never reaches white at all, which is what 100 did — the corrected
    // ramp topped out at a light grey. The white point is the one value that
    // saturates and wastes nothing, and reading it off the ramp's fine
    // shoulder row puts it near 190.
    public static volatile int BlitBrightness = 190;
    // Measurement, not decoration. Nothing in this mod has ever checked that the
    // alpha we hand the panel is the brightness that comes back off the glass —
    // the whole tone pipeline assumes it. This draws two known ramps so the
    // panel's real response can be read straight off a screenshot.
    public static volatile bool ToneRamp = true;
    private const int SuperSample = 2; // image rendered at Nx panel pixels; GPU downsamples on the glass
    private const long ImgVersion = 7; // bump when the resampler changes to bust the image cache
    public static volatile bool ShowCursor = true;
    private static ResourceHandle _cursorHandle;
    private static int _cursorState; // 0=untried, 1=resolved, -1=unavailable (vector fallback)
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<object, (long Touch, int Key)> _cursorCtxs = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<object, long> _idleRebuilt = new();
    // Event-driven repaint policy: forced rebuilds clear+rerecord the panel RT,
    // and each one risks a dropped frame (visible flash). Repaint only when
    // something actually changed.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, (float U, float V, bool Aimed, int Ver)> _lastRepaint = new();
    public static readonly System.Collections.Concurrent.ConcurrentDictionary<int, bool> RepaintRequest = new();

    private static bool NeedsRepaint(int key, long now)
    {
        if (RepaintRequest.TryRemove(key, out _)) return true;
        if (Calibration.ActiveKey == key) return true; // targets animate
        // Warm an incoming texture — but ONLY while it is actually warming. A
        // pending entry that never promotes (e.g. the band renderer took over
        // the panel, so StepEntry stopped running) would otherwise force a
        // full re-record every frame forever.
        if (_imgBase.TryGetValue(key, out var eb) && eb.PendingWant != 0
            && now - eb.PendingAt < 4000) return true;
        bool aimed = CursorAim.Aim.TryGetValue(key, out var uv);
        int ver = System.Threading.Volatile.Read(ref PanelState.Get(key).Version);
        var prev = _lastRepaint.TryGetValue(key, out var p) ? p : default;
        bool need = aimed != prev.Aimed || ver != prev.Ver
                 || (aimed && (Math.Abs(uv.U - prev.U) > 0.0015f || Math.Abs(uv.V - prev.V) > 0.0015f));
        if (need) _lastRepaint[key] = (aimed ? uv.U : 0f, aimed ? uv.V : 0f, aimed, ver);
        return need;
    }
    private static System.Reflection.MethodInfo _rebuildMi;
    private static System.Reflection.FieldInfo _ctxCollectionField;
    private static bool _ctxFieldIsKvp;
    private static bool _tickHookLogged;
    private static long _tickCount, _rebuildCount, _lastTickLog, _lastPin;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<object, bool> _tickComps = new();
    private static readonly string _imgSalt = DateTime.UtcNow.Ticks.ToString("x"); // unique names per logic reload; engine caches textures by handle
    // Double-buffered async image pipeline: the render thread only ever draws
    // the last ready texture (remapped to the current window); builds happen on
    // the thread pool and swap in after a warmup so the GPU has streamed them.
    private sealed class ImgEntry
    {
        public long Want;
        public ResourceHandle Handle;
        public double X0, Y0, X1, Y1;   // source window the texture was rendered from (cells)
        public int PxW, PxH;            // texture pixel size
        public int Gen;
        public long PendingWant;
        public ResourceHandle PendingHandle;
        public double PX0, PY0, PX1, PY1;
        public int PPxW, PPxH;
        public long PendingAt;
        public bool Building;
        public string Abs, PAbs;
        public readonly List<(ResourceHandle Handle, string Abs, long At)> Retired = new();
    }
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, ImgEntry> _img = new();     // zoom/pan detail layer
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, ImgEntry> _imgBase = new(); // full-view base layer
    private const long ImgWarmupMs = 450;
    private const long WantPrime = unchecked((long)0x9E3779B97F4A7C15);
    private static long _lastImgDiag;
    private static bool _imgSigLogged;
    private static int _imgErrLogs;
    private static int _imgGenLogs;
    private static System.Reflection.ConstructorInfo _fhCtor;

    private static bool _apiDumped;

    // One-shot: what can the canvas actually fill a path WITH? Flat colour only
    // means smooth shading needs many bands (a contour map); a gradient or
    // image/texture fill means ONE sharp silhouette path can carry smooth
    // shading in a single draw call.
    private static void DumpApi()
    {
        if (_apiDumped) return;
        _apiDumped = true;
        try
        {
            var t = typeof(IDrawBatch);
            var seen = new HashSet<Type>();
            foreach (var m in t.GetMethods())
            {
                var ps = m.GetParameters();
                ProbeLog.Line($"API IDrawBatch.{m.Name}({string.Join(", ", ps.Select(p => p.ParameterType.Name + " " + p.Name))}) -> {m.ReturnType.Name}");
                foreach (var p in ps)
                {
                    var pt = Nullable.GetUnderlyingType(p.ParameterType) ?? p.ParameterType;
                    if (pt.IsPrimitive || pt == typeof(string) || !seen.Add(pt)) continue;
                    if (pt.Namespace != null && pt.Namespace.StartsWith("System")) continue;
                    ProbeLog.Line($"API   type {pt.FullName}");
                    foreach (var mm in pt.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                        ProbeLog.Line($"API     .{mm.Name}({string.Join(", ", mm.GetParameters().Select(q => q.ParameterType.Name + " " + q.Name))})");
                    foreach (var f in pt.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                        ProbeLog.Line($"API     field {f.FieldType.Name} {f.Name}");
                }
            }
        }
        catch (Exception e) { ProbeLog.Error("api dump", e); }
    }

    public static void OnRender(object batchObj, object ctxObj)
    {
        if (batchObj is not IDrawBatch batch || ctxObj is not LcdPanelSurfaceContext ctx) return;
        try
        {
            if (!_reconDone) { _reconDone = true; ReconDump(ctx); }
            var text = ctx.State.Text;
            if (string.IsNullOrEmpty(text)) return;
            int tagIdx = text.IndexOf(LcdProbe.Tag, StringComparison.OrdinalIgnoreCase);
            if (tagIdx < 0) return;

            OccupancyScan scan = null;
            int shipKey = 0;
            int hash = text.IndexOf('#', tagIdx);
            if (hash >= 0)
            {
                int end = hash + 1;
                while (end < text.Length && char.IsDigit(text[end])) end++;
                if (end > hash + 1 && int.TryParse(text.AsSpan(hash + 1, end - hash - 1), out var key))
                {
                    shipKey = key;
                    Scans.TryGetValue(key, out scan);
                    if (ctx.Definition.Resolution.X < 1024 && ctx.Definition.Resolution.Y < 1024)
                    {
                        object defBox = ctx.Definition;
                        if (ResolutionBooster.BoostSurfaceObject(defBox))
                        {
                            try
                            {
                                var bf = ctx.GetType().GetField("<Definition>k__BackingField",
                                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                if (bf != null)
                                {
                                    bf.SetValue(ctx, defBox);
                                    ctx.ReleaseScreenMaterialHandle();
                                    ctx.RenderTarget = null;
                                    ctx.ContentDirty = true;
                                    if (_reallocLogs++ < 4)
                                        ProbeLog.Line($"Boxed-def writeback + release on ctx#{ctx.GetHashCode():X8}, now {ctx.Definition.Resolution.X}x{ctx.Definition.Resolution.Y}.");
                                }
                                else if (_reallocLogs++ < 4) ProbeLog.Line("Definition backing field not found.");
                            }
                            catch (Exception e) { if (_reallocLogs++ < 4) ProbeLog.Line("Writeback failed: " + e.Message); }
                        }
                    }
                    if (ctx.Definition.Resolution.X >= 1024 && _rtResets.TryAdd(ctx.GetHashCode(), true))
                    {
                        try { ctx.ReleaseScreenMaterialHandle(); ctx.RenderTarget = null; ctx.ContentDirty = true; NeedsContentCycle[key] = true; ProbeLog.Line($"RT reset on ctx#{ctx.GetHashCode():X8}"); } catch { }
                    }
                    var r = ctx.Definition.Resolution;
                    if (PanelRes.TryGetValue(key, out var prev) && (prev.W != r.X || prev.H != r.Y))
                        ProbeLog.Line($"Panel {key} resolution changed: {prev.W}x{prev.H} -> {r.X}x{r.Y}");
                    PanelRes[key] = (r.X, r.Y);
                    ResolutionBooster.BoostSurfaceObject(ctx.Definition);
                }
            }
            // Never fall back to another ship's scan: a stale key shows nothing
            // for a pass rather than someone else's grid.
            if (scan == null) return;
            if (_ctxLogs < 10)
            {
                _ctxLogs++;
                ProbeLog.Line($"ctx#{ctx.GetHashCode():X8} surf {ctx.SurfaceIndex} defRes {ctx.Definition.Resolution.X}x{ctx.Definition.Resolution.Y} rt={(ctx.RenderTarget.HasValue ? "yes" : "no")}");
            }
            Draw(batch, ctx, scan, shipKey);
        }
        catch (Exception e)
        {
            if (_errorCount++ < 3) ProbeLog.Error("vector draw", e);
        }
    }

    private static void ReconDump(LcdPanelSurfaceContext ctx)
    {
        try
        {
            var sb = new System.Text.StringBuilder("SurfaceContext recon:\n");
            foreach (var f in ctx.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
            {
                object v = null;
                try { v = f.GetValue(ctx); } catch { }
                sb.AppendLine($"  field {f.FieldType.Name} {f.Name} = {(v == null ? "null" : v.GetType().Name)}");
                if (f.Name == "RenderCache" && v != null)
                {
                    foreach (var cf in v.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
                    {
                        object cv = null;
                        try { cv = cf.GetValue(v); } catch { }
                        var preview = cv is string s ? $"\"{(s.Length > 60 ? s[..60] : s)}\"" : cv?.GetType().Name ?? "null";
                        sb.AppendLine($"    cache field {cf.FieldType.Name} {cf.Name} = {preview}");
                    }
                }
            }
            ProbeLog.Line(sb.ToString());
        }
        catch (Exception e) { ProbeLog.Error("recon dump", e); }
    }

    private static void Draw(IDrawBatch batch, LcdPanelSurfaceContext ctx, OccupancyScan scan, int shipKey)
    {
        var res = ctx.Definition.Resolution;
        float W = res.X, H = res.Y;

        var st = PanelState.Get(shipKey);
        int depthAxis = PanelState.DepthAxisOf(st.ViewAxis);
        var view = st.ViewAxis switch
        {
            PanelState.ViewFront => scan.Top,   // Top array = collapse Z = front elevation (grid is Y-up)
            PanelState.ViewSide => scan.Side,
            _ => scan.Front,                    // Front array = collapse Y = top-down deck plan
        };
        // Mode selection happens in the tone field; this array only supplies the
        // view dimensions and the last-ditch fallback fill.
        int vw = view.GetLength(0), vh = view.GetLength(1);
        int maxV = 1;
        for (int x = 0; x < vw; x++) for (int y = 0; y < vh; y++) if (view[x, y] > maxV) maxV = view[x, y];

        const float headerStrip = 32f, margin = 10f;

        float scale = Math.Min((W - 2 * margin) / vw, (H - headerStrip - 2 * margin) / vh);
        float ox = (W - vw * scale) / 2f, oy = headerStrip + (H - headerStrip - vh * scale) / 2f;
        float overlap = scale * 0.35f;

        if (_drawLogCount < 2)
        {
            _drawLogCount++;
            string rtInfo = "null";
            try
            {
                var rtField = ctx.GetType().GetField("RenderTarget");
                var rtVal = rtField?.GetValue(ctx);
                if (rtVal != null)
                {
                    var sb = new System.Text.StringBuilder(rtVal.GetType().Name);
                    foreach (var p in rtVal.GetType().GetProperties())
                    { try { sb.Append($" {p.Name}={p.GetValue(rtVal)}"); } catch { } }
                    rtInfo = sb.ToString();
                }
            }
            catch { }
            ProbeLog.Line($"Draw[{_drawLogCount}]: surf {ctx.SurfaceIndex}, defRes {res.X}x{res.Y}, mesh '{ctx.Definition.MeshPartName}', view {vw}x{vh}, scale {scale:F2}, origin ({ox:F0},{oy:F0}), RT: {rtInfo}");
        }


        if (UseImageBlit)
        {
            try
            {
                if (!_imgSigLogged)
                {
                    _imgSigLogged = true;
                    var mi = typeof(IDrawBatch).GetMethod("DrawImage");
                    if (mi != null)
                        ProbeLog.Line("DrawImage sig: " + string.Join(", ", mi.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}")));
                }
                EnsureViewHash(scan, view);
                var win = PanelState.GetWindow(st, vw, vh, W, H);
                double cellPx = win.VpW / win.WinW; // on-screen pixels per data cell

                // Mode-mapped display tones (thickness fallback until channels land).
                var tones = ToneFields.Get(scan, st.ViewAxis, st.Mode)
                         ?? ToneFields.Get(scan, st.ViewAxis, PanelState.ModeThickness);
                bool tonesAreFallback = st.Mode != PanelState.ModeThickness && scan.ChannelAxis != depthAxis;

                bool drewContent = false;
                bool hasPendBase = false;
                bool bandsDrawn = false;
                // Single unified renderer: cached iso-band vector geometry drawn
                // at EVERY zoom through a pure transform — the picture never
                // changes character with zoom. The texture below is only a
                // warmup fallback while a band set builds on a worker thread.
                var bands = scan.GetBands(st.ViewAxis, st.Mode);
                if (bands == null)
                {
                    scan.RequestBands(st.ViewAxis, st.Mode, shipKey);
                    bands = scan.GetBands(st.ViewAxis, PanelState.ModeThickness); // interim geometry beats a blackout
                }
                if (bands != null && bands.Bands.Count > 0)
                {
                    DrawBands(batch, bands, win);
                    bandsDrawn = true;
                    drewContent = true;

                    // System overlay on top of the hull, in the same recovered
                    // geometry so highlighted blocks keep their real shape.
                    if (st.Highlight != PanelState.HighlightNone)
                    {
                        var hl = scan.GetHighlight(st.ViewAxis, st.Highlight);
                        if (hl == null) scan.RequestHighlight(st.ViewAxis, st.Highlight, shipKey);
                        else if (hl.Bands.Count > 0) DrawBands(batch, hl, win, PanelUi.Lime);
                    }
                    // Bands own this panel now: shut the texture pipeline down so
                    // it stops pinning and stops forcing per-frame repaints.
                    if (_imgBase.TryRemove(shipKey, out _))
                        ProbeLog.Line($"Texture fallback retired for k{shipKey} (bands active).");
                }
                if (!bandsDrawn && tones != null)
                {
                    // Overview regime: one base texture per ship state (built on
                    // scan cadence only — never rebuilt by zoom/pan/clicks).
                    int destW = Math.Max(1, (int)Math.Round(vw * scale));
                    int destH = Math.Max(1, (int)Math.Round(vh * scale));
                    // 2x beyond supersampling for zoom sharpness, but capped:
                    // oversized textures stream slowly and cause visible gaps.
                    int pxW = destW * SuperSample * 2, pxH = destH * SuperSample * 2;
                    int longSide = Math.Max(pxW, pxH);
                    if (longSide > MaxTexLong)
                    {
                        double f = (double)MaxTexLong / longSide;
                        pxW = Math.Max(64, (int)(pxW * f));
                        pxH = Math.Max(64, (int)(pxH * f));
                    }
                    long baseSalt = ((long)st.ViewAxis << 1) ^ ((long)st.Mode << 4)
                                  ^ ((tonesAreFallback ? 0L : 1L) << 7);
                    long baseWant = scan.ViewHash ^ ((long)pxW << 40) ^ ((long)pxH << 20) ^ ImgVersion ^ (baseSalt * WantPrime);
                    var eBase = _imgBase.GetOrAdd(shipKey, _ => new ImgEntry());
                    StepEntry(eBase, shipKey, baseWant, tones, pxW, pxH, 0, 0, vw, vh,
                        out var imgBase, out var winBase, out bool anyBase, out var pendBase, out hasPendBase);
                    if (hasPendBase && !TryPreload(pendBase))
                        batch.DrawImage(pendBase, new BoundingBox2(new Vector2(0f, H - 2f), new Vector2(2f, H)),
                            new ColorSRGB((byte)255, (byte)255, (byte)255, (byte)255), false, null, null);
                    if (anyBase)
                    {
                        byte bb = (byte)BlitBrightness;
                        DrawRemapped(batch, imgBase, winBase, win.Wx0, win.Wy0, win.WinW, win.WinH,
                            win.Vx0, win.Vy0, (int)win.VpW, (int)win.VpH, new ColorSRGB(bb, bb, bb, (byte)255));
                        drewContent = true;
                    }
                }

                if (ToneRamp) DrawToneRamp(batch, W, H);

                // Disappearance forensics: 1/s state line for the drawn panel.
                long nowDiag = Environment.TickCount64;
                if (nowDiag - _lastDrawDiag > 1000)
                {
                    double winSec = Math.Max(0.001, (nowDiag - _lastDrawDiag) / 1000.0);
                    _lastDrawDiag = nowDiag;
                    _imgBase.TryGetValue(shipKey, out var eDbg);
                    long pendAge = eDbg != null && eDbg.PendingWant != 0 ? nowDiag - eDbg.PendingAt : -1;
                    double recMs = _bandRecords > 0
                        ? _bandTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / _bandRecords : 0;
                    double recPerSec = _bandRecords / winSec;
                    double cpuPct = recMs * recPerSec / 10.0; // ms/s -> % of a core
                    ProbeLog.Line($"BandCost k{shipKey}: {recPerSec:F0} records/s x {recMs:F2} ms = {cpuPct:F1}% core | {_lastBandSegs} segs, {_bandFillCalls} fills, lod{_lastBandLod}");
                    _bandTicks = 0; _bandRecords = 0;
                    ProbeLog.Line($"DrawDiag k{shipKey} ctx#{ctx.GetHashCode():X8} s{ctx.SurfaceIndex} mode={st.Mode}{(tonesAreFallback ? "(fb)" : "")} z={st.Zoom:F1} cellPx={cellPx:F1} bands={bandsDrawn}({(bandsDrawn ? bands.Bands.Count : 0)} lod{_lastBandLod} drawn{_lastBandSegs}) drew={drewContent} aim={CursorAim.Aim.ContainsKey(shipKey)} pend={hasPendBase} pendAge={pendAge} bld={eDbg?.Building} gen={eDbg?.Gen} rt={(ctx.RenderTarget.HasValue ? "y" : "n")}");
                }

                if (drewContent)
                {
                    PanelUi.Draw(batch, W, H, st, HoverButton(shipKey, W, H));
                    if (Calibration.ActiveKey == shipKey)
                    {
                        _cursorCtxs[ctx] = (Environment.TickCount64, shipKey); // keep 60fps repaint during calibration
                        bool phase2 = Calibration.Step >= 3;
                        for (int i = 0; i < Calibration.Targets.Length; i++)
                        {
                            bool current = i == Calibration.Step % 3;
                            var c = !current
                                ? new ColorSRGB((byte)110, (byte)115, (byte)120, (byte)160)
                                : phase2
                                    ? new ColorSRGB((byte)255, (byte)170, (byte)60, (byte)255)  // pass 2: orange = new standpoint
                                    : new ColorSRGB((byte)90, (byte)190, (byte)255, (byte)255); // pass 1: blue
                            float tx = Calibration.Targets[i].X * W, ty = Calibration.Targets[i].Y * H;
                            float r = current ? 30f : 14f;
                            FillRect(batch, tx - r, ty - 2, tx + r, ty + 2, c);
                            FillRect(batch, tx - 2, ty - r, tx + 2, ty + r, c);
                        }
                    }
                    else DrawCursorOverlay(batch, ctx, shipKey, W, H);
                    return;
                }
            }
            catch (Exception e) { if (_imgErrLogs++ < 3) ProbeLog.Error("image blit", e); }
        }

        // Last-ditch net: only reached in the frame or two after a load, before
        // any band geometry exists. Strided so a large ship can't dump tens of
        // thousands of rects into a single frame.
        const int maxRectsPerCall = 600;
        int stride = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((double)vw * vh / 8000.0)));
        var buckets = new List<(float x0, float y0, float x1, float y1)>[256];
        int totalRects = 0;
        for (int z = 0; z < vh; z += stride)
        {
            int x = 0;
            while (x < vw)
            {
                int gv = GrayOf(view[x, z], maxV);
                if (gv <= 0) { x += stride; continue; }
                int runStart = x;
                while (x < vw && GrayOf(view[x, z], maxV) == gv) x += stride;
                (buckets[gv] ??= new()).Add((
                    ox + runStart * scale, oy + z * scale,
                    ox + x * scale + overlap, oy + (z + stride) * scale + overlap));
                totalRects++;
            }
        }

        int drawCalls = 0;
        for (int gv = 1; gv < 256; gv++)
        {
            var rects = buckets[gv];
            if (rects == null) continue;
            byte g = (byte)gv;
            var color = new ColorSRGB(g, g, g, (byte)255);
            for (int start = 0; start < rects.Count; start += maxRectsPerCall)
            {
                int count = Math.Min(maxRectsPerCall, rects.Count - start);
                var splines = new QuadraticBezier2[count * 4];
                for (int r = 0; r < count; r++)
                {
                    var (x0, y0, x1, y1) = rects[start + r];
                    splines[r * 4 + 0] = new QuadraticBezier2(new Vector2(x0, y0), new Vector2(x1, y0));
                    splines[r * 4 + 1] = new QuadraticBezier2(new Vector2(x1, y0), new Vector2(x1, y1));
                    splines[r * 4 + 2] = new QuadraticBezier2(new Vector2(x1, y1), new Vector2(x0, y1));
                    splines[r * 4 + 3] = new QuadraticBezier2(new Vector2(x0, y1), new Vector2(x0, y0));
                }
                batch.DrawFill(splines, color, null, false);
                drawCalls++;
            }
        }
        if (_drawLogCount < 2)
            ProbeLog.Line($"Draw stats: {totalRects} rects -> {drawCalls} fill calls (+bg).");

        if (!_loggedFirstDraw)
        {
            _loggedFirstDraw = true;
            ProbeLog.Line($"Vector draw active: surface {ctx.SurfaceIndex}, RT {res.X}x{res.Y}, view {vw}x{vh}, scale {scale:F2}.");
        }
        DrawCursorOverlay(batch, ctx, shipKey, W, H);
    }

    // Cursor at the camera aim point (CursorAim.Update runs per frame from the tick hook).
    private static void DrawCursorOverlay(IDrawBatch batch, LcdPanelSurfaceContext ctx, int shipKey, float W, float H)
    {
        if (!ShowCursor) return;
        try
        {
            if (!_cursorCtxs.ContainsKey(ctx))
                ProbeLog.Line($"ctx tracked: k{shipKey} ctx#{ctx.GetHashCode():X8} s{ctx.SurfaceIndex} (new or re-created)");
            _cursorCtxs[ctx] = (Environment.TickCount64, shipKey);
            if (!CursorAim.Aim.TryGetValue(shipKey, out var uv)) return;
            float cx = uv.U * W;
            float cy = uv.V * H;
            const float size = 24f;
            if (TryResolveCursor())
            {
                var dest = new BoundingBox2(new Vector2(cx, cy), new Vector2(cx + size, cy + size));
                batch.DrawImage(_cursorHandle, dest, new ColorSRGB((byte)255, (byte)255, (byte)255, (byte)255), false, null, null);
            }
            else
            {
                var white = new ColorSRGB((byte)235, (byte)235, (byte)235, (byte)255);
                FillRect(batch, cx - 14, cy - 2, cx + 14, cy + 2, white);
                FillRect(batch, cx - 2, cy - 14, cx + 2, cy + 14, white);
            }
        }
        catch (Exception e)
        {
            if (_errorCount++ < 3) ProbeLog.Error("cursor overlay", e);
        }
    }

    // Called every frame per LCD render component (bootstrap postfix on TickFsrMask).
    // Re-invokes the game's own RebuildSurfaceContent for panels with a live cursor,
    // which is the engine's real repaint path (ContentDirty gets cleared right after
    // Render, so flagging it from inside the render postfix does nothing).
    public static void OnLcdTick(object comp)
    {
        try
        {
            _tickCount++;
            _tickComps.TryAdd(comp, true);
            if (!_tickHookLogged)
            {
                _tickHookLogged = true;
                ProbeLog.Line("LcdTick hook online (TickFsrMask postfix firing).");
            }
            CursorAim.Update();
            TryResolveUiSystem(comp);
            if (_cursorCtxs.IsEmpty) return;

            // Pin active textures every frame: the engine's streamer evicts
            // by distance/priority, and an evicted texture silently draws
            // nothing (confirmed live: recorded draws + blank panel).
            long pinNow = Environment.TickCount64;
            if (_uiSystem != null && pinNow - _lastPin > 100)
            {
                _lastPin = pinNow;
                foreach (var kv in _imgBase)
                {
                    var e = kv.Value;
                    if (e.Want != 0) TryPreload(e.Handle);
                    if (e.PendingWant != 0) TryPreload(e.PendingHandle);
                }
            }
            long now = Environment.TickCount64;
            foreach (var kv in _cursorCtxs)
                if (now - kv.Value.Touch > 10000) _cursorCtxs.TryRemove(kv.Key, out _);
            if (_cursorCtxs.IsEmpty) return;

            var t = comp.GetType();
            _rebuildMi ??= t.GetMethod("RebuildSurfaceContent",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (_rebuildMi == null) return;

            if (_ctxCollectionField == null)
            {
                foreach (var f in t.GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                {
                    object v = null;
                    try { v = f.GetValue(comp); } catch { }
                    if (v is not System.Collections.IEnumerable en || v is string) continue;
                    foreach (var item in en)
                    {
                        if (item is LcdPanelSurfaceContext) { _ctxCollectionField = f; _ctxFieldIsKvp = false; break; }
                        var vp = item?.GetType().GetProperty("Value");
                        if (vp?.GetValue(item) is LcdPanelSurfaceContext) { _ctxCollectionField = f; _ctxFieldIsKvp = true; break; }
                        break; // only inspect the first element of each collection
                    }
                    if (_ctxCollectionField != null)
                    {
                        ProbeLog.Line($"LcdTick: surface collection = {f.FieldType.Name} {f.Name} (kvp={_ctxFieldIsKvp}).");
                        break;
                    }
                }
                if (_ctxCollectionField == null) return; // this component holds no contexts yet; try again next tick
            }

            object col = null;
            try { col = _ctxCollectionField.GetValue(comp); } catch { }
            if (col is not System.Collections.IEnumerable list) return;
            // If ANY context of this component is tracked, decide ONCE whether a
            // repaint is warranted (event-driven), then rebuild ALL its contexts
            // so re-created ones are covered too.
            int trackedKey = 0;
            bool anyTracked = false;
            foreach (var item in list)
            {
                var c = _ctxFieldIsKvp ? item?.GetType().GetProperty("Value")?.GetValue(item) as LcdPanelSurfaceContext : item as LcdPanelSurfaceContext;
                if (c != null && _cursorCtxs.TryGetValue(c, out var info))
                {
                    anyTracked = true;
                    trackedKey = info.Key;
                    break;
                }
            }
            if (!anyTracked || !NeedsRepaint(trackedKey, now)) return;
            foreach (var item in list)
            {
                var c = _ctxFieldIsKvp ? item?.GetType().GetProperty("Value")?.GetValue(item) as LcdPanelSurfaceContext : item as LcdPanelSurfaceContext;
                if (c == null) continue;
                _rebuildMi.Invoke(comp, new object[] { c });
                _rebuildCount++;
            }

            if (now - _lastTickLog > 10000)
            {
                // FPS proxy: this hook fires once per LCD render component per
                // frame, so frames = ticks / distinct components.
                int comps = _tickComps.Count;
                double secs = (now - _lastTickLog) / 1000.0;
                double fps = comps > 0 ? _tickCount / (double)comps / secs : 0;
                _lastTickLog = now;
                ProbeLog.Line($"LcdTick: {_tickCount} ticks over {comps} comps = ~{fps:F0} fps | {_rebuildCount} rebuilds in {secs:F0}s.");
                _tickCount = 0; _rebuildCount = 0; _tickComps.Clear();
            }
        }
        catch (Exception e)
        {
            if (_errorCount++ < 3) ProbeLog.Error("lcd tick", e);
        }
    }

    private static bool TryResolveCursor()
    {
        if (_cursorState != 0) return _cursorState == 1;
        try
        {
            var guid = new Guid("14238111-29c6-4cb6-be0d-ba78f6b8ce24"); // vanilla arrow cursor icon (AvaloniaCursorDefinition_Arrow)
            var h = new ResourceHandle(guid);
            var cc = Singleton<FileSystem>.Instance?.ContentCache;
            if (cc != null && cc.TryTranslateResourceHandle(h, out var fh))
            {
                string ext = (fh.GetExtension() ?? "").ToLowerInvariant();
                ProbeLog.Line($"Cursor icon resolved: '{fh}' ext '{ext}'.");
                if (ext is ".png" or ".dds" or ".jpg" or ".slug")
                {
                    _cursorHandle = h;
                    _cursorState = 1;
                    return true;
                }
                ProbeLog.Line("Cursor icon extension unsupported by DrawImage — using vector fallback.");
            }
            else ProbeLog.Line("Cursor icon GUID not in content cache — using vector fallback.");
        }
        catch (Exception e) { ProbeLog.Error("cursor resolve", e); }
        _cursorState = -1;
        return false;
    }

    private static PanelUi.Button HoverButton(int key, float W, float H)
    {
        if (!CursorAim.Aim.TryGetValue(key, out var uv)) return PanelUi.Button.None;
        return PanelUi.HitTest(W, H, uv.U * W, uv.V * H);
    }

    private const int MaxTexLong = 1300;      // texture long-axis cap (streaming weight)
    private static long _lastDrawDiag;
    private static QuadraticBezier2[] _splineBuf;
    private static readonly object _vecLock = new();

    // Per-frame geometry ceiling, set from what the smoothest mode costs while
    // holding 60 fps. Keeps every mode in the same performance envelope.
    private const int SegmentBudget = 40000;
    private static int _lastBandLod, _lastBandSegs;
    // Record-cost telemetry: is the per-frame cost OUR CPU work (transform +
    // submit) or the GPU/engine? Averaged over each diag window.
    private static long _bandTicks;
    private static int _bandRecords, _bandFillCalls;

    // The one renderer: cached iso-band polygons (cell space) transformed into
    // the window and filled back-to-front. The LOD whose cell-space error
    // stays under ~1/3 of an on-screen pixel is chosen per frame — identical
    // rule at every zoom, never more vertices than the screen can resolve.
    // Loops are culled by bounding box; each band is a single DrawFill whose
    // winding punches its holes.
    // Two 17-step ramps, left (0) to right (255), read straight off a screenshot.
    //
    //   ROW 0 — raw response over the full range, 0..255 in steps of 16.
    //   ROW 1 — raw response over the TOE, 0..16 in steps of 1. Reads off the
    //           exact value where the panel stops being black. Steps of 16 were
    //           far too coarse to see this, which is why the dark end kept
    //           being wrong.
    //   ROW 2 — raw response over the SHOULDER, 128..255 in steps of 8. Reads
    //           off the exact value where it reaches white, which is what
    //           BlitBrightness has to equal.
    //   ROW 3 — the corrected tone sweep: what the ship is actually drawn on.
    //           This is the one that should run evenly from black to white.
    private static void DrawToneRamp(IDrawBatch batch, float W, float H)
    {
        const int Steps = 17;
        const int Rows = 4;
        float rowH = H * 0.05f;
        float cellW = W / Steps;
        var quad = new QuadraticBezier2[4];

        void Rect(float x0, float y0, float x1, float y1, ColorSRGB col)
        {
            var a = new Vector2(x0, y0); var b = new Vector2(x1, y0);
            var c = new Vector2(x1, y1); var d = new Vector2(x0, y1);
            quad[0] = new QuadraticBezier2(a, b);
            quad[1] = new QuadraticBezier2(b, c);
            quad[2] = new QuadraticBezier2(c, d);
            quad[3] = new QuadraticBezier2(d, a);
            batch.DrawFill(new ReadOnlySpan<QuadraticBezier2>(quad, 0, 4), col, null, false);
        }

        // Black backing so each patch composites over a known floor, exactly
        // like the ship does over the empty panel.
        Rect(0f, 0f, W, rowH * Rows, new ColorSRGB((byte)0, (byte)0, (byte)0, (byte)255));

        byte bb = (byte)BlitBrightness;
        for (int i = 0; i < Steps; i++)
        {
            float x0 = i * cellW, x1 = x0 + cellW - 1f;
            void Raw(int row, int v)
            {
                byte c = (byte)Math.Clamp(v, 0, 255);
                Rect(x0, row * rowH, x1, (row + 1) * rowH, new ColorSRGB(c, c, c, (byte)255));
            }

            Raw(0, i * 16);            // full range
            Raw(1, i);                 // toe, one level per cell
            Raw(2, 128 + i * 8);       // shoulder

            // The corrected sweep, drawn exactly the way a band is drawn.
            int a = (int)Math.Round(255.0 * ToneBands.PanelCurve(Math.Min(255, i * 16) / 255.0));
            Rect(x0, 3 * rowH, x1, 4 * rowH, new ColorSRGB(bb, bb, bb, (byte)a));
        }
    }

    private static void DrawBands(IDrawBatch batch, ToneBands.BandSet bands,
        (double Wx0, double Wy0, double WinW, double WinH, float Vx0, float Vy0, float VpW, float VpH) win,
        ColorSRGB? tint = null)
    {
        var swRec = System.Diagnostics.Stopwatch.StartNew();
        lock (_vecLock)
        {
            double s = win.VpW / win.WinW;                  // on-screen pixels per cell
            double tolCells = 0.35 / Math.Max(1e-6, s);     // allowed cell-space error
            int lod = 0;                                    // coarsest tier still under the bound
            for (int t = ToneBands.LodTol.Length - 1; t >= 0; t--)
                if (tolCells >= ToneBands.LodTol[t]) { lod = t; break; }
            // The error bound is a quality target; this is a cost guarantee.
            // A richer tone field (complexity, voids) fragments every contour,
            // so the same on-screen accuracy can cost twice the geometry. Step
            // coarser rather than let one mode run away — the detail being
            // dropped is what a stricter error bound would have kept, which at
            // these zooms is below what the panel resolves anyway.
            while (lod < ToneBands.LodTol.Length - 1 && bands.TotalSegs[lod] > SegmentBudget) lod++;
            float wx0 = (float)(win.Wx0 - 1), wy0 = (float)(win.Wy0 - 1);
            float wx1 = (float)(win.Wx0 + win.WinW + 1), wy1 = (float)(win.Wy0 + win.WinH + 1);
            byte bb = (byte)BlitBrightness;
            int drawn = 0, calls = 0, n = 0;
            batch.ScissorPush(new BoundingBox2I(
                new Vector2I((int)win.Vx0, (int)win.Vy0),
                new Vector2I((int)(win.Vx0 + win.VpW), (int)(win.Vy0 + win.VpH))));

            // Append one loop's transformed segments; reverse flips its winding
            // so it punches a hole instead of adding area. Loops crossing the
            // window are CLIPPED to it first (Sutherland-Hodgman, cell space):
            // when zoomed in, a contour spanning the whole ship would otherwise
            // submit thousands of off-screen vertices every frame.
            int Emit(ToneBands.Loop loop, bool reverse)
            {
                if (loop.MaxX < wx0 || loop.MinX > wx1 || loop.MaxY < wy0 || loop.MinY > wy1) return 0;
                var pts = loop.L[lod];
                if (pts.Length < 6) return 0;               // dropped at this LOD
                int m = pts.Length / 2;
                bool inside = loop.MinX >= wx0 && loop.MaxX <= wx1 && loop.MinY >= wy0 && loop.MaxY <= wy1;
                if (!inside)
                {
                    pts = ClipToWindow(pts, wx0, wy0, wx1, wy1, out m);
                    if (m < 3) return 0;
                }
                Vector2 At(int i)
                {
                    int k = reverse ? m - 1 - i : i;
                    return new Vector2(
                        win.Vx0 + (float)((pts[k * 2] - win.Wx0) * s),
                        win.Vy0 + (float)((pts[k * 2 + 1] - win.Wy0) * s));
                }
                if (n + m + 2 > _splineBuf.Length)
                    Array.Resize(ref _splineBuf, n + m + 2048);
                var prev = At(m - 1);
                for (int i = 0; i < m; i++)
                {
                    var cur = At(i);
                    _splineBuf[n++] = new QuadraticBezier2(prev, cur);
                    prev = cur;
                }
                return m;
            }

            // Bands are drawn NESTED and translucent, each adding its increment
            // over the ones below (alpha solved at build time so the stack lands
            // exactly on the band's tone). Do NOT draw them as disjoint rings:
            // adjacent strips each only partially cover the shared boundary
            // pixel, which lands it BELOW both tones -> a dark line at every
            // boundary, i.e. cel-shaded contours instead of smooth shading.
            var list = bands.Bands;
            for (int b = 0; b < list.Count; b++)
            {
                var band = list[b];
                int need = band.Segs0 + 8;
                if (_splineBuf == null || _splineBuf.Length < need)
                    _splineBuf = new QuadraticBezier2[need + 2048];
                n = 0;
                foreach (var loop in band.Loops) Emit(loop, false);
                if (n == 0) continue;
                drawn += n;
                calls++;
                // A tinted set is an overlay: keep its own colour and paint it
                // solid so highlighted systems read clearly over the hull.
                var col = tint.HasValue
                    ? new ColorSRGB(tint.Value.R, tint.Value.G, tint.Value.B, (byte)235)
                    : new ColorSRGB(bb, bb, bb, band.Alpha);
                batch.DrawFill(new ReadOnlySpan<QuadraticBezier2>(_splineBuf, 0, n), col, null, false);
            }
            batch.ScissorPop();
            _lastBandLod = lod;
            _lastBandSegs = drawn;
            _bandFillCalls = calls;
        }
        _bandTicks += swRec.ElapsedTicks;
        _bandRecords++;
    }

    private static float[] _clipA, _clipB;

    // Sutherland-Hodgman clip of a closed polygon against the window rect.
    // A loop enclosing the whole window clips to the window itself, so large
    // solid regions still fill correctly — the reason simple point-clamping
    // isn't safe here. Result lives in a reused buffer (caller uses it before
    // the next call).
    private static float[] ClipToWindow(float[] pts, float x0, float y0, float x1, float y1, out int count)
    {
        int m = pts.Length / 2;
        int cap = Math.Max(64, (m + 8) * 2);
        if (_clipA == null || _clipA.Length < cap * 2) { _clipA = new float[cap * 2]; _clipB = new float[cap * 2]; }
        var src = _clipA;
        var dst = _clipB;
        Array.Copy(pts, src, pts.Length);
        int n = m;

        for (int plane = 0; plane < 4 && n >= 3; plane++)
        {
            int outN = 0;
            float px = 0, py = 0;
            for (int i = 0; i < n; i++)
            {
                float ax = src[i * 2], ay = src[i * 2 + 1];
                int j = (i + 1) % n;
                float bx = src[j * 2], by = src[j * 2 + 1];
                bool aIn = plane switch { 0 => ax >= x0, 1 => ax <= x1, 2 => ay >= y0, _ => ay <= y1 };
                bool bIn = plane switch { 0 => bx >= x0, 1 => bx <= x1, 2 => by >= y0, _ => by <= y1 };
                if (aIn)
                {
                    if (outN * 2 + 1 < dst.Length) { dst[outN * 2] = ax; dst[outN * 2 + 1] = ay; outN++; }
                }
                if (aIn != bIn)
                {
                    float t = plane switch
                    {
                        0 => (x0 - ax) / (bx - ax),
                        1 => (x1 - ax) / (bx - ax),
                        2 => (y0 - ay) / (by - ay),
                        _ => (y1 - ay) / (by - ay),
                    };
                    if (float.IsFinite(t))
                    {
                        t = Math.Clamp(t, 0f, 1f);
                        px = ax + (bx - ax) * t;
                        py = ay + (by - ay) * t;
                        if (outN * 2 + 1 < dst.Length) { dst[outN * 2] = px; dst[outN * 2 + 1] = py; outN++; }
                    }
                }
            }
            n = outN;
            (src, dst) = (dst, src);
        }
        count = n;
        return src;
    }

    // Same tone curve as PanelImage (min-max normalized, sqrt), returned as the alpha byte.
    private static int ToneOf(int v, int minV, int maxV)
    {
        if (v <= 0) return 0;
        double range = maxV - minV;
        double t = range > 0 ? (v - minV) / range : 1.0;
        return (int)Math.Min(255.0, 40 + 215.0 * Math.Sqrt(t));
    }

    // UISystem.PreloadTexture is the engine's own texture warmup. The UISystem
    // instance is fished out of the LCD render component's fields once (its
    // rebuild path calls GetUISystem, so it must hold the contracts object).
    private static Keen.VRage.Render.Contracts.UISystem _uiSystem;
    private static bool _uiSystemTried;

    // Debug: drop every cache for a panel so the next pass rebuilds from raw
    // block data (rules out stale scans/stamps/textures).
    public static void ForceRefresh(int key)
    {
        Scans.TryRemove(key, out _);
        _img.TryRemove(key, out _);
        _imgBase.TryRemove(key, out _);
        BlockShapes.ResetCaches();
        System.Threading.Interlocked.Increment(ref PanelState.Get(key).Version);
        ProbeLog.Line($"Force refresh key {key}: scan/image/shape caches cleared; rebuilding from scratch next pass.");
    }

    public static void TryResolveUiSystem(object comp)
    {
        if (_uiSystemTried || comp == null) return;
        _uiSystemTried = true;
        try
        {
            foreach (var f in comp.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static))
            {
                object v = null;
                try { v = f.GetValue(f.IsStatic ? null : comp); } catch { }
                if (v is Keen.VRage.Render.Contracts.UISystem us) { _uiSystem = us; break; }
                if (v is Keen.VRage.Render.Contracts.RenderContracts rc) { _uiSystem = rc.GetUISystem(); break; }
            }
            ProbeLog.Line($"UISystem preload {(_uiSystem != null ? "available" : "NOT found on component")} .");
        }
        catch (Exception e) { ProbeLog.Error("uisystem resolve", e); }
    }

    private static bool TryPreload(ResourceHandle handle)
    {
        var ui = _uiSystem;
        if (ui == null) return false;
        try
        {
            ui.PreloadTexture(handle);
            return true;
        }
        catch (Exception e)
        {
            if (_imgErrLogs++ < 3) ProbeLog.Error("preload", e);
            _uiSystem = null;
            return false;
        }
    }

    private static void EnsureViewHash(OccupancyScan scan, int[,] view)
    {
        if (scan.ViewHash != 0) return;
        unchecked
        {
            long f = 1469598103934665603L;
            int w = view.GetLength(0), h = view.GetLength(1);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++) { f ^= view[x, y]; f *= 1099511628211L; }
            scan.ViewHash = f == 0 ? 1 : f;
        }
    }

    // Draw a texture rendered from source window texWin so that it lands where
    // those cells belong in the CURRENT window (wx0,wy0,winW,winH) -> pixel rect.
    private static void DrawRemapped(IDrawBatch batch, ResourceHandle img,
        (double X0, double Y0, double X1, double Y1, int PxW, int PxH) texWin,
        double wx0, double wy0, double winW, double winH,
        float bx, float by, int destW, int destH, ColorSRGB tint)
    {
        double ix0 = Math.Max(texWin.X0, wx0), iy0 = Math.Max(texWin.Y0, wy0);
        double ix1 = Math.Min(texWin.X1, wx0 + winW), iy1 = Math.Min(texWin.Y1, wy0 + winH);
        if (ix1 <= ix0 || iy1 <= iy0) return;
        double tw = texWin.X1 - texWin.X0, th = texWin.Y1 - texWin.Y0;
        int sx0 = (int)((ix0 - texWin.X0) / tw * texWin.PxW);
        int sy0 = (int)((iy0 - texWin.Y0) / th * texWin.PxH);
        int sx1 = Math.Max(sx0 + 1, (int)Math.Ceiling((ix1 - texWin.X0) / tw * texWin.PxW));
        int sy1 = Math.Max(sy0 + 1, (int)Math.Ceiling((iy1 - texWin.Y0) / th * texWin.PxH));
        float dx0 = bx + (float)((ix0 - wx0) / winW * destW);
        float dy0 = by + (float)((iy0 - wy0) / winH * destH);
        float dx1 = bx + (float)((ix1 - wx0) / winW * destW);
        float dy1 = by + (float)((iy1 - wy0) / winH * destH);
        var dest = new BoundingBox2(new Vector2(dx0, dy0), new Vector2(dx1, dy1));
        var src = new BoundingBox2I(new Vector2I(sx0, sy0), new Vector2I(sx1, sy1));
        batch.DrawImage(img, dest, tint, false, null, src);
    }

    // Render-thread side of the async pipeline for one layer entry: promote a
    // warmed pending image, kick a background build if the want differs, return
    // what to draw right now.
    private static void StepEntry(ImgEntry e, int key, long want, byte[,] tones, int pxW, int pxH,
        double wx0, double wy0, double wx1, double wy1,
        out ResourceHandle handle, out (double X0, double Y0, double X1, double Y1, int PxW, int PxH) texWin, out bool anyImage,
        out ResourceHandle pending, out bool hasPending)
    {
        handle = default;
        texWin = default;
        anyImage = false;
        pending = default;
        hasPending = false;
        try
        {
            long now = Environment.TickCount64;
            lock (e)
            {
                // First image ever: promote instantly — a blank panel has nothing
                // to protect, and preload/warm-draw covers the streaming window.
                if (e.PendingWant != 0 && (e.Want == 0 || now - e.PendingAt > ImgWarmupMs))
                {
                    if (e.Want != 0)
                        e.Retired.Add((e.Handle, e.Abs, now)); // release later, after any in-flight use
                    e.Want = e.PendingWant;
                    e.Handle = e.PendingHandle;
                    e.X0 = e.PX0; e.Y0 = e.PY0; e.X1 = e.PX1; e.Y1 = e.PY1;
                    e.PxW = e.PPxW; e.PxH = e.PPxH;
                    e.Abs = e.PAbs;
                    e.PendingWant = 0;
                    RepaintRequest[key] = true; // show the fresh texture promptly
                    ProbeLog.Line($"Img swap key {key}: gen {e.Gen} live ({e.PxW}x{e.PxH}).");
                }
                // Drain retired textures after a generous grace period so the
                // registered-texture pile can't grow into VRAM/budget pressure.
                for (int i = e.Retired.Count - 1; i >= 0; i--)
                {
                    if (now - e.Retired[i].At < 60000) continue;
                    var r = e.Retired[i];
                    e.Retired.RemoveAt(i);
                    try { Singleton<FileSystem>.Instance?.ContentCache?.Unregister(r.Handle); } catch { }
                    try { if (!string.IsNullOrEmpty(r.Abs)) File.Delete(r.Abs); } catch { }
                }
                if (e.Want != want && e.PendingWant != want && !e.Building)
                {
                    e.Building = true;
                    System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                        BuildPanelImage(key, e, want, tones, pxW, pxH, wx0, wy0, wx1, wy1));
                }
                if (e.Want != 0)
                {
                    handle = e.Handle;
                    texWin = (e.X0, e.Y0, e.X1, e.Y1, e.PxW, e.PxH);
                    anyImage = true;
                }
                if (e.PendingWant != 0) { pending = e.PendingHandle; hasPending = true; }
            }
        }
        catch (Exception ex)
        {
            if (_imgErrLogs++ < 3) ProbeLog.Error("panel image step", ex);
        }
    }

    private static void BuildPanelImage(int key, ImgEntry e, long want, byte[,] tones, int destW, int destH,
        double wx0, double wy0, double wx1, double wy1)
    {
        try
        {
            var gray = PanelImage.RenderTones(tones, destW, destH, wx0, wy0, wx1, wy1);
            int gen = System.Threading.Interlocked.Increment(ref e.Gen);
            string name = $"gs_panel_{key}_{_imgSalt}_{gen}.png";
            var wfh = new FileHandleWritable(RootPath.Temp, name);
            wfh.CreateDirectories();
            using (var stream = wfh.Open(FileMode.Create, FileAccess.Write, FileShare.None))
                PngWriter.WriteGrayRgba(stream, gray);

            _fhCtor ??= typeof(FileHandle).GetConstructor(
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null, new[] { typeof(RootPath), typeof(string) }, null);
            if (_fhCtor == null) { lock (e) e.Building = false; return; }
            var fh = (FileHandle)_fhCtor.Invoke(new object[] { RootPath.Temp, name });
            var handle = ResourceHandle.GetOrRegister(fh, false);
            string abs = null;
            try { abs = wfh.GetAbsolutePath(); } catch { }
            lock (e)
            {
                e.PendingWant = want;
                e.PendingHandle = handle;
                e.PX0 = wx0; e.PY0 = wy0; e.PX1 = wx1; e.PY1 = wy1;
                e.PPxW = destW; e.PPxH = destH;
                e.PAbs = abs;
                e.PendingAt = Environment.TickCount64;
                e.Building = false;
            }
            if (_imgGenLogs++ < 60)
                ProbeLog.Line($"Panel image key {key} gen {gen}: {destW}x{destH} px built async ({gray.LongLength * 4 / 1024} KB raw).");
        }
        catch (Exception ex)
        {
            lock (e) e.Building = false;
            if (_imgErrLogs++ < 3) ProbeLog.Error("panel image build", ex);
        }
    }

    // Same tone curve as BmpWriter so the panel matches the exported renders.
    private static int GrayOf(int v, int maxV)
    {
        if (v <= 0) return 0;
        return (int)Math.Min(255.0, 40 + 215.0 * Math.Sqrt((double)v / maxV));
    }

    private static void FillRect(IDrawBatch batch, float x0, float y0, float x1, float y1, ColorSRGB color)
    {
        Span<QuadraticBezier2> rect = stackalloc QuadraticBezier2[4];
        rect[0] = new QuadraticBezier2(new Vector2(x0, y0), new Vector2(x1, y0));
        rect[1] = new QuadraticBezier2(new Vector2(x1, y0), new Vector2(x1, y1));
        rect[2] = new QuadraticBezier2(new Vector2(x1, y1), new Vector2(x0, y1));
        rect[3] = new QuadraticBezier2(new Vector2(x0, y1), new Vector2(x0, y0));
        batch.DrawFill(rect, color, null, false);
    }
}
