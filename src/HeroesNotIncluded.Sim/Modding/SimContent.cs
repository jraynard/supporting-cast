using System.Reflection;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Modding;

/// <summary>
/// A mod's sim half: the loader finds every public type implementing this in a mod's
/// <c>.Sim.dll</c> and calls <see cref="Register"/> once, before any simulation is made. Everything a
/// mod adds that the sim must know by name (components to save, sessions under old names) is
/// registered here; types the sim finds by scanning (work sessions, commands) need nothing.
/// </summary>
public interface ISimMod
{
    void Register(SimRegistry registry);
}

/// <summary>What one mod registers with the sim (see <see cref="ISimMod"/>). Names are prefixed with the mod's id.</summary>
public sealed class SimRegistry
{
    internal SimRegistry(string modId) => ModId = modId;

    /// <summary>The mod registering (its <c>mod.json</c> id); names it registers are prefixed <c>modId:</c>.</summary>
    public string ModId { get; }

    internal readonly List<ComponentCodec> Codecs = [];
    internal readonly Dictionary<string, Func<WorkSession>> SessionAliases = new(StringComparer.Ordinal);

    private string Qualify(string name) => $"{ModId}:{name}";

    /// <summary>
    /// A component the mod adds, and how it's saved: every component needs a codec (an unsaved one
    /// would vanish on load). Written by hand, like the game's own, so the format is explicit.
    /// </summary>
    public void Component<T>(string name, Action<SaveWriter, T> write, Func<SaveReader, T> read) =>
        Codecs.Add(new ComponentCodec<T>(Qualify(name), write, read));

    /// <summary>A marker component with no data.</summary>
    public void Tag<T>(string name) where T : struct => Component<T>(name, (_, _) => { }, _ => default);

    /// <summary>A session renamed or retired: saves that name it read back as this.</summary>
    public void SessionAlias(string oldName, Func<WorkSession> make) => SessionAliases[Qualify(oldName)] = make;
}

/// <summary>
/// The assemblies the sim is made of this run: its own, then each mod's sim half in load order
/// (docs/MODDING.md). Whatever the sim finds by scanning (work sessions, commands) or by name
/// (component codecs) comes from here, so a mod's types save, load and travel like the game's.
/// A type's saved name is its own for the game's, and <c>modId:Name</c> for a mod's, so mods can't
/// collide with each other or the game. Set once, before any simulation; <see cref="Version"/>
/// tells the caches to rebuild.
/// </summary>
public static class SimContent
{
    private sealed record Loaded(string ModId, Assembly Assembly, SimRegistry Registry);

    private static List<Loaded> _mods = [];

    /// <summary>Bumped by every <see cref="Use"/>, so caches built from the content know to rebuild.</summary>
    public static int Version { get; private set; }

    /// <summary>The game's sim assembly.</summary>
    public static Assembly Core => typeof(Simulation).Assembly;

    /// <summary>The game's sim assembly, then each mod's, in load order.</summary>
    public static IReadOnlyList<Assembly> Assemblies => [Core, .. _mods.Select(m => m.Assembly)];

    /// <summary>The mods' sim assemblies, by mod id, in load order.</summary>
    public static IEnumerable<(string ModId, Assembly Assembly)> Mods => _mods.Select(m => (m.ModId, m.Assembly));

    /// <summary>
    /// Take these mods' sim assemblies (load order), calling each one's <see cref="ISimMod"/>s. An empty
    /// list is the game alone. Throws <see cref="InvalidOperationException"/> naming the mod whose
    /// registration failed; nothing is changed then.
    /// </summary>
    public static void Use(IReadOnlyList<(string ModId, Assembly Assembly)> mods)
    {
        var loaded = new List<Loaded>();
        foreach (var (id, asm) in mods)
        {
            var registry = new SimRegistry(id);
            foreach (var type in ExportedTypes(asm).Where(t => typeof(ISimMod).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false }).OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                try
                {
                    ((ISimMod)Activator.CreateInstance(type)!).Register(registry);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    throw new InvalidOperationException($"Mod {id}: {type.Name}.Register failed: {ex.Message}", ex);
                }
            }
            loaded.Add(new Loaded(id, asm, registry));
        }
        _mods = loaded;
        Version++;
    }

    /// <summary>The mod an assembly belongs to, or null for the game's own.</summary>
    public static string? ModOf(Assembly assembly) => _mods.FirstOrDefault(m => m.Assembly == assembly)?.ModId;

    /// <summary>The name a type is saved and sent by: its own for the game's types, <c>modId:Name</c> for a mod's.</summary>
    public static string NameOf(Type type) => ModOf(type.Assembly) is { } id ? $"{id}:{type.Name}" : type.Name;

    /// <summary>Every concrete type in the content assignable to <typeparamref name="T"/>, assembly by assembly.</summary>
    public static IEnumerable<Type> TypesOf<T>() =>
        Assemblies.SelectMany(a => (a == Core ? a.GetTypes() : ExportedTypes(a)).Where(t => typeof(T).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false }));

    internal static IEnumerable<ComponentCodec> ModCodecs => _mods.SelectMany(m => m.Registry.Codecs);

    internal static IEnumerable<KeyValuePair<string, Func<WorkSession>>> ModSessionAliases => _mods.SelectMany(m => m.Registry.SessionAliases);

    /// <summary>A mod's public types (its internals are its own business).</summary>
    private static Type[] ExportedTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            throw new InvalidOperationException($"{assembly.GetName().Name} didn't load: {ex.LoaderExceptions.FirstOrDefault()?.Message}", ex);
        }
    }
}
