using System.Reflection;

// Offline capability survey: loads the game assemblies and reports the block
// DEFINITION and COMPONENT type hierarchies, so system membership can be decided
// from types rather than from block names.
//   dotnet run -- types [filter]
internal static class TypeScan
{
    public static int Run(string[] args)
    {
        string filter = args.Length > 1 ? args[1].ToLowerInvariant() : null;
        string dir = @"D:\SteamLibrary\steamapps\common\SpaceEngineers2\Game2";

        var asmNames = new[]
        {
            "Game2.Simulation.dll", "Game2.Game.dll", "VRage.Core.Game.dll",
            "VRage.Game.dll", "Game2.Client.dll", "VRage.Core.dll",
        };

        foreach (var name in asmNames)
        {
            string path = Path.Combine(dir, name);
            if (!File.Exists(path)) { Console.WriteLine($"-- missing {name}"); continue; }
            Assembly asm;
            try { asm = Assembly.LoadFrom(path); }
            catch (Exception e) { Console.WriteLine($"-- {name}: {e.GetType().Name}"); continue; }

            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }

            foreach (var t in types)
            {
                string n = t.Name;
                if (filter != null)
                {
                    // An explicit term is authoritative: search everything by it,
                    // including the namespace, so families can be found by where
                    // they live as well as what they are called.
                    if (!(t.FullName ?? n).ToLowerInvariant().Contains(filter)) continue;
                }
                else if (!(n.EndsWith("BlockDefinition") || n.Contains("Conveyor")))
                    continue;

                var bases = new List<string>();
                for (var b = t.BaseType; b != null && b != typeof(object); b = b.BaseType) bases.Add(b.Name);
                var ifaces = t.GetInterfaces().Select(i => i.Name).Take(6).ToArray();

                Console.WriteLine($"{(t.IsPublic ? "pub " : "int ")}{t.FullName}");
                if (bases.Count > 0) Console.WriteLine($"      : {string.Join(" > ", bases)}");
                if (ifaces.Length > 0) Console.WriteLine($"      [{string.Join(", ", ifaces)}]");

                if (t.IsEnum)
                    Console.WriteLine($"      values: {string.Join(", ", Enum.GetNames(t))}");

                // Public instance members that could confirm the capability.
                var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Select(p => $"{p.PropertyType.Name} {p.Name}").Take(12).ToArray();
                if (props.Length > 0) Console.WriteLine($"      props: {string.Join(", ", props)}");
            }
        }
        return 0;
    }
}
