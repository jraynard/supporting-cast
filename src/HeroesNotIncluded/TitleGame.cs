using Brutal.ImGuiApi;
using Narsil.Ui;
using Narsil;
using Narsil.Rendering;
using HeroesNotIncluded.Characters;
using HeroesNotIncluded.Rendering;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Net;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

/// <summary>
/// The title screen, dressed by the brand guide (art/brand/brand-guide.html): the stacked logo and
/// tagline, and the menu flush left in the display face (Cinzel), each entry saying what it does
/// with its line from the word bank underneath while it's picked, over Millbrook going about its
/// day in golden-hour light, slowly orbited. Ember stays rare: the logo's "!" is the only ember
/// thing on the screen. Entries start a village (new, hosted, joined or loaded) through
/// <see cref="GameHost.SwitchTo"/>, behind a moment's loading line; or open the settings, the bug
/// report or the credits.
/// </summary>
public sealed class TitleGame : Game
{
    /// <summary>The game's name, as players see it (window, title screen, credits).</summary>
    public const string Title = "Heroes Not Included";

    /// <summary>The per-user folder for settings, saves, screenshots and logs.</summary>
    public const string DataFolder = "Heroes Not Included";

    /// <summary>Linux desktop id: the Wayland app_id / X11 class a <c>heroes-not-included.desktop</c> entry matches (tools/install-desktop-entry.sh).</summary>
    public const string AppId = "heroes-not-included";
    public const ulong DefaultSeed = 1234;

    private Simulation _sim = null!;
    private WorldRenderer _renderer = null!;
    private QuadRenderer _quads = null!;
    private readonly Camera3D _camera = new() { Zoom = 34f, VerticalScale = WorldScale.LevelHeight };
    private float _time;
    private float _accumulator;
    private float _fade;

    /// <summary>Debug/screenshots (<c>--menu-pick "Host game"</c>): press this entry after a few seconds.</summary>
    public string? AutoPick { get; set; }

    /// <summary>Debug/screenshots (<c>--creator character</c>): open the character creator at once.</summary>
    public string? AutoCreator { get; init; }

    /// <summary>Debug/screenshots (<c>--creator character --preset Wanderer</c>): start the creator on a preset.</summary>
    public string? AutoPreset { get; init; }

    private SettingsWindow _settings = null!;

    /// <summary>A submenu (Adventure's difficulties, Load Game's saves) shown instead of the main menu.</summary>
    private MenuItem[]? _submenu;
    private string? _submenuTitle;

    /// <summary>Where a submenu's Back leads: the submenu it was opened from (More…, Play with friends), or the main menu when null.</summary>
    private Action? _submenuBack;

    /// <summary>The list on show changed: its first entry takes the pick, so a pad's first press moves from something you can see.</summary>
    private object? _focusedList;
    private MenuItem[] _items = [];
    private DefDatabase _defs = null!;

    /// <summary>Why the last load failed, shown under the menu.</summary>
    private string? _error;

    private enum MenuAction { Start, Settings, Exit, NewGame, LoadGame, Back, Host, HostNew, HostSave, Join, Characters, BackToCreator, Report, Credits, Practice, Replays, Friends, More }

    /// <summary>The Credits window (BRUTAL License 2.1: its copyright notice; and the fonts' licences).</summary>
    private readonly CreditsWindow _credits = new(Title, "Somebody has to keep the lights on.", GameCredits.People);
    private bool _showCredits;

    /// <summary>Why the last start came back (a save that wouldn't read, say), shown on the title.</summary>
    public string? StartError { get; init; }

    /// <summary>Into a village behind the loading screen (<see cref="LoadingScreen"/>): back here with what went wrong if it fails.</summary>
    private void Begin(Func<Game> start) => Host.SwitchTo(new LoadingScreen(start, error => new TitleGame { StartError = error }));

    /// <summary>Adventure's character creator, shown instead of the menu while open.</summary>
    private CharacterCreator _creator = null!;
    private bool _creating;

    /// <summary>The Report a Bug panel (where the logs are, where to send them), shown instead of the menu while open.</summary>
    private bool _reporting;
    private string? _logsError;

    /// <summary>What the character being picked or made is for: every way into a village starts with who you are.</summary>
    private enum Purpose { Solo, Host, Join, Manage, Practice }

    private Purpose _purpose;

    /// <summary>Saved characters (<see cref="CharacterRoster"/>): picked before any game, or looked after on the Characters screen.</summary>
    private CharacterRoster _roster = null!;
    private CharacterPicker _picker = null!;
    private bool _picking;

    /// <summary>The saved character the creator is editing; null: a new one.</summary>
    private Guid? _editing;

    /// <summary>Co-op's Join panel, shown instead of the menu while open.</summary>
    private CoopMenu? _coopMenu;
    private bool _joining;

    /// <summary><c>--join address</c>: open the Join panel and connect straight away.</summary>
    public string? AutoJoin { get; set; }

    /// <summary>
    /// An entry: a label saying what it does, and the line underneath (the word bank's, or what it'll do).
    /// <see cref="Then"/>: a step that leads on to another menu rather than starting a game (the town, then the trade).
    /// </summary>
    private sealed record MenuItem(string Label, string Hint, Func<Game>? Start, MenuAction Action = MenuAction.Start, Action? Then = null)
    {
        /// <summary>Marked as the place to start (the tutorial, for someone who's done nothing yet).</summary>
        public bool Nudge { get; init; }
    }

    /// <summary>Nobody saved has finished a lesson or a round: whoever's here is new, and the tutorial's where to start.</summary>
    private bool Newcomer => _roster.Characters.All(c => c.Progress.Lessons.Count == 0 && c.Progress.Rounds == 0);

    /// <summary>How many saves the Load Game list shows (the in-game Load window lists them all).</summary>
    private const int MaxListedSaves = 8;

    private MenuItem[] MainItems()
    {
        var newest = SaveSlots.Newest();
        return
        [
            // Continue only when there's something to continue: a disabled entry at the top made a pad's first press skip it.
            .. (newest is { } last ? new[] { new MenuItem("Continue", $"{last.Name}: {last.Summary}", () => FromSave(last)) } : Array.Empty<MenuItem>()),
            new("New round", "Just you and the regulars.", null, MenuAction.NewGame),
            Newcomer
                ? new("Tutorial", "New here? Start here: learn a trade, step by step.", null, MenuAction.Practice) { Nudge = true }
                : new("Tutorial", "Learn a trade, step by step, with nobody waiting.", null, MenuAction.Practice),
            new("Play with friends", "Open the doors, or pull up a stool.", null, MenuAction.Friends),
            new("Settings", "House rules.", null, MenuAction.Settings),
            new("More", "Characters, saves, replays, bugs, credits.", null, MenuAction.More),
            new("Quit", "Close up for the night.", null, MenuAction.Exit),
        ];
    }

    /// <summary>Co-op: host a village or join one.</summary>
    private MenuItem[] FriendsItems() =>
    [
        new("Host game", "Open the doors.", null, MenuAction.Host),
        new("Join game", "Pull up a stool.", null, MenuAction.Join),
        new("Back", "Back to the main menu.", null, MenuAction.Back),
    ];

    /// <summary>
    /// The rest of the title, out of the main list so it fits a Steam Deck's 800 lines without
    /// scrolling: playtesters on one found the long list hard to get round.
    /// </summary>
    private MenuItem[] MoreItems()
    {
        var anySave = SaveSlots.Newest() != null;
        return
        [
            new("Characters", "Who you've been.", null, MenuAction.Characters),
            new("Load game", anySave ? "Pick up where you left off." : "Nothing saved yet.", null, anySave ? MenuAction.LoadGame : MenuAction.Start),
            new("Replays", Replay.ReplayLibrary.List().Count == 0 ? "Finish a round and it's kept here." : "Watch a season again, from anywhere.", null, MenuAction.Replays),
            new("Report a bug", "Something broke? Tell us how.", null, MenuAction.Report),
            new("Credits", "Everyone who swept up.", null, MenuAction.Credits),
            new("Back", "Back to the main menu.", null, MenuAction.Back),
        ];
    }

    /// <summary>Show <paramref name="items"/> under <paramref name="title"/>; its Back (or B) runs <paramref name="back"/>, or returns to the main menu.</summary>
    private void OpenSubmenu(MenuItem[] items, string title, Action? back = null) =>
        (_submenu, _submenuTitle, _submenuBack) = (items, title, back);

    private void OpenFriends() => OpenSubmenu(FriendsItems(), "Play with friends");

    private void OpenMore() => OpenSubmenu(MoreItems(), "More");

    /// <summary>A submenu's Back: to the one it came from, or the main menu.</summary>
    private void SubmenuBack()
    {
        var back = _submenuBack;
        (_submenu, _submenuTitle, _submenuBack) = (null, null, null);
        back?.Invoke();
    }

    private MenuItem[] SaveItems()
    {
        var now = DateTime.UtcNow;
        return SaveSlots.List()
            .Where(s => s.Header != null)
            .Take(MaxListedSaves)
            .Select(s => new MenuItem(s.Name, $"{s.Summary} · saved {SaveSlots.Ago(s.Modified, now)}", () => FromSave(s)))
            .Append(new MenuItem("Back", "Back to more.", null, MenuAction.Back))
            .ToArray();
    }

    /// <summary>The replays kept, newest first: one made on another build of the game won't play.</summary>
    private MenuItem[] ReplayItems()
    {
        var build = CoopSession.BuildTag(_defs);
        return Replay.ReplayLibrary.List()
            .Take(MaxListedSaves)
            .Select(r =>
            {
                var meta = r.Header.Meta;
                var label = $"{meta.GetValueOrDefault("date", r.Written.ToString("yyyy-MM-dd HH:mm"))} · {meta.GetValueOrDefault("map", "the town")}";
                if (r.Header.BuildTag != build)
                    return new MenuItem(label, "Made on another version of the game: it plays only there.", null);
                var hint = string.Join(" · ", new[] { meta.GetValueOrDefault("headline"), meta.GetValueOrDefault("players") }.Where(t => !string.IsNullOrEmpty(t)));
                var path = r.Path;
                return new MenuItem(label, hint.Length > 0 ? hint : "A season, kept.", () => new Replay.ReplayGame(Replay.ReplayLibrary.Load(path), path));
            })
            .Append(new MenuItem("Back", "Back to more.", null, MenuAction.Back))
            .ToArray();
    }

    private MenuItem[] HostItems()
    {
        var anySave = SaveSlots.Newest() != null;
        return
        [
            new("New round", "Open a lobby: friends say which trades they want, you deal.", null, MenuAction.HostNew),
            new("A saved village", anySave ? "Open one of your saved villages to friends." : "Nothing saved yet.", null, anySave ? MenuAction.HostSave : MenuAction.Start),
            new("Back", "Back to playing with friends.", null, MenuAction.Back),
        ];
    }

    private MenuItem[] HostSaveItems() =>
        SaveSlots.List()
            .Where(s => s.Header != null)
            .Take(MaxListedSaves)
            .Select(s => new MenuItem(s.Name, $"{s.Summary}: open it to friends", () =>
            {
                var save = SaveSlots.Load(s.Path, _defs);
                return new VillageGame(save.Sim.Seed) { Loaded = save, HostCoop = HostSettingsFor() };
            }))
            .Append(new MenuItem("Back", "Back to hosting.", null, MenuAction.Back))
            .ToArray();

    /// <summary>
    /// A solo round's last step: the trade to be dealt. The bots fill the others, so any choice
    /// leaves a full cast; "Any" lets the deal decide as before.
    /// </summary>
    private MenuItem[] TradeItems(SavedCharacter c, string map)
    {
        Game Start(string? trade) => new VillageGame(DefaultSeed) { Adventure = true, Character = c, HeroTrade = trade, MapName = map };
        return _defs.AllTrades
            .Select(t => new MenuItem(char.ToUpperInvariant(t.Label[0]) + t.Label[1..], t.Description, () => Start(t.Name)))
            .Append(new MenuItem("Any", "Let the deal pick a trade for you.", () => Start(null)))
            .Append(new MenuItem("Back", "Back to who you'll be.", null, MenuAction.BackToCreator))
            .ToArray();
    }

    /// <summary>
    /// The tutorial's trades: those with a workplace and games of their own, each with how far this
    /// character has got in it. It runs in the practice yard: no bots, no season.
    /// </summary>
    private MenuItem[] PracticeItems(SavedCharacter c)
    {
        Game Start(string trade) => new VillageGame(DefaultSeed) { Adventure = true, Practice = true, Character = c, HeroTrade = trade };
        string Progress(Sim.Defs.TradeDef t)
        {
            var learned = c.Progress.Lessons;
            if (Sim.Scenarios.Lessons.Tutorial.CoreLearned(t, learned.Contains))
            {
                var done = t.Lessons.Count(l => learned.Contains(Sim.Scenarios.Lessons.LessonRun.KeyOf(t, l)));
                return $"Learned ({done} of {t.Lessons.Count} chapters). ";
            }
            return "";
        }
        return _defs.AllTrades
            .Where(t => t.HasWorkplace)
            .Select(t => new MenuItem(char.ToUpperInvariant(t.Label[0]) + t.Label[1..], Progress(t) + t.Description, () => Start(t.Name)))
            .Append(new MenuItem("Back", "Back to the main menu.", null, MenuAction.Back))
            .ToArray();
    }

    /// <summary>Hosting as <paramref name="c"/>: friends see the character's name, and the village is theirs.</summary>
    private HostSettings HostSettingsFor(SavedCharacter? c = null)
    {
        var me = CoopIdentity.Load(Host);
        if (c != null && CoopIdentity.NameOverride == null)
            me = me with { Name = c.Name };
        return new HostSettings(me, $"{me.Name}'s village");
    }

    /// <summary>The creator starts from a saved character.</summary>
    private void Prefill(SavedCharacter c)
    {
        _creator.Load(c.Name, c.Look, c.Traits, c.Clothes);
    }

    /// <summary>
    /// Who will you be, for <paramref name="purpose"/>: the picker when there's anyone saved, else
    /// the creator. The Characters screen always opens on the picker.
    /// </summary>
    private void ChooseCharacter(Purpose purpose)
    {
        (_purpose, _editing, _submenu, _submenuTitle, _submenuBack, _error) = (purpose, null, null, null, null, null);
        _picker.Showing = purpose == Purpose.Manage ? CharacterPicker.Mode.Manage : CharacterPicker.Mode.Choose;
        _creator.ContinueLabel = purpose == Purpose.Manage ? "Save" : "Continue";
        if (_roster.Characters.Count > 0 || purpose == Purpose.Manage)
            _picking = true;
        else
            _creating = true;
    }

    /// <summary>The creator's character, saved: the one being edited keeps its id and all it's done.</summary>
    private SavedCharacter? SaveCreated()
    {
        var made = (_editing is { } id ? _roster.Get(id) : null) ?? new SavedCharacter();
        made = made with { Name = _creator.Name, Look = _creator.Look, Traits = _creator.Traits.ToList(), Clothes = _creator.Clothes.ToList() };
        var saved = _purpose == Purpose.Manage ? _roster.Update(made) : _roster.Put(made);
        if (saved == null)
            _error = $"The roster's full ({CharacterRoster.Max}): let someone go first.";
        return saved;
    }

    /// <summary>The towns a round can be played in: maps with an inn (the practice ring isn't one), Millbrook first.</summary>
    private List<Sim.Defs.MapDef> Towns() => _defs.AllMaps
        .Where(m => m.Buildings.Exists(b => b.BuildingDef.Has(Sim.Defs.TownRole.Tavern)))
        .OrderBy(m => m.Name == VillageRound.DefaultMap ? 0 : 1).ToList();

    /// <summary>
    /// Which town, then <paramref name="then"/> with its name; straight on to it where there's only the one.
    /// </summary>
    private void ChooseTown(Action<string> then, MenuAction back)
    {
        var towns = Towns();
        if (towns.Count <= 1)
        {
            then(towns.FirstOrDefault()?.Name ?? VillageRound.DefaultMap);
            return;
        }
        OpenSubmenu(towns.Select(m => new MenuItem(m.Label, m.Description, null, Then: () => then(m.Name)))
            .Append(new MenuItem("Back", "Back.", null, back)).ToArray(), "Which town?");
    }

    /// <summary><paramref name="c"/> is who you'll be: on to what they were chosen for.</summary>
    private void Chosen(SavedCharacter c)
    {
        (_picking, _creating) = (false, false);
        c = _roster.Put(c) ?? c; // most recently used
        Prefill(c);
        switch (_purpose)
        {
            case Purpose.Solo:
                ChooseTown(map => OpenSubmenu(TradeItems(c, map), "Which trade will you take?"), MenuAction.BackToCreator);
                break;
            case Purpose.Practice:
                OpenSubmenu(PracticeItems(c), "Which trade will you learn?");
                break;
            case Purpose.Host:
                ChooseTown(map => HostAs(c, map), MenuAction.Back);
                break;
            case Purpose.Join:
                (_coopMenu ??= new CoopMenu(Host, _defs)).Playing = c;
                _joining = true;
                break;
        }
    }

    /// <summary>Host a new village as <paramref name="c"/>.</summary>
    private void HostAs(SavedCharacter c, string map) => Begin(() => new VillageGame((ulong)Random.Shared.Next(1, 1_000_000))
    {
        Adventure = true, Character = c, HostCoop = HostSettingsFor(c), MapName = map,
    });

    /// <summary>Load a save for <see cref="Press"/>; a <see cref="Sim.Saving.SaveException"/> keeps us on the title.</summary>
    private Game FromSave(SaveSlot slot) => VillageGame.FromSave(SaveSlots.Load(slot.Path, _defs));

    public override void Load()
    {
        Theme.Apply(Settings.Current.UiScale);
        _error = StartError;
        _settings = new SettingsWindow(Host);
        var defs = _defs = DefDatabase.LoadDirectory(Path.Combine(Host.DataDirectory, "defs"));
        _creator = new CharacterCreator(Host, defs);
        _roster = CharacterRoster.Load(Host, defs);
        _picker = new CharacterPicker(Host, _roster, defs);
        if (_roster.Last is { } last)
            Prefill(last);
        _items = MainItems();
        // The village behind the menu, its people about their day (no round: nobody's dealt a trade).
        _sim = VillageRound.NewSimulation(defs, (ulong)Random.Shared.Next(1, 100_000));
        _sim.StrictJobs = false; // a buggy job fails itself, not the game
        _sim.Presets.PresetMode = true; // as a round's town is
        var town = VillageRound.Setup(_sim);
        _sim.RebuildDerivedIfNeeded();

        // Turning about a point between the square and the inn, so the inn, the town's heart, stays in the frame.
        var center = new float3(town.Center.X + 0.5f, town.Center.Y + 0.5f, town.Center.Z);
        if (_sim.World.TryGet<PresetBuilding>(town.Tavern, out var inn))
            center += (new float3(inn.Anchor.X + 0.5f, inn.Anchor.Y + 0.5f, center.Z) - center) * 0.4f;
        _camera.Target = center;
        _camera.Yaw = 0.6f;
        _renderer = new WorldRenderer(Host.Atlas) { ShowNames = false, ShowBubbles = false };
        // Roofs on: the title's view of the village is from outside, with nobody inside to open up.
        _quads = new QuadRenderer(Host, new int3(_sim.Map.Width, _sim.Map.Height, _sim.Map.Depth), ShaderReload.QuadShaders(Host)) { HideRoofs = false, SmoothAll = true };
        if (AutoCreator != null)
        {
            _creating = true;
            if (AutoPreset != null)
                _creator.ApplyPreset(AutoPreset);
        }
        if (AutoJoin is { } address)
        {
            _joining = true;
            _coopMenu ??= new CoopMenu(Host, _defs);
            var wanted = CharacterRoster.PickOverride is { } pick ? _roster.Find(pick) : null;
            CharacterRoster.PickOverride = null;
            _coopMenu.Playing = wanted ?? _roster.Last;
            _coopMenu.Connect(address);
        }
    }

    /// <summary>
    /// The light the title is drawn in, whatever the hour in its village: golden, a little low,
    /// the brand's "common room at dusk" without a night falling on the menu.
    /// </summary>
    private const float TitleSky = 0.82f;

    /// <summary>How far the title's grade leans gold past the player's own (the HD-2D grade's warmth).</summary>
    private const float TitleGold = 1.6f;

    public override void Update(float dt)
    {
        _time += dt;
        _fade = Math.Min(1f, _fade + dt * 1.5f);
        var window = Host.Window.Size;
        _camera.ViewportSize = new float2(Math.Max(1, window.X), Math.Max(1, window.Y));
        _camera.Yaw += dt * 0.025f; // a slow orbit
        Audio.GameAudio.Engine.PlayMusic(Audio.Moods.Title);
        Audio.GameAudio.Update(Host, dt);
        if (Host.Input.WasKeyPressed(GlfwKey.F7))
            _error = ShaderReload.Reload(Host, _quads) is { } shaderError ? $"Shader error: {shaderError}" : null;
        UpdatePad();

        if (_joining)
            _coopMenu?.Update(dt);
        _accumulator += dt * SimClock.TicksPerSecond;
        var ticks = Math.Min((int)_accumulator, 30);
        _accumulator -= ticks;
        for (var i = 0; i < ticks; i++)
            _sim.Tick();
    }

    private string? _padFocused;

    /// <summary>The menu's window has been drawn once, so focusing it by name finds it.</summary>
    private bool _titleDrawn;

    /// <summary>
    /// The title is all menus: a pad drives them (ImGui's navigation) unless the controls page is
    /// waiting for a button. The newest window takes the focus, and B closes it or leaves a submenu.
    /// </summary>
    private void UpdatePad()
    {
        Controls.Map.Update();
        Host.ImGui.GamepadNav = !_settings.Capturing;
        var top = _settings.IsOpen ? "Settings" : _showCredits ? "###credits" : "##title";
        // Only once the window's there to take it: asked before its first frame, the focus went nowhere and wasn't asked again.
        if (top != _padFocused && (top != "##title" || _titleDrawn))
        {
            ImGui.SetWindowFocus(top);
            _padFocused = top;
        }
        var back = false;
        foreach (var b in Controls.Map.Bindings(Act.Back))
            back |= b.IsPad && Host.Gamepad.WasPressedRaw((Narsil.Platform.PadButton)b.Code);
        if (!back || _settings.Capturing || ImGui.IsPopupOpen(""u8, ImGuiPopupFlags.AnyPopup))
            return;
        if (_settings.IsOpen)
            _settings.Close();
        else if (_showCredits)
            _showCredits = false;
        else if (_submenu != null && !_joining && !_picking && !_creating && !_reporting)
            SubmenuBack();
        else if (_reporting)
            _reporting = false;
    }

    // Light uploads are transfers, recorded before the render pass opens.
    public override void PrepareFrame(in FrameContext frame) => _renderer.UpdateLight(_quads, frame, _sim);

    public override bool DrawsScene => true;

    public override void Draw(in FrameContext frame) { }

    public override void DrawScene(in FrameContext frame)
    {
        VisualStyle.Apply(Host, _quads);
        if (Settings.Current.Hd2d)
            Host.Post.Settings = Host.Post.Settings with { Warmth = Host.Post.Settings.Warmth + TitleGold, Vignette = Math.Max(Host.Post.Settings.Vignette, 0.4f) };
        _quads.Begin(_camera, _sim.Map.Depth - 1, TitleSky, true);
        _renderer.Draw(_quads, _camera, _sim, Entity.Null, _time, Host.FrameTimeMs / 1000f);
        _quads.Render(frame);
    }

    public override void DrawUi()
    {
        var io = ImGui.GetIO();
        var screen = io.DisplaySize;
        var bg = ImGui.GetBackgroundDrawList(ImGui.GetMainViewport());

        // A cellar-dark wash from the left so the menu reads over the scenery, plus a fade-in.
        var a = float2.Zero;
        var b = new float2(screen.X * 0.55f, screen.Y);
        uint dark = Theme.U8(Theme.Cellar with { W = 0.9f });
        uint clear = Theme.U8(Theme.Cellar with { W = 0f });
        bg.AddRectFilledMultiColor(in a, in b, dark, clear, clear, dark);
        if (_fade < 1f)
        {
            var full = screen;
            bg.AddRectFilled(in a, in full, Theme.U8(new float4(0, 0, 0, 1f - _fade)), 0f, ImDrawFlags.None);
        }

        var pos = new float2(screen.X * 0.08f, screen.Y * 0.5f);
        float2? pivot = new float2(0f, 0.5f);
        ImGui.SetNextWindowPos(in pos, ImGuiCond.Always, in pivot);
        ImGui.SetNextWindowBgAlpha(0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.Begin("##title"u8, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove);
        _titleDrawn = true;

        DrawLogo(screen);
        var dim = Theme.Parchment;
        ImGui.TextColored(dim, "Somebody has to keep the lights on."u8);
        var gap = new float2(1, Gui.S(24));
        ImGui.Dummy(in gap);
        // What went wrong last (a save that wouldn't read, a start that failed): up here, where a
        // short screen can't push it off the bottom.
        if (_error != null)
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Gui.S(340));
            ImGui.TextColored(Theme.Bad, Narsil.Ui.ImGuiText.Escape(_error));
            ImGui.PopTextWrapPos();
            ImGui.Dummy(in gap);
        }

        if (_joining && _coopMenu != null)
        {
            ImGui.TextColored(dim, "Join a village"u8);
            switch (_coopMenu.Draw())
            {
                case CoopMenu.Result.Joined when _coopMenu.Joined is { Sim: { } joinedSim } session:
                    _joining = false;
                    var playing = _coopMenu.Playing;
                    Begin(() => new VillageGame(joinedSim.Seed) { Coop = session, Character = playing });
                    break;
                case CoopMenu.Result.Change:
                    _joining = false;
                    ChooseCharacter(Purpose.Join);
                    break;
                case CoopMenu.Result.Back:
                    _joining = false;
                    break;
            }
        }
        else if (_reporting)
            DrawReport(dim);
        else if (_picking)
        {
            ImGui.TextColored(dim, _purpose == Purpose.Manage ? "Your characters"u8 : "Who will you be?"u8);
            switch (_picker.Draw(_time))
            {
                case CharacterPicker.Result.Use:
                    Chosen(_picker.Picked!);
                    break;
                case CharacterPicker.Result.Edit:
                    Prefill(_picker.Picked!);
                    (_editing, _picking, _creating) = (_picker.Picked!.Id, false, true);
                    break;
                case CharacterPicker.Result.New:
                    (_editing, _picking, _creating) = (null, false, true);
                    break;
                case CharacterPicker.Result.Back:
                    _picking = false;
                    // Changing who you'll join as, and thinking better of it: back to the Join panel as you were.
                    _joining = _purpose == Purpose.Join && _coopMenu?.Playing != null;
                    break;
            }
        }
        else if (_creating)
        {
            ImGui.TextColored(dim, _editing != null && _roster.Get(_editing.Value) is { } edited ? edited.Name : "Who are you?");
            switch (_creator.Draw(_time))
            {
                case CharacterCreator.Result.Continue when SaveCreated() is { } made:
                    _creating = false;
                    if (_purpose == Purpose.Manage)
                        _picking = true;
                    else
                        Chosen(made);
                    break;
                case CharacterCreator.Result.Back:
                    _creating = false;
                    if (_roster.Characters.Count > 0 || _purpose == Purpose.Manage)
                        _picking = true;
                    break;
            }
        }
        else
        {
            if (_submenuTitle != null)
                ImGui.TextColored(dim, _submenuTitle);
            // Every hint always shown, each entry its own fixed height: the list never changes size as the pick moves.
            var list = _submenu ?? _items;
            // A new list on show, with a pad in hand: its first live entry takes the pick, plainly. Left
            // to itself ImGui chose one unseen, and the first press down skipped past it.
            var focusFirst = !ReferenceEquals(list, _focusedList) && Host.Gamepad.Connected && _padFocused == "##title";
            if (focusFirst)
                _focusedList = list;
            foreach (var item in list)
            {
                if (focusFirst && Enabled(item))
                {
                    ImGui.SetKeyboardFocusHere(0);
                    focusFirst = false;
                }
                MenuButton(item);
            }
        }

        ImGui.Dummy(in gap);
        ImGui.TextColored(dim with { W = 0.7f }, $"Playtest build {BuildInfo.Label}. Built with BRUTAL.");
        ImGui.End();
        ImGui.PopStyleVar(1);
        _settings.Draw();
        _credits.Draw(ref _showCredits, $"Playtest build {BuildInfo.Label}");

        if (AutoPick != null && _time > 3f && (_submenu ?? _items).FirstOrDefault(i => i.Label.Equals(AutoPick, StringComparison.OrdinalIgnoreCase)) is { } pick)
        {
            Log.Engine.Info($"Title screen: auto-picking '{AutoPick}'");
            AutoPick = null;
            Press(pick);
        }
    }

    /// <summary>
    /// For playtesters: where reports go, the version line to quote, and the logs folder (crash
    /// reports and desync dumps land there too).
    /// </summary>
    private void DrawReport(float4 dim)
    {
        ImGui.TextColored(dim, "Report a bug"u8);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Gui.S(420));
        if (BuildInfo.Community.Length > 0)
            ImGuiText.Wrapped($"Post it in #bug-reports on our Discord: {BuildInfo.Community}");
        else
            ImGuiText.Wrapped("Tell whoever gave you the game.");
        ImGuiText.Wrapped($"Say what you were doing, and include this line: {BuildInfo.Label}.");
        ImGuiText.Wrapped("Attach the newest log file, and any crash or desync files beside it:");
        ImGui.TextColored(dim, ImGuiText.Escape(_logsError ?? BuildInfo.LogDirectory));
        ImGui.PopTextWrapPos();
        float2? size = new float2(Gui.S(200), 0);
        if (ImGui.Button("Open logs folder"u8, in size))
            _logsError = SettingsWindow.OpenFolder(BuildInfo.LogDirectory);
        if (ImGui.Button("Back"u8, in size))
            _reporting = false;
    }

    private void Press(MenuItem item)
    {
        switch (item.Action)
        {
            case MenuAction.Settings:
                _settings.Toggle();
                break;
            case MenuAction.Exit:
                Host.Window.ShouldClose = true;
                break;
            case MenuAction.NewGame:
                ChooseCharacter(Purpose.Solo);
                break;
            case MenuAction.Characters:
                ChooseCharacter(Purpose.Manage);
                break;
            case MenuAction.Practice:
                ChooseCharacter(Purpose.Practice);
                break;
            case MenuAction.Host:
                OpenSubmenu(HostItems(), "Open the doors", OpenFriends);
                break;
            case MenuAction.Friends:
                OpenFriends();
                break;
            case MenuAction.More:
                OpenMore();
                break;
            case MenuAction.Credits:
                _showCredits = !_showCredits;
                break;
            case MenuAction.HostNew:
                ChooseCharacter(Purpose.Host);
                break;
            case MenuAction.HostSave:
                OpenSubmenu(HostSaveItems(), "Which village will you open to friends?", () => OpenSubmenu(HostItems(), "Open the doors", OpenFriends));
                break;
            case MenuAction.Join:
                _coopMenu ??= new CoopMenu(Host, _defs);
                if ((_coopMenu.Playing ??= _roster.Last) != null)
                    _joining = true;
                else
                    ChooseCharacter(Purpose.Join);
                break;
            case MenuAction.LoadGame:
                OpenSubmenu(SaveItems(), "Which village?", OpenMore);
                break;
            case MenuAction.Replays:
                OpenSubmenu(ReplayItems(), "Which season?", OpenMore);
                break;
            case MenuAction.Back:
                SubmenuBack();
                break;
            case MenuAction.Report:
                (_reporting, _logsError) = (true, null);
                break;
            case MenuAction.BackToCreator:
                ChooseCharacter(Purpose.Solo);
                break;
            default:
                if (item.Then is { } then)
                {
                    then();
                    break;
                }
                if (item.Start == null)
                    break;
                _error = null;
                Begin(item.Start);
                break;
        }
    }

    /// <summary>
    /// The stacked logo (art/brand, rendered by tools/gen_icon.py), sized to leave the menu room on
    /// a short screen; the title in text if the sprite is missing.
    /// </summary>
    private static void DrawLogo(float2 screen)
    {
        if (!Gui.TrySprite("ui/logo", out var logo) || logo.PixelSize.Y == 0)
        {
            using (Gui.DisplayFont(64f))
                ImGui.TextColored(Theme.Text, Title); // the brand's lettering is bone; ember is the "!"'s
            return;
        }
        var h = MathF.Min(Gui.S(280), screen.Y * 0.25f);
        var size = new float2(h * logo.PixelSize.X / logo.PixelSize.Y, h);
        var min = ImGui.GetCursorScreenPos();
        Gui.Image(ImGui.GetWindowDrawList(), logo, min, min + size, new ImColor8(255, 255, 255, 255));
        ImGui.Dummy(in size);
    }


    /// <summary>
    /// A menu entry: its label in the display face, its hint always underneath (wrapped, the entry
    /// as tall as it needs, so nothing shifts as the pick moves: playtesters saw the old grow-on-hover
    /// jump about), and while it's picked (hovered, or reached with keys or a pad) the hint brightened
    /// and a barrel-gold bar at its edge. Returns how much taller than one hint line it stands.
    /// </summary>
    private float MenuButton(MenuItem item)
    {
        // A choice that leads on (a town, a trade) has a Then and no Start of its own; it's live too.
        var enabled = Enabled(item);
        var width = Gui.S(MenuWidth);
        var inset = Gui.S(17);
        var (hint, lineHeight) = Hint(item);
        // Every entry is laid out tall enough for its whole hint, always shown: nothing moves as the pick does.
        var extra = HintGrowth((hint, lineHeight));
        var size = new float2(width, Gui.S(46) + extra);
        var min = ImGui.GetCursorScreenPos();
        if (!enabled)
            ImGui.BeginDisabled(true);
        // EnableNav: ImGui leaves invisible buttons out of pad and keyboard navigation unless asked.
        var pressed = ImGui.InvisibleButton($"##{item.Label}", in size, ImGuiButtonFlags.EnableNav);
        if (!enabled)
            ImGui.EndDisabled();
        var picked = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) || ImGui.IsItemFocused();
        var held = enabled && ImGui.IsItemActive();

        var dl = ImGui.GetWindowDrawList();
        var max = min + size;
        if (picked && enabled)
        {
            dl.AddRectFilled(in min, in max, Theme.U8(held ? Theme.OakLight : Theme.Oak, held ? 0.9f : 0.55f), Gui.S(3), ImDrawFlags.None);
            var barMax = new float2(min.X + Gui.S(4), max.Y);
            dl.AddRectFilled(in min, in barMax, Theme.U8(Theme.Barrel), Gui.S(2), ImDrawFlags.None);
        }
        else if (item.Nudge)
        {
            // The place to start, marked even while something else is picked.
            var barMax = new float2(min.X + Gui.S(4), max.Y);
            var pulse = 0.6f + 0.4f * MathF.Sin((float)ImGui.GetTime() * 3f);
            dl.AddRectFilled(in min, in barMax, Theme.U8(Theme.Barrel, pulse), Gui.S(2), ImDrawFlags.None);
        }
        var label = min + new float2(Gui.S(16), Gui.S(3));
        using (Gui.DisplayFont(25f))
        {
            dl.AddText(in label, Theme.U8(enabled ? Theme.Text : Theme.Dim, enabled ? 1f : 0.6f), item.Label);
            if (item.Nudge && !picked)
            {
                var after = label + new float2(ImGui.CalcTextSize(item.Label).X + Gui.S(12), Gui.S(9));
                using (Gui.BodyFont(15f))
                    dl.AddText(in after, Theme.U8(Theme.Barrel), "start here"u8);
            }
        }
        {
            // The hint, always there: bright on the picked entry, quieter on the rest.
            var line = min + new float2(inset, Gui.S(28));
            using (Gui.BodyFont(15f))
                foreach (var text in hint)
                {
                    dl.AddText(in line, picked ? Theme.U8(Theme.Parchment, enabled ? 0.95f : 0.6f) : Theme.U8(Theme.Dim, enabled ? 0.7f : 0.45f), text);
                    line += new float2(0, lineHeight);
                }
        }
        if (pressed)
            Press(item);
        return extra;
    }

    private const float MenuWidth = 420f;

    /// <summary>An entry does something: it starts a game, leads on (a town, a trade, with a Then), or is an action.</summary>
    private static bool Enabled(MenuItem item) => item.Start != null || item.Then != null || item.Action != MenuAction.Start;

    /// <summary>An entry's hint in lines as it's drawn (the body face, wrapped to the entry), and the height of a line.</summary>
    private static (List<string> Lines, float LineHeight) Hint(MenuItem item)
    {
        using (Gui.BodyFont(15f))
            return (WrapText(item.Hint, Gui.S(MenuWidth) - Gui.S(17) - Gui.S(10)), ImGui.GetTextLineHeight());
    }

    /// <summary>How much taller than the rest an entry stands while its hint shows: a line for each beyond the first.</summary>
    private static float HintGrowth((List<string> Lines, float LineHeight) hint) => (hint.Lines.Count - 1) * hint.LineHeight;

    /// <summary>Text broken into lines at spaces, each at most <paramref name="width"/> wide in the current font.</summary>
    private static List<string> WrapText(string text, float width)
    {
        var lines = new List<string>();
        var line = "";
        foreach (var word in text.Split(' '))
        {
            var tried = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && ImGui.CalcTextSize(tried, false, -1f).X > width)
            {
                lines.Add(line);
                line = word;
            }
            else
                line = tried;
        }
        lines.Add(line);
        return lines;
    }

    public override void Dispose()
    {
        _quads?.Dispose();
        _coopMenu?.Dispose();
    }
}
