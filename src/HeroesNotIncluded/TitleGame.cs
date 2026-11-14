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
    private readonly Camera3D _camera = new() { Zoom = 30f, VerticalScale = WorldScale.LevelHeight };
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
    private MenuItem[] _items = [];
    private DefDatabase _defs = null!;

    /// <summary>Why the last load failed, shown under the menu.</summary>
    private string? _error;

    private enum MenuAction { Start, Settings, Exit, NewGame, LoadGame, Back, Host, HostNew, HostSave, Join, Characters, BackToCreator, Report, Credits }

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
    private enum Purpose { Solo, Host, Join, Manage }

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

    /// <summary>An entry: a label saying what it does, and the line underneath (the word bank's, or what it'll do).</summary>
    private sealed record MenuItem(string Label, string Hint, Func<Game>? Start, MenuAction Action = MenuAction.Start);

    /// <summary>How many saves the Load Game list shows (the in-game Load window lists them all).</summary>
    private const int MaxListedSaves = 8;

    private MenuItem[] MainItems()
    {
        var newest = SaveSlots.Newest();
        return
        [
            newest is { } last
                ? new("Continue", $"{last.Name}: {last.Summary}", () => FromSave(last))
                : new("Continue", "Nothing saved yet.", null),
            new("New round", "Just you and the regulars.", null, MenuAction.NewGame),
            new("Host game", "Open the doors.", null, MenuAction.Host),
            new("Join game", "Pull up a stool.", null, MenuAction.Join),
            new("Characters", "Who you've been.", null, MenuAction.Characters),
            new("Load game", newest == null ? "Nothing saved yet." : "Pick up where you left off.", null, newest == null ? MenuAction.Start : MenuAction.LoadGame),
            new("Settings", "House rules.", null, MenuAction.Settings),
            new("Report a bug", "Something broke? Tell us how.", null, MenuAction.Report),
            new("Credits", "Everyone who swept up.", null, MenuAction.Credits),
            new("Quit", "Close up for the night.", null, MenuAction.Exit),
        ];
    }

    private MenuItem[] SaveItems()
    {
        var now = DateTime.UtcNow;
        return SaveSlots.List()
            .Where(s => s.Header != null)
            .Take(MaxListedSaves)
            .Select(s => new MenuItem(s.Name, $"{s.Summary} · saved {SaveSlots.Ago(s.Modified, now)}", () => FromSave(s)))
            .Append(new MenuItem("Back", "Back to the main menu.", null, MenuAction.Back))
            .ToArray();
    }

    private MenuItem[] HostItems()
    {
        var anySave = SaveSlots.Newest() != null;
        return
        [
            new("New round", "Open a lobby: friends say which trades they want, you deal.", null, MenuAction.HostNew),
            new("A saved village", anySave ? "Open one of your saved villages to friends." : "Nothing saved yet.", null, anySave ? MenuAction.HostSave : MenuAction.Start),
            new("Back", "Back to the main menu.", null, MenuAction.Back),
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
            .Append(new MenuItem("Back", "Back.", null, MenuAction.Back))
            .ToArray();

    /// <summary>
    /// A solo round's last step: the trade to be dealt. The bots fill the others, so any choice
    /// leaves a full cast; "Any" lets the deal decide as before.
    /// </summary>
    private MenuItem[] TradeItems(SavedCharacter c)
    {
        Game Start(string? trade) => new VillageGame(DefaultSeed) { Adventure = true, Character = c, HeroTrade = trade };
        return _defs.AllTrades
            .Select(t => new MenuItem(char.ToUpperInvariant(t.Label[0]) + t.Label[1..], t.Description, () => Start(t.Name)))
            .Append(new MenuItem("Any", "Let the deal pick a trade for you.", () => Start(null)))
            .Append(new MenuItem("Back", "Back to who you'll be.", null, MenuAction.BackToCreator))
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
        (_purpose, _editing, _submenu, _submenuTitle, _error) = (purpose, null, null, null, null);
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

    /// <summary><paramref name="c"/> is who you'll be: on to what they were chosen for.</summary>
    private void Chosen(SavedCharacter c)
    {
        (_picking, _creating) = (false, false);
        c = _roster.Put(c) ?? c; // most recently used
        Prefill(c);
        switch (_purpose)
        {
            case Purpose.Solo:
                (_submenu, _submenuTitle) = (TradeItems(c), "Which trade will you take?");
                break;
            case Purpose.Host:
                HostAs(c);
                break;
            case Purpose.Join:
                (_coopMenu ??= new CoopMenu(Host, _defs)).Playing = c;
                _joining = true;
                break;
        }
    }

    /// <summary>Host a new village as <paramref name="c"/>.</summary>
    private void HostAs(SavedCharacter c) => Begin(() => new VillageGame((ulong)Random.Shared.Next(1, 1_000_000))
    {
        Adventure = true, Character = c, HostCoop = HostSettingsFor(c),
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
        var center = VillageRound.Setup(_sim).Center;
        _sim.RebuildDerivedIfNeeded();

        _camera.Target = new float3(center.X + 0.5f, center.Y + 0.5f, center.Z);
        _camera.Yaw = 0.6f;
        _renderer = new WorldRenderer(Host.Atlas) { ShowNames = false, ShowBubbles = false };
        // Roofs on: the title's view of the village is from outside, with nobody inside to open up.
        _quads = new QuadRenderer(Host, new int3(_sim.Map.Width, _sim.Map.Height, _sim.Map.Depth), ShaderReload.QuadShaders(Host)) { HideRoofs = false };
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

    /// <summary>
    /// The title is all menus: a pad drives them (ImGui's navigation) unless the controls page is
    /// waiting for a button. The newest window takes the focus, and B closes it or leaves a submenu.
    /// </summary>
    private void UpdatePad()
    {
        Controls.Map.Update();
        Host.ImGui.GamepadNav = !_settings.Capturing;
        var top = _settings.IsOpen ? "Settings" : _showCredits ? "###credits" : "##title";
        if (top != _padFocused)
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
            (_submenu, _submenuTitle) = (null, null);
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
            foreach (var item in _submenu ?? _items)
                MenuButton(item);
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
            case MenuAction.Host:
                (_submenu, _submenuTitle) = (HostItems(), "Open the doors");
                break;
            case MenuAction.Credits:
                _showCredits = !_showCredits;
                break;
            case MenuAction.HostNew:
                ChooseCharacter(Purpose.Host);
                break;
            case MenuAction.HostSave:
                (_submenu, _submenuTitle) = (HostSaveItems(), "Which village will you open to friends?");
                break;
            case MenuAction.Join:
                _coopMenu ??= new CoopMenu(Host, _defs);
                if ((_coopMenu.Playing ??= _roster.Last) != null)
                    _joining = true;
                else
                    ChooseCharacter(Purpose.Join);
                break;
            case MenuAction.LoadGame:
                (_submenu, _submenuTitle) = (SaveItems(), "Which village?");
                break;
            case MenuAction.Back:
                (_submenu, _submenuTitle) = (null, null);
                break;
            case MenuAction.Report:
                (_reporting, _logsError) = (true, null);
                break;
            case MenuAction.BackToCreator:
                ChooseCharacter(Purpose.Solo);
                break;
            default:
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
        var h = MathF.Min(Gui.S(280), screen.Y * 0.3f);
        var size = new float2(h * logo.PixelSize.X / logo.PixelSize.Y, h);
        var min = ImGui.GetCursorScreenPos();
        Gui.Image(ImGui.GetWindowDrawList(), logo, min, min + size, new ImColor8(255, 255, 255, 255));
        ImGui.Dummy(in size);
    }

    /// <summary>
    /// A menu entry: its label in the display face, and while it's picked (hovered, or reached with
    /// keys or a pad) its line underneath and a barrel-gold bar at its edge. Every entry is tall
    /// enough for its line, so picking one doesn't shift the rest.
    /// </summary>
    private void MenuButton(MenuItem item)
    {
        var enabled = item.Start != null || item.Action != MenuAction.Start;
        var size = new float2(Gui.S(360), Gui.S(46));
        var min = ImGui.GetCursorScreenPos();
        if (!enabled)
            ImGui.BeginDisabled(true);
        var pressed = ImGui.InvisibleButton($"##{item.Label}", in size, ImGuiButtonFlags.None);
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
        var label = min + new float2(Gui.S(16), Gui.S(3));
        using (Gui.DisplayFont(25f))
            dl.AddText(in label, Theme.U8(enabled ? Theme.Text : Theme.Dim, enabled ? 1f : 0.6f), item.Label);
        if (picked)
        {
            var line = min + new float2(Gui.S(17), Gui.S(28));
            using (Gui.BodyFont(15f))
                dl.AddText(in line, Theme.U8(Theme.Parchment, enabled ? 0.95f : 0.6f), item.Hint);
        }
        if (pressed)
            Press(item);
    }

    public override void Dispose()
    {
        _quads?.Dispose();
        _coopMenu?.Dispose();
    }
}
