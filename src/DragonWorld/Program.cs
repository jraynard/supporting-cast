using DragonWorld;
using DragonWorld.Debug;
using DragonWorld.Net;
using Narsil;
using Narsil.Platform;
using Narsil.Rendering;
using DragonWorld.Rendering;
using DragonWorld.Sim;
using DragonWorld.Sim.Defs;
using DragonWorld.Sim.Saving;
using DragonWorld.Sim.Scenarios;

// Headless runs (bench, checks) log to the console only; the game also writes a file testers can send.
var headless = args.Any(a => a is "--bench" or "--check-art" or "--check-save" or "--check-gpu" or "--version");
Log.Initialize(logDirectory: headless ? null : BuildInfo.LogDirectory, appName: "dragonworld");
string? buildTag = null;
Log.CrashContext = () => BuildInfo.Describe(buildTag);

if (args.Contains("--version"))
{
    // Headless: what testers (and the packaging script) compare. Co-op only works between equal tags.
    var defs = DefDatabase.LoadDirectory(Path.Combine(AppContext.BaseDirectory, "data", "defs"));
    Console.WriteLine($"DragonWorld {BuildInfo.Label}");
    Console.WriteLine($"build tag {BuildInfo.Tag(defs)}");
    Console.WriteLine($"{System.Runtime.InteropServices.RuntimeInformation.OSDescription} {System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier}");
    return;
}

if (args.Contains("--check-gpu"))
{
    // Headless: does Vulkan load and offer a GPU (MoltenVK on macOS)? No window, so it runs on CI.
    try
    {
        var gpus = VulkanContext.ListGpus(TitleGame.Title);
        foreach (var gpu in gpus)
            Console.WriteLine(gpu);
        Console.WriteLine($"{gpus.Count} GPU(s)");
        Environment.Exit(gpus.Count > 0 ? 0 : 1);
    }
    catch (Exception e)
    {
        Console.WriteLine($"error: Vulkan didn't start: {e.InnerException?.Message ?? e.Message}");
        Environment.Exit(1);
    }
}

if (args.Contains("--bench"))
{
    Bench.Run(ParseSeed(args));
    return;
}

if (args.Contains("--check-art"))
{
    // Headless: pack the atlas on the CPU and audit it against the defs. Non-zero exit on missing sprites.
    var data = Path.Combine(AppContext.BaseDirectory, "data");
    var packed = AtlasPacker.Pack(Path.Combine(data, "textures"));
    var report = ArtAudit.Run(DefDatabase.LoadDirectory(Path.Combine(data, "defs")), packed.Sprites, packed.Sheets);
    foreach (var e in report.Errors)
        Console.WriteLine($"error: {e}");
    foreach (var w in report.Warnings)
        Console.WriteLine($"warning: {w}");
    Console.WriteLine($"{packed.Sprites.Count} sprites, {packed.Sheets.Count} sheets, {packed.Pages} atlas page(s) of {packed.Size}x{packed.Size}: {report.Errors.Count} error(s), {report.Warnings.Count} warning(s)");
    Environment.Exit(report.Errors.Count > 0 ? 1 : 0);
}

if (ParseString(args, "--check-save") is { } checkPath)
{
    // Headless: load a save, play an hour on and print what's in it. Non-zero exit if it won't load.
    try
    {
        var defs = DefDatabase.LoadDirectory(Path.Combine(AppContext.BaseDirectory, "data", "defs"));
        var save = SaveSlots.Load(ResolveSave(checkPath), defs);
        var h = save.Header;
        Console.WriteLine($"{save.SlotName}: format {h.Version}, seed {h.Seed}, {h.Time}, {h.Width}x{h.Height}x{h.Depth}, {h.Villagers} villager(s), dragons: {string.Join(", ", h.Dragons)}");
        foreach (var (key, value) in h.Meta)
            Console.WriteLine($"  {key} = {value}");
        Console.WriteLine($"Loaded: {save.Sim.World.AliveCount} entities, state hash {save.Sim.StateHash():X16}");
        save.Sim.TickMany(SimClock.TicksPerHour);
        Console.WriteLine($"After an hour: {save.Sim.Clock.TimeString()}, state hash {save.Sim.StateHash():X16}");
    }
    catch (SaveException ex)
    {
        Console.WriteLine($"error: {ex.Message}");
        Environment.Exit(1);
    }
    return;
}

var stress = args.Contains("--stress");
try
{
    buildTag = BuildInfo.Tag(DefDatabase.LoadDirectory(Path.Combine(AppContext.BaseDirectory, "data", "defs")));
}
catch (Exception ex)
{
    Log.Engine.Warning($"Couldn't read the defs for the build tag: {ex.Message}"); // the game reports bad defs itself
}
Log.Engine.Info(BuildInfo.Describe(buildTag));
if (Log.File is { } file)
    Log.Engine.Info($"Logging to {file.Path}");
// Co-op testing: run two copies on one machine as different players.
CoopIdentity.IdOverride = ParseString(args, "--player-id");
CoopIdentity.NameOverride = ParseString(args, "--name");
var settings = Settings.Load(Settings.DefaultPath);
SaveSlots.Directory = SaveSlots.DefaultDirectory;
VillageGame.OpenProfiler = args.Contains("--profiler");
DragonWorld.Net.CoopRelay.Override = ParseString(args, "--relay");
CharacterRoster.PickOverride = ParseString(args, "--character");
using var host = new GameHost(new GameHostOptions
{
    Title = TitleGame.Title,
    AppName = TitleGame.DataFolder, // the config folder (and shader cache) keep the old name
    AppId = TitleGame.AppId,
    IconPath = Path.Combine(AppContext.BaseDirectory, "data", "icon.png"),
    Display = settings.Display,
    VSync = !stress && settings.VSync,
    MaxFps = stress ? 0 : settings.MaxFps,
    CapUnsyncedFps = !stress,
    UnfocusedMaxFps = stress ? 0 : FrameCap.DefaultUnfocused,
    SkipSplash = args.Contains("--no-splash"), // Debug builds only
    Profile = Environment.GetEnvironmentVariable("DW_PROFILE") == "1" || Environment.GetEnvironmentVariable("NARSIL_PROFILE") == "1",
});

if (args.Contains("--render-test"))
    host.Run(new RenderTestGame { StressFromArgs = stress });
else if (args.Contains("--art-preview"))
    host.Run(new ArtPreviewGame());
else if (args.Contains("--host"))
{
    // Co-op host: a saved village (--host <slot or path>), or a new Adventure start.
    var me = CoopIdentity.Load(host);
    var hosting = new HostSettings(me, $"{me.Name}'s village", args.Contains("--port") ? ParseInt(args, "--port") : CoopSession.DefaultPort);
    if (ParseString(args, "--host") is { } hostPath && !hostPath.StartsWith("--"))
    {
        try
        {
            var save = SaveSlots.Load(ResolveSave(hostPath), DefDatabase.LoadDirectory(Path.Combine(AppContext.BaseDirectory, "data", "defs")));
            host.Run(new VillageGame(save.Sim.Seed) { Loaded = save, HostCoop = hosting, AutoPause = false });
        }
        catch (SaveException ex)
        {
            Console.WriteLine($"error: {ex.Message}");
            Environment.Exit(1);
        }
    }
    else
    {
        // --character: host as a saved character (the roster's), else --name and --dragon-name.
        var saved = CharacterRoster.PickOverride is { } pick
            ? CharacterRoster.Load(host, DefDatabase.LoadDirectory(Path.Combine(AppContext.BaseDirectory, "data", "defs"))).Find(pick)
            : null;
        CharacterRoster.PickOverride = null;
        host.Run(new VillageGame(ParseSeed(args))
        {
            Start = StartKind.Pact, HeroName = saved?.Name ?? ParseString(args, "--name"), HeroLook = saved?.Look, HeroTraits = saved?.Traits, HeroClothes = saved?.Clothes,
            Difficulty = ParseString(args, "--difficulty"), // raids once the tower stands; none without
            DragonName = saved?.DragonName ?? ParseString(args, "--dragon-name"), DragonLook = saved?.DragonLook ?? ElementLookArg(args),
            DragonBreed = saved?.DragonBreed ?? ParseString(args, "--breed"), HostCoop = hosting, AutoPause = false,
        });
    }
}
else if (ParseString(args, "--join") is { } joinAddress)
    host.Run(new TitleGame { AutoJoin = joinAddress });
else if (ParseString(args, "--load") is { } loadPath)
{
    LoadedGame save;
    try
    {
        save = SaveSlots.Load(ResolveSave(loadPath), DefDatabase.LoadDirectory(Path.Combine(AppContext.BaseDirectory, "data", "defs")));
    }
    catch (SaveException ex)
    {
        Console.WriteLine($"error: {ex.Message}");
        Environment.Exit(1);
        return;
    }
    host.Run(new VillageGame(save.Sim.Seed) { Loaded = save, AutoPause = !args.Contains("--no-autopause"), OpenSaves = ParseString(args, "--open-saves") });
}
else if (!LaunchesDirectly(args))
    host.Run(new TitleGame { AutoPick = ParseString(args, "--menu-pick"), AutoCreator = ParseString(args, "--creator"), AutoPreset = ParseString(args, "--preset") });
else
    host.Run(new VillageGame(ParseSeed(args), demo: args.Contains("--demo"), skipHours: ParseInt(args, "--skip-hours"))
    {
        // Debug/screenshot helpers: starting camera rotation (degrees) and cutaway level.
        StartYawDegrees = args.Contains("--yaw") ? ParseInt(args, "--yaw") : 45,
        StartCutaway = args.Contains("--cutaway") ? ParseInt(args, "--cutaway") : int.MaxValue,
        StartZoom = args.Contains("--zoom") ? ParseInt(args, "--zoom") : 0,
        IgniteForest = args.Contains("--ignite-forest"),
        BreathDemo = args.Contains("--breath-demo"),
        DragonDemo = args.Contains("--dragon-demo"),
        FurnitureDemo = args.Contains("--furniture-demo"),
        BuildingDemo = args.Contains("--building-demo"),
        Freeform = args.Contains("--freeform"),
        ShowRoofs = args.Contains("--show-roofs"),
        Mounted = args.Contains("--mounted"),
        RaidDemo = args.Contains("--raid-demo"),
        AutoCombat = args.Contains("--auto-combat"),
        AutoPause = !args.Contains("--no-autopause"),
        Difficulty = ParseString(args, "--difficulty"),
        // --adventure escape|pact|egg picks a start; bare --adventure (or --bearer, its old name) is
        // the egg start at a new home, without the prologue.
        Adventure = AdventureStartArg(args) == null && (args.Contains("--adventure") || args.Contains("--bearer")),
        Start = AdventureStartArg(args) is StartKind.Escape or StartKind.Pact ? AdventureStartArg(args)!.Value : StartKind.None,
        Prologue = args.Contains("--prologue") || AdventureStartArg(args) == StartKind.EggBearer,
        HeroName = ParseString(args, "--name"),
        HeroPreset = ParseString(args, "--preset"),
        DragonName = ParseString(args, "--dragon-name"),
        DragonBreed = ParseString(args, "--breed"),     // a breed's def name: NorseDragon
        DragonLook = ElementLookArg(args),               // --element ice: that element in its own colours
        SkipOpening = ParseString(args, "--stage") == "site", // --adventure pact --stage site: straight to the tower's site
    });

// Remember a window the player resized by hand. Settings.Current, not `settings`: the options
// screen replaces the instance when it applies changes.
if (host.Display.Mode == WindowMode.Windowed && host.Window.GetAttribute(GlfwWindowAttribute.Maximized) == 0
    && host.Window.Size is { X: > 0, Y: > 0 } size && size != new int2(Settings.Current.Width, Settings.Current.Height))
{
    (Settings.Current.Width, Settings.Current.Height) = (size.X, size.Y);
    Settings.Save();
}

// Any flag that sets up a game skips the title screen, so scripts and the commands in CLAUDE.md
// keep dropping straight into the village. --new-game does the same with no other changes.
static bool LaunchesDirectly(string[] args) =>
    args.Any(a => a is "--new-game" or "--demo" or "--seed" or "--skip-hours" or "--dragon-demo" or "--raid-demo" or "--furniture-demo" or "--building-demo" or "--freeform"
        or "--breath-demo" or "--ignite-forest" or "--auto-combat" or "--yaw" or "--cutaway" or "--difficulty" or "--adventure" or "--bearer" or "--prologue");

// The start named after --adventure, if any.
static StartKind? AdventureStartArg(string[] args) => ParseString(args, "--adventure")?.ToLowerInvariant() switch
{
    "escape" => StartKind.Escape,
    "pact" => StartKind.Pact,
    "egg" => StartKind.EggBearer,
    _ => null,
};

// --load and --check-save take a path, or the name of a slot in the saves directory ("quicksave").
static string ResolveSave(string pathOrSlot)
{
    if (File.Exists(pathOrSlot))
        return pathOrSlot;
    var slot = Path.Combine(SaveSlots.DefaultDirectory, pathOrSlot.EndsWith(SaveSlots.Extension) ? pathOrSlot : pathOrSlot + SaveSlots.Extension);
    return File.Exists(slot) ? slot : pathOrSlot; // let the load report the missing file
}

// --element ice: a dragon (or egg) of that element in its own colours; null without the flag.
static DragonWorld.Sim.Components.DragonLook? ElementLookArg(string[] args) =>
    Enum.TryParse<DragonWorld.Sim.Components.DragonElement>(ParseString(args, "--element"), ignoreCase: true, out var element)
        ? DragonWorld.Rendering.DragonLooks.ForElement(element)
        : null;

static string? ParseString(string[] args, string flag)
{
    var i = Array.IndexOf(args, flag);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static int ParseInt(string[] args, string flag)
{
    var i = Array.IndexOf(args, flag);
    return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v) ? v : 0;
}

static ulong ParseSeed(string[] args)
{
    var i = Array.IndexOf(args, "--seed");
    return i >= 0 && i + 1 < args.Length && ulong.TryParse(args[i + 1], out var seed) ? seed : 1234;
}
