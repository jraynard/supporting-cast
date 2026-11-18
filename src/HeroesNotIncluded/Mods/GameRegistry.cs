using System.Reflection;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;
using Narsil;
using Narsil.Audio;

namespace HeroesNotIncluded.Mods;

/// <summary>
/// A mod's presentation half: the loader finds every public type implementing this in a mod's
/// game assembly (<c>&lt;Id&gt;.dll</c>) and calls <see cref="Register"/> once at startup. Views and
/// sounds read only what the player perceives, as the game's own do: never hidden state.
/// </summary>
public interface IGameMod
{
    void Register(GameRegistry registry);
}

/// <summary>What one mod registers with the game: how its mini-games look and sound.</summary>
public sealed class GameRegistry
{
    internal GameRegistry(string modId) => ModId = modId;

    public string ModId { get; }

    internal readonly Dictionary<Type, Action<WorkView, WorkSession>> Views = [];
    internal readonly Dictionary<Type, Action<WorkSoundContext, WorkSession>> Sounds = [];

    /// <summary>
    /// How a work session (a mini-game) is drawn while it's in hand: open the window with
    /// <see cref="WorkView.Begin"/>, draw the game, close it with <see cref="WorkView.End"/>. A mod may
    /// also give a game's own session a new look.
    /// </summary>
    public void WorkView<T>(Action<WorkView, T> draw) where T : WorkSession =>
        Views[typeof(T)] = (view, s) => draw(view, (T)s);

    /// <summary>
    /// How a work session sounds, once a frame while it's in hand: hold a loop for the station, play a
    /// sound when a count the player can see moves on (<see cref="WorkSoundContext"/> keeps the counts).
    /// </summary>
    public void WorkSound<T>(Action<WorkSoundContext, T> update) where T : WorkSession =>
        Sounds[typeof(T)] = (ctx, s) => update(ctx, (T)s);
}

/// <summary>
/// A work session's sound, from one frame to the next: the engine, and a few counts kept for the
/// session in hand (reset when a new one starts) so a sound plays once each time one moves on.
/// </summary>
public sealed class WorkSoundContext(AudioEngine audio)
{
    public AudioEngine Audio { get; } = audio;

    /// <summary>The first frame of this session.</summary>
    public bool Fresh { get; internal set; }

    /// <summary>Counts for the mod's own use, zero when a session starts.</summary>
    public int A, B, C;
    public float Value;

    internal void Reset() => (A, B, C, Value) = (0, 0, 0, 0f);
}

/// <summary>
/// The game as a mod's work view sees it: the window to draw a mini-game in, what the player
/// perceives, and the clock. It's the public face of the village screen, and grows as mods need more
/// (docs/MODDING.md).
/// </summary>
public sealed class WorkView
{
    private readonly VillageGame _game;

    internal WorkView(VillageGame game) => _game = game;

    public GameHost Host => _game.Host;

    /// <summary>The simulation, for state anyone could see (positions, the clock). Hidden state goes through <see cref="Perceived"/>.</summary>
    public Simulation Sim => _game.CurrentSim;

    /// <summary>What the player's character knows (docs: Secrets): read other characters and items through this.</summary>
    public PerceptionView Perceived => _game.Perceived;

    /// <summary>Real seconds since the screen opened, for animation.</summary>
    public float Time => _game.Time;

    /// <summary>Open the mini-game's window: a title plate and the keys as caps ("F", "hammer"). Pair with <see cref="End"/>.</summary>
    public void Begin(string title, params (string Keys, string Does)[] hints) => _game.BeginWorkWindow(title, hints);

    public void End() => _game.EndWorkWindow();

    /// <summary>Send a command, as the game's own controls do (it's checked on every peer).</summary>
    public void Issue(ISimCommand command) => _game.IssueCommand(command);
}

/// <summary>
/// The game halves of the mods turned on, and what they registered (see <see cref="GameContent"/>,
/// which loads them). Looked up before the game's own views, so a mod can restyle one.
/// </summary>
public static class GameMods
{
    private static List<GameRegistry> _registries = [];

    /// <summary>Call each mod's <see cref="IGameMod"/>s (load order). Throws naming the mod whose registration failed.</summary>
    public static void Use(IReadOnlyList<(string ModId, Assembly Assembly)> mods)
    {
        var registries = new List<GameRegistry>();
        foreach (var (id, asm) in mods)
        {
            var registry = new GameRegistry(id);
            foreach (var type in asm.GetExportedTypes().Where(t => typeof(IGameMod).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false }).OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                try
                {
                    ((IGameMod)Activator.CreateInstance(type)!).Register(registry);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    throw new InvalidOperationException($"Mod {id}: {type.Name}.Register failed: {ex.Message}", ex);
                }
            }
            registries.Add(registry);
        }
        _registries = registries;
    }

    private static T? Find<T>(Type session, Func<GameRegistry, Dictionary<Type, T>> table) where T : class
    {
        // The last mod to register a session's view wins; a base type's view covers its subclasses.
        for (var t = session; t != null && t != typeof(object); t = t.BaseType)
            for (var i = _registries.Count - 1; i >= 0; i--)
                if (table(_registries[i]).TryGetValue(t, out var found))
                    return found;
        return null;
    }

    internal static bool TryDraw(WorkView view, WorkSession session)
    {
        if (Find(session.GetType(), r => r.Views) is not { } draw)
            return false;
        draw(view, session);
        return true;
    }

    internal static bool TrySound(WorkSoundContext ctx, WorkSession session)
    {
        if (Find(session.GetType(), r => r.Sounds) is not { } update)
            return false;
        update(ctx, session);
        return true;
    }
}
