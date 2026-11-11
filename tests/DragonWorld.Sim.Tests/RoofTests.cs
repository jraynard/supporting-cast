using DragonWorld.Sim.Components;
using DragonWorld.Sim.Saving;

namespace DragonWorld.Sim.Tests;

/// <summary>
/// Built roofs (<see cref="Defs.BuildingProps.IsRoof"/>): a porch or lean-to off a wall, in the floor
/// layer on top of the walls, sheltering what's below without being walked on.
/// </summary>
public class RoofTests
{
    /// <summary>The level above the test map's walk level: where a roof on a one-storey wall sits.</summary>
    private static int3 Up(int x, int y) => new(x, y, 2);

    private static Simulation WithWall()
    {
        var sim = TestWorld.Create(depth: 4);
        sim.SpawnStructure(sim.Thing("WoodWall"), C(10, 10));
        sim.RebuildDerivedIfNeeded();
        return sim;
    }

    [Fact]
    public void ARoofReachesThreeCellsFromAWallAndNoFurther()
    {
        var sim = WithWall();
        var roof = sim.Thing("ThatchRoof");
        Assert.True(roof.IsRoof && roof.IsFloor);
        Assert.True(sim.CanPlaceStructure(Up(10, 10), roof), "on the wall itself");
        Assert.True(sim.CanPlaceStructure(Up(13, 10), roof), "three cells out");
        Assert.True(sim.CanPlaceStructure(Up(13, 13), roof), "three out, diagonally");
        Assert.False(sim.CanPlaceStructure(Up(14, 10), roof), "four out is too far");
        Assert.False(sim.CanPlaceStructure(Up(20, 20), roof), "nothing near");
        Assert.False(sim.CanPlaceStructure(C(11, 10), roof), "not on the ground: it goes over the wall tops");
    }

    [Fact]
    public void ARoofSheltersTheCellBelowButIsntAFloor()
    {
        var sim = WithWall();
        var below = sim.Map.Idx(C(12, 10));
        Assert.False(sim.Map.CoveredAbove(below));
        var roof = sim.SpawnStructure(sim.Thing("ThatchRoof"), Up(12, 10));
        sim.RebuildDerivedIfNeeded();
        sim.Rooms.UpdateIfNeeded();

        var at = sim.Map.Idx(Up(12, 10));
        Assert.True(sim.Map.HasRoof[at]);
        Assert.False(sim.Map.HasFloor[at]);
        Assert.False(sim.Map.Standable(at), "nobody walks on a roof");
        Assert.True(sim.Map.CoveredAbove(below));
        Assert.True(sim.Map.Roofed[below]);
        Assert.False(sim.Map.Roofed[sim.Map.Idx(C(14, 10))], "beside it is still open sky");

        // Nothing goes on top of it, and it's taken apart like a floor.
        Assert.False(sim.CanPlaceStructure(Up(12, 10), sim.Thing("Stool")));
        Assert.False(sim.CanPlaceStructure(Up(12, 10), sim.Thing("WoodFloor")));
        Assert.Equal(roof, sim.DeconstructTarget(Up(12, 10)));
        sim.DestroyThing(roof);
        sim.RebuildDerivedIfNeeded();
        Assert.False(sim.Map.HasRoof[at]);
        Assert.False(sim.Map.CoveredAbove(below));
    }

    [Fact]
    public void TheWallHoldingARoofUpCantComeDownFirst()
    {
        var sim = WithWall();
        var wall = sim.Map.Structure[sim.Map.Idx(C(10, 10))];
        sim.SpawnStructure(sim.Thing("ThatchRoof"), Up(12, 10));
        Assert.False(sim.CanDeconstruct(wall, out var reason));
        Assert.Contains("roof", reason);

        // A second wall in reach of it frees the first.
        sim.SpawnStructure(sim.Thing("WoodWall"), C(14, 10));
        Assert.True(sim.CanDeconstruct(wall, out _));
    }

    [Fact]
    public void ARoofBlueprintWaitsForAFinishedWall()
    {
        var sim = TestWorld.Create(depth: 4);
        var wallSite = sim.PlaceBlueprint(sim.Thing("WoodWall"), C(10, 10));
        var roofSite = sim.PlaceBlueprint(sim.Thing("ThatchRoof"), Up(11, 10));
        Assert.False(roofSite.IsNull, "a planned wall holds up a planned roof");
        Assert.False(sim.CanBuildNow(roofSite));
        sim.CompleteConstruction(wallSite);
        Assert.True(sim.CanBuildNow(roofSite));
        sim.CompleteConstruction(roofSite);
        Assert.True(sim.Map.HasRoof[sim.Map.Idx(Up(11, 10))]);
    }

    [Fact]
    public void ARoofSurvivesASave()
    {
        var sim = WithWall();
        sim.SpawnStructure(sim.Thing("ThatchRoof"), Up(11, 10));
        sim.RebuildDerivedIfNeeded();
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.True(loaded.Map.HasRoof[loaded.Map.Idx(Up(11, 10))]);
        Assert.True(loaded.Map.CoveredAbove(loaded.Map.Idx(C(11, 10))));
        Assert.Equal(sim.StateHash(), loaded.StateHash());
        loaded.TickMany(SimClock.TicksPerHour);
        sim.TickMany(SimClock.TicksPerHour);
        Assert.Equal(sim.StateHash(), loaded.StateHash());
    }

    [Fact]
    public void AVillagerBuildsARoofFromTheGroundBelow()
    {
        var sim = WithWall();
        var builder = sim.SpawnCreature(sim.Defs.Creature("Human"), "Roofer", C(5, 5), ageDays: 25 * 365);
        Array.Fill(sim.World.Get<Needs>(builder).Values, 1f);
        sim.SpawnItem(sim.Thing("Wood"), 10, C(6, 6));
        sim.ApplyNow(new AreaOrderCommand(AreaTool.Build, new int2(11, 10), new int2(12, 11), 2, 2, sim.Thing("ThatchRoof")));
        sim.TickMany(SimClock.HoursToTicks(3));
        foreach (var c in new[] { Up(11, 10), Up(12, 10), Up(11, 11), Up(12, 11) })
            Assert.True(sim.Map.HasRoof[sim.Map.Idx(c)], $"roof at {c}");
        Assert.True(sim.Map.Roofed[sim.Map.Idx(C(12, 11))]);
    }
}

/// <summary>The <c>--furniture-demo</c> scene: every wide piece and the porch roof fit on open ground.</summary>
public class FurnitureShowcaseTests
{
    [Fact]
    public void EveryPieceFits()
    {
        var sim = TestWorld.Create(40, 40, depth: 4);
        Scenarios.FurnitureShowcase.Setup(sim, C(20, 20));
        var names = sim.World.Query<Thing>().Select(e => sim.World.Get<Thing>(e).Def.Name).ToList();
        Assert.Equal(2, names.Count(n => n == "Bed"));
        foreach (var n in new[] { "StrawPallet", "Table", "LongTable", "SquareTable", "CarpenterBench", "MasonBench", "TanningRack", "Sawhorse" })
            Assert.Contains(n, names);
        Assert.Equal(24, names.Count(n => n == "ThatchRoof"));
        sim.Rooms.UpdateIfNeeded();
        Assert.True(sim.Rooms.RoomAt(C(9, 18))!.Indoors);
    }
}
