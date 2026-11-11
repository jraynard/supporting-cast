using Brutal.ImGuiApi;
using Narsil;
using Narsil.Platform;
using DragonWorld.Net;
using DragonWorld.Rendering;
using DragonWorld.Sim.Ai;
using DragonWorld.Sim.Components;
using DragonWorld.Sim.Defs;
using DragonWorld.Sim.Core;
using DragonWorld.Sim.Ecs;
using DragonWorld.Sim.Maps;
using DragonWorld.Sim.Scenarios;
using DragonWorld.Sim.Systems;

namespace DragonWorld;

/// <summary>
/// Adventure mode: the player is one character. WASD walks it (relative to the view), or the left
/// mouse button does (click to walk there, hold to follow the cursor, click on something to walk
/// over and do what F would); F works on what's in reach while held, and the camera follows it. Every input goes to the sim as an
/// <see cref="ISimCommand"/>, never by changing sim state directly.
/// </summary>
public sealed partial class VillageGame
{
    /// <summary>Adventure mode at a new home: the keeper is the player's avatar, egg in hand (no prologue).</summary>
    public bool Adventure { get; init; }

    /// <summary>Name for the avatar (from the character creator).</summary>
    public string? HeroName { get; init; }

    /// <summary>The avatar's look (from the character creator); null keeps a derived one.</summary>
    public Appearance? HeroLook { get; init; }

    /// <summary>The avatar's traits by name (from the character creator); null keeps rolled ones.</summary>
    public IReadOnlyList<string>? HeroTraits { get; init; }

    /// <summary>The avatar's clothes (<c>"Def:Dye"</c>, from the character creator); null dresses them as a villager.</summary>
    public IReadOnlyList<string>? HeroClothes { get; init; }

    /// <summary>Debug/screenshots (<c>--preset Wanderer</c>): the avatar looks and dresses as a creator preset.</summary>
    public string? HeroPreset { get; init; }

    /// <summary>The avatar's look and clothes: the creator's, else <see cref="HeroPreset"/>'s.</summary>
    private (Appearance? Look, IReadOnlyList<string>? Clothes) HeroAppearance() =>
        HeroPreset != null && _sim.Defs.AllOutfits.Find(o => o.Name.Equals(HeroPreset, StringComparison.OrdinalIgnoreCase)) is { } preset
            ? Rendering.HumanLook.FromPreset(preset, HeroLook ?? Rendering.HumanLook.Default)
            : (HeroLook, HeroClothes);

    /// <summary>Start in the prologue's village (Adventure), rather than straight at a new home.</summary>
    public bool Prologue { get; init; }

    /// <summary>A party arriving from the village: set them down on this game's fresh map.</summary>
    public TravelParty? Arrival { get; init; }

    /// <summary>Adventure's new starts (Escape, Pact): with a dragon of your own (see <see cref="AdventureStart"/>).</summary>
    public StartKind Start { get; init; }

    /// <summary>The starting dragon's name and colours (from the dragon creator); null keeps the defaults.</summary>
    public string? DragonName { get; init; }

    public DragonLook? DragonLook { get; init; }

    /// <summary>The starting dragon's breed (a def name), or for the prologue the egg's; null is the default breed.</summary>
    public string? DragonBreed { get; init; }

    /// <summary>
    /// Start where the opening leads, skipping it: a Pact at the tower's site (co-op hosts always
    /// do: leaving a map isn't networked yet), an Escape free in the wild. <c>--stage site</c>.
    /// </summary>
    public bool SkipOpening { get; init; }

    /// <summary>The prologue: who's speaking (the dialogue window's page), and a pending departure to confirm.</summary>
    private int _dialoguePage = -1;
    private Entity _confirmDepart = Entity.Null;

    /// <summary>Set up the prologue village, or the new home a party arrives at. Returns where to look.</summary>
    private int3 StartAdventureStory(DefDatabase defs)
    {
        var (look, clothes) = HeroAppearance();
        var hero = new Hero(HeroName, look, HeroTraits, DragonName, DragonLook, clothes, DragonBreed);
        var avatar = Arrival is { } party ? Travel.Arrive(_sim, party)
            : Start == StartKind.Pact && (SkipOpening || HostCoop != null) ? AdventureStart.PactSite(_sim, hero, Difficulty)
            : Start == StartKind.Escape && SkipOpening ? AdventureStart.Refuge(_sim, hero, Difficulty)
            : Start is StartKind.Escape or StartKind.Pact ? AdventureStart.Setup(_sim, Start, hero, Difficulty)
            : Sim.Scenarios.Prologue.Setup(_sim, HeroName, look, Difficulty, HeroTraits, clothes,
                DragonBreed != null ? hero.Breed(defs) : null, DragonLook?.Element ?? DragonElement.Fire); // the egg picked in the creator
        // Raids only at the new home (the village is someone else's to defend), and for a pact
        // only once the tower stands: until then the difficulty waits in sim.Story.
        if (Arrival is { Start: not StartKind.Pact } && Difficulty is { } difficulty && !difficulty.Equals("Off", StringComparison.OrdinalIgnoreCase))
            _sim.Storyteller.SetDifficulty(_sim, defs.Difficulty(difficulty));
        return _sim.World.Get<Position>(avatar).Cell;
    }

    /// <summary>
    /// Leave this map: pack the party and switch to a game on a new one. With a wagon it's the
    /// prologue's road; without (Entity.Null) it's a flight on dragonback.
    /// </summary>
    private void SetOut(Entity wagon, int travelHours = Travel.RoadHours)
    {
        var party = Travel.Pack(_sim, _avatar, wagon, travelHours);
        var difficulty = _sim.World.TryGet<Quest>(_avatar, out var q) && q.Difficulty != null ? q.Difficulty : Difficulty;
        Log.Engine.Info(wagon.IsNull
            ? $"Flying off with {party.Dragon?.Name ?? "no dragon"} ({party.Start})"
            : $"Setting out from {Sim.Scenarios.Prologue.VillageName} with {party.Cargo.Count} stacks of cargo");
        Host.SwitchTo(new VillageGame(seed + 1) { Arrival = party, Difficulty = difficulty, AutoPause = AutoPause });
    }

    /// <summary>The "Fly away?" question is open.</summary>
    private bool _confirmFlight;

    /// <summary>The prologue's objective line, Orla's words, and the "set out?" question.</summary>
    private void DrawStory()
    {
        if (!HasAvatar)
            return;
        var screen = ImGui.GetIO().DisplaySize;
        if ((Sim.Scenarios.Prologue.Objective(_sim, _avatar) ?? RefugeObjective()) is { } objective)
        {
            var pos = new float2(screen.X * 0.5f, 132);
            float2? pivot = new float2(0.5f, 0f);
            ImGui.SetNextWindowPos(in pos, ImGuiCond.Always, in pivot);
            ImGui.SetNextWindowBgAlpha(0.8f);
            ImGui.Begin("##objective"u8, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings
                | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoNav);
            TextC(Ui.Theme.Accent, objective);
            ImGui.End();
        }

        DrawPactChecklist(screen);
        var center = screen * 0.5f;
        float2? middle = new float2(0.5f, 0.5f);
        DrawFlightPrompt(screen, center, middle);
        if (_dialoguePage >= 0 && Sim.Scenarios.Prologue.DialogueOf(_sim, _dialogueWith) is { } dialogue)
        {
            var lines = dialogue.Lines;
            ImGui.SetNextWindowPos(in center, ImGuiCond.Always, in middle);
            var size = new float2(520, 0);
            ImGui.SetNextWindowSize(in size, ImGuiCond.Always);
            var speaker = dialogue.Speaker.Length > 0 ? dialogue.Speaker : _sim.World.Get<Creature>(_dialogueWith).Name;
            ImGui.Begin($"{speaker}###dialogue", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoResize);
            Wrapped(lines[Math.Min(_dialoguePage, lines.Count - 1)]);
            ImGui.Spacing();
            var last = _dialoguePage >= lines.Count - 1;
            if (ImGui.Button(last ? dialogue.Accept : "Continue", in _autoSize) || ImGui.IsKeyPressed(ImGuiKey.Enter))
            {
                if (last)
                {
                    // The story beat itself is the talk action, sent as a command like any other.
                    Issue(new AvatarWorkCommand(_avatar, true, _dialogueAt));
                    Issue(new AvatarWorkCommand(_avatar, false));
                    _dialoguePage = -1;
                }
                else
                    _dialoguePage++;
            }
            ImGui.End();
        }
        else
            _dialoguePage = -1;

        if (!_confirmDepart.IsNull)
        {
            ImGui.SetNextWindowPos(in center, ImGuiCond.Always, in middle);
            ImGui.Begin("Set out?###depart"u8, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings);
            Wrapped($"Hitch the wagon and leave {Sim.Scenarios.Prologue.VillageName} for good? The road is long: you'll arrive somewhere new in a few days.");
            if (_coop != null)
                TextC(Ui.Theme.Warn, "Setting out isn't possible in co-op yet.");
            else if (ImGui.Button("Set out"u8, in _autoSize))
            {
                var wagon = _confirmDepart;
                _confirmDepart = Entity.Null;
                ImGui.End();
                SetOut(wagon);
                return;
            }
            ImGui.SameLine(0, 8);
            if (ImGui.Button("Not yet"u8, in _autoSize))
                _confirmDepart = Entity.Null;
            ImGui.End();
        }
    }

    private string? _refugeObjective;
    private float _refugeCheckedAt = -10f;

    /// <summary>
    /// Escape's refuge, once you've got away: the next thing to see to (a fire, a roof over a bed,
    /// food put by, a tool), guidance only and gone once all are done. Rechecked every couple of
    /// seconds rather than every frame: it scans every thing on the map.
    /// </summary>
    private string? RefugeObjective()
    {
        if (_sim.Story.Start != StartKind.Escape || _sim.Story.Escape != null || _sim.World.Has<Quest>(_avatar))
            return null;
        if (_time - _refugeCheckedAt < 2f)
            return _refugeObjective;
        _refugeCheckedAt = _time;
        var w = _sim.World;
        bool fire = false, bed = false, tool = false;
        var food = 0;
        foreach (var e in w.Each<Thing>())
        {
            var t = w.Get<Thing>(e);
            var def = t.Def;
            if (def.Tool != null)
                tool = true;
            if (def.IsFood)
                food += w.TryGet<Stack>(e, out var stack) ? stack.Count : 1;
            if (!t.Spawned || w.Has<Construction>(e))
                continue;
            if (def.Building?.Fuel != null && def.Name == "Campfire" && w.TryGet<Fueled>(e, out var fueled) && fueled.Fuel > 0)
                fire = true;
            if (def.Building is { IsBed: true, IsNest: false } && _sim.Map.Roofed[_sim.Map.Idx(t.Cell)])
                bed = true;
        }
        _refugeObjective = !fire ? "Make camp: build a campfire and light it before nightfall (Build tab)."
            : !bed ? "Shelter: put a bed under a roof (walls round it, or a floor overhead)."
            : food < 30 ? $"Put food by: {food}/30 (hunt, fish, gather berries; your dragon hunts for itself)."
            : !tool ? "Make a tool: a hammer, saw or pick at a workbench (Recipes)."
            : null;
        return _refugeObjective;
    }

    /// <summary>
    /// The Pact's tower: what it still needs, ticked off as the hourly check finds it built
    /// (<see cref="TowerPact"/>). Under the clock, collapsible; gone once the pact is settled.
    /// </summary>
    private void DrawPactChecklist(float2 screen)
    {
        if (TowerPact.Of(_sim) is not { Done: false } pact)
            return;
        var pos = new float2(screen.X - 16, 214);
        float2? pivot = new float2(1f, 0f);
        ImGui.SetNextWindowPos(in pos, ImGuiCond.Always, in pivot);
        ImGui.SetNextWindowBgAlpha(0.8f);
        if (ImGui.Begin($"{Capitalized(pact.Tower.Label)}###pact", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav))
        {
            var requirements = TowerPact.Requirements(pact);
            for (var i = 0; i < requirements.Count; i++)
            {
                var met = i < pact.Met.Length && pact.Met[i];
                TextC(met ? Ui.Theme.Good : Ui.Theme.Dim, met ? "[x]" : "[ ]");
                ImGui.SameLine(0, 6);
                ImGui.Text(requirements[i]);
            }
            TextC(Ui.Theme.Dim, "Within the ley stone's reach. Checked every hour.");
        }
        ImGui.End();
    }

    private static string Capitalized(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>
    /// Mounted on your own dragon once the story lets you go: a "Fly away" button under the
    /// objective, and the question it opens. Leaving isn't possible in co-op yet.
    /// </summary>
    private void DrawFlightPrompt(float2 screen, float2 center, float2? middle)
    {
        if (!Departure.StoryAllows(_sim, _avatar) || Departure.CantFlyOff(_sim, _avatar) != null)
        {
            _confirmFlight = false;
            return;
        }
        var dragon = _sim.World.Get<Creature>(Companions.DragonOf(_sim, _avatar)).Name;
        if (!_confirmFlight)
        {
            var pos = new float2(screen.X * 0.5f, 172);
            float2? pivot = new float2(0.5f, 0f);
            ImGui.SetNextWindowPos(in pos, ImGuiCond.Always, in pivot);
            ImGui.SetNextWindowBgAlpha(0.8f);
            ImGui.Begin("##flyaway"u8, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings
                | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav);
            if (ImGui.Button($"Fly away on {dragon}", in _autoSize))
                _confirmFlight = true;
            ImGui.End();
            return;
        }
        ImGui.SetNextWindowPos(in center, ImGuiCond.Always, in middle);
        ImGui.Begin("Fly away?###flyaway-confirm"u8, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings);
        Wrapped($"Leave this place for good on {dragon}'s back? You'll take only what you carry, and come down somewhere new by morning.");
        if (_coop != null)
            TextC(Ui.Theme.Warn, "Flying off isn't possible in co-op yet.");
        else if (ImGui.Button("Fly away"u8, in _autoSize))
        {
            _confirmFlight = false;
            ImGui.End();
            SetOut(Entity.Null, Travel.FlightHours);
            return;
        }
        ImGui.SameLine(0, 8);
        if (ImGui.Button("Not yet"u8, in _autoSize))
            _confirmFlight = false;
        ImGui.End();
    }

    private int3 _dialogueAt;
    private Entity _dialogueWith = Entity.Null;

    private Entity _avatar = Entity.Null;
    private int2 _sentMoveDir;

    /// <summary>Co-op only: the local walk drawn ahead of the round trip (see <see cref="AvatarPredictor"/>).</summary>
    private readonly AvatarPredictor _predictor = new();
    private bool _sentWork;

    /// <summary>
    /// Keys the avatar's controls use, the dragon's included (R ride, B breathe: <see cref="HandleDragonKeys"/>);
    /// the toolbar's letter hotkeys skip them in Adventure mode.
    /// </summary>
    private static readonly GlfwKey[] AvatarKeys = [GlfwKey.W, GlfwKey.A, GlfwKey.S, GlfwKey.D, GlfwKey.F, GlfwKey.Z, GlfwKey.X, GlfwKey.N, GlfwKey.R, GlfwKey.B];

    private bool _sentDig;

    /// <summary>
    /// Adventure mode before anyone has joined: no work orders or work priorities, since there's
    /// nobody to carry them out. Newcomers arrive once a bed is ready indoors (SettlerSystem).
    /// </summary>
    private bool OrdersLocked
    {
        get
        {
            if (!HasAvatar)
                return false;
            var w = _sim.World;
            foreach (var e in _sim.Creatures())
            {
                var c = w.Get<Creature>(e);
                if (e != _avatar && c.Faction == Faction.Village && c.Def.Race == Race.Human)
                    return false;
            }
            return true;
        }
    }

    private bool HasAvatar => !_avatar.IsNull && _sim.World.IsAlive(_avatar) && _sim.World.Has<Avatar>(_avatar);

    /// <summary>After a new start or a load: pick up the avatar, if this is an Adventure game.</summary>
    private void FindAvatar()
    {
        _avatar = Entity.Null;
        if (LocalPlayer is { } me)
        {
            // Co-op: our own character, if we're steering it (others' avatars are theirs).
            var mine = Players.CharacterOf(_sim, me);
            if (!mine.IsNull && _sim.World.Has<Avatar>(mine))
                _avatar = _selected = mine;
        }
        else
        {
            foreach (var e in _sim.World.Query<Avatar>())
            {
                _avatar = e;
                _selected = e;
                break;
            }
        }
        _sentMoveDir = default;
        _predictor.Reset();
        _sentWork = false;
        _sentDig = false;
        _clickHold = ClickHold.None;
        _clickWindow = null;
    }

    /// <summary>Read WASD/F and send what changed. Outside combat only: in a fight the HUD gives orders.</summary>
    private void HandleAvatarInput(Input input)
    {
        if (!HasAvatar)
            return;
        var dir = int2.Zero;
        if (!InCombat)
        {
            float x = 0, y = 0;
            if (input.IsKeyDown(GlfwKey.W)) y += 1;
            if (input.IsKeyDown(GlfwKey.S)) y -= 1;
            if (input.IsKeyDown(GlfwKey.D)) x += 1;
            if (input.IsKeyDown(GlfwKey.A)) x -= 1;
            dir = MapDirection(x, y);
        }
        if (dir != _sentMoveDir)
        {
            // Co-op: walk on screen at once, and tell the sim where we turned or stopped so it
            // ends up there too when the command comes back.
            var at = _coop != null ? _predictor.DirectionChanged(_sim, _avatar, dir) : null;
            Issue(new AvatarMoveCommand(_avatar, dir, at));
            _sentMoveDir = dir;
        }
        if (dir != int2.Zero)
            _clickWindow = null; // walked off: the clicked wagon or bench stays shut

        if (!InCombat)
        {
            HandleInventoryKeys(input);
            HandleDragonKeys(input);
        }

        // N lies down where you stand (in the bed there, else on the ground): its own key, so F and clicks never put you to sleep.
        if (!InCombat && input.WasKeyPressed(GlfwKey.N))
            Issue(new SleepCommand(_avatar));

        // X puts down what you hold: its own key, so F never drops anything by accident.
        if (!InCombat && input.WasKeyPressed(GlfwKey.X) && WorkTarget(AvatarTool.Drop) is { Kind: AvatarActionKind.PutDown } drop)
            Issue(new AvatarDropCommand(_avatar, drop.Cell));

        // F uses, Z digs; both work only while held. A press starts the action, a release of both stops work.
        var use = !InCombat && input.IsKeyDown(GlfwKey.F);
        var dig = !InCombat && input.IsKeyDown(GlfwKey.Z);
        if (use && !_sentWork && WorkTarget() is { OpensWindow: true } window)
            OpenWindowFor(window, toggle: true); // F again closes it
        else if (use && !_sentWork)
            Issue(new AvatarWorkCommand(_avatar, true, WorkTarget().Cell));
        else if (dig && !_sentDig)
            Issue(new AvatarWorkCommand(_avatar, true, WorkTarget(AvatarTool.Dig).Cell, AvatarTool.Dig));
        else if (!use && !dig && (_sentWork || _sentDig) && _clickHold != ClickHold.Act)
            Issue(new AvatarWorkCommand(_avatar, false)); // (a mouse button still held keeps working)
        _sentWork = use;
        _sentDig = dig;
    }

    /// <summary>The game's side of an action that opens something: a window, the dialogue, the "set out?" question.</summary>
    private void OpenWindowFor(AvatarAction action, bool toggle)
    {
        switch (action.Kind)
        {
            case AvatarActionKind.OpenStorage:
                _openStorage = toggle && _openStorage == action.Target ? Entity.Null : action.Target;
                break;
            case AvatarActionKind.OpenStation:
                _openStation = toggle && _openStation == action.Target ? Entity.Null : action.Target;
                break;
            case AvatarActionKind.Talk:
                (_dialoguePage, _dialogueAt, _dialogueWith) = (0, action.Cell, action.Target); // the story beat happens when the dialogue ends
                break;
            case AvatarActionKind.Depart:
                _confirmDepart = action.Target;
                break;
            case AvatarActionKind.TendDragon:
                _openCare = toggle && _openCare == action.Target ? Entity.Null : action.Target;
                break;
        }
    }

    /// <summary>What the left button's last press started, until it's let go.</summary>
    private enum ClickHold
    {
        None,

        /// <summary>A walk: while held, the destination follows the cursor.</summary>
        Walk,

        /// <summary>Walking over to work on something (or working on it): the button stands in for F.</summary>
        Act,
    }

    private ClickHold _clickHold;
    private int3 _clickSent;

    /// <summary>A click on something that opens a window (the wagon, a bench, the keeper): opened once the walk there ends in reach.</summary>
    private int3? _clickWindow;

    /// <summary>Real time since the window click, so a co-op command still in flight isn't taken for a walk that ended.</summary>
    private float _clickWindowAge;

    /// <summary>
    /// Diablo-style mouse control, with the Select tool (other tools keep their clicks): click the
    /// ground to walk there and hold to follow the cursor; click something F could act on to walk
    /// over and do it (holding the button works it, like F). Clicking a creature, or anything with
    /// Shift held, selects it for the inspector instead. Returns whether it took the click.
    /// </summary>
    private bool HandleAdventureMouse(Input input, float dt)
    {
        if (!HasAvatar || InCombat || _tool != ToolKind.Select)
            return false;
        UpdateClickWindow(dt);

        var shift = input.IsKeyDown(GlfwKey.LeftShift) || input.IsKeyDown(GlfwKey.RightShift);
        if (!Ridden.IsNull)
        {
            // Mounted: a click on the ground (either button) sends the dragon there, flying with the
            // wing toggle or Ctrl. Clicking a creature, or with Shift, still selects.
            var left = input.WasMousePressed(MouseButton.Left);
            if (!(left || input.WasMousePressed(MouseButton.Right)) || shift || !_sim.Map.InBounds(_hoverCell))
                return false;
            var clicked = PickCreature(input.MousePosition);
            // Right-click on game: the dragon gives chase (F shoots it from the saddle).
            if (!left && !clicked.IsNull && _sim.World.Get<Creature>(clicked) is { Dead: false } prey && prey.Def.Race == Race.Animal)
            {
                Issue(new MountedHuntCommand(_avatar, clicked));
                return true;
            }
            if (left && !clicked.IsNull)
                return false;
            Issue(new MoveOrderCommand(_avatar, _hoverCell,
                Fly: _rideFly || input.IsKeyDown(GlfwKey.LeftControl) || input.IsKeyDown(GlfwKey.RightControl)));
            return true;
        }
        if (input.WasMouseReleased(MouseButton.Left))
        {
            if (_clickHold == ClickHold.Act && !_sentWork && !_sentDig)
                Issue(new AvatarWorkCommand(_avatar, false));
            _clickHold = ClickHold.None;
        }
        else if (_clickHold == ClickHold.Walk && input.IsMouseDown(MouseButton.Left)
                 && _sim.Map.InBounds(_hoverCell) && _hoverCell != _clickSent)
        {
            Issue(new AvatarGoToCommand(_avatar, _hoverCell));
            _clickSent = _hoverCell;
        }

        if (input.WasMousePressed(MouseButton.Right) && ItemInReach() is { IsNull: false } menuItem)
        {
            _worldMenuItem = menuItem; // DrawWorldItemMenu opens it this frame
            _openWorldMenu = true;
            return true;
        }
        if (!input.WasMousePressed(MouseButton.Left) || shift)
            return false;
        var creature = PickCreature(input.MousePosition);
        var (action, cell) = creature.IsNull ? ClickTarget()
            : (ProbeAt(_sim.World.Get<Position>(creature).Cell), _sim.World.Get<Position>(creature).Cell);
        if (!creature.IsNull && action.Kind != AvatarActionKind.Talk)
            return false; // people and animals are selected, as before
        _clickWindow = null;
        if (action.Kind != AvatarActionKind.None)
        {
            var here = AvatarSystem.ActionAt(_sim, _avatar, cell);
            if (here.Kind == action.Kind && here.OpensWindow)
                OpenWindowFor(here, toggle: true);
            else if (here.Kind == action.Kind)
                Issue(new AvatarWorkCommand(_avatar, true, cell)); // already in reach: just like F
            else
            {
                Issue(new AvatarGoToCommand(_avatar, cell, Interact: true));
                if (action.OpensWindow)
                    (_clickWindow, _clickWindowAge) = (cell, 0);
            }
            _clickHold = action.OpensWindow ? ClickHold.None : ClickHold.Act;
            return true;
        }
        if (!_sim.Map.InBounds(_hoverCell))
            return true;
        Issue(new AvatarGoToCommand(_avatar, _hoverCell));
        (_clickHold, _clickSent) = (ClickHold.Walk, _hoverCell);
        return true;
    }

    /// <summary>
    /// What a click would walk over and do: the first of the hovered cell, block and what walls
    /// hide that F could act on once you stood there.
    /// </summary>
    private (AvatarAction Action, int3 Cell) ClickTarget()
    {
        foreach (var c in (ReadOnlySpan<int3>)[_hoverCell, _hoverBlock, _hoverPastWalls])
            if (ProbeAt(c) is { Kind: not AvatarActionKind.None } action)
                return (action, c);
        return (AvatarAction.None, _hoverCell);
    }

    /// <summary>What F would do at a cell from right by it. Setting out counts only on the wagon itself, not anywhere near it.</summary>
    private AvatarAction ProbeAt(int3 cell)
    {
        if (!_sim.Map.InBounds(cell))
            return AvatarAction.None;
        var action = AvatarSystem.ActionAt(_sim, _avatar, cell, from: cell);
        return action.Kind == AvatarActionKind.Depart && action.Cell != cell ? AvatarAction.None : action;
    }

    /// <summary>Right-clicked food lying in reach (or behind a wall in reach): its menu, to eat it there.</summary>
    private Entity _worldMenuItem = Entity.Null;
    private bool _openWorldMenu;

    private Entity ItemInReach()
    {
        foreach (var c in (ReadOnlySpan<int3>)[_hoverCell, _hoverPastWalls])
        {
            if (!_sim.Map.InBounds(c))
                continue;
            var item = _sim.Map.Item[_sim.Map.Idx(c)];
            if (!item.IsNull && AvatarSystem.CanEat(_sim, _avatar, item))
                return item;
        }
        return Entity.Null;
    }

    /// <summary>The right-click menu for an item in the world, at the cursor.</summary>
    private void DrawWorldItemMenu()
    {
        if (_worldMenuItem.IsNull)
            return;
        if (_openWorldMenu)
        {
            ImGui.OpenPopup("##worldItem"u8);
            _openWorldMenu = false;
        }
        if (ImGui.BeginPopup("##worldItem"u8))
        {
            ItemMenu(_worldMenuItem);
            ImGui.EndPopup();
        }
        else
            _worldMenuItem = Entity.Null;
    }

    /// <summary>A walk to something with a window ended: open it if it's now in reach.</summary>
    private void UpdateClickWindow(float dt)
    {
        if (_clickWindow is not { } cell)
            return;
        _clickWindowAge += dt;
        if (_sim.World.Get<Mind>(_avatar).Job is AvatarWalkJob)
            return;
        if (AvatarSystem.ActionAt(_sim, _avatar, cell) is { OpensWindow: true } action)
            OpenWindowFor(action, toggle: false);
        else if (_clickWindowAge < 1f)
            return; // the walk may not have started yet (co-op)
        _clickWindow = null;
    }

    /// <summary>Screen-relative input (x right, y up) as one of the 8 map directions.</summary>
    private int2 MapDirection(float x, float y)
    {
        if (x == 0 && y == 0)
            return int2.Zero;
        var right = _camera.Right;
        var up = _camera.Heading;
        var vx = right.X * x + up.X * y;
        var vy = right.Y * x + up.Y * y;
        var len = MathF.Sqrt(vx * vx + vy * vy);
        // Snap to the nearest of 8 directions: a component counts when it's past sin(22.5°).
        const float Snap = 0.383f;
        return new int2(vx / len > Snap ? 1 : vx / len < -Snap ? -1 : 0, vy / len > Snap ? 1 : vy / len < -Snap ? -1 : 0);
    }

    /// <summary>
    /// What F would act on: the hovered cell (or the block under the cursor, or what a wall in the
    /// way hides) if it's in reach, else the cell in front of the avatar, else where it stands. Z and X look at their own cells.
    /// </summary>
    private AvatarAction WorkTarget(AvatarTool tool = AvatarTool.Use)
    {
        var at = _sim.World.Get<Position>(_avatar).Cell;
        var facing = _sim.World.Get<Avatar>(_avatar).Facing;
        var front = new int3(at.X + facing.X, at.Y + facing.Y, at.Z);
        // Digging: the hovered block, else the face in front, else the ground in front (a way down).
        ReadOnlySpan<int3> candidates = tool switch
        {
            AvatarTool.Dig => [_hoverBlock, front, new int3(front.X, front.Y, front.Z - 1)],
            AvatarTool.Drop => [_hoverCell, _hoverPastWalls, front, at],
            _ => [_hoverCell, _hoverBlock, _hoverPastWalls, front, at],
        };
        // An animal under the cursor comes first: its sprite stands over a cell other than the ground hovered.
        if (tool == AvatarTool.Use && !Host.Input.MouseCaptured && PickCreature(Host.Input.MousePosition) is { IsNull: false } animal
            && _sim.World.Get<Creature>(animal).Def.Race == Race.Animal
            && AvatarSystem.ActionAt(_sim, _avatar, _sim.World.Get<Position>(animal).Cell, tool) is { Kind: AvatarActionKind.Hunt or AvatarActionKind.Butcher } hunt)
            return hunt;
        foreach (var c in candidates)
        {
            var action = AvatarSystem.ActionAt(_sim, _avatar, c, tool);
            if (action.Kind != AvatarActionKind.None)
                return action;
        }
        return AvatarAction.None;
    }

    /// <summary>The container whose window is open (the wagon), or null.</summary>
    private Entity _openStorage = Entity.Null;

    /// <summary>A container's contents with a Take button each, open while the avatar stands by it.</summary>
    private void DrawStorageWindow()
    {
        if (_openStorage.IsNull)
            return;
        var w = _sim.World;
        if (!HasAvatar || InCombat || !Inventory.CanReachContainer(_sim, _avatar, _openStorage))
        {
            _openStorage = Entity.Null; // walked away (or someone picked the pack up)
            return;
        }
        var thing = w.Get<Thing>(_openStorage);
        var items = _sim.StoredIn(_openStorage);
        var handsFull = w.Get<Carrying>(_avatar).HasItem;
        var myPack = _sim.PackOf(_avatar);
        var pos = ImGui.GetIO().DisplaySize * 0.5f + new float2(60, 0); // beside the avatar, not over them
        float2? pivot = new float2(0f, 0.5f);
        ImGui.SetNextWindowPos(in pos, ImGuiCond.Appearing, in pivot);
        var open = true;
        ImGui.Begin($"{char.ToUpper(thing.Label[0])}{thing.Label[1..]}###storage", ref open, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings);
        ImGui.BeginGroup();
        if (thing.Def.Container != null)
        {
            LoadBar(_openStorage, 220);
            if (AvatarSystem.EmptySlotFor(_sim, _avatar, thing.Def) || !myPack.IsNull)
            {
                if (ImGui.SmallButton(myPack.IsNull ? "Put it on"u8 : "Swap packs"u8))
                    Issue(new EquipCommand(_avatar, _openStorage));
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                    Tip(myPack.IsNull ? "Wear it, with what's in it." : "Wear this one instead: what fits moves across from yours, which is left in your hands or here.");
            }
        }
        else
            ImGui.TextDisabled($"{items.Count}/{thing.Def.Building!.StorageStacks} stacks");
        foreach (var item in items)
        {
            ImGui.PushID(item.Index);
            var t = w.Get<Thing>(item);
            var canTake = !handsFull || (!myPack.IsNull && _sim.ContainerRoom(myPack, t.Def, t.Material) > 0);
            if (!canTake)
                ImGui.BeginDisabled(true);
            if (ImGui.Button("Take"u8, in _autoSize))
                Issue(new TakeFromStorageCommand(_avatar, item));
            if (!canTake)
                ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                Tip(!canTake ? "Your hands are full and it won't fit in your pack." : handsFull ? "Into your pack (as much as fits)." : "Into your hands.");
            EatButton(item);
            ImGui.SameLine(0, 8);
            ImGui.Text($"{w.Get<Stack>(item).Count} {t.Label}");
            ImGui.PopID();
        }
        if (items.Count == 0)
        {
            ImGui.TextDisabled("Empty."u8);
            DrawDismantle(thing);
        }
        ImGui.EndGroup();
        // Your own pack beside it, to put things in.
        ImGui.SameLine(0, 24);
        ImGui.BeginGroup();
        DrawPackList(myPack, _openStorage);
        ImGui.EndGroup();
        ImGui.End();
        if (!open)
            _openStorage = Entity.Null;
    }

    /// <summary>The inspector's request buttons for a companion: what you've asked of them, and what you could.</summary>
    private void DrawRequests(Entity companion)
    {
        var w = _sim.World;
        var current = w.TryGet<Request>(companion, out var req) ? req.Kind : (RequestKind?)null;
        ImGui.TextDisabled(current switch
        {
            RequestKind.Follow => "Following you",
            RequestKind.GuardEgg => "Standing guard",
            _ => "Doing their own work",
        });
        var guardLabel = Requests.GuardTarget(_sim, _avatar) is var ward && !ward.IsNull && !w.Has<Egg>(ward)
            ? $"Guard {w.Get<Creature>(ward).Name}" : "Guard the egg";
        if (current != RequestKind.Follow)
        {
            if (ImGui.Button("Follow me"u8, in _autoSize))
                Issue(new RequestCommand(_avatar, companion, RequestKind.Follow));
            WillingnessTip(companion, RequestKind.Follow);
        }
        if (current != RequestKind.GuardEgg && !ward.IsNull)
        {
            ImGui.SameLine(0, 6);
            if (ImGui.Button(guardLabel, in _autoSize))
                Issue(new RequestCommand(_avatar, companion, RequestKind.GuardEgg));
            WillingnessTip(companion, RequestKind.GuardEgg);
        }
        if (Requests.RecentlyRefused(_sim, _avatar, companion))
            TextC(Warn, "They've just said no to you. Give it an hour.");
        if (current != null)
        {
            ImGui.SameLine(0, 6);
            if (ImGui.Button("Back to your work"u8, in _autoSize))
                Issue(new RequestCommand(_avatar, companion, null));
        }
    }

    /// <summary>
    /// An empty wagon at journey's end can be broken up for its wood: the button marks it, then F
    /// (held) does the work. Marking first keeps a stray F from wrecking storage you meant to keep.
    /// </summary>
    private void DrawDismantle(in Thing thing)
    {
        if (!AvatarSystem.CanDismantle(_sim, _avatar, _openStorage))
            return;
        var idx = _sim.Map.Idx(thing.Cell);
        var marked = (_sim.Map.Designations[idx] & Designation.Deconstruct) != 0;
        var salvage = string.Join(", ", _sim.SalvageOf(_openStorage).Select(c => $"{c.Count} {(c.Material != null ? c.Material.Label + " " : "")}{c.Def.Label}"));
        ImGui.Separator();
        if (!marked)
        {
            if (ImGui.Button("Take it apart"u8, in _autoSize))
                Issue(new DeconstructOrderCommand(thing.Cell, true));
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip($"Mark the {thing.Label} to be broken up for {salvage}. Then hold F at it (companions on Construct work help).");
        }
        else
        {
            TextC(Warn, $"Marked to take apart for {salvage}: hold F at it.");
            if (ImGui.Button("Keep it"u8, in _autoSize))
                Issue(new DeconstructOrderCommand(thing.Cell, false));
        }
    }

    /// <summary>Hovering a request button: how likely they are to agree, and what weighs on it.</summary>
    private void WillingnessTip(Entity companion, RequestKind kind)
    {
        if (!ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            return;
        var (chance, reason) = Requests.Willingness(_sim, _avatar, companion, kind);
        var likely = chance >= 0.9f ? "Will almost surely agree" : chance >= 0.6f ? "Likely to agree" : chance >= 0.35f ? "Might refuse" : "Probably won't";
        Tip($"{likely} ({chance * 100:F0}%): {reason}.\nMood {MoodRules.MoodOf(_sim, companion) * 100:F0}, thinks of you {MoodRules.Opinion(_sim, companion, _avatar):+0;-0;0}.");
    }

    /// <summary>
    /// Keep the camera on the avatar (this also cancels the camera controller's WASD panning).
    /// Runs after the frame's ticks and follows the drawn (interpolated) position: tracking the
    /// raw tick position, or last frame's, makes the whole world judder against the avatar.
    /// Not in a fight: there WASD moves the camera (the avatar takes clicks), and the fight's own
    /// framing (the start, a moving enemy) holds; it comes back to the avatar when it's over.
    /// </summary>
    private void FollowAvatar()
    {
        if (!HasAvatar || InCombat)
            return;
        _camera.Target = AvatarDrawPos();
    }

    /// <summary>Where the avatar is drawn this frame.</summary>
    private float3 AvatarDrawPos() => _renderer.DrawPos(_sim, _avatar, _sim.World.Get<Position>(_avatar).Pos);

    /// <summary>The "[F] Chop pine tree" prompt over the avatar's head, and what it's doing while F is held.</summary>
    private void DrawAvatarPrompt()
    {
        if (!HasAvatar || InCombat)
            return;
        var w = _sim.World;
        var mind = w.Get<Mind>(_avatar);
        string text;
        if (mind.Job is AvatarWalkJob { Interact: false })
            return; // just walking where you clicked
        if (mind.Asleep)
            text = $"{mind.Job?.Report ?? "Sleeping"} (walk to get up)";
        else if (mind.Job != null)
            // Only hands-on work waits for the key; walking to a bed or eating just happens.
            text = w.Get<Avatar>(_avatar).WorkHeld || !mind.Job.IsWorking(_sim.Clock.Tick)
                ? mind.Job.Report : $"{(mind.Job is DigJob ? "[Z]" : "[F]")} {mind.Job.Report} (hold)";
        else if (WorkTarget() is { Kind: not AvatarActionKind.None } action)
            text = $"[F] {action.Label}";
        else if (_tool == ToolKind.Select && !Host.Input.MouseCaptured && ClickTarget() is { Action.Kind: not AvatarActionKind.None } click)
            text = $"[Click] {click.Action.Label}"; // out of reach: a click walks over
        else if (WorkTarget(AvatarTool.Drop) is { Kind: AvatarActionKind.PutDown } drop)
            text = $"[X] {drop.Label}";
        else if (_sim.Map.InBounds(_hoverBlock) && AvatarSystem.ActionAt(_sim, _avatar, _hoverBlock, AvatarTool.Dig) is { Kind: not AvatarActionKind.None } dig)
            text = $"[Z] {dig.Label}"; // only when pointing at a block in reach, so it isn't offered everywhere
        else if (_sim.Map.InBounds(_hoverBlock) && _sim.Map.IsSolid(_sim.Map.Idx(_hoverBlock))
                 && Mining.ShownBlock(_sim, _sim.Map.Idx(_hoverBlock)) is var rock && Mining.BreakProblem(_sim, _avatar, rock) is { } why
                 && Map.Distance(_sim.World.Get<Position>(_avatar).Cell, _hoverBlock) <= 2)
            text = $"{Capitalized(rock.Label)}: {why}"; // rock too hard for your pick
        else
            return;

        var pos = AvatarDrawPos();
        var size = w.Get<Creature>(_avatar).Stage(_sim.Clock.Tick).Size;
        var screen = _camera.WorldToScreen(pos + WorldScale.Rise(size + 0.35f));
        float2? pivot = new float2(0.5f, 1f);
        ImGui.SetNextWindowPos(in screen, ImGuiCond.Always, in pivot);
        ImGui.SetNextWindowBgAlpha(0.75f);
        ImGui.Begin("##avatarPrompt"u8, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoNav);
        ImGui.Text(text);
        ImGui.End();
    }
}
