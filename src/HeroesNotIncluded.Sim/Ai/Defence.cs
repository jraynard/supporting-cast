using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Maps;

namespace HeroesNotIncluded.Sim.Ai;

/// <summary>
/// What the town does when raiders or predators come at it: whoever's armed and fit stands and
/// fights, and everyone else gets indoors, away from them. Adventurers, who fight for a living,
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
        if (sim.Rooms.RoomAt(tile) is { Indoors: true } && Map.Distance(tile, at) > 2)
            return new HideJob();
        if (Shelter(sim, pawn, at) is { } inside)
            return new FleeJob(inside);
        return AnimalBrain.FleeSpot(sim, pawn, at, 12) is { } away ? new FleeJob(away) : null;
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

    /// <summary>Is this job already the answer to trouble (fighting, or getting away)?</summary>
    public static bool Responding(Job? job) => job is DefendJob or FleeJob or HideJob;
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
        if (Combat.CombatRules.Attacks(Sim, Pawn).FirstOrDefault(a => !a.Ranged) is not { } attack)
        {
            Fail("nothing to fight with");
            yield break;
        }
        foreach (var s in FightFree(foe, attack, () => !Sim.World.Has<Leaving>(foe) && !RaiderBrain.WantsToFlee(Sim, Pawn), AssaultJob.HeavyShare,
            giveUp: Defence.VillagerRange + 4))
            yield return s;
    }
}
