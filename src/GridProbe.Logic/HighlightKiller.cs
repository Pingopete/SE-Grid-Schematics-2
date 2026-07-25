using System.Reflection;

namespace GridProbe;

internal static class HighlightKiller
{
    private static readonly Guid[] HighlightGuids =
    {
        new("9c36070a-6bad-40dc-9dc7-a8514e5e8657"),
        new("5e9d4179-6fe9-4460-b889-295cbc62b11b"),
    };

    private static bool _done;

    public static void TryKill()
    {
        if (_done) return;
        try
        {
            if (SceneLocator.Sessions.IsEmpty) return;
            int killed2 = 0;
            foreach (var session in SceneLocator.Sessions.Keys)
            {
            var recon = _sessionDumps.TryAdd(session, true) ? new System.Text.StringBuilder() : null;
            session.SessionComponents.ForEach<Keen.VRage.DCS.Components.Component>(c =>
            {
                var tn = c.GetType().Name;
                recon?.Append(tn).Append(", ");
                bool interesting = tn.Contains("UseAction") || tn.Contains("Highlight") || tn.Contains("Interact")
                    || tn.Contains("ControlHints") || tn.Contains("Targeting") || tn.Contains("ScreenOpening");
                if (interesting && _componentDumps++ < 6)
                    ProbeLog.Line($"HighlightKiller: candidate session component {c.GetType().FullName}");
                if (!interesting) return;
                foreach (var f in c.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    object v = null;
                    try { v = f.GetValue(c); } catch { }
                    if (v == null) continue;
                    if (v.GetType().Name.Contains("HighlightEffectDefinition"))
                    {
                        if (Neuter(v)) { killed2++; ProbeLog.Line($"HighlightKiller: neutered def held by {tn}.{f.Name}"); }
                    }
                    else if (v is System.Collections.IEnumerable seq && v is not string)
                    {
                        foreach (var el in seq)
                        {
                            if (el == null) continue;
                            var elv = el;
                            var et = el.GetType();
                            if (et.IsGenericType && et.Name.StartsWith("KeyValuePair"))
                            { elv = et.GetProperty("Value")?.GetValue(el); et = elv?.GetType(); }
                            if (elv != null && et != null && et.Name.Contains("HighlightEffectDefinition"))
                                if (Neuter(elv)) { killed2++; ProbeLog.Line($"HighlightKiller: neutered def in {tn}.{f.Name}[]"); }
                        }
                    }
                }
            }, reverse: false);
            if (recon != null)
                ProbeLog.Line($"Session components ({SceneLocator.Sessions[session]}): {recon}");
            }
            if (killed2 > 0) { _done = true; ProbeLog.Line($"HighlightKiller: done, {killed2} neutered."); }
            return;
        }
        catch (Exception e) { ProbeLog.Error("highlight killer v2", e); }
        try
        {
            var core = Keen.VRage.Core.VRageCore.Instance;
            var engine = core?.Engine;
            if (engine == null) return;

            object manager = null;
            engine.ForEach<Keen.VRage.DCS.Components.Component>(c =>
            {
                if (c.GetType().Name.Contains("DefinitionSetManager")) manager = c;
            }, reverse: false);
            if (manager == null) { ProbeLog.Line("HighlightKiller: definition manager not found on engine."); _done = true; return; }
            ProbeLog.Line($"HighlightKiller: using manager {manager.GetType().Name}.");

            var mt = manager.GetType();
            var setsProp = mt.GetProperty("AvailableDefinitionSets");
            var sets = setsProp?.GetValue(manager) as System.Collections.IEnumerable;
            var getLocator = mt.GetMethod("GetDefinitionLocator", BindingFlags.Public | BindingFlags.Instance);
            if (sets == null || getLocator == null) { ProbeLog.Line("HighlightKiller: manager API mismatch."); _done = true; return; }

            int killed = 0;
            foreach (var setName in sets)
            {
                object locator = null;
                try { locator = getLocator.Invoke(manager, new[] { (object)setName.ToString() }); } catch { }
                if (locator == null) continue;
                var lt = locator.GetType();
                if (killed == 0 && _locatorDumps++ < 2)
                    ProbeLog.Line($"HighlightKiller: set '{setName}' locator {lt.Name}: " +
                        string.Join(", ", lt.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                            .Where(m => !m.IsSpecialName && m.DeclaringType != typeof(object)).Select(m => m.Name).Distinct().Take(12)));

                var getAll = lt.GetMethods(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(m => m.Name == "GetDefinitionsAsync");
                if (getAll == null) continue;
                object taskObj = null;
                try
                {
                    var args = getAll.GetParameters()
                        .Select(p => p.ParameterType == typeof(CancellationToken) ? (object)CancellationToken.None
                                   : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null)
                        .ToArray();
                    taskObj = getAll.Invoke(locator, args);
                }
                catch (Exception e) { ProbeLog.Line($"HighlightKiller: GetDefinitionsAsync({string.Join(",", getAll.GetParameters().Select(p => p.ParameterType.Name))}) failed on '{setName}': {e.InnerException?.Message ?? e.Message}"); continue; }
                if (taskObj != null && taskObj is not System.Collections.IEnumerable)
                {
                    var rt = taskObj.GetType();
                    if (taskObj is Task sysTask) { try { sysTask.Wait(3000); } catch { } }
                    else if (rt.GetProperty("IsCompleted")?.GetValue(taskObj) is bool done && !done)
                    { ProbeLog.Line($"HighlightKiller: '{setName}' task pending; will retry."); continue; }
                    object unwrapped = null;
                    try { unwrapped = rt.GetProperty("Result")?.GetValue(taskObj); } catch { }
                    if (unwrapped == null)
                        try { unwrapped = rt.GetMethod("GetResult", Type.EmptyTypes)?.Invoke(taskObj, null); } catch { }
                    taskObj = unwrapped ?? taskObj;
                }
                if (taskObj is not System.Collections.IEnumerable defs) { ProbeLog.Line($"HighlightKiller: '{setName}' result not enumerable: {taskObj?.GetType().Name ?? "null"}"); continue; }
                foreach (var def in defs)
                {
                    if (def == null || !def.GetType().Name.Contains("HighlightEffectDefinition")) continue;
                    if (Neuter(def)) { killed++; ProbeLog.Line($"HighlightKiller: neutered {def.GetType().Name} in set '{setName}'."); }
                }
            }
            if (killed >= HighlightGuids.Length) { ProbeLog.Line("HighlightKiller: all highlight definitions neutered."); _done = true; }
            else if (killed > 0) ProbeLog.Line($"HighlightKiller: {killed}/{HighlightGuids.Length} neutered so far; will retry.");
        }
        catch (Exception e) { ProbeLog.Error("highlight killer", e); _done = true; }
    }

    private static int _locatorDumps;
    private static int _componentDumps;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<object, bool> _sessionDumps = new();

    private static bool Neuter(object def)
    {
        bool any = false;
        var t = def.GetType();
        foreach (var name in new[] { "Color", "StrobePeriod" })
        {
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var f = t.GetField($"<{name}>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)
                 ?? t.GetField("_" + char.ToLowerInvariant(name[0]) + name[1..], BindingFlags.NonPublic | BindingFlags.Instance)
                 ?? t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            object value = name == "Color" ? new Keen.VRage.Library.Mathematics.ColorSRGB((byte)0, (byte)0, (byte)0, (byte)0) : 100000f;
            try
            {
                if (p?.CanWrite == true) { p.SetValue(def, Convert.ChangeType(value, p.PropertyType)); any = true; }
                else if (f != null) { f.SetValue(def, f.FieldType == typeof(float) ? 100000f : value); any = true; }
            }
            catch { }
        }
        return any;
    }
}
