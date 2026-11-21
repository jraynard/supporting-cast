using System.Reflection;
using HeroesNotIncluded.Audio;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Ecs;
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

/// <summary>
/// A mark a mod draws on a cell (<see cref="GameRegistry.CellMarks"/>): a sprite, tinted, laid flat on its top; or, <see cref="Floating"/>,
/// an icon bobbing over it, as over a dig order (where to take something).
/// </summary>
public readonly record struct CellMark(int3 Cell, string Sprite, float4 Colour, float Size = 0.6f, bool Floating = false);

/// <summary>What one mod registers with the game: how its mini-games look and sound.</summary>
public sealed class GameRegistry
{
    internal GameRegistry(string modId) => ModId = modId;

    public string ModId { get; }

    internal readonly Dictionary<Type, Action<WorkView, WorkSession>> Views = [];
    internal readonly Dictionary<Type, Action<WorkSoundContext, WorkSession>> Sounds = [];
    internal readonly Dictionary<Type, Func<WorkView, WorkSession, AimLine?>> AimLines = [];
    internal readonly List<Func<Simulation, Entity, long, Tilt?>> Tilts = [];
    internal readonly List<Func<Simulation, IEnumerable<CellMark>>> CellMarkers = [];
    internal readonly List<Action<WorkView>> Overlays = [];
    internal readonly List<Func<Simulation, Entity, int?>> ModelCounts = [];
    internal readonly List<Func<IWorldSounds>> WorldSoundMakers = [];
    internal readonly List<StationTab> StationTabs = [];
    internal readonly List<Func<WorkView, Entity, Entity, string?>> MendHints = [];
    internal readonly List<Action<WorkView, Entity>> WagonPanels = [];
    internal readonly List<ShopSection> ShopSections = [];
    internal readonly Dictionary<Type, Func<WorkView, WorkSession, IEnumerable<int>>> Inputs = [];

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

    /// <summary>A line on the ground while a session's in hand (where a tree's aimed to fall), as a bow's aim is drawn.</summary>
    public void AimLine<T>(Func<WorkView, T, AimLine?> line) where T : WorkSession =>
        AimLines[typeof(T)] = (view, s) => line(view, (T)s);

    /// <summary>
    /// A thing drawn tipped over (a tree coming down): given the thing and the tick, the way it tips and how
    /// far (radians, 0 upright to π/2 flat), or null to draw it standing. Read only what anyone can see.
    /// </summary>
    public void Tilt(Func<Simulation, Entity, long, Tilt?> tilt) => Tilts.Add(tilt);

    /// <summary>
    /// Marks the mod puts on cells for anyone to see (a miner's chalk on a rock face): each drawn flat on the cell's top
    /// (a solid one's, as a dig order's mark is; else its floor). Read only what anyone can see: it's drawn for every player.
    /// </summary>
    public void CellMarks(Func<Simulation, IEnumerable<CellMark>> marks) => CellMarkers.Add(marks);

    /// <summary>
    /// Called every frame while the player's in the world, mini-game or not: a panel of the mod's own on the HUD
    /// (<see cref="WorkView.BeginHudPanel"/>), its controls read (<see cref="WorkView.Playing"/>) and sent as commands
    /// (<see cref="WorkView.Issue"/>). The miner's barrow, leaned back as it's pushed. Read only what the player perceives.
    /// </summary>
    public void Overlay(Action<WorkView> draw) => Overlays.Add(draw);

    /// <summary>
    /// How full a thing is drawn (its model's boxes with a <c>minCount</c> shown from that many: a pile grows with its
    /// stack), for a thing that's one item yet holds more (a barrow's load heaping up). Null if not the mod's. Read only
    /// what anyone can see.
    /// </summary>
    public void ModelCount(Func<Simulation, Entity, int?> count) => ModelCounts.Add(count);

    /// <summary>Sounds of the world the mod adds (a tree's creak and crash), made once per game: heard as the player perceives it.</summary>
    public void WorldSounds(Func<IWorldSounds> make) => WorldSoundMakers.Add(make);

    /// <summary>A tab of a station's window (the carpenter's bench: the furniture the fire took, to join anew).</summary>
    public void StationTab(string name, Func<WorkView, Entity, bool> applies, Action<WorkView, Entity> draw, string? footer = null) =>
        StationTabs.Add(new StationTab(name, applies, draw, footer));

    /// <summary>What the Mend button's tip says for a piece mended by this mod's hand game; null if it isn't this mod's.</summary>
    public void MendHint(Func<WorkView, Entity, Entity, string?> hint) => MendHints.Add(hint);

    /// <summary>
    /// A control of the mod's own (a note on the pipe), rebindable in Settings like the game's and live
    /// while working (the work context). Returns its id (<c>modId:id</c>), which the mod asks
    /// <see cref="WorkView.Down"/> and friends about, and names in text with <see cref="WorkView.Glyph"/>.
    /// </summary>
    /// <param name="context">
    /// When it's live, for Settings' clash check: <see cref="Controls.Work"/> (the default) clashes with the game's
    /// mini-game keys; a context of the mod's own ("Music") clashes only with its own.
    /// </param>
    public string Control(string id, string label, Narsil.Platform.Binding[] keys, Narsil.Platform.Binding[] pad, string context = Controls.Work)
    {
        var full = $"{ModId}:{id}";
        Controls.Map.Add(new Narsil.Platform.ActionDef(full, label, "Hands-on work", context, keys, pad));
        return full;
    }

    /// <summary>
    /// This frame's inputs of the mod's own for a session in hand (<see cref="WorkInputKind.Custom"/>):
    /// values the session's <c>Custom</c> unpacks (a note's degree, velocity, on or off). Stamped and
    /// sent like the game's own, so co-op and replays carry them. Read the mod's controls here.
    /// </summary>
    public void WorkInput<T>(Func<WorkView, T, IEnumerable<int>> inputs) where T : WorkSession =>
        Inputs[typeof(T)] = (view, s) => inputs(view, (T)s);

    /// <summary>A part of a wagon's window (its woodwork's mending): drawn under the wagon's own.</summary>
    public void WagonPanel(Action<WorkView, Entity> draw) => WagonPanels.Add(draw);

    /// <summary>
    /// A section of the shop window (as the carter's is), shown where <paramref name="applies"/>: the carpenter's firewood to
    /// order for a keeper, or the orders waiting for the carpenter. Drawn after the carter's.
    /// </summary>
    public void ShopSection(string name, Func<WorkView, bool> applies, Action<WorkView> draw) => ShopSections.Add(new ShopSection(name, applies, draw));
}

/// <summary>A line on the ground from <see cref="From"/> to <see cref="To"/>, drawn <see cref="Drawn"/> (0..1) of the way; ember once <see cref="Ready"/>.</summary>
public readonly record struct AimLine(float3 From, float3 To, float Drawn, bool Ready, bool Winded);

/// <summary>A thing tipped <see cref="Angle"/> radians (π/2 is flat) toward <see cref="Degrees"/> (0 east, 90 south).</summary>
public readonly record struct Tilt(float Degrees, float Angle);

/// <summary>A tab a mod adds to a station's window.</summary>
public sealed record StationTab(string Name, Func<WorkView, Entity, bool> Applies, Action<WorkView, Entity> Draw, string? Footer);

/// <summary>A mod's section of the shop window (<see cref="GameRegistry.ShopSection"/>).</summary>
public sealed record ShopSection(string Name, Func<WorkView, bool> Applies, Action<WorkView> Draw);

/// <summary>A mod's sounds of the world, asked once a frame (<see cref="GameRegistry.WorldSounds"/>).</summary>
public interface IWorldSounds
{
    void Update(WorldSoundContext ctx);
}

/// <summary>The world as the player hears it, for a mod's sounds: never key a sound to hidden state.</summary>
public sealed class WorldSoundContext
{
    public required Simulation Sim { get; init; }
    public required PerceptionView View { get; init; }
    public required AudioEngine Audio { get; init; }

    /// <summary>Where the player is (null for a camera with no character).</summary>
    public int3? Me { get; init; }

    internal Func<int3, float?> ReachOf { get; init; } = _ => null;

    /// <summary>How muffled a sound at a cell comes to the player (0 clear), or null if it's out of earshot.</summary>
    public float? Reach(int3 cell) => ReachOf(cell);

    /// <summary>Hold the background music this frame (live music in earshot): it pauses, and goes on after.</summary>
    public void HoldMusic() => GameAudio.HoldMusic();

    /// <summary>A cell's place for the audio engine.</summary>
    public static float3 At(int3 cell) => HeroesNotIncluded.Audio.SoundDirector.ToAudio(cell);
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
    public float Value, Warm;
    public bool Flag;

    internal void Reset() => (A, B, C, Value, Warm, Flag) = (0, 0, -1, 0f, 0f, false);
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

    /// <summary><see cref="Begin(string, ValueTuple{string, string}[])"/>, saying whether the window takes the mouse's buttons (left as F, right as Space).</summary>
    public void Begin(string title, bool mouse, params (string Keys, string Does)[] hints) => _game.BeginWorkWindow(title, mouse, hints);

    /// <summary>The work session in hand (the predicted one, so it answers the frame a key is pressed), or null.</summary>
    public WorkSession? Session => _game.WorkInHand;

    /// <summary>The player's character, Entity.Null without one.</summary>
    public Entity Me => _game.Me;

    /// <summary>Busy at work already (a button that starts work is off while it is).</summary>
    public bool Working => _game.IsWorking;

    /// <summary>This frame's panel stands against the right edge, the middle of the screen left clear (work out in the world: a tree). Call before <see cref="Begin(string, ValueTuple{string, string}[])"/>.</summary>
    public void Aside() => _game.WorkAside();

    /// <summary>The mouse is the game's here (a drag, a click to aim): it works the session rather than the camera.</summary>
    public void TakesPointer(float2 min, float2 max, bool wheel = false, bool edges = false) => _game.MarkPointer(min, max, wheel, edges);

    /// <summary>A way on the map (degrees: 0 east, 90 south) as it points on screen, the camera's way round.</summary>
    public float2 ScreenDir(float degrees) => _game.OnScreen(degrees);

    /// <summary>A tool sprite swung about its handle (a hammer, an axe), as the anvil draws its hammer.</summary>
    public void DrawToolSwing(Brutal.ImGuiApi.ImDrawListPtr dl, string sprite, float faceAlong, float raise, float2 strike, float px, Brutal.ImGuiApi.ImColor8 tint) =>
        _game.ToolSwing(dl, sprite, faceAlong, raise, strike, px, tint);

    /// <summary>Whether a control (the game's, or a mod's: <see cref="GameRegistry.Control"/>) is held, pressed this frame, let go this frame.</summary>
    public bool Down(string control) => Controls.Map.Down(control);

    public bool Pressed(string control) => Controls.Map.Pressed(control);

    public bool Released(string control) => Controls.Map.Released(control);

    /// <summary>A control's key cap for the device last used, for the words on a mini-game.</summary>
    public static string Glyph(string control) => Controls.Glyph(control);

    /// <summary>Whether the work's clock is running (not paused, the session live): read controls only then.</summary>
    public bool Live => _game.WorkLive;

    /// <summary>In the world with a character, the game running (an overlay reads its controls only then).</summary>
    public bool Playing => _game.InWorld;

    /// <summary>A panel of the HUD's (anchored, stacked, nothing overlapping) for an overlay; close it with <see cref="EndHudPanel"/>.</summary>
    public void BeginHudPanel(string id, Narsil.Ui.Anchor anchor = Narsil.Ui.Anchor.Bottom) => _game.HudPanel(id, anchor);

    public void EndHudPanel() => Narsil.Ui.Panel.End();

    /// <summary>A tooltip in the game's style.</summary>
    public void Tip(string text) => _game.ShowTip(text);

    /// <summary>The size the game's own buttons take in a window.</summary>
    public float2? ButtonSize => _game.AutoSize;
}

/// <summary>
/// The game halves of the mods turned on, and what they registered (see <see cref="GameContent"/>,
/// which loads them). Looked up before the game's own views, so a mod can restyle one.
/// </summary>
public static class GameMods
{
    private static List<GameRegistry> _registries = [];
    private static IReadOnlyList<(string ModId, Assembly Assembly)> _loaded = [];

    /// <summary>The game halves in use, to put back after a look at others (<see cref="ModCheck"/>).</summary>
    public static IReadOnlyList<(string ModId, Assembly Assembly)> Loaded => _loaded;

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
        _loaded = [.. mods];
        _worldSounds = null;
        Controls.Map.Load(Settings.Current.Bindings); // a mod's own controls' saved bindings, now they're there
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

    internal static AimLine? AimLineFor(WorkView view, WorkSession? session) =>
        session != null && Find(session.GetType(), r => r.AimLines) is { } line ? line(view, session) : null;

    /// <summary>Every mod's marks on cells (<see cref="GameRegistry.CellMarks"/>), this frame.</summary>
    internal static IEnumerable<CellMark> CellMarks(Simulation sim) => _registries.SelectMany(r => r.CellMarkers).SelectMany(marks => marks(sim));

    /// <summary>How full a thing's drawn, by a mod (<see cref="GameRegistry.ModelCount"/>); null if no mod says.</summary>
    internal static int? ModelCountOf(Simulation sim, Entity e)
    {
        foreach (var r in _registries)
            foreach (var count in r.ModelCounts)
                if (count(sim, e) is { } n)
                    return n;
        return null;
    }

    /// <summary>Every mod's overlay (<see cref="GameRegistry.Overlay"/>), this frame.</summary>
    internal static void DrawOverlays(WorkView view)
    {
        foreach (var r in _registries)
            foreach (var draw in r.Overlays)
                draw(view);
    }

    internal static Tilt? TiltOf(Simulation sim, Entity e, long tick)
    {
        foreach (var r in _registries)
            foreach (var tilt in r.Tilts)
                if (tilt(sim, e, tick) is { } t)
                    return t;
        return null;
    }

    private static List<IWorldSounds>? _worldSounds;

    internal static IReadOnlyList<IWorldSounds> WorldSoundsMade => _worldSounds ??= [.. _registries.SelectMany(r => r.WorldSoundMakers).Select(make => make())];

    internal static IEnumerable<StationTab> StationTabs => _registries.SelectMany(r => r.StationTabs);

    internal static string? MendHint(WorkView view, Entity station, Entity item) =>
        _registries.SelectMany(r => r.MendHints).Select(h => h(view, station, item)).FirstOrDefault(h => h != null);

    internal static IEnumerable<Action<WorkView, Entity>> WagonPanels => _registries.SelectMany(r => r.WagonPanels);
    internal static IEnumerable<ShopSection> ShopSections => _registries.SelectMany(r => r.ShopSections);

    /// <summary>This frame's inputs of a mod's own for the session in hand; null if no mod reads any for it.</summary>
    internal static List<int>? InputsFor(WorkView view, WorkSession? session) =>
        session != null && Find(session.GetType(), r => r.Inputs) is { } inputs ? [.. inputs(view, session)] : null;

    internal static bool TrySound(WorkSoundContext ctx, WorkSession session)
    {
        if (Find(session.GetType(), r => r.Sounds) is not { } update)
            return false;
        update(ctx, session);
        return true;
    }
}
