using Brutal.ImGuiApi;
using Narsil.Ui;
using Narsil;
using Narsil.Rendering;
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
/// The title screen: the game's name and main menu over a live, slowly orbiting view of a
/// generated valley. Menu entries start a village
/// (new, or continued from a save) or one of the demos through <see cref="GameHost.SwitchTo"/>, or
/// open the settings; Credits is a placeholder for now. Exit closes the game.
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

    /// <summary>Debug/screenshots (<c>--menu-pick "Dragon Demo"</c>): press this entry after a few seconds.</summary>
    public string? AutoPick { get; set; }

    /// <summary>Debug/screenshots (<c>--creator dragon|character</c>): open that Adventure creator step (an Escape start) at once.</summary>
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

    private enum MenuAction { Start, Settings, Exit, NewGame, LoadGame, Back, Coop, HostNew, HostSave, Join, BackToCreator, Report }

    /// <summary>Adventure's character creator, shown instead of the menu while open.</summary>
    private CharacterCreator _creator = null!;
    private bool _creating;

    /// <summary>The Report a Bug panel (where the logs are, where to send them), shown instead of the menu while open.</summary>
    private bool _reporting;
    private string? _logsError;

    /// <summary>The character creator is for a village to host (no prologue; the difficulty is asked last, in <see cref="HostAs"/>).</summary>
    private bool _hostingNew;

    /// <summary>Saved characters (<see cref="CharacterRoster"/>): picked from before hosting; solo starts begin from the last one.</summary>
    private CharacterRoster _roster = null!;
    private CharacterPicker _picker = null!;
    private bool _pickingHost;

    /// <summary>The saved character the creators are editing for hosting; null: a new one.</summary>
    private Guid? _editing;

    /// <summary>Co-op's Join panel, shown instead of the menu while open.</summary>
    private CoopMenu? _coopMenu;
    private bool _joining;

    /// <summary><c>--join address</c>: open the Join panel and connect straight away.</summary>
    public string? AutoJoin { get; set; }

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
                : new("Continue", "No saves yet.", null),
            new("New Round", "Walk into the village on your own, with AI villagers in the other trades.", null, MenuAction.NewGame),
            new("Multiplayer", "Play together: host a village for friends, or join theirs (local network or address).", null, MenuAction.Coop),
            new("Load Game", newest == null ? "No saves yet." : "Pick a saved village.", null, newest == null ? MenuAction.Start : MenuAction.LoadGame),
            new("Options", "Display, interface and gameplay settings.", null, MenuAction.Settings),
            new("Report a Bug", "Where to tell us, and the files that help.", null, MenuAction.Report),
            new("Credits", "Coming soon.", null),
            new("Exit", "Close the game.", null, MenuAction.Exit),
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

    private MenuItem[] CoopItems()
    {
        var anySave = SaveSlots.Newest() != null;
        return
        [
            new("Host New Round", "Make your character and open a lobby. Friends join, say which trades they want, and you deal the round.", null, MenuAction.HostNew),
            new("Host a Saved Village", anySave ? "Open one of your saved villages to friends." : "No saves yet.", null, anySave ? MenuAction.HostSave : MenuAction.Start),
            new("Join a Game", "Find a village on your network, or type the host's address.", null, MenuAction.Join),
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
    private MenuItem[] TradeItems()
    {
        Game Start(string? trade) => new VillageGame(DefaultSeed)
        {
            Adventure = true, HeroName = _creator.Name, HeroLook = _creator.Look, HeroTraits = _creator.Traits, HeroClothes = _creator.Clothes,
            HeroTrade = trade,
        };
        return _defs.AllTrades
            .Select(t => new MenuItem(char.ToUpperInvariant(t.Label[0]) + t.Label[1..], t.Description, () => Start(t.Name)))
            .Append(new MenuItem("Any", "Let the deal pick a trade for you.", () => Start(null)))
            .Append(new MenuItem("Back", "Back to your character.", null, MenuAction.BackToCreator))
            .ToArray();
    }

    private HostSettings HostSettingsFor()
    {
        var me = CoopIdentity.Load(Host);
        return new HostSettings(me, $"{me.Name}'s village");
    }

    /// <summary>The creator starts from a saved character.</summary>
    private void Prefill(SavedCharacter c)
    {
        _creator.Load(c.Name, c.Look, c.Traits, c.Clothes);
    }

    /// <summary>Host a new village as <paramref name="c"/>.</summary>
    private void HostAs(SavedCharacter c) => Host.SwitchTo(new VillageGame((ulong)Random.Shared.Next(1, 1_000_000))
    {
        Adventure = true, HeroName = c.Name, HeroLook = c.Look, HeroTraits = c.Traits, HeroClothes = c.Clothes, HostCoop = HostSettingsFor(),
    });

    /// <summary>Load a save for <see cref="Press"/>; a <see cref="Sim.Saving.SaveException"/> keeps us on the title.</summary>
    private Game FromSave(SaveSlot slot) => VillageGame.FromSave(SaveSlots.Load(slot.Path, _defs));

    public override void Load()
    {
        Theme.Apply(Settings.Current.UiScale);
        _settings = new SettingsWindow(Host);
        var defs = _defs = DefDatabase.LoadDirectory(Path.Combine(Host.DataDirectory, "defs"));
        _creator = new CharacterCreator(Host, defs);
        _roster = CharacterRoster.Load(Host, defs);
        _picker = new CharacterPicker(Host, _roster, defs);
        if (_roster.Last is { } last)
            Prefill(last);
        _items = MainItems();
        // The village behind the menu, its people not yet arrived.
        var map = defs.Map(VillageRound.DefaultMap);
        _sim = MapBuilder.CreateSimulation(defs, map, (ulong)Random.Shared.Next(1, 100_000));
        _sim.StrictJobs = false; // a buggy job fails itself, not the game
        var center = MapBuilder.Build(_sim, map, new MapGenerator.Options { SpawnStartingKit = false }).Center;
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
            (_coopMenu ??= new CoopMenu(Host, _defs)).Connect(address);
        }
    }

    public override void Update(float dt)
    {
        _time += dt;
        _fade = Math.Min(1f, _fade + dt * 1.5f);
        var window = Host.Window.Size;
        _camera.ViewportSize = new float2(Math.Max(1, window.X), Math.Max(1, window.Y));
        _camera.Yaw += dt * 0.025f; // a slow orbit
        if (Host.Input.WasKeyPressed(GlfwKey.F7))
            _error = ShaderReload.Reload(Host, _quads) is { } shaderError ? $"Shader error: {shaderError}" : null;

        if (_joining)
            _coopMenu?.Update(dt);
        _accumulator += dt * SimClock.TicksPerSecond;
        var ticks = Math.Min((int)_accumulator, 30);
        _accumulator -= ticks;
        for (var i = 0; i < ticks; i++)
            _sim.Tick();
    }

    // Light uploads are transfers, recorded before the render pass opens.
    public override void PrepareFrame(in FrameContext frame) => _renderer.UpdateLight(_quads, frame, _sim);

    public override bool DrawsScene => true;

    public override void Draw(in FrameContext frame) { }

    public override void DrawScene(in FrameContext frame)
    {
        VisualStyle.Apply(Host, _quads);
        _quads.Begin(_camera, _sim.Map.Depth - 1, _sim.Lighting.SkyLight(_sim), true);
        _renderer.Draw(_quads, _camera, _sim, Entity.Null, _time, Host.FrameTimeMs / 1000f);
        _quads.Render(frame);
    }

    public override void DrawUi()
    {
        var io = ImGui.GetIO();
        var screen = io.DisplaySize;
        var bg = ImGui.GetBackgroundDrawList(ImGui.GetMainViewport());

        // A dark wash from the left so the menu reads over the scenery, plus a fade-in.
        var a = float2.Zero;
        var b = new float2(screen.X * 0.55f, screen.Y);
        uint dark = Theme.U8(new float4(0.06f, 0.04f, 0.03f, 0.88f));
        uint clear = Theme.U8(new float4(0.06f, 0.04f, 0.03f, 0f));
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

        using (Gui.HeaderFont(76f))
            ImGui.TextColored(Theme.Accent, Title);
        var dim = Theme.Dim;
        ImGui.TextColored(dim, "The people the heroes walk past"u8);
        var gap = new float2(1, 34);
        ImGui.Dummy(in gap);

        if (_joining && _coopMenu != null)
        {
            ImGui.TextColored(dim, "Join a village"u8);
            switch (_coopMenu.Draw())
            {
                case CoopMenu.Result.Joined when _coopMenu.Joined is { Sim: { } joinedSim } session:
                    _joining = false;
                    Host.SwitchTo(new VillageGame(joinedSim.Seed) { Coop = session });
                    break;
                case CoopMenu.Result.Back:
                    _joining = false;
                    break;
            }
        }
        else if (_reporting)
            DrawReport(dim);
        else if (_pickingHost)
        {
            ImGui.TextColored(dim, "Who will you be?"u8);
            switch (_picker.Draw(_time))
            {
                case CharacterPicker.Result.Use:
                    _pickingHost = false;
                    HostAs(_roster.Put(_picker.Picked!));
                    break;
                case CharacterPicker.Result.Edit:
                    Prefill(_picker.Picked!);
                    (_editing, _pickingHost, _creating) = (_picker.Picked!.Id, false, true);
                    break;
                case CharacterPicker.Result.New:
                    (_editing, _pickingHost, _creating) = (null, false, true);
                    break;
                case CharacterPicker.Result.Back:
                    _pickingHost = false;
                    break;
            }
        }
        else if (_creating)
        {
            ImGui.TextColored(dim, "Who are you?"u8);
            switch (_creator.Draw(_time))
            {
                case CharacterCreator.Result.Continue when _hostingNew:
                    _creating = false;
                    HostAs(_roster.Put(new SavedCharacter
                    {
                        Id = _editing ?? Guid.NewGuid(), Name = _creator.Name, Look = _creator.Look, Traits = _creator.Traits.ToList(), Clothes = _creator.Clothes.ToList(),
                    }));
                    break;
                case CharacterCreator.Result.Continue:
                    _creating = false;
                    (_submenu, _submenuTitle) = (TradeItems(), "Which trade will you take?");
                    break;
                case CharacterCreator.Result.Back:
                    _creating = false;
                    if (_hostingNew && _roster.Characters.Count > 0)
                        _pickingHost = true;
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

        if (_error != null)
        {
            ImGui.Dummy(in gap);
            ImGui.PushTextWrapPos(340);
            ImGui.TextColored(Theme.Bad, Narsil.Ui.ImGuiText.Escape(_error));
            ImGui.PopTextWrapPos();
        }
        ImGui.Dummy(in gap);
        ImGui.TextColored(dim, $"Alpha build {BuildInfo.Label}. Built with BRUTAL.");
        ImGui.End();
        ImGui.PopStyleVar(1);
        _settings.Draw();

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
                _hostingNew = false;
                (_submenu, _submenuTitle) = (null, null);
                _creating = true;
                break;
            case MenuAction.Coop:
                (_submenu, _submenuTitle) = (CoopItems(), "Play together");
                break;
            case MenuAction.HostNew:
                _hostingNew = true;
                _editing = null;
                if (_roster.Characters.Count > 0)
                    _pickingHost = true;
                else
                    _creating = true;
                break;
            case MenuAction.HostSave:
                (_submenu, _submenuTitle) = (HostSaveItems(), "Which village will you open to friends?");
                break;
            case MenuAction.Join:
                _joining = true;
                _coopMenu ??= new CoopMenu(Host, _defs);
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
                (_submenu, _submenuTitle) = (null, null);
                _creating = true;
                break;
            default:
                if (item.Start == null)
                    break;
                try
                {
                    _error = null;
                    Host.SwitchTo(item.Start());
                }
                catch (Sim.Saving.SaveException ex)
                {
                    Log.Engine.Warning($"Couldn't load '{item.Label}': {ex.Message}");
                    _error = ex.Message;
                }
                break;
        }
    }

    private void MenuButton(MenuItem item)
    {
        var enabled = item.Start != null || item.Action != MenuAction.Start;
        var size = (float2?)new float2(340, 48);
        var align = new float2(0.06f, 0.5f);
        ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, in align);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f); // the theme rims frames; these entries are bare text
        var clear = new float4(0, 0, 0, 0);
        var hover = Theme.AccentSoft with { W = 0.85f };
        var active = Theme.Accent with { W = 0.9f };
        ImGui.PushStyleColor(ImGuiCol.Button, in clear);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, in hover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, in active);
        bool pressed;
        using (Gui.HeaderFont(26f))
        {
            if (!enabled)
                ImGui.BeginDisabled(true);
            pressed = ImGui.Button(item.Label, in size);
            if (!enabled)
                ImGui.EndDisabled();
        }
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar(2);

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            // An amber bar marks the hovered entry; the hint explains it (or why it's not ready).
            var dl = ImGui.GetWindowDrawList();
            var min = ImGui.GetItemRectMin();
            var max = new float2(min.X + 4, ImGui.GetItemRectMax().Y);
            if (enabled)
                dl.AddRectFilled(in min, in max, Theme.U8(Theme.Accent), 2f, ImDrawFlags.None);
            Narsil.Ui.ImGuiText.Tooltip(item.Hint);
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
