using System.Reflection;

namespace GridProbe;

internal static class ResolutionBooster
{
    private const int MinDim = 1024;
    private static readonly HashSet<object> _boostedDefs = new(ReferenceEqualityComparer.Instance);
    private static int _boostLogs;

    public static bool BoostPanelDefinition(object definition)
    {
        if (definition == null || !_boostedDefs.Add(definition)) return false;
        int boosted = 0;
        var surfaces = GetMember(definition, "Surfaces") as System.Collections.IEnumerable;
        if (surfaces == null)
        {
            ProbeLog.Line($"ResolutionBooster: no Surfaces member on {definition.GetType().Name} — members: " +
                string.Join(", ", definition.GetType().GetProperties().Select(p => p.Name).Take(12)));
            return false;
        }
        foreach (var item in surfaces)
        {
            var surface = item;
            var vt = item?.GetType();
            if (vt != null && vt.IsGenericType && vt.Name.StartsWith("KeyValuePair"))
                surface = vt.GetProperty("Value")?.GetValue(item);
            if (surface == null || !surface.GetType().Name.Contains("LcdPanelSurface")) continue;
            if (BoostSurface(surface)) boosted++;
        }
        if (boosted > 0) ProbeLog.Line($"ResolutionBooster: boosted {boosted} surfaces on {definition.GetType().Name} (min dim {MinDim}).");
        return boosted > 0;
    }

    public static bool BoostSurfaceObject(object surface)
    {
        if (surface == null || !_boostedDefs.Add(surface)) return false;
        bool ok = BoostSurface(surface);
        if (_boostLogs++ < 3)
            ProbeLog.Line(ok
                ? $"ResolutionBooster: surface def boosted to min {MinDim}."
                : "ResolutionBooster: surface boost FAILED (Resolution not writable?).");
        return ok;
    }

    private static bool BoostSurface(object surface)
    {
        var t = surface.GetType();
        var resField = t.GetField("Resolution", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var resProp = t.GetProperty("Resolution", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var resVal = resField?.GetValue(surface) ?? resProp?.GetValue(surface);
        if (resVal == null) return false;
        var resType = resVal.GetType();
        int x = (int)(resType.GetField("X")?.GetValue(resVal) ?? 0);
        int y = (int)(resType.GetField("Y")?.GetValue(resVal) ?? 0);
        if (x <= 0 || y <= 0) return false;
        int min = Math.Min(x, y);
        if (min >= MinDim) return false;
        int factor = MinDim / min;
        object newRes;
        try { newRes = Activator.CreateInstance(resType, x * factor, y * factor); }
        catch { return false; }
        try
        {
            if (resField != null) { resField.SetValue(surface, newRes); return true; }
            if (resProp?.CanWrite == true) { resProp.SetValue(surface, newRes); return true; }
            var bf = t.GetField("<Resolution>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
            if (bf != null) { bf.SetValue(surface, newRes); return true; }
        }
        catch { }
        return false;
    }

    private static object GetMember(object obj, string name)
    {
        var t = obj.GetType();
        try
        {
            return t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(obj)
                ?? t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(obj);
        }
        catch { return null; }
    }
}
