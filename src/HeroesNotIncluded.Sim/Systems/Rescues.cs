using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;

namespace HeroesNotIncluded.Sim.Systems;

/// <summary>
/// Who can carry whom to bed when they're down (<see cref="RescueJob"/>): a person carries another
/// person, or a dragon no bigger than a person can drag (a hatchling or juvenile, up to
/// <see cref="MaxCarrySize"/>); a grown dragon carries anyone up to its own size,
/// its keeper above all. People go to a bed, dragons to a nest. A downed adult dragon is too big
/// for anyone: it's tended and fed where it lies (Doctor work) and mends faster by a fire.
/// </summary>
public static class Rescues
{
    public static bool CanCarry(Simulation sim, Entity carrier, Entity patient)
    {
        var w = sim.World;
        var by = w.Get<Creature>(carrier);
        var them = w.Get<Creature>(patient);
        return them.Def.Race == Race.Human && by.Def.Race == Race.Human && by.Able;
    }

    /// <summary>The bed <paramref name="carrier"/> can take <paramref name="patient"/> to, or Entity.Null.</summary>
    public static Entity BedFor(Simulation sim, Entity carrier, Entity patient)
    {
        var w = sim.World;
        var c = w.Get<Creature>(patient);
        var bed = AiQueries.FindFreeBed(sim, patient, prisoner: c.Faction == Faction.Prisoner);
        // The patient's own reach may count the sky (a flyer's); the one carrying has to get there.
        return !bed.IsNull && Navigation.CanReach(sim, carrier, w.Get<Position>(carrier).Cell, w.Get<Thing>(bed).Cell, PathEnd.OnTile) ? bed : Entity.Null;
    }

    /// <summary>Who or what <paramref name="carrier"/> has in their arms (a <see cref="Carried"/> creature), or Entity.Null.</summary>
    public static Entity CarriedBy(Simulation sim, Entity carrier)
    {
        var w = sim.World;
        foreach (var e in w.Each<Carried>())
            if (w.Get<Carried>(e).By == carrier)
                return e;
        return Entity.Null;
    }

    /// <summary>A downed person or dragon lying in a cell (not <paramref name="except"/>), or Entity.Null.</summary>
    public static Entity DownedAt(Simulation sim, int3 cell, Entity except)
    {
        var w = sim.World;
        foreach (var e in w.Each<Creature>())
        {
            var c = w.Get<Creature>(e);
            if (e != except && c.Downed && !c.Dead && c.Def.Race is Race.Human or Race.Dragon && w.Get<Position>(e).Cell == cell && !w.Has<Carried>(e))
                return e;
        }
        return Entity.Null;
    }

    /// <summary>
    /// What the player's character can do with the creature in their arms at a cell: lay a dragon in a
    /// nest (a person in a bed), set a carcass down by a butcher spot, or just put it down.
    /// </summary>
    public static AvatarAction SetDownAt(Simulation sim, Entity carried, int3 cell)
    {
        var w = sim.World;
        var map = sim.Map;
        if (!map.InBounds(cell))
            return AvatarAction.None;
        var c = w.Get<Creature>(carried);
        var site = map.Structure[map.Idx(cell)];
        if (!c.Dead && !site.IsNull && !w.Has<Construction>(site) && w.Get<Thing>(site).Def.Building is { IsBed: true } b
            && !w.Has<ForPrisoners>(site))
            return new(AvatarActionKind.Carry, $"Lay {c.Name} in the {w.Get<Thing>(site).Label}", cell, carried);
        if (!map.Standable(map.Idx(cell)) || !map.Passable(cell))
            return AvatarAction.None;
        if (c.Dead && !ButcherSpots.SpotAt(sim, cell).IsNull)
            return new(AvatarActionKind.Carry, $"Set the {c.Def.Label} down by the butcher spot", cell, carried);
        return new(AvatarActionKind.Carry, c.Dead ? $"Put the {c.Def.Label} down" : $"Put {c.Name} down", cell, carried);
    }

    /// <summary>Pick a creature up (empty arms, in reach), or set down the one carried at <paramref name="cell"/>.</summary>
    public static void Carry(Simulation sim, Entity carrier, Entity target, int3 cell)
    {
        var w = sim.World;
        if (!w.IsAlive(target) || !w.Has<Creature>(target))
            return;
        if (w.TryGet<Carried>(target, out var held))
        {
            if (held.By != carrier)
                return;
            w.Remove<Carried>(target);
            w.Get<Position>(target) = Position.At(cell);
            if (!w.Get<Creature>(target).Dead)
                MoodSystem.Remember(sim, target, sim.Defs.Thoughts.RescuedMe, carrier);
            return;
        }
        if (!CarriedBy(sim, carrier).IsNull || w.Get<Carrying>(carrier).HasItem)
            return;
        w.Get<Mover>(target).Stop();
        w.Add(target, new Carried { By = carrier });
    }

    /// <summary>Put down whoever <paramref name="carrier"/> holds, where they stand (they went down, or died).</summary>
    public static void Drop(Simulation sim, Entity carrier)
    {
        var w = sim.World;
        if (CarriedBy(sim, carrier) is { IsNull: false } load)
        {
            w.Remove<Carried>(load);
            w.Get<Position>(load) = Position.At(w.Get<Position>(carrier).Cell);
        }
    }
}
