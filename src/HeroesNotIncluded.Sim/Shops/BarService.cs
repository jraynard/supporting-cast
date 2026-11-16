using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Shops;

/// <summary>
/// Serving across the bar. A server holding a poured drink presses the work key at the counter,
/// or at a customer, and it goes to whoever is waiting for one. They don't have to find that
/// customer and pick them from the inspector. The customer is chosen in this order: one whose
/// order matches the drink (a mixed drink for a mixed-drink order, plain ale for plain), then an
/// order the server took on, then whoever has waited longest in the bar's line. Only orders the
/// server knows of count, and only customers within arm's reach. A lodger's board at an inn without
/// a kitchen (<see cref="Hospitality.Provisions"/>) is served the same way, with a dish off the warming shelf.
/// </summary>
public static class BarService
{
    /// <summary>What to serve (a drink, or a lodger's dish) and the order it goes to.</summary>
    public readonly record struct Serving(Entity Drink, Order Order);

    /// <summary>
    /// What the work key would serve at this cell: pointing at a bar counter serves the best match
    /// in reach; pointing at a customer serves that customer. Null when there's nothing to serve.
    /// </summary>
    public static Serving? At(Simulation sim, Entity server, Entity site, int3 cell)
    {
        var w = sim.World;
        var drink = DrinkOn(sim, server);
        var dish = DishOn(sim, server);
        if (drink.IsNull && dish.IsNull)
            return null;
        var atBar = !site.IsNull && w.IsAlive(site) && w.TryGet<Thing>(site, out var t) && t.Def.Building is { Bar: true };
        Serving? best = null;
        var bestRank = long.MaxValue;
        foreach (var o in sim.Shops.All)
        {
            if (!Waiting(sim, server, o) || (!atBar && w.Get<Position>(o.Poster).Cell != cell))
                continue;
            var served = o.Kind.Kind == ServiceKind.Meal ? dish : drink;
            if (served.IsNull || (o.Kind.Kind == ServiceKind.Meal && !Hatch.Fits(sim, o, w.Get<Thing>(served).Def)))
                continue;
            var rank = Rank(sim, server, served, o);
            if (rank < bestRank)
            {
                bestRank = rank;
                best = new Serving(served, o);
            }
        }
        return best;
    }

    /// <summary>The drink a server has to hand: in hand first, then the pack.</summary>
    public static Entity DrinkOn(Simulation sim, Entity who) => AvatarSystem.Supply(sim, who, d => d.Drink);

    /// <summary>A cooked dish a server has to hand, for a lodger's board: in hand first, then the pack.</summary>
    public static Entity DishOn(Simulation sim, Entity who) => AvatarSystem.Supply(sim, who, d => d is { Cooked: true, Nutrition: > 0, Drink: false });

    /// <summary>
    /// An open drink order (or a lodger's board) this server knows of, its customer in reach and
    /// nobody else seeing to it.
    /// </summary>
    private static bool Waiting(Simulation sim, Entity server, Order o) =>
        o.IsOpen && (o.Kind.Kind is ServiceKind.Drink or ServiceKind.MixedDrink || Hospitality.Provisions.IsBoard(sim, o)) && o.KnownBy.Contains(server)
        && (o.Assignee.IsNull || o.Assignee == server) && o.Poster != server && Handing.InReach(sim, server, o.Poster);

    /// <summary>Lower serves first: the drink matching the order, then your own orders, then the line's order.</summary>
    private static long Rank(Simulation sim, Entity server, Entity drink, Order o)
    {
        var mixed = sim.World.Has<Work.DrinkMix>(drink);
        long rank = mixed == (o.Kind.Kind == ServiceKind.MixedDrink) ? 0 : 4;
        if (o.Assignee != server)
            rank += 2;
        var place = sim.Shops.LineAt(sim, o.Where).IndexOf(o.Poster);
        if (place < 0)
            rank += 1;
        // Then by place in the line, then oldest order first.
        return (rank << 48) + ((long)Math.Max(0, place) << 32) + o.Id;
    }

    /// <summary>The work key's label for a serving.</summary>
    public static string Label(Simulation sim, Serving s) =>
        $"Serve the {sim.LabelOf(s.Drink)} to {s.Order.PosterName}";

    /// <summary>Hand it over: one drink, or a lodger's helping, to the order's customer, as <see cref="GiveCommand"/> would.</summary>
    public static void Serve(Simulation sim, Entity server, Serving s)
    {
        if (!Inventory.OnPerson(sim, server, s.Drink) || !Handing.InReach(sim, server, s.Order.Poster))
            return;
        var taken = s.Order.Kind.Kind == ServiceKind.Meal
            ? Hospitality.Provisions.Portion(sim, s.Drink,
                Math.Max(1, Math.Min(Ai.EatJob.Wanted(sim, s.Order.Poster, sim.World.Get<Thing>(s.Drink).Def), sim.World.Get<Stack>(s.Drink).Count)), server)
            : Inventory.TakeFromPerson(sim, server, s.Drink, 1);
        sim.Shops.Received(sim, server, s.Order.Poster, taken);
    }
}
