using Mono.Cecil;

static class IlDump
{
    // Scan Game2.Client for every method that touches LcdPanelSurfaceContext.ContentDirty
    // or calls LcdContentRendererSessionComponent.Render — finds the repaint scheduler.
    public static void FindLcdScheduler()
    {
        var dll = @"D:\SteamLibrary\steamapps\common\SpaceEngineers2\Game2\Game2.Client.dll";
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(dll));
        var asm = AssemblyDefinition.ReadAssembly(dll, new ReaderParameters { AssemblyResolver = resolver });
        foreach (var type in asm.MainModule.GetTypes())
        {
            foreach (var m in type.Methods.Where(m => m.HasBody))
            {
                bool hits = false;
                foreach (var ins in m.Body.Instructions)
                {
                    var op = ins.Operand?.ToString() ?? "";
                    if (op.Contains("ContentDirty") || (op.Contains("LcdContentRendererSessionComponent::Render") && !m.DeclaringType.Name.Contains("LcdContentRenderer")))
                    { hits = true; break; }
                }
                if (hits)
                {
                    Console.WriteLine($"=== {m.FullName}");
                    foreach (var ins in m.Body.Instructions)
                    {
                        var op = ins.Operand?.ToString() ?? "";
                        if (op.Contains("ContentDirty") || op.Contains("::Render") || op.Contains("RenderTarget") || ins.OpCode.Name.StartsWith("call"))
                            Console.WriteLine($"  {ins.OpCode.Name,-12} {op}");
                    }
                    Console.WriteLine();
                }
            }
        }
    }

    // List every method of LcdPanelSurfaceRenderComponent, and every Game2.Client
    // method that calls into its rebuild/transition entry points.
    public static void LcdComponentMap()
    {
        var dll = @"D:\SteamLibrary\steamapps\common\SpaceEngineers2\Game2\Game2.Client.dll";
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(dll));
        var asm = AssemblyDefinition.ReadAssembly(dll, new ReaderParameters { AssemblyResolver = resolver });
        var comp = asm.MainModule.GetType("Keen.Game2.Client.WorldObjects.CubeBlocks.Render.Lcd.LcdPanelSurfaceRenderComponent");
        Console.WriteLine("== LcdPanelSurfaceRenderComponent methods:");
        foreach (var m in comp.Methods)
            Console.WriteLine($"  {(m.IsPublic ? "pub " : "npub")} {m.Name}({string.Join(", ", m.Parameters.Select(p => p.ParameterType.Name))})");
        Console.WriteLine();
        Console.WriteLine("== Callers of rebuild/transition entry points:");
        var wanted = new[] { "RebuildSurfaceContent", "ApplyDesiredState", "OnSurfaceStatesChanged", "MarkAllDirtyAndRebuild", "TransitionToCustomRender" };
        foreach (var type in asm.MainModule.GetTypes())
            foreach (var m in type.Methods.Where(m => m.HasBody))
                foreach (var ins in m.Body.Instructions)
                {
                    if (ins.Operand is MethodReference mr && mr.DeclaringType.Name == "LcdPanelSurfaceRenderComponent" && wanted.Contains(mr.Name))
                        Console.WriteLine($"  {m.FullName}  ->  {mr.Name}");
                }
    }

    // Route A gate: how does the UI recorder classify texture handles for DrawImage?
    // If generated (render-target) handles bypass the content-cache metadata path,
    // RT-to-RT DrawImage is safe; if not, it throws on the render thread.
    public static void GraphicsTypeIl()
    {
        var dll = @"D:\SteamLibrary\steamapps\common\SpaceEngineers2\Game2\VRage.Render12.dll";
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(dll));
        var asm = AssemblyDefinition.ReadAssembly(dll, new ReaderParameters { AssemblyResolver = resolver });
        foreach (var type in asm.MainModule.GetTypes())
        {
            if (!type.FullName.Contains("UISystemComponent")) continue;
            foreach (var m in type.Methods.Where(m => m.HasBody && (m.Name == "TryExtractGraphicsType" || m.Name.Contains("DrawImage"))))
            {
                Console.WriteLine($"=== {m.FullName}");
                foreach (var ins in m.Body.Instructions)
                    Console.WriteLine($"  {ins.OpCode.Name,-12} {ins.Operand}");
                Console.WriteLine();
            }
        }
    }

    // Who reads OffscreenRenderTarget.TextureHandle, and does any of it flow into DrawImage?
    public static void RtHandleUsage()
    {
        foreach (var dllName in new[] { "VRage.Render12.dll", "Game2.Client.dll", "VRage.Render.dll", "Game2.Game.dll" })
        {
            var dll = Path.Combine(@"D:\SteamLibrary\steamapps\common\SpaceEngineers2\Game2", dllName);
            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(Path.GetDirectoryName(dll));
            var asm = AssemblyDefinition.ReadAssembly(dll, new ReaderParameters { AssemblyResolver = resolver });
            foreach (var type in asm.MainModule.GetTypes())
                foreach (var m in type.Methods.Where(m => m.HasBody))
                {
                    bool readsHandle = m.Body.Instructions.Any(i => i.Operand is MethodReference mr && mr.Name == "get_TextureHandle" && mr.DeclaringType.Name.Contains("OffscreenRenderTarget"));
                    if (!readsHandle) continue;
                    bool draws = m.Body.Instructions.Any(i => i.Operand is MethodReference mr && mr.Name.StartsWith("DrawImage"));
                    Console.WriteLine($"{dllName}: {m.DeclaringType.Name}.{m.Name}  drawsImage={draws}");
                }
        }
    }

    // Who holds an IPhysics reference (field/property) — finds the acquisition path.
    public static void FindPhysicsOwner()
    {
        foreach (var dllName in new[] { "VRage.Core.Game.dll", "Game2.Simulation.dll", "VRage.Physics.dll" })
        {
            var dll = Path.Combine(@"D:\SteamLibrary\steamapps\common\SpaceEngineers2\Game2", dllName);
            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(Path.GetDirectoryName(dll));
            var asm = AssemblyDefinition.ReadAssembly(dll, new ReaderParameters { AssemblyResolver = resolver });
            foreach (var type in asm.MainModule.GetTypes())
            {
                foreach (var f in type.Fields)
                    if (f.FieldType.Name == "IPhysics")
                        Console.WriteLine($"{dllName}: field  {type.FullName}.{f.Name} {(f.IsPublic ? "pub" : "npub")}{(f.IsStatic ? " static" : "")}");
                foreach (var p in type.Properties)
                    if (p.PropertyType.Name == "IPhysics")
                        Console.WriteLine($"{dllName}: prop   {type.FullName}.{p.Name}");
            }
        }
    }

    public static void Run()
    {
        var core = @"D:\SteamLibrary\steamapps\common\SpaceEngineers2\Game2\VRage.Core.dll";
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(core));
        var asm = AssemblyDefinition.ReadAssembly(core, new ReaderParameters { AssemblyResolver = resolver });
        var host = asm.MainModule.GetType("Keen.VRage.Core.Plugins.PluginHost");
        var sb = new System.Text.StringBuilder();
        foreach (var name in new[] { "Add", "TryAddFromAssembly", "LoadPluginsFromArgs", "LoadPlugins", "PostEngineInit", "InvokeOnBeforeEngineInstantiated", "InvokeOnBeforeProjectsLoaded", ".ctor" })
        {
            foreach (var m in host.Methods.Where(m => m.Name == name && m.HasBody))
            {
                sb.AppendLine($"=== {m.FullName}");
                foreach (var ins in m.Body.Instructions)
                    sb.AppendLine($"  {ins.OpCode.Name,-12} {ins.Operand}");
                sb.AppendLine();
            }
        }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "pluginhost-il.txt"), sb.ToString());
        Console.WriteLine("pluginhost-il.txt written.");
    }
}
