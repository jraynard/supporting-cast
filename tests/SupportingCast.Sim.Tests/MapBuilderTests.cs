using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Maps;

namespace SupportingCast.Sim.Tests;

/// <summary>A hand-made map (<see cref="MapDef"/>) stamps in as drawn: levels, ground, features, buildings.</summary>
public class MapBuilderTests
{
    // 20x12: a river two levels down across the middle (rows 5-6) with a plank bridge, a dwelling
    // to the north, a fence and an oak to the south.
    private const string TinyMap = """
    { "maps": [ {
      "name": "Tiny", "width": 20, "height": 12, "baseZ": 2,
      "groundLegend": { "g": "Grass", "d": "Dirt", "w": "Water", "k": "Stone" },
      "featureLegend": { "O": "Oak", "f": "Fence", "=": "WoodFloor@2" },
      "ground": [
        "gggggggggggggggggggg",
        "gggggggggggggggggggg",
        "gggggggggggggggggggg",
        "gggggggggdgggggggggg",
        "kkkkkkkkkdkkkkkkkkkk",
        "wwwwwwwwwwwwwwwwwwww",
        "wwwwwwwwwwwwwwwwwwww",
        "kkkkkkkkkdkkkkkkkkkk",
        "gggggggggdgggggggggg",
        "gggggggggggggggggggg",
        "gggggggggggggggggggg",
        "gggggggggggggggggggg"
      ],
      "levels": [
        "22222222222222222222",
        "22222222222222222222",
        "22222222222222222222",
        "22222222222222222222",
        "22222222222222222222",
        "00000000000000000000",
        "00000000000000000000",
        "22222222222222222222",
        "22222222222222222222",
        "22222222222222222222",
        "22222222222222222222",
        "22222222222222222222"
      ],
      "features": [
        "....................",
        "....................",
        "....................",
        "....................",
        "....................",
        ".........=..........",
        ".........=..........",
        "....................",
        "....................",
        "....................",
        "..O........fffff....",
        "...................."
      ],
      "buildings": [ { "def": "Dwelling", "at": [4, 2], "role": "Home" } ],
      "center": [10, 9],
      "stockpile": [14, 8, 3, 2]
    } ] }
    """;

    private static DefDatabase DefsWith(string json)
    {
        var files = Directory.EnumerateFiles(TestWorld.FindDefsDirectory(), "*.json", SearchOption.AllDirectories).Order().Select(File.ReadAllText);
        return DefDatabase.FromJson([.. files, json]);
    }

    private static (Simulation Sim, MapBuilder.Built Built) Build(ulong seed = 1)
    {
        var defs = DefsWith(TinyMap);
        var sim = MapBuilder.CreateSimulation(defs, defs.Map("Tiny"), seed);
        var built = MapBuilder.Build(sim, defs.Map("Tiny"), new MapGenerator.Options { Villagers = 2, Wildlife = false });
        return (sim, built);
    }

    [Fact]
    public void TheGroundIsLaidAsDrawn()
    {
        var (sim, built) = Build();
        var map = sim.Map;
        Assert.Equal(new int3(10, 9, 4), built.Center);
        Assert.Equal("Grass", map.Block[map.Idx(0, 0, 3)].Name);
        Assert.Equal("Dirt", map.Block[map.Idx(9, 3, 3)].Name);
        Assert.True(map.Block[map.Idx(0, 5, 1)].IsWater, "the river lies two levels down");
        Assert.False(map.IsSolid(map.Idx(0, 5, 3)), "nothing stands over the river");
        Assert.Equal("Oak", sim.World.Get<Thing>(map.Structure[map.Idx(2, 10, 4)]).Def.Name);
        Assert.Equal(1, map.Zone[map.Idx(14, 8, 4)]);
    }

    [Fact]
    public void TheBridgeCarriesPeopleOverTheRiver()
    {
        var (sim, _) = Build();
        sim.RebuildDerivedIfNeeded();
        Assert.True(sim.Map.Passable(new int3(9, 5, 4)), "the bridge is walkable");
        Assert.True(sim.Regions.CanReach(new int3(9, 1, 4), new int3(9, 10, 4), PathEnd.OnTile), "north reaches south over the bridge");
    }

    [Fact]
    public void BuildingsStandFinishedAtTheirAnchors()
    {
        var (sim, built) = Build();
        var (spec, home) = Assert.Single(built.Buildings);
        Assert.Equal("Home", spec.Role);
        var b = sim.World.Get<PresetBuilding>(home);
        Assert.Equal(new int3(4, 2, 4), b.Anchor);
        Assert.Equal(BuildPhase.Done, b.Phase);
    }

    [Fact]
    public void AMisfitBuildingIsAnErrorInTheMap()
    {
        var bad = TinyMap.Replace("\"at\": [4, 2]", "\"at\": [4, 5]"); // in the river
        var defs = DefsWith(bad);
        var sim = MapBuilder.CreateSimulation(defs, defs.Map("Tiny"), 1);
        var e = Assert.Throws<Exception>(() => MapBuilder.Build(sim, defs.Map("Tiny"), new MapGenerator.Options { Wildlife = false }));
        Assert.Contains("Dwelling", e.Message);
    }

    [Fact]
    public void ARaggedRowIsALoadError()
    {
        var bad = TinyMap.Replace("\"..O........fffff....\"", "\"..O........fffff...\"");
        var e = Assert.Throws<Exception>(() => DefsWith(bad));
        Assert.Contains("features row 10", e.Message);
    }
}
