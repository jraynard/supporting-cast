using Brutal.ImGuiApi;
using Narsil.Ui;
using Narsil;
using Narsil.Platform;
using Narsil.Rendering;
using SupportingCast.Net;
using SupportingCast.Rendering;
using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Core;
using SupportingCast.Sim.Ecs;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Scenarios;
using SupportingCast.Sim.Systems;

namespace SupportingCast;

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

    private Entity _avatar = Entity.Null;
    private int2 _sentMoveDir;

    /// <summary>Co-op only: the local walk drawn ahead of the round trip (see <see cref="AvatarPredictor"/>).</summary>
    private readonly AvatarPredictor _predictor = new();
    private bool _sentWork;

    /// <summary>
    /// Keys the avatar's controls use.
    /// the toolbar's letter hotkeys skip them in Adventure mode.
    /// </summary>
    private static readonly GlfwKey[] AvatarKeys = [GlfwKey.W, GlfwKey.A, GlfwKey.S, GlfwKey.D, GlfwKey.F, GlfwKey.Z, GlfwKey.X, GlfwKey.N];

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
        _sentMoveDir = default;
        _sentKeys = 0;
        _predictor.Reset();
        _sentWork = false;
        _sentDig = false;
        _clickHold = ClickHold.None;
        _clickWindow = null;
        _seenWalkFailure = !_avatar.IsNull && _sim.Ai.LastFailure.TryGetValue(_avatar, out var failed) ? failed : null;
    }

    /// <summary>
    /// Seconds a direction that may be half a two-key press waits before it's sent: starting on one
    /// key (the second often lands a frame later), or letting go of one of two (the other often
    /// follows). Without it, those frames start a stray step the wrong way.
    /// </summary>
    private const float ChordGrace = 0.05f;

    private int2 _heldDir;
    private float _heldFor;
    private int _sentKeys;
    private bool _moveKeysFromWork, _useKeyFromWork;

    /// <summary>Read WASD/F and send what changed. Outside combat only: in a fight the HUD gives orders.</summary>
    private void HandleAvatarInput(Input input, float dt)
    {
        if (!HasAvatar || Working) // at a mini-game the keys are the mini-game's (UpdateWork)
        {
            _moveKeysFromWork = _useKeyFromWork = Working;
            return;
        }
        float x = 0, y = 0;
        if (input.IsKeyDown(GlfwKey.W)) y += 1;
        if (input.IsKeyDown(GlfwKey.S)) y -= 1;
        if (input.IsKeyDown(GlfwKey.D)) x += 1;
        if (input.IsKeyDown(GlfwKey.A)) x -= 1;
        // A key that ended a mini-game (D draws the piece out, F the last blow) doesn't walk off or
        // use the station again once it's over: nothing till the keys held then come up.
        if (_moveKeysFromWork && (x != 0 || y != 0))
            x = y = 0;
        else
            _moveKeysFromWork = false;
        var dir = MapDirection(x, y);
        if (dir != _heldDir)
            (_heldDir, _heldFor) = (dir, 0f);
        else
            _heldFor += dt;
        // One key held, coming from nothing or from two: wait a moment for the other key.
        var oneKey = (x != 0) != (y != 0);
        var sentTwo = _sentKeys == 2;
        if (oneKey && (_sentMoveDir == int2.Zero || sentTwo) && _heldFor < ChordGrace)
            dir = _sentMoveDir;
        if (dir != _sentMoveDir)
        {
            _sentKeys = dir == int2.Zero ? 0 : oneKey ? 1 : 2;
            // Co-op: walk on screen at once, and tell the sim where we turned or stopped so it
            // ends up there too when the command comes back.
            var at = _coop != null ? _predictor.DirectionChanged(_sim, _avatar, dir) : null;
            Issue(new AvatarMoveCommand(_avatar, dir, at));
            _sentMoveDir = dir;
        }
        if (dir != int2.Zero)
            _clickWindow = null; // walked off: the clicked wagon or bench stays shut

        HandleInventoryKeys(input);

        // N lies down where you stand (in the bed there, else on the ground): its own key, so F and clicks never put you to sleep.
        if (input.WasKeyPressed(GlfwKey.N))
            Issue(new SleepCommand(_avatar));

        // X puts down what you hold: its own key, so F never drops anything by accident.
        if (input.WasKeyPressed(GlfwKey.X) && WorkTarget(AvatarTool.Drop) is { Kind: AvatarActionKind.PutDown } drop)
            Issue(new AvatarDropCommand(_avatar, drop.Cell));

        // F uses, Z digs; both work only while held. A press starts the action, a release of both stops work.
        var use = input.IsKeyDown(GlfwKey.F);
        if (_useKeyFromWork && use)
            use = false;
        else
            _useKeyFromWork = false;
        var dig = input.IsKeyDown(GlfwKey.Z);
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
        UpdateClickWindow(dt);

        var shift = input.IsKeyDown(GlfwKey.LeftShift) || input.IsKeyDown(GlfwKey.RightShift);
        if (input.WasMouseReleased(MouseButton.Left))
        {
            if (_clickHold == ClickHold.Act && !_sentWork && !_sentDig)
                Issue(new AvatarWorkCommand(_avatar, false));
            _clickHold = ClickHold.None;
        }
        else if (_clickHold == ClickHold.Walk && input.IsMouseDown(MouseButton.Left))
        {
            // A click names one cell; the camera following you mustn't drag it about. Only a held,
            // moved press steers the walk with the cursor.
            _clickHeldFor += dt;
            var moved = input.MousePosition - _clickPressAt;
            if (_clickHeldFor >= FollowAfter && moved.X * moved.X + moved.Y * moved.Y >= FollowDrag * FollowDrag)
                _clickPressAt = new float2(float.NegativeInfinity, 0); // dragging from now on
            if (float.IsNegativeInfinity(_clickPressAt.X) && _sim.Map.InBounds(_hoverCell) && _hoverCell != _clickSent)
            {
                Issue(new AvatarGoToCommand(_avatar, _hoverCell));
                _clickSent = _hoverCell;
            }
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
        Issue(new AvatarGoToCommand(_avatar, _hoverCell));
        (_clickHold, _clickSent, _clickPressAt, _clickHeldFor) = (ClickHold.Walk, _hoverCell, input.MousePosition, 0f);
        return true;
    }

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
            Mark(_hoverCell, Rgba.Pack(1f, 1f, 1f, 0.3f));
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
        // With the pointer over a window, what's behind it isn't what you mean: only what's in front.
        var overUi = ImGui.GetIO().WantCaptureMouse;
        ReadOnlySpan<int3> candidates = (tool, overUi) switch
        {
            (AvatarTool.Dig, false) => [_hoverBlock, front, new int3(front.X, front.Y, front.Z - 1)],
            (AvatarTool.Dig, true) => [front, new int3(front.X, front.Y, front.Z - 1)],
            (AvatarTool.Drop, false) => [_hoverCell, _hoverPastWalls, front, at],
            (_, false) => [_hoverCell, _hoverBlock, _hoverPastWalls, front, at],
            _ => [front, at],
        };
        // An animal under the cursor comes first: its sprite stands over a cell other than the ground hovered.
        if (tool == AvatarTool.Use && !overUi && !Host.Input.MouseCaptured && PickCreature(Host.Input.MousePosition) is { IsNull: false } animal
            && _sim.World.Get<Creature>(animal).Def.Race == Race.Animal
            && AvatarSystem.ActionAt(_sim, _avatar, _sim.World.Get<Position>(animal).Cell, tool) is { Kind: AvatarActionKind.Hunt or AvatarActionKind.Butcher } hunt)
            return hunt;
        foreach (var c in candidates)
        {
            var action = AvatarSystem.ActionAt(_sim, _avatar, c, tool);
            if (action.Kind != AvatarActionKind.None)
                return action;
        }
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
        AvatarActionKind.OpenStation => 3,
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
        var pos = ImGui.GetIO().DisplaySize * 0.5f + new float2(60, 0); // beside the avatar, not over them
        float2? pivot = new float2(0f, 0.5f);
        ImGui.SetNextWindowPos(in pos, ImGuiCond.Appearing, in pivot);
        var open = true;
        Narsil.Ui.Panel.Begin($"{char.ToUpper(thing.Label[0])}{thing.Label[1..]}###storage", ref open, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings);
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
        Narsil.Ui.Panel.End();
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
        if (!HasAvatar)
            return;
        _camera.Target = AvatarDrawPos();
    }

    /// <summary>Where the avatar is drawn this frame.</summary>
    private float3 AvatarDrawPos() => _renderer.DrawPos(_sim, _avatar, _sim.World.Get<Position>(_avatar).Pos);

    /// <summary>The "[F] Chop pine tree" prompt over the avatar's head, and what it's doing while F is held.</summary>
    private void DrawAvatarPrompt()
    {
        if (!HasAvatar)
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
