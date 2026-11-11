using SupportingCast.Sim.Components;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Saving;

namespace SupportingCast.Sim.Tests;

/// <summary>
/// Structures wider than a cell (<see cref="Defs.ThingDef.Size"/>): the 3x3 dragon nest, 1x2 beds,
/// 2x1 benches and 2x2 tables, turned by their facing.
/// </summary>
public class FootprintTests
{
    private static IEnumerable<int3> Around(int3 c)
    {
        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
            yield return new int3(c.X + dx, c.Y + dy, c.Z);
    }

    [Fact]
    public void ANestHoldsItsWholeFootprintFromBlueprintToRubble()
    {
        var sim = TestWorld.Create();
        var nest = sim.Thing("Nest");
        Assert.Equal(3, nest.Footprint);

        var site = sim.PlaceBlueprint(nest, C(10, 10));
        Assert.False(site.IsNull);
        foreach (var c in Around(C(10, 10)))
            Assert.Equal(site, sim.Map.Structure[sim.Map.Idx(c)]);
        Assert.True(sim.Map.Structure[sim.Map.Idx(C(12, 10))].IsNull);

        // Nothing else fits in it, nor another nest overlapping its edge; one beside it does.
        Assert.False(sim.CanPlaceStructure(C(11, 11), sim.Thing("Campfire")));
        Assert.Equal(Entity.Null, sim.PlaceBlueprint(nest, C(12, 10)));
        Assert.True(sim.CanPlaceStructure(C(13, 10), nest));

        var built = sim.CompleteConstruction(site);
        Assert.Equal(C(10, 10), sim.World.Get<Thing>(built).Cell);
        foreach (var c in Around(C(10, 10)))
            Assert.Equal(built, sim.Map.Structure[sim.Map.Idx(c)]);

        sim.DestroyThing(built);
        foreach (var c in Around(C(10, 10)))
            Assert.True(sim.Map.Structure[sim.Map.Idx(c)].IsNull);
    }

    [Fact]
    public void ANestNeedsRoomOnTheMapAndGroundUnderEveryCell()
    {
        var sim = TestWorld.Create();
        var nest = sim.Thing("Nest");
        Assert.False(sim.CanPlaceStructure(C(0, 10), nest));      // its west edge would be off the map
        Assert.True(sim.CanPlaceStructure(C(1, 10), nest));
        sim.SpawnStructure(sim.Thing("WoodWall"), C(6, 6));
        Assert.False(sim.CanPlaceStructure(C(5, 5), nest));       // a wall in its corner
    }

    [Fact]
    public void AWideStructureSurvivesASaveAndLoad()
    {
        var sim = TestWorld.Create();
        var built = sim.SpawnStructure(sim.Thing("Nest"), C(10, 10));
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        var anchor = loaded.Map.Structure[loaded.Map.Idx(C(10, 10))];
        Assert.False(anchor.IsNull);
        foreach (var c in Around(C(10, 10)))
            Assert.Equal(anchor, loaded.Map.Structure[loaded.Map.Idx(c)]);
        Assert.Equal(sim.StateHash(), loaded.StateHash());

        loaded.DestroyThing(anchor);
        foreach (var c in Around(C(10, 10)))
            Assert.True(loaded.Map.Structure[loaded.Map.Idx(c)].IsNull);
        Assert.False(built.IsNull);
    }

    private static HashSet<int3> Cells(Simulation sim, Entity e) => sim.FootprintCells(e).ToHashSet();

    private static HashSet<int3> Holding(Simulation sim, Entity e)
    {
        var held = new HashSet<int3>();
        for (var i = 0; i < sim.Map.Count; i++)
            if (sim.Map.Structure[i] == e)
                held.Add(sim.Map.CellOf(i));
        return held;
    }

    [Theory]
    // A bed's foot is in front of its head: south, then west, north and east as it turns.
    [InlineData(0, 0, 1)]
    [InlineData(1, -1, 0)]
    [InlineData(2, 0, -1)]
    [InlineData(3, 1, 0)]
    public void ABedIsTwoCellsLongAndTurnsWithItsFacing(int facing, int dx, int dy)
    {
        var sim = TestWorld.Create();
        var bed = sim.SpawnStructure(sim.Thing("Bed"), C(10, 10), facing);
        var expected = new HashSet<int3> { C(10, 10), C(10 + dx, 10 + dy) };
        Assert.Equal(expected, Cells(sim, bed));
        Assert.Equal(expected, Holding(sim, bed));

        // The model's front, where the foot is, is the same way the game turns it.
        var front = Footprint.Rotate(0, 1, facing);
        Assert.Equal(new int2(dx, dy), front);

        sim.DestroyThing(bed);
        Assert.Empty(Holding(sim, bed));
    }

    [Fact]
    public void ASquareTableAndAWideBenchGrowFromTheirAnchor()
    {
        var sim = TestWorld.Create();
        var table = sim.SpawnStructure(sim.Thing("SquareTable"), C(10, 10));
        Assert.Equal(new HashSet<int3> { C(10, 10), C(11, 10), C(10, 11), C(11, 11) }, Holding(sim, table));
        var turned = sim.SpawnStructure(sim.Thing("SquareTable"), C(5, 5), facing: 2);
        Assert.Equal(new HashSet<int3> { C(5, 5), C(4, 5), C(5, 4), C(4, 4) }, Holding(sim, turned));

        var bench = sim.SpawnStructure(sim.Thing("CarpenterBench"), C(15, 15), facing: 1);
        Assert.Equal(new HashSet<int3> { C(15, 15), C(15, 16) }, Holding(sim, bench)); // 2 across, turned a quarter

        // Any covered cell blocks something else, and a footprint over a wall won't place.
        Assert.False(sim.CanPlaceStructure(C(11, 11), sim.Thing("Stool")));
        sim.SpawnStructure(sim.Thing("WoodWall"), C(21, 21));
        Assert.True(sim.CanPlaceStructure(C(20, 20), sim.Thing("SquareTable"), facing: 2));
        Assert.False(sim.CanPlaceStructure(C(20, 20), sim.Thing("SquareTable")));
        Assert.Equal(Entity.Null, sim.PlaceBlueprint(sim.Thing("Bed"), C(21, 20)));
    }

    [Fact]
    public void ABlueprintKeepsItsTurnWhenBuilt()
    {
        var sim = TestWorld.Create();
        var site = sim.PlaceBlueprint(sim.Thing("Bed"), C(10, 10), facing: 3);
        Assert.Equal(new HashSet<int3> { C(10, 10), C(11, 10) }, Holding(sim, site));
        var bed = sim.CompleteConstruction(site);
        Assert.Equal(new HashSet<int3> { C(10, 10), C(11, 10) }, Holding(sim, bed));
    }

    [Fact]
    public void ABedFromBeforeBedsGrewTakesItsSecondCellOnLoad()
    {
        var sim = TestWorld.Create();
        var bed = sim.SpawnStructure(sim.Thing("Bed"), C(10, 10));
        var hash = sim.StateHash();
        // As a save from when beds were one cell would have it.
        sim.Map.Structure[sim.Map.Idx(C(10, 11))] = Entity.Null;
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.Equal(new HashSet<int3> { C(10, 10), C(10, 11) }, Holding(loaded, bed));
        Assert.Equal(hash, loaded.StateHash());
    }
}

public class FootprintContainsTests
{
    [Fact]
    public void ContainsUndoesEveryTurn()
    {
        var sim = TestWorld.Create();
        var bed = sim.Thing("Bed");
        for (var facing = 0; facing < 4; facing++)
            foreach (var c in Maps.Footprint.Cells(sim.Map, bed, C(10, 10), facing))
                Assert.True(Maps.Footprint.Contains(bed, C(10, 10), facing, c), $"facing {facing}, {c}");
    }
}
