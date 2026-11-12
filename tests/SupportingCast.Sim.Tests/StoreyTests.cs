using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Saving;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Tests;

/// <summary>Two-storey preset buildings (<see cref="BuildingDef.UpperLayout"/>): the inn's rooms upstairs.</summary>
public class StoreyTests
{
    private static Simulation World() => TestWorld.Create(30, 30, depth: 5);

    private static (Simulation Sim, Entity Inn, PresetBuilding State) FinishedInn(int facing = 0)
    {
        var sim = World();
        var inn = Buildings.SpawnFinished(sim, sim.Defs.Building("Inn"), C(15, 15), facing);
        Assert.False(inn.IsNull);
        sim.RebuildDerivedIfNeeded();
        return (sim, inn, sim.World.Get<PresetBuilding>(inn));
    }

    private static string DefAt(Simulation sim, Entity[] layer, int3 cell) =>
        layer[sim.Map.Idx(cell)] is { IsNull: false } e ? sim.World.Get<Thing>(e).Def.Name : "";

    [Fact]
    public void TheInnStandsWithItsUpperFloorStairAndRoof()
    {
        var (sim, _, b) = FinishedInn();
        var stair = Buildings.CellOf(sim.Map, b.Def, b.Anchor, 0, 1, 7);
        Assert.Equal("InnStair", DefAt(sim, sim.Map.Structure, stair));
        Assert.Equal("", DefAt(sim, sim.Map.Floor, stair + new int3(0, 0, 1))); // the stairwell
        var room = Buildings.CellOf(sim.Map, b.Def, b.Anchor, 0, 5, 6, level: 1);
        Assert.Equal(2, room.Z);
        Assert.Equal("Floorboards", DefAt(sim, sim.Map.Floor, room));
        Assert.Equal("TimberWall", DefAt(sim, sim.Map.Structure, Buildings.CellOf(sim.Map, b.Def, b.Anchor, 0, 4, 1, level: 1)));
        Assert.Equal("ThatchRoofPart", DefAt(sim, sim.Map.Floor, room + new int3(0, 0, 1)));
        Assert.Equal(6, sim.World.Query<Thing>().Count(e => sim.World.Get<Thing>(e) is { Def.Name: "Bed", Cell.Z: 2 })); // three over the kitchen, three over the common room
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void SomeoneWalksFromTheStreetToAnUpstairsBed(int facing)
    {
        var (sim, _, b) = FinishedInn(facing);
        var bedside = Buildings.CellOf(sim.Map, b.Def, b.Anchor, b.Facing, 2, 2, level: 1); // in the first guest room
        var street = sim.SpiralFrom(C(3, 3), 3).First(sim.Map.Passable);
        Assert.True(sim.Map.Passable(bedside));
        var path = sim.Pathfinder.FindPath(street, bedside, PathEnd.OnTile);
        Assert.NotNull(path);
        var stair = Buildings.CellOf(sim.Map, b.Def, b.Anchor, b.Facing, 1, 7);
        Assert.Contains(stair, path!);
    }

    [Fact]
    public void ItsPhasesGoUpOneStoreyAtATime()
    {
        var sim = World();
        var b = Buildings.Place(sim, sim.Defs.Building("Inn"), C(15, 15), 0);
        var seen = new List<BuildPhase>();
        for (var guard = 0; guard < 40 && sim.World.Get<PresetBuilding>(b).Phase != BuildPhase.Done; guard++)
        {
            var phase = sim.World.Get<PresetBuilding>(b).Phase;
            if (!seen.Contains(phase))
                seen.Add(phase);
            foreach (var p in Buildings.Parts(sim, b))
                if (sim.World.Has<Construction>(p))
                    sim.CompleteConstruction(p);
            sim.RebuildDerivedIfNeeded();
        }
        Assert.Equal(BuildPhase.Done, sim.World.Get<PresetBuilding>(b).Phase);
        Assert.Equal([BuildPhase.Foundation, BuildPhase.Walls, BuildPhase.UpperFloor, BuildPhase.UpperWalls, BuildPhase.Roof, BuildPhase.Furnish], seen);
    }

    [Fact]
    public void AOneStoreyBuildingHasNoUpperPhases()
    {
        var sim = World();
        var b = new PresetBuilding { Def = sim.Defs.Building("Dwelling"), Anchor = C(15, 15) };
        Assert.Empty(Buildings.Stamps(sim.Map, b, BuildPhase.UpperFloor));
        Assert.Empty(Buildings.Stamps(sim.Map, b, BuildPhase.UpperWalls));
        Assert.All(Buildings.Stamps(sim.Map, b, BuildPhase.Roof), s => Assert.Equal(2, s.Cell.Z));
    }

    [Fact]
    public void ItNeedsRoomForBothStoreysAndTheRoof()
    {
        var sim = TestWorld.Create(30, 30, depth: 3); // the roof would stick out of the map
        Assert.NotNull(Buildings.PlacementProblem(sim, sim.Defs.Building("Inn"), C(15, 15), 0));
        Assert.Null(Buildings.PlacementProblem(sim, sim.Defs.Building("Dwelling"), C(15, 15), 0));
    }

    [Fact]
    public void TheInnSavesAndLoads()
    {
        var (sim, inn, _) = FinishedInn();
        var loaded = SaveGame.Read(SaveGame.Write(sim), sim.Defs).Sim;
        var b = loaded.World.Get<PresetBuilding>(inn);
        Assert.Equal(BuildPhase.Done, b.Phase);
        Assert.Equal("Inn", b.Def.Name);
        Assert.Equal(inn, Buildings.At(loaded, Buildings.CellOf(loaded.Map, b.Def, b.Anchor, 0, 5, 6, level: 1)));
    }
}
