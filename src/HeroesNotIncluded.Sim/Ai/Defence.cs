using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Maps;

namespace HeroesNotIncluded.Sim.Ai;

/// <summary>
/// What the town does when raiders or predators come at it: whoever's armed and fit stands and
/// fights, the unarmed out in the street gang up on a raider when there are enough of them
/// (<see cref="Mob"/>), and everyone else gets indoors, away from them. Adventurers, who fight for a living,
/// see trouble from further off. Players decide for themselves; this is for the AI.
/// </summary>
public static class Defence
{
    /// <summary>How near a raider or predator has to be before a villager does something about it, in cells.</summary>
    public const int VillagerRange = 10;

    /// <summary>The same for an adventurer: they go looking for the fight.</summary>
    public const int AdventurerRange = 16;

    /// <summary>How near a foe has to be before someone with only a makeshift weapon stands and fights it, in cells.</summary>
    public const int CorneredRange = 2;

    /// <summary>How clear of raiders a downed neighbour has to lie before someone goes out to stop their bleeding, in cells.</summary>
    public const int AidClear = 6;

    /// <summary>How far someone hiding will go to a downed neighbour, in cells.</summary>
    public const int AidRange = 24;

    /// <summary>How near a raider comes before someone gone out to a neighbour (<see cref="AidClear"/>) gets back under cover.</summary>
    public const int AidAbandon = 4;

    /// <summary>How near a raider the unarmed have to be to gang up on it (<see cref="Mob"/>), in cells.</summary>
    public const int MobRange = 5;

    /// <summary>How many of the town (the unarmed who'd join in and those already fighting it) it takes before the unarmed gang up on a raider.</summary>
    public const int MobSize = 3;

    /// <summary>How far off a dragon is everyone's business: the whole town hears it.</summary>
    public const int DragonRange = 96;

    /// <summary>
    /// The nearest raider or predator on its feet within <paramref name="range"/> cells (and two
    /// levels), or null. A dragon on the ground counts from anywhere in town (<see cref="DragonRange"/>).
    /// </summary>
    public static Entity? Threat(Simulation sim, Entity pawn, int range)
    {
        var w = sim.World;
        var tile = w.Get<Position>(pawn).Cell;
        Entity? best = null;
        var bestDist = int.MaxValue;
        foreach (var e in sim.Creatures())
        {
            var c = w.Get<Creature>(e);
            if (!Factions.Attacker(c.Faction) || c.Dead || c.Downed || w.Has<Leaving>(e))
                continue;
            var at = w.Get<Position>(e).Cell;
            if (Math.Abs(at.Z - tile.Z) > 2)
                continue;
            var d = Map.Distance(tile, at);
            if (d <= (c.Def.Race == Defs.Race.Dragon ? Math.Max(range, DragonRange) : range) && d < bestDist)
            {
                best = e;
                bestDist = d;
            }
        }
        return best;
    }

    /// <summary>Can this one fight: a weapon in hand, and not hurt so badly they'd rather run?</summary>
    public static bool CanFight(Simulation sim, Entity pawn) =>
        RaiderBrain.Armed(sim, pawn) && !RaiderBrain.WantsToFlee(sim, pawn) && sim.World.Get<Creature>(pawn).Able;

    /// <summary>Fight the foe if they can reach it, else get away from it: indoors if there's a door near, else just away.</summary>
    public static Job? Respond(Simulation sim, Entity pawn, Entity foe)
    {
        var w = sim.World;
        var tile = w.Get<Position>(pawn).Cell;
        var at = w.Get<Position>(foe).Cell;
        // A trade's makeshift weapon is for when they're cornered: further off, they get away.
        if (CanFight(sim, pawn) && sim.Regions.CanReach(tile, at, PathEnd.Touch)
            && (!Militia.MilitiaSystem.ArmedMakeshift(sim, pawn) || Map.Distance(tile, at) <= CorneredRange))
            return new DefendJob(foe);
        // Unarmed, but not alone: enough of the town round a raider gang up on it with their fists.
        if (Mob(sim, pawn, foe) && sim.Regions.CanReach(tile, at, PathEnd.Touch))
            return new DefendJob(foe);
        // Not fighting it: but a neighbour lying bleeding where no raider is gets seen to.
        if (Map.Distance(tile, at) > AidAbandon && Aid(sim, pawn) is { } aid)
            return aid;
        if (sim.Rooms.RoomAt(tile) is { Indoors: true } && Map.Distance(tile, at) > 2)
            return new HideJob();
        if (Shelter(sim, pawn, at) is { } inside)
            return new FleeJob(inside);
        return AnimalBrain.FleeSpot(sim, pawn, at, 12) is { } away ? new FleeJob(away) : null;
    }

    /// <summary>
    /// Would <paramref name="pawn"/>, unarmed, gang up on the raider <paramref name="foe"/>: out in
    /// the open within <see cref="MobRange"/> of it, and with enough of the town there to make
    /// <see cref="MobSize"/>, counting those already fighting it and the others who'd join in? A lone
    /// villager still gets away; a crowd doesn't all run from one raider. Not the dragon, not beasts.
    /// </summary>
    public static bool Mob(Simulation sim, Entity pawn, Entity foe)
    {
        if (!WouldMob(sim, pawn, foe))
            return false;
        var n = 1;
        foreach (var e in sim.Creatures())
            if (e != pawn && (FightingIt(sim, e, foe) || WouldMob(sim, e, foe)) && ++n >= MobSize)
                return true;
        return false;
    }

    /// <summary>Unarmed, unhurt and out in the street near the raider: one who'd join a mob on it.</summary>
    private static bool WouldMob(Simulation sim, Entity e, Entity foe)
    {
        var w = sim.World;
        if (w.Get<Creature>(e) is not { Able: true, Faction: Faction.Village or Faction.Adventurer } c || c.Def.Race != Defs.Race.Human
            || w.Has<Avatar>(e) || w.Has<NonViolent>(e) || RaiderBrain.Armed(sim, e) || FreeFight.Wary(sim, e) || RaiderBrain.WantsToFlee(sim, e))
            return false;
        if (w.Get<Creature>(foe) is not { Faction: Faction.Raider, Dead: false, Downed: false } || w.Has<Leaving>(foe))
            return false;
        var at = w.Get<Position>(e).Cell;
        var them = w.Get<Position>(foe).Cell;
        return Math.Abs(at.Z - them.Z) <= 1 && Map.Distance(at, them) <= MobRange && sim.Rooms.RoomAt(at) is not { Indoors: true };
    }

    /// <summary>On their feet near the raider and fighting it already: at it with a <see cref="DefendJob"/>, or a player with a weapon drawn.</summary>
    private static bool FightingIt(Simulation sim, Entity e, Entity foe)
    {
        var w = sim.World;
        if (w.Get<Creature>(e) is not { Able: true } c || !Factions.Defends(c.Faction)
            || Map.Distance(w.Get<Position>(e).Cell, w.Get<Position>(foe).Cell) > MobRange)
            return false;
        return w.Has<Avatar>(e) ? w.TryGet<FreeMover>(e, out var m) && m.Drawn
            : w.Get<Mind>(e).Job is DefendJob d && d.Focus == foe;
    }

    /// <summary>
    /// An indoor cell further from the foe than they are now, nearest them: behind walls, the
    /// raiders have to come through a door for them.
    /// </summary>
    public static int3? Shelter(Simulation sim, Entity pawn, int3 foe)
    {
        var tile = sim.World.Get<Position>(pawn).Cell;
        var from = Map.Distance(tile, foe);
        foreach (var c in sim.SpiralFrom(tile, 14))
        {
            if (Map.Distance(c, foe) <= from || !sim.Map.Passable(c) || !sim.Map.Standable(sim.Map.Idx(c)))
                continue;
            if (sim.Rooms.RoomAt(c) is not { Indoors: true })
                continue;
            if (Navigation.CanReach(sim, pawn, tile, c, PathEnd.OnTile))
                return c;
        }
        return null;
    }

    /// <summary>
    /// First aid with raiders about: a villager lying downed and bleeding, no raider within
    /// <see cref="AidClear"/> cells of them, gets their bleeding stopped where they lie. Without it
    /// the downed bleed out while the town hides, however long the raiders linger.
    /// </summary>
    public static Job? Aid(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        var from = w.Get<Position>(pawn).Cell;
        var best = Entity.Null;
        var bestDist = AidRange + 1;
        foreach (var e in sim.Creatures())
        {
            if (e == pawn || w.Get<Creature>(e) is not { Faction: Faction.Village, Downed: true, Dead: false } || w.Has<Carried>(e)
                || sim.Reservations.IsReservedByOther(e, pawn) || !w.TryGet<Health>(e, out var h) || !h.Injuries.Exists(i => Systems.Bandaging.Stanchable(h, i)))
                continue;
            var at = sim.LocationOf(e);
            var d = Map.Distance(from, at);
            if (d >= bestDist || Threat(sim, e, AidClear) != null || !sim.Regions.CanReach(from, at, PathEnd.Touch))
                continue;
            best = e;
            bestDist = d;
        }
        return best.IsNull ? null : new TendJob(best, firstAid: true, braving: true);
    }

    /// <summary>Is this job already the answer to trouble (fighting, getting away, or seeing to the fallen)?</summary>
    public static bool Responding(Job? job) => job is DefendJob or FleeJob or HideJob or TendJob { Braving: true };

    /// <summary>
    /// Should this answer to trouble be thought again now: hiding or running with a raider at their
    /// elbow and something in hand to fight with, running from a raider the town's ganging up on
    /// (<see cref="Mob"/>), or out to a neighbour with a raider coming near?
    /// </summary>
    public static bool Reconsider(Simulation sim, Entity pawn, Job? job) => job switch
    {
        HideJob => Threat(sim, pawn, CorneredRange) != null && CanFight(sim, pawn),
        // Out in the street running: back to join the rest if enough of the town is at the raider.
        FleeJob => (Threat(sim, pawn, CorneredRange) != null && CanFight(sim, pawn))
            || (Threat(sim, pawn, MobRange) is { } foe && Mob(sim, pawn, foe)),
        TendJob { Braving: true } => Threat(sim, pawn, AidAbandon) != null,
        _ => false,
    };
}

/// <summary>Indoors with trouble outside: keep still and out of sight a while, then look again.</summary>
public sealed class HideJob : Job
{
    protected override IEnumerable<JobStatus> Run()
    {
        Report = "Hiding indoors";
        for (var t = 0; t < 600; t++)
            yield return JobStatus.Running;
    }
}

/// <summary>
/// Stand and fight a raider or predator that's come into town (<see cref="FreeFight"/>): in to
/// reach, on their turn (<see cref="Engagement"/>), each blow wound up where it's seen coming (<see cref="Blows"/>). Ends when it falls, flees or steps away (the brain sends
/// them after it again if it's still near), or when they're hurt enough to want out.
/// </summary>
public sealed class DefendJob(Entity foe) : Job
{
    public override Entity Focus => foe;

    protected override IEnumerable<JobStatus> Run()
    {
        Report = $"Fighting {Get<Creature>(foe).Name}";
        // What they hold, as a raider fights (AssaultJob): a bowman keeps their distance and shoots.
        if (Combat.CombatRules.Attacks(Sim, Pawn).FirstOrDefault() is not { } attack)
        {
            Fail("nothing to fight with");
            yield break;
        }
        foreach (var s in FightFree(foe, attack, () => !Sim.World.Has<Leaving>(foe) && !RaiderBrain.WantsToFlee(Sim, Pawn), AssaultJob.HeavyShare,
            giveUp: Defence.VillagerRange + 4))
            yield return s;
    }
}
