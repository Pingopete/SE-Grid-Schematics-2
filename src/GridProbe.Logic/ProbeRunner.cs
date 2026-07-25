using System.Diagnostics;
using Keen.Game2.Simulation.WorldObjects.CubeGrids;
using Keen.VRage.DCS.Accessors;
using Keen.VRage.DCS.Components;
using Keen.VRage.DCS.Scenes;

namespace GridProbe;

internal static class ProbeRunner
{
    private static int _pass;
    private static DateTime _lastPass = DateTime.MinValue;

    public static int RunOnce(Scene scene)
    {
        if ((DateTime.UtcNow - _lastPass).TotalSeconds < 2) return 0;
        _lastPass = DateTime.UtcNow;
        _pass++;
        var grids = new List<CubeGridComponent>();
        var sw = Stopwatch.StartNew();
        try
        {
            foreach (var d in scene.EnumerateEntities())
            {
                var entity = Entity.TryGetFromDataEntity(new DEntityContext(scene, d));
                var grid = entity?.TryGet<CubeGridComponent>();
                if (grid != null) grids.Add(grid);
            }
        }
        catch (Exception e)
        {
            ProbeLog.Error("entity enumeration", e);
            return 0;
        }
        var enumMs = sw.Elapsed.TotalMilliseconds;
        if (grids.Count == 0)
        {
            if (_pass % 4 == 1) ProbeLog.Line($"Pass {_pass}: scene alive, 0 grids ({enumMs:F1} ms enumerate).");
            return 0;
        }

        var seen = new HashSet<DEntity>();
        var ships = new List<List<CubeGridComponent>>();
        foreach (var g in grids)
        {
            if (!seen.Add(g.DEntity)) continue;
            var ship = new List<CubeGridComponent> { g };
            try
            {
                foreach (var otherEntity in g.GetAllGridsInShip())
                {
                    var other = otherEntity?.TryGet<CubeGridComponent>();
                    if (other != null && seen.Add(other.DEntity)) ship.Add(other);
                }
            }
            catch { }
            ships.Add(ship);
        }

        OccupancyScan biggest = null, taggedBest = null;
        int panelsDrawn = 0, scanned = 0;
        double scanMsTotal = 0;
        foreach (var ship in ships)
        {
            var scan = OccupancyScan.Run(ship[0]);
            if (scan == null) continue;
            scanned++;
            scanMsTotal += scan.TotalMs;
            var tagged = LcdProbe.ShowOnTaggedPanels(ship, scan, _pass);
            panelsDrawn += tagged;
            if (tagged > 0 && (taggedBest == null || scan.BlockCount > taggedBest.BlockCount)) taggedBest = scan;
            if (biggest == null || scan.BlockCount > biggest.BlockCount) biggest = scan;
        }
        VectorLcd.CurrentScan = taggedBest;

        biggest?.WriteBmps(Path.Combine(ProbeLog.OutDir, "scan"));
        taggedBest?.WriteBmps(Path.Combine(ProbeLog.OutDir, "tagged"));
        ProbeLog.Line($"Pass {_pass}: {grids.Count} grids -> {ships.Count} ships, {scanned} scanned ({scanMsTotal:F1} ms total), {panelsDrawn} tagged panel surfaces drawn. Biggest: {(biggest == null ? "none" : $"{biggest.BlockCount} blocks {biggest.Size.X}x{biggest.Size.Y}x{biggest.Size.Z}")}.");
        return grids.Count;
    }
}
