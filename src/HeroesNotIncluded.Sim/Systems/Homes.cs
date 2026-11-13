using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Maps;

namespace HeroesNotIncluded.Sim.Systems;

/// <summary>
/// Where the townsfolk sleep: a round deals each of them a home along with their trade, a bed in a
/// cottage (<see cref="BuildingDef.Home"/>) that's theirs (<see cref="BedOwner"/>), and nobody
/// else sleeps in it. Everyone gets a cottage to themselves while there are cottages to go round,
/// the nearest to their work; then they share. The inn's staff choose last; if there's no cottage
/// bed left for them, they get a room at the inn, which is never let.
/// </summary>
public static class Homes
{
    /// <summary>Deal homes to everyone of the village who hasn't one. Deterministic: entity order, then distance.</summary>
    public static void DealAll(Simulation sim)
    {
        var w = sim.World;
        var people = sim.Creatures()
            .Where(e => w.Get<Creature>(e) is { Faction: Faction.Village, Def.Race: Race.Human, Dead: false } && !Housed(sim, e))
            .ToList();
        // The inn's staff last: they've a roof at work if it comes to it.
        foreach (var e in people.Where(e => !AtTheInn(sim, e)).Concat(people.Where(e => AtTheInn(sim, e))))
            Give(sim, e);
    }

    /// <summary>A home for one: an empty cottage, else a free bed in one, else (inn staff) a room at the inn. False if none.</summary>
    public static bool Give(Simulation sim, Entity who)
    {
        if (Housed(sim, who))
            return true;
        var w = sim.World;
        var near = WorkOf(sim, who) ?? w.Get<Position>(who).Cell;
        var taken = Owned(sim);
        var cottages = w.Query<PresetBuilding>()
            .Where(b => w.Get<PresetBuilding>(b) is { Def.Home: true, Phase: BuildPhase.Done, Demolishing: false })
            .OrderBy(b => Map.Distance(w.Get<PresetBuilding>(b).Anchor, near))
            .ToList();
        var bed = Entity.Null;
        // A cottage to themselves first, then a spare bed in any.
        foreach (var empty in new[] { true, false })
        {
            foreach (var b in cottages)
            {
                var beds = BedsIn(sim, b);
                if (empty && beds.Any(taken.Contains))
                    continue;
                bed = beds.FirstOrDefault(x => !taken.Contains(x), Entity.Null);
                if (!bed.IsNull)
                    break;
            }
            if (!bed.IsNull)
                break;
        }
        // Inn staff with no cottage bed left: a room at the inn, kept from guests.
        if (bed.IsNull && AtTheInn(sim, who) && w.Get<Trade>(who).Def.WorkplaceDef is { } inn)
            bed = Shops.ShopSystem.FreeLetBed(sim, Shops.ShopSystem.BuildingOf(sim, inn));
        if (bed.IsNull)
            return false;
        if (w.Has<BedOwner>(who))
            w.Remove<BedOwner>(who);
        w.Add(who, new BedOwner { Bed = bed });
        return true;
    }

    /// <summary>The cottage (or inn room) someone sleeps in, or Entity.Null.</summary>
    public static Entity HomeOf(Simulation sim, Entity who) =>
        sim.World.TryGet<BedOwner>(who, out var own) && sim.World.IsAlive(own.Bed) ? Buildings.Of(sim, own.Bed) : Entity.Null;

    private static bool Housed(Simulation sim, Entity who) =>
        sim.World.TryGet<BedOwner>(who, out var own) && sim.World.IsAlive(own.Bed);

    private static bool AtTheInn(Simulation sim, Entity who) =>
        sim.World.TryGet<Trade>(who, out var t) && t.Def.WorkplaceDef is { LetsBeds: true };

    private static int3? WorkOf(Simulation sim, Entity who)
    {
        if (!sim.World.TryGet<Trade>(who, out var t) || t.Def.WorkplaceDef is not { } def)
            return null;
        var b = Shops.ShopSystem.BuildingOf(sim, def);
        return b.IsNull ? null : sim.World.Get<PresetBuilding>(b).Anchor;
    }

    private static List<Entity> BedsIn(Simulation sim, Entity building)
    {
        var w = sim.World;
        return Buildings.Parts(sim, building)
            .Where(p => w.Get<Thing>(p).Def.Building is { IsBed: true } && !w.Has<Construction>(p) && !w.Has<ForPrisoners>(p))
            .ToList();
    }

    /// <summary>Beds someone living owns.</summary>
    private static HashSet<Entity> Owned(Simulation sim)
    {
        var w = sim.World;
        var owned = new HashSet<Entity>();
        foreach (var o in w.Each<BedOwner>())
            if (!(w.TryGet<Creature>(o, out var c) && c.Dead))
                owned.Add(w.Get<BedOwner>(o).Bed);
        return owned;
    }
}
