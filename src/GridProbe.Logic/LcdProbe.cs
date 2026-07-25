using Keen.Game2.Simulation.WorldObjects.CubeBlocks.Lcd;
using Keen.Game2.Simulation.WorldObjects.CubeGrids;

namespace GridProbe;

internal static class LcdProbe
{
    public const string Tag = "[GS]";
    private static readonly char[] Shades = { ' ', '░', '▒', '▓', '█' };
    private static bool _modesDumped;

    public static int ShowOnTaggedPanels(List<CubeGridComponent> ship, OccupancyScan scan, int pass)
    {
        var anyGridOfShip = ship[0];
        var panels = new List<LcdMultiPanelComponent>();
        try
        {
            anyGridOfShip.VisitAllBlocksWithComponent<LcdMultiPanelComponent>(p => panels.Add(p), includeSubgrids: true);
        }
        catch (Exception e) { ProbeLog.Error("lcd visit", e); return 0; }

        // Ship key derived from the tagged panel block's grid-local position:
        // entity identities churn (the game re-creates grid entities), block
        // positions do not. Stable across passes, reloads, and sessions.
        int shipKey = int.MaxValue;
        foreach (var lcd in panels)
        {
            int count0;
            try { count0 = lcd.SurfaceCount; } catch { continue; }
            bool anyTag = false;
            for (int i = 0; i < count0 && !anyTag; i++)
            {
                string t0 = null, n0 = null;
                try { t0 = lcd.GetSurfaceState(i).Text; } catch { }
                try { n0 = lcd.GetSurfaceEffectiveDisplayName(i); } catch { }
                anyTag = (t0 != null && t0.Contains(Tag, StringComparison.OrdinalIgnoreCase))
                      || (n0 != null && n0.Contains(Tag, StringComparison.OrdinalIgnoreCase));
            }
            if (!anyTag) continue;
            try
            {
                var bc = lcd.Entity?.TryGet<Keen.Game2.Simulation.WorldObjects.CubeBlocks.CubeBlockComponent>();
                if (bc != null)
                {
                    var m = bc.AABB.Min;
                    unchecked
                    {
                        int h = (int)2166136261;
                        h = (h ^ m.X) * 16777619;
                        h = (h ^ m.Y) * 16777619;
                        h = (h ^ m.Z) * 16777619;
                        h = (h & 0x7FFFFFFF) % 100000;
                        if (h < shipKey) shipKey = h;
                    }
                }
            }
            catch { }
        }
        if (shipKey == int.MaxValue) return 0; // no tagged panels on this ship

        var state = PanelState.Get(shipKey);
        // Unchanged geometry keeps the previous scan object — and with it every
        // derived cache (tones, channels, contours, bands, textures). Only a
        // real edit swaps the scan and forces rebuilds.
        if (VectorLcd.Scans.TryGetValue(shipKey, out var prevScan) && prevScan.ContentEquals(scan))
            scan = prevScan;
        else
            VectorLcd.Scans[shipKey] = scan;

        int shown = 0;
        var taggedPanels = new List<CursorAim.PanelRef>();
        foreach (var lcd in panels)
        {
            int count;
            try { count = lcd.SurfaceCount; } catch { continue; }
            for (int i = 0; i < count; i++)
            {
                string text = null, name = null;
                try { text = lcd.GetSurfaceState(i).Text; } catch { }
                try { name = lcd.GetSurfaceEffectiveDisplayName(i); } catch { }
                bool tagged = (text != null && text.Contains(Tag, StringComparison.OrdinalIgnoreCase))
                           || (name != null && name.Contains(Tag, StringComparison.OrdinalIgnoreCase));
                if (!tagged) continue;
                try
                {
                    var blockComp = lcd.Entity?.TryGet<Keen.Game2.Simulation.WorldObjects.CubeBlocks.CubeBlockComponent>();
                    if (blockComp != null) taggedPanels.Add(new CursorAim.PanelRef { Lcd = lcd, Block = blockComp });
                }
                catch { }
                try
                {
                    // Only cycle when the render side actually reset the RT (post-boost
                    // unstick); a fresh panel starts at boosted res and shows instantly.
                    if (VectorLcd.NeedsContentCycle.TryRemove(shipKey, out _))
                    {
                        lcd.SetSurfaceContent(i, LcdPanelContent.None);
                        ProbeLog.Line($"Content cycle (off) for key {shipKey}; restoring next pass.");
                        continue;
                    }
                    const int maxCols = 110, maxRows = 64;
                    const double charAspect = 2.0;
                    int w = scan.Size.X, h = scan.Size.Z;
                    int cols = maxCols;
                    int rows = Math.Max(6, (int)Math.Round(cols * (double)h / Math.Max(1, w) / charAspect));
                    if (rows > maxRows)
                    {
                        rows = maxRows;
                        cols = Math.Max(12, (int)Math.Round(rows * charAspect * w / (double)Math.Max(1, h)));
                    }
                    lcd.SetSurfaceContent(i, LcdPanelContent.TextAndImage);
                    lcd.SetSurfaceFontSize(i, 11f);
                    lcd.SetSurfacePadding(i, 6f);
                    string resInfo = VectorLcd.PanelRes.TryGetValue(shipKey, out var pr) ? $" | {pr.W}x{pr.H}px" : "";
                    string uiInfo = $" | {PanelState.ViewName(state.ViewAxis)}/{PanelState.ModeName(state.Mode)} z{state.Zoom:F1}";
                    lcd.SetSurfaceText(i, $"{Tag}#{shipKey} p{pass} | {scan.BlockCount} blocks | {scan.Size.X}x{scan.Size.Z} cells{resInfo}{uiInfo}");
                    shown++;
                }
                catch (Exception e) { ProbeLog.Error($"lcd draw surface {i}", e); }
            }
        }
        if (taggedPanels.Count > 0) CursorAim.Tagged[shipKey] = taggedPanels;
        else CursorAim.Tagged.TryRemove(shipKey, out _);

        // Heavy per-scan preprocessing only for ships someone is looking at.
        if (taggedPanels.Count > 0)
        {
            try
            {
                scan.EnsureBands(state.ViewAxis, state.Mode); // channels+tones+band geometry for the renderer
                if (!_modesDumped)
                {
                    // One-shot self-verification: export mode tone fields AND the
                    // coverage fields (fractional slope edges must show mid-grays).
                    _modesDumped = true;
                    scan.EnsureChannels(PanelState.DepthAxisOf(state.ViewAxis));
                    for (int m = 0; m < 3; m++)
                    {
                        var t = ToneFields.Get(scan, state.ViewAxis, m);
                        if (t != null)
                            BmpWriter.WriteGray8(Path.Combine(ProbeLog.OutDir, $"mode_{PanelState.ModeName(m)}.bmp"), t, topRowFirst: true);
                    }
                    DumpCoverage(scan.CovFront, "cov_top");
                    DumpCoverage(scan.CovTop, "cov_front");
                    DumpCoverage(scan.CovSide, "cov_side");
                    ProbeLog.Line("Mode tone + coverage fields exported (output\\mode_*.bmp, cov_*.bmp).");
                    AutoEdgeAudit(scan, anyGridOfShip);
                    SeamAudit(scan, anyGridOfShip);
                }
            }
            catch (Exception e) { ProbeLog.Error("pass preprocessing", e); }
        }
        return shown;
    }

    // Finds stepped silhouette corners in the top-view coverage (binary 90°
    // outer corners = staircase teeth) and names the blocks that form them.
    private static void AutoEdgeAudit(OccupancyScan scan, CubeGridComponent grid)
    {
        try
        {
            var cov = scan.CovFront; // top view [X,Z]
            if (cov == null) return;
            int w = cov.GetLength(0), h = cov.GetLength(1);
            var picks = new List<(int X, int Z)>();
            for (int x = 2; x < w - 2 && picks.Count < 40; x += 1)
                for (int z = 2; z < h - 2; z += 1)
                {
                    // full cell forming an outer corner with fully empty diagonal
                    // neighborhood — a staircase tooth, no fractional easing
                    if (cov[x, z] < BlockShapes.FracUnits) continue;
                    bool cornerA = cov[x - 1, z] == 0 && cov[x, z - 1] == 0 && cov[x - 1, z - 1] == 0
                                && cov[x + 1, z] >= BlockShapes.FracUnits && cov[x, z + 1] >= BlockShapes.FracUnits;
                    bool cornerB = cov[x + 1, z] == 0 && cov[x, z - 1] == 0 && cov[x + 1, z - 1] == 0
                                && cov[x - 1, z] >= BlockShapes.FracUnits && cov[x, z + 1] >= BlockShapes.FracUnits;
                    if (cornerA || cornerB)
                    {
                        bool spaced = true;
                        foreach (var p in picks)
                            if (Math.Abs(p.X - x) + Math.Abs(p.Z - z) < 25) { spaced = false; break; }
                        if (spaced) picks.Add((x, z));
                    }
                }
            ProbeLog.Line($"Edge audit: {picks.Count} stepped silhouette corners sampled (top view).");
            int audit = 0;
            foreach (var p in picks)
            {
                if (++audit > 4) break;
                ProbeLog.Line($"Edge audit sample {audit}: cell ({p.X},{p.Z}) -> grid x={scan.Min.X + p.X} z={scan.Min.Z + p.Z}");
                CellInspector.ListColumn(grid, PanelState.ViewTop, scan.Min.X + p.X, int.MinValue, scan.Min.Z + p.Z);
            }
        }
        catch (Exception e) { ProbeLog.Error("edge audit", e); }
    }

    // Finds where an otherwise steady diagonal edge JOGS — the silhouette
    // stepping two or more cells in one row where it had been stepping one, and
    // losing its fractional cell as it does. That is the signature of the
    // "divet" artefact, and it names the blocks meeting at the seam.
    private static void SeamAudit(OccupancyScan scan, CubeGridComponent grid)
    {
        try
        {
            var cov = scan.CovSide; // side view [Z,Y] after rotation
            if (cov == null) return;
            int w = cov.GetLength(0), h = cov.GetLength(1);

            // Leftmost occupied cell per row: the silhouette's left boundary.
            var edge = new int[h];
            for (int y = 0; y < h; y++)
            {
                edge[y] = -1;
                for (int x = 0; x < w; x++)
                    if (cov[x, y] > 0) { edge[y] = x; break; }
            }

            int found = 0;
            for (int y = 2; y < h - 2 && found < 6; y++)
            {
                if (edge[y] < 0 || edge[y - 1] < 0 || edge[y + 1] < 0 || edge[y - 2] < 0) continue;
                int stepPrev = edge[y - 1] - edge[y - 2];
                int stepHere = edge[y] - edge[y - 1];
                // Was walking one cell per row, then jumped further in one row.
                if (Math.Abs(stepPrev) != 1 || Math.Abs(stepHere) < 2) continue;
                if (Math.Sign(stepPrev) != Math.Sign(stepHere)) continue;

                found++;
                ProbeLog.Line($"Seam audit {found}: side cell ({edge[y]},{y}) edge stepped {stepHere} after steady {stepPrev}; cov here={cov[edge[y], y]} prev row={cov[edge[y - 1], y - 1]}");
                // Side view display (du,dv) -> grid, mirroring CellInspector.
                int gy = scan.Min.Y + (scan.Size.Y - 1 - y);
                int gz = scan.Min.Z + edge[y];
                CellInspector.ListColumn(grid, PanelState.ViewSide, int.MinValue, gy, gz);
            }
            if (found == 0) ProbeLog.Line("Seam audit: no jogged diagonal edges found.");
        }
        catch (Exception e) { ProbeLog.Error("seam audit", e); }
    }

    private static void DumpCoverage(byte[,] cov, string name)
    {
        if (cov == null) return;
        int w = cov.GetLength(0), h = cov.GetLength(1);
        var img = new byte[w, h];
        int frac = 0, occupied = 0;
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                int c = cov[x, y];
                img[x, y] = (byte)Math.Min(255, c * 16);
                if (c > 0) occupied++;
                if (c > 0 && c < BlockShapes.FracUnits) frac++;
            }
        BmpWriter.WriteGray8(Path.Combine(ProbeLog.OutDir, $"{name}.bmp"), img, topRowFirst: true);
        ProbeLog.Line($"{name}: {occupied} occupied columns, {frac} fractional ({100.0 * frac / Math.Max(1, occupied):F1}% — slope edges should be well above 0).");
    }

    private static string RenderTopView(OccupancyScan scan, int cols, int rows)
    {
        var view = scan.Front;
        int w = view.GetLength(0), h = view.GetLength(1);
        int maxV = 1;
        for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) if (view[x, y] > maxV) maxV = view[x, y];
        var sb = new System.Text.StringBuilder(rows * (cols + 1));
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int x0 = c * w / cols, x1 = Math.Max(x0 + 1, (c + 1) * w / cols);
                int y0 = r * h / rows, y1 = Math.Max(y0 + 1, (r + 1) * h / rows);
                int best = 0;
                for (int x = x0; x < x1 && x < w; x++)
                    for (int y = y0; y < y1 && y < h; y++)
                        if (view[x, y] > best) best = view[x, y];
                int shade = best <= 0 ? 0 : 1 + (int)Math.Min(3.0, 4.0 * best / (maxV + 1));
                sb.Append(Shades[shade]);
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }
}
