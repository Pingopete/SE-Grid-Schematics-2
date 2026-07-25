using System.Reflection;
using System.Runtime.Loader;
using Keen.VRage.Core.Plugins;

namespace GridProbe;

public static class ProbeBridge
{
    public static volatile Action<object, object> LcdRenderHook;
    public static volatile Action<object> LcdSurfaceDefHook;
    public static volatile Action<object> LcdTickHook;
    public static volatile bool SuppressHighlights;
}

public sealed class ProbePlugin : IPlugin
{
    private const string LogicPath = @"D:\SE2Probe\GridProbe.Logic.dll";
    private const string LogPath = @"D:\Projects\Space Engineers Stuff\Grid Schematics Mod\SE2Probe\output\probe.log";

    private AssemblyLoadContext _logicContext;
    private MethodInfo _tick;
    private DateTime _loadedStamp;

    public ProbePlugin(PluginHost host)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
        File.WriteAllText(LogPath, $"=== GridProbe bootstrap {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}");
        Log("Bootstrap constructed with PluginHost. Hot-reload watching " + LogicPath);
        ApplyLcdRenderPatch();
        var worker = new Thread(WorkerLoop) { IsBackground = true, Name = "GridProbeBootstrap" };
        worker.Start();
    }

    private static void ApplyLcdRenderPatch()
    {
        try
        {
            var target = Type.GetType("Keen.Game2.Client.WorldObjects.CubeBlocks.Render.Lcd.LcdContentRendererSessionComponent, Game2.Client");
            var render = target?.GetMethod("Render", BindingFlags.Public | BindingFlags.Instance);
            if (render == null) { Log("LCD render patch: target method not found."); return; }
            var harmony = new HarmonyLib.Harmony("gridprobe.bootstrap");
            var postfix = typeof(ProbePlugin).GetMethod(nameof(LcdRenderPostfix), BindingFlags.Static | BindingFlags.NonPublic);
            harmony.Patch(render, postfix: new HarmonyLib.HarmonyMethod(postfix));
            Log("LCD render patch applied (postfix on LcdContentRendererSessionComponent.Render).");

            var ctxType = Type.GetType("Keen.Game2.Client.WorldObjects.CubeBlocks.Render.Lcd.LcdPanelSurfaceContext, Game2.Client");
            var ctor = ctxType?.GetConstructors().FirstOrDefault(c => c.GetParameters().Length == 2);
            if (ctor != null)
            {
                var pre = typeof(ProbePlugin).GetMethod(nameof(SurfaceCtorPrefix), BindingFlags.Static | BindingFlags.NonPublic);
                harmony.Patch(ctor, prefix: new HarmonyLib.HarmonyMethod(pre));
                Log("LCD surface ctor patch applied (definition boost point).");
            }
            else Log("LCD surface ctor not found for patching.");

            var rcType = Type.GetType("Keen.Game2.Client.WorldObjects.CubeBlocks.Render.Lcd.LcdPanelSurfaceRenderComponent, Game2.Client");
            var tick = rcType?.GetMethod("TickFsrMask", BindingFlags.NonPublic | BindingFlags.Instance);
            if (tick != null)
            {
                var tp = typeof(ProbePlugin).GetMethod(nameof(LcdTickPostfix), BindingFlags.Static | BindingFlags.NonPublic);
                harmony.Patch(tick, postfix: new HarmonyLib.HarmonyMethod(tp));
                Log("LCD tick patch applied (TickFsrMask per-frame hook).");
            }
            else Log("TickFsrMask not found for patching.");

            var mes = Type.GetType("Keen.VRage.Render.Contracts.MeshEffectSystem, VRage.Render");
            var ch = mes?.GetMethods(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(m => m.Name == "CreateHighlight");
            if (ch != null)
            {
                var hp = typeof(ProbePlugin).GetMethod(nameof(HighlightPrefix), BindingFlags.Static | BindingFlags.NonPublic);
                harmony.Patch(ch, prefix: new HarmonyLib.HarmonyMethod(hp));
                Log("Highlight suppression patch applied (MeshEffectSystem.CreateHighlight).");
            }
            else Log("CreateHighlight not found for patching.");
        }
        catch (Exception e) { Log("LCD render patch FAILED: " + e.Message); }
    }

    private static void LcdRenderPostfix(object __0, object __1)
    {
        try { ProbeBridge.LcdRenderHook?.Invoke(__0, __1); } catch { }
    }

    private static void SurfaceCtorPrefix(object __1)
    {
        try { ProbeBridge.LcdSurfaceDefHook?.Invoke(__1); } catch { }
    }

    private static void LcdTickPostfix(object __instance)
    {
        try { ProbeBridge.LcdTickHook?.Invoke(__instance); } catch { }
    }

    private static bool HighlightPrefix()
    {
        return !ProbeBridge.SuppressHighlights;
    }

    public ProbePlugin() : this(null) { }

    private int _tickBusy;
    private long _tickStartedAt;

    private void WorkerLoop()
    {
        Thread.Sleep(8000);
        while (true)
        {
            try
            {
                // Reload always runs, even if a tick wedges: ticks execute on the
                // thread pool behind a busy flag so a stuck tick can never block
                // hot reload (a wedged old tick just makes new ticks skip).
                ReloadLogicIfChanged();
                var tick = _tick;
                if (tick != null && Interlocked.CompareExchange(ref _tickBusy, 1, 0) == 0)
                {
                    _tickStartedAt = Environment.TickCount64;
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try { tick.Invoke(null, null); }
                        catch (Exception e)
                        {
                            Log($"ERROR tick: {e.InnerException?.Message ?? e.Message}\n{e.InnerException?.StackTrace ?? e.StackTrace}");
                        }
                        finally { Interlocked.Exchange(ref _tickBusy, 0); }
                    });
                }
                else if (tick != null && Environment.TickCount64 - _tickStartedAt > 30000)
                {
                    Log("WARNING: logic tick has been running >30s — likely wedged; reload stays alive, ticks skipped.");
                    _tickStartedAt = Environment.TickCount64;
                }
            }
            catch (Exception e)
            {
                Log($"ERROR worker: {e.Message}");
            }
            Thread.Sleep(2000);
        }
    }

    private void ReloadLogicIfChanged()
    {
        if (!File.Exists(LogicPath))
        {
            if (_tick == null) Log("Waiting for logic dll to appear...");
            return;
        }
        var stamp = File.GetLastWriteTimeUtc(LogicPath);
        if (_tick != null && stamp == _loadedStamp) return;

        try
        {
            var old = _logicContext;
            var ctx = new AssemblyLoadContext("GridProbeLogic_" + stamp.Ticks, isCollectible: true);
            Assembly asm;
            using (var ms = new MemoryStream(File.ReadAllBytes(LogicPath)))
            {
                var pdbPath = Path.ChangeExtension(LogicPath, ".pdb");
                if (File.Exists(pdbPath))
                {
                    using var pdb = new MemoryStream(File.ReadAllBytes(pdbPath));
                    asm = ctx.LoadFromStream(ms, pdb);
                }
                else asm = ctx.LoadFromStream(ms);
            }
            var entry = asm.GetType("GridProbe.LogicEntry");
            var tick = entry?.GetMethod("Tick", BindingFlags.Public | BindingFlags.Static);
            if (tick == null)
            {
                Log("Logic dll loaded but GridProbe.LogicEntry.Tick not found — keeping previous logic.");
                ctx.Unload();
                return;
            }
            _logicContext = ctx;
            _tick = tick;
            _loadedStamp = stamp;
            Log($"Logic loaded (build stamp {stamp:HH:mm:ss}). Hot-reload active.");
            old?.Unload();
        }
        catch (Exception e)
        {
            Log($"ERROR loading logic dll: {e.Message} — keeping previous logic.");
        }
    }

    private static readonly object LogGate = new();
    private static void Log(string msg)
    {
        try { lock (LogGate) File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] [boot] {msg}{Environment.NewLine}"); } catch { }
    }
}
