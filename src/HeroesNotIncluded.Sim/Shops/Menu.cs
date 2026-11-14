using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Shops;

/// <summary>
/// The dishes on a shop's sign beyond its plain meal (docs/DESIGN.md, "Cook"): a roast off the spit,
/// later bread from the oven. A plain meal order takes whatever's up on the hatch at the meal's
/// price; a diner who wants a dish asks for it by name, at its own price, and only it will do
/// (<see cref="Order.Dish"/>). A dish is on the menu only where the building has what makes it
/// (<see cref="MenuProps.MadeAt"/>) and only once its cook offers it, so a kitchen nobody roasts in
/// is never asked for a roast. Posted with the prices, for anyone to read.
/// </summary>
public sealed class DishPrices
{
    /// <summary>Price and whether it's offered, by dish def name (the defaults until the cook sets one).</summary>
    public readonly SortedDictionary<string, (int Price, bool Offered)> Dishes = new(StringComparer.Ordinal);
}

/// <summary>A dish on a shop's sign, as anyone reading it sees it.</summary>
public readonly record struct PostedDish(ThingDef Dish, int Price, bool Offered);

public static class Menu
{
    /// <summary>The chance a diner asks for a dish on the menu by name, if there's one offered.</summary>
    public const float AskChance = 0.3f;

    /// <summary>The dishes a building could put on its sign: those made at a part it has, in def order.</summary>
    public static List<PostedDish> Of(Simulation sim, Entity building)
    {
        var list = new List<PostedDish>();
        var w = sim.World;
        if (!w.IsAlive(building) || !w.Has<PresetBuilding>(building))
            return list;
        var parts = Buildings.Parts(sim, building).Where(e => !w.Has<Construction>(e)).Select(e => w.Get<Thing>(e).Def.Name).ToHashSet();
        var set = w.TryGet<DishPrices>(building, out var p) ? p : null;
        foreach (var def in sim.Defs.AllThings)
            if (def.Menu is { } m && parts.Contains(m.MadeAt))
                list.Add(set != null && set.Dishes.TryGetValue(def.Name, out var x) ? new PostedDish(def, x.Price, x.Offered) : new PostedDish(def, m.Price, false));
        return list;
    }

    /// <summary>The dishes offered on a building's sign.</summary>
    public static IEnumerable<PostedDish> Offered(Simulation sim, Entity building) => Of(sim, building).Where(d => d.Offered);

    /// <summary>
    /// The cook prices a dish, or takes it off. False (and nothing changes) unless they keep the
    /// shop, see to its meals, and the dish is one it can make.
    /// </summary>
    public static bool Set(Simulation sim, Entity who, Entity building, ThingDef dish, int price, bool offered)
    {
        if (!ShopPrices.Keeps(sim, who, building) || !sim.World.Get<Trade>(who).Def.Serving(ServiceKind.Meal)
            || !Of(sim, building).Exists(d => d.Dish == dish))
            return false;
        var w = sim.World;
        if (!w.TryGet<DishPrices>(building, out var p))
            w.Add(building, p = new DishPrices());
        p.Dishes[dish.Name] = (Math.Clamp(price, 0, ShopPrices.MaxPrice), offered);
        return true;
    }
}

/// <summary>Price a dish on the sign, or take it off (<see cref="Menu.Set"/>).</summary>
public sealed record SetDishPriceCommand(Entity Avatar, Entity Shop, ThingDef Dish, int Price, bool Offered) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (!sim.World.IsAlive(Avatar) || !sim.World.IsAlive(Shop) || Dish?.Menu == null)
            return;
        Menu.Set(sim, Avatar, Shop, Dish, Price, Offered);
    }
}
