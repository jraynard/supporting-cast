using DragonWorld.Sim.Ai;
using DragonWorld.Sim.Components;
using DragonWorld.Sim.Defs;
using DragonWorld.Sim.Systems;

namespace DragonWorld.Sim.Combat;

public enum Side
{
    Village,
    Raiders,
}

/// <summary>A creature taking part in an encounter, and what it has done this turn.</summary>
public sealed class CombatUnit
{
    public required Entity Entity;
    public required Side Side;

    /// <summary>
    /// Time units left (<see cref="Combat.TimeUnits"/>): spent on moves and actions in its own turn;
    /// what's left when the turn ends is there for reactions in the enemy's.
    /// </summary>
    public float TimeUnits;

    /// <summary>Has moved this turn (the AI repositions once a turn).</summary>
    public bool Moved;

    /// <summary>Ended its turn (the player's "done", or the AI's): no more orders until its side's next.</summary>
    public bool Acted;

    /// <summary>The player keeps enough TU back for a reaction: moves won't eat into it (<see cref="Encounter.Reserve"/>).</summary>
    public bool ReserveForReactions;

    /// <summary>
    /// Has taken its overwatch shot in the enemy's current phase (<see cref="ReactionKind.Overwatch"/>):
    /// one a phase, however many TU it kept.
    /// </summary>
    public bool OverwatchUsed;

    /// <summary>Has answered an attack on itself in the enemy's current phase (<see cref="ReactionKind.Response"/>): one a phase.</summary>
    public bool ResponseUsed;

    /// <summary>AI steps taken this turn (a backstop against a unit that never decides it's done).</summary>
    internal int AiSteps;

    /// <summary>Dragons roll obedience once per turn, on the first order.</summary>
    public bool ObedienceRolled;

    /// <summary>A dragon that refused its order acts on instinct (the AI) for the rest of its turn.</summary>
    public bool Instinct;

    /// <summary>Broke and is running for the map edge.</summary>
    public bool Fleeing;

    /// <summary>
    /// Adventure mode: a village unit the player doesn't control (anyone but their avatar, its mount,
    /// and a dragon that trusts them enough). The AI fights its turn.
    /// </summary>
    public bool Companion;
}

public sealed record CombatLogEntry(int Round, Side Side, string Text);

/// <summary>
/// Turn-based combat. It starts when hostile creatures come within sight of the village; from
/// then on the sides take turns. In its phase each of a side's units spends its time units
/// (<see cref="Combat.TimeUnits"/>) on moves and actions (attacks in snap or aimed mode, breath,
/// picking things up, story actions) in any order until it ends its turn; TU it keeps are spent on
/// reactions in the enemy's phase (<see cref="Reactions"/>). The world clock is frozen during phases (the game animates moves with <see cref="Animate"/>); when both sides
/// have gone, the world advances <see cref="RoundTicks"/> so fires spread and wounds bleed, with
/// the combatants' own AI held off. The village's phase is the player's unless
/// <see cref="VillageAuto"/>; dragons may refuse orders and act on instinct instead.
/// </summary>
public sealed class Encounter
{
    /// <summary>World time that passes between rounds (5 game seconds).</summary>
    public const int RoundTicks = 300;

    /// <summary>Hostiles this close (with a clear line of sight) start combat.</summary>
    public const int DetectRadius = 20;

    /// <summary>Creatures this close to a hostile when combat starts (or at a new round) join in.</summary>
    public const int JoinRadius = 30;

    public bool Active { get; private set; }
    public int Round { get; private set; }
    public Side Phase { get; private set; }

    /// <summary>The village side plays itself (tests, the HUD's Auto button).</summary>
    public bool VillageAuto { get; set; }

    /// <summary>How the last encounter ended, for the HUD and tests (<see cref="WonOutcome"/> or <see cref="LostOutcome"/>).</summary>
    public string? Outcome { get; private set; }

    public const string WonOutcome = "The raiders are beaten.";
    public const string LostOutcome = "The village can no longer fight. The raiders take what they want and leave.";

    /// <summary>The last fight was lost (the village went down), as far as anyone remembers.</summary>
    public bool LastFightLost => Outcome == LostOutcome;

    /// <summary>Encounters fought so far.</summary>
    public int Count { get; private set; }

    public List<CombatUnit> Units { get; } = [];
    public List<CombatLogEntry> Log { get; } = [];

    /// <summary>The unit whose move is being animated, or null.</summary>
    public Entity Animating { get; private set; } = Entity.Null;

    /// <summary>Reactions fired so far (the game pauses a moment on each so the shot reads).</summary>
    public int ReactionCount { get; private set; }

    /// <summary>The last reaction: who reacted, at whom, how. Render-only.</summary>
    public (Entity Reactor, Entity Target, ReactionKind Kind) LastReaction { get; private set; }

    private readonly HashSet<Entity> _members = [];

    /// <summary>Units that have reacted to the enemy action playing out now (a move, an attack): each reacts at most once to any one action.</summary>
    internal readonly HashSet<Entity> ReactedThisMove = [];

    /// <summary>
    /// Player orders that arrived while a move was playing out or a reaction was holding the
    /// picture (another co-op player's unit walking, say): <see cref="Advance"/> applies them in
    /// order once things settle, rather than refusing them as "still moving".
    /// </summary>
    internal readonly List<Core.ISimCommand> Queued = [];

    /// <summary>Most orders that may wait in <see cref="Queued"/>: past that, new ones are dropped.</summary>
    public const int MaxQueued = 16;

    /// <summary>
    /// What each player order came to (its refusal, or what happened), for the unit's player to
    /// read as a notice (the game keeps how many it has shown). Render-only: never read by the sim,
    /// not hashed; cleared when a fight starts.
    /// </summary>
    public List<(Entity Unit, string Text)> Feedback { get; } = [];

    // Pacing (Advance): fight time is counted in steps of 1/60 s, so every peer plays a fight alike.

    /// <summary>Movement ticks a move plays per step: moves run this many times faster than walking.</summary>
    public const int AnimateSteps = 2;

    /// <summary>Steps a move holds still after a reaction fires, so the shot reads (0.45 s).</summary>
    public const int ReactionHoldSteps = 27;

    /// <summary>Steps between AI moves, so players can follow the enemy's turn (0.4 s).</summary>
    public const int AiStepDelay = 24;

    private int _hold;
    private int _aiWait;
    private int _seenReactions;

    /// <summary>A move is playing out, or a reaction is holding the picture: orders wait (see <see cref="Queued"/>).</summary>
    public bool Settling(Simulation sim) => Busy(sim) || _hold > 0;

    public bool Involves(Entity e) => Active && _members.Contains(e);

    public CombatUnit? Unit(Entity e) => Units.Find(u => u.Entity == e);

    public bool Busy(Simulation sim) => !Animating.IsNull && sim.World.IsAlive(Animating) && sim.World.Get<Mover>(Animating).Moving;

    // ---------------- Starting and ending ----------------

    /// <summary>Start combat if a hostile is within <see cref="DetectRadius"/> of the village and can see it.</summary>
    public bool TryStart(Simulation sim)
    {
        if (!HostilesInSight(sim))
            return false;
        Start(sim);
        return true;
    }

    /// <summary>
    /// <see cref="CombatSystem"/> will start a fight on the next tick. Read-only, so the game can
    /// autosave just before the fight (saving during one isn't supported).
    /// </summary>
    public bool StartsNextTick(Simulation sim) =>
        !Active && (sim.Clock.Tick + 1) % CombatSystem.CheckInterval == 0 && HostilesInSight(sim);

    /// <summary>A raider is within <see cref="DetectRadius"/> of a village creature and can see it (no fight on yet).</summary>
    public bool HostilesInSight(Simulation sim)
    {
        if (Active)
            return false;
        var w = sim.World;
        var creatures = sim.Creatures().ToList();
        foreach (var r in creatures)
        {
            var rc = w.Get<Creature>(r);
            if (!Factions.Attacker(rc.Faction) || !rc.Able || w.Has<Leaving>(r))
                continue;
            var rp = w.Get<Position>(r).Pos;
            foreach (var c in creatures)
            {
                var cc = w.Get<Creature>(c);
                if (!Factions.Hostile(rc.Faction, cc.Faction) || !cc.Able)
                    continue;
                var cp = w.Get<Position>(c).Pos;
                var d = cp - rp;
                if (d.X * d.X + d.Y * d.Y + d.Z * d.Z > DetectRadius * DetectRadius)
                    continue;
                if (!CombatRules.LineOfFire(sim, rp + new float3(0, 0, 0.7f), cp + new float3(0, 0, 0.7f)))
                    continue;
                return true;
            }
        }
        return false;
    }

    /// <summary>Begin an encounter with everyone near the raiders. Exposed for tests and scripted fights.</summary>
    public void Start(Simulation sim)
    {
        Active = true;
        Count++;
        Round = 1;
        Outcome = null;
        Units.Clear();
        _members.Clear();
        Log.Clear();
        Queued.Clear();
        Feedback.Clear();
        _hold = 0;
        _aiWait = 0;
        _seenReactions = ReactionCount;
        Recruit(sim);
        var beast = Units.Select(u => sim.World.Get<Creature>(u.Entity)).FirstOrDefault(c => c.Faction == Faction.Predator);
        sim.Messages.Add(MessageKind.Danger, beast.Def is { } kind ? $"{Capital(kind.Animal?.Plural is { Length: > 0 } p ? p : kind.Label + "s")}! The fight is on." : "Raiders! The fight is on.",
            Units.Count > 0 ? sim.World.Get<Position>(Units[0].Entity).Cell : default);
        Write(sim, Side.Village, "Combat begins.");
        BeginPhase(sim, Side.Village);
    }

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>Pull in every able village creature and raider near a raider that isn't in yet.</summary>
    private void Recruit(Simulation sim)
    {
        var w = sim.World;
        var creatures = sim.Creatures().ToList();
        var raiders = creatures.Where(e => Factions.Attacker(w.Get<Creature>(e).Faction) && !w.Has<Leaving>(e)).ToList();
        foreach (var e in creatures)
        {
            var c = w.Get<Creature>(e);
            if (_members.Contains(e) || !c.Able || w.Has<Leaving>(e) || w.Has<Chained>(e) || c.Faction is not (Faction.Village or Faction.Raider or Faction.Predator))
                continue;
            var at = w.Get<Position>(e).Cell;
            if (!raiders.Exists(r => CombatRules.Distance(at, w.Get<Position>(r).Cell) <= JoinRadius))
                continue;
            // Drop whatever they were doing and stand ready (flyers stay up).
            AiSystem.EndJob(sim, e, w.Get<Mind>(e), false);
            w.Get<Mind>(e).Asleep = false;
            w.Get<Position>(e) = Position.At(at);
            _members.Add(e);
            var side = Factions.Attacker(c.Faction) ? Side.Raiders : Side.Village;
            // Everyone joins with a full pool of TU: the side that isn't moving can react from the start.
            Units.Add(new CombatUnit
            {
                Entity = e, Side = side, Acted = true, TimeUnits = Combat.TimeUnits.PerTurn(sim, e),
                Companion = side == Side.Village && !PlayerCommands(sim, e),
            });
        }
        Units.Sort((a, b) => a.Entity.Index.CompareTo(b.Entity.Index));
    }

    private void End(Simulation sim, bool villageWon)
    {
        Outcome = villageWon ? WonOutcome : LostOutcome;
        Write(sim, Phase, Outcome);
        sim.Messages.Add(villageWon ? MessageKind.Good : MessageKind.Danger, Outcome, default);
        // Win or lose, the raiders are done here: whoever can still walk heads off.
        foreach (var u in Units)
            if (u.Side == Side.Raiders && sim.World.IsAlive(u.Entity))
                sim.World.Add(u.Entity, new Leaving());
        // Survivors remember the fight, and each other in it.
        var survivors = Units.Where(u => u.Side == Side.Village && sim.World.IsAlive(u.Entity) && !sim.World.Get<Creature>(u.Entity).Dead)
            .Select(u => u.Entity).ToList();
        foreach (var e in survivors)
        {
            MoodSystem.Remember(sim, e, villageWon ? sim.Defs.Thoughts.WonFight : sim.Defs.Thoughts.LostFight);
            foreach (var ally in survivors)
                if (ally != e)
                    MoodSystem.Remember(sim, e, sim.Defs.Thoughts.FoughtBesideMe, ally);
        }
        Active = false;
        Animating = Entity.Null;
        _members.Clear();
    }

    /// <summary>Over when one side has nobody left who can fight.</summary>
    internal bool CheckEnd(Simulation sim)
    {
        if (!Active)
            return true;
        var w = sim.World;
        bool CanFight(CombatUnit u) => w.IsAlive(u.Entity) && w.Get<Creature>(u.Entity).Able && CombatRules.Attacks(sim, u.Entity).Count > 0;
        if (!Units.Exists(u => u.Side == Side.Raiders && CanFight(u)))
            End(sim, villageWon: true);
        else if (!Units.Exists(u => u.Side == Side.Village && CanFight(u)))
            End(sim, villageWon: false);
        return !Active;
    }

    // ---------------- Turns ----------------

    private void BeginPhase(Simulation sim, Side side)
    {
        Phase = side;
        var w = sim.World;
        foreach (var u in Units)
        {
            // A new phase: each unit gets its one overwatch shot and one answer again.
            u.OverwatchUsed = false;
            u.ResponseUsed = false;
            // The other side keeps whatever TU it saved: that's what it reacts with.
            if (u.Side != side)
                continue;
            var able = w.IsAlive(u.Entity) && w.Get<Creature>(u.Entity).Able;
            // Frost leaves fewer time units; a lightning stun takes a share of this turn's.
            u.TimeUnits = able ? AfflictionRules.TurnTimeUnits(sim, u.Entity, Combat.TimeUnits.PerTurn(sim, u.Entity)) : 0;
            u.Moved = false;
            u.Acted = !able;
            u.AiSteps = 0;
            u.ObedienceRolled = false;
            u.Instinct = false;
        }
    }

    /// <summary>The side whose phase it is has finished: hand over, or play out the round's world time.</summary>
    public void EndPhase(Simulation sim)
    {
        if (!Active)
            return;
        FinishAnimation(sim);
        if (Phase == Side.Village)
        {
            BeginPhase(sim, Side.Raiders);
            return;
        }

        // Both sides have gone: the world moves on for a few seconds.
        sim.TickMany(RoundTicks);
        sim.RecentShots.RemoveAll(s => sim.Clock.Tick - s.Tick > RoundTicks);
        Units.RemoveAll(u => !sim.World.IsAlive(u.Entity) || sim.World.Get<Creature>(u.Entity).Dead);
        _members.RemoveWhere(e => !sim.World.IsAlive(e) || sim.World.Get<Creature>(e).Dead);
        if (CheckEnd(sim))
            return;
        Round++;
        Recruit(sim);
        BeginPhase(sim, Side.Village);
    }

    /// <summary>Is it this unit's turn, and can it still do something?</summary>
    public string? CanCommand(Simulation sim, Entity e)
    {
        if (!Active)
            return "not in combat";
        var u = Unit(e);
        if (u == null)
            return "not part of this fight";
        if (u.Side != Phase)
            return "not this side's turn";
        if (u.Acted)
            return "already done this turn";
        if (!sim.World.Get<Creature>(e).Able)
            return "can't act";
        if (Busy(sim))
            return "still moving";
        return null;
    }

    /// <summary>TU a unit keeps back for a reaction while <see cref="CombatUnit.ReserveForReactions"/> is on: its cheapest attack.</summary>
    public float Reserve(Simulation sim, CombatUnit u) =>
        u.ReserveForReactions ? Combat.TimeUnits.CheapestAttack(sim, u.Entity) ?? 0f : 0f;

    /// <summary>Where the unit can move with the TU it may spend on moving (all of it, less any reserve).</summary>
    public MoveRange RangeOf(Simulation sim, Entity e) =>
        TacticalMoves.Reachable(sim, e, Unit(e) is { } u ? Math.Max(0, u.TimeUnits - Reserve(sim, u)) : 0);

    /// <summary>
    /// A dragon given an order rolls obedience once a turn. On a refusal it acts on its own for
    /// the rest of the turn; the refusal is returned (and logged).
    /// </summary>
    private string? Obey(Simulation sim, CombatUnit u)
    {
        if (u.ObedienceRolled || u.Instinct || !sim.World.Has<Dragon>(u.Entity) || u.Side != Side.Village || VillageAuto)
            return null;
        u.ObedienceRolled = true;
        if (sim.Rand.Chance(DragonSystem.Obedience(sim, u.Entity)))
            return null;
        u.Instinct = true;
        var name = sim.World.Get<Creature>(u.Entity).Name;
        var refusal = $"{name} ignores the order and follows its own instincts!";
        Write(sim, u.Side, refusal);
        return refusal;
    }

    /// <summary>Pay <paramref name="cost"/> TU from the unit's pool, or say why it can't.</summary>
    private static string? Afford(CombatUnit u, float cost) =>
        u.TimeUnits + 1e-3f >= cost ? null : $"not enough time units ({cost:F0} needed, {u.TimeUnits:F0} left)";

    /// <summary>
    /// Move a unit to a cell it can reach with its TU (it may move again with what's left, and act
    /// before or after). A rider doesn't move: their mount does, carrying them along.
    /// </summary>
    public string? Move(Simulation sim, Entity e, int3 cell, bool fromAi = false)
    {
        if (sim.World.TryGet<Riding>(e, out var riding))
            return $"riding {sim.World.Get<Creature>(riding.Mount).Name}: move the dragon";
        if (CanCommand(sim, e) is { } why)
            return why;
        var u = Unit(e)!;
        if (!fromAi && Obey(sim, u) is { } refused)
            return refused;
        var range = RangeOf(sim, e);
        if (range.PathTo(sim.Map, cell) is not { } path)
            return u.ReserveForReactions ? "can't get there and keep TU for a reaction" : "not enough time units to get there";
        if (path.Count == 0)
            return null;
        u.TimeUnits = Math.Max(0, u.TimeUnits - range.CostTo(sim.Map, cell));
        u.Moved = true;
        var mover = sim.World.Get<Mover>(e);
        mover.Path = path;
        mover.PathIndex = 0;
        mover.StepProgress = 0;
        mover.StepFrom = range.From;
        ReactedThisMove.Clear();
        Animating = e;
        return null;
    }

    /// <summary>Attack in a shot mode (ranged: snap or aimed; melee: Normal), paying its TU.</summary>
    public AttackResult? Attack(Simulation sim, Entity e, Entity target, AttackDef attack, out string? error, bool fromAi = false, ShotMode? mode = null)
    {
        error = CanCommand(sim, e);
        if (error != null)
            return null;
        var u = Unit(e)!;
        var how = mode ?? Combat.TimeUnits.DefaultMode(attack);
        if (!attack.Ranged)
            how = ShotMode.Normal;
        else if (how == ShotMode.Normal)
            how = ShotMode.Snap;
        if (!CombatRules.Attacks(sim, e).Contains(attack))
        {
            error = "can't attack that way";
            return null;
        }
        var cost = Combat.TimeUnits.AttackCost(sim, e, attack, how);
        if (Afford(u, cost) is { } broke)
        {
            error = broke;
            return null;
        }
        var odds = CombatRules.Chance(sim, e, sim.World.Get<Position>(e).Cell, target, attack, mode: how);
        if (!odds.Possible)
        {
            error = odds.Blocked;
            return null;
        }
        if (!fromAi && Obey(sim, u) is { } refused)
        {
            error = refused;
            return null;
        }
        u.TimeUnits -= cost;
        ReactedThisMove.Clear();
        var result = CombatRules.Resolve(sim, e, target, attack, mode: how);
        Write(sim, u.Side, (how == ShotMode.Aimed ? "Aimed: " : "") + result.Describe(sim));
        sim.RecentShots.Add(new ShotEvent(sim.Clock.Tick, sim.World.Get<Position>(e).Pos, sim.World.Get<Position>(target).Pos, attack.Ranged, result.Outcome == AttackOutcome.Hit));
        if (!CheckEnd(sim))
            Reactions.AfterAttack(sim, this, e, [target]);
        return result;
    }

    /// <summary>Breathe (fire, or its element's breath) at a cell within the dragon's breath range.</summary>
    public string? Breathe(Simulation sim, Entity e, int3 cell, bool fromAi = false)
    {
        if (CanCommand(sim, e) is { } why)
            return why;
        var u = Unit(e)!;
        var stage = sim.World.Get<Creature>(e).Stage(sim.Clock.Tick);
        if (stage.BreathRange <= 0)
            return "can't breathe fire";
        if (CombatRules.Distance(sim.World.Get<Position>(e).Cell, cell) > Environment.Breath.RangeOf(sim, e))
            return "out of breath range";
        var cost = Combat.TimeUnits.BreathCost(sim, e);
        if (Afford(u, cost) is { } broke)
            return broke;
        if (!fromAi && Obey(sim, u) is { } refused)
            return refused;
        u.TimeUnits -= cost;
        ReactedThisMove.Clear();
        var (breath, burned) = CombatRules.Breathe(sim, e, cell);
        var name = sim.World.Get<Creature>(e).Name;
        var who = burned.Count == 0 ? "no one" : string.Join(", ", burned.Select(b => $"{sim.World.Get<Creature>(b.Who).Name} ({b.Damage:F0})"));
        if (breath.Element == DragonElement.Fire)
            Write(sim, u.Side, $"{name} breathes fire: {breath.Ignited} cells catch, burning {who}.");
        else
            Write(sim, u.Side, $"{name} {Systems.Dragons.BreathOf(sim, e).Breath}, catching {who}{(breath.Arcs.Count > 0 ? $" (it leaps {breath.Arcs.Count} times)" : "")}.");
        if (!CheckEnd(sim))
            Reactions.AfterAttack(sim, this, e, burned.Select(b => b.Who).ToList());
        return null;
    }

    /// <summary>Items a unit could pick up now: lying in its cell or one it touches, on its level.</summary>
    public List<Entity> ItemsInReach(Simulation sim, Entity e)
    {
        var list = new List<Entity>();
        var w = sim.World;
        if (w.Has<Riding>(e) || w.Get<Creature>(e).Def.Race != Race.Human)
            return list;
        var here = w.Get<Position>(e).Cell;
        foreach (var c in sim.SpiralFrom(here, 1))
        {
            if (c.Z != here.Z || (c != here && !Pathfinder.Touches(here, c)))
                continue;
            var item = sim.Map.Item[sim.Map.Idx(c)];
            if (!item.IsNull && w.Get<Thing>(item).Def.Category == ThingCategory.Item && !sim.Reservations.IsReservedByOther(item, e))
                list.Add(item);
        }
        return list;
    }

    /// <summary>
    /// Pick up an item in reach (<see cref="Combat.TimeUnits.PickUp"/>): see <see cref="Acquire"/>
    /// for where it goes. Refused, for nothing, if there's nowhere to put it.
    /// </summary>
    public string? PickUp(Simulation sim, Entity e, Entity item)
    {
        if (CanCommand(sim, e) is { } why)
            return why;
        if (!ItemsInReach(sim, e).Contains(item))
            return "out of reach";
        return Take(sim, e, item, "picks up");
    }

    /// <summary>Containers (a chest, the wagon) beside the unit or in its cell, on its level.</summary>
    public List<Entity> ContainersInReach(Simulation sim, Entity e)
    {
        var list = new List<Entity>();
        var w = sim.World;
        if (w.Has<Riding>(e) || w.Get<Creature>(e).Def.Race != Race.Human)
            return list;
        var here = w.Get<Position>(e).Cell;
        foreach (var c in sim.SpiralFrom(here, 1))
        {
            if (c.Z != here.Z || (c != here && !Pathfinder.Touches(here, c)))
                continue;
            var s = sim.Map.Structure[sim.Map.Idx(c)];
            if (!s.IsNull && !w.Has<Construction>(s) && w.Get<Thing>(s).Def.Building is { StorageStacks: > 0 } && Inventory.CanReachContainer(sim, e, s))
                list.Add(s);
        }
        return list;
    }

    /// <summary>
    /// Take a stack out of a container in reach (<see cref="Combat.TimeUnits.PickUp"/>), your gear
    /// out of the captors' chest, say: see <see cref="Acquire"/> for where it goes.
    /// </summary>
    public string? TakeOut(Simulation sim, Entity e, Entity item)
    {
        if (CanCommand(sim, e) is { } why)
            return why;
        var w = sim.World;
        if (!w.IsAlive(item) || !w.TryGet<Stored>(item, out var stored) || !ContainersInReach(sim, e).Contains(stored.In))
            return "out of reach";
        return Take(sim, e, item, "takes");
    }

    private string? Take(Simulation sim, Entity e, Entity item, string verb)
    {
        var u = Unit(e)!;
        var w = sim.World;
        if (Placement(sim, e, item) is not { } how)
            return "hands full (and no room in a pack)";
        var cost = Combat.TimeUnits.Share(sim, e, Combat.TimeUnits.PickUp);
        if (Afford(u, cost) is { } broke)
            return broke;
        u.TimeUnits -= cost;
        var label = w.Get<Thing>(item).Label;
        Acquire(sim, e, item, how);
        Write(sim, u.Side, $"{w.Get<Creature>(e).Name} {verb} the {label}{how switch { Gain.Wield => " and wields it", Gain.Wear => " and puts it on", _ => "" }}.");
        return null;
    }

    private enum Gain { Wield, Wear, Hands, Pack }

    /// <summary>
    /// Where a taken item would go: a weapon into empty weapon hands, gear into a free slot (a
    /// pack onto an empty back), anything else into empty hands or the pack. Null: nowhere, so
    /// nothing is ever dropped to make room (the chain key in your hand stays there).
    /// </summary>
    private static Gain? Placement(Simulation sim, Entity e, Entity item)
    {
        var w = sim.World;
        var t = w.Get<Thing>(item);
        if (t.Def.Weapon != null && (!w.TryGet<Equipment>(e, out var eq) || !w.IsAlive(eq.Weapon)))
            return Gain.Wield;
        if (t.Def.Apparel is { } a && (!w.TryGet<Apparel>(e, out var worn) || !w.IsAlive(worn[a.Slot])))
            return Gain.Wear;
        if (!w.Get<Carrying>(e).HasItem)
            return Gain.Hands;
        var pack = sim.PackOf(e);
        return !pack.IsNull && sim.ContainerRoom(pack, t.Def, t.Material) > 0 ? Gain.Pack : null;
    }

    private static void Acquire(Simulation sim, Entity e, Entity item, Gain how)
    {
        var w = sim.World;
        var t = w.Get<Thing>(item);
        var stack = w.TryGet<Stack>(item, out var st) ? st.Count : 1;
        switch (how)
        {
            case Gain.Wield or Gain.Wear:
                Inventory.PutOn(sim, e, sim.TakeFromStack(item, 1));
                break;
            case Gain.Hands:
                w.Get<Carrying>(e).Item = sim.TakeFromStack(item, Math.Min(stack, t.Def.StackLimit));
                break;
            case Gain.Pack:
                var pack = sim.PackOf(e);
                sim.Store(pack, sim.TakeFromStack(item, Math.Min(stack, sim.ContainerRoom(pack, t.Def, t.Material))));
                break;
        }
    }

    /// <summary>Bodies bigger than this (a grown dragon) are too heavy to drag.</summary>
    public const float MaxDragSize = 1.5f;

    /// <summary>
    /// Downed or dead creatures the unit could drag now: beside it on its level, not too big, not
    /// being carried. (A body blocks its cell: nobody can end a move there.)
    /// </summary>
    public List<Entity> BodiesInReach(Simulation sim, Entity e)
    {
        var list = new List<Entity>();
        var w = sim.World;
        if (w.Has<Riding>(e) || w.Get<Creature>(e).Def.Race != Race.Human)
            return list;
        var here = w.Get<Position>(e).Cell;
        foreach (var o in sim.Creatures())
        {
            if (o == e || w.Has<Carried>(o) || w.Has<Riding>(o))
                continue;
            var c = w.Get<Creature>(o);
            var at = w.Get<Position>(o).Cell;
            if (c.Able || at.Z != here.Z || !Pathfinder.Touches(here, at) || c.Stage(sim.Clock.Tick).Size > MaxDragSize)
                continue;
            list.Add(o);
        }
        return list;
    }

    /// <summary>Where a body can be dragged to: a free, standable cell beside it on its level (not the dragger's).</summary>
    public List<int3> DragTargets(Simulation sim, Entity e, Entity body)
    {
        var list = new List<int3>();
        var w = sim.World;
        var map = sim.Map;
        var at = w.Get<Position>(body).Cell;
        var here = w.Get<Position>(e).Cell;
        var taken = new HashSet<int3>();
        foreach (var o in sim.Creatures())
            if (!w.Has<Carried>(o) && !w.Has<Riding>(o))
                taken.Add(w.Get<Position>(o).Cell);
        foreach (var d in Maps.Map.Neighbors8)
        {
            var c = new int3(at.X + d.X, at.Y + d.Y, at.Z);
            if (c == here || taken.Contains(c) || !map.InBounds(c) || !map.Passable(c) || TacticalMoves.Airborne(sim, c))
                continue;
            // Diagonals only where both sides are open (no dragging through a wall's corner).
            if (d.X != 0 && d.Y != 0 && (!map.Passable(new int3(c.X, at.Y, at.Z)) || !map.Passable(new int3(at.X, c.Y, at.Z))))
                continue;
            list.Add(c);
        }
        return list;
    }

    /// <summary>
    /// Drag a downed or dead creature beside the unit into a free cell next to where it lies
    /// (<see cref="Combat.TimeUnits.Drag"/>): clearing a doorway, pulling a friend out of the line of fire.
    /// </summary>
    public string? Drag(Simulation sim, Entity e, Entity body, int3 to)
    {
        if (CanCommand(sim, e) is { } why)
            return why;
        var u = Unit(e)!;
        var w = sim.World;
        if (!BodiesInReach(sim, e).Contains(body))
            return w.IsAlive(body) && w.Get<Creature>(body).Stage(sim.Clock.Tick).Size > MaxDragSize ? "too heavy to drag" : "nothing to drag there";
        if (!DragTargets(sim, e, body).Contains(to))
            return "can't drag it there";
        var cost = Combat.TimeUnits.Share(sim, e, Combat.TimeUnits.Drag);
        if (Afford(u, cost) is { } broke)
            return broke;
        u.TimeUnits -= cost;
        if (w.TryGet<Mover>(body, out var mover))
            mover.Stop();
        w.Get<Position>(body) = Position.At(to);
        Write(sim, u.Side, $"{w.Get<Creature>(e).Name} drags {w.Get<Creature>(body).Name} aside.");
        return null;
    }

    /// <summary>
    /// Let go of what's in hand (<see cref="Combat.TimeUnits.Drop"/>): it falls at the unit's feet,
    /// freeing the hands to pick something else up (a sword where the chain key was).
    /// </summary>
    public string? DropHeld(Simulation sim, Entity e)
    {
        if (CanCommand(sim, e) is { } why)
            return why;
        var u = Unit(e)!;
        var w = sim.World;
        var held = w.Get<Carrying>(e).Item;
        if (held.IsNull || !w.IsAlive(held))
            return "nothing in hand";
        var cost = Combat.TimeUnits.Share(sim, e, Combat.TimeUnits.Drop);
        if (Afford(u, cost) is { } broke)
            return broke;
        u.TimeUnits -= cost;
        var label = w.Get<Thing>(held).Label;
        w.Get<Carrying>(e).Item = Entity.Null;
        sim.PlaceItemNear(held, w.Get<Position>(e).Cell);
        Write(sim, u.Side, $"{w.Get<Creature>(e).Name} drops the {label}.");
        return null;
    }

    /// <summary>Weapons a unit could switch to now: in its hands or its own pack (not the one it's wielding).</summary>
    public List<Entity> WeaponsToHand(Simulation sim, Entity e)
    {
        var list = new List<Entity>();
        var w = sim.World;
        if (!w.Has<Carrying>(e) || w.Get<Creature>(e).Def.Race != Race.Human)
            return list;
        var held = w.Get<Carrying>(e).Item;
        if (!held.IsNull && w.IsAlive(held) && w.Get<Thing>(held).Def.Weapon != null)
            list.Add(held);
        var pack = sim.PackOf(e);
        if (!pack.IsNull)
            foreach (var item in sim.StoredIn(pack))
                if (w.Get<Thing>(item).Def.Weapon != null)
                    list.Add(item);
        return list;
    }

    /// <summary>
    /// Switch to a weapon from the hands or the pack (<see cref="Combat.TimeUnits.Wield"/>): the one
    /// it was wielding goes into the pack, else the hands, else down at its feet (<see cref="Inventory.Equip"/>).
    /// </summary>
    public string? Wield(Simulation sim, Entity e, Entity weapon)
    {
        if (CanCommand(sim, e) is { } why)
            return why;
        var u = Unit(e)!;
        var w = sim.World;
        if (!WeaponsToHand(sim, e).Contains(weapon))
            return "not to hand";
        var cost = Combat.TimeUnits.Share(sim, e, Combat.TimeUnits.Wield);
        if (Afford(u, cost) is { } broke)
            return broke;
        var label = w.Get<Thing>(weapon).Label;
        var before = w.TryGet<Equipment>(e, out var eq) && w.IsAlive(eq.Weapon) ? w.Get<Thing>(eq.Weapon).Label : null;
        if (!Inventory.Equip(sim, e, weapon))
            return "can't wield that";
        u.TimeUnits -= cost;
        Write(sim, u.Side, $"{w.Get<Creature>(e).Name} {(before != null ? $"puts away the {before} and draws" : "draws")} the {label}.");
        return null;
    }

    /// <summary>
    /// Something the story lets a unit do mid-fight (freeing a chained dragon), for
    /// <paramref name="share"/> of its TU. <paramref name="act"/> does it and says whether it worked.
    /// </summary>
    public string? Interact(Simulation sim, Entity e, float share, Func<bool> act, string log)
    {
        if (CanCommand(sim, e) is { } why)
            return why;
        var u = Unit(e)!;
        var cost = Combat.TimeUnits.Share(sim, e, share);
        if (Afford(u, cost) is { } broke)
            return broke;
        if (!act())
            return "can't do that now";
        u.TimeUnits -= cost;
        Write(sim, u.Side, log);
        return null;
    }

    internal void Reacted(Simulation sim, Entity reactor, Entity target, ReactionKind kind)
    {
        ReactionCount++;
        LastReaction = (reactor, target, kind);
        ReactedThisMove.Add(reactor);
        if (Unit(reactor) is { } u)
        {
            if (kind == ReactionKind.Overwatch)
                u.OverwatchUsed = true;
            else if (kind == ReactionKind.Response)
                u.ResponseUsed = true;
        }
        CheckEnd(sim);
    }

    /// <summary>End the unit's turn: what TU it has left are kept for reactions.</summary>
    public void Wait(Simulation sim, Entity e)
    {
        if (Unit(e) is { } u && u.Side == Phase)
            u.Acted = true;
    }

    /// <summary>Remove a unit that fled off the map.</summary>
    internal void Flee(Simulation sim, CombatUnit u)
    {
        Write(sim, u.Side, $"{sim.World.Get<Creature>(u.Entity).Name} flees the battle.");
        sim.Depart(u.Entity);
        u.Acted = true;
        Units.Remove(u);
        _members.Remove(u.Entity);
        CheckEnd(sim);
    }

    // ---------------- Driving ----------------

    /// <summary>
    /// One movement tick for the unit being animated (the world clock stays put). Reactions fire
    /// as the mover leaves a cell (swings) and enters one (overwatch); a mover they bring down
    /// stops where it is.
    /// </summary>
    public void Animate(Simulation sim)
    {
        if (Animating.IsNull || !sim.World.IsAlive(Animating))
            return;
        var e = Animating;
        var w = sim.World;
        var mover = w.Get<Mover>(e);
        if (mover.Moving && mover.StepProgress == 0)
        {
            Reactions.BeforeStep(sim, this, e, mover.StepFrom, mover.Path![mover.PathIndex]);
            if (StoppedByReaction(sim, e, mover))
                return;
        }
        var before = w.Get<Position>(e).Cell;
        sim.Movement.Step(sim, e);
        var after = w.Get<Position>(e).Cell;
        if (after != before && Active)
        {
            Reactions.Entered(sim, this, e, before, after);
            if (StoppedByReaction(sim, e, mover))
                return;
        }
        if (!mover.Moving)
            Animating = Entity.Null;
    }

    /// <summary>The fight ended or the mover went down: stop it in the cell it's in.</summary>
    private bool StoppedByReaction(Simulation sim, Entity e, Mover mover)
    {
        var w = sim.World;
        if (Active && w.IsAlive(e) && w.Get<Creature>(e).Able)
            return false;
        if (w.IsAlive(e))
        {
            mover.Stop();
            w.Get<Position>(e) = Position.At(w.Get<Position>(e).Cell);
            if (w.TryGet<Mount>(e, out var mount) && w.IsAlive(mount.Rider))
                w.Get<Position>(mount.Rider) = w.Get<Position>(e);
        }
        Animating = Entity.Null; // (the move's TU were paid up front: a mover cut short doesn't get them back)
        return true;
    }

    /// <summary>Play any move animation to the end at once.</summary>
    public void FinishAnimation(Simulation sim)
    {
        for (var i = 0; i < 10_000 && Busy(sim); i++)
            Animate(sim);
        Animating = Entity.Null;
    }

    /// <summary>Does the AI play this unit (raiders, the village on auto, dragons acting on instinct, companions)?</summary>
    public bool AiControls(CombatUnit u) => u.Side == Side.Raiders || VillageAuto || u.Instinct || u.Companion;

    /// <summary>
    /// Can the player give this village creature orders in a fight? Without an avatar (the overseer
    /// game) everyone; in Adventure mode only the avatar, the dragon it rides, and a dragon whose bond
    /// with the avatar has reached <see cref="AvatarSystem.CommandDragonAffinity"/>.
    /// </summary>
    public static bool PlayerCommands(Simulation sim, Entity e)
    {
        var w = sim.World;
        if (w.Count<Avatar>() == 0 || w.Has<Avatar>(e))
            return true;
        if (w.TryGet<Mount>(e, out var mount) && w.IsAlive(mount.Rider) && w.Has<Avatar>(mount.Rider))
            return true;
        return w.TryGet<Dragon>(e, out var d) && w.IsAlive(d.Keeper) && w.Has<Avatar>(d.Keeper)
            && d.Affinity >= AvatarSystem.CommandDragonAffinity;
    }

    /// <summary>
    /// Let the AI take one step (a move or an action) for the next unit it controls in this
    /// phase. When an AI-controlled side has nobody left to act, its phase ends. Returns false
    /// when there was nothing for the AI to do (it's the player's move).
    /// </summary>
    public bool StepAi(Simulation sim)
    {
        if (!Active || Busy(sim))
            return Active;
        var u = Units.Find(x => x.Side == Phase && !x.Acted && AiControls(x));
        if (u != null)
        {
            Tactics.Step(sim, this, u);
            return true;
        }
        // Also hand over once every player's units are done (or down): in co-op the phase is shared.
        if (Phase == Side.Raiders || VillageAuto || !Units.Exists(x => x.Side == Side.Village && !x.Acted && sim.World.IsAlive(x.Entity) && sim.World.Get<Creature>(x.Entity).Able))
        {
            EndPhase(sim);
            return true;
        }
        return false;
    }

    /// <summary>
    /// One step (1/60 s) of a fight as players see it: the move under way plays
    /// <see cref="AnimateSteps"/> ticks, a reaction holds the picture <see cref="ReactionHoldSteps"/>,
    /// orders that waited for that are applied, and the AI takes its next step every
    /// <see cref="AiStepDelay"/>. The game calls it at 60 a second; a co-op session once per lockstep
    /// step, so every peer plays the same fight at the same pace. Deterministic: only the step count matters.
    /// </summary>
    public void Advance(Simulation sim)
    {
        if (!Active)
            return;
        RefreshCompanions(sim);
        if (_hold > 0)
        {
            _hold--;
            return;
        }
        if (Busy(sim))
        {
            for (var i = 0; i < AnimateSteps && Busy(sim); i++)
            {
                Animate(sim);
                if (HoldOnReaction())
                    break;
            }
            _aiWait = Math.Max(_aiWait, AiStepDelay / 2);
            return;
        }
        if (Queued.Count > 0)
        {
            var order = Queued[0];
            Queued.RemoveAt(0);
            sim.ApplyNow(order);
            HoldOnReaction();
            return;
        }
        if (_aiWait > 0)
        {
            _aiWait--;
            return;
        }
        var round = Round;
        if (StepAi(sim))
            _aiWait = Round != round ? 2 * AiStepDelay : AiStepDelay;
        if (!Busy(sim))
            HoldOnReaction();
    }

    /// <summary>A reaction fired since the last look: hold the picture on it a moment.</summary>
    private bool HoldOnReaction()
    {
        if (ReactionCount == _seenReactions)
            return false;
        _seenReactions = ReactionCount;
        _hold = ReactionHoldSteps;
        return true;
    }

    /// <summary>
    /// Who the AI plays on the village side can change mid-fight: a co-op player who leaves hands
    /// their character (and dragon) to it, and a dragon's trust may grow enough to take orders.
    /// </summary>
    private void RefreshCompanions(Simulation sim)
    {
        foreach (var u in Units)
            if (u.Side == Side.Village && sim.World.IsAlive(u.Entity))
                u.Companion = !PlayerCommands(sim, u.Entity);
    }

    /// <summary>Note what a player's order came to, for their notice (render-only).</summary>
    internal void Report(Entity unit, string text)
    {
        Feedback.Add((unit, text));
    }

    /// <summary>Play out the whole encounter with the AI on both sides (tests, auto-resolve).</summary>
    public void Resolve(Simulation sim, int maxRounds = 100)
    {
        VillageAuto = true;
        while (Active && Round <= maxRounds)
        {
            FinishAnimation(sim);
            StepAi(sim);
        }
    }

    internal void Write(Simulation sim, Side side, string text) => Log.Add(new CombatLogEntry(Round, side, text));

    /// <summary>
    /// The encounter for a save: the last fight (outcome, log, units), and from format 14 a fight in
    /// progress whole (whose turn, every unit's TU and reactions used, the pacing, orders waiting),
    /// so co-op can hand a fight to a joiner or a resynced peer. Player saves still refuse a fight
    /// (<see cref="Saving.SaveGame.CannotSave"/>). A move under way isn't kept (movers' paths aren't
    /// saved): <see cref="Saving.SaveGame.Snapshot"/>'s callers finish it first.
    /// </summary>
    internal void Save(Saving.SaveWriter w, Simulation sim)
    {
        w.Int(Count);
        w.Int(Round);
        w.Int((int)Phase);
        w.Bool(VillageAuto);
        w.NullableString(Outcome);
        w.Int(Log.Count);
        foreach (var entry in Log)
        {
            w.Int(entry.Round);
            w.Int((int)entry.Side);
            w.String(entry.Text);
        }
        w.Int(Units.Count);
        foreach (var u in Units)
        {
            w.Entity(u.Entity);
            w.Int((int)u.Side);
            w.Float(u.TimeUnits); // (the slot that held the old move budget: same format)
            w.Bool(u.Moved);
            w.Bool(u.Acted);
            w.Bool(u.ObedienceRolled);
            w.Bool(u.Instinct);
            w.Bool(u.Fleeing);
        }

        // Format 14: the rest of each unit, and a fight in progress.
        foreach (var u in Units)
        {
            w.Bool(u.ReserveForReactions);
            w.Int(u.AiSteps);
            w.Bool(u.Companion);
            w.Bool(u.OverwatchUsed);
            w.Bool(u.ResponseUsed);
        }
        w.Bool(Active);
        if (!Active)
            return;
        w.Int(ReactionCount);
        w.Entity(LastReaction.Reactor);
        w.Entity(LastReaction.Target);
        w.Int((int)LastReaction.Kind);
        w.Entity(Busy(sim) ? Animating : Entity.Null);
        WriteEntities(w, _members);
        WriteEntities(w, ReactedThisMove);
        w.Int(_hold);
        w.Int(_aiWait);
        w.Int(_seenReactions);
        w.Int(Queued.Count);
        foreach (var order in Queued)
        {
            var bytes = Core.CommandCodec.Encode(order, w.Defs);
            w.Int(bytes.Length);
            w.Raw<byte>(bytes);
        }
    }

    /// <summary>A set in index order, so the bytes (and the hash) don't depend on how it was filled.</summary>
    private static void WriteEntities(Saving.SaveWriter w, HashSet<Entity> set)
    {
        var list = set.OrderBy(e => e.Index).ThenBy(e => e.Generation).ToList();
        w.Int(list.Count);
        foreach (var e in list)
            w.Entity(e);
    }

    private static void ReadEntities(Saving.SaveReader r, HashSet<Entity> into)
    {
        into.Clear();
        var n = r.Count();
        for (var i = 0; i < n; i++)
            into.Add(r.Entity());
    }

    internal void Load(Saving.SaveReader r)
    {
        Active = false;
        Animating = Entity.Null;
        _members.Clear();
        ReactedThisMove.Clear();
        Queued.Clear();
        Feedback.Clear();
        _hold = _aiWait = 0;
        Count = r.Int();
        Round = r.Int();
        Phase = (Side)r.Int();
        VillageAuto = r.Bool();
        Outcome = r.NullableString();
        Log.Clear();
        var n = r.Count();
        for (var i = 0; i < n; i++)
            Log.Add(new CombatLogEntry(r.Int(), (Side)r.Int(), r.String()));
        Units.Clear();
        n = r.Count();
        for (var i = 0; i < n; i++)
            Units.Add(new CombatUnit
            {
                Entity = r.Entity(), Side = (Side)r.Int(), TimeUnits = r.Float(), Moved = r.Bool(), Acted = r.Bool(),
                ObedienceRolled = r.Bool(), Instinct = r.Bool(), Fleeing = r.Bool(),
            });
        if (r.Version < 14)
            return;
        foreach (var u in Units)
        {
            u.ReserveForReactions = r.Bool();
            u.AiSteps = r.Int();
            u.Companion = r.Bool();
            u.OverwatchUsed = r.Bool();
            u.ResponseUsed = r.Bool();
        }
        if (!r.Bool())
            return;
        Active = true;
        ReactionCount = r.Int();
        LastReaction = (r.Entity(), r.Entity(), (ReactionKind)r.Int());
        Animating = r.Entity();
        ReadEntities(r, _members);
        ReadEntities(r, ReactedThisMove);
        _hold = r.Int();
        _aiWait = r.Int();
        _seenReactions = r.Int();
        n = r.Count(MaxQueued);
        for (var i = 0; i < n; i++)
        {
            var bytes = new byte[r.Count(1 << 16)];
            r.Raw<byte>(bytes);
            Queued.Add(Core.CommandCodec.Decode(bytes, r.Defs));
        }
    }

    /// <summary>
    /// The fight in progress for <see cref="Simulation.StateHash"/> (co-op's desync check): whose
    /// turn, each unit's TU and flags, the pacing and orders waiting. Nothing while there's no fight,
    /// so hashes outside fights stay as they were.
    /// </summary>
    internal void Fingerprint(Simulation sim, Action<long> mix)
    {
        if (!Active)
            return;
        mix(Round);
        mix((long)Phase);
        mix(VillageAuto ? 1 : 0);
        mix(Log.Count);
        mix(ReactionCount);
        mix(Busy(sim) ? Animating.Index : -1);
        mix(_hold);
        mix(_aiWait);
        mix(Queued.Count);
        foreach (var u in Units)
        {
            mix(u.Entity.Index);
            mix((long)(u.TimeUnits * 1000));
            mix((u.Moved ? 1 : 0) | (u.Acted ? 2 : 0) | (u.ObedienceRolled ? 4 : 0) | (u.Instinct ? 8 : 0) | (u.Fleeing ? 16 : 0)
                | (u.Companion ? 32 : 0) | (u.OverwatchUsed ? 64 : 0) | (u.ResponseUsed ? 128 : 0) | (u.ReserveForReactions ? 256 : 0));
            mix(u.AiSteps);
        }
    }
}

/// <summary>A raider who has given up and is heading off the map.</summary>
public struct Leaving;

/// <summary>An attack, for the renderer (arrow streaks, hit flashes). Not sim state.</summary>
public sealed record ShotEvent(long Tick, float3 From, float3 To, bool Ranged, bool Hit);

/// <summary>Watches for hostiles closing in and starts an <see cref="Encounter"/>.</summary>
public sealed class CombatSystem : ISimSystem
{
    public const int CheckInterval = 30;

    public string Name => "Combat";
    public int Interval => CheckInterval;

    public void Update(Simulation sim) => sim.Combat.TryStart(sim);
}
