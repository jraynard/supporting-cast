using Brutal.ImGuiApi;
using Narsil;
using Narsil.Platform;
using DragonWorld.Rendering;
using Narsil.Rendering;
using DragonWorld.Sim;
using DragonWorld.Sim.Combat;
using DragonWorld.Sim.Components;
using DragonWorld.Sim.Core;
using DragonWorld.Sim.Defs;
using DragonWorld.Sim.Ecs;
using DragonWorld.Sim.Systems;

namespace DragonWorld;

/// <summary>
/// Turn-based combat in the game: while <see cref="Simulation.Combat"/> is active the real-time
/// loop stops, the player commands the village's units in its phase, paying time units for
/// everything (click a highlighted cell to move, an enemy to attack in the chosen shot mode;
/// Ctrl+click to hover a flyer; Enter ends the turn). Every order is a command (<c>Core/CombatCommands.cs</c>)
/// sent through <see cref="Issue"/>, and the fight's pacing (moves animated a step at a time,
/// a pause on each reaction, the AI's steps) is <see cref="Encounter.Advance"/>: single player runs
/// it 60 times a second here, co-op once per lockstep step, so a fight plays alike on every peer. Enemies with TU left to react get an amber ring and
/// the ground they cover; the move preview turns red where a move would draw a reaction, and
/// amber where it leaves too few TU to attack. The HUD shows the round, each unit's TU and the
/// combat log; the tooltip over an enemy breaks down the odds.
/// </summary>
public sealed partial class VillageGame
{
    private bool _wasInCombat;

    /// <summary>The round and phase the camera last framed, so a new turn for the village recentres on its unit.</summary>
    private (int Round, Side Phase) _framedTurn;

    /// <summary>Where the camera glides at the start of the player's turn, and for how much longer (real seconds).</summary>
    private float3 _turnFocus;
    private float _turnGlide;

    /// <summary>Real seconds the camera takes to settle on the player's unit at the start of their turn.</summary>
    private const float TurnGlideTime = 0.6f;
    /// <summary>Real time owed to <see cref="Encounter.Advance"/> (single player), in steps.</summary>
    private double _combatSteps;

    /// <summary>How much of <see cref="Encounter.Feedback"/> has been shown as notices.</summary>
    private int _seenFeedback;

    /// <summary>Chosen attack for the selected unit (null: the best one for the target), and its shot mode.</summary>
    private AttackDef? _combatAttack;

    private ShotMode _combatMode = ShotMode.Snap;

    private bool _breathMode;

    /// <summary>A body being dragged: the next click on a highlighted cell beside it drops it there.</summary>
    private Entity _dragBody = Entity.Null;
    private string? _combatNotice;
    private float _noticeUntil;

    private int _seenReactions;

    /// <summary>Reactions the hovered move would draw (cached per hovered cell and range).</summary>
    private List<Threat> _threats = [];
    private (int3 Cell, (Entity, float, int, Side, int, bool, bool) Range) _threatKey;

    /// <summary>Cells each watcher covers, per round and phase (line-of-fire checks are too slow per frame).</summary>
    private readonly Dictionary<Entity, (int Round, Side Phase, List<int3> Cells)> _watched = [];

    private MoveRange? _range;
    private (Entity Unit, float Left, int Round, Side Phase, int Log, bool Busy, bool Reserve) _rangeKey;

    private bool InCombat => _sim.Combat.Active;

    private void Notice(string text)
    {
        _combatNotice = text;
        _noticeUntil = _time + 4f;
    }

    /// <summary>The selected unit, if it's the village's and can take orders now.</summary>
    private CombatUnit? CommandableUnit()
    {
        var enc = _sim.Combat;
        if (!_sim.World.IsAlive(_selected) || enc.Unit(_selected) is not { } u)
            return null;
        if (u.Side != Side.Village || enc.Phase != Side.Village || enc.AiControls(u) || u.Acted || !Mine(u.Entity))
            return null;
        return _sim.World.Get<Creature>(_selected).Able ? u : null;
    }

    /// <summary>Is this unit the local player's to command? Single player: any; co-op: their character and dragon.</summary>
    private bool Mine(Entity e) => _coop == null || CommandRules.ControllerOf(_sim, e) == _coop.Me.Id;

    /// <summary>The player id combat orders go out under: null single player.</summary>
    private Guid? CombatPlayer => _coop?.Me.Id;

    private void OnCombatStarted()
    {
        _wasInCombat = true;
        SetTool(ToolKind.Select);
        _combatAttack = null;
        _breathMode = false;
        var enc = _sim.Combat;
        var mine = (HasAvatar ? enc.Unit(_avatar) : null)
            ?? enc.Units.Find(u => u.Side == Side.Village && CombatRules.Attacks(_sim, u.Entity).Count > 0) ?? enc.Units.Find(u => u.Side == Side.Village);
        var theirs = enc.Units.Find(u => u.Side == Side.Raiders);
        if (mine != null)
            _selected = mine.Entity;
        _seenReactions = enc.ReactionCount;
        _seenFeedback = 0;
        _combatSteps = 0;
        if (mine != null && theirs != null)
        {
            var a = _sim.World.Get<Position>(mine.Entity).Pos;
            var b = _sim.World.Get<Position>(theirs.Entity).Pos;
            _camera.Target = (a + b) * 0.5f;
        }
        // The opening shot frames both sides; later turns of the village's recentre on its unit.
        _framedTurn = (enc.Round, enc.Phase);
        _turnGlide = 0;
        Log.Engine.Info($"Combat started at {_sim.Clock.TimeString()} with {enc.Units.Count} units");
        Notice("Combat! Moves and actions cost time units (TU). Click a highlighted cell to move, an enemy to attack. "
            + "TU you don't spend are kept for reactions. WASD moves the camera. Enter ends your turn.");
    }

    private void OnCombatEnded()
    {
        _wasInCombat = false;
        if (_defReloadPending)
            ReloadDefs();
        _range = null;
        _breathMode = false;
        _combatAttack = null;
        if (_sim.Combat.Outcome is { } outcome)
            Notice(outcome);
        Log.Engine.Info($"Combat ended after {_sim.Combat.Round} rounds: {_sim.Combat.Outcome}");
        if (Paused)
            SetSpeed(1);
    }

    /// <summary>Single player: play the fight's steps for this frame's real time (<see cref="Encounter.Advance"/>).</summary>
    private void RunCombat(float dt)
    {
        var enc = _sim.Combat;
        _combatSteps = Math.Min(_combatSteps + dt * SimClock.TicksPerSecond, 10);
        while (_combatSteps >= 1 && enc.Active)
        {
            enc.Advance(_sim);
            _combatSteps -= 1;
        }
    }

    /// <summary>
    /// What the player sees of the fight each frame, however its steps ran: the camera on a new
    /// turn and on a moving enemy, and notices for what their orders came to and for reactions.
    /// </summary>
    private void WatchCombat(float dt)
    {
        var enc = _sim.Combat;
        if (!enc.Active)
            return;
        FrameTurn(dt);
        var shown = false;
        for (; _seenFeedback < enc.Feedback.Count; _seenFeedback++)
            if (enc.Feedback[_seenFeedback] is var (unit, text) && Mine(unit))
            {
                Notice(text);
                shown = true;
            }
        if (enc.ReactionCount != _seenReactions)
        {
            // An order's notice already carries the answers it drew.
            _seenReactions = enc.ReactionCount;
            if (!shown && enc.Log.Count > 0)
                Notice(enc.Log[^1].Text);
        }
        // Keep a moving raider (or dragon acting on its own) in view.
        if (enc.Busy(_sim) && enc.Unit(enc.Animating) is { } u && enc.AiControls(u))
        {
            var p = _sim.World.Get<Position>(enc.Animating).Pos;
            _camera.Target += (p - _camera.Target) * Math.Min(1f, dt * 2f);
        }
    }

    /// <summary>
    /// When the village's turn comes round (and the player is playing it), select the unit to
    /// command (the avatar in Adventure mode, else the last one selected if it can still act) and
    /// glide the camera onto it. WASD takes over again once the glide settles.
    /// </summary>
    private void FrameTurn(float dt)
    {
        var enc = _sim.Combat;
        var turn = (enc.Round, enc.Phase);
        if (turn != _framedTurn && !enc.Busy(_sim))
        {
            _framedTurn = turn;
            if (enc.Phase == Side.Village && !enc.VillageAuto && TurnUnit() is { } u)
            {
                _selected = u.Entity;
                _combatAttack = null;
                _breathMode = false;
                _dragBody = Entity.Null;
                _turnFocus = _sim.World.Get<Position>(u.Entity).Pos;
                _turnGlide = TurnGlideTime;
            }
        }
        if (_turnGlide <= 0)
            return;
        _turnGlide -= dt;
        _camera.Target = _turnGlide <= 0 ? _turnFocus : _camera.Target + (_turnFocus - _camera.Target) * Math.Min(1f, dt * 8f);
    }

    /// <summary>The unit the player starts their turn with: the avatar, the one already selected, or the first that can act.</summary>
    private CombatUnit? TurnUnit()
    {
        var enc = _sim.Combat;
        bool Ready(CombatUnit? u) => u != null && u.Side == Side.Village && !u.Acted && !enc.AiControls(u) && Mine(u.Entity)
            && _sim.World.IsAlive(u.Entity) && _sim.World.Get<Creature>(u.Entity).Able;
        if (HasAvatar && enc.Unit(_avatar) is var a && Ready(a))
            return a;
        if (_sim.World.IsAlive(_selected) && enc.Unit(_selected) is var s && Ready(s))
            return s;
        return enc.Units.Find(Ready);
    }

    private void HandleCombatKeys(Input input)
    {
        var enc = _sim.Combat;
        if (input.WasKeyPressed(GlfwKey.Enter))
            EndPlayerTurn();
        if (input.WasKeyPressed(GlfwKey.Tab))
        {
            // Next unit still waiting for orders.
            var ready = enc.Units.Where(u => u.Side == Side.Village && !u.Acted && !enc.AiControls(u) && Mine(u.Entity) && _sim.World.Get<Creature>(u.Entity).Able).ToList();
            if (ready.Count > 0)
            {
                var i = ready.FindIndex(u => u.Entity == _selected);
                _selected = ready[(i + 1) % ready.Count].Entity;
                _camera.Target = _sim.World.Get<Position>(_selected).Pos;
                _combatAttack = null;
                _breathMode = false;
            }
        }
        if (input.WasKeyPressed(GlfwKey.Escape))
        {
            _combatAttack = null;
            _breathMode = false;
            _dragBody = Entity.Null;
        }
    }

    private void EndPlayerTurn()
    {
        var enc = _sim.Combat;
        if (enc.Phase != Side.Village || enc.VillageAuto)
            return;
        // Ends the turn for this player's units; the phase passes once every player's are done.
        Issue(new CombatEndTurnCommand(CombatPlayer));
    }

    /// <summary>Where a click would send the selected unit: the hovered ground, or two levels above it with Ctrl (flyers hover).</summary>
    private int3 CombatTargetCell()
    {
        if (!_buildUp)
            return _hoverCell;
        return _hoverCell with { Z = Math.Min(_hoverCell.Z + 2, _sim.Map.Depth - 1) };
    }

    private void HandleCombatMouse(Input input)
    {
        if (input.WasMousePressed(MouseButton.Right))
        {
            if (_breathMode || _combatAttack != null || !_dragBody.IsNull)
            {
                _breathMode = false;
                _combatAttack = null;
                _dragBody = Entity.Null;
            }
            else
            {
                _selected = Entity.Null;
            }
            return;
        }
        if (!input.WasMousePressed(MouseButton.Left) || !_sim.Map.InBounds(_hoverCell))
            return;

        var enc = _sim.Combat;
        var w = _sim.World;
        var clicked = PickCreature(input.MousePosition);
        if (!clicked.IsNull && enc.Unit(clicked) is { Side: Side.Village })
        {
            _selected = clicked;
            _combatAttack = null;
            _breathMode = false;
            return;
        }
        if (CommandableUnit() is not { } unit)
        {
            if (!clicked.IsNull)
                _selected = clicked;
            return;
        }

        if (!_dragBody.IsNull)
        {
            // Dragging: the click picks the cell beside the body to drop it in (the body itself may be under the cursor).
            Issue(new CombatDragCommand(unit.Entity, _dragBody, _hoverCell));
            _dragBody = Entity.Null;
            return;
        }

        if (!clicked.IsNull && Factions.Hostile(Faction.Village, w.Get<Creature>(clicked).Faction) && !_breathMode)
        {
            if (AttackFor(unit.Entity, clicked) is not { } attack)
            {
                Notice($"{w.Get<Creature>(unit.Entity).Name} has nothing to attack with.");
                return;
            }
            Issue(new CombatAttackCommand(unit.Entity, clicked, CombatRules.Attacks(_sim, unit.Entity).IndexOf(attack), attack.Ranged ? _combatMode : ShotMode.Normal));
            _combatAttack = null;
            return;
        }

        if (_breathMode)
        {
            var target = clicked.IsNull ? _hoverCell : w.Get<Position>(clicked).Cell;
            Issue(new CombatBreatheCommand(unit.Entity, target));
            _breathMode = false;
            return;
        }

        Issue(new CombatMoveCommand(unit.Entity, CombatTargetCell()));
    }

    /// <summary>The chosen attack, else the one with the best odds of hurting the target from here.</summary>
    private AttackDef? AttackFor(Entity attacker, Entity target)
    {
        var attacks = CombatRules.Attacks(_sim, attacker);
        if (_combatAttack != null && attacks.Contains(_combatAttack))
            return _combatAttack;
        AttackDef? best = null;
        var bestScore = -1f;
        foreach (var a in attacks)
        {
            var odds = CombatRules.Chance(_sim, attacker, target, a, a.Ranged ? _combatMode : ShotMode.Normal);
            var score = odds.Possible ? odds.Chance * odds.DamageChance * (a.DamageMin + a.DamageMax) : -0.5f + attacks.IndexOf(a) * -0.01f;
            if (score > bestScore)
            {
                best = a;
                bestScore = score;
            }
        }
        return best;
    }

    /// <summary>A creature whose on-screen body is under the cursor.</summary>
    private Entity PickCreature(float2 mouse)
    {
        var w = _sim.World;
        var best = Entity.Null;
        var bestDist = 0.6f * _camera.Zoom;
        foreach (var e in w.Query<Creature>())
        {
            var pos = w.Get<Position>(e);
            if (pos.Cell.Z > _renderer.CutawayZ)
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

    // ---------------- World overlays ----------------

    private void DrawCombatPreview()
    {
        var enc = _sim.Combat;
        var w = _sim.World;
        var map = _sim.Map;
        var ring = Host.Atlas.Get("ui/selection");
        var white = Host.Atlas.WhiteSprite;

        // Rings under every combatant: green for ours, red for theirs; dim once they've acted.
        foreach (var u in enc.Units)
        {
            if (!w.IsAlive(u.Entity))
                continue;
            var p = w.Get<Position>(u.Entity);
            var size = w.Get<Creature>(u.Entity).Stage(_sim.Clock.Tick).Size;
            var alpha = u.Acted || u.Side != enc.Phase ? 0.45f : 0.95f;
            var color = u.Side == Side.Village ? Rgba.Pack(0.4f, 1f, 0.5f, alpha) : Rgba.Pack(1f, 0.35f, 0.3f, alpha);
            _renderer.Decal(ring, p.Pos + new float3(0, 0, 0.04f), Math.Max(1f, size * 0.9f), color, Quad.Unlit | Quad.Always, translucent: true);
            if (u.Side != enc.Phase && Reactions.CanReact(_sim, u))
            {
                // TU left to react with: an amber ring, and the ground it watches (a shooter's sights, a fighter's reach).
                _renderer.Decal(ring, p.Pos + new float3(0, 0, 0.05f), Math.Max(1f, size * 0.9f) * 1.35f, Rgba.Pack(1f, 0.8f, 0.2f, 0.9f), Quad.Unlit | Quad.Always, translucent: true);
                foreach (var c in u.OverwatchUsed ? [] : WatchedCells(u))
                    _renderer.Decal(white, Position.CenterOf(c) + new float3(0, 0, 0.025f), 0.92f, Rgba.Pack(1f, 0.75f, 0.2f, 0.1f), Quad.Level(c.Z) | Quad.Unlit, translucent: true);
            }
        }
        // Only a hovered path keeps its threats (cached while the cell and range hold).
        var lastThreats = _threats;
        _threats = [];

        if (CommandableUnit() is not { } unit)
            return;
        var key = (unit.Entity, unit.TimeUnits, enc.Round, enc.Phase, enc.Log.Count, enc.Busy(_sim), unit.ReserveForReactions);
        if (_range == null || key != _rangeKey)
        {
            _range = enc.Busy(_sim) ? null : enc.RangeOf(_sim, unit.Entity);
            _rangeKey = key;
        }

        if (!_dragBody.IsNull)
        {
            if (!w.IsAlive(_dragBody) || !enc.BodiesInReach(_sim, unit.Entity).Contains(_dragBody))
            {
                _dragBody = Entity.Null;
                return;
            }
            // Where the body can go: free cells beside it; the hovered one brighter.
            _renderer.Decal(ring, w.Get<Position>(_dragBody).Pos + new float3(0, 0, 0.06f), 1.2f, Rgba.Pack(1f, 0.85f, 0.3f, 0.9f), Quad.Unlit | Quad.Always, translucent: true);
            foreach (var c in enc.DragTargets(_sim, unit.Entity, _dragBody))
                _renderer.Decal(white, Position.CenterOf(c) + new float3(0, 0, 0.04f), 0.92f,
                    c == _hoverCell ? Rgba.Pack(1f, 0.85f, 0.3f, 0.5f) : Rgba.Pack(1f, 0.85f, 0.3f, 0.22f), Quad.Level(c.Z) | Quad.Unlit, translucent: true);
            return;
        }

        if (_breathMode)
        {
            var stage = w.Get<Creature>(unit.Entity).Stage(_sim.Clock.Tick);
            var from = w.Get<Position>(unit.Entity).Cell;
            var inRange = CombatRules.Distance(from, _hoverCell) <= Sim.Environment.Breath.RangeOf(_sim, unit.Entity);
            foreach (var c in _sim.SpiralFrom(_hoverCell, 2))
                if (CombatRules.Distance(c, _hoverCell) <= 1.5f)
                    _renderer.Decal(white, Position.CenterOf(c) + new float3(0, 0, 0.05f), 1f,
                        inRange ? Rgba.Pack(1f, 0.45f, 0.1f, 0.35f) : Rgba.Pack(0.5f, 0.5f, 0.5f, 0.3f), Quad.Unlit | Quad.Always, translucent: true);
            return;
        }

        if (_range == null)
            return;

        // Reachable ground (blue where there'd be TU left to attack, amber where not), and columns
        // only a flyer can reach (marked on the ground beneath).
        var attackCost = TimeUnits.CheapestAttack(_sim, unit.Entity) ?? 0f;
        var groundColumns = new HashSet<int>();
        var airColumns = new HashSet<int>();
        foreach (var i in _range.Cells)
        {
            var c = map.CellOf(i);
            if (TacticalMoves.Airborne(_sim, c))
                airColumns.Add(c.Y * map.Width + c.X);
            else
            {
                groundColumns.Add(c.Y * map.Width + c.X);
                var canAct = unit.TimeUnits - _range.CostTo(map, c) + 1e-3f >= attackCost;
                _renderer.Decal(white, Position.CenterOf(c) + new float3(0, 0, 0.03f), 0.92f,
                    canAct ? Rgba.Pack(0.35f, 0.65f, 1f, 0.22f) : Rgba.Pack(1f, 0.75f, 0.25f, 0.2f), Quad.Level(c.Z) | Quad.Unlit, translucent: true);
            }
        }
        foreach (var col in airColumns)
        {
            if (groundColumns.Contains(col))
                continue;
            var x = col % map.Width;
            var y = col / map.Width;
            var visible = _renderer.VisibleCell(map, x, y);
            _renderer.Decal(white, new float3(x + 0.5f, y + 0.5f, _renderer.SurfaceHeight(map, visible) + 0.03f), 0.6f, Rgba.Pack(0.6f, 0.95f, 1f, 0.2f), Quad.Unlit, translucent: true);
        }

        // The path to the hovered cell.
        var target = CombatTargetCell();
        if (_range.PathTo(map, target) is { } path)
        {
            // Reactions the move would draw: the path turns red from the first one, with a ring
            // where each fires and on whoever fires it.
            if (_threatKey != (target, _rangeKey))
            {
                lastThreats = Reactions.Threats(_sim, enc, unit.Entity, _range.From, path);
                _threatKey = (target, _rangeKey);
            }
            _threats = lastThreats;
            var firstThreat = _threats.Count > 0 ? _threats[0].Step : int.MaxValue;
            for (var i = 0; i < path.Count; i++)
                _renderer.Decal(white, Position.CenterOf(path[i]) + new float3(0, 0, 0.06f), 0.22f,
                    i >= firstThreat ? Rgba.Pack(1f, 0.3f, 0.25f, 0.9f) : Rgba.Pack(1f, 1f, 1f, 0.8f), Quad.Unlit | Quad.Always, translucent: true);
            foreach (var t in _threats)
            {
                _renderer.Decal(ring, Position.CenterOf(t.Cell) + new float3(0, 0, 0.065f), 0.7f, Rgba.Pack(1f, 0.3f, 0.25f, 0.9f), Quad.Unlit | Quad.Always, translucent: true);
                if (w.IsAlive(t.Reactor))
                    _renderer.Decal(ring, w.Get<Position>(t.Reactor).Pos + new float3(0, 0, 0.06f), 1.5f, Rgba.Pack(1f, 0.3f, 0.25f, 0.95f), Quad.Unlit | Quad.Always, translucent: true);
            }
            _renderer.Decal(ring, Position.CenterOf(target) + new float3(0, 0, 0.07f), 1f, Rgba.Pack(1f, 1f, 1f, 0.9f), Quad.Unlit | Quad.Always, translucent: true);
        }
    }

    /// <summary>
    /// Ground a unit watching (<see cref="Reactions.Watching"/>) covers, cached for the phase: what
    /// it can shoot at (in range of its bow, line of fire), and the cells in reach of its blade.
    /// </summary>
    private List<int3> WatchedCells(CombatUnit u)
    {
        var enc = _sim.Combat;
        if (_watched.TryGetValue(u.Entity, out var cached) && cached.Round == enc.Round && cached.Phase == enc.Phase)
            return cached.Cells;
        var cells = new List<int3>();
        var w = _sim.World;
        var at = w.Get<Position>(u.Entity).Cell;
        foreach (var attack in Reactions.Watching(_sim, u))
        {
            if (!attack.Ranged)
            {
                // A swing at whoever steps in beside it.
                foreach (var c in _sim.SpiralFrom(at, 1))
                    if (c != at && CombatRules.InReach(_sim, at, c) && !cells.Contains(c))
                        cells.Add(c);
                continue;
            }
            var size = w.Get<Creature>(u.Entity).Stage(_sim.Clock.Tick).Size;
            var eye = Position.CenterOf(at) + new float3(0, 0, 0.7f * size + (w.Has<Riding>(u.Entity) ? 1f : 0f));
            var range = attack.Range;
            foreach (var c in _sim.SpiralFrom(at, (int)MathF.Ceiling(range)))
                if (c != at && CombatRules.Distance(at, c) <= range + 0.01f && CombatRules.LineOfFire(_sim, eye, Position.CenterOf(c) + new float3(0, 0, 0.5f)))
                    cells.Add(c);
        }
        _watched[u.Entity] = (enc.Round, enc.Phase, cells);
        return cells;
    }

    // ---------------- HUD ----------------

    private void DrawCombatHud()
    {
        var enc = _sim.Combat;
        var w = _sim.World;
        // Left side: the clock and toasts own the top-right corner.
        SetNextWindow(new float2(10, 130), new float2(370, 0));
        // New ID ("###battle") so layouts saved before the clock took the top-right corner don't pin it there.
        ImGui.Begin("Combat###battle"u8, ImGuiWindowFlags.AlwaysAutoResize);

        var yourTurn = enc.Phase == Side.Village && !enc.VillageAuto;
        TextC(yourTurn ? Good : Bad, $"Round {enc.Round}: {(enc.Phase == Side.Village ? "your turn" : "the raiders' turn")}");
        if (enc.Busy(_sim))
        {
            ImGui.SameLine(0, -1);
            TextC(Dim, "(moving)");
        }
        var auto = enc.VillageAuto;
        ImGui.BeginDisabled(IsCoopClient); // a host-only switch in co-op
        if (ImGui.Checkbox("Auto (the AI plays the village)"u8, ref auto))
            Issue(new CombatAutoCommand(auto));
        ImGui.EndDisabled();
        // Co-op: the phase is shared, so a player who's done waits for the others.
        var waiting = yourTurn && _coop != null && !enc.Units.Exists(u => u.Side == Side.Village && !u.Acted && !enc.AiControls(u) && Mine(u.Entity));
        if (waiting)
            TextC(Dim, "Waiting for the other players to end their turns.");
        else if (yourTurn && ImGui.Button("End turn (Enter)"u8, in _autoSize))
            EndPlayerTurn();

        ImGui.Separator();
        foreach (var u in enc.Units)
        {
            if (!w.IsAlive(u.Entity))
                continue;
            var c = w.Get<Creature>(u.Entity);
            var status = c.Dead ? "dead" : c.Downed ? "down" : u.Fleeing ? "fleeing" : u.Side == enc.Phase && u.Acted ? "done" : $"{u.TimeUnits:F0} TU";
            var color = u.Side == Side.Village ? Good : Bad;
            ImGui.PushStyleColor(ImGuiCol.Text, in color);
            ImGui.PushID(u.Entity.Index);
            if (ImGui.Selectable($"{(u.Entity == _selected ? "> " : "  ")}{c.Name,-10} {status,-7}", u.Entity == _selected, ImGuiSelectableFlags.None, in _autoSize))
            {
                _selected = u.Entity;
                _camera.Target = w.Get<Position>(u.Entity).Pos;
            }
            ImGui.PopID();
            ImGui.PopStyleColor(1);
            if (w.TryGet<Health>(u.Entity, out var h))
            {
                ImGui.SameLine(0, -1);
                TextC(h.Condition < 0.5f ? Bad : h.Condition < 0.8f ? Warn : Dim, $"health {h.Condition * 100:F0}%  blood {h.Blood * 100:F0}%");
            }
        }

        if (CommandableUnit() is { } unit)
        {
            ImGui.Separator();
            var c = w.Get<Creature>(unit.Entity);
            var max = TimeUnits.PerTurn(_sim, unit.Entity);
            ImGui.Text($"{c.Name}: {unit.TimeUnits:F0} / {max:F0} TU");
            var frac = max > 0 ? unit.TimeUnits / max : 0f;
            var barSize = (float2?)new float2(250, 8);
            ImGui.ProgressBar(frac, in barSize, ""u8);
            var target = CombatTargetCell();
            if (_range != null && _range.CanEndAt(_sim.Map, target) && target != _range.From)
                TextC(Dim, $"Move there: {_range.CostTo(_sim.Map, target):F0} TU ({unit.TimeUnits - _range.CostTo(_sim.Map, target):F0} left).");
            else
                TextC(Dim, CanFly(unit.Entity) ? "Click a cell to move (Ctrl+click to hover)." : "Click a blue cell to move.");
            var bsize = (float2?)new float2(300, 0);
            foreach (var a in CombatRules.Attacks(_sim, unit.Entity))
            {
                // Melee: one button. Ranged: a snap and an aimed shot.
                foreach (var mode in a.Ranged ? new[] { ShotMode.Snap, ShotMode.Aimed } : new[] { ShotMode.Normal })
                {
                    var cost = TimeUnits.AttackCost(_sim, unit.Entity, a, mode);
                    var active = _combatAttack == a && (!a.Ranged || _combatMode == mode);
                    if (active)
                    {
                        var hl = new float4(0.25f, 0.5f, 0.85f, 1f);
                        ImGui.PushStyleColor(ImGuiCol.Button, in hl);
                    }
                    var what = mode switch { ShotMode.Snap => "snap", ShotMode.Aimed => "aimed", _ => $"{a.DamageMin:F0}-{a.DamageMax:F0} {DamageTypes.Wound(a.DamageType)}" };
                    ImGui.BeginDisabled(unit.TimeUnits + 1e-3f < cost);
                    if (ImGui.Button($"{a.Label} ({what}, {cost:F0} TU)###atk{a.Name}{mode}", in bsize))
                    {
                        _combatAttack = active ? null : a;
                        if (a.Ranged)
                            _combatMode = mode;
                        _breathMode = false;
                    }
                    ImGui.EndDisabled();
                    if (active)
                        ImGui.PopStyleColor(1);
                    if (mode != ShotMode.Normal && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                        Tip(mode == ShotMode.Snap
                            ? $"A quick shot: {TimeUnits.SnapAccuracy * 100:F0}% of the usual accuracy. Reactions are snap shots too."
                            : $"A careful shot: {TimeUnits.AimedAccuracy * 100:F0}% of the usual accuracy, for nearly twice the TU.");
                }
            }
            if (c.Stage(_sim.Clock.Tick).BreathRange > 0 && CombatRules.Attacks(_sim, unit.Entity).Count > 0)
            {
                var cost = TimeUnits.BreathCost(_sim, unit.Entity);
                ImGui.BeginDisabled(unit.TimeUnits + 1e-3f < cost);
                var label = $"Breathe {Sim.Systems.Dragons.BreathOf(_sim, unit.Entity).Noun}";
                if (ImGui.Button(_breathMode ? $"{label}: pick a target###breath" : $"{label} ({cost:F0} TU)###breath", in bsize))
                    _breathMode = !_breathMode;
                ImGui.EndDisabled();
            }
            // What's in hand can be let go of (to pick up something better), and a weapon in the
            // hands or the pack drawn in place of the one wielded.
            if (w.TryGet<Carrying>(unit.Entity, out var hands) && !hands.Item.IsNull && w.IsAlive(hands.Item))
            {
                var cost = TimeUnits.Share(_sim, unit.Entity, TimeUnits.Drop);
                ImGui.BeginDisabled(unit.TimeUnits + 1e-3f < cost);
                if (ImGui.Button($"Drop {ItemLine(hands.Item)} ({cost:F0} TU)###drop", in bsize))
                    Issue(new CombatDropCommand(unit.Entity));
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    Tip("Let go of what's in your hands: it falls at your feet, and your hands are free.");
            }
            foreach (var weapon in enc.WeaponsToHand(_sim, unit.Entity))
            {
                var cost = TimeUnits.Share(_sim, unit.Entity, TimeUnits.Wield);
                ImGui.BeginDisabled(unit.TimeUnits + 1e-3f < cost);
                if (ImGui.Button($"Wield {w.Get<Thing>(weapon).Label} ({cost:F0} TU)###wield{weapon.Index}", in bsize))
                    Issue(new CombatWieldCommand(unit.Entity, weapon));
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    Tip(w.TryGet<Equipment>(unit.Entity, out var eq) && w.IsAlive(eq.Weapon)
                        ? $"Put away the {w.Get<Thing>(eq.Weapon).Label} and fight with this instead."
                        : "Fight with this instead of your bare hands.");
            }
            // Things lying in reach (the jailer's key and club, a dropped weapon).
            foreach (var item in enc.ItemsInReach(_sim, unit.Entity))
            {
                var cost = TimeUnits.Share(_sim, unit.Entity, TimeUnits.PickUp);
                ImGui.BeginDisabled(unit.TimeUnits + 1e-3f < cost);
                if (ImGui.Button($"Pick up {w.Get<Thing>(item).Label} ({cost:F0} TU)###pick{item.Index}", in bsize))
                    Issue(new CombatPickUpCommand(unit.Entity, item));
                ImGui.EndDisabled();
            }
            // Containers beside you (the captors' chest with your gear): take things out, a stack at a time.
            foreach (var container in enc.ContainersInReach(_sim, unit.Entity))
            {
                var contents = _sim.StoredIn(container).ToList();
                if (contents.Count == 0)
                    continue;
                var cost = TimeUnits.Share(_sim, unit.Entity, TimeUnits.PickUp);
                TextC(Dim, $"In the {w.Get<Thing>(container).Label}:");
                foreach (var item in contents)
                {
                    ImGui.BeginDisabled(unit.TimeUnits + 1e-3f < cost);
                    if (ImGui.Button($"Take {ItemLine(item)} ({cost:F0} TU)###take{item.Index}", in bsize))
                        Issue(new CombatPickUpCommand(unit.Entity, item));
                    ImGui.EndDisabled();
                }
                ImGui.BeginDisabled(unit.TimeUnits + 1e-3f < cost);
                if (ImGui.Button($"Take everything (while TU last)###takeall{container.Index}", in bsize))
                    Issue(new CombatTakeAllCommand(unit.Entity, container));
                ImGui.EndDisabled();
            }
            // Bodies in the way (a downed jailer in the cage's doorway): drag one into a free cell beside it.
            foreach (var body in enc.BodiesInReach(_sim, unit.Entity))
            {
                var cost = TimeUnits.Share(_sim, unit.Entity, TimeUnits.Drag);
                var dragging = _dragBody == body;
                ImGui.BeginDisabled(unit.TimeUnits + 1e-3f < cost || enc.DragTargets(_sim, unit.Entity, body).Count == 0);
                if (ImGui.Button(dragging ? $"Drag {w.Get<Creature>(body).Name}: pick a cell###drag{body.Index}" : $"Drag {w.Get<Creature>(body).Name} ({cost:F0} TU)###drag{body.Index}", in bsize))
                {
                    _dragBody = dragging ? Entity.Null : body;
                    _breathMode = false;
                    _combatAttack = null;
                }
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    Tip("Pull the body into a free cell beside it (click one of the highlighted cells): bodies block the cell they lie in.");
            }
            // Bandages: bind your own wounds, or a friend's beside you (your dragon), all at once for TU.
            if (Bandaging.BandagesOn(_sim, unit.Entity) > 0)
                foreach (var patient in enc.Units.Where(o => o.Side == Side.Village).Select(o => o.Entity).Prepend(unit.Entity).Distinct())
                {
                    if (Bandaging.WoundsToBind(_sim, patient) == 0 || (patient != unit.Entity && Bandaging.CantBind(_sim, unit.Entity, patient) != null))
                        continue;
                    var cost = TimeUnits.Share(_sim, unit.Entity, TimeUnits.Bandage);
                    var who = patient == unit.Entity ? "yourself" : w.Get<Creature>(patient).Name;
                    var wounds = Math.Min(Bandaging.WoundsToBind(_sim, patient), Bandaging.BandagesOn(_sim, unit.Entity));
                    ImGui.BeginDisabled(unit.TimeUnits + 1e-3f < cost);
                    if (ImGui.Button($"Bandage {who} ({wounds} wounds, {cost:F0} TU)###bandage{patient.Index}", in bsize))
                        Issue(new CombatBandageCommand(unit.Entity, patient));
                    ImGui.EndDisabled();
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                        Tip($"One bandage a wound ({Bandaging.BandagesOn(_sim, unit.Entity)} left): tended wounds stop bleeding and heal faster.");
                }
            // Escape: beside your chained dragon with the jailer's key, freeing it costs TU.
            if (Sim.Scenarios.EscapeCamp.ChainedDragonInReach(_sim, unit.Entity) is { IsNull: false } chained)
            {
                var cost = TimeUnits.Share(_sim, unit.Entity, TimeUnits.Interact);
                var dragonName = w.Get<Creature>(chained).Name;
                ImGui.BeginDisabled(unit.TimeUnits + 1e-3f < cost);
                if (ImGui.Button($"Free {dragonName} ({cost:F0} TU)", in bsize))
                    Issue(new CombatUnchainCommand(unit.Entity, chained));
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    Tip("Strike off the chains. Then, beside it, you can fly away.");
            }
            var reserve = unit.ReserveForReactions;
            if (ImGui.Checkbox("Reserve TU for reactions"u8, ref reserve))
                Issue(new CombatReserveCommand(unit.Entity, reserve));
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip($"Moves won't spend the last {TimeUnits.CheapestAttack(_sim, unit.Entity) ?? 0:F0} TU: enough to react (a snap shot, or a swing) "
                    + $"when an enemy moves into range or reach in their turn (overwatch, once a turn), or out of reach. Reactions hit at {CombatRules.ReactionFactor * 100:F0}% of the usual odds.");
            if (ImGui.Button("End this unit's turn"u8, in _autoSize))
                Issue(new CombatWaitCommand(unit.Entity));
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip("Its TU left are kept for reactions in the enemy's turn.");
            if (_threats.Count > 0)
            {
                TextC(Bad, "Moving there draws:");
                foreach (var t in _threats)
                    Bullet($"{w.Get<Creature>(t.Reactor).Name}: {t.Kind switch { ReactionKind.Overwatch => "overwatch", ReactionKind.Guard => "guarding its keeper", _ => "a swing as you pass" }} at {w.Get<Creature>(t.Target).Name} ({t.Chance * 100:F0}%)");
            }
        }

        if (_combatNotice != null && _time < _noticeUntil)
        {
            ImGui.Separator();
            Wrapped(_combatNotice);
        }

        if (ImGui.CollapsingHeader("Log"u8, ImGuiTreeNodeFlags.DefaultOpen))
            foreach (var entry in enc.Log.Skip(Math.Max(0, enc.Log.Count - 12)))
                TextC(entry.Side == Side.Village ? Dim : Warn, $"[{entry.Round}] {entry.Text}");
        ImGui.End();
    }

    private bool CanFly(Entity e) => Sim.Ai.Navigation.FlyingStage(_sim, e) != null;

    /// <summary>Over an enemy: the odds for the selected unit's attack, broken down. Over anyone: their condition.</summary>
    private bool DrawCombatTooltip()
    {
        var hovered = PickCreature(Host.Input.MousePosition);
        if (hovered.IsNull || Host.Input.MouseCaptured)
            return false;
        var w = _sim.World;
        var target = w.Get<Creature>(hovered);
        var lines = new List<string> { target.Name };
        if (w.TryGet<Health>(hovered, out var th))
            lines.Add($"health {th.Condition * 100:F0}%, blood {th.Blood * 100:F0}%, pain {th.Pain * 100:F0}%{(target.Downed ? ", DOWN" : "")}");

        if (CommandableUnit() is { } unit && Factions.Hostile(Faction.Village, target.Faction) && AttackFor(unit.Entity, hovered) is { } attack)
        {
            var mode = attack.Ranged ? _combatMode : ShotMode.Normal;
            var odds = CombatRules.Chance(_sim, unit.Entity, hovered, attack, mode);
            lines.Add("");
            if (!odds.Possible)
            {
                lines.Add($"{attack.Label}: can't ({odds.Blocked})");
            }
            else
            {
                var parts = new List<string> { $"base {odds.Base * 100:F0}%" };
                if (odds.Range > 0) parts.Add($"range -{odds.Range * 100:F0}%");
                if (odds.Cover > 0) parts.Add($"cover -{odds.Cover * 100:F0}%");
                if (odds.Height != 0) parts.Add($"height {odds.Height * 100:+0;-0}%");
                if (odds.Size > 0) parts.Add($"big target +{odds.Size * 100:F0}%");
                if (odds.Helpless > 0) parts.Add($"helpless +{odds.Helpless * 100:F0}%");
                if (MathF.Abs(odds.Skill) > 0.001f) parts.Add($"skill {odds.Skill * 100:+0;-0}%");
                if (odds.Wounds < 0.99f) parts.Add($"wounds x{odds.Wounds:F2}");
                if (MathF.Abs(odds.Aim - 1f) > 0.001f) parts.Add($"{(mode == ShotMode.Aimed ? "aimed" : "snap")} x{odds.Aim:F2}");
                var cost = TimeUnits.AttackCost(_sim, unit.Entity, attack, mode);
                lines.Add($"{attack.Label}{(attack.Ranged ? $" ({(mode == ShotMode.Aimed ? "aimed" : "snap")})" : "")}: {odds.Chance * 100:F0}% to hit ({string.Join(", ", parts)}), {cost:F0} TU"
                    + (unit.TimeUnits + 1e-3f < cost ? " (not enough TU)" : ""));
                lines.Add($"then {odds.DamageChance * 100:F0}% to hurt: {attack.DamageMin:F0}-{attack.DamageMax:F0} {DamageTypes.Wound(attack.DamageType)}");
                // Who'd answer the attack with TU they kept (a swing or a snap shot back).
                foreach (var t in Reactions.Answers(_sim, _sim.Combat, unit.Entity, w.Get<Position>(unit.Entity).Cell, hovered))
                    lines.Add($"answered by {w.Get<Creature>(t.Reactor).Name}: {t.Attack.Label}, {t.Chance * 100:F0}% to hit");
                if (th != null)
                {
                    var total = th.Body.Parts.Sum(p => p.Coverage);
                    var likely = th.Body.Parts.OrderByDescending(p => p.Coverage).Take(4).Select(p => $"{p.Label} {p.Coverage / total * 100:F0}%");
                    lines.Add($"likely: {string.Join(", ", likely)}");
                }
            }
        }
        Tip(string.Join("\n", lines));
        return true;
    }
}
