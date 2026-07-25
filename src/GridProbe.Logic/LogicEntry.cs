namespace GridProbe;

public static class LogicEntry
{
    public static string Build => typeof(LogicEntry).Assembly.GetName().Version?.ToString() ?? "?";

    static LogicEntry()
    {
        try
        {
            var bridge = Type.GetType("GridProbe.ProbeBridge, GridProbe");
            var field = bridge?.GetField("LcdRenderHook");
            if (field != null)
            {
                field.SetValue(null, (Action<object, object>)VectorLcd.OnRender);
                bridge.GetField("LcdSurfaceDefHook")?.SetValue(null, (Action<object>)(def => ResolutionBooster.BoostSurfaceObject(def)));
                bridge.GetField("LcdTickHook")?.SetValue(null, (Action<object>)VectorLcd.OnLcdTick);
                bridge.GetField("SuppressHighlights")?.SetValue(null, true);
                ProbeLog.Line("Vector render + surface def hooks registered.");
            }
            else ProbeLog.Line("ProbeBridge not found — bootstrap too old? Restart game to adopt new bootstrap.");
        }
        catch (Exception e) { ProbeLog.Error("bridge hookup", e); }
    }

    public static void Tick()
    {
        var scene = SceneLocator.TryFindSessionScene();
        if (scene != null) ProbeRunner.RunOnce(scene);
    }
}
