using DragonWorld.Sim.Ai;
using DragonWorld.Sim.Components;
using DragonWorld.Sim.Defs;
using DragonWorld.Sim.Systems;

namespace DragonWorld.Sim.Combat;

/// <summary>
/// The combat AI, one step at a time: raiders, the village on auto, companions, and dragons that
/// refused an order. A step is a move or an action, so the game can animate the move before the
/// unit attacks; a unit keeps taking steps while its time units (<see cref="TimeUnits"/>) last and
/// then ends its turn, keeping the rest for reactions. Units weigh every attack they could pay for
/// from where they stand against every attack from every cell in reach with the TU left after
/// getting there (expected damage: hit chance × chance to hurt × average damage), reposition
/// (once a turn) if that's clearly better, else close in. Moves are charged the expected damage of
/// the reactions they'd draw (<see cref="Reactions.Threats"/>); archers keep a snap shot's TU back
/// when they close in. Hurt raiders run for the map edge; dragons favor threats near their keeper;
/// the non-violent keep their distance.
/// </summary>
public static class Tactics
{
    private readonly record struct Option(float Score, Entity Target, AttackDef? Attack, ShotMode Mode, int3 BreathAt)
    {
        public static readonly Option None = new(0, Entity.Null, null, ShotMode.Normal, default);
    }

    /// <summary>A backstop: steps an AI unit may take in a turn before it's made to end it.</summary>
    public const int MaxStepsPerTurn = 12;

    public static void Step(Simulation sim, Encounter enc, CombatUnit u)
    {
        var w = sim.World;
        var e = u.Entity;
        var here = w.Get<Position>(e).Cell;
        if (++u.AiSteps > MaxStepsPerTurn)
        {
            enc.Wait(sim, e);
            return;
        }

        // Hurt raiders give up; an escaped prisoner with bare hands just runs.
        if (u.Side == Side.Raiders && (RaiderBrain.WantsToFlee(sim, e) || (w.Has<Escaping>(e) && !RaiderBrain.Armed(sim, e)) || PackBroken(sim, enc, e)))
        {
            Flee(sim, enc, u, here);
            return;
        }

        var enemies = enc.Units.Where(o => o.Side != u.Side && w.IsAlive(o.Entity) && w.Get<Creature>(o.Entity).Able).Select(o => o.Entity).ToList();
        var allies = enc.Units.Where(o => o.Side == u.Side && o.Entity != e && w.IsAlive(o.Entity)).Select(o => o.Entity).ToList();
        var attacks = CombatRules.Attacks(sim, e);
        var stage = w.Get<Creature>(e).Stage(sim.Clock.Tick);
        var breathes = stage.BreathRange > 0 && attacks.Count > 0;
        var focus = w.TryGet<Dragon>(e, out var dragon) && w.IsAlive(dragon.Keeper) ? dragon.Keeper : Entity.Null;

        if (enemies.Count == 0)
        {
            enc.Wait(sim, e);
            return;
        }
        if (attacks.Count == 0)
        {
            KeepAway(sim, enc, u, enemies);
            return;
        }

        var now = BestFrom(sim, enc, e, here, enemies, allies, attacks, breathes, focus, u.TimeUnits);
        if (!u.Moved)
        {
            var range = enc.RangeOf(sim, e);
            var cells = Sorted(range);
            var ranged = attacks[0].Ranged;
            var danger = Dangerous(sim, enc, u, here);
            var best = now;
            var bestCell = here;
            foreach (var i in cells)
            {
                var c = sim.Map.CellOf(i);
                if (c == here)
                    continue;
                var o = BestFrom(sim, enc, e, c, enemies, allies, attacks, breathes, focus, u.TimeUnits - range.CostTo(sim.Map, c));
                // Archers would rather not end next to someone with an axe, and nobody wants to
                // walk into a snap shot or past a blade for nothing.
                var score = o.Score - (ranged && NearestDistance(sim, c, enemies) < 1.5f ? 3f : 0f)
                    - (danger ? ThreatCost(sim, enc, e, here, range, c) : 0f);
                if (score > best.Score + 0.5f)
                {
                    best = o with { Score = score };
                    bestCell = c;
                }
            }
            if (bestCell != here)
            {
                if (enc.Move(sim, e, bestCell, fromAi: true) != null)
                    enc.Wait(sim, e);
                return;
            }
            if (now.Score <= 0)
            {
                // Nothing to hit from anywhere in reach: close in. An archer keeps a snap shot's TU back to react with.
                var keep = ranged ? TimeUnits.AttackCost(sim, e, attacks[0], ShotMode.Snap) : 0f;
                var approachRange = keep > 0 ? TacticalMoves.Reachable(sim, e, Math.Max(0, u.TimeUnits - keep)) : range;
                if (Approach(sim, enc, e, here, approachRange, Sorted(approachRange), enemies, focus, attacks, danger) is { } toward && toward != here)
                {
                    if (enc.Move(sim, e, toward, fromAi: true) != null)
                        enc.Wait(sim, e);
                    return;
                }
            }
        }

        // Attack (again) while the TU last; then end the turn, keeping the rest for reactions.
        if (now.Attack != null)
        {
            if (enc.Attack(sim, e, now.Target, now.Attack, out _, fromAi: true, mode: now.Mode) == null)
                enc.Wait(sim, e);
        }
        else if (now.Score > 0)
        {
            if (enc.Breathe(sim, e, now.BreathAt, fromAi: true) != null)
                enc.Wait(sim, e);
        }
        else
            enc.Wait(sim, e);
    }

    /// <summary>
    /// The shot mode the AI makes a ranged attack in with <paramref name="tu"/> to spend: two snap
    /// shots if it can afford them, else one aimed, else one snap; null if not even that.
    /// </summary>
    /// <summary>
    /// A predator gives up once half its side is down, dead or gone: a pack breaks and runs (a lone
    /// bear only when it's hurt, as anyone does).
    /// </summary>
    public static bool PackBroken(Simulation sim, Encounter enc, Entity e)
    {
        var w = sim.World;
        if (w.Get<Creature>(e).Faction != Faction.Predator)
            return false;
        int total = 0, down = 0;
        foreach (var u in enc.Units)
        {
            if (u.Side != Side.Raiders)
                continue;
            total++;
            if (!w.IsAlive(u.Entity) || !w.Get<Creature>(u.Entity).Able || u.Fleeing)
                down++;
        }
        return total > 1 && down * 2 >= total;
    }

    public static ShotMode? ModeFor(Simulation sim, Entity e, AttackDef attack, float tu)
    {
        if (!attack.Ranged)
            return tu + 1e-3f >= TimeUnits.AttackCost(sim, e, attack, ShotMode.Normal) ? ShotMode.Normal : null;
        var snap = TimeUnits.AttackCost(sim, e, attack, ShotMode.Snap);
        var aimed = TimeUnits.AttackCost(sim, e, attack, ShotMode.Aimed);
        return tu + 1e-3f >= 2 * snap ? ShotMode.Snap : tu + 1e-3f >= aimed ? ShotMode.Aimed : tu + 1e-3f >= snap ? ShotMode.Snap : null;
    }

    /// <summary>Could a move from here draw a reaction at all (someone with TU to shoot, or a blade near enough)? Skips the path checks when not.</summary>
    private static bool Dangerous(Simulation sim, Encounter enc, CombatUnit u, int3 here)
    {
        var w = sim.World;
        var reach = u.TimeUnits / Math.Max(0.1f, TimeUnits.WalkCost(sim, u.Entity)) + 2f;
        foreach (var o in enc.Units)
        {
            if (o.Side == u.Side || !w.IsAlive(o.Entity) || !w.Get<Creature>(o.Entity).Able || !Reactions.CanReact(sim, o))
                continue;
            var theirs = CombatRules.Attacks(sim, o.Entity);
            if (theirs.Exists(a => a.Ranged))
                return true;
            if (CombatRules.Distance(w.Get<Position>(o.Entity).Cell, here) <= reach && theirs.Exists(a => !a.Ranged))
                return true;
        }
        return false;
    }

    /// <summary>Expected damage the reactions along the path to <paramref name="c"/> would deal (to the mover or its rider).</summary>
    private static float ThreatCost(Simulation sim, Encounter enc, Entity e, int3 here, MoveRange range, int3 c) =>
        range.PathTo(sim.Map, c) is { } path ? Reactions.Threats(sim, enc, e, here, path).Sum(t => t.Expected) : 0f;

    /// <summary>How much the AI minds the answers an attack draws (their expected damage to it), against the damage it deals.</summary>
    private const float AnswerWeight = 0.5f;

    /// <summary>
    /// The best thing to do from a cell with <paramref name="tu"/> to spend: an attack on someone, or
    /// a breath at a cluster of enemies, less what the enemies who'd answer it would do back.
    /// </summary>
    private static Option BestFrom(Simulation sim, Encounter enc, Entity e, int3 from, List<Entity> enemies, List<Entity> allies, List<AttackDef> attacks, bool breathes, Entity focus, float tu)
    {
        var best = BestAction(sim, e, from, enemies, allies, attacks, breathes, focus, tu);
        if (best.Score <= 0)
            return best;
        var answers = Reactions.Answers(sim, enc, e, from, best.Target).Sum(t => t.Expected);
        // Still worth doing if it's anything at all: answers make it less attractive, not a reason to stand idle.
        return best with { Score = Math.Max(0.01f, best.Score - AnswerWeight * answers) };
    }

    private static Option BestAction(Simulation sim, Entity e, int3 from, List<Entity> enemies, List<Entity> allies, List<AttackDef> attacks, bool breathes, Entity focus, float tu)
    {
        var w = sim.World;
        var best = Option.None;
        foreach (var t in enemies)
        {
            var pain = w.TryGet<Health>(t, out var h) ? h.Pain : 0f;
            var protect = !focus.IsNull && CombatRules.Distance(w.Get<Position>(t).Cell, w.Get<Position>(focus).Cell) <= 4 ? 1.3f : 1f;
            foreach (var a in attacks)
            {
                if (ModeFor(sim, e, a, tu) is not { } mode)
                    continue;
                var odds = CombatRules.Chance(sim, e, from, t, a, mode: mode);
                if (!odds.Possible)
                    continue;
                var score = odds.Chance * odds.DamageChance * (a.DamageMin + a.DamageMax) * 0.5f * (1f + 0.5f * pain) * protect;
                if (score > best.Score)
                    best = new Option(score, t, a, mode, default);
            }
        }

        if (breathes && tu + 1e-3f >= TimeUnits.BreathCost(sim, e))
        {
            var stage = w.Get<Creature>(e).Stage(sim.Clock.Tick);
            var breathType = sim.Defs.AllElements.Count > 0 ? Dragons.BreathOf(sim, e).DamageType : DamageType.Burn;
            foreach (var t in enemies)
            {
                var at = w.Get<Position>(t).Cell;
                if (CombatRules.Distance(from, at) > Environment.Breath.RangeOf(sim, e) * 0.9f)
                    continue;
                if (!CombatRules.LineOfFire(sim, Position.CenterOf(from) + new float3(0, 0, stage.Size * 0.6f), Position.CenterOf(at) + new float3(0, 0, 0.5f)))
                    continue;
                // Flames spill about a cell and a half around where they land; never on friends.
                var caught = enemies.Count(o => CombatRules.Distance(w.Get<Position>(o).Cell, at) <= 1.5f && !Dragons.Resists(sim, o, breathType));
                if (caught == 0 || allies.Exists(o => CombatRules.Distance(w.Get<Position>(o).Cell, at) <= 2.5f))
                    continue;
                if (CombatRules.Distance(from, at) < 1.5f)
                    continue; // too close: we'd stand in our own fire
                var score = caught * 9f;
                if (score > best.Score)
                    best = new Option(score, t, null, ShotMode.Normal, at);
            }
        }
        return best;
    }

    /// <summary>Nothing to hit from anywhere in reach: get closer (to the threat nearest the keeper, for a dragon).</summary>
    private static int3? Approach(Simulation sim, Encounter enc, Entity e, int3 here, MoveRange range, List<int> cells, List<Entity> enemies, Entity focus,
        List<AttackDef> attacks, bool danger)
    {
        var w = sim.World;
        var target = enemies.MinBy(t => CombatRules.Distance(w.Get<Position>(t).Cell, w.Get<Position>(focus.IsNull ? e : focus).Cell));
        var goal = w.Get<Position>(target).Cell;
        var melee = attacks.TrueForAll(a => !a.Ranged);
        int3? best = null;
        var bestScore = float.MaxValue;
        foreach (var i in cells)
        {
            var c = sim.Map.CellOf(i);
            // Melee fighters can't strike from the air, so they'd rather come down.
            var score = CombatRules.Distance(c, goal) + (melee && TacticalMoves.Airborne(sim, c) ? 2f : 0f)
                + (danger ? 0.5f * ThreatCost(sim, enc, e, here, range, c) : 0f);
            if (score < bestScore)
            {
                best = c;
                bestScore = score;
            }
        }
        return best;
    }

    /// <summary>The non-violent: get as far from the enemy as their TU allow, then end the turn.</summary>
    private static void KeepAway(Simulation sim, Encounter enc, CombatUnit u, List<Entity> enemies)
    {
        var e = u.Entity;
        var here = sim.World.Get<Position>(e).Cell;
        if (!u.Moved)
        {
            var best = here;
            var bestDist = NearestDistance(sim, here, enemies);
            foreach (var i in Sorted(enc.RangeOf(sim, e)))
            {
                var c = sim.Map.CellOf(i);
                var d = NearestDistance(sim, c, enemies) - (TacticalMoves.Airborne(sim, c) ? 1f : 0f);
                if (d > bestDist + 0.5f)
                {
                    best = c;
                    bestDist = d;
                }
            }
            if (best != here)
            {
                if (enc.Move(sim, e, best, fromAi: true) != null)
                    enc.Wait(sim, e);
                return;
            }
        }
        enc.Wait(sim, e);
    }

    /// <summary>Run for the map edge; off the map once there.</summary>
    private static void Flee(Simulation sim, Encounter enc, CombatUnit u, int3 here)
    {
        var map = sim.Map;
        if (!u.Fleeing)
        {
            u.Fleeing = true;
            enc.Write(sim, u.Side, $"{sim.World.Get<Creature>(u.Entity).Name} has had enough and tries to flee!");
        }
        if (here.X == 0 || here.Y == 0 || here.X == map.Width - 1 || here.Y == map.Height - 1)
        {
            enc.Flee(sim, u);
            return;
        }
        if (!u.Moved && sim.NearestEdge(here) is { } edge)
        {
            var cells = Sorted(enc.RangeOf(sim, u.Entity));
            var best = cells.Select(map.CellOf).MinBy(c => CombatRules.Distance(c, edge));
            if (best != here)
            {
                if (enc.Move(sim, u.Entity, best, fromAi: true) != null)
                    enc.Wait(sim, u.Entity);
                return;
            }
        }
        enc.Wait(sim, u.Entity);
    }

    private static float NearestDistance(Simulation sim, int3 c, List<Entity> others) =>
        others.Count == 0 ? float.MaxValue : others.Min(o => CombatRules.Distance(c, sim.World.Get<Position>(o).Cell));

    /// <summary>Endpoints in a fixed order, so the AI's choices are deterministic.</summary>
    private static List<int> Sorted(MoveRange range)
    {
        var list = range.Cells.ToList();
        list.Sort();
        return list;
    }
}
