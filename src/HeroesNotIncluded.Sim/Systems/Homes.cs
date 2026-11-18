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
        if (bed.IsNull && AtTheInn(sim, who))
            bed = Shops.ShopSystem.FreeLetBed(sim, Shops.ShopSystem.WorkplaceOf(sim, who));
        if (bed.IsNull)
            return false;
        if (w.Has<BedOwner>(who))
            w.Remove<BedOwner>(who);
        w.Add(who, new BedOwner { Bed = bed });
        return true;
    }

    /// <summary>
    /// The bread crock in every cottage: plain food for whoever lives there, topped up by the carter
    /// each morning, eaten by hand (F at it). No job takes from it (<see cref="Simulation.IsAvailable(Entity)"/>):
    /// it's not the cook's to serve or a hauler's to carry off.
    /// </summary>
    public const string Crock = "BreadCrock";

    /// <summary>What the carter leaves in each crock, topped up to (not added to) each morning.</summary>
    public static readonly (string Def, int Count)[] CrockFood = [("Bread", 3), ("Berries", 8)];

    public static bool IsCrock(Simulation sim, Entity e) =>
        !e.IsNull && sim.World.IsAlive(e) && sim.World.TryGet<Thing>(e, out var t) && t.Def.Name == Crock && !sim.World.Has<Construction>(e);

    /// <summary>Something to eat from their own home's crock for someone peckish enough, or Entity.Null: bread first.</summary>
    public static Entity FromCrock(Simulation sim, Entity who, Entity crock)
    {
        var w = sim.World;
        if (!IsCrock(sim, crock) || HomeOf(sim, who) != Buildings.Of(sim, crock))
            return Entity.Null;
        var eater = w.Get<Creature>(who).Def;
        // Only what would do them good: a loaf after a hearty supper wouldn't (WellBeing).
        return sim.StoredIn(crock)
            .Where(e => w.Get<Thing>(e).Def is { IsFood: true } d && d.EdibleBy(eater) && !sim.Reservations.IsReservedByOther(e, who)
                && WellBeing.WouldFeed(sim, who, WellBeing.FedStrength(d, Meal.Of(sim, e), knowRot: false)))
            .OrderByDescending(e => w.Get<Thing>(e).Def.Cooked).FirstOrDefault(Entity.Null);
    }

    /// <summary>Top every cottage's crock up to <see cref="CrockFood"/>: at the deal, and with the carter each morning.</summary>
    public static void StockCrocks(Simulation sim)
    {
        var w = sim.World;
        foreach (var e in w.Query<Thing>())
        {
            if (!IsCrock(sim, e) || !w.Get<Thing>(e).Spawned)
                continue;
            foreach (var (def, count) in CrockFood)
            {
                var thing = sim.Defs.Thing(def);
                var have = sim.StoredIn(e).Where(i => w.Get<Thing>(i).Def == thing).Sum(i => w.Get<Stack>(i).Count);
                if (have >= count)
                    continue;
                var left = sim.Store(e, sim.CreateItem(thing, count - have));
                if (!left.IsNull)
                    sim.DestroyThing(left); // full of something else: the carter takes it back
            }
        }
    }

    /// <summary>The cottage (or inn room) someone sleeps in, or Entity.Null.</summary>
    public static Entity HomeOf(Simulation sim, Entity who) =>
        sim.World.TryGet<BedOwner>(who, out var own) && sim.World.IsAlive(own.Bed) ? Buildings.Of(sim, own.Bed) : Entity.Null;

    private static bool Housed(Simulation sim, Entity who) =>
        sim.World.TryGet<BedOwner>(who, out var own) && sim.World.IsAlive(own.Bed);

    private static bool AtTheInn(Simulation sim, Entity who) =>
        Shops.ShopSystem.WorkplaceOf(sim, who) is { IsNull: false } b && sim.World.Get<PresetBuilding>(b).Def.LetsBeds;

    private static int3? WorkOf(Simulation sim, Entity who)
    {
        var b = Shops.ShopSystem.WorkplaceOf(sim, who);
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
