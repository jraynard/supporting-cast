using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;

namespace HeroesNotIncluded.Sim.Systems;

/// <summary>
/// Who can carry whom to bed when they're down (<see cref="RescueJob"/>): a person carries anyone
/// no bigger than a person (up to <see cref="MaxCarrySize"/>) to a bed. Anything bigger (the dragon)
/// is too big for anyone: it's tended where it lies.
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

    /// <summary>
    /// The bed <paramref name="carrier"/> can take <paramref name="patient"/> to, or Entity.Null: a
    /// shrine's pallet if one's free (where the empath sees to the hurt, and nobody sleeps the night
    /// on it), else the nearest free bed.
    /// </summary>
    public static Entity BedFor(Simulation sim, Entity carrier, Entity patient)
    {
        var w = sim.World;
        var c = w.Get<Creature>(patient);
        var bed = c.Faction == Faction.Prisoner ? Entity.Null : FreePallet(sim, carrier);
        if (bed.IsNull)
            bed = AiQueries.FindFreeBed(sim, patient, prisoner: c.Faction == Faction.Prisoner);
        // The patient's own reach may count the sky (a flyer's); the one carrying has to get there.
        return !bed.IsNull && Navigation.CanReach(sim, carrier, w.Get<Position>(carrier).Cell, w.Get<Thing>(bed).Cell, PathEnd.OnTile) ? bed : Entity.Null;
    }

    /// <summary>The nearest free shrine pallet (<see cref="Shops.ShopSystem.IsForPatients"/>) the carrier can get to, or Entity.Null.</summary>
    private static Entity FreePallet(Simulation sim, Entity carrier)
    {
        var w = sim.World;
        var from = w.Get<Position>(carrier).Cell;
        var best = Entity.Null;
        var bestDist = int.MaxValue;
        foreach (var e in w.Query<Thing>())
        {
            var t = w.Get<Thing>(e);
            if (t.Def.Building is not { IsBed: true } || w.Has<Construction>(e) || !Shops.ShopSystem.IsForPatients(sim, e)
                || sim.Reservations.IsReservedByOther(e, carrier) || LainOn(sim, t.Cell))
                continue;
            var d = Maps.Map.Distance(from, t.Cell);
            if (d < bestDist && Navigation.CanReach(sim, carrier, from, t.Cell, PathEnd.OnTile))
            {
                best = e;
                bestDist = d;
            }
        }
        return best;
    }

    /// <summary>Someone's lying in this cell already (a patient on the pallet, a customer waiting to be seen to).</summary>
    private static bool LainOn(Simulation sim, int3 cell)
    {
        foreach (var e in sim.Creatures())
            if (sim.World.Get<Position>(e).Cell == cell && !sim.World.Has<Carried>(e))
                return true;
        return false;
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
    /// What the player's character can do with the creature in their arms at a cell: lay a person in a
    /// bed, set a carcass down by a butcher spot, or just put it down.
    /// </summary>
    public static AvatarAction SetDownAt(Simulation sim, Entity carried, int3 cell)
    {
        var w = sim.World;
        var map = sim.Map;
        if (!map.InBounds(cell))
            return AvatarAction.None;
        var c = w.Get<Creature>(carried);
        var site = map.Structure[map.Idx(cell)];
        if (!site.IsNull && !w.Has<Construction>(site) && w.Get<Thing>(site).Def.Building is { IsBed: true } b
            && !w.Has<ForPrisoners>(site) && (!c.Dead || (w.Has<Afterlife.Death>(carried) && Buildings.At(sim, cell) is { IsNull: false } at
                && w.Get<PresetBuilding>(at).Def.Name == "Shrine")))
            // The dead too, on a shrine's pallet: a body keeps longer there for the empath (Afterlife.Revival).
            return new(AvatarActionKind.Carry, c.Dead ? $"Lay {c.Name} out on the {w.Get<Thing>(site).Label}" : $"Lay {c.Name} in the {w.Get<Thing>(site).Label}", cell, carried);
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
