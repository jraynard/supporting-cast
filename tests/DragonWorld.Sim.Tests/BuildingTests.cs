using DragonWorld.Sim.Ai;
using DragonWorld.Sim.Components;
using DragonWorld.Sim.Defs;

namespace DragonWorld.Sim.Tests;

/// <summary>Multi-storey building: floors, support, ladders, upper rooms.</summary>
public class BuildingTests
{
    /// <summary>Upper level of a <see cref="TestWorld.Create"/> map.</summary>
    private static int3 Up(int x, int y) => new(x, y, 2);

    private static Simulation World()
    {
        var sim = TestWorld.Create(20, 20, depth: 5);
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 20f);
        return sim;
    }

    [Fact]
    public void FloorsNeedSupportNearby()
    {
        var sim = World();
        var floor = sim.Thing("WoodFloor");
        Assert.False(sim.CanPlaceStructure(Up(10, 10), floor), "nothing below or near");

        sim.SpawnStructure(sim.Thing("StoneWall"), C(10, 10));
        sim.RebuildDerivedIfNeeded();
        Assert.True(sim.CanPlaceStructure(Up(10, 10), floor), "on top of a wall");
        Assert.True(sim.CanPlaceStructure(Up(12, 12), floor), "two cells out from the wall");
        Assert.False(sim.CanPlaceStructure(Up(13, 10), floor), "three cells out is too far");
    }

    [Fact]
    public void FloorPlannedOnAWallBlueprintWaitsForTheWall()
    {
        var sim = World();
        var wall = sim.PlaceBlueprint(sim.Thing("WoodWall"), C(10, 10));
        var floor = sim.PlaceBlueprint(sim.Thing("WoodFloor"), Up(10, 11));
        Assert.False(wall.IsNull);
        Assert.False(floor.IsNull);
        Assert.False(sim.CanBuildNow(floor));

        sim.CompleteConstruction(wall);
        Assert.True(sim.CanBuildNow(floor));
    }

    [Fact]
    public void VillagerBuildsAnUpperFloorAndStoresThingsOnIt()
    {
        var sim = World();
        sim.BuildWallRing(C(4, 4), C(8, 8), "StoneWall", door: C(6, 8));
        sim.SpawnStructure(sim.Thing("Ladder"), C(6, 6));
        for (var y = 4; y <= 8; y++)
        for (var x = 4; x <= 8; x++)
            Assert.False(sim.PlaceBlueprint(sim.Thing("WoodFloor"), Up(x, y)).IsNull, $"floor at {x},{y}");
        sim.SpawnItem(sim.Thing("Wood"), 50, C(12, 6));
        var pawn = sim.SpawnCreature(sim.Defs.Creature("Human"), "Builder", C(12, 8));
        Array.Fill(sim.World.Get<Needs>(pawn).Values, 1f);
        var mind = sim.World.Get<Mind>(pawn);
        Array.Fill(mind.WorkPriority, 0);
        mind.WorkPriority[(int)WorkType.Construct] = 1;
        mind.WorkPriority[(int)WorkType.Haul] = 2;

        sim.TickMany(SimClock.HoursToTicks(6));
        Assert.Empty(sim.World.Query<Construction>());
        Assert.True(sim.Map.Standable(sim.Map.Idx(Up(5, 5))));
        Assert.True(sim.Map.Roofed[sim.Map.Idx(C(5, 5))], "the room below now has a real ceiling");

        // A stockpile upstairs: the leftover wood gets carried up the ladder.
        sim.SpawnItem(sim.Thing("Wood"), 20, C(12, 6));
        for (var x = 5; x <= 7; x++)
            sim.Map.SetZone(sim.Map.Idx(Up(x, 5)), 1);
        sim.TickMany(SimClock.HoursToTicks(2));
        var upstairs = sim.SpawnedItems().Where(e => sim.World.Get<Thing>(e).Cell.Z == 2).ToList();
        Assert.NotEmpty(upstairs);
        Assert.All(upstairs, e => Assert.True(AiQueries.InStockpile(sim, sim.World.Get<Thing>(e).Cell)));
    }

    [Fact]
    public void UpperRoomHoldsHeat()
    {
        var sim = World();
        sim.Climate.BaseTemperature = 0;
        Array.Fill(sim.Map.Temperature, 0f);
        sim.BuildWallRing(C(4, 4), C(10, 10), "StoneWall");
        for (var y = 4; y <= 10; y++)
        for (var x = 4; x <= 10; x++)
            sim.SpawnStructure(sim.Thing("WoodFloor"), Up(x, y));
        sim.BuildWallRing(Up(4, 4), Up(10, 10), "StoneWall");
        sim.SpawnStructure(sim.Thing("Campfire"), Up(7, 7));
        sim.RebuildDerivedIfNeeded();

        var room = sim.Rooms.RoomAt(Up(7, 7));
        Assert.NotNull(room);
        Assert.True(room.Indoors);
        sim.TickMany(SimClock.HoursToTicks(4));
        Assert.True(sim.TemperatureAt(Up(7, 8)) > 25f, $"upper room should be warm, is {sim.TemperatureAt(Up(7, 8)):F1}");
        Assert.True(sim.TemperatureAt(C(15, 15)) < 5f);
    }

    [Fact]
    public void ThingsFallWhenTheirFloorIsLost()
    {
        var sim = World();
        sim.SpawnStructure(sim.Thing("StoneWall"), C(10, 10));
        var floor = sim.SpawnStructure(sim.Thing("WoodFloor"), Up(11, 10)); // held up by the wall next door
        var bed = sim.SpawnStructure(sim.Thing("Bed"), Up(11, 10));
        sim.SpawnItem(sim.Thing("Wood"), 10, Up(11, 10));
        sim.RebuildDerivedIfNeeded();
        Assert.True(sim.Map.Standable(sim.Map.Idx(Up(11, 10))));

        sim.DestroyThing(floor); // burned away
        sim.RebuildDerivedIfNeeded();

        Assert.False(sim.World.IsAlive(bed));
        var wood = Assert.Single(sim.SpawnedItems());
        Assert.Equal(1, sim.World.Get<Thing>(wood).Cell.Z);
    }

    [Fact]
    public void WallsCanGoAboveADoor()
    {
        var sim = World();
        var wall = sim.Thing("WoodWall");
        sim.SpawnStructure(wall, C(9, 10));
        sim.SpawnStructure(sim.Thing("Door"), C(10, 10));
        sim.SpawnStructure(wall, C(11, 10));
        sim.RebuildDerivedIfNeeded();
        Assert.True(sim.CanPlaceStructure(Up(10, 10), wall), "over the door");
        Assert.False(sim.CanPlaceStructure(Up(12, 10), wall), "over nothing");

        // Planned over a door blueprint, it waits for the door.
        var door = sim.PlaceBlueprint(sim.Thing("Door"), C(14, 10));
        var lintel = sim.PlaceBlueprint(wall, Up(14, 10));
        Assert.False(lintel.IsNull);
        Assert.False(sim.CanBuildNow(lintel));
        sim.CompleteConstruction(door);
        Assert.True(sim.CanBuildNow(lintel));
    }

    [Fact]
    public void FloorsCantCloseAStairwell()
    {
        var sim = World();
        sim.SpawnStructure(sim.Thing("StoneWall"), C(10, 9)); // support nearby
        var stairs = sim.PlaceBlueprint(sim.Thing("WoodStairs"), C(10, 10));
        Assert.False(stairs.IsNull);
        Assert.False(sim.CanPlaceStructure(Up(10, 10), sim.Thing("WoodFloor")), "over planned stairs");
        sim.CompleteConstruction(stairs);
        Assert.False(sim.CanPlaceStructure(Up(10, 10), sim.Thing("WoodFloor")), "over built stairs");
        Assert.True(sim.CanPlaceStructure(Up(10, 11), sim.Thing("WoodFloor")), "next to them is fine");

        sim.SpawnStructure(sim.Thing("WoodFloor"), Up(11, 9));
        Assert.False(sim.CanPlaceStructure(new int3(11, 9, 1), sim.Thing("WoodStairs")), "stairs under a floor");
    }

    [Fact]
    public void VillagerTakesAWallApartForHalfTheWood()
    {
        var sim = World();
        var wall = sim.SpawnStructure(sim.Thing("WoodWall"), C(10, 10));
        sim.RebuildDerivedIfNeeded();
        sim.Map.Designate(sim.Map.Idx(C(10, 10)), Maps.Designation.Deconstruct);
        var pawn = sim.SpawnCreature(sim.Defs.Creature("Human"), "Builder", C(5, 5));
        Array.Fill(sim.World.Get<Needs>(pawn).Values, 1f);
        var mind = sim.World.Get<Mind>(pawn);
        Array.Fill(mind.WorkPriority, 0);
        mind.WorkPriority[(int)WorkType.Construct] = 1;

        sim.TickMany(SimClock.HoursToTicks(1));
        Assert.False(sim.World.IsAlive(wall));
        var wood = sim.SpawnedItems().Where(e => sim.World.Get<Thing>(e).Def.Name == "Wood").Sum(e => sim.World.Get<Stack>(e).Count);
        Assert.Equal(3, wood); // half of 5, rounded up
        Assert.Equal(Maps.Designation.None, sim.Map.Designations[sim.Map.Idx(C(10, 10))]);
    }

    [Fact]
    public void ThingsComeApartTopDown()
    {
        var sim = World();
        var wall = sim.SpawnStructure(sim.Thing("StoneWall"), C(10, 10));
        var floor = sim.SpawnStructure(sim.Thing("WoodFloor"), Up(11, 10)); // held up only by the wall
        var bed = sim.SpawnStructure(sim.Thing("Bed"), Up(11, 10));
        sim.RebuildDerivedIfNeeded();

        Assert.False(sim.CanDeconstruct(wall, out var why), "the wall holds up the floor");
        Assert.Contains("floor", why);
        Assert.False(sim.CanDeconstruct(floor, out _), "the bed is on the floor");
        Assert.Equal(bed, sim.DeconstructTarget(Up(11, 10))); // the bed comes first

        sim.Deconstruct(bed);
        Assert.True(sim.CanDeconstruct(floor, out _));
        sim.Deconstruct(floor);
        Assert.True(sim.CanDeconstruct(wall, out _));
    }
}

public class FacingTests
{
    [Fact]
    public void ABlueprintsFacingCarriesIntoTheBuildingAndSurvivesASave()
    {
        var sim = TestWorld.Create();
        var site = sim.PlaceBlueprint(sim.Thing("Bed"), C(5, 5), facing: 3);
        Assert.Equal(3, sim.World.Get<Components.Thing>(site).Facing);
        var bed = sim.CompleteConstruction(site);
        Assert.Equal(3, sim.World.Get<Components.Thing>(bed).Facing);

        var (loaded, _) = Saving.SaveGame.Read(Saving.SaveGame.Write(sim), sim.Defs);
        var again = loaded.Map.Structure[loaded.Map.Idx(C(5, 5))];
        Assert.Equal(3, loaded.World.Get<Components.Thing>(again).Facing);
    }
}
