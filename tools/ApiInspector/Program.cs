using System.Reflection;
using System.Runtime.InteropServices;

if (args.Length > 0 && args[0] == "il") { IlDump.Run(); return; }
if (args.Length > 0 && args[0] == "lcd") { IlDump.FindLcdScheduler(); return; }
if (args.Length > 0 && args[0] == "lcdmap") { IlDump.LcdComponentMap(); return; }
if (args.Length > 0 && args[0] == "gfxtype") { IlDump.GraphicsTypeIl(); return; }
if (args.Length > 0 && args[0] == "rtusage") { IlDump.RtHandleUsage(); return; }
if (args.Length > 0 && args[0] == "physowner") { IlDump.FindPhysicsOwner(); return; }

var gameDir = @"D:\SteamLibrary\steamapps\common\SpaceEngineers2\Game2";
var runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();

var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
foreach (var f in Directory.GetFiles(runtimeDir, "*.dll")) paths.TryAdd(Path.GetFileNameWithoutExtension(f), f);
foreach (var f in Directory.GetFiles(gameDir, "*.dll")) paths[Path.GetFileNameWithoutExtension(f)] = f;

var mlc = new MetadataLoadContext(new PathAssemblyResolver(paths.Values));

(string asm, string type)[] targets =
{
    ("Game2.Simulation", "Keen.Game2.Simulation.WorldObjects.CubeGrids.CubeGridComponent"),
    ("Game2.Simulation", "Keen.Game2.Simulation.WorldObjects.CubeBlocks.CubeBlockComponent"),
    ("Game2.Simulation", "Keen.Game2.Simulation.WorldObjects.CubeGrids.BlockOctrees.BlockOctreeComponent"),
    ("Game2.Simulation", "Keen.Game2.Simulation.WorldObjects.CubeBlocks.Lcd.LcdMultiPanelComponent"),
    ("Game2.Simulation", "Keen.Game2.Simulation.Utils.PhysicsExtensions"),
    ("VRage.Physics", "Keen.VRage.Physics.IPhysics"),
    ("VRage.DCS", "Keen.VRage.DCS.Scenes.Scene"),
    ("VRage.DCS", "Keen.VRage.DCS.Components.Component"),
    ("VRage.Core", "Keen.VRage.Core.Plugins.IPlugin"),
    ("VRage.Core", "Keen.VRage.Core.Plugins.PluginHost"),
    ("VRage.Core.Game", "Keen.VRage.Core.Game.Components.SessionComponent"),
    ("VRage.Core.Game", "Keen.VRage.Core.Game.GameSystems.Queries.RayCastArgs"),
    ("Game2.Client", "Keen.Game2.Client.GameSystems.CameraSystems.CameraComponent"),
    ("Game2.Client", "Keen.Game2.Client.WorldObjects.CubeBlocks.Render.Lcd.LcdContentRendererSessionComponent"),
};

foreach (var (asmName, typeName) in targets)
{
    try
    {
        var asm = mlc.LoadFromAssemblyPath(paths[asmName]);
        var t = asm.GetType(typeName, throwOnError: false);
        if (t == null) { Console.WriteLine($"MISSING  {typeName}"); continue; }
        var vis = t.IsPublic || t.IsNestedPublic ? "PUBLIC  " : "internal";
        var pubMethods = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Length;
        var pubProps = t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Length;
        Console.WriteLine($"{vis} {typeName}  (pub methods: {pubMethods}, pub props: {pubProps})");
        if (typeName.EndsWith("IPlugin") || typeName.EndsWith("PluginHost") || typeName.EndsWith("LcdContentRendererSessionComponent"))
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                Console.WriteLine($"    {(m.IsPublic ? "pub" : "npub")} {m}");
    }
    catch (Exception e) { Console.WriteLine($"ERROR    {typeName}: {e.Message}"); }
}

var dump = new System.Text.StringBuilder();
(string asm, string type)[] dumpTargets =
{
    ("VRage.DCS", "Keen.VRage.DCS.Scenes.Scene"),
    ("VRage.DCS", "Keen.VRage.DCS.Components.Entity"),
    ("VRage.DCS", "Keen.VRage.DCS.Components.Component"),
    ("Game2.Simulation", "Keen.Game2.Simulation.WorldObjects.CubeGrids.CubeGridComponent"),
    ("Game2.Simulation", "Keen.Game2.Simulation.WorldObjects.CubeBlocks.CubeBlockComponent"),
    ("Game2.Simulation", "Keen.Game2.Simulation.WorldObjects.CubeGrids.BlockOctrees.BlockOctreeComponent"),
    ("Game2.Simulation", "Keen.Game2.Simulation.WorldObjects.CubeBlocks.Lcd.LcdMultiPanelComponent"),
    ("Game2.Simulation", "Keen.Game2.Simulation.WorldObjects.CubeBlocks.Lcd.LcdPanelSurfaceState"),
    ("VRage.Physics", "Keen.VRage.Physics.IPhysics"),
    ("VRage.Core.Game", "Keen.VRage.Core.Game.Components.SessionComponent"),
    ("VRage.Core.Game", "Keen.VRage.Core.Game.Components.SessionComponentFunctions"),
    ("VRage.Core", "Keen.VRage.Core.Plugins.PluginHost"),
    ("VRage.Render", "Keen.VRage.Render.Contracts.IDrawBatch"),
    ("VRage.Library", "Keen.VRage.Library.Mathematics.QuadraticBezier2"),
    ("VRage.Library", "Keen.VRage.Library.Mathematics.ColorSRGB"),
    ("VRage.Render", "Keen.VRage.Render.Contracts.Font"),
    ("Game2.Client", "Keen.Game2.Client.WorldObjects.CubeBlocks.Render.Lcd.LcdContentRendererSessionComponent+ContentRenderCache"),
    ("VRage.Library", "Keen.VRage.Library.Utils.ResourceHandle`1"),
    ("VRage.Core", "Keen.VRage.Core.Render.GUIAsset"),
    ("VRage.Core", "Keen.VRage.Core.Render.TextureAsset"),
    ("VRage.Core", "Keen.VRage.Core.Render.FontAsset"),
    ("Game2.Client", "Keen.Game2.Client.WorldObjects.CubeBlocks.Render.Lcd.LcdPanelSurfaceContext"),
    ("Game2.Client", "Keen.Game2.Client.WorldObjects.CubeBlocks.Render.Lcd.LcdPanelSurfaceRenderComponent"),
    ("Game2.Simulation", "Keen.Game2.Simulation.GameSystems.EntityNameSessionComponent"),
    ("Game2.Simulation", "Keen.Game2.Simulation.GameSystems.NamedEntity"),
    ("VRage.Core", "Keen.VRage.Core.VRageCore"),
    ("VRage.Core.Game", "Keen.VRage.Core.Game.Systems.Session"),
    ("VRage.Library", "Keen.VRage.Library.Utils.Singleton`1"),
    ("VRage.DCS", "Keen.VRage.DCS.Accessors.DEntityContext"),
    ("VRage.DCS", "Keen.VRage.DCS.Accessors.DEntity"),
    ("VRage.DCS", "Keen.VRage.DCS.Components.EntityFunctions"),
    ("VRage.Library", "Keen.VRage.Library.Filesystem.FileHandle"),
    ("VRage.Library", "Keen.VRage.Library.Filesystem.RootPath"),
    ("VRage.Library", "Keen.VRage.Library.Utils.ResourceHandle"),
    ("VRage.Library", "Keen.VRage.Library.Filesystem.ContentCache.ContentCache"),
    ("VRage.Library", "Keen.VRage.Library.Filesystem.IFileSystem"),
    ("Game2.Client", "Keen.Game2.Client.WorldObjects.CubeBlocks.Render.Lcd.LcdContentRendererSessionComponent"),
    ("Game2.Client", "Keen.Game2.Client.GameSystems.CameraSystems.CameraSystemComponent"),
    ("Game2.Client", "Keen.Game2.Client.GameSystems.CameraSystems.ICameraSystem"),
    ("Game2.Client", "Keen.Game2.Client.GameSystems.CameraSystems.CameraComponent"),
    ("Game2.Client", "Keen.Game2.Client.GameSystems.CameraSystems.CameraData"),
    ("Game2.Simulation", "Keen.Game2.Simulation.Utils.PhysicsExtensions"),
    ("VRage.Core.Game", "Keen.VRage.Core.Game.GameSystems.Queries.RayCastArgs"),
    ("VRage.Core", "Keen.VRage.Core.WorldTransform"),
    ("VRage.Render", "Keen.VRage.Render.Data.GradientFillData"),
    ("VRage.Render", "Keen.VRage.Render.Data.GradientType"),
    ("VRage.Render", "Keen.VRage.Render.Contracts.OffscreenRenderTarget"),
    ("VRage.Render", "Keen.VRage.Render.Contracts.UISystem"),
    ("VRage.Render", "Keen.VRage.Render.Contracts.ImmediateDrawBatch"),
    ("VRage.Render", "Keen.VRage.Render.Contracts.RenderContracts"),
    ("Game2.Client", "Keen.Game2.Client.WorldObjects.CubeBlocks.Render.Lcd.LcdRenderTargetPoolSessionComponent"),
    ("VRage.Physics", "Keen.VRage.Physics.CollisionPreset"),
    ("VRage.Physics", "Keen.VRage.Physics.Queries.SweepQueryHit"),
    ("VRage.Physics", "Keen.VRage.Physics.Queries.NearestQueryHit"),
    ("VRage.Library", "Keen.VRage.Library.Collections.BufferReference`1"),
    ("VRage.Library", "Keen.VRage.Library.Collections.Buffer`1"),
    ("VRage.Core.Game", "Keen.VRage.Core.Game.Systems.Session"),
};
foreach (var (asmName, typeName) in dumpTargets)
{
    try
    {
        var asm = mlc.LoadFromAssemblyPath(paths[asmName]);
        var t = asm.GetType(typeName, false);
        if (t == null) { dump.AppendLine($"== MISSING {typeName}"); continue; }
        dump.AppendLine($"== {typeName} (base: {t.BaseType?.FullName})");
        foreach (var c in t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            dump.AppendLine($"  ctor({string.Join(", ", c.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))}) {(c.IsPublic ? "" : "[nonpublic]")}");
        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            dump.AppendLine($"  prop {p.PropertyType.Name} {p.Name}");
        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Where(m => !m.IsSpecialName && m.DeclaringType?.FullName?.StartsWith("System") != true))
            dump.AppendLine($"  {(m.IsStatic ? "static " : "")}{m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))})");
        dump.AppendLine();
    }
    catch (Exception e) { dump.AppendLine($"== ERROR {typeName}: {e.Message}"); }
}
try
{
    var core = mlc.LoadFromAssemblyPath(paths["VRage.Core"]);
    dump.AppendLine("== All types in Keen.VRage.Core.Plugins namespace:");
    foreach (var t in core.GetTypes().Where(t => t.Namespace == "Keen.VRage.Core.Plugins"))
        dump.AppendLine($"  {(t.IsPublic ? "pub" : "int")} {(t.IsInterface ? "interface" : t.IsAbstract ? "abstract class" : "class")} {t.Name}");
}
catch (Exception e) { dump.AppendLine($"plugins ns error: {e.Message}"); }
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "api-notes.txt"), dump.ToString());
Console.WriteLine("api-notes.txt written.");

Console.WriteLine();
foreach (var asmName in new[] { "Game2.Simulation", "Game2.Client", "VRage.DCS", "VRage.Physics", "VRage.Core" })
{
    try
    {
        var asm = mlc.LoadFromAssemblyPath(paths[asmName]);
        int pub = 0, npub = 0;
        foreach (var t in asm.GetTypes()) { if (t.IsPublic) pub++; else if (!t.IsNested) npub++; }
        Console.WriteLine($"{asmName}: {pub} public / {npub} internal top-level types");
    }
    catch (Exception e) { Console.WriteLine($"{asmName}: enumeration error: {e.GetType().Name} {e.Message}"); }
}
