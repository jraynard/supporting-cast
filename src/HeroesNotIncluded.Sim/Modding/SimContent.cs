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
    internal readonly List<PlacedSystem> Systems = [];
    internal readonly List<ITradeModule> TradeModules = [];
    internal readonly List<Scenarios.Lessons.Lesson> Lessons = [];
    internal readonly List<Func<Bots.BotPlayer, Bots.IBotTrade>> Bots = [];
    internal readonly Dictionary<string, string> Formerly = new(StringComparer.Ordinal);

    /// <summary>The game's own registrations (its bundled trades) keep their names unprefixed.</summary>
    internal bool IsCore => ModId.Length == 0;

    private string Qualify(string name) => IsCore ? name : $"{ModId}:{name}";

    /// <summary>
    /// A trade's sim half (<see cref="ITradeModule"/>): the hooks the core calls at a station, for the
    /// AI's work, at a player's F, and as the town is set up.
    /// </summary>
    public void Trade(ITradeModule module) => TradeModules.Add(module);

    /// <summary>
    /// A trade's tutorial chapters: each lesson's logic, read from what the player knows every time
    /// it's asked (its words are the trade's <c>lessons</c> in trades.json).
    /// </summary>
    public void Lesson(params Scenarios.Lessons.Lesson[] lessons) => Lessons.AddRange(lessons);

    /// <summary>How a bot plays the trade (<see cref="Bots.IBotTrade"/>): made fresh for each bot, so it may keep state.</summary>
    public void Bot(Func<Bots.BotPlayer, Bots.IBotTrade> make) => Bots.Add(make);

    /// <summary>
    /// A component the mod adds, and how it's saved: every component needs a codec (an unsaved one
    /// would vanish on load). Written by hand, like the game's own, so the format is explicit.
    /// </summary>
    /// <param name="formerly">The name saves used before (a component the game had before the mod took it): read as this one.</param>
    public void Component<T>(string name, Action<SaveWriter, T> write, Func<SaveReader, T> read, string? formerly = null)
    {
        Codecs.Add(new ComponentCodec<T>(Qualify(name), write, read));
        if (formerly != null)
            Formerly[formerly] = Qualify(name);
    }

    /// <summary>A marker component with no data.</summary>
    public void Tag<T>(string name) where T : struct => Component<T>(name, (_, _) => { }, _ => default);

    /// <summary>
    /// A system the mod adds to every simulation, made fresh for each one: run each tick after the
    /// system named <paramref name="after"/> (or before <paramref name="before"/>; at the end if
    /// neither), by <see cref="ISimSystem.Name"/>. Order decides what a tick sees, so name the
    /// neighbour that matters. A system with state to keep implements <see cref="ISaveState"/>.
    /// </summary>
    public void System(Func<ISimSystem> make, string? after = null, string? before = null) =>
        Systems.Add(new PlacedSystem(make, after, before, ModId));

    /// <summary>
    /// A session saves knew by a name outside this mod's (a session the game had before the mod took
    /// it over, saved unprefixed): read back as this.
    /// </summary>
    public void SessionFormerly(string oldName, Func<WorkSession> make) => SessionAliases[oldName] = make;

    /// <summary>A session renamed or retired: saves that name it read back as this.</summary>
    public void SessionAlias(string oldName, Func<WorkSession> make) => SessionAliases[Qualify(oldName)] = make;
}

internal sealed record PlacedSystem(Func<ISimSystem> Make, string? After, string? Before, string ModId);

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

    /// <summary>
    /// The game's own trades registered the way a mod's are (<see cref="ISimMod"/>s in the sim
    /// assembly itself), before any mod: the core is the first mod.
    /// </summary>
    private static readonly Lazy<SimRegistry> CoreRegistry = new(() =>
    {
        var registry = new SimRegistry("");
        foreach (var type in Core.GetTypes().Where(t => typeof(ISimMod).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false }).OrderBy(t => t.FullName, StringComparer.Ordinal))
            ((ISimMod)Activator.CreateInstance(type, nonPublic: true)!).Register(registry);
        return registry;
    });

    private static IEnumerable<SimRegistry> Registries => [CoreRegistry.Value, .. _mods.Select(m => m.Registry)];

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

    internal static IEnumerable<PlacedSystem> ModSystems => Registries.SelectMany(r => r.Systems);

    internal static IEnumerable<ComponentCodec> ModCodecs => Registries.SelectMany(r => r.Codecs);

    internal static IEnumerable<KeyValuePair<string, Func<WorkSession>>> ModSessionAliases => Registries.SelectMany(r => r.SessionAliases);

    /// <summary>A component's old save name to its name now, for every one a mod took over from the game.</summary>
    internal static string CodecName(string saved) => Registries.Select(r => r.Formerly.GetValueOrDefault(saved)).FirstOrDefault(n => n != null) ?? saved;

    internal static IEnumerable<Scenarios.Lessons.Lesson> Lessons => Registries.SelectMany(r => r.Lessons);

    internal static IEnumerable<Func<Bots.BotPlayer, Bots.IBotTrade>> BotFactories => Registries.SelectMany(r => r.Bots);

    private static (int Version, List<ITradeModule> Modules)? _trades;

    internal static IReadOnlyList<ITradeModule> TradeModules
    {
        get
        {
            if (_trades is { } t && t.Version == Version)
                return t.Modules;
            var modules = Registries.SelectMany(r => r.TradeModules).ToList();
            _trades = (Version, modules);
            return modules;
        }
    }

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
