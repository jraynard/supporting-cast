using System.Security.Cryptography;
using HeroesNotIncluded.Audio;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Modding;
using Narsil;
using Narsil.Mods;

namespace HeroesNotIncluded.Mods;

/// <summary>
/// What the game is made of this run: its own data (the core, always first) and the mods turned on,
/// in load order (docs/MODDING.md). Everything that reads content asks here (defs, the atlas's
/// roots, the sound manifests, the co-op build tag), so a mod reaches every one of them or none.
/// </summary>
public sealed class GameContent
{
    /// <summary>The game's own data folder in the build output.</summary>
    public static string DataDirectory => Path.Combine(AppContext.BaseDirectory, "data");

    /// <summary>The per-user mods folder (where Workshop items will go too).</summary>
    public static string UserModsDirectory => Path.Combine(GameHost.DefaultConfigDirectory(TitleGame.DataFolder), "mods");

    /// <summary>
    /// Where mods are looked for, first first: the game's own <c>mods/</c> (the checkout's when running
    /// from one, so edits apply without a rebuild), then the user's.
    /// </summary>
    public static IReadOnlyList<string> ModRoots =>
        [DevPaths.Mods ?? Path.Combine(AppContext.BaseDirectory, "mods"), UserModsDirectory];

    private static ModCatalog? _catalog;

    /// <summary>Every mod installed (looked for once; <see cref="Rediscover"/> looks again).</summary>
    public static ModCatalog Catalog => _catalog ??= Discover();

    public static ModCatalog Rediscover() => _catalog = Discover();

    private static ModCatalog Discover()
    {
        var catalog = ModCatalog.Discover(ModRoots);
        foreach (var p in catalog.Problems)
            Log.Engine.Warning($"Mod: {p}");
        return catalog;
    }

    /// <summary>
    /// The mods to turn on: those on by default (<see cref="ModManifest.DefaultOn"/>) unless turned off,
    /// and those turned on.
    /// </summary>
    public static IReadOnlyList<string> Enabled(IEnumerable<string> on, IEnumerable<string> off)
    {
        var offs = off.ToHashSet(StringComparer.Ordinal);
        return [.. Catalog.All.Where(m => m.DefaultOn).Select(m => m.Id).Concat(on).Distinct().Where(id => !offs.Contains(id))];
    }

    /// <summary>The mods a <c>--mods</c> list names: <c>bard,-carpenter</c> turns the bard on and the carpenter off.</summary>
    public static IReadOnlyList<string> FromArgument(string? list)
    {
        var items = (list ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return Enabled(items.Where(i => !i.StartsWith('-')), items.Where(i => i.StartsWith('-')).Select(i => i[1..]));
    }

    /// <summary>This run's content: the core alone until <see cref="Use"/> says otherwise.</summary>
    public static GameContent Current { get; private set; } = new(ModOrder.None);

    /// <summary>The core with the mods named turned on (missing or broken ones left out, and logged).</summary>
    public static GameContent Use(IEnumerable<string> enabled)
    {
        var wanted = enabled.ToList();
        var order = Catalog.Resolve(wanted);
        // A mod whose code won't load is left out (and anything needing it), and the rest go on.
        var problems = new List<string>(order.Problems);
        var failed = new HashSet<string>(StringComparer.Ordinal);
        var code = ModCode.Load(order, problems, failed);
        if (failed.Count > 0)
        {
            order = Catalog.Resolve(wanted.Where(id => !failed.Contains(id)));
            code = ModCode.Load(order, problems, failed);
        }
        foreach (var p in problems.Distinct())
            Log.Engine.Warning($"Mod left out: {p}");
        SimContent.Use(code.Sim);
        GameMods.Use(code.Game);
        if (order.Mods.Count > 0)
            Log.Engine.Info($"Mods: {string.Join(", ", order.Mods.Select(m => $"{m.Id} {m.Version}"))}{(code.Sim.Count + code.Game.Count > 0 ? $"; code from {string.Join(", ", code.Sim.Concat(code.Game).Select(c => c.ModId).Distinct())}" : "")}");
        return Current = new GameContent(order, problems);
    }

    public GameContent(ModOrder mods, IReadOnlyList<string>? problems = null)
    {
        Mods = mods;
        Problems = problems ?? mods.Problems;
    }

    public ModOrder Mods { get; }

    /// <summary>Why any mod asked for was left out (missing, a dependency missing, code that didn't load).</summary>
    public IReadOnlyList<string> Problems { get; }

    public bool HasMods => Mods.Mods.Count > 0;

    /// <summary>"bard 0.1.0, lutes 1.2": for logs, saves and refusals.</summary>
    public string Describe() => HasMods ? string.Join(", ", Mods.Mods.Select(m => $"{m.Id} {m.Version}")) : "none";

    private IEnumerable<string> ModFolders(string name) => Mods.Mods.Select(m => m.Folder(name)).OfType<string>();

    /// <summary>The defs to load, in order: the build's copy of the core's, then each mod's.</summary>
    public IReadOnlyList<string> DefDirectories(string? dataDirectory = null) =>
        [Path.Combine(dataDirectory ?? DataDirectory, "defs"), .. ModFolders("defs")];

    /// <summary>
    /// The defs as a reload sees them: the checkout's copy of the core's when running from one (an edit
    /// shows without a rebuild), then each mod's.
    /// </summary>
    public IReadOnlyList<string> LiveDefDirectories(string? dataDirectory = null) =>
        [DevPaths.Defs ?? Path.Combine(dataDirectory ?? DataDirectory, "defs"), .. ModFolders("defs")];

    public DefDatabase LoadDefs(string? dataDirectory = null) => DefDatabase.LoadDirectories(DefDirectories(dataDirectory));

    /// <summary>The atlas's roots: the core's textures, then each mod's (a later one's sprite of the same name wins).</summary>
    public IReadOnlyList<string> TextureRoots(string? dataDirectory = null) =>
        [Path.Combine(dataDirectory ?? DataDirectory, "textures"), .. ModFolders("textures")];

    /// <summary>The sound manifests: the core's, then each mod's <c>sounds/sounds.json</c>.</summary>
    public IReadOnlyList<string> SoundManifests(string? dataDirectory = null) =>
        [GameAudio.ManifestPath(dataDirectory ?? DataDirectory), .. ModFolders("sounds").Select(d => Path.Combine(d, "sounds.json"))];

    /// <summary>Point the host's atlas and the game's sounds at this content (a no-op when they already are).</summary>
    public void ApplyTo(GameHost host)
    {
        host.Atlas.SetRoots(TextureRoots(host.DataDirectory));
        GameAudio.UseManifests(SoundManifests(host.DataDirectory));
    }

    /// <summary>
    /// Adds the mods to a content hash: each mod's id, version and every file it ships, in load order.
    /// Adds nothing without mods, so the core's tag is what it always was.
    /// </summary>
    public void AppendFingerprint(IncrementalHash sha)
    {
        foreach (var mod in Mods.Mods)
        {
            sha.AppendData(System.Text.Encoding.UTF8.GetBytes($"mod {mod.Id} {mod.Version}\n"));
            foreach (var file in Directory.EnumerateFiles(mod.Directory, "*", SearchOption.AllDirectories)
                .Select(f => (Full: f, Rel: Path.GetRelativePath(mod.Directory, f).Replace('\\', '/')))
                .OrderBy(f => f.Rel, StringComparer.Ordinal))
            {
                sha.AppendData(System.Text.Encoding.UTF8.GetBytes(file.Rel + "\n"));
                sha.AppendData(File.ReadAllBytes(file.Full));
            }
        }
    }

    /// <summary>The save meta key naming the mods a save was made with.</summary>
    public const string SaveMetaKey = "mods";

    /// <summary>The mods as a save records them: "bard 0.1.0,lutes 1.2" (empty without).</summary>
    public string SaveMeta => string.Join(",", Mods.Mods.Select(m => $"{m.Id} {m.Version}"));

    /// <summary>
    /// Why a save can't be loaded with this content, or null if it can: its mods (by id; versions may
    /// differ) must be the ones turned on now. A save from before mods names none.
    /// </summary>
    public string? Refuses(IReadOnlyDictionary<string, string> meta)
    {
        var saved = (meta.GetValueOrDefault(SaveMetaKey) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(m => m.Split(' ')[0]).Order(StringComparer.Ordinal).ToList();
        var now = Mods.Ids.Order(StringComparer.Ordinal).ToList();
        if (saved.SequenceEqual(now))
            return null;
        static string Names(List<string> ids) => ids.Count == 0 ? "no mods" : string.Join(", ", ids);
        return $"This save was made with {Names(saved)}, and {Names(now)} {(now.Count == 1 ? "is" : "are")} turned on now (Mods, on the title screen).";
    }

    /// <summary>The tag's readable tail naming the mods ("+bard,lutes"), so a refused joiner sees which; empty without mods.</summary>
    public string TagSuffix => HasMods ? "+" + string.Join(",", Mods.Ids) : "";
}
