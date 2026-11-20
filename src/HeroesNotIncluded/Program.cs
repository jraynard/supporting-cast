using HeroesNotIncluded;
using HeroesNotIncluded.Characters;
using HeroesNotIncluded.Debug;
using HeroesNotIncluded.Mods;
using HeroesNotIncluded.Net;
using Narsil;
using Narsil.Platform;
using Narsil.Rendering;
using HeroesNotIncluded.Rendering;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Scenarios;

// Headless runs (bench, checks) log to the console only; the game also writes a file testers can send.
var headless = args.Any(a => a is "--bench" or "--combat-sweep" or "--season-sweep" or "--check-art" or "--check-mod" or "--check-save" or "--check-gpu" or "--version" or "--dictate-test");
Log.Initialize(logDirectory: headless ? null : BuildInfo.LogDirectory, appName: TitleGame.AppId);
string? buildTag = null;
Log.CrashContext = () => BuildInfo.Describe(buildTag);

// Mods for this run (docs/MODDING.md): --mods a,b, else the player's own list once the settings are read.
// Headless runs (bench, sweeps, checks) see only the defaults and --mods, so their results don't depend on a player's setup.
// --mods musician,-carpenter: the musician on, the carpenter (on by default) off.
var modsGiven = args.Contains("--mods");
if (modsGiven || headless)
    GameContent.Use(GameContent.FromArgument(ParseString(args, "--mods")));

if (args.Contains("--version"))
{
    // Headless: what testers (and the packaging script) compare. Co-op only works between equal tags.
    var defs = GameContent.Current.LoadDefs();
    Console.WriteLine($"{TitleGame.Title} {BuildInfo.Label}");
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

if (ParseString(args, "--dictate-test") is { } dictatePath)
{
    // Headless: a recorded clip through the speech recognizer, timed (docs/VOICE.md).
    Settings.Load(Settings.DefaultPath);
    Environment.Exit(HeroesNotIncluded.Voice.DictateTest.Run(dictatePath, ParseString(args, "--model"), ParseString(args, "--prompt"), ParseInt(args, "--threads")));
}

if (args.Contains("--bench"))
{
    Bench.Run(ParseSeed(args), round: args.Contains("--round"), map: ParseString(args, "--map") ?? HeroesNotIncluded.Sim.Scenarios.VillageRound.DefaultMap);
    return;
}

if (args.Contains("--combat-sweep"))
{
    CombatSweepRun.Run(args);
    return;
}

if (args.Contains("--season-sweep"))
{
    SeasonSweepRun.Run(args);
    return;
}

if (args.Contains("--check-art"))
{
    // Headless: pack the atlas on the CPU and audit it against the defs. Non-zero exit on missing sprites.
    var content = GameContent.Current;
    var packed = AtlasPacker.Pack(content.TextureRoots());
    var report = ArtAudit.Run(content.LoadDefs(), packed.Sprites, packed.Sheets);
    foreach (var e in report.Errors)
        Console.WriteLine($"error: {e}");
    foreach (var w in report.Warnings)
        Console.WriteLine($"warning: {w}");
    Console.WriteLine($"{packed.Sprites.Count} sprites, {packed.Sheets.Count} sheets, {packed.Pages} atlas page(s) of {packed.Size}x{packed.Size}: {report.Errors.Count} error(s), {report.Warnings.Count} warning(s)");
    var sounds = HeroesNotIncluded.Audio.SoundAudit.Run(Path.Combine(GameContent.DataDirectory, "sounds", "sounds.json"));
    foreach (var e in sounds.Errors)
        Console.WriteLine($"error: {e}");
    foreach (var w in sounds.Warnings)
        Console.WriteLine($"warning: {w}");
    Console.WriteLine($"{sounds.Files} sound file(s): {sounds.Errors.Count} error(s), {sounds.Warnings.Count} warning(s)");
    Environment.Exit(report.Errors.Count + sounds.Errors.Count > 0 ? 1 : 0);
}

if (ParseString(args, "--check-mod") is { } modPath)
{
    // Headless: a mod over the core, its defs resolved and its art and sounds audited (docs/MODDING.md).
    Environment.Exit(ModCheck.Run(modPath, Console.Out));
}

if (ParseString(args, "--check-save") is { } checkPath)
{
    // Headless: load a save, play an hour on and print what's in it. Non-zero exit if it won't load.
    try
    {
        var defs = GameContent.Current.LoadDefs();
        var save = SaveSlots.Load(ResolveSave(checkPath), defs);
        var h = save.Header;
        Console.WriteLine($"{save.SlotName}: format {h.Version}, seed {h.Seed}, {h.Time}, {h.Width}x{h.Height}x{h.Depth}, {h.Villagers} villager(s)");
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
var settings = Settings.Load(Settings.DefaultPath);
if (!modsGiven)
    GameContent.Use(GameContent.Enabled(settings.Mods, settings.ModsOff));
try
{
    buildTag = BuildInfo.Tag(GameContent.Current.LoadDefs());
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
// Trying another screen from the desktop: --deck (the Steam Deck's first-run settings), --size WxH,
// --ui-scale N. These are for looking, so the player's settings file is left alone.
if (args.Contains("--deck") || args.Contains("--size") || args.Contains("--ui-scale"))
{
    if (args.Contains("--deck"))
        settings = Settings.Current = HandheldDefaults.FirstRun(steamDeck: true);
    if (ParseString(args, "--size")?.Split('x') is [var w, var h] && int.TryParse(w, out var width) && int.TryParse(h, out var height))
        (settings.WindowMode, settings.Width, settings.Height) = (WindowMode.Windowed, width, height);
    else if (args.Contains("--deck"))
        settings.WindowMode = WindowMode.Windowed; // a Deck-sized window, not the desktop's whole screen
    if (args.Contains("--ui-scale"))
        settings.UiScale = ParseFloat(args, "--ui-scale");
    settings = Settings.Current = settings.Sanitized();
    Settings.FilePath = null;
}
SaveSlots.Directory = SaveSlots.DefaultDirectory;
VillageGame.OpenProfiler = args.Contains("--profiler");
VillageGame.VoiceClip = ParseString(args, "--voice-clip");
TitleGame.OpenSettingsTab = ParseString(args, "--settings-tab");
if (args.Contains("--dev"))
    VillageGame.DevMode = true;
HeroesNotIncluded.Net.CoopRelay.Override = ParseString(args, "--relay");
CharacterRoster.PickOverride = ParseString(args, "--character");
// Steam, when this copy is Steam's (a steam.json beside it) or --steam N asks: before the window, since its
// overlay hooks Vulkan as it starts. A Steam copy opened directly is started again through Steam.
if (HeroesNotIncluded.Platforms.SteamConfig.Find(args, AppContext.BaseDirectory) is { } steamConfig)
{
    if (HeroesNotIncluded.Platforms.SteamPlatform.RestartThroughSteam(steamConfig))
    {
        Log.Engine.Info("Steam: starting the game again through Steam");
        return;
    }
    if (HeroesNotIncluded.Platforms.SteamPlatform.TryStart(steamConfig) is { } steam)
    {
        HeroesNotIncluded.Platforms.GamePlatform.Use(steam);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => HeroesNotIncluded.Platforms.GamePlatform.Shutdown();
    }
}
using var host = new GameHost(new GameHostOptions
{
    Title = TitleGame.Title,
    AppName = TitleGame.DataFolder, // the config folder and shader cache
    AppId = TitleGame.AppId,
    DisplayFont = "Cinzel-SemiBold.ttf", // the brand's display face (art/brand/brand-guide.html): the title's menu
    IconPath = Path.Combine(AppContext.BaseDirectory, "data", "icon.png"),
    Display = settings.Display,
    VSync = !stress && settings.VSync,
    // Steam's overlay (Shift+Tab, invites, the store) hooks X11 input: on a Wayland desktop it never sees its
    // hotkey over a native Wayland window, so a Steam copy opens through XWayland. itch and dev runs stay native.
    PreferX11 = HeroesNotIncluded.Platforms.GamePlatform.Current is HeroesNotIncluded.Platforms.SteamPlatform,
    SceneSamples = settings.Msaa, // fixed for the run: the world's pipelines are built for it
    MaxFps = stress ? 0 : settings.MaxFps,
    CapUnsyncedFps = !stress,
    UnfocusedMaxFps = stress ? 0 : FrameCap.DefaultUnfocused,
    SkipSplash = args.Contains("--no-splash"), // Debug builds only
    Profile = Environment.GetEnvironmentVariable("SC_PROFILE") == "1" || Environment.GetEnvironmentVariable("NARSIL_PROFILE") == "1",
    // Joysticks, focus and clicks to the log, for a Steam Deck's input (or NARSIL_INPUT_LOG=1 in Steam's launch options).
    InputLog = args.Contains("--input-log") || Environment.GetEnvironmentVariable("NARSIL_INPUT_LOG") == "1",
    // Scripted screenshots: --screenshot shot.png [--screenshot-at 5] [--screenshot-no-ui] [--quit].
    AutoScreenshot = ParseString(args, "--screenshot") is { } shotPath
        ? new AutoScreenshot(shotPath, args.Contains("--screenshot-at") ? ParseFloat(args, "--screenshot-at") : 3f,
            WithUi: !args.Contains("--screenshot-no-ui"), Quit: args.Contains("--quit"))
        : null,
});

Controls.Attach(host);
GameContent.Current.ApplyTo(host); // the mods' art (the host packed the game's own)

// Sound for every mode; silent with no device, or with --mute (screenshot runs, scripts).
if (args.Contains("--mute"))
    Environment.SetEnvironmentVariable("NARSIL_AUDIO", "0");
HeroesNotIncluded.Audio.GameAudio.Open(host);

if (args.Contains("--render-test"))
    host.Run(new RenderTestGame { StressFromArgs = stress });
else if (args.Contains("--art-preview"))
    host.Run(new ArtPreviewGame { DressedOnly = args.Contains("--dressed") }); // --dressed: just the people in their clothes
else if (args.Contains("--host"))
{
    // Co-op host: a saved village (--host <slot or path>), or a new round; as --character if given.
    var saved = PickedCharacter();
    var me = CoopIdentity.Load(host);
    if (saved != null && CoopIdentity.NameOverride == null)
        me = me with { Name = saved.Name };
    var hosting = new HostSettings(me, $"{me.Name}'s village", args.Contains("--port") ? ParseInt(args, "--port") : CoopSession.DefaultPort);
    if (ParseString(args, "--host") is { } hostPath && !hostPath.StartsWith("--"))
    {
        try
        {
            var save = SaveSlots.Load(ResolveSave(hostPath), GameContent.Current.LoadDefs());
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
        host.Run(new VillageGame(ParseSeed(args))
        {
            Adventure = true, Character = saved, HeroName = saved == null ? ParseString(args, "--name") : null,
            Difficulty = ParseString(args, "--difficulty"), HostCoop = hosting, AutoPause = false, FriendlyFire = args.Contains("--friendly-fire"),
            MapName = ParseString(args, "--map") ?? HeroesNotIncluded.Sim.Scenarios.VillageRound.DefaultMap,
            PairsTest = args.Contains("--pairs-test"), // staged for everyone once the round's dealt
        });
    }
}
else if (ParseString(args, "--join") is { } joinAddress)
    host.Run(new TitleGame { AutoJoin = joinAddress });
else if (ParseString(args, "--replay") is { } replayPath)
{
    // --replay PATH: watch a kept round again (the newest with --replay newest).
    if (replayPath == "newest")
        replayPath = HeroesNotIncluded.Replay.ReplayLibrary.List().FirstOrDefault()?.Path ?? replayPath;
    HeroesNotIncluded.Sim.Replay.ReplayFile replay;
    try
    {
        replay = HeroesNotIncluded.Replay.ReplayLibrary.Load(replayPath);
    }
    catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
    {
        Console.WriteLine($"error: {ex.Message}");
        Environment.Exit(1);
        return;
    }
    host.Run(new HeroesNotIncluded.Ui.LoadingScreen(() => new HeroesNotIncluded.Replay.ReplayGame(replay, replayPath), error => new TitleGame { StartError = error }));
}
else if (ParseString(args, "--load") is { } loadPath)
{
    LoadedGame save;
    try
    {
        save = SaveSlots.Load(ResolveSave(loadPath), GameContent.Current.LoadDefs());
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
    // Behind the loading screen like a start from the title (back to the title if it fails).
    host.Run(new HeroesNotIncluded.Ui.LoadingScreen(() => new VillageGame(ParseSeed(args), skipHours: ParseInt(args, "--skip-hours"))
    {
        // Debug/screenshot helpers: starting camera rotation (degrees) and cutaway level.
        StartYawDegrees = args.Contains("--yaw") ? ParseInt(args, "--yaw") : 45,
        // --pitch N: the starting tilt in degrees; --camera-mode: start flying the camera (staged shots).
        StartPitchDegrees = args.Contains("--pitch") ? ParseInt(args, "--pitch") : 0,
        StartCameraMode = args.Contains("--camera-mode"),
        StartCutaway = args.Contains("--cutaway") ? ParseInt(args, "--cutaway") : int.MaxValue,
        StartZoom = args.Contains("--zoom") ? ParseInt(args, "--zoom") : args.Contains("--arena") ? (int)VillageGame.AdventureZoom : 0,
        IgniteForest = args.Contains("--ignite-forest"),
        FurnitureDemo = args.Contains("--furniture-demo"),
        WorkScenesDemo = args.Contains("--work-scenes"),
        BuildingDemo = args.Contains("--building-demo"),
        MapName = ParseString(args, "--map") ?? HeroesNotIncluded.Sim.Scenarios.VillageRound.DefaultMap,
        Freeform = args.Contains("--freeform"),
        HideAllRoofs = args.Contains("--hide-roofs"), // roofs are on by default; --show-roofs is still accepted
        LookAt = ParseString(args, "--look-at") is { } look && look.Split(',') is [var lx, var ly] && int.TryParse(lx, out var x) && int.TryParse(ly, out var y)
            ? new int2(x, y) : null,
        AutoPause = !args.Contains("--no-autopause"),
        Difficulty = ParseString(args, "--difficulty"),
        FriendlyFire = args.Contains("--friendly-fire"),
        // The demos are for looking at, and the arena has no round; everything else walks in as a character.
        Adventure = !args.Contains("--furniture-demo") && !args.Contains("--building-demo") && !args.Contains("--arena"),
        // --arena [FOE]: the practice ring, one raider kind at a time (--weapon NAME to fight with).
        ArenaFoe = args.Contains("--arena") ? (ParseString(args, "--arena") is { } foe && !foe.StartsWith("--") ? foe : HeroesNotIncluded.Sim.Scenarios.Arena.DefaultFoe) : null,
        ArenaWeapon = ParseString(args, "--weapon"),
        // --character: a saved character, credited at the reveal; without it, an unsaved one-off.
        Character = PickedCharacter(),
        HeroName = ParseString(args, "--name"),
        HeroPreset = ParseString(args, "--preset"),
        HeroFolk = Enum.TryParse<HeroesNotIncluded.Sim.Components.Folk>(ParseString(args, "--folk"), true, out var folk) ? folk : null,
        // --raid-test: the raid drill (RaidDrill), as the smith unless --trade says otherwise; --bridge-test as the tinker.
        HeroTrade = ParseString(args, "--trade") ?? (ParseString(args, "--practice") is { } practising && !practising.StartsWith("--") ? practising
            : args.Contains("--raid-test") ? "Blacksmith" : args.Contains("--bridge-test") ? "Tinker" : args.Contains("--fell-test") ? "Carpenter" : null),
        RaidTest = args.Contains("--raid-test"),
        // --fire-test: the fire drill (FireDrill): the inn alight, you at the well with a full bucket.
        FireTest = args.Contains("--fire-test"),
        // --fire-spread N: how many cells it starts with (a blaze for screenshots; 4 by default).
        FireSpread = args.Contains("--fire-spread") ? ParseInt(args, "--fire-spread") : HeroesNotIncluded.Sim.Scenarios.FireDrill.Spread,
        // --dragon-test: the dragon drill (DragonDrill): the dragon coming, you at the well with a full bucket.
        DragonTest = args.Contains("--dragon-test"),
        // --bridge-test: the bridge drill (BridgeDrill): the bridge down, you at the gap with wood to mend it.
        BridgeTest = args.Contains("--bridge-test"),
        // --drill NAME: a trade's moment staged at once (a mod registers its drills, SimRegistry.Drill);
        // --fell-test is the carpenter's woods drill: by a pine with an axe and a saw.
        Drill = ParseString(args, "--drill") ?? (args.Contains("--fell-test") ? "fell" : null),
        // --mode NAME: how the round's played (a mod's mode, say "siege"): a season unless told otherwise.
        Mode = ParseString(args, "--mode"),
        // --pairs-test: every job for two pairs of hands ready at once (PairsDrill): a greatsword's iron, a great
        // log, a wagon's axle and a body on a shrine pallet. With --host, for co-op; alone, Help! brings a villager.
        PairsTest = args.Contains("--pairs-test"),
        // --help-as KIND (striker, keeper, pitman, lifter): a job for two played as the second player, alone. A bot
        // takes the lead's trade and waits at the work for you (PairsLead); you're dealt another trade, stood beside it.
        HelpAs = ParseString(args, "--help-as"),
        AutoHelp = args.Contains("--auto-help"), // with --help-as: F at the work for you as soon as it wants a hand
        // --practice TRADE: the practice yard (Practice): your trade's games, no season, customers when sent for.
        Practice = args.Contains("--practice"),
        // --lesson ID (with --practice): straight into one of the trade's lessons (trades.json "lessons").
        StartLesson = ParseString(args, "--lesson"),
        // --die-at SECONDS: your character drops dead then, to try being a ghost.
        DieAt = args.Contains("--die-at") ? ParseFloat(args, "--die-at") : -1f,
        // --emote KIND [--emote-at SECONDS]: your character shows it then (default 1 s), to see one (Wave, Point...).
        DevEmote = ParseString(args, "--emote"),
        DevEmoteAt = args.Contains("--emote-at") ? ParseFloat(args, "--emote-at") : 1f,
        // --festival-at SECONDS: the festival begins then, to see it without playing to it.
        FestivalAt = args.Contains("--festival-at") ? ParseFloat(args, "--festival-at") : -1f,
        // --keep-replay-at N: keep the round's replay so far after N seconds (then --replay newest).
        KeepReplayAt = args.Contains("--keep-replay-at") ? ParseFloat(args, "--keep-replay-at") : -1f,
        Bots = args.Contains("--bots") ? ParseInt(args, "--bots") : 0,
        // --bot-skill F (with --bots): 1 plays to each bot's temper; more, fewer slips; less, more.
        BotSkill = args.Contains("--bot-skill") ? ParseFloat(args, "--bot-skill") : 1f,
    }, error => new TitleGame { StartError = error }));

HeroesNotIncluded.Audio.GameAudio.Close();
HeroesNotIncluded.Voice.VoiceService.Close();

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
// --character NAME: the saved character of that name (--host, --new-game), once; null without it.
SavedCharacter? PickedCharacter()
{
    if (CharacterRoster.PickOverride is not { } pick)
        return null;
    CharacterRoster.PickOverride = null;
    var found = CharacterRoster.Load(host, GameContent.Current.LoadDefs()).Find(pick);
    if (found == null)
        Console.WriteLine($"warning: no saved character called {pick}");
    return found;
}

static bool LaunchesDirectly(string[] args) =>
    args.Any(a => a is "--new-game" or "--seed" or "--skip-hours" or "--furniture-demo" or "--building-demo" or "--freeform" or "--show-roofs" or "--hide-roofs"
        or "--ignite-forest" or "--raid-test" or "--fire-test" or "--fire-spread" or "--dragon-test" or "--bridge-test" or "--fell-test" or "--pairs-test" or "--help-as" or "--arena" or "--practice" or "--yaw" or "--pitch" or "--camera-mode" or "--keep-replay-at" or "--cutaway" or "--difficulty" or "--map" or "--work-scenes");

// --load and --check-save take a path, or the name of a slot in the saves directory ("quicksave").
static string ResolveSave(string pathOrSlot)
{
    if (File.Exists(pathOrSlot))
        return pathOrSlot;
    var slot = Path.Combine(SaveSlots.DefaultDirectory, pathOrSlot.EndsWith(SaveSlots.Extension) ? pathOrSlot : pathOrSlot + SaveSlots.Extension);
    return File.Exists(slot) ? slot : pathOrSlot; // let the load report the missing file
}

static string? ParseString(string[] args, string flag)
{
    var i = Array.IndexOf(args, flag);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static float ParseFloat(string[] args, string flag)
{
    var i = Array.IndexOf(args, flag);
    return i >= 0 && i + 1 < args.Length && float.TryParse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0f;
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
