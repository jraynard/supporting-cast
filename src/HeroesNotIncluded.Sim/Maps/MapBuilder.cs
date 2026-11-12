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
        var center = new int3(def.Center[0], def.Center[1], def.StandZ(def.Center[0], def.Center[1]));
        MapGenerator.PlaceStrata(sim, standZ, center.Z, center, seed);
        MapGenerator.PlaceVeins(sim, standZ, moistures, center, seed);
        map.MarkStructureChanged();

        // Floors first (a bridge holds up whatever stands on it), then everything on the ground.
        for (var pass = 0; pass < 2; pass++)
        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            if (def.Features[y * map.Width + x] is not { } f || f.Def.IsFloor != (pass == 0))
                continue;
            var cell = new int3(x, y, f.Level < 0 ? standZ[y * map.Width + x] : def.BaseZ + f.Level);
            if (!sim.CanPlaceStructure(cell, f.Def, Facing(def, x, y)))
                throw new Exception($"Map '{def.Name}': {f.Def.Name} at {x},{y} (z {cell.Z}) can't be placed there");
            var placed = sim.SpawnStructure(f.Def, cell, Facing(def, x, y));
            if (f.Locked)
                sim.SetLocked(placed, true);
        }
        sim.RebuildDerivedIfNeeded();

        var buildings = new List<(MapBuilding, Entity)>();
        foreach (var b in def.Buildings)
        {
            var anchor = new int3(b.At[0], b.At[1], def.StandZ(b.At[0], b.At[1]));
            if (Buildings.PlacementProblem(sim, b.BuildingDef, anchor, b.Facing, b.VariantIndex) is { } problem)
                throw new Exception($"Map '{def.Name}': {b.Def} at {b.At[0]},{b.At[1]}: {problem}");
            buildings.Add((b, Buildings.SpawnFinished(sim, b.BuildingDef, anchor, b.Facing, b.VariantIndex)));
            sim.RebuildDerivedIfNeeded(); // the next plot must see this one
        }

        if (def.Stockpile.Length == 4)
            for (var y = def.Stockpile[1]; y < def.Stockpile[1] + def.Stockpile[3]; y++)
            for (var x = def.Stockpile[0]; x < def.Stockpile[0] + def.Stockpile[2]; x++)
                if (map.InBounds(x, y, 0))
                    map.SetZone(map.Idx(x, y, def.StandZ(x, y)), 1);

        MapGenerator.Settle(sim, center, options);

        foreach (var l in def.Livestock)
        {
            var at = new int3(l.At[0], l.At[1], def.StandZ(l.At[0], l.At[1]));
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
        return new Built(center, buildings);
    }

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
