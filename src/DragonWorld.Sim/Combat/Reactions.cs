using DragonWorld.Sim.Components;
using DragonWorld.Sim.Defs;

namespace DragonWorld.Sim.Combat;

public enum ReactionKind
{
    /// <summary>
    /// Watching, paid from saved TU, one a phase: a snap shot at an enemy stepping into range and
    /// sight, or with a melee weapon, a swing at one stepping into reach.
    /// </summary>
    Overwatch,

    /// <summary>A free swing at an enemy stepping out of reach.</summary>
    Opportunity,

    /// <summary>Answering an attack on yourself: a swing or a snap shot at whoever just attacked (or breathed fire on) you, in reach or sight. One a phase.</summary>
    Response,

    /// <summary>
    /// A devoted dragon guarding its keeper unasked: a swing at an enemy stepping in beside the
    /// keeper, within the dragon's reach (paid from its TU, like any reaction).
    /// </summary>
    Guard,

    /// <summary>
    /// A soulbound dragon answering whoever hurt its keeper: in reach or sight, even with no TU
    /// kept back, and without the reaction penalty (fury, not caution).
    /// </summary>
    Defend,
}

/// <summary>A reaction a move would draw: who, where along the path, and at whom.</summary>
public readonly record struct Threat(Entity Reactor, ReactionKind Kind, int Step, int3 Cell, Entity Target, AttackDef Attack, float Chance, float Expected);

/// <summary>
/// Reactions during the other side's phase, paid from the TU a unit kept back from its own turn
/// (Xenonauts style): a unit with the TU for an attack strikes at an enemy that steps from a cell
/// it couldn't hit into one it can (overwatch, once a phase): a snap shot as it comes into range
/// and sight, or a swing as it steps into reach (the bow first, if it has both); one with a melee
/// attack and the TU for it also swings at an enemy stepping out of its reach;
/// and a unit that has just been attacked (or breathed on) answers its attacker, with a swing if
/// it's in reach or a snap shot if it's in sight (<see cref="AfterAttack"/>, once a phase). Each
/// unit reacts at most once to any one action, with the odds scaled by
/// <see cref="CombatRules.ReactionFactor"/> (and a snap shot's own); swings at passers-by go on
/// while its TU last. Dragons bonded deeply enough add two of their own (<see cref="Systems.Bonding"/>
/// levels): a devoted one strikes at an enemy stepping in beside its keeper
/// (<see cref="ReactionKind.Guard"/>), a soulbound one answers an attack on its keeper whatever
/// TU it has left (<see cref="ReactionKind.Defend"/>). <see cref="Encounter.Animate"/> calls
/// <see cref="BeforeStep"/> and <see cref="Entered"/> as a move plays out, so the game and
/// headless fights roll the same reactions in the same order; <see cref="Threats"/> predicts them
/// without rolling, for the move preview and the AI.
/// </summary>
public static class Reactions
{
    /// <summary>The mover is about to step from <paramref name="from"/> to <paramref name="next"/>: attacks of opportunity.</summary>
    internal static void BeforeStep(Simulation sim, Encounter enc, Entity mover, int3 from, int3 next)
    {
        foreach (var r in Reactors(sim, enc, mover))
        {
            if (!Standing(sim, mover) || !enc.Active)
                return;
            if (Opportunity(sim, r, from, next) is not { } attack)
                continue;
            if (BestTarget(sim, enc, r.Entity, Cell(sim, r.Entity), mover, from, attack) is { } t)
                Fire(sim, enc, r, t, attack, ReactionKind.Opportunity);
        }
    }

    /// <summary>The mover has moved from <paramref name="from"/> into <paramref name="cell"/>: guards, and overwatch shots at it coming into view.</summary>
    internal static void Entered(Simulation sim, Encounter enc, Entity mover, int3 from, int3 cell)
    {
        foreach (var r in Reactors(sim, enc, mover))
        {
            if (!Standing(sim, mover) || !enc.Active)
                return;
            if (Guarding(sim, r, cell) is { } swing && BestTarget(sim, enc, r.Entity, Cell(sim, r.Entity), mover, cell, swing) is { } g)
            {
                Fire(sim, enc, r, g, swing, ReactionKind.Guard);
                continue;
            }
            foreach (var attack in Watching(sim, r))
                if (Overwatch(sim, enc, r, mover, from, cell, attack) is { } t)
                {
                    Fire(sim, enc, r, t, attack, ReactionKind.Overwatch);
                    break;
                }
        }
    }

    /// <summary>
    /// <paramref name="attacker"/> has just attacked or breathed fire on <paramref name="victims"/>:
    /// each victim that can hit it back, has the TU and hasn't answered yet this phase does (in unit
    /// order). A swing if it's in reach, else a snap shot if it's in range and sight, whichever would
    /// hurt more. A soulbound dragon whose keeper was hit answers too (<see cref="Defends"/>).
    /// </summary>
    internal static void AfterAttack(Simulation sim, Encounter enc, Entity attacker, IReadOnlyCollection<Entity> victims)
    {
        foreach (var r in Reactors(sim, enc, attacker))
        {
            if (!Standing(sim, attacker) || !enc.Active)
                return;
            var defend = Defends(sim, r, victims);
            if (!defend && !Responds(r, victims))
                continue;
            if (Answer(sim, enc, r, attacker, Cell(sim, attacker), free: defend) is { } answer)
                Fire(sim, enc, r, answer.Target, answer.Attack, defend ? ReactionKind.Defend : ReactionKind.Response, free: defend);
        }
    }

    /// <summary>Was this unit among those just attacked, with its answer for the phase still to give?</summary>
    private static bool Responds(CombatUnit r, IReadOnlyCollection<Entity> victims) => !r.ResponseUsed && victims.Contains(r.Entity);

    /// <summary>
    /// Whom the watcher would strike as <paramref name="mover"/> steps from <paramref name="from"/> to
    /// <paramref name="cell"/>: only if it hasn't reacted on overwatch this phase, and the step brings
    /// the mover within the attack (into range and sight for a bow, into reach for a blade: it
    /// couldn't be hit where it was).
    /// </summary>
    private static (Entity Target, HitChance Odds)? Overwatch(Simulation sim, Encounter enc, CombatUnit r, Entity mover, int3 from, int3 cell, AttackDef bow)
    {
        if (r.OverwatchUsed)
            return null;
        var at = Cell(sim, r.Entity);
        if (BestTarget(sim, enc, r.Entity, at, mover, cell, bow) is not { } t)
            return null;
        return BestTarget(sim, enc, r.Entity, at, mover, from, bow) is null ? t : null;
    }

    /// <summary>A soulbound dragon whose keeper was among those just attacked: it answers whatever its TU.</summary>
    private static bool Defends(Simulation sim, CombatUnit r, IReadOnlyCollection<Entity> victims)
    {
        var w = sim.World;
        return victims.Count > 0 && w.TryGet<Dragon>(r.Entity, out var d) && w.IsAlive(d.Keeper) && victims.Contains(d.Keeper)
            && Systems.Bonding.Reached(sim, r.Entity, Systems.Bonding.Soulbound);
    }

    /// <summary>
    /// A devoted dragon's swing at whoever steps into <paramref name="cell"/>, beside its keeper and
    /// in the dragon's own reach, if it has the TU for it; else null.
    /// </summary>
    private static AttackDef? Guarding(Simulation sim, CombatUnit r, int3 cell)
    {
        var w = sim.World;
        if (!w.TryGet<Dragon>(r.Entity, out var d) || !w.IsAlive(d.Keeper) || !w.Get<Creature>(d.Keeper).Able
            || !Systems.Bonding.Reached(sim, r.Entity, Systems.Bonding.Devoted))
            return null;
        var at = Cell(sim, r.Entity);
        if (!CombatRules.InReach(sim, Cell(sim, d.Keeper), cell) || !CombatRules.InReach(sim, at, cell))
            return null;
        return CombatRules.Attacks(sim, r.Entity).Where(a => !a.Ranged).MaxBy(a => a.DamageMin + a.DamageMax) is { } claws
            && r.TimeUnits + 1e-3f >= TimeUnits.AttackCost(sim, r.Entity, claws, ShotMode.Normal) ? claws : null;
    }

    /// <summary>
    /// The answers an attack by <paramref name="attacker"/> from <paramref name="from"/> would draw,
    /// assuming it stays standing: <paramref name="victim"/>'s own answer, and a soulbound dragon's if
    /// it's the keeper. Pure: no rolls, no changes (the attack tooltip, the AI).
    /// </summary>
    public static List<Threat> Answers(Simulation sim, Encounter enc, Entity attacker, int3 from, Entity victim = default)
    {
        var victims = victim.IsNull ? [] : new[] { victim };
        var list = new List<Threat>();
        if (!enc.Active)
            return list;
        foreach (var r in enc.Units.ToList())
        {
            if (enc.Unit(attacker) is not { } m || r.Side == m.Side || r.Fleeing || !Standing(sim, r.Entity))
                continue;
            var defend = Defends(sim, r, victims);
            if (!defend && !Responds(r, victims))
                continue;
            if (Answer(sim, enc, r, attacker, from, free: defend) is { } a)
                list.Add(new Threat(r.Entity, defend ? ReactionKind.Defend : ReactionKind.Response, 0, from, a.Target.Target, a.Attack, a.Target.Odds.Chance, CombatRules.Expected(a.Target.Odds, a.Attack)));
        }
        return list;
    }

    /// <summary>
    /// The reactor's best answer to <paramref name="attacker"/> it has the TU for (any, when
    /// <paramref name="free"/>: a soulbound dragon defending its keeper, at full odds): the attack and
    /// whom it'd hit (the attacker or its rider/mount).
    /// </summary>
    private static ((Entity Target, HitChance Odds) Target, AttackDef Attack)? Answer(Simulation sim, Encounter enc, CombatUnit r, Entity attacker, int3 there, bool free = false)
    {
        var at = Cell(sim, r.Entity);
        ((Entity, HitChance), AttackDef)? best = null;
        var bestScore = 0f;
        foreach (var a in CombatRules.Attacks(sim, r.Entity))
        {
            if (!free && r.TimeUnits + 1e-3f < TimeUnits.AttackCost(sim, r.Entity, a, a.Ranged ? ShotMode.Snap : ShotMode.Normal))
                continue;
            if (!a.Ranged && !CombatRules.InReach(sim, at, there))
                continue;
            if (BestTarget(sim, enc, r.Entity, at, attacker, there, a, reaction: !free) is not { } t)
                continue;
            var score = CombatRules.Expected(t.Odds, a);
            if (score > bestScore)
            {
                best = (t, a);
                bestScore = score;
            }
        }
        return best;
    }

    /// <summary>
    /// The reactions moving <paramref name="mover"/> along <paramref name="path"/> (from
    /// <paramref name="start"/>) would draw, assuming it keeps going. Pure: no rolls, no changes.
    /// </summary>
    public static List<Threat> Threats(Simulation sim, Encounter enc, Entity mover, int3 start, IReadOnlyList<int3> path)
    {
        var list = new List<Threat>();
        if (!enc.Active || path.Count == 0)
            return list;
        var reactors = Reactors(sim, enc, mover).ToList();
        if (reactors.Count == 0)
            return list;
        var used = new HashSet<Entity>();
        var from = start;
        // (Each reactor reacts once to a move, so its TU only need cover the one reaction.)
        for (var i = 0; i < path.Count; i++)
        {
            var next = path[i];
            foreach (var r in reactors)
                if (!used.Contains(r.Entity) && Opportunity(sim, r, from, next) is { } swing
                    && BestTarget(sim, enc, r.Entity, Cell(sim, r.Entity), mover, from, swing) is { } t)
                {
                    used.Add(r.Entity);
                    list.Add(new Threat(r.Entity, ReactionKind.Opportunity, i, from, t.Target, swing, t.Odds.Chance, CombatRules.Expected(t.Odds, swing)));
                }
            foreach (var r in reactors)
                if (!used.Contains(r.Entity) && Guarding(sim, r, next) is { } claws
                    && BestTarget(sim, enc, r.Entity, Cell(sim, r.Entity), mover, next, claws) is { } g)
                {
                    used.Add(r.Entity);
                    list.Add(new Threat(r.Entity, ReactionKind.Guard, i, next, g.Target, claws, g.Odds.Chance, CombatRules.Expected(g.Odds, claws)));
                }
            foreach (var r in reactors)
            {
                if (used.Contains(r.Entity))
                    continue;
                foreach (var shot in Watching(sim, r))
                    if (Overwatch(sim, enc, r, mover, from, next, shot) is { } t)
                    {
                        used.Add(r.Entity);
                        list.Add(new Threat(r.Entity, ReactionKind.Overwatch, i, next, t.Target, shot, t.Odds.Chance, CombatRules.Expected(t.Odds, shot)));
                        break;
                    }
            }
            from = next;
        }
        return list;
    }

    /// <summary>Units that may still react to this mover: the other side's able units that haven't reacted to this move, in index order.</summary>
    private static IEnumerable<CombatUnit> Reactors(Simulation sim, Encounter enc, Entity mover)
    {
        if (enc.Unit(mover) is not { } m)
            yield break;
        var w = sim.World;
        foreach (var u in enc.Units.ToList())
            if (u.Side != m.Side && !enc.ReactedThisMove.Contains(u.Entity) && !u.Fleeing && w.IsAlive(u.Entity) && w.Get<Creature>(u.Entity).Able)
                yield return u;
    }

    /// <summary>
    /// What the unit watches with, in the order it'd use them, each only if it has the TU: its
    /// longest-reaching ranged attack (a snap shot), then its best melee attack (a swing at whoever
    /// steps into reach).
    /// </summary>
    public static IEnumerable<AttackDef> Watching(Simulation sim, CombatUnit r)
    {
        var attacks = CombatRules.Attacks(sim, r.Entity);
        if (attacks.Where(a => a.Ranged).MaxBy(a => a.Range) is { } bow && r.TimeUnits + 1e-3f >= TimeUnits.AttackCost(sim, r.Entity, bow, ShotMode.Snap))
            yield return bow;
        if (attacks.Where(a => !a.Ranged).MaxBy(a => a.DamageMin + a.DamageMax) is { } blade && r.TimeUnits + 1e-3f >= TimeUnits.AttackCost(sim, r.Entity, blade, ShotMode.Normal))
            yield return blade;
    }

    /// <summary>The unit's best melee attack, if it has the TU for it and a step from <paramref name="from"/> to <paramref name="next"/> leaves its reach.</summary>
    private static AttackDef? Opportunity(Simulation sim, CombatUnit r, int3 from, int3 next)
    {
        var at = Cell(sim, r.Entity);
        if (!CombatRules.InReach(sim, at, from) || CombatRules.InReach(sim, at, next))
            return null;
        return CombatRules.Attacks(sim, r.Entity).Where(a => !a.Ranged).MaxBy(a => a.DamageMin + a.DamageMax) is { } blade
            && r.TimeUnits + 1e-3f >= TimeUnits.AttackCost(sim, r.Entity, blade, ShotMode.Normal) ? blade : null;
    }

    /// <summary>Can this unit react at all right now (TU for its cheapest reaction)? For the HUD and the AI.</summary>
    public static bool CanReact(Simulation sim, CombatUnit u) =>
        TimeUnits.CheapestAttack(sim, u.Entity) is { } cost && u.TimeUnits + 1e-3f >= cost;

    /// <summary>The mover, or the one riding it, whichever the reactor would hurt more, with the mover at <paramref name="cell"/>.</summary>
    private static (Entity Target, HitChance Odds)? BestTarget(Simulation sim, Encounter enc, Entity reactor, int3 at, Entity mover, int3 cell, AttackDef attack, bool reaction = true)
    {
        var w = sim.World;
        (Entity, HitChance)? best = null;
        var bestScore = -1f;
        foreach (var t in (Entity[])[mover, w.TryGet<Mount>(mover, out var m) && w.IsAlive(m.Rider) && enc.Unit(m.Rider) != null ? m.Rider : Entity.Null])
        {
            if (t.IsNull || !w.Get<Creature>(t).Able)
                continue;
            var odds = CombatRules.Chance(sim, reactor, at, t, cell, attack, reaction: reaction);
            if (!odds.Possible)
                continue;
            var score = CombatRules.Expected(odds, attack);
            if (score > bestScore)
            {
                best = (t, odds);
                bestScore = score;
            }
        }
        return best;
    }

    private static void Fire(Simulation sim, Encounter enc, CombatUnit r, (Entity Target, HitChance Odds) t, AttackDef attack, ReactionKind kind, bool free = false)
    {
        if (!free)
            r.TimeUnits = Math.Max(0, r.TimeUnits - TimeUnits.AttackCost(sim, r.Entity, attack, attack.Ranged ? ShotMode.Snap : ShotMode.Normal));
        var result = CombatRules.Resolve(sim, r.Entity, t.Target, attack, reaction: !free);
        var name = sim.World.Get<Creature>(r.Entity).Name;
        var keeper = sim.World.TryGet<Dragon>(r.Entity, out var d) && sim.World.IsAlive(d.Keeper) ? sim.World.Get<Creature>(d.Keeper).Name : "its keeper";
        enc.Write(sim, r.Side, $"{name} {kind switch
        {
            ReactionKind.Overwatch => attack.Ranged ? "fires on overwatch" : "strikes on overwatch",
            ReactionKind.Response => "answers the attack",
            ReactionKind.Guard => $"guards {keeper}",
            ReactionKind.Defend => $"flies at whoever hurt {keeper}",
            _ => "strikes as they pass",
        }}: {result.Describe(sim)}");
        sim.RecentShots.Add(new ShotEvent(sim.Clock.Tick, sim.World.Get<Position>(r.Entity).Pos, sim.World.Get<Position>(t.Target).Pos, attack.Ranged, result.Outcome == AttackOutcome.Hit));
        enc.Reacted(sim, r.Entity, t.Target, kind);
    }

    private static bool Standing(Simulation sim, Entity e) => sim.World.IsAlive(e) && sim.World.Get<Creature>(e).Able;

    private static int3 Cell(Simulation sim, Entity e) => sim.World.Get<Position>(e).Cell;
}
