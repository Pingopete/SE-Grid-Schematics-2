using Keen.Game2.Simulation.WorldObjects.CubeBlocks;
using Keen.Game2.Simulation.WorldObjects.CubeBlocks.ResourceDistribution.Resources;
using Keen.Game2.Simulation.WorldObjects.CubeGrids.ResourceDistribution.Conveyors;

namespace GridProbe;

// Which ship system a block belongs to, decided from what the block IS — the
// components the engine itself attached to it — never from its name. Name
// matching breaks on every new block, every renamed block and every mod; a
// capability check keeps working because it asks the same question the game does.
//
// CONVEYOR: the block carries a ConveyorSystemComponent. That component holds
// the block's own conveyor graph and its links to neighbouring systems, so its
// presence IS membership of the network — nothing to infer.
//
// POWER / GAS: the block carries a ResourceContainerComponent, which names the
// ResourceTypeDefinition it handles. Consumers do not get one; only blocks that
// source or store a resource do. The two families are told apart by the
// resource's own declared behaviour: gases must travel through conveyors,
// electricity does not (ResourceTypeDefinition.RequiresConveyors).
internal static class BlockSystems
{
    public static int Classify(CubeBlockComponent b)
    {
        if (b == null) return PanelState.HighlightNone;
        var entity = b.Entity;
        if (entity == null) return PanelState.HighlightNone;

        try
        {
            if (entity.TryGet<ConveyorSystemComponent>() != null)
            {
                Survey(b, "conveyor", null);
                return PanelState.HighlightConveyor;
            }
        }
        catch { }

        try
        {
            var res = entity.TryGet<ResourceContainerComponent>();
            if (res != null)
            {
                var type = res.ResourceType;
                bool gas = type != null && type.RequiresConveyors;
                Survey(b, gas ? "gas" : "power", type);
                return gas ? PanelState.HighlightGas : PanelState.HighlightPower;
            }
        }
        catch { }

        return PanelState.HighlightNone;
    }

    // One line per definition the first time it is classified, so the mapping
    // can be checked against a real ship rather than assumed.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _surveyed = new();
    private static int _surveyCount;

    private static void Survey(CubeBlockComponent b, string category, ResourceTypeDefinition type)
    {
        if (_surveyCount >= 50) return;
        try
        {
            var d = b.Definition;
            string name = (d?.GetType().GetProperty("DebugName")?.GetValue(d) ?? d?.ToString())?.ToString() ?? "?";
            if (name.Length > 60) name = name[^60..];
            if (!_surveyed.TryAdd(name, true)) return;
            _surveyCount++;
            string res = type == null ? "" : $" resource={type.Name} requiresConveyors={type.RequiresConveyors}";
            ProbeLog.Line($"System: {category}{res} :: {name}");
        }
        catch { }
    }
}
