using System.Reflection;
using System.Runtime.Loader;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Modding;
using HeroesNotIncluded.Sim.Work;
using Narsil.Mods;

namespace HeroesNotIncluded.Mods;

/// <summary>
/// Loads the assemblies a mod ships (the <c>*.dll</c> files at its folder's top level) and sorts them
/// into the sim half and the presentation half. A sim half may reference only what the game's sim
/// does (its own sim, Narsil.Sim, BRUTAL's numerics, the runtime): that's what keeps a lockstep game
/// deterministic, so an assembly with sim types (<see cref="ISimMod"/>, sessions, commands) that
/// reaches for the engine, a window or the game is refused. Each mod loads in its own context, its own
/// extra dependencies from its folder, the game's assemblies shared. Loaded assemblies stay loaded:
/// turning a code mod off stops it registering; a rebuilt DLL loads afresh in a new context.
/// </summary>
public static class ModCode
{
    public sealed record Halves(IReadOnlyList<(string ModId, Assembly Assembly)> Sim, IReadOnlyList<(string ModId, Assembly Assembly)> Game);

    private sealed class ModLoadContext(string directory, string modId) : AssemblyLoadContext($"mod:{modId}")
    {
        protected override Assembly? Load(AssemblyName name)
        {
            // The game's own assemblies (and the runtime's) come from the game, so types are shared.
            if (Default.Assemblies.Any(a => a.GetName().Name == name.Name))
                return null;
            var path = Path.Combine(directory, name.Name + ".dll");
            return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
        }
    }

    private static readonly Dictionary<string, Assembly> Loaded = new(StringComparer.Ordinal);

    /// <summary>The assemblies a sim half may reference: what the game's own sim does.</summary>
    private static readonly HashSet<string> SimSafe = SimReferences();

    private static HashSet<string> SimReferences()
    {
        var safe = new HashSet<string>(StringComparer.Ordinal) { typeof(Simulation).Assembly.GetName().Name! };
        var pending = new Queue<Assembly>([typeof(Simulation).Assembly]);
        while (pending.TryDequeue(out var asm))
            foreach (var r in asm.GetReferencedAssemblies())
                if (safe.Add(r.Name!) && AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == r.Name) is { } found && !IsRuntime(r.Name!))
                    pending.Enqueue(found);
        return safe;
    }

    private static bool IsRuntime(string name) => name is "netstandard" or "mscorlib" || name.StartsWith("System", StringComparison.Ordinal) || name.StartsWith("Microsoft.", StringComparison.Ordinal);

    /// <summary>
    /// Every code mod's assemblies, in load order, sorted into halves; a mod whose code won't load is
    /// named in <paramref name="problems"/> and its id in <paramref name="failed"/> (it should be left out).
    /// </summary>
    public static Halves Load(ModOrder order, List<string> problems, HashSet<string> failed)
    {
        var sim = new List<(string, Assembly)>();
        var game = new List<(string, Assembly)>();
        foreach (var mod in order.Mods)
        {
            // A copy of one of the engine's assemblies (a build's habit of copying references) is the game's, not the mod's.
            var dlls = Directory.Exists(mod.Directory)
                ? Directory.GetFiles(mod.Directory, "*.dll").Order(StringComparer.Ordinal).Where(f => !IsEngine(Path.GetFileNameWithoutExtension(f))).ToList()
                : [];
            if (dlls.Count == 0)
                continue;
            try
            {
                var context = new ModLoadContext(mod.Directory, mod.Id);
                foreach (var dll in dlls)
                {
                    // A mod the game ships and references itself (its own trades, until their views move out too)
                    // is already the game's: the same assembly, not a second copy.
                    var name = Path.GetFileNameWithoutExtension(dll);
                    var asm = IsGames(name) ? AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName(name)) : LoadOnce(context, dll);
                    var unsafeRefs = asm.GetReferencedAssemblies().Select(r => r.Name!).Where(n => !IsRuntime(n) && !SimSafe.Contains(n)).ToList();
                    var simTypes = asm.GetExportedTypes().Where(t => t is { IsAbstract: false, IsInterface: false } && (typeof(ISimMod).IsAssignableFrom(t) || typeof(WorkSession).IsAssignableFrom(t) || typeof(ISimCommand).IsAssignableFrom(t))).ToList();
                    if (unsafeRefs.Count == 0)
                        sim.Add((mod.Id, asm));
                    else if (simTypes.Count > 0)
                        throw new InvalidDataException($"{Path.GetFileName(dll)} has sim types ({string.Join(", ", simTypes.Take(3).Select(t => t.Name))}) but references {string.Join(", ", unsafeRefs)}: the sim half must reference only HeroesNotIncluded.Sim (put views and sounds in a second assembly)");
                    if (unsafeRefs.Count > 0 || asm.GetExportedTypes().Any(t => typeof(IGameMod).IsAssignableFrom(t)))
                        game.Add((mod.Id, asm));
                }
            }
            catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or FileNotFoundException or InvalidDataException or ReflectionTypeLoadException or TypeLoadException)
            {
                problems.Add($"\"{mod.Id}\"'s code didn't load: {ex.Message}");
                failed.Add(mod.Id);
                sim.RemoveAll(x => x.Item1 == mod.Id);
                game.RemoveAll(x => x.Item1 == mod.Id);
            }
        }
        return new Halves(sim, game);
    }

    /// <summary>The engine's and the game's own assemblies, and the runtime's: never a mod's, wherever a copy turns up.</summary>
    private static bool IsEngine(string name) =>
        IsRuntime(name) || SimSafe.Contains(name) || name is "HeroesNotIncluded" || name.StartsWith("Narsil", StringComparison.Ordinal)
        || name.StartsWith("Brutal", StringComparison.Ordinal) || name.StartsWith("Whisper", StringComparison.Ordinal) || name.StartsWith("NVorbis", StringComparison.Ordinal);

    private static bool IsGames(string name) =>
        AssemblyLoadContext.Default.Assemblies.Any(a => a.GetName().Name == name) || File.Exists(Path.Combine(AppContext.BaseDirectory, name + ".dll"));

    private static Assembly LoadOnce(AssemblyLoadContext context, string path)
    {
        var key = $"{Path.GetFullPath(path)}|{File.GetLastWriteTimeUtc(path).Ticks}";
        if (Loaded.TryGetValue(key, out var asm))
            return asm;
        return Loaded[key] = context.LoadFromAssemblyPath(Path.GetFullPath(path));
    }
}
