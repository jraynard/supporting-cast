using Brutal.ImGuiApi;
using Narsil;
using Narsil.Platform;
using Narsil.Rendering;
using HeroesNotIncluded.Rendering;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded;

public enum ToolKind
{
    Select,
    Chop,
    Mine,
    Dig,
    Deconstruct,
    Harvest,

    /// <summary>Mark an area for dragons to clear with fire.</summary>
    Burn,
    Build,
    Stockpile,
    ClearZone,
    Cancel,
    DevIgnite,
    DevSpawn,

    /// <summary>Click a target for the selected dragon to breathe fire at.</summary>

    /// <summary>Mark the wild animals in an area for hunting.</summary>
    Hunt,

    /// <summary>Mark bank cells next to water as fishing spots.</summary>
    Fish,

    /// <summary>Lay out a preset building (<see cref="Sim.Defs.BuildingDef"/>) with a click.</summary>
    PlaceBuilding,
}

/// <summary>
/// The playable village: runs the simulation at the chosen speed, turns mouse/keyboard input
/// into player orders, and draws the world plus the ImGui interface.
/// </summary>
public sealed partial class VillageGame(ulong seed, int skipHours = 0) : Game
{
    private static readonly int[] SpeedMultipliers = [0, 1, 3, 6];

    private Simulation _sim = null!;
    private WorldRenderer _renderer = null!;
    private readonly Camera3D _camera = new() { Zoom = 40f, VerticalScale = WorldScale.LevelHeight };
    private Camera3DController _cameraController = null!;
    private float _basePanSpeed;
    private QuadRenderer _quads = null!;

    private int _speed = 1;
    private int _speedBeforePause = 1;
    private double _tickAccumulator;
    private float _time;
    private float _peaksResetAt;

    /// <summary>Positions one tick back, so creatures and the camera move smoothly between ticks.</summary>
    private readonly Interpolation _interp = new();
    private readonly Dictionary<string, double> _shownPeaks = [];

    /// <summary>Animation time this frame: real time scaled by game speed (capped so wings don't blur), 0 when paused.</summary>
    private float _animDt;
    private ArtReloader? _artReloader;
    private DefReloader? _defReloader;
    private double _lastTickMs;

    private ToolKind _tool = ToolKind.Select;
    private ThingDef? _toolDef;

    /// <summary>The preset building being placed (<see cref="ToolKind.PlaceBuilding"/>).</summary>
    private BuildingDef? _toolPlan;

    /// <summary>The look last picked for each preset building (C while placing).</summary>
    private readonly Dictionary<BuildingDef, int> _planVariant = [];

    private int PlanVariant(BuildingDef def) => Math.Clamp(_planVariant.GetValueOrDefault(def), 0, def.Variants.Count - 1);

    /// <summary>How the next building is turned (R while building), in quarter turns.</summary>
    private int _buildFacing;
    private Entity _selected = Entity.Null;
    private int3? _dragStart;

    /// <summary>Block (or wall) the dig tool's drag started on; its level is the plane dug.</summary>
    private int3 _dragStartBlock;

    /// <summary>The cell a tool acts on: standable floor under the cursor (or the wall/block itself).</summary>
    private int3 _hoverCell;

    /// <summary>The solid block or wall the cursor ray hit, for digging.</summary>
    private int3 _hoverBlock;

    /// <summary>Adventure mode: the cell under the cursor if walls and doors weren't there (an item behind a wall).</summary>
    private int3 _hoverPastWalls;

    /// <summary>Ctrl is held: build tools act one level above what the cursor points at.</summary>
    private bool _buildUp;

    /// <summary>Where a build tool would place things: the hovered cell, or the one above it with Ctrl held.</summary>
    private bool UsesBuildPlane => _tool is ToolKind.Build or ToolKind.Deconstruct;

    /// <remarks>A roof always goes on the level above what you point at (the tops of the walls round the floor).</remarks>
    private int3 BuildCell =>
        (_buildUp || PlacingRoof) && _hoverCell.Z + 1 < _sim.Map.Depth ? new int3(_hoverCell.X, _hoverCell.Y, _hoverCell.Z + 1) : _hoverCell;

    private bool PlacingRoof => _tool == ToolKind.Build && _toolDef?.IsRoof == true;

    /// <summary>
    /// Every roof cut away (built ones, and the ones drawn over enclosed rooms), and every room's
    /// near walls with them, so you can see into all of them; \ or the toolbar toggles it. Off by
    /// default: roofs are on, but for the building you're in (<see cref="InsideBuilding"/>).
    /// </summary>
    private bool HideRoofs { get; set; }

    /// <summary>The preset building your character's standing in and the level they're on (its roof's off, and what's above them), or Entity.Null.</summary>
    private (Entity Building, int Level) InsideBuilding() =>
        HasAvatar && _sim.World.TryGet<Position>(_avatar, out var p) ? (Buildings.At(_sim, p.Cell), p.Cell.Z) : (Entity.Null, int.MaxValue);

    /// <summary>How wide (cells, on screen) the x-ray circle is.</summary>
    private const float XrayRadius = 1.7f;

    /// <summary>
    /// The x-ray circle's focus: your character's feet in Adventure mode, else the ground under the
    /// cursor (past any wall in front of it), or none with the cursor off the map or over a window.
    /// </summary>
    private (float3, float)? XrayFocus()
    {
        if (HasAvatar && _sim.World.IsAlive(_avatar))
            return (_renderer.DrawPos(_sim, _avatar, _sim.World.Get<Position>(_avatar).Pos), XrayRadius);
        if (!_sim.Map.InBounds(_hoverPastWalls) || Host.Input.MouseCaptured)
            return null;
        return (Position.CenterOf(_hoverPastWalls), XrayRadius);
    }

    private void ToggleRoofs()
    {
        HideRoofs = !HideRoofs;
        Notify(MessageKind.Info, HideRoofs ? "Every roof and front wall cut away (\\)" : "Roofs on, but for the building you're in (\\)");
    }
    /// <summary>
    /// Developer tools: the Debug window, frame times, cell coordinates, the overlays on F1–F4.
    /// On in Debug builds, or with <c>--dev</c>.
    /// </summary>
#if DEBUG
    public static bool DevMode { get; set; } = true;
#else
    public static bool DevMode { get; set; }
#endif

    private bool _devMode => DevMode;

    private bool Paused => _speed == 0;

    /// <summary>How fast time runs while every player sleeps (<see cref="Players.AllAsleep"/>): an 8-hour night in 40 seconds.</summary>
    private const int NightSpeed = 8;

    /// <summary>Everyone playing is asleep in a round, so the night is passing fast.</summary>
    private bool NightPassing => _sim.Round.Live && Players.AllAsleep(_sim);

    /// <summary>The speed time actually runs at: the chosen one, or the night's while everyone sleeps.</summary>
    private int RunMultiplier => Paused ? 0 : NightPassing ? NightSpeed : SpeedMultipliers[_speed];

    public int StartYawDegrees { get; init; } = 45;
    public int StartCutaway { get; init; } = int.MaxValue;

    /// <summary>
    /// Adventure's starting zoom (screen units per cell): closer than the overseer's 40, since the
    /// game is about one person, and close enough that a high-density character sheet (the
    /// Valkyrie's 4x) shows most of its detail.
    /// </summary>
    public const float AdventureZoom = 72f;

    /// <summary>Debug/screenshots: starting zoom (screen pixels per cell); 0 keeps the default.</summary>
    public float StartZoom { get; init; }

    /// <summary>Debug: set the tree cluster east of the landing site alight at the start.</summary>
    public bool IgniteForest { get; init; }

    /// <summary>Debug (<c>--raid-test</c>): arm your character and the muster rack, and bandits come by the north road at once (<see cref="RaidDrill"/>).</summary>
    public bool RaidTest { get; init; }

    /// <summary>Debug/screenshots (<c>--furniture-demo</c>): wide furniture and roofs beside the landing (<see cref="FurnitureShowcase"/>).</summary>
    public bool FurnitureDemo { get; init; }

    /// <summary>Debug/screenshots (<c>--work-scenes</c>): every mini-game's scene in one panel, played by stand-in values.</summary>
    public bool WorkScenesDemo { get; init; }

    /// <summary>Debug/screenshots (<c>--building-demo</c>): every preset building, and one at each phase (<see cref="BuildingShowcase"/>).</summary>
    public bool BuildingDemo { get; init; }

    /// <summary>
    /// Debug (<c>--arena [FOE]</c>): the practice ring instead of a village, this raider kind in each
    /// bout (<see cref="Arena"/>). Null for a village.
    /// </summary>
    public string? ArenaFoe { get; init; }

    /// <summary>Debug (<c>--weapon NAME</c>, with <c>--arena</c>): what you fight with in the ring.</summary>
    public string? ArenaWeapon { get; init; }

    /// <summary><c>--map NAME</c>: the hand-made map (data/defs/maps/) a new village is laid out on.</summary>
    public string MapName { get; init; } = VillageRound.DefaultMap;

    /// <summary>
    /// <c>--freeform</c>: build walls, floors and roofs cell by cell as before, rather than raising
    /// preset buildings (<see cref="BuildingSystem.PresetMode"/>; both stay on the toolbar).
    /// </summary>
    public bool Freeform { get; init; }

    /// <summary>Debug/screenshots (<c>--look-at X,Y</c>): hold the camera on this map cell rather than following your character.</summary>
    public int2? LookAt { get; init; }

    /// <summary>Debug/screenshots (<c>--hide-roofs</c>): start with every roof cut away, as \ does.</summary>
    public bool HideAllRoofs { init => HideRoofs = value; }

    /// <summary>The storyteller's difficulty by def name ("Normal", "Hard"...); null or "Off" sends no raids.</summary>
    public string? Difficulty { get; init; }

    /// <summary>Pause when a danger message arrives (off for demos and screenshots; the player can also turn it off in Settings).</summary>
    public bool AutoPause { get; init; } = true;

    /// <summary>A saved game to continue instead of founding a new village (see <see cref="FromSave"/>).</summary>
    public LoadedGame? Loaded { get; init; }

    /// <summary>Debug/screenshots (<c>--open-saves save|load</c>): open that window once the village is up.</summary>
    public string? OpenSaves { get; init; }

    public static VillageGame FromSave(LoadedGame save, bool autoPause = true) => new(save.Sim.Seed) { Loaded = save, AutoPause = autoPause };

    /// <summary>A new village's world, made ready ahead of <see cref="Load"/> (<see cref="PrepareWorld"/>), and where to look.</summary>
    private (DefDatabase Defs, int3 Focus)? _prepared;

    /// <summary>
    /// The CPU side of starting a new village, off the main thread (<see cref="Ui.LoadingScreen"/>):
    /// the defs, the map and the town, the round dealt. Touches only the sim, the bots and this
    /// game's own fields, never the GPU or ImGui; <see cref="Load"/> does the rest. A save or a
    /// joined village has nothing to do here.
    /// </summary>
    public void PrepareWorld(string dataDirectory)
    {
        if (Coop != null || Loaded != null || _prepared != null)
            return;
        var defs = DefDatabase.LoadDirectory(Path.Combine(dataDirectory, "defs"));
        _prepared = (defs, StartNewVillage(defs));
    }

    public override void Load()
    {
        var defs = Coop?.Defs ?? Loaded?.Sim.Defs ?? _prepared?.Defs ?? DefDatabase.LoadDirectory(Path.Combine(Host.DataDirectory, "defs"));
        ArtAudit.Log(ArtAudit.Run(defs, Host.Atlas.Sprites, Host.Atlas.Sheets));
        Ui.Theme.Apply(Settings.Current.UiScale);
        _settings = new Ui.SettingsWindow(Host);
        Host.ScreenshotSaved += OnScreenshotSaved;
        if (LoadJoinedVillage())
            return;
        if (Loaded is { } save)
        {
            _sim = save.Sim;
            _sim.StrictJobs = false;
            SetUpView(new int3(_sim.Map.Width / 2, _sim.Map.Height / 2, _sim.Map.Depth / 2));
            RestoreView(save.Header.Meta);
            if (CarriedOver is { } carried)
            {
                FindAvatar();
                // A def reload: same village, so the last real save still stands.
                OnSavedOrLoaded(null);
                (_lastSaveTick, _lastSaveTime) = (carried.LastSaveTick, -carried.SecondsSinceSave);
                Notify(MessageKind.Info, carried.Notice);
                return;
            }
            FindAvatar();
            OnSavedOrLoaded(save.SlotName);
            Log.Engine.Info($"Loaded '{save.SlotName}' at {_sim.Clock.TimeString()}");
            StartHostingIfAsked();
            return;
        }
        SetUpView(_prepared?.Focus ?? StartNewVillage(defs));
        FindAvatar();
        OnSavedOrLoaded(null);
        Log.Engine.Info($"Village started with seed {seed}");
        AnnounceTrade();
        StartHostingIfAsked();
        SeatHost();
    }

    /// <summary>Generate the map and found the village (plus any demo setup). Returns where the camera should look.</summary>
    private int3 StartNewVillage(DefDatabase defs)
    {
        if (ArenaFoe != null)
            return StartArena(defs);
        _sim = VillageRound.NewSimulation(defs, seed, MapName);
        _sim.StrictJobs = false; // a buggy job fails itself, not the game
        _sim.Presets.PresetMode = !Freeform; // preset buildings replace freeform walls (the trial; --freeform for the old way)
        var center = VillageRound.Setup(_sim, MapName).Center;

        if (Difficulty is { } difficulty && !difficulty.Equals("Off", StringComparison.OrdinalIgnoreCase))
            _sim.Storyteller.SetDifficulty(_sim, defs.Difficulty(difficulty));
        // A round's season brings its crises, at Normal unless asked otherwise ("Off" for none).
        if (Adventure && !string.Equals(Difficulty, "Off", StringComparison.OrdinalIgnoreCase))
            _sim.Director.Enable(defs.Difficulty(Difficulty ?? "Normal"));
        if (Adventure)
        {
            _sim.Round.OpenLobby();
            if (HostCoop == null)
                center = StartSoloRound(center);
            if (RaidTest && _sim.World.Query<Avatar>().FirstOrDefault(Entity.Null) is { IsNull: false } me)
                RaidDrill.Stage(_sim, me);
        }
        var focus = center;
        if (IgniteForest)
        {
            // The nearest stand of trees to the landing.
            focus = _sim.SpiralFrom(center, 40).FirstOrDefault(c => _sim.Map.Structure[_sim.Map.Idx(c)] is { IsNull: false } s
                && _sim.World.Get<Thing>(s).Def.Harvest?.WorkType == WorkType.Chop, center);
            foreach (var cell in _sim.SpiralFrom(focus, 3))
                _sim.Fire.Ignite(_sim, cell, 1f);
        }
        if (FurnitureDemo)
            focus = FurnitureShowcase.Setup(_sim, center);
        if (BuildingDemo)
            focus = BuildingShowcase.Setup(_sim, center);
        if (skipHours > 0)
        {
            for (var h = 0; h < skipHours; h++)
            {
                _sim.TickMany(SimClock.TicksPerHour);
            }
            Log.Engine.Info($"Skipped ahead {skipHours} hours to {_sim.Clock.TimeString()}");
        }
        return focus;
    }

    private void SetUpView(int3 focus)
    {
        if (LookAt is { } look && _sim.Map.InBounds(look.X, look.Y, 0))
            focus = new int3(look.X, look.Y, _sim.Map.SurfaceZ(look.X, look.Y));
        _camera.Target = new float3(focus.X + 0.5f, focus.Y + 0.5f, focus.Z);
        _camera.Yaw = StartYawDegrees * MathF.PI / 180f;
        if (StartZoom > 0)
            _camera.Zoom = StartZoom;
        else if (HasAvatar)
            _camera.Zoom = AdventureZoom;
        _cameraController = new Camera3DController(_camera)
        {
            Bounds = (float2.Zero, new float2(_sim.Map.Width, _sim.Map.Height)),
            // Zoom without a scroll wheel comes from the zoom actions (HandleCameraActions).
        };
        _basePanSpeed = _cameraController.PanSpeed;
        _renderer = new WorldRenderer(Host.Atlas) { CutawayZ = Math.Clamp(StartCutaway, 1, _sim.Map.Depth - 1), Interp = _interp };
        _followLevel = StartCutaway == int.MaxValue;
        _quads = new QuadRenderer(Host, new int3(_sim.Map.Width, _sim.Map.Height, _sim.Map.Depth), ShaderReload.QuadShaders(Host));
        // Whatever happened during setup and skipping (or before the save) is history: don't auto-pause for it.
        _lastSeenMessages = _sim.Messages.Total;
    }

    public override void Update(float dt)
    {
        _time += dt;
        _animDt = dt * Math.Min(RunMultiplier, 3);
        _animDt = HitFeel(dt, _animDt);
        _artReloader ??= new ArtReloader(Host.Atlas, DevPaths.Watch("SC_WATCH_ART"));
        _artReloader.Update(Host.Input, dt);
        _defReloader ??= new DefReloader(DevPaths.Defs ?? Path.Combine(Host.DataDirectory, "defs"), DevPaths.Watch());
        if (_defReloader.Update(Host.Input, dt))
            ReloadDefs();
        // Window units, not framebuffer pixels: the cursor and ImGui use them, and the projection
        // only needs the aspect and scale, so this also makes the world scale with the display.
        var window = Host.Window.Size;
        _camera.ViewportSize = new float2(Math.Max(1, window.X), Math.Max(1, window.Y));

        RecordRound();

        var input = Host.Input;
        Controls.Map.Update();
        UpdatePadUi();
        HandleWheel();
        HandleKeys(input);
        // WASD walks your character, not the camera: panning it here would also shift the cursor's cell.
        _cameraController.PanSpeed = HasAvatar ? 0f : _basePanSpeed * Settings.Current.CameraPanSpeed;
        // Q is a guard for a character: the view turns on its own actions (Z/X, the d-pad). Without
        // one (god mode, the demos) Z and X are the toolbar's, and the view turns on Q/E as before.
        (_cameraController.RotateLeftKey, _cameraController.RotateRightKey) = HasAvatar ? ((GlfwKey?)null, (GlfwKey?)null) : (GlfwKey.Q, GlfwKey.E);
        HandleCameraActions();
        _cameraController.Update(input, dt);

        UpdateHover(input.MousePosition);
        HandleAvatarInput(input, dt);
        _buildUp = input.IsKeyDown(GlfwKey.LeftControl) || input.IsKeyDown(GlfwKey.RightControl);
        HandleMouse(input, dt);
        if (_coop != null)
            RunCoop(dt);
        else
            RunSimulation(dt);
        UpdateFollowedLevel();
        RecordSimProfile();
        PollAutosave();
        if (_coop != null && FreeMoving)
            _freePredictor.Update(_sim, _avatar, _wish, dt, Paused ? 0 : RunMultiplier);
        _renderer.FreePredicted = _coop != null && FreeMoving ? _freePredictor : null;
        _renderer.AimLine = HasAvatar ? AimLine() : null;
        _renderer.PredictedEntity = _avatar;
        _renderer.Viewer = View.Omniscient ? null : View;
        UpdateOrderBubbles();
        UpdateStationMarks();
        UpdateWork(dt);
        RunBots();
        FollowAvatar(); // after the ticks, so the camera shows this frame's position
        UpdateSound(dt);
    }

    private Audio.SoundDirector? _sound;
    private Audio.WorkSounds? _workSound;

    /// <summary>The world as heard from the camera (panned to the screen), and the work in hand.</summary>
    private void UpdateSound(float dt)
    {
        var audio = Audio.GameAudio.Engine;
        _sound ??= new Audio.SoundDirector(audio);
        _workSound ??= new Audio.WorkSounds(audio);
        var center = _camera.ViewportSize * 0.5f;
        var right = _camera.ScreenToPlane(center + new float2(64, 0), _camera.Target.Z) - _camera.ScreenToPlane(center, _camera.Target.Z);
        // Zoomed out, the town is heard from higher up: quieter, and more of it at once.
        var height = Math.Clamp(240f / Math.Max(1f, _camera.Zoom), 1.5f, 14f);
        _sound.Update(_sim, View, HasAvatar ? _avatar : Entity.Null, _camera.Target, right, height, dt);
        _workSound.Update(HasAvatar ? _work.Session : null);
        Audio.GameAudio.Update(Host, dt);
    }

    private readonly Dictionary<string, double> _profiledTotals = [];
    private readonly Dictionary<string, string> _profiledNames = [];
    private Simulation? _profiledSim;

    /// <summary>This frame's simulation time per system, for the profiler window's game plot.</summary>
    private void RecordSimProfile()
    {
        if (!ReferenceEquals(_profiledSim, _sim))
        {
            _profiledTotals.Clear(); // a load or reload starts the totals over
            _profiledSim = _sim;
        }
        var profiler = Host.Profiler;
        double sum = 0;
        foreach (var (name, total) in _sim.SystemTotals)
        {
            var ms = total - _profiledTotals.GetValueOrDefault(name);
            _profiledTotals[name] = total;
            if (!_profiledNames.TryGetValue(name, out var label))
                _profiledNames[name] = label = "sim/" + name;
            profiler.Record(label, (float)ms);
            if (!name.StartsWith("Derived.", StringComparison.Ordinal)) // the parts of "Derived"
                sum += ms;
        }
        profiler.Record("sim", (float)sum);
    }

    public override void Dispose()
    {
        Host.ScreenshotSaved -= OnScreenshotSaved;
        // Quitting (or travelling) right after dawn mustn't lose a half-written autosave.
        try { _autosaving?.Wait(TimeSpan.FromSeconds(5)); } catch (AggregateException) { }
        _quads?.Dispose();
        DisposeCoop();
        _artReloader?.Dispose();
        _defReloader?.Dispose();
    }

    /// <summary>
    /// Cast the cursor ray into the map (respecting the cutaway). A top face picks the cell above
    /// it, a side face the floor next to it. Floors (and floor blueprints) and walls are picked
    /// themselves. Off the map, fall back to the ground plane. (Building a level up is
    /// <see cref="BuildCell"/>'s job, with Ctrl held.)
    /// </summary>
    private void UpdateHover(float2 mouse)
    {
        (_hoverBlock, _hoverCell) = PickCell(mouse, throughWalls: false);
        // Adventure mode also looks past walls and doors, so F can reach an item they hide.
        // The overseer's x-ray circle follows the same spot (XrayFocus).
        _hoverPastWalls = PickCell(mouse, throughWalls: true).Cell;
    }

    /// <summary>
    /// The cursor ray's hit (block, floor or wall) and the cell a tool acts on there.
    /// <paramref name="throughWalls"/> ignores walls and doors, finding the ground they hide.
    /// </summary>
    private (int3 Block, int3 Cell) PickCell(float2 mouse, bool throughWalls)
    {
        var map = _sim.Map;
        var cut = Math.Min(_renderer.CutawayZ, map.Depth - 1);
        var (origin, dir) = _camera.ScreenToRay(mouse);
        bool Hits(int i)
        {
            if (i / map.LayerSize > cut)
                return false;
            if (map.IsSolid(i))
                return true;
            // A built roof cut away (\\) doesn't catch the ray either: it reaches the room under it.
            // A floor is a thin slab at the bottom of its cell, not a cube: it catches the ray only
            // where the ray crosses it, so clicks land where the floor is drawn, not cells short.
            if (map.Floor[i] is { IsNull: false } floor)
            {
                // The building you're in: its roof and what's above your level aren't drawn, so they don't catch it.
                var open = !HideRoofs && Buildings.Of(_sim, floor) is var of && !of.IsNull && of == _renderer.OpenBuilding.Building;
                if (open && (_sim.World.Get<Thing>(floor).Def.IsRoof || i / map.LayerSize > _renderer.OpenBuilding.Level))
                    return false;
                return (!HideRoofs || !_sim.World.Get<Thing>(floor).Def.IsRoof) && CrossesFloor(map.CellOf(i));
            }
            var s = map.Structure[i];
            // A wall cut down to a stub doesn't stand in the way: the ray reaches the room behind it.
            // Nor does the storey above you in the building you're in, which isn't drawn.
            if (!s.IsNull && !HideRoofs && i / map.LayerSize > _renderer.OpenBuilding.Level && Buildings.Of(_sim, s) is { IsNull: false } wallOf
                && wallOf == _renderer.OpenBuilding.Building)
                return false;
            return !throughWalls && !s.IsNull && _sim.World.Get<Thing>(s).Def.Shape == RenderShape.Block
                && !_renderer.IsStub(_sim, map.CellOf(i));
        }

        bool CrossesFloor(int3 c)
        {
            if (dir.Z >= -1e-6f)
                return false;
            var t = (c.Z + 0.01f - origin.Z) / dir.Z;
            var p = origin + dir * t;
            return (int)MathF.Floor(p.X) == c.X && (int)MathF.Floor(p.Y) == c.Y;
        }

        if (!map.Raycast(origin, dir, _camera.DepthRange * 3, Hits, out var hit, out var normal))
        {
            var p = _camera.ScreenToPlane(mouse, _camera.Target.Z);
            var x = (int)MathF.Floor(p.X);
            var y = (int)MathF.Floor(p.Y);
            var cell = map.InBounds(x, y, 0) ? _renderer.VisibleCell(map, x, y) : new int3(x, y, (int)_camera.Target.Z);
            return (cell, cell);
        }

        if (!map.IsSolid(map.Idx(hit)))
            return (hit, hit); // a floor, wall or door
        var next = new int3(hit.X + normal.X, hit.Y + normal.Y, hit.Z + normal.Z);
        return (hit, map.InBounds(next) && next.Z <= cut
            ? map.NearestStandable(next.X, next.Y, next.Z, 2) ?? next
            : hit); // looking at a cut surface: the block itself
    }

    private void RunSimulation(float dt)
    {
        // Whoever plays a character can't stop the clock, whatever a save or a key last set.
        if (HasAvatar && !_devMode && _speed != 1)
            _speed = 1;
        if (Paused || RoundOver)
        {
            _tickAccumulator = 0;
            _interp.Alpha = 1f; // show exactly where things are (paused orders apply at once)
            return;
        }
        _tickAccumulator += dt * SimClock.TicksPerSecond * RunMultiplier;
        // Never fall into a death spiral: cap the work per frame.
        var ticks = Math.Min((int)_tickAccumulator, 6 * SimClock.TicksPerSecond / 4);
        _tickAccumulator -= ticks;
        if (_tickAccumulator > 60)
            _tickAccumulator = 0;

        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        for (var i = 0; i < ticks; i++)
        {
            _interp.Capture(_sim);
            _sim.Tick();
            if (AutosaveEnabled && _sim.Clock.Day > _lastAutosaveDay && _sim.Clock.HourOfDay >= AutosaveHour)
                Autosave("at dawn");
        }
        if (ticks > 0)
            _lastTickMs = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds / ticks;
        // The leftover fraction of a tick is how far the picture is toward the next one.
        _interp.Alpha = (float)Math.Clamp(_tickAccumulator, 0, 1);
    }

    /// <summary>
    /// Every player action on the sim goes through here as a command, never as a direct change:
    /// that is what lets a lockstep peer replay it. Single player applies it at once, so orders
    /// given while paused show straight away; co-op sends it to be applied in the next turn.
    /// </summary>
    private void Issue(ISimCommand command)
    {
        if (_coop != null)
            _coop.Send(command);
        else
            _sim.ApplyNow(command);
    }

    private void SetSpeed(int speed)
    {
        if (IsCoopClient)
            return; // the host keeps the clock
        if (HasAvatar && !_devMode)
            speed = 1; // one speed for whoever plays a character, and no pausing: the town doesn't wait
        if (speed == 0 && _speed != 0)
            _speedBeforePause = _speed;
        _speed = speed;
    }

    private void HandleKeys(Input input)
    {
        // Space pauses god mode and the demos. Never someone playing a character, dev mode
        // included: the town doesn't wait (and at hands-on work Space is the game's other action).
        if (input.WasKeyPressed(GlfwKey.Space) && !HasAvatar && !Working)
            SetSpeed(Paused ? _speedBeforePause : 0);
        // Ctrl+1–3: plain numbers are the Adventure hotbar (VillageGame.Inventory.cs).
        if (!HasAvatar && (input.IsKeyDown(GlfwKey.LeftControl) || input.IsKeyDown(GlfwKey.RightControl)))
        {
            if (input.WasKeyPressed(GlfwKey.Number1)) SetSpeed(1);
            if (input.WasKeyPressed(GlfwKey.Number2)) SetSpeed(2);
            if (input.WasKeyPressed(GlfwKey.Number3)) SetSpeed(3);
        }
        // Back (Esc, B) puts work down (VillageGame.Work.cs) and closes a menu (VillageGame.Pad.cs)
        // before it does anything else; with a weapon out, the pad's B is a dash.
        if (Controls.Map.Pressed(Act.Back) && !Working && !(WeaponDrawn && Controls.Map.PadPressed(Act.Dash)))
        {
            if (_tool != ToolKind.Select)
                SetTool(ToolKind.Select);
            else if (_showControls)
                _showControls = false;
            else
                _selected = Entity.Null;
        }
        if (_devMode)
        {
            if (input.WasKeyPressed(GlfwKey.F1)) _renderer.Overlay = _renderer.Overlay == Overlay.Temperature ? Overlay.None : Overlay.Temperature;
            if (input.WasKeyPressed(GlfwKey.F2)) _renderer.Overlay = _renderer.Overlay == Overlay.Rooms ? Overlay.None : Overlay.Rooms;
            if (input.WasKeyPressed(GlfwKey.F3)) _renderer.ShowPaths = !_renderer.ShowPaths;
            if (input.WasKeyPressed(GlfwKey.F4)) _renderer.ShowDarkness = !_renderer.ShowDarkness;
        }
        var map = Controls.Map;
        if (map.Pressed(Act.LevelUp)) SetCutaway(_renderer.CutawayZ + 1);
        if (map.Pressed(Act.LevelDown)) SetCutaway(_renderer.CutawayZ - 1);
        if (map.Pressed(Act.LevelFollow)) FollowLevel();
        if (map.Pressed(Act.Roofs)) ToggleRoofs();
        if (input.WasKeyPressed(GlfwKey.F10))
        {
            Settings.Current.Hd2d = !Settings.Current.Hd2d;
            Settings.Save();
            Notify(MessageKind.Info, Settings.Current.Hd2d ? "HD-2D look on (F10)" : "HD-2D look off (F10)");
        }
        if (input.WasKeyPressed(GlfwKey.F6))
        {
            if (CoopForbids("save") is { } no) Notify(MessageKind.Info, no);
            else QuickSave();
        }
        if (input.WasKeyPressed(GlfwKey.F7)) ReloadShaders();
        if (input.WasKeyPressed(GlfwKey.F9))
        {
            if (CoopForbids("load a game") is { } no) Notify(MessageKind.Info, no);
            else QuickLoad();
        }
        HandleJournalKeys(input);
        if (ToolbarOpen)
            HandleToolHotkeys(input);
        if (_tool is ToolKind.Build or ToolKind.PlaceBuilding && input.WasKeyPressed(GlfwKey.R))
            _buildFacing = (_buildFacing + 1) & 3; // turn the blueprint (beds, tables, buildings...)
        if (_tool == ToolKind.Build && input.WasKeyPressed(GlfwKey.C))
            CycleShape();
        if (_tool == ToolKind.PlaceBuilding && _toolPlan != null && input.WasKeyPressed(GlfwKey.C))
            _planVariant[_toolPlan] = (PlanVariant(_toolPlan) + 1) % _toolPlan.Variants.Count;
    }

    /// <summary>
    /// The camera's actions: zoom in any mode, and with a character the quarter turns (without one,
    /// Q/E turn it as ever). Not at hands-on work, where the d-pad's runes are.
    /// </summary>
    private void HandleCameraActions()
    {
        var map = Controls.Map;
        var free = !Working;
        _cameraController.ZoomHeld = free ? (map.Down(Act.ZoomIn) ? 1f : 0f) - (map.Down(Act.ZoomOut) ? 1f : 0f) : 0f;
        if (HasAvatar && free)
            _cameraController.Turn = (map.Pressed(Act.TurnRight) ? 1 : 0) - (map.Pressed(Act.TurnLeft) ? 1 : 0);
    }

    /// <summary>Pick a level by hand: the view stays there until Home hands it back to <see cref="FollowLevel"/>.</summary>
    private void SetCutaway(int z)
    {
        _followLevel = false;
        _renderer.CutawayZ = Math.Clamp(z, 1, _sim.Map.Depth - 1);
    }

    /// <summary>
    /// The cutaway follows your character (<see cref="UpdateFollowedLevel"/>), or shows every level
    /// when you have none. The default: [ and ] take it over by hand.
    /// </summary>
    private bool _followLevel = true;

    private void FollowLevel()
    {
        _followLevel = true;
        UpdateFollowedLevel();
    }

    /// <summary>
    /// Under a floor that isn't a building's (a bridge, an overhang), cut the view at your level
    /// so it doesn't hide you; anywhere else, show everything. In a preset building, only that
    /// building's storeys above you are left out (<see cref="InsideBuilding"/>), so the rest of the
    /// town keeps its roofs. Roofs aren't counted. The 3x3 around you counts too, so a stairwell's
    /// opening doesn't flicker the floor back in on the way up.
    /// </summary>
    private void UpdateFollowedLevel()
    {
        if (!_followLevel)
            return;
        var map = _sim.Map;
        var cut = map.Depth - 1;
        if (HasAvatar && _sim.World.TryGet<Position>(_avatar, out var pos) && InsideBuilding().Building.IsNull)
        {
            var c = pos.Cell;
            for (var z = c.Z + 1; z < map.Depth && cut == map.Depth - 1; z++)
                for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        int x = c.X + dx, y = c.Y + dy;
                        if (x >= 0 && y >= 0 && x < map.Width && y < map.Height && map.HasFloor[map.Idx(x, y, z)])
                            cut = c.Z;
                    }
        }
        _renderer.CutawayZ = Math.Clamp(cut, 1, map.Depth - 1);
    }

    private void SetTool(ToolKind tool, ThingDef? def = null)
    {
        _tool = tool;
        _toolDef = def;
        _dragStart = null;
    }

    private static bool IsAreaTool(ToolKind t) => t is not (ToolKind.Select or ToolKind.DevIgnite or ToolKind.DevSpawn or ToolKind.PlaceBuilding);

    private void HandleMouse(Input input, float dt)
    {
        if (HandleAdventureMouse(input, dt))
            return;
        if (input.WasMousePressed(MouseButton.Right))
        {
            if (_tool != ToolKind.Select)
                SetTool(ToolKind.Select);
            else if (_sim.World.IsAlive(_selected) && _sim.World.TryGet<Mind>(_selected, out var mind) && mind.Drafted)
                Issue(new MoveOrderCommand(_selected, _hoverCell));
            else
                _selected = Entity.Null;
            return;
        }

        if (input.WasMousePressed(MouseButton.Left) && _sim.Map.InBounds(_hoverCell))
        {
            if (IsAreaTool(_tool))
            {
                _dragStart = UsesBuildPlane ? BuildCell : _hoverCell;
                _dragStartBlock = _hoverBlock;
            }
            else
            {
                ApplyClick(input.MousePosition);
            }
        }

        if (_dragStart is { } start && input.WasMouseReleased(MouseButton.Left))
        {
            ApplyArea(start, ClampToMap(_hoverCell));
            _dragStart = null;
        }
    }

    private int3 ClampToMap(int3 t) => new(Math.Clamp(t.X, 0, _sim.Map.Width - 1), Math.Clamp(t.Y, 0, _sim.Map.Height - 1), t.Z);

    private void ApplyClick(float2 mouse)
    {
        switch (_tool)
        {
            case ToolKind.Select:
                _selected = PickEntity(mouse);
                break;
            case ToolKind.DevIgnite:
                Issue(new DevIgniteCommand(_hoverCell));
                break;
            case ToolKind.DevSpawn when _toolDef != null:
                Issue(new DevSpawnCommand(_toolDef, _hoverCell));
                Notify(MessageKind.Info, $"Spawned {_toolDef.Label} at {_hoverCell.X},{_hoverCell.Y},{_hoverCell.Z}. Right-click to put the spawn tool down.");
                break;
            case ToolKind.PlaceBuilding when _toolPlan != null:
                Issue(new PlaceBuildingCommand(_toolPlan, _hoverCell, _buildFacing, PlanVariant(_toolPlan)));
                break;
        }
    }

    /// <summary>A creature under the cursor (by its on-screen body), else the item or structure in the hovered cell.</summary>
    private Entity PickEntity(float2 mouse)
    {
        var best = PickCreature(mouse);
        if (!best.IsNull || !_sim.Map.InBounds(_hoverCell))
            return best;
        var i = _sim.Map.Idx(_hoverCell);
        // Click again on the same tile to cycle from the item to the structure underneath.
        var item = _sim.Map.Item[i];
        if (!item.IsNull && !View.CanSee(item))
            item = Entity.Null;
        var structure = _sim.Map.Structure[i];
        if (!item.IsNull && _selected != item)
            return item;
        if (!structure.IsNull || !item.IsNull)
            return !structure.IsNull ? structure : item;
        // A preset building's plot: the phase going up, else its floor (either shows the building).
        var building = Buildings.At(_sim, _hoverCell);
        if (building.IsNull)
            return Entity.Null;
        var site = Buildings.PhaseSite(_sim, building);
        return !site.IsNull ? site : Buildings.Of(_sim, _sim.Map.Floor[i]) == building ? _sim.Map.Floor[i] : Entity.Null;
    }

    /// <summary>Apply the current tool to the dragged rectangle (as an order every peer applies the same way).</summary>
    private void ApplyArea(int3 a, int3 b)
    {
        AreaTool? tool = _tool switch
        {
            ToolKind.Chop => AreaTool.Chop,
            ToolKind.Mine => AreaTool.Mine,
            ToolKind.Dig => AreaTool.Dig,
            ToolKind.Deconstruct => AreaTool.Deconstruct,
            ToolKind.Harvest => AreaTool.Harvest,
            ToolKind.Burn => AreaTool.Burn,
            ToolKind.Build when _toolDef != null => AreaTool.Build,
            ToolKind.Stockpile => AreaTool.Stockpile,
            ToolKind.ClearZone => AreaTool.ClearZone,
            ToolKind.Cancel => AreaTool.Cancel,
            ToolKind.Hunt => AreaTool.Hunt,
            ToolKind.Fish => AreaTool.Fish,
            _ => null,
        };
        if (tool is not { } t)
            return;
        // Inside a finished preset building, a piece of furniture goes in the nearest free spot for it.
        if (t == AreaTool.Build && _toolDef != null && SlotUnder(a) is { } slot)
        {
            Issue(new FillSlotCommand(slot.Building, slot.Spot.Index, _toolDef));
            return;
        }
        // Build and deconstruct act on the drag's level; dig on the level of the block it started on.
        var z = t == AreaTool.Dig ? _dragStartBlock.Z : a.Z;
        Issue(new AreaOrderCommand(t, new int2(a.X, a.Y), new int2(b.X, b.Y), z, _renderer.CutawayZ, t == AreaTool.Build ? _toolDef : null, _buildFacing));
    }

    // Light uploads are transfers, recorded before the render pass opens.
    public override void PrepareFrame(in FrameContext frame) => _renderer.UpdateLight(_quads, frame, _sim);

    public override bool DrawsScene => true;

    private void ApplyVisualStyle() => VisualStyle.Apply(Host, _quads);

    /// <summary>Nothing goes straight to the screen: the HUD is ImGui, and the world is the scene.</summary>
    public override void Draw(in FrameContext frame) { }

    /// <summary>The world, into the post-processing scene pass (then composited with the HD-2D look).</summary>
    public override void DrawScene(in FrameContext frame)
    {
        ApplyVisualStyle();
        _quads.HideRoofs = HideRoofs;
        _renderer.HideRoofs = HideRoofs;
        _renderer.OpenBuilding = InsideBuilding();
        // With the roofs off, the walls in front of rooms come down too; whatever still stands
        // between the camera and you (or the cursor) thins out round you.
        _renderer.CutawayWalls = HideRoofs;
        _quads.XrayFocus = XrayFocus();
        _quads.Begin(_camera, Math.Min(_renderer.CutawayZ, _sim.Map.Depth - 1), _sim.Lighting.SkyLight(_sim), _renderer.ShowDarkness);
        _renderer.Draw(_quads, _camera, _sim, _selected, _time, _animDt);
        DrawToolPreview();
        _quads.Render(frame);
    }

    /// <summary>Why the blueprint under the cursor can't go there (shown in the hover tooltip), or null.</summary>
    private string? _placeProblem;

    /// <summary>
    /// A player-facing reason for <see cref="Simulation.CanPlaceStructure"/> saying no. Mirrors its
    /// checks in order; only consulted after it has already refused.
    /// </summary>
    private string PlacementProblem(int3 cell, ThingDef def, int facing)
    {
        // A wide one: the first of its cells that won't take it.
        if (Footprint.IsWide(def) && !def.IsFloor)
        {
            foreach (var c in _sim.FootprintCells(def, cell, facing))
                if (!_sim.CanPlaceStructure(c, def.Building?.IsWall == true ? def : null))
                    return $"it takes {def.SizeX}x{def.SizeY} cells, and {CellProblem(c, def)} in one of them";
            return "it doesn't fit here";
        }
        return CellProblem(cell, def);
    }

    private string CellProblem(int3 cell, ThingDef def)
    {
        var map = _sim.Map;
        if (!map.InBounds(cell))
            return "off the map";
        var i = map.Idx(cell);
        if (map.IsSolid(i))
            return "solid rock or earth is in the way";
        if (def.IsRoof)
            return !map.Floor[i].IsNull ? "there's already a floor or roof here"
                : !map.Structure[i].IsNull ? $"{_sim.World.Get<Thing>(map.Structure[i]).Def.Label} is in the way"
                : map.Standable(i) ? "a roof goes over the tops of walls, not on the ground (point at the floor under it)"
                : $"too far from a wall or door to hold it up (at most {def.Building!.SupportSpan} cells out)";
        if (def.IsFloor)
            return !map.Floor[i].IsNull ? "there's already a floor here"
                : $"needs ground or a wall one level down within {def.Building!.SupportSpan} cells to hold it up (and no stairs below)";
        if (!map.Structure[i].IsNull)
            return $"{_sim.World.Get<Thing>(map.Structure[i]).Def.Label} is already here";
        if (def.Building?.Climb == ClimbKind.Ramp && cell.Z + 1 < map.Depth && !map.Floor[i + map.LayerSize].IsNull)
            return "stairs need open space above them";
        if (map.GroundUnder(i) is { } ground)
            return $"can't build on {ground.Label}";
        return def.Building?.IsWall == true ? "needs a wall below it" : "nothing to stand on";
    }

    /// <summary>
    /// A dragged roof shows per cell what it would do: red past the reach of its support (a wall
    /// or door within the span), grey where the cell below is already sheltered (an enclosed
    /// room, a floor above), else the tool's colour.
    /// </summary>
    private uint PreviewColor(int3 cell, uint color)
    {
        if (!PlacingRoof || _toolDef == null || !_sim.Map.InBounds(cell))
            return color;
        if (!_sim.CanPlaceStructure(cell, _toolDef))
            return Rgba.Pack(1f, 0.3f, 0.25f, 0.4f);
        var below = _sim.Map.Idx(cell) - _sim.Map.LayerSize;
        return below >= 0 && _sim.Map.Roofed[below] ? Rgba.Pack(0.55f, 0.55f, 0.55f, 0.35f) : Rgba.Pack(0.5f, 0.9f, 0.5f, 0.35f);
    }

    private void DrawToolPreview()
    {
        _placeProblem = null;
        var map = _sim.Map;
        var white = Host.Atlas.WhiteSprite;
        if (_dragStart is { } a)
        {
            var b = ClampToMap(_hoverCell);
            var color = _tool switch
            {
                ToolKind.Cancel or ToolKind.ClearZone => Rgba.Pack(1f, 0.3f, 0.3f, 0.3f),
                ToolKind.Burn => Rgba.Pack(1f, 0.5f, 0.1f, 0.3f),
                ToolKind.Stockpile => Rgba.Pack(0.3f, 0.7f, 1f, 0.35f),
                _ => Rgba.Pack(1f, 1f, 1f, 0.22f),
            };
            for (var y = Math.Min(a.Y, b.Y); y <= Math.Max(a.Y, b.Y); y++)
            for (var x = Math.Min(a.X, b.X); x <= Math.Max(a.X, b.X); x++)
            {
                if (!map.InBounds(x, y, 0))
                    continue;
                // Dig and build preview the plane they act on; other tools follow the visible surface.
                var height = _tool switch
                {
                    ToolKind.Dig => _dragStartBlock.Z + 1,
                    ToolKind.Build or ToolKind.Deconstruct => a.Z,
                    _ => _renderer.SurfaceHeight(map, _renderer.VisibleCell(map, x, y)),
                };
                _renderer.Decal(white, new float3(x + 0.5f, y + 0.5f, height + 0.05f), 1f, PreviewColor(new int3(x, y, a.Z), color), Quad.Unlit | Quad.Always, translucent: true);
            }
            return;
        }
        if (_tool == ToolKind.Select && _sim.World.IsAlive(_selected) && !Buildings.Of(_sim, _selected).IsNull)
            DrawSlotMarkers(null); // the selected building's free spots
        if (_tool == ToolKind.Select && HasAvatar)
            DrawWalkMarkers();
        if (HasAvatar)
            DrawAvatarBeacon();
        if (!map.InBounds(_hoverCell) || _tool == ToolKind.Select)
            return;

        if (_tool == ToolKind.PlaceBuilding && _toolPlan != null)
        {
            DrawBuildingPreview(_toolPlan, _hoverCell);
            return;
        }

        var target = _tool switch
        {
            ToolKind.Dig => _hoverBlock,
            ToolKind.Build or ToolKind.Deconstruct => BuildCell,
            _ => _hoverCell,
        };
        var floor = Position.CenterOf(target);
        if (map.IsSolid(map.Idx(target)))
            floor.Z += 1;
        if (_tool == ToolKind.Build && _toolDef != null)
            DrawSlotMarkers(_toolDef);
        if (_tool == ToolKind.Build && _toolDef != null && Buildings.At(_sim, target) is { IsNull: false } inside && _sim.Map.InBounds(target))
        {
            // Inside a building, furniture snaps to the nearest free spot for it.
            if (SlotUnder(target) is { } slot)
            {
                var spotGhost = Rgba.Pack(0.6f, 1f, 0.7f, 0.55f);
                if (_toolDef.Model is { } spotModel)
                    _renderer.AddModel(spotModel, slot.Spot.Cell, slot.Spot.Facing, null, spotGhost, Quad.Always, translucent: true, heightScale: WorldScale.HeightOf(_toolDef));
                else
                    _renderer.Cube(Host.Atlas.Get(_toolDef.Sprite), slot.Spot.Cell, spotGhost, Quad.Always, translucent: true);
            }
            else
                _placeProblem = $"Can't place {_toolDef.Label}: {Buildings.PlayerProblem(_sim, _toolDef, target, _buildFacing, out _) ?? $"the {Buildings.Label(_sim.World.Get<PresetBuilding>(inside))} has no free spot for one"}";
            _renderer.Decal(Host.Atlas.Get("ui/selection"), floor + new float3(0, 0, 0.05f), 1f, Rgba.Pack(1, 1, 1, 0.8f), Quad.Unlit | Quad.Always, translucent: true);
            return;
        }
        if (_tool == ToolKind.Build && _toolDef != null)
        {
            var rule = Buildings.PlayerProblem(_sim, _toolDef, target, _buildFacing, out _);
            var ok = rule == null && _sim.CanPlaceStructure(target, _toolDef, _buildFacing);
            if (!ok)
                _placeProblem = rule != null ? $"Can't place {_toolDef.Label}: {rule}" : $"Can't place {_toolDef.Label}: {PlacementProblem(target, _toolDef, _buildFacing)}";
            if (Footprint.IsWide(_toolDef) && !_toolDef.IsFloor)
                foreach (var c in _sim.FootprintCells(_toolDef, target, _buildFacing))
                    if (map.InBounds(c) && c != target)
                        _renderer.Decal(Host.Atlas.Get("ui/selection"), Position.CenterOf(c) + new float3(0, 0, 0.05f), 1f, Rgba.Pack(1, 1, 1, 0.5f), Quad.Unlit | Quad.Always, translucent: true);
            var ghost = ok ? Rgba.Pack(0.6f, 0.8f, 1f, 0.5f) : Rgba.Pack(1f, 0.35f, 0.3f, 0.5f);
            var sprite = Host.Atlas.Get(_toolDef.Sprite);
            if (_toolDef.Model is { } model)
                _renderer.AddModel(model, target, _buildFacing, null, ghost, Quad.Always, translucent: true, heightScale: WorldScale.HeightOf(_toolDef));
            else switch (_toolDef.Shape)
            {
                case RenderShape.Block:
                    _renderer.Cube(sprite, target, ghost, Quad.Always, translucent: true);
                    break;
                case RenderShape.Billboard:
                    _renderer.Billboard(sprite, floor, _toolDef.RenderSize, _toolDef.RenderSize, ghost, Quad.Always, translucent: true);
                    break;
                default:
                    _renderer.Decal(sprite, floor + new float3(0, 0, 0.03f), _toolDef.RenderSize, ghost, Quad.Always, translucent: true);
                    break;
            }
        }
        _renderer.Decal(Host.Atlas.Get("ui/selection"), floor + new float3(0, 0, 0.05f), 1f, Rgba.Pack(1, 1, 1, 0.8f), Quad.Unlit | Quad.Always, translucent: true);
    }

    /// <summary>
    /// Placing a preset building: its plot as tiles (green where it can go, red where it can't),
    /// its walls as ghost blocks and its furniture as ghost models, turned as it would be built.
    /// </summary>
    private void DrawBuildingPreview(BuildingDef plan, int3 anchor)
    {
        var map = _sim.Map;
        var variant = PlanVariant(plan);
        var problem = Buildings.PlacementProblem(_sim, plan, anchor, _buildFacing, variant);
        if (problem != null)
            _placeProblem = $"Can't place {plan.Label}: {problem}";
        var white = Host.Atlas.WhiteSprite;
        var tile = problem == null ? Rgba.Pack(0.5f, 0.9f, 0.5f, 0.35f) : Rgba.Pack(1f, 0.3f, 0.25f, 0.4f);
        var ghost = problem == null ? Rgba.Pack(0.6f, 0.8f, 1f, 0.45f) : Rgba.Pack(1f, 0.35f, 0.3f, 0.45f);
        var parts = plan.Variant(variant).Resolved;
        foreach (var p in Buildings.Plot(map, plan, anchor, _buildFacing))
        {
            if (!map.InBounds(p.Cell))
                continue;
            _renderer.Decal(white, Position.CenterOf(p.Cell) + new float3(0, 0, 0.05f), 1f, tile, Quad.Unlit | Quad.Always, translucent: true);
            if (parts.ForSymbol(p.Symbol) is { } wall)
                _renderer.Cube(Host.Atlas.Get(wall.Sprite), p.Cell, ghost, Quad.Always, translucent: true);
        }
        foreach (var f in plan.Furnish)
        {
            var cell = Buildings.CellOf(map, plan, anchor, _buildFacing, f.At[0], f.At[1]);
            if (map.InBounds(cell) && f.ThingDef.Model is { } model)
                _renderer.AddModel(model, cell, (f.Facing + _buildFacing) & 3, null, ghost, Quad.Always, translucent: true, heightScale: WorldScale.HeightOf(f.ThingDef));
        }
    }

    /// <summary>The free slot for the build tool's def nearest <paramref name="cell"/>, in the finished building there.</summary>
    private (Entity Building, Buildings.SlotSpot Spot)? SlotUnder(int3 cell)
    {
        if (_toolDef == null || !_sim.Map.InBounds(cell))
            return null;
        var building = Buildings.At(_sim, cell);
        return !building.IsNull && Buildings.FreeSlotFor(_sim, building, _toolDef, cell) is { } spot ? (building, spot) : null;
    }

    /// <summary>
    /// Free spots for extras, outlined: those that take <paramref name="def"/> in every finished
    /// building (while it's being placed), or all of the selected building's (null).
    /// </summary>
    private void DrawSlotMarkers(ThingDef? def)
    {
        var w = _sim.World;
        var marker = Host.Atlas.Get("ui/selection");
        foreach (var e in w.Each<PresetBuilding>())
        {
            if (def == null && Buildings.Of(_sim, _selected) != e)
                continue;
            var b = w.Get<PresetBuilding>(e);
            if (b.Phase != BuildPhase.Done || b.Demolishing)
                continue;
            foreach (var spot in Buildings.Slots(_sim, b))
            {
                if (!_sim.Map.InBounds(spot.Cell) || !Buildings.InSlot(_sim, e, spot).IsNull)
                    continue;
                var takes = def ?? spot.Slot.ThingDefs[0];
                if (Buildings.SlotProblem(_sim, e, spot.Index, takes) != null)
                    continue;
                foreach (var c in _sim.FootprintCells(takes, spot.Cell, spot.Facing))
                    if (_sim.Map.InBounds(c))
                        _renderer.Decal(marker, Position.CenterOf(c) + new float3(0, 0, 0.06f), 0.9f, Rgba.Pack(0.6f, 1f, 0.7f, 0.55f), Quad.Unlit | Quad.Always, translucent: true);
            }
        }
    }
}
