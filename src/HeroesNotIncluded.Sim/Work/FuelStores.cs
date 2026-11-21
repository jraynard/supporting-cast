using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Work;

/// <summary>
/// Each workplace keeps its own fuel (<see cref="Defs.BuildingProps.FuelStore"/>: the kitchen's log basket, the inn's and
/// the shrine's log racks, the smithy's bin). It opens a round with <see cref="Defs.SeasonDef.WorkplaceFuelDays"/> of what
/// the fires it serves burn, is refilled to order by the carter (supplies.json) or the carpenter, and in a round a workplace's
/// fires are fed from it alone: the barn's firewood is for homes. Running out is real.
/// </summary>
public static class FuelStores
{
    public static bool IsStore(Simulation sim, Entity e) =>
        sim.World.TryGet<Thing>(e, out var t) && t.Def.Building is { FuelStore: true };

    /// <summary>The fuel stores of a building.</summary>
    public static IEnumerable<Entity> In(Simulation sim, Entity building) =>
        building.IsNull ? [] : Buildings.Parts(sim, building).Where(e => IsStore(sim, e));

    /// <summary>The store a fire of a workplace is fed from: the nearest in its building (the kitchen's basket for the kitchen's fires, the rack for the common room's).</summary>
    public static Entity For(Simulation sim, Entity fire)
    {
        var w = sim.World;
        var at = w.Get<Thing>(fire).Cell;
        return In(sim, Buildings.Of(sim, fire)).OrderBy(e => Maps.Map.Distance(w.Get<Thing>(e).Cell, at)).ThenBy(e => e.Index).FirstOrDefault(Entity.Null);
    }

    /// <summary>
    /// Whether <paramref name="item"/> may feed <paramref name="fire"/> by an AI's hand: in a round a workplace's fire (one that goes
    /// cold) burns only what's in its own store (<see cref="For"/>), or the keeper's own workplace's for a station that stands outside
    /// any (the smith's kiln); anything else burns what it's given. Outside a round, anything.
    /// </summary>
    public static bool MayFeed(Simulation sim, Entity pawn, Entity fire, Entity item)
    {
        if (sim.Round.Phase == Round.RoundPhase.None || !Kindling.GoesCold(sim, fire))
            return true;
        var w = sim.World;
        if (!w.TryGet<Stored>(item, out var stored) || !IsStore(sim, stored.In))
            return false;
        // Its own store (the common room's rack, not the kitchen's basket under the same roof); a station standing outside
        // any building (the kiln) burns from its keeper's workplace's.
        if (For(sim, fire) is { IsNull: false } own)
            return stored.In == own;
        var home = Shops.ShopSystem.WorkplaceOf(sim, pawn);
        return !home.IsNull && Buildings.Of(sim, stored.In) == home;
    }

    /// <summary>
    /// At the deal: each workplace's stores get <see cref="Defs.SeasonDef.WorkplaceFuelDays"/> of what its fires that burn all day
    /// go through (a forge or kiln, fired only for work, is stocked by its trade's own reckoning: season.json's smithy charcoal).
    /// </summary>
    public static void StockAll(Simulation sim)
    {
        var w = sim.World;
        var days = sim.Defs.Season.WorkplaceFuelDays;
        var wanted = new Dictionary<(Entity Store, Defs.ThingDef Fuel), float>();
        foreach (var fire in w.Query<Fueled>().OrderBy(e => e.Index))
        {
            if (!Kindling.GoesCold(sim, fire) || w.Get<Thing>(fire).Def.Building!.Fuel is not { OnDemand: false } props)
                continue;
            if (For(sim, fire) is { IsNull: false } store)
            {
                var key = (store, props.ThingDef);
                wanted[key] = wanted.GetValueOrDefault(key) + props.BurnPerDay * days;
            }
        }
        foreach (var ((store, fuel), count) in wanted.OrderBy(kv => kv.Key.Store.Index))
            sim.Store(store, sim.CreateItem(fuel, (int)MathF.Ceiling(count), sim.Defs.Material("Pine")));
    }
}
