using System.Reflection;

namespace GridProbe;

internal static class DefinitionTools
{
    private static int _taskDumps;
    private static int _loadingDataDumps;

    public static void EnumerateAll(Action<object> perDefinition)
    {
        var core = Keen.VRage.Core.VRageCore.Instance;
        var engine = core?.Engine;
        if (engine == null) return;

        object manager = null;
        engine.ForEach<Keen.VRage.DCS.Components.Component>(c =>
        {
            if (c.GetType().Name.Contains("DefinitionSetManager")) manager = c;
        }, reverse: false);
        if (manager == null) return;

        var mt = manager.GetType();
        var sets = mt.GetProperty("AvailableDefinitionSets")?.GetValue(manager) as System.Collections.IEnumerable;
        var getLocator = mt.GetMethod("GetDefinitionLocator", BindingFlags.Public | BindingFlags.Instance);
        if (sets == null || getLocator == null) return;

        foreach (var setName in sets)
        {
            object locator = null;
            try { locator = getLocator.Invoke(manager, new[] { (object)setName.ToString() }); } catch { }
            var getAll = locator?.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(m => m.Name == "GetDefinitionsAsync");
            if (getAll == null) continue;
            object result;
            try
            {
                var args = getAll.GetParameters()
                    .Select(p => p.ParameterType == typeof(CancellationToken) ? (object)CancellationToken.None
                               : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null)
                    .ToArray();
                result = getAll.Invoke(locator, args);
            }
            catch { continue; }

            if (result != null && result is not System.Collections.IEnumerable)
                result = Unwrap(result);
            if (result is not System.Collections.IEnumerable defs) continue;
            foreach (var raw in defs)
            {
                var item = raw;
                var it = item?.GetType();
                if (it != null && it.IsGenericType && it.Name.StartsWith("KeyValuePair"))
                {
                    item = it.GetProperty("Value")?.GetValue(item);
                    it = item?.GetType();
                }
                if (item != null && it != null && it.Name.Contains("DefinitionLoadingData"))
                {
                    object inner = null;
                    try
                    {
                        inner = it.GetProperty("Definition", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(item)
                             ?? it.GetField("Definition", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(item);
                    }
                    catch { }
                    if (inner == null && _loadingDataDumps++ < 1)
                        ProbeLog.Line("DefinitionLoadingData members: " + string.Join(", ",
                            it.GetProperties().Select(p => "prop:" + p.Name).Concat(it.GetFields().Select(f => "field:" + f.Name)).Take(15)));
                    item = inner ?? item;
                }
                if (item != null)
                    try { perDefinition(item); } catch { }
            }
        }
    }

    private static object Unwrap(object taskLike)
    {
        var rt = taskLike.GetType();
        if (_taskDumps++ < 1)
        {
            var members = rt.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => m.DeclaringType != typeof(object)).Select(m => m.Name)
                .Concat(rt.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => "prop:" + p.Name))
                .Distinct().Take(20);
            ProbeLog.Line($"DefinitionTools: unwrapping {rt.FullName}: {string.Join(", ", members)}");
        }
        try
        {
            var awaiter = rt.GetMethod("GetAwaiter", Type.EmptyTypes)?.Invoke(taskLike, null);
            var getResult = awaiter?.GetType().GetMethod("GetResult", Type.EmptyTypes);
            if (getResult != null)
            {
                var v = getResult.Invoke(awaiter, null);
                if (v != null) return v;
            }
        }
        catch (Exception e) { ProbeLog.Line("DefinitionTools: awaiter unwrap failed: " + (e.InnerException?.Message ?? e.Message)); }
        foreach (var name in new[] { "Result", "Value" })
        {
            try
            {
                var v = rt.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(taskLike);
                if (v != null) return v;
            }
            catch { }
        }
        return null;
    }
}
