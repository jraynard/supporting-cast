using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;

namespace HeroesNotIncluded.Sim.Scenarios;

/// <summary>
/// Debug/screenshots (<c>--item-demo</c>): the inn's crockery and the kitchen's food, clean and
/// dirty, laid out on boards in the open valley (<see cref="BuildingShowcase.NewValley"/>) a cell
/// apart, with a table set with a few of them: to judge how they look in the world, side by side.
/// Defs a mod leaves out are skipped.
/// </summary>
public static class ItemShowcase
{
    /// <summary>What's laid out, in rows.</summary>
    public static readonly string[][] Rows =
    [
        ["Tankard", "DirtyTankard", "Ale", "HouseMix", "Spirits", "Bowl", "DirtyBowl"],
        ["Stew", "Porridge", "SimpleMeal", "Bread", "Pie", "RawPie", "Dough"],
        ["Roast", "PorkSide", "Meat", "SaltMeat", "SmokedMeat", "DressedFowl", "Fowl"],
        ["Flour", "Oats", "CoarseSalt", "Rations", "RawFish", "SmokedFish", "Cabbage"],
    ];

    public static int3 Setup(Simulation sim, int3 center)
    {
        var defs = sim.Defs;
        var o = new int3(center.X - 7, center.Y - 4, center.Z);
        int3 At(int x, int y) => new(o.X + x, o.Y + y, o.Z);
        for (var y = -1; y <= 2 * Rows.Length + 2; y++)
        for (var x = -1; x <= 16; x++)
        {
            var c = At(x, y);
            if (!sim.Map.InBounds(c))
                continue;
            var i = sim.Map.Idx(c);
            if (sim.Map.Structure[i] is { IsNull: false } s)
                sim.DestroyThing(s);
            if (sim.Map.Item[i] is { IsNull: false } item)
                sim.DestroyThing(item);
            if (defs.AllThings.FirstOrDefault(t => t.Name == "Floorboards") is { } boards && sim.CanPlaceStructure(c, boards))
                sim.SpawnStructure(boards, c);
        }
        ThingDef? Def(string name) => defs.AllThings.FirstOrDefault(t => t.Name == name);
        for (var r = 0; r < Rows.Length; r++)
            for (var k = 0; k < Rows[r].Length; k++)
                if (Def(Rows[r][k]) is { } def)
                    sim.SpawnItem(def, 1, At(k * 2, r * 2));
        // A table set as a customer left it, off to the side.
        if (Def("LongTable") is { } table && sim.CanPlaceStructure(At(15, 3), table, 0))
            sim.SpawnStructure(table, At(15, 3), 0);
        foreach (var (name, y) in new[] { ("DirtyTankard", 2), ("DirtyBowl", 3), ("Stew", 4) })
            if (Def(name) is { } def)
                sim.SpawnItem(def, 1, At(15, y));
        sim.RebuildDerivedIfNeeded();
        return At(7, Rows.Length);
    }
}
