using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Maps;

/// <summary>
/// Stamps a hand-made <see cref="MapDef"/> into a simulation: the ground column by column, rock and
/// ore below it as a generated map has (so mining still finds something), the features, the
/// buildings finished at their anchors, then the people and herds as <see cref="MapGenerator"/>
/// brings them. Everything placed is fixed by the def; the seed still varies what grows from
/// <c>sim.Rand</c>, the rock and ore underground, and who the villagers are.
/// </summary>
public static class MapBuilder
{
    /// <summary>What was built: the village's middle, and each listed building's entity (in def order; Null never, a misfit throws).</summary>
    public readonly record struct Built(int3 Center, IReadOnlyList<(MapBuilding Spec, Entity Building)> Buildings);

    /// <summary>A simulation sized for <paramref name="map"/>, with the standard systems.</summary>
    public static Simulation CreateSimulation(DefDatabase defs, MapDef map, ulong seed) =>
        Simulation.CreateStandard(defs, map.Width, map.Height, seed, map.Depth);

    /// <summary>The middle of the map's stockpile zone, on its ground.</summary>
    private static int3 StockpileMiddle(MapDef def)
    {
        int x = def.Stockpile[0] + def.Stockpile[2] / 2, y = def.Stockpile[1] + def.Stockpile[3] / 2;
        return new int3(x, y, def.SurfaceZ(x, y));
    }

    public static Built Build(Simulation sim, MapDef def, MapGenerator.Options? options = null)
    {
        options ??= new MapGenerator.Options();
        var map = sim.Map;
        if (map.Width != def.Width || map.Height != def.Height || map.Depth != def.Depth)
            throw new ArgumentException($"Map '{def.Name}' is {def.Width}x{def.Height}x{def.Depth}; the simulation is {map.Width}x{map.Height}x{map.Depth}");
        var seed = (uint)sim.Seed;
        var dirt = sim.Defs.Block("Dirt");
        var rock = sim.Defs.Block("Stone");

        var standZ = new int[map.LayerSize];
        var moistures = new float[map.LayerSize];
        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            var col = y * map.Width + x;
            standZ[col] = def.StandZ(x, y);
            moistures[col] = MapGenerator.Fbm(x, y, seed + 7919, 32f, 3);
            MapGenerator.FillColumn(map, x, y, standZ[col], def.Ground[col], dirt, rock);
        }
        var center = new int3(def.Center[0], def.Center[1], def.SurfaceZ(def.Center[0], def.Center[1]));
        MapGenerator.PlaceStrata(sim, standZ, center.Z, center, seed);
        MapGenerator.PlaceVeins(sim, standZ, moistures, center, seed, def.VeinDefs);
        PlaceSeams(sim, def, seed);
        CarveTunnels(sim, def);
        map.MarkStructureChanged();

        // Floors first (a bridge holds up whatever stands on it), then everything on the ground.
        for (var pass = 0; pass < 2; pass++)
        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            if (def.Features[y * map.Width + x] is not { } f)
                continue;
            var cell = new int3(x, y, f.Level < 0 ? standZ[y * map.Width + x] : def.BaseZ + f.Level);
            // A floor's in the first pass and what stands on it in the second, with everything else.
            var (put, facing) = pass == 0 ? (f.Def.IsFloor ? f.Def : null, Facing(def, x, y))
                : f.Def.IsFloor ? (f.On, 0) : (f.Def, Facing(def, x, y));
            if (put == null)
                continue;
            if (!sim.CanPlaceStructure(cell, put, facing))
                throw new Exception($"Map '{def.Name}': {put.Name} at {x},{y} (z {cell.Z}) can't be placed there");
            var placed = sim.SpawnStructure(put, cell, facing);
            if (f.Locked && put == f.Def)
                sim.SetLocked(placed, true);
        }
        sim.RebuildDerivedIfNeeded();

        var buildings = new List<(MapBuilding, Entity)>();
        foreach (var b in def.Buildings)
        {
            var anchor = new int3(b.At[0], b.At[1], def.StandZ(b.At[0], b.At[1], b.Level));
            if (Buildings.PlacementProblem(sim, b.BuildingDef, anchor, b.Facing, b.VariantIndex) is { } problem)
                throw new Exception($"Map '{def.Name}': {b.Def} at {b.At[0]},{b.At[1]}: {problem}");
            buildings.Add((b, Buildings.SpawnFinished(sim, b.BuildingDef, anchor, b.Facing, b.VariantIndex)));
            sim.RebuildDerivedIfNeeded(); // the next plot must see this one
        }

        if (def.Stockpile.Length == 4)
            for (var y = def.Stockpile[1]; y < def.Stockpile[1] + def.Stockpile[3]; y++)
            for (var x = def.Stockpile[0]; x < def.Stockpile[0] + def.Stockpile[2]; x++)
                if (map.InBounds(x, y, 0))
                    map.SetZone(map.Idx(x, y, def.SurfaceZ(x, y)), 1);

        MapGenerator.Settle(sim, center, new MapGenerator.Options
        {
            Villagers = options.Villagers, SpawnStartingKit = options.SpawnStartingKit, Wildlife = options.Wildlife,
            Forest = options.Forest, PileAt = def.Stockpile.Length == 4 ? StockpileMiddle(def) : null,
        });

        foreach (var l in def.Livestock)
        {
            var at = new int3(l.At[0], l.At[1], def.SurfaceZ(l.At[0], l.At[1]));
            var placed = 0;
            foreach (var c in sim.SpiralFrom(at, 4))
            {
                if (placed == l.Count)
                    break;
                if (!map.Passable(c))
                    continue;
                // Wild-faction and herdless: nobody's villager to be dealt into, and no herd to wander off with.
                sim.SpawnCreature(l.CreatureDef, l.CreatureDef.Label, c, Faction.Wild, ageDays: 365);
                placed++;
            }
        }
        sim.Festival.Plan(def);
        return new Built(center, buildings);
    }

    /// <summary>
    /// The map's sure ore bodies (<see cref="MapDef.Seams"/>): rock within about the seam's radius of
    /// its column, through its levels, turns to its ore. The edge is the seed's hashed noise (no
    /// sim.Rand, so it's the same on every peer). Only rock a pick breaks is turned, never soil or ore
    /// already there.
    /// </summary>
    private static void PlaceSeams(Simulation sim, MapDef def, uint seed)
    {
        var map = sim.Map;
        for (var n = 0; n < def.Seams.Count; n++)
        {
            var seam = def.Seams[n];
            var salt = seed + 15485863u + (uint)n * 7919u;
            var reach = (int)MathF.Ceiling(seam.Radius * 1.3f);
            int cx = seam.At[0], cy = seam.At[1];
            for (var y = cy - reach; y <= cy + reach; y++)
            for (var x = cx - reach; x <= cx + reach; x++)
            {
                if (!def.Inside(x, y))
                    continue;
                var d = MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                for (var z = def.BaseZ + seam.Levels[0]; z <= def.BaseZ + seam.Levels[1]; z++)
                {
                    var i = map.Idx(x, y, z);
                    if (map.Block[i] is not { Solid: true, IsWater: false, Ore: false, DigTool: "Pick" })
                        continue;
                    if (d <= seam.Radius * (0.7f + 0.6f * MapGenerator.Fbm(x + z * 37, y - z * 53, salt, 3f, 2)))
                        map.Block[i] = seam.BlockDef;
                }
            }
        }
    }

    /// <summary>
    /// The map's tunnels (<see cref="MapDef.Tunnels"/>) dug out: air where people walk, the rock left
    /// above. Their walls are worked out: ore that a tunnel would lay open is its host rock instead, so a
    /// mine's ore is struck by digging, never found lying open at the start.
    /// </summary>
    private static void CarveTunnels(Simulation sim, MapDef def)
    {
        var map = sim.Map;
        var air = sim.Defs.Block("Air");
        var rock = sim.Defs.Block("Stone");
        foreach (var t in def.Tunnels)
        foreach (var c in t.Cells)
            map.Block[map.Idx(c[0], c[1], def.BaseZ + t.Level)] = air;
        foreach (var t in def.Tunnels)
        foreach (var c in t.Cells)
        {
            var at = new int3(c[0], c[1], def.BaseZ + t.Level);
            foreach (var d in Walls)
                if (map.InBounds(at + d) && map.Block[map.Idx(at + d)] is { Ore: true } ore)
                    map.Block[map.Idx(at + d)] = ore.HostRockDef ?? rock;
        }
    }

    private static readonly int3[] Walls =
        [new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, -1), new(0, 0, 1)];

    /// <summary>
    /// Which way a feature faces: along its run of like features (a bridge across its river, a
    /// fence along its line), else the default.
    /// </summary>
    private static int Facing(MapDef def, int x, int y)
    {
        var here = def.Features[y * def.Width + x]?.Def;
        bool Same(int nx, int ny) => def.Inside(nx, ny) && def.Features[ny * def.Width + nx]?.Def == here;
        return !Same(x - 1, y) && !Same(x + 1, y) && (Same(x, y - 1) || Same(x, y + 1)) ? 1 : 0;
    }
}
