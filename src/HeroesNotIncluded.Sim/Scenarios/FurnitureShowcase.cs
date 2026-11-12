using HeroesNotIncluded.Sim.Defs;

namespace HeroesNotIncluded.Sim.Scenarios;

/// <summary>
/// Debug/screenshots (<c>--furniture-demo</c>): the wide furniture and roofs, finished, beside the
/// landing. A walled room (roofed by its walls) holds beds and every table shape; off its east
/// wall a porch under a built thatch roof shelters the workbenches. Pieces that don't fit the
/// ground there are skipped.
/// </summary>
public static class FurnitureShowcase
{
    public static int3 Setup(Simulation sim, int3 center)
    {
        var defs = sim.Defs;
        var o = new int3(center.X - 14, center.Y - 4, center.Z);
        int3 At(int x, int y) => new(o.X + x, o.Y + y, o.Z);
        void Put(string def, int x, int y, int facing = 0)
        {
            var d = defs.Thing(def);
            if (sim.CanPlaceStructure(At(x, y), d, facing))
                sim.SpawnStructure(d, At(x, y), facing);
        }

        // Clear the plot of trees, bushes and boulders (and anything lying there).
        for (var y = -1; y <= 8; y++)
        for (var x = -1; x <= 11; x++)
        {
            var c = At(x, y);
            if (!sim.Map.InBounds(c))
                continue;
            var i = sim.Map.Idx(c);
            if (sim.Map.Structure[i] is { IsNull: false } s && sim.World.Get<Components.Thing>(s).Def.Category is ThingCategory.Plant or ThingCategory.Resource)
                sim.DestroyThing(s);
            if (sim.Map.Item[i] is { IsNull: false } item)
                sim.DestroyThing(item);
        }

        // The room: 6x6 inside, a door to the south.
        for (var y = 0; y <= 7; y++)
        for (var x = 0; x <= 7; x++)
            if (x == 0 || x == 7 || y == 0 || y == 7)
                Put(x == 3 && y == 7 ? "Door" : "WoodWall", x, y);
        Put("Bed", 1, 1);
        Put("Bed", 2, 1);
        Put("StrawPallet", 6, 1, facing: 1);
        Put("Table", 1, 5);
        Put("LongTable", 3, 3, facing: 3);
        Put("SquareTable", 5, 4);
        Put("Stool", 2, 5);
        Put("Chest", 6, 6);

        // The porch: open on three sides, its roof held up by the room's east wall.
        for (var y = 0; y <= 7; y++)
        for (var x = 8; x <= 10; x++)
        {
            var roof = new int3(o.X + x, o.Y + y, o.Z + 1);
            if (sim.CanPlaceStructure(roof, defs.Thing("ThatchRoof")))
                sim.SpawnStructure(defs.Thing("ThatchRoof"), roof);
        }
        Put("CarpenterBench", 9, 0);
        Put("MasonBench", 9, 2);
        Put("TanningRack", 9, 4);
        Put("Sawhorse", 9, 6);
        sim.RebuildDerivedIfNeeded();
        return At(5, 4);
    }
}
