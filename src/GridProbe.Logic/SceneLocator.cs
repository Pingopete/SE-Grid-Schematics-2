using System.Reflection;
using Keen.VRage.Core;
using Keen.VRage.Core.Game.Systems;
using Keen.VRage.DCS.Scenes;

namespace GridProbe;

internal static class SceneLocator
{
    public static volatile Session LastSession;
    public static readonly System.Collections.Concurrent.ConcurrentDictionary<Session, string> Sessions = new();
    private static bool _reconDumped;
    private static bool _coreSeen;

    public static Scene TryFindSessionScene()
    {
        VRageCore core;
        try { core = VRageCore.Instance; }
        catch (Exception e) { ProbeLog.Error("VRageCore.Instance", e); return null; }
        if (core == null) return null;
        if (!_coreSeen) { _coreSeen = true; ProbeLog.Line($"VRageCore.Instance live: app '{core.ApplicationName}', frame {core.FrameCounter}"); }

        var engine = core.Engine;
        if (engine == null) return null;

        Scene sessionScene = null;
        var recon = _reconDumped ? null : new System.Text.StringBuilder();
        try
        {
            engine.ForEach<Keen.VRage.DCS.Components.Component>(c =>
            {
                var t = c.GetType();
                recon?.AppendLine($"  engine component: {t.FullName}");
                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    try
                    {
                        if (typeof(Session).IsAssignableFrom(p.PropertyType))
                        {
                            if (p.GetValue(c) is Session s && s.Scene != null)
                            {
                                if (Sessions.TryAdd(s, t.Name + "." + p.Name))
                                    ProbeLog.Line($"Session found via {t.Name}.{p.Name}");
                                LastSession = s;
                                if (sessionScene == null) sessionScene = s.Scene;
                            }
                        }
                        else if (typeof(System.Collections.IEnumerable).IsAssignableFrom(p.PropertyType)
                                 && p.PropertyType.IsGenericType
                                 && p.PropertyType.GetGenericArguments().Any(a => typeof(Session).IsAssignableFrom(a)))
                        {
                            if (p.GetValue(c) is System.Collections.IEnumerable seq)
                                foreach (var item in seq)
                                    if (item is Session s2 && s2.Scene != null)
                                    {
                                        if (!_reconDumped) ProbeLog.Line($"Session found via {t.Name}.{p.Name}[]");
                                        sessionScene = s2.Scene;
                                        return;
                                    }
                        }
                    }
                    catch { }
                }
            }, reverse: false);
        }
        catch (Exception e) { ProbeLog.Error("engine component walk", e); }

        if (recon != null && recon.Length > 0)
        {
            _reconDumped = true;
            ProbeLog.Line("Engine component recon:\n" + recon);
        }
        return sessionScene;
    }
}
