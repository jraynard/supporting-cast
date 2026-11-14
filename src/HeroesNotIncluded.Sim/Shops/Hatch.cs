using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Shops;

/// <summary>
/// A serving hatch: the window in the kitchen wall the inn's meals are ordered at and collected
/// from. Orders of the kinds it serves (<see cref="BuildingDef.HatchServes"/>) line up at it rather
/// than at the bar (their own line, <see cref="BuildingDef.HatchLine"/>), and are said through it.
/// The cook puts dishes up on it (it's a container: anything in hand stored in it); whoever's at
/// the front of its line with an order open takes one, pays whoever made it, and goes to sit with
/// it. First come, first served: a dish up on the hatch is anyone's at the front.
/// </summary>
public static class Hatch
{
    /// <summary>A building's hatch, or Entity.Null.</summary>
    public static Entity Of(Simulation sim, Entity shop)
    {
        if (!sim.World.IsAlive(shop) || !sim.World.Has<PresetBuilding>(shop))
            return Entity.Null;
        foreach (var part in Buildings.Parts(sim, shop))
            if (sim.World.Get<Thing>(part).Def.Building is { Hatch: true } && !sim.World.Has<Construction>(part))
                return part;
        return Entity.Null;
    }

    /// <summary>The building a hatch is part of (the inn), or Entity.Null.</summary>
    public static Entity ShopOf(Simulation sim, Entity hatch) =>
        sim.World.IsAlive(hatch) && sim.World.Get<Thing>(hatch).Def.Building is { Hatch: true } ? Buildings.Of(sim, hatch) : Entity.Null;

    /// <summary>Does this order line up at its building's hatch rather than its counter?</summary>
    public static bool Takes(Simulation sim, Order e) =>
        sim.World.IsAlive(e.Where) && sim.World.TryGet<PresetBuilding>(e.Where, out var b) && b.Def.HatchServes.Contains(e.Kind.Kind);

    /// <summary>Would this dish do for this order?</summary>
    public static bool Fits(Simulation sim, Order e, ThingDef dish) => e.Kind.Kind switch
    {
        ServiceKind.Meal => dish.Cooked && dish.Nutrition > 0 && sim.World.IsAlive(e.Poster) && dish.EdibleBy(sim.World.Get<Creature>(e.Poster).Def),
        ServiceKind.Drink or ServiceKind.MixedDrink => dish.Drink,
        _ => false,
    };

    /// <summary>The dishes up on a hatch: what's in it that would do for an order of this kind.</summary>
    public static List<Entity> DishesOn(Simulation sim, Entity hatch, ServiceKind kind)
    {
        var dishes = new List<Entity>();
        if (hatch.IsNull)
            return dishes;
        foreach (var item in sim.StoredIn(hatch))
        {
            var def = sim.World.Get<Thing>(item).Def;
            if (kind == ServiceKind.Meal ? def.Cooked && def.Nutrition > 0 : def.Drink)
                dishes.Add(item);
        }
        return dishes;
    }

    /// <summary>Open orders waiting at a building's hatch for a dish of this kind.</summary>
    public static int Waiting(Simulation sim, Entity shop, ServiceKind kind) =>
        sim.Shops.All.Count(e => e.IsOpen && e.Where == shop && e.Kind.Kind == kind && Takes(sim, e));

    /// <summary>
    /// Enough is up already: as many dishes on the hatch as orders waiting for one. Cooks don't put
    /// up more than are asked for.
    /// </summary>
    public static bool Enough(Simulation sim, Entity shop, ServiceKind kind) =>
        DishesOn(sim, Of(sim, shop), kind).Sum(d => sim.World.Get<Stack>(d).Count) >= Waiting(sim, shop, kind);

    /// <summary>
    /// The customer at the front of the hatch's line takes a dish for their order, if there's one up:
    /// a serving's worth, paid to whoever made it (else whoever took the order on, else the keeper
    /// serving it there). True if they did.
    /// </summary>
    internal static bool TryCollect(Simulation sim, Order e)
    {
        var w = sim.World;
        if (!e.IsOpen || !Takes(sim, e) || sim.Shops.LineAt(sim, e.Where, hatch: true) is not { Count: > 0 } line || line[0] != e.Poster)
            return false;
        var cells = ShopSystem.LineCells(sim, e.Where, hatch: true);
        var hatch = Of(sim, e.Where);
        if (cells.Count == 0 || hatch.IsNull || !w.TryGet<Position>(e.Poster, out var at) || at.Cell.Z != cells[0].Z
            || Maps.Map.Distance(at.Cell, cells[0]) > 1 || !Inventory.CanReachContainer(sim, e.Poster, hatch))
            return false;
        var dish = Entity.Null;
        foreach (var d in DishesOn(sim, hatch, e.Kind.Kind))
            if (Fits(sim, e, w.Get<Thing>(d).Def) && !sim.Reservations.IsReservedByOther(d, e.Poster))
            {
                dish = d;
                break;
            }
        if (dish.IsNull)
            return false;
        var def = w.Get<Thing>(dish).Def;
        var made = w.TryGet<Crafted>(dish, out var c) ? c : (Crafted?)null;
        var flavoured = w.TryGet<Work.Dish>(dish, out var d0) ? d0 : (Work.Dish?)null;
        var count = e.Kind.Kind == ServiceKind.Meal ? Math.Min(EatJob.Wanted(sim, e.Poster, def), w.Get<Stack>(dish).Count) : 1;
        var portion = sim.TakeFromStack(dish, count);
        if (made is { } m && !w.Has<Crafted>(portion))
            w.Add(portion, m);
        if (flavoured is { } fl && !w.Has<Work.Dish>(portion))
            w.Add(portion, fl);
        sim.Shops.Received(sim, PaidTo(sim, e, made?.Maker ?? Entity.Null), e.Poster, portion);
        return true;
    }

    /// <summary>Who's paid for a dish taken off the hatch: its maker, else the order's taker, else whoever serves it there.</summary>
    private static Entity PaidTo(Simulation sim, Order e, Entity maker)
    {
        var w = sim.World;
        bool Here(Entity x) => !x.IsNull && w.IsAlive(x) && w.Has<Creature>(x) && !w.Get<Creature>(x).Dead;
        if (Here(maker) && maker != e.Poster)
            return maker;
        if (Here(e.Assignee) && e.Assignee != e.Poster)
            return e.Assignee;
        foreach (var x in sim.Creatures())
            if (w.TryGet<Trade>(x, out var t) && t.Def.WorkplaceDef == e.Kind.AtDef && t.Def.Serving(e.Kind.Kind))
                return x;
        return e.Poster; // nobody to pay: on the house
    }
}
