using SupportingCast.Sim.Components;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Shops;

/// <summary>What a shop's sign says to anyone who stops to read it: its name, and the prices it's offering.</summary>
public readonly record struct SignReading(string Name, string What, IReadOnlyList<PostedPrice> Prices);

/// <summary>
/// A shop's sign, hung on its front wall outside (<see cref="Defs.BuildingDef.Sign"/>): its name
/// (<see cref="ShopName"/>) and its price list (<see cref="ShopPrices"/>), for anyone passing to
/// read. Nothing on it is secret: it's painted up for the street.
/// </summary>
public static class Signs
{
    /// <summary>The wall cell a building's sign hangs on, or null if it has none.</summary>
    public static int3? CellOf(Simulation sim, Entity building)
    {
        if (!sim.World.IsAlive(building) || !sim.World.TryGet<PresetBuilding>(building, out var b) || b.Def.Sign is not { } at)
            return null;
        return Buildings.CellOf(sim.Map, b.Def, b.Anchor, b.Facing, at[0], at[1]);
    }

    /// <summary>The building whose sign hangs on this wall cell, or Entity.Null. Only a finished wall carries one.</summary>
    public static Entity At(Simulation sim, int3 cell)
    {
        var w = sim.World;
        if (!sim.Map.InBounds(cell) || sim.Map.Structure[sim.Map.Idx(cell)] is not { IsNull: false } wall || w.Has<Construction>(wall))
            return Entity.Null;
        var building = Buildings.Of(sim, wall);
        return !building.IsNull && CellOf(sim, building) == cell ? building : Entity.Null;
    }

    /// <summary>The sign as it reads: the name (or what the place is), and every service it's offering, at its price.</summary>
    public static SignReading Read(Simulation sim, Entity building)
    {
        var b = sim.World.Get<PresetBuilding>(building);
        var what = Buildings.Label(b);
        var name = ShopName.Of(sim, building) ?? $"The {char.ToUpperInvariant(what[0])}{what[1..]}";
        return new SignReading(name, what, ShopPrices.Board(sim, building).Where(p => p.Offered).ToList());
    }
}
