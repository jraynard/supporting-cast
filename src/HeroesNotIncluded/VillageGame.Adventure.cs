using Brutal.ImGuiApi;
using Narsil.Ui;
using Narsil;
using Narsil.Platform;
using Narsil.Rendering;
using HeroesNotIncluded.Characters;
using HeroesNotIncluded.Net;
using HeroesNotIncluded.Rendering;
using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

/// <summary>
/// Adventure mode: the player is one character. WASD walks it (relative to the view), or the left
/// mouse button does (click to walk there, hold to follow the cursor, click on something to walk
/// over and do what F would); F works on what's in reach while held, and the camera follows it. Every input goes to the sim as an
/// <see cref="ISimCommand"/>, never by changing sim state directly.
/// </summary>
public sealed partial class VillageGame
{
    /// <summary>Played as a character: the player inhabits a villager (their avatar), rather than overseeing the town.</summary>
    public bool Adventure { get; init; }

    /// <summary>
    /// The saved character played as (<see cref="CharacterRoster"/>), if any: the hero's name,
    /// look, traits and clothes unless set apart, and whom the reveal credits with the round.
    /// Game-side only; the sim sees what's sent from it.
    /// </summary>
    public SavedCharacter? Character { get; init; }

    /// <summary>Name for the avatar (from the character creator).</summary>
    public string? HeroName { get => field ?? Character?.Name; init; }

    /// <summary>The avatar's look (from the character creator); null keeps a derived one.</summary>
    public Appearance? HeroLook { get => field ?? Character?.Look; init; }

    /// <summary>The avatar's traits by name (from the character creator); null keeps rolled ones.</summary>
    public IReadOnlyList<string>? HeroTraits { get => field ?? Character?.Traits; init; }

    /// <summary>The avatar's clothes (<c>"Def:Dye"</c>, from the character creator); null dresses them as a villager.</summary>
    public IReadOnlyList<string>? HeroClothes { get => field ?? Character?.Clothes; init; }

    /// <summary>Debug/screenshots (<c>--preset Wanderer</c>): the avatar looks and dresses as a creator preset.</summary>
    public string? HeroPreset { get; init; }

    /// <summary>Debug/screenshots (<c>--folk Elf</c>): the avatar is of this folk, with a skin their kind runs to.</summary>
    public Folk? HeroFolk { get; init; }

    /// <summary>The avatar's look and clothes: the creator's, else <see cref="HeroPreset"/>'s; then <see cref="HeroFolk"/>.</summary>
    private (Appearance? Look, IReadOnlyList<string>? Clothes) HeroAppearance()
    {
        var (look, clothes) = HeroPreset != null && _sim.Defs.AllOutfits.Find(o => o.Name.Equals(HeroPreset, StringComparison.OrdinalIgnoreCase)) is { } preset
            ? Rendering.HumanLook.FromPreset(preset, HeroLook ?? Rendering.HumanLook.Default)
            : (HeroLook, HeroClothes);
        if (HeroFolk is not { } folk || look?.Folk == folk)
            return (look, clothes);
        var a = (look ?? Rendering.HumanLook.Default) with { Folk = folk, Model = null };
        var kind = Rendering.HumanLook.FolkOf(folk);
        a = a with { SkinColor = kind.Skin[0], HairColor = kind.Hair?[0] ?? a.HairColor };
        if (kind.BeardedMen == 0)
            a = a with { Beard = BeardStyle.None };
        else if (kind.BeardedMen >= 10 && a.Body == BodyType.Male && a.Beard == BeardStyle.None)
            a = a with { Beard = BeardStyle.Full };
        return (a, clothes);
    }

    private Entity _avatar = Entity.Null;

    /// <summary>Co-op only: the local walk drawn ahead of the round trip (see <see cref="AvatarPredictor"/>).</summary>
    private readonly FreeMovePredictor _freePredictor = new();
    private bool _sentWork;

    /// <summary>Actions still live while the build drawer's open (the panels' keys are the drawer's then).</summary>
    private static readonly string[] AvatarActions =
    [
        Act.WalkUp, Act.WalkDown, Act.WalkLeft, Act.WalkRight, Act.Use, Act.Dig, Act.Drop, Act.Sleep,
        Act.Draw, Act.Guard, Act.TurnLeft, Act.TurnRight,
    ];

    /// <summary>
    /// Keys those actions are bound to now (<see cref="Controls"/>): the build drawer's letter
    /// hotkeys skip them while you play a character.
    /// </summary>
    private static HashSet<GlfwKey> AvatarKeys()
    {
        var keys = new HashSet<GlfwKey>();
        foreach (var id in AvatarActions)
            foreach (var b in Controls.Map.Bindings(id))
                if (b.Kind == Narsil.Platform.BindingKind.Key)
                    keys.Add((GlfwKey)b.Code);
        return keys;
    }

    private bool _sentDig;

    /// <summary>
    /// A player is one person in the village, not its overseer: no work orders or priorities for
    /// the AI villagers. Those belong to whoever holds the trade (M1).
    /// </summary>
    private bool OrdersLocked => HasAvatar || _sim.Round.Phase != Sim.Round.RoundPhase.None;

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
                if (_sim.World.Has<PlayerCharacter>(e))
                    continue; // a solo round's bots
                _avatar = e;
                break;
            }
        }
        _freePredictor.Reset();
        _sentWork = false;
        _sentDig = false;
        _guardSent = false;
        _clickHold = ClickHold.None;
        _clickWindow = null;
        _seenWalkFailure = !_avatar.IsNull && _sim.Ai.LastFailure.TryGetValue(_avatar, out var failed) ? failed : null;
    }

    private bool _moveKeysFromWork, _useKeyFromWork;

    /// <summary>Whether the pointer still says what F means, or the avatar has walked away from it.</summary>
    private PointerIntent _pointer;

    /// <summary>Read WASD/F and send what changed. Outside combat only: in a fight the HUD gives orders.</summary>
    private void HandleAvatarInput(Input input, float dt)
    {
        if (HasAvatar)
            _pointer.Update(input.MouseDelta != float2.Zero && !Controls.UsingPad, _sim.World.Get<Position>(_avatar).Cell);
        if (!HasAvatar || Working) // at a mini-game the keys are the mini-game's (UpdateWork)
        {
            _moveKeysFromWork = _useKeyFromWork = Working;
            return;
        }
        if (_cameraMode || PauseHeld)
        {
            HandleFreeMove(input, 0, 0); // the keys fly the camera, or time's held: your character stands still
            return;
        }
        var (x, y) = WalkAxes();
        // A key that ended a mini-game (F the last blow, A/D steering as it ended) doesn't walk off or
        // use the station again once it's over: nothing till the keys held then come up.
        if (_moveKeysFromWork && (x != 0 || y != 0))
            x = y = 0;
        else
            _moveKeysFromWork = false;
        HandleFreeMove(input, x, y);
        if (x != 0 || y != 0)
            _clickWindow = null; // walked off: the clicked wagon or bench stays shut

        HandleInventoryKeys(input);
        HandleCombatKeys(input);

        // Sleep (N) lies down where you stand (in the bed there, else on the ground): its own key, so F and clicks never put you to sleep.
        if (Controls.Map.Pressed(Act.Sleep))
            Sleep();

        // Drop (G) puts down what you hold: its own key, so F never drops anything by accident.
        if (Controls.Map.Pressed(Act.Drop))
            Drop();

        // F uses, C digs (not with a build tool out: C is theirs); both work only while held. A press starts the action, a release of both stops work.
        var use = Controls.Map.Down(Act.Use) && !WheelOpen;
        if (_useKeyFromWork && use)
            use = false;
        else
            _useKeyFromWork = false;
        var dig = Controls.Map.Down(Act.Dig) && !WheelOpen && _tool is not (ToolKind.Build or ToolKind.PlaceBuilding);
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

    private void Sleep() => Issue(new SleepCommand(_avatar));

    private void Drop()
    {
        if (WorkTarget(AvatarTool.Drop) is { Kind: AvatarActionKind.PutDown } drop)
            Issue(new AvatarDropCommand(_avatar, drop.Cell));
    }

    /// <summary>
    /// Where the walk actions point, screen-wise (x right, y up): the keys as -1/0/1, or a stick's
    /// own lean when the walk's bound to one (so a pad steers in any direction, not just eight).
    /// </summary>
    private static (float X, float Y) WalkAxes()
    {
        var map = Controls.Map;
        if (map.Pad is { } pad && StickOf(Act.WalkUp) is { } left)
        {
            var stick = left ? pad.LeftStick : pad.RightStick;
            if (stick != float2.Zero)
                return (stick.X, stick.Y);
        }
        float x = 0, y = 0;
        if (map.Down(Act.WalkUp)) y += 1;
        if (map.Down(Act.WalkDown)) y -= 1;
        if (map.Down(Act.WalkRight)) x += 1;
        if (map.Down(Act.WalkLeft)) x -= 1;
        return (x, y);
    }

    /// <summary>Whether an action's on a stick direction: true for the left, false the right, null for neither.</summary>
    private static bool? StickOf(string id)
    {
        foreach (var b in Controls.Map.Bindings(id))
            if (b.IsPad && (Narsil.Platform.PadButton)b.Code is >= Narsil.Platform.PadButton.LeftUp and <= Narsil.Platform.PadButton.RightLeft)
                return (Narsil.Platform.PadButton)b.Code <= Narsil.Platform.PadButton.LeftLeft;
        return null;
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
            case AvatarActionKind.ReadSign:
                _readingSign = toggle && _readingSign == action.Target ? Entity.Null : action.Target;
                break;
            case AvatarActionKind.Tinker:
                OpenTinkerWindow(action.Target, toggle);
                break;
            case AvatarActionKind.ReadBoard:
                Issue(new Sim.Favours.ReadBoardCommand(_avatar));
                _showShop = true;
                _favoursOpen = true;
                break;
        }
    }

    private static string Capitalized(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>
    /// A creature whose on-screen body is under the cursor. Never your own character: the camera
    /// sits on it, so clicks round yourself would select you instead of walking (the portrait does that).
    /// </summary>
    private Entity PickCreature(float2 mouse)
    {
        var w = _sim.World;
        var best = Entity.Null;
        var bestDist = 0.6f * _camera.Zoom;
        foreach (var e in w.Query<Creature>())
        {
            if (e == _avatar)
                continue;
            var pos = w.Get<Position>(e);
            if (pos.Cell.Z > _renderer.CutawayZ || _renderer.UnderDrawnFloor(_sim.Map, pos.Cell) || !View.CanSee(e))
                continue;
            var size = w.Get<Creature>(e).Stage(_sim.Clock.Tick).Size;
            var s = _camera.WorldToScreen(pos.Pos + WorldScale.Rise(size * 0.5f));
            var d = MathF.Sqrt((s.X - mouse.X) * (s.X - mouse.X) + (s.Y - mouse.Y) * (s.Y - mouse.Y));
            if (d < bestDist)
            {
                best = e;
                bestDist = d;
            }
        }
        return best;
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

    /// <summary>Where and how long ago the button went down: a walk only follows the cursor once it's clearly being dragged.</summary>
    private float2 _clickPressAt;
    private float _clickHeldFor;

    /// <summary>Hold this long (seconds) and move this far (pixels) before a walk follows the cursor.</summary>
    private const float FollowAfter = 0.25f, FollowDrag = 6f;

    /// <summary>The last walk failure shown, so each "can't get there" is said once.</summary>
    private string? _seenWalkFailure;

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
        if (!HasAvatar || _tool != ToolKind.Select)
            return false;
        UpdateHandsFor(dt);
        if (HandleFreeMouse(input))
            return true; // weapon drawn, the buttons fight
        if (HandleHandsAim(input))
            return true; // the next click lays hands on someone
        UpdateClickWindow(dt);

        var shift = input.IsKeyDown(GlfwKey.LeftShift) || input.IsKeyDown(GlfwKey.RightShift);
        if (input.WasMouseReleased(MouseButton.Left))
        {
            if (_clickHold == ClickHold.Act && !_sentWork && !_sentDig)
                Issue(new AvatarWorkCommand(_avatar, false));
            _clickHold = ClickHold.None;
        }
        if (Working)
        {
            // A mini-game in hand has the mouse, as it has the keys: a click off its window doesn't
            // walk you away from the work, open a menu or pick someone out.
            if (_clickHold == ClickHold.Walk)
                _clickHold = ClickHold.None;
            return true;
        }
        if (_clickHold == ClickHold.Walk && input.IsMouseDown(MouseButton.Left))
        {
            // A click names one cell; the camera following you mustn't drag it about. Only a held,
            // moved press steers the walk with the cursor.
            _clickHeldFor += dt;
            var moved = input.MousePosition - _clickPressAt;
            if (_clickHeldFor >= FollowAfter && moved.X * moved.X + moved.Y * moved.Y >= FollowDrag * FollowDrag)
                _clickPressAt = new float2(float.NegativeInfinity, 0); // dragging from now on
            if (float.IsNegativeInfinity(_clickPressAt.X) && _sim.Map.InBounds(_hoverCell) && WalkCell is var to && to != _clickSent)
            {
                Issue(new AvatarGoToCommand(_avatar, to));
                _clickSent = to;
            }
        }

        // In a fight the right button is your guard (HandleCombatKeys), not the item menu.
        if (input.WasMousePressed(MouseButton.Right) && NearestFoe() == null && ItemInReach() is { IsNull: false } menuItem)
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
        if (!creature.IsNull)
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
        var walkTo = WalkCell;
        Issue(new AvatarGoToCommand(_avatar, walkTo));
        (_clickHold, _clickSent, _clickPressAt, _clickHeldFor) = (ClickHold.Walk, walkTo, input.MousePosition, 0f);
        return true;
    }

    /// <summary>Where a plain click walks: the hovered cell, or, on a building's roof, in through its door to the floor under the click.</summary>
    private int3 WalkCell => Rendering.BuildingEntry.Inside(_sim, _hoverCell, _renderer.OpenBuilding.Building) ?? _hoverCell;

    /// <summary>
    /// Where a click would go: a faint ring on the hovered cell, amber on the thing a click would
    /// act on; and a ring on where a clicked walk is heading, so you can see what it understood.
    /// </summary>
    private void DrawWalkMarkers()
    {
        var ring = Host.Atlas.Get("ui/selection");
        void Mark(int3 c, uint color)
        {
            if (!_sim.Map.InBounds(c))
                return;
            var at = Position.CenterOf(c);
            if (_sim.Map.IsSolid(_sim.Map.Idx(c)))
                at.Z += 1;
            _renderer.Decal(ring, at + new float3(0, 0, 0.05f), 0.8f, color, Quad.Unlit | Quad.Always, translucent: true);
        }
        if (_sim.World.Get<Mind>(_avatar).Job is AvatarWalkJob walk)
            Mark(walk.Target, Rgba.Pack(0.55f, 0.95f, 0.5f, 0.7f));
        if (Host.Input.MouseCaptured || Working)
            return;
        var (action, cell) = ClickTarget();
        if (action.Kind != AvatarActionKind.None)
            Mark(cell, Rgba.Pack(1f, 0.75f, 0.25f, 0.65f));
        else
            Mark(WalkCell, Rgba.Pack(1f, 1f, 1f, 0.3f));
    }

    /// <summary>
    /// Under a floor that's drawn (you're downstairs and the view is on the storey above), you're
    /// hidden like everything else there: a ring shows through the floor so you don't lose yourself.
    /// </summary>
    private void DrawAvatarBeacon()
    {
        var map = _sim.Map;
        var cell = _sim.World.Get<Position>(_avatar).Cell;
        if (!_renderer.UnderDrawnFloor(map, cell))
            return;
        // On the floor over you (a quad can't draw through one): the first floor or ground above.
        var at = AvatarDrawPos();
        for (var z = cell.Z + 1; z < map.Depth; z++)
        {
            var i = map.Idx(cell.X, cell.Y, z);
            if (map.IsSolid(i))
            {
                at.Z = z + 1;
                break;
            }
            if (map.HasFloor[i] || map.HasRoof[i])
            {
                at.Z = z;
                break;
            }
        }
        var pulse = 1f + 0.06f * MathF.Sin(_time * 4f);
        _renderer.Decal(Host.Atlas.Get("ui/selection"), at + new float3(0, 0, 0.04f), 1.1f * pulse,
            Rgba.Pack(1f, 0.85f, 0.35f, 0.75f), Quad.Unlit | Quad.Always, translucent: true);
    }

    /// <summary>A clicked walk that found no way there (and nowhere near it) says so, once.</summary>
    private void NoticeWalkFailure()
    {
        if (!HasAvatar || !_sim.Ai.LastFailure.TryGetValue(_avatar, out var failure) || ReferenceEquals(failure, _seenWalkFailure))
            return;
        _seenWalkFailure = failure;
        if (failure.StartsWith(nameof(AvatarWalkJob), StringComparison.Ordinal))
            Notify(MessageKind.Info, "You can't get there from here.");
    }

    /// <summary>
    /// What a click would walk over and do. Only a thing counts: one whose sprite is under the
    /// cursor (a tree's crown, a tall station), else a structure or item in the hovered cell, the
    /// block, or what walls hide. Bare ground is a plain walk, even where F would do something
    /// (fish from a bank): a click on the ground means "go there".
    /// </summary>
    private (AvatarAction Action, int3 Cell) ClickTarget()
    {
        if (!Host.Input.MouseCaptured && PickThingSprite(Host.Input.MousePosition) is { IsNull: false } thing
            && _sim.World.Get<Thing>(thing).Cell is var at && ProbeAt(at) is { Kind: not AvatarActionKind.None } onSprite)
            return (onSprite, at);
        foreach (var c in (ReadOnlySpan<int3>)[_hoverCell, _hoverBlock, _hoverPastWalls])
            if (HoldsThing(c) && ProbeAt(c) is { Kind: not AvatarActionKind.None } action)
                return (action, c);
        return (AvatarAction.None, _hoverCell);
    }

    /// <summary>Something to click on in a cell: a structure, plant, item or blueprint, not just ground.</summary>
    private bool HoldsThing(int3 c)
    {
        var map = _sim.Map;
        if (!map.InBounds(c))
            return false;
        var i = map.Idx(c);
        return !map.Structure[i].IsNull || !map.Item[i].IsNull
            || map.Floor[i] is { IsNull: false } f && _sim.World.Has<Construction>(f);
    }

    /// <summary>
    /// A plant or piece of furniture whose sprite is under the cursor, frontmost first. Tall sprites
    /// rise over cells behind them, so the ground under the cursor isn't enough: clicking a tree's
    /// crown should chop that tree, not walk to the grass behind it.
    /// </summary>
    private Entity PickThingSprite(float2 mouse)
    {
        var map = _sim.Map;
        var w = _sim.World;
        var best = Entity.Null;
        var bestFront = float.NegativeInfinity;
        var cut = Math.Min(_renderer.CutawayZ, map.Depth - 1);
        var c0 = _hoverCell;
        // A sprite's base is nearer the camera than the cell its top hides: look a few cells round.
        for (var dz = -1; dz <= 1; dz++)
        for (var dy = -3; dy <= 3; dy++)
        for (var dx = -3; dx <= 3; dx++)
        {
            var c = new int3(c0.X + dx, c0.Y + dy, c0.Z + dz);
            if (!map.InBounds(c) || c.Z > cut)
                continue;
            var e = map.Structure[map.Idx(c)];
            if (e.IsNull || !w.TryGet<Thing>(e, out var thing) || thing.Cell != c || !View.CanSee(e))
                continue; // a wide piece is tested once, from its anchor
            if (!HideRoofs && c.Z > _renderer.OpenBuilding.Level && !_renderer.OpenBuilding.Building.IsNull
                && Rendering.BuildingPlanIndex.For(_sim).At(c) == _renderer.OpenBuilding.Building)
                continue; // the storey above you in the building you're in isn't drawn (playtest: the bed upstairs, clicked from a table)
            var def = thing.Def;
            if (def.Building?.IsWall == true || (def.Model == null && def.Shape is RenderShape.Block or RenderShape.Ramp or RenderShape.Slab or RenderShape.Decal))
                continue; // walls and flat things are picked by the cell
            var rise = def.Model != null ? 0.9f : def.RenderSize; // sprite cells
            var feet = _camera.WorldToScreen(Position.CenterOf(c));
            var top = _camera.WorldToScreen(Position.CenterOf(c) + WorldScale.Rise(rise));
            var halfWidth = (def.Model != null ? 0.5f : 0.3f * def.RenderSize) * _camera.Zoom;
            if (mouse.X < feet.X - halfWidth || mouse.X > feet.X + halfWidth || mouse.Y > feet.Y + 0.15f * _camera.Zoom || mouse.Y < top.Y)
                continue;
            if (feet.Y > bestFront)
                (best, bestFront) = (e, feet.Y);
        }
        return best;
    }

    /// <summary>What F would do at a cell from right by it. Setting out counts only on the wagon itself, not anywhere near it.</summary>
    private AvatarAction ProbeAt(int3 cell)
    {
        if (!_sim.Map.InBounds(cell))
            return AvatarAction.None;
        var action = AvatarSystem.ActionAt(_sim, _avatar, cell, from: cell);
        return action;
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
        NoticeWalkFailure();
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

    /// <summary>
    /// What F would act on: the hovered cell (or the block under the cursor, or what a wall in the
    /// way hides) if it's in reach and you haven't walked since the pointer moved, else the cell in front
    /// of the avatar, else where it stands. Digging (C) and putting down (G) look at their own cells.
    /// </summary>
    private AvatarAction WorkTarget(AvatarTool tool = AvatarTool.Use)
    {
        var at = _sim.World.Get<Position>(_avatar).Cell;
        var facing = _sim.World.Get<Avatar>(_avatar).Facing;
        var front = new int3(at.X + facing.X, at.Y + facing.Y, at.Z);
        // Digging: the hovered block, else the face in front, else the ground in front (a way down).
        // With the pointer over a window, what's behind it isn't what you mean: only what's in front. The same once
        // you've walked away from where the pointer rests (a stick and an idle trackpad on a Steam Deck).
        var overUi = ImGui.GetIO().WantCaptureMouse || _pointer.Stale || Controls.UsingPad;
        ReadOnlySpan<int3> candidates = (tool, overUi) switch
        {
            (AvatarTool.Dig, false) => [_hoverBlock, front, new int3(front.X, front.Y, front.Z - 1)],
            (AvatarTool.Dig, true) => [front, new int3(front.X, front.Y, front.Z - 1)],
            (AvatarTool.Drop, false) => [_hoverCell, _hoverPastWalls, front, at],
            (_, false) => [_hoverCell, _hoverBlock, _hoverPastWalls, front, at],
            _ => [front, at],
        };
        // Beside the butts' mark (on it, or a step off): F starts the turn, whichever way you face.
        if (tool == AvatarTool.Use && Sim.Work.FestivalGames.MarkBeside(_sim, _avatar) is { IsNull: false } mark
            && AvatarSystem.ActionAt(_sim, _avatar, _sim.World.Get<Thing>(mark).Cell, tool) is { Kind: AvatarActionKind.FestivalGame } shoot)
            return shoot;
        // A carcass under the cursor comes first: its sprite lies over a cell other than the ground hovered.
        if (tool == AvatarTool.Use && !overUi && !Host.Input.MouseCaptured && PickCreature(Host.Input.MousePosition) is { IsNull: false } animal
            && _sim.World.Get<Creature>(animal).Def.Race == Race.Animal
            && AvatarSystem.ActionAt(_sim, _avatar, _sim.World.Get<Position>(animal).Cell, tool) is { Kind: AvatarActionKind.Butcher or AvatarActionKind.Carry } carcass)
            return carcass;
        foreach (var c in candidates)
        {
            var action = AvatarSystem.ActionAt(_sim, _avatar, c, tool);
            if (action.Kind != AvatarActionKind.None)
                return action;
        }
        // A customer waiting on you within handing reach, wherever you face: across the counter, a
        // step off. With a pad there's no pointing at them, and playtesters couldn't find how to take one.
        if (tool == AvatarTool.Use)
            foreach (var o in View.Orders())
                if (o.Status == Sim.Shops.OrderStatus.Open && !o.Mine && o.AssigneeName.Length == 0 && Sim.Shops.ShopSystem.HandsOver(o.Kind)
                    && _sim.World.TryGet<Position>(o.Poster, out var waiting)
                    && AvatarSystem.ActionAt(_sim, _avatar, waiting.Cell, tool) is { Kind: AvatarActionKind.TakeOrder } take)
                    return take;
        // Nothing where you're looking: a station, cask or chest anywhere beside you will do, so you
        // needn't line up square in front of a forge in a cramped smithy.
        if (tool == AvatarTool.Use)
        {
            AvatarAction best = AvatarAction.None;
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                    continue;
                var action = AvatarSystem.ActionAt(_sim, _avatar, new int3(at.X + dx, at.Y + dy, at.Z), tool);
                if (Rank(action) > Rank(best))
                    best = action;
            }
            return best;
        }
        return AvatarAction.None;
    }

    /// <summary>
    /// Which use beside you F prefers when you aren't facing anything: the next step with the piece
    /// in your tongs (the fire while it's dark, the anvil or tub while it glows), then working a
    /// craft, the stew pot over a station, a cask, a chest.
    /// </summary>
    private int Rank(AvatarAction action)
    {
        if (action.Kind != AvatarActionKind.Smith)
            return FurnitureRank(action.Kind);
        var fire = _sim.World.IsAlive(action.Target) && _sim.World.Get<Thing>(action.Target).Def.Building?.Fuel != null;
        var hot = View.Piece(_avatar) is { Hot: true };
        return fire != hot ? 6 : 5;
    }

    /// <summary>Which furniture use beside you F prefers when you aren't facing anything: working a craft first, then a station, a cask, a chest.</summary>
    private static int FurnitureRank(AvatarActionKind kind) => kind switch
    {
        AvatarActionKind.Cook or AvatarActionKind.BarWork => 5,
        AvatarActionKind.Clean => 1,
        AvatarActionKind.Craft => 4,
        AvatarActionKind.OpenStation or AvatarActionKind.ReadSign or AvatarActionKind.Tinker or AvatarActionKind.OrderMeal or AvatarActionKind.OrderProvisions or AvatarActionKind.OrderHealing => 3,
        AvatarActionKind.Pour => 2,
        AvatarActionKind.OpenStorage => 1,
        _ => 0,
    };

    /// <summary>The container whose window is open (the wagon), or null.</summary>
    private Entity _openStorage = Entity.Null;

    /// <summary>A container's contents with a Take button each, open while the avatar stands by it.</summary>
    private void DrawStorageWindow()
    {
        if (_openStorage.IsNull)
            return;
        var w = _sim.World;
        if (!HasAvatar || !Inventory.CanReachContainer(_sim, _avatar, _openStorage))
        {
            _openStorage = Entity.Null; // walked away (or someone picked the pack up)
            return;
        }
        var thing = w.Get<Thing>(_openStorage);
        var items = _sim.StoredIn(_openStorage);
        var handsFull = w.Get<Carrying>(_avatar).HasItem;
        var myPack = _sim.PackOf(_avatar);
        PlaceBeside(2 * PackColumnWidth + Narsil.Ui.Gui.S(60)); // beside the avatar, not over them
        var open = true;
        ScreenFit.Next();
        Narsil.Ui.Panel.Begin($"{char.ToUpper(thing.Label[0])}{thing.Label[1..]}###storage", ref open, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings);
        PadWindow("###storage", () => _openStorage = Entity.Null);
        if (!BeginTwoColumns("##storage-cols"))
        {
            Narsil.Ui.Panel.End();
            return;
        }
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
        DrawStorageTiles(items, handsFull, myPack);
        if (items.Count == 0)
        {
            ImGui.TextDisabled("Empty."u8);
            DrawDismantle(thing);
        }
        if (thing.Def.Name == Sim.Hospitality.InnRooms.BoardDef)
            DrawRoomBook(_openStorage);
        // Your own pack beside it, to put things in.
        ImGui.TableSetColumnIndex(1);
        DrawPackTiles(myPack);
        ImGui.EndTable();
        Narsil.Ui.Panel.End();
        if (!open)
            _openStorage = Entity.Null;
    }

    /// <summary>
    /// What's in the open container as a grid of tiles, icons only: left click (or A) takes one,
    /// right click (or Y) the whole stack, into your pack if it fits, else your hands. With a pack
    /// item picked up (<see cref="CarriedItem"/>), the tiles (and a free one after them) take it in.
    /// </summary>
    private void DrawStorageTiles(List<Entity> items, bool handsFull, Entity myPack)
    {
        var w = _sim.World;
        var carried = CarriedItem;
        ISimCommand? store = null;
        if (!carried.IsNull && w.Get<Thing>(carried) is var ct && _sim.ContainerRoom(_openStorage, ct.Def, ct.Material) > 0)
            store = new StoreFromPackCommand(_avatar, _openStorage, carried);
        var gap = Gui.S(4);
        var tile = Gui.S(IconSize) + 2 * Gui.S(5);
        var across = Math.Max(1, (int)((ImGui.GetContentRegionAvail().X + gap) / (tile + gap)));
        // Solo applies a command at once, so a take can leave the item gone (or merged) this frame:
        // stop drawing after the first one.
        for (var k = 0; k < items.Count; k++)
        {
            var item = items[k];
            if (!w.IsAlive(item))
                continue;
            if (k % across != 0)
                ImGui.SameLine(0, gap);
            var t = w.Get<Thing>(item);
            var count = w.TryGet<Stack>(item, out var stack) ? stack.Count : 1;
            var packRoom = myPack.IsNull ? 0 : _sim.ContainerRoom(myPack, t.Def, t.Material);
            var canTake = !handsFull || packRoom > 0;
            ImGui.PushID(item.Index);
            var min = ImGui.GetCursorScreenPos();
            var box = DrawSlotArt(item, "", dim: !canTake && carried.IsNull);
            var one = ImGui.InvisibleButton("##take"u8, in box, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.EnableNav);
            if (!carried.IsNull)
            {
                // Carrying a pack item: every tile here is the container, to put it in.
                if (store != null)
                {
                    MarkDropTarget(min, box, ImGui.IsItemHovered(ImGuiHoveredFlags.None));
                    if (one)
                    {
                        ImGui.PopID();
                        PutCarried(store);
                        return;
                    }
                }
                ImGui.PopID();
                continue;
            }
            var all = ImGui.IsItemClicked(ImGuiMouseButton.Right)
                || (ImGui.IsItemFocused() && ImGui.IsKeyPressed(ImGuiKey.GamepadFaceUp, false));
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip($"{ItemLine(item)}\n{ItemDetails(item)}\n{TakeTip(canTake, packRoom, handsFull, count)}");
            ImGui.PopID();
            if (canTake && (one || all))
            {
                Issue(all && count > 1
                    ? new TakeFromStorageCommand(_avatar, item, TakeFromStorageCommand.All)
                    : new TakeFromStorageCommand(_avatar, item));
                break;
            }
        }
        if (store != null)
        {
            // A free place after what's there (all there is, in an empty chest).
            if (items.Count % across != 0)
                ImGui.SameLine(0, gap);
            var min = ImGui.GetCursorScreenPos();
            var box = DrawSlotArt(Entity.Null, "");
            var put = ImGui.InvisibleButton("##put"u8, in box, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.EnableNav);
            MarkDropTarget(min, box, ImGui.IsItemHovered(ImGuiHoveredFlags.None));
            if (put)
                PutCarried(store);
        }
    }

    /// <summary>A storage tile's tooltip line: what a click and a right click would do.</summary>
    private static string TakeTip(bool canTake, int packRoom, bool handsFull, int count)
    {
        if (!canTake)
            return "Your hands are full and it won't fit in your pack.";
        var one = $"{(Controls.UsingPad ? "A" : "Click")}: take one, into your {(packRoom > 0 ? "pack" : "hands")}.";
        if (count <= 1)
            return one;
        var all = packRoom <= 0 ? "into your hands" : handsFull ? "as much as fits in your pack" : "what fits in your pack, the rest in your hands";
        return $"{one}\n{(Controls.UsingPad ? "Y" : "Right click")}: take them all, {all}.";
    }

    /// <summary>The inspector's request buttons for a companion: what you've asked of them, and what you could.</summary>
    private void DrawRequests(Entity companion)
    {
        var w = _sim.World;
        var current = w.TryGet<Request>(companion, out var req) ? req.Kind : (RequestKind?)null;
        ImGui.TextDisabled(current switch
        {
            RequestKind.Follow => "Following you",
            RequestKind.Guard => "Standing guard",
            _ => "Doing their own work",
        });
        if (current != RequestKind.Follow)
        {
            if (ImGui.Button("Follow me"u8, in _autoSize))
                Issue(new RequestCommand(_avatar, companion, RequestKind.Follow));
            WillingnessTip(companion, RequestKind.Follow);
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
                Tip(Controls.Keys($"Mark the {thing.Label} to be broken up for {salvage}. Then hold ~F~ at it (companions on Construct work help)."));
        }
        else
        {
            TextC(Warn, Controls.Keys($"Marked to take apart for {salvage}: hold ~F~ at it."));
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
        if (!HasAvatar || LookAt != null || _cameraMode)
            return;
        // A look elsewhere (a festival card's line, a toast) holds until you move, or a while passes.
        if (_lookingAt is { } look)
        {
            if (_time - _lookingSince < LookHold && _sim.World.Get<Position>(_avatar).Cell == _lookingFrom)
            {
                _camera.Target = look;
                return;
            }
            _lookingAt = null;
        }
        _camera.Target = AvatarDrawPos() + ShakeOffset() + DrunkSway();
    }

    /// <summary>Seconds a look elsewhere holds the camera before it comes back to your character.</summary>
    private const float LookHold = 8f;

    private float3? _lookingAt;
    private float _lookingSince;
    private int3 _lookingFrom;

    /// <summary>Show a cell: the camera goes there and stays, in a round until your character moves (or <see cref="LookHold"/> passes).</summary>
    private void LookAtCell(int3 cell)
    {
        var at = new float3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z);
        _camera.Target = at;
        if (!HasAvatar)
            return;
        _lookingAt = at;
        _lookingSince = _time;
        _lookingFrom = _sim.World.Get<Position>(_avatar).Cell;
    }

    /// <summary>Where the avatar is drawn this frame.</summary>
    private float3 AvatarDrawPos() => _renderer.DrawPos(_sim, _avatar, _sim.World.Get<Position>(_avatar).Pos);

    /// <summary>
    /// The prompt over the avatar's head: a key cap and what it does ("F  Chop pine tree"), or what
    /// it's doing while the key is held. The key is drawn, not spelled out in brackets, so it reads
    /// at a glance.
    /// </summary>
    private void DrawAvatarPrompt()
    {
        if (!HasAvatar)
            return;
        var w = _sim.World;
        var mind = w.Get<Mind>(_avatar);
        string? key = null;
        string text;
        var hold = false;
        if (mind.Job is AvatarWalkJob { Interact: false })
            return; // just walking where you clicked
        if (mind.Asleep)
            text = NightPassing
                ? "Everyone's asleep: the night passes (walk to get up)"
                : $"{mind.Job?.Report ?? "Sleeping"} (walk to get up; time runs on once everyone sleeps)";
        else if (mind.Job != null)
        {
            // Only hands-on work waits for the key; walking to a bed or eating just happens.
            text = mind.Job.Report;
            if (!w.Get<Avatar>(_avatar).WorkHeld && mind.Job.IsWorking(_sim.Clock.Tick))
                (key, hold) = (CapFor(mind.Job is DigJob ? Act.Dig : Act.Use), true);
        }
        else if (WorkTarget() is { Kind: not AvatarActionKind.None } action)
            (key, text) = (CapFor(Act.Use), action.Label);
        else if (_tool == ToolKind.Select && !Controls.UsingPad && !Host.Input.MouseCaptured && ClickTarget() is { Action.Kind: not AvatarActionKind.None } click)
            (key, text) = ("Click", click.Action.Label); // out of reach: a click walks over
        else if (WorkTarget(AvatarTool.Drop) is { Kind: AvatarActionKind.PutDown } drop)
            (key, text) = (CapFor(Act.Drop), drop.Label);
        else if (_sim.Map.InBounds(_hoverBlock) && AvatarSystem.ActionAt(_sim, _avatar, _hoverBlock, AvatarTool.Dig) is { Kind: not AvatarActionKind.None } dig)
            (key, text) = (CapFor(Act.Dig), dig.Label); // only when pointing at a block in reach, so it isn't offered everywhere
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
        if (key != null)
        {
            Gui.KeyCap(key);
            ImGui.SameLine(0, Gui.S(6));
        }
        ImGui.AlignTextToFramePadding();
        ImGui.Text(text);
        if (hold)
        {
            ImGui.SameLine(0, Gui.S(6));
            TextC(HeroesNotIncluded.Ui.Theme.Dim, "hold");
        }
        ImGui.End();
    }
}
