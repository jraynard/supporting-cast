using DragonWorld.Sim.Ai;
using DragonWorld.Sim.Maps;
using Xunit.Abstractions;

namespace DragonWorld.Sim.Tests;

public class MapGenTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1234UL)]
    [InlineData(99UL)]
    public void HillsRiseAboveTheLandingSiteAndAreMostlyReachable(ulong seed)
    {
        var sim = Simulation.CreateStandard(TestWorld.LoadDefs(), 160, 160, seed, MapGenerator.RecommendedDepth);
        var center = MapGenerator.Generate(sim);
        var map = sim.Map;

        int high = 0, reachable = 0, slopes = 0;
        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            var z = map.SurfaceZ(x, y);
            var cell = new int3(x, y, z);
            if (!map.Structure[map.Idx(cell)].IsNull && sim.World.Get<Components.Thing>(map.Structure[map.Idx(cell)]).Def.Name == "Slope")
                slopes++;
            if (z <= center.Z || map.PathCost[map.Idx(cell)] <= 0)
                continue;
            high++;
            if (sim.Regions.CanReach(center, cell, PathEnd.OnTile))
                reachable++;
        }
        output.WriteLine($"high ground {high} cells, {reachable} reachable, {slopes} slopes");

        Assert.Equal(center.Z, map.SurfaceZ(center.X, center.Y));
        Assert.True(high > 500, "expected some hills");
        Assert.True(reachable > high / 2, $"only {reachable}/{high} high cells reachable");
    }

    /// <summary>Forests mix oak, pine and birch, but in groves: most trees stand near their own kind.</summary>
    [Theory]
    [InlineData(1234UL)]
    [InlineData(99UL)]
    public void ForestsGrowInSingleSpeciesGroves(ulong seed)
    {
        var sim = Simulation.CreateStandard(TestWorld.LoadDefs(), 160, 160, seed, MapGenerator.RecommendedDepth);
        MapGenerator.Generate(sim);
        var map = sim.Map;
        var species = new Dictionary<(int, int), string>();
        foreach (var e in sim.World.Query<Components.Thing>())
        {
            var t = sim.World.Get<Components.Thing>(e);
            if (t.Def.IsTree)
                species[(t.Cell.X, t.Cell.Y)] = t.Def.Name;
        }

        var counts = species.Values.GroupBy(n => n).ToDictionary(g => g.Key, g => g.Count());
        output.WriteLine(string.Join(", ", counts.Select(kv => $"{kv.Key} {kv.Value}")));
        foreach (var name in new[] { "Oak", "Pine", "Birch" })
            Assert.True(counts.GetValueOrDefault(name) > species.Count / 20, $"too few {name}: {counts.GetValueOrDefault(name)} of {species.Count}");

        // Among neighbouring trees, matching species should be far likelier than chance.
        int pairs = 0, same = 0;
        foreach (var ((x, y), name) in species)
            foreach (var (dx, dy) in new[] { (1, 0), (0, 1), (1, 1), (1, -1) })
                if (species.TryGetValue((x + dx, y + dy), out var other))
                {
                    pairs++;
                    if (other == name)
                        same++;
                }
        var chance = counts.Values.Sum(c => (double)c * c) / ((double)species.Count * species.Count);
        output.WriteLine($"neighbour pairs {pairs}, same species {same / (double)pairs:P0} (chance {chance:P0})");
        Assert.True(same / (double)pairs > chance + 0.15, "trees aren't grouped into groves");
    }
}
