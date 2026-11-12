using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Environment;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tests;

public class EnvironmentTests
{
    [Fact]
    public void EnclosedSpaceBecomesRoofedIndoorRoom()
    {
        var sim = TestWorld.Create();
        sim.BuildWallRing(C(5, 5), C(11, 10), door: C(8, 10));

        var room = sim.Rooms.RoomAt(C(8, 7));
        Assert.NotNull(room);
        Assert.True(room.Indoors);
        Assert.Equal(5 * 4, room.TileCount);
        Assert.True(sim.Map.Roofed[sim.Map.Idx(C(8, 7))]);
        Assert.True(sim.Map.Roofed[sim.Map.Idx(C(5, 5))]); // the wall itself
        Assert.False(sim.Map.Roofed[sim.Map.Idx(C(20, 20))]);
        Assert.False(sim.Rooms.RoomAt(C(20, 20))!.Indoors);
    }

    [Fact]
    public void CampfireWarmsAnEnclosedRoomFarMoreThanOutdoors()
    {
        var sim = TestWorld.Create();
        sim.Climate.BaseTemperature = 0;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 0f);

        sim.BuildWallRing(C(4, 4), C(10, 10), door: C(7, 10));
        var inside = sim.SpawnStructure(sim.Thing("Campfire"), C(7, 7));
        var outside = sim.SpawnStructure(sim.Thing("Campfire"), C(30, 30));
        sim.World.Get<Fueled>(inside).Fuel = 1000;
        sim.World.Get<Fueled>(outside).Fuel = 1000;

        sim.TickMany(SimClock.TicksPerHour * 6);

        var roomTemp = sim.Rooms.RoomAt(C(7, 7))!.AverageTemperature;
        var nextToInside = sim.TemperatureAt(C(7, 8));
        var nextToOutside = sim.TemperatureAt(C(30, 31));
        var farAway = sim.TemperatureAt(C(20, 30));

        Assert.InRange(roomTemp, 18f, 60f);
        Assert.True(nextToInside > 28f, $"egg spot next to indoor fire should be hot, was {nextToInside:F1}");
        Assert.True(nextToOutside < nextToInside - 10f, $"outdoor fire {nextToOutside:F1} vs indoor {nextToInside:F1}");
        Assert.InRange(farAway, -1f, 2f);
    }

    /// <summary>A 0°C night with a lit campfire at (20, 20) in the open, settled for a few hours.</summary>
    private static (Simulation Sim, Entity Fire) ColdNightByAFire()
    {
        var sim = TestWorld.Create();
        sim.Climate.BaseTemperature = 0;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 0f);
        var fire = sim.SpawnStructure(sim.Thing("Campfire"), C(20, 20));
        sim.World.Get<Fueled>(fire).Fuel = 1000;
        sim.TickMany(SimClock.TicksPerHour * 4);
        return (sim, fire);
    }

    [Fact]
    public void AnOutdoorFireWarmsWhoeverSitsBesideIt()
    {
        var (sim, fire) = ColdNightByAFire();
        float Radiant(int3 c) => Warmth.FeltAt(sim, c) - sim.TemperatureAt(c);

        // The air hardly warms a few tiles off in the open, but its warmth is felt.
        Assert.True(sim.TemperatureAt(C(23, 20)) < 3f, $"air 3 tiles off {sim.TemperatureAt(C(23, 20)):F1}");
        Assert.Equal(8f, Radiant(C(21, 20)), 3);
        Assert.Equal(6f, Radiant(C(22, 22)), 3);
        Assert.Equal(4f, Radiant(C(23, 20)), 3);
        Assert.Equal(0f, Radiant(C(24, 20)), 3);

        // Not through a wall into a room, and not once it's out.
        sim.BuildWallRing(C(21, 15), C(25, 19));
        Assert.Equal(0f, Radiant(C(22, 18)), 3);
        sim.World.Get<Fueled>(fire).Fuel = 0;
        Assert.Equal(0f, Radiant(C(21, 20)), 3);
    }

    [Fact]
    public void ColdSleepIsSlowerSleep()
    {
        var tol = new Defs.TempTolerance(15, 90, 0, 400);
        Assert.Equal(1f, NeedsSystem.ColdSleep(20, tol));
        Assert.Equal(1f, NeedsSystem.ColdSleep(15, tol));
        Assert.Equal(0.75f, NeedsSystem.ColdSleep(7.5f, tol), 3);
        Assert.Equal(NeedsSystem.ColdSleepFloor, NeedsSystem.ColdSleep(-10, tol));
    }

    [Fact]
    public void FireSpreadsOnlyThroughFlammablesAndBurnsOut()
    {
        // Fire is chancy: along a line of walls it dies out about half the time. A seed where it takes.
        var sim = TestWorld.Create(30, 10, seed: 3);
        var dirt = sim.Defs.Block("Dirt");
        Array.Fill(sim.Map.Block, dirt, 0, sim.Map.LayerSize);
        // A row of wooden walls touching a stone wall.
        for (var x = 2; x < 8; x++)
            sim.SpawnStructure(sim.Thing("WoodWall"), C(x, 5));
        sim.SpawnStructure(sim.Thing("StoneWall"), C(8, 5));
        sim.SpawnStructure(sim.Thing("WoodWall"), C(20, 5)); // isolated, must not burn
        sim.RebuildDerivedIfNeeded();

        Assert.True(sim.Fire.Ignite(sim, C(2, 5), 1f));
        Assert.False(sim.Fire.Ignite(sim, C(15, 5))); // bare dirt can't burn

        sim.TickMany(SimClock.TicksPerHour * 12);

        Assert.Empty(sim.Fire.BurningTiles);
        for (var x = 2; x < 8; x++)
            Assert.True(sim.Map.Structure[sim.Map.Idx(C(x, 5))].IsNull, $"wood wall at {x} should have burned");
        Assert.False(sim.Map.Structure[sim.Map.Idx(C(8, 5))].IsNull);
        Assert.False(sim.Map.Structure[sim.Map.Idx(C(20, 5))].IsNull);
    }

    [Fact]
    public void BurningGrassTurnsToDirt()
    {
        var sim = TestWorld.Create(20, 20);
        sim.Fire.Ignite(sim, C(10, 10), 1f);
        sim.TickMany(SimClock.TicksPerHour * 2);
        var dirt = sim.Defs.Block("Dirt");
        Assert.Contains(sim.Map.Block, t => t == dirt);
    }

    /// <summary>
    /// Two stacked 5x5 rooms (walls at z = 1 and 2, a floor between them) with a campfire on one of
    /// them. With <paramref name="stairs"/>, a stairwell opens the floor above the stairs.
    /// </summary>
    private static Simulation StackedRooms(bool stairs, bool fireUpstairs = false)
    {
        var sim = TestWorld.Create(20, 20, depth: 5);
        sim.Climate.BaseTemperature = 0;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 0f);
        int3 Up(int x, int y) => new(x, y, 2);
        sim.BuildWallRing(C(4, 4), C(10, 10), "StoneWall");
        for (var y = 4; y <= 10; y++)
        for (var x = 4; x <= 10; x++)
            if (!(stairs && x == 6 && y == 6))
                sim.SpawnStructure(sim.Thing("WoodFloor"), Up(x, y));
        sim.BuildWallRing(Up(4, 4), Up(10, 10), "StoneWall");
        if (stairs)
            sim.SpawnStructure(sim.Thing("WoodStairs"), C(6, 6)); // up onto (7, 6) upstairs
        sim.SpawnStructure(sim.Thing("Campfire"), fireUpstairs ? Up(8, 8) : C(8, 8));
        sim.RebuildDerivedIfNeeded();
        return sim;
    }

    private static float RoomTemp(Simulation sim, int3 cell)
    {
        sim.Rooms.UpdateTemperatures();
        return sim.Rooms.RoomAt(cell)!.AverageTemperature;
    }

    [Fact]
    public void HeatGoesUpThroughFloorsMoreThanDownAndStairwellsAddToIt()
    {
        var below = StackedRooms(stairs: false);
        var above = StackedRooms(stairs: false, fireUpstairs: true);
        var stairwell = StackedRooms(stairs: true);
        Assert.True(stairwell.Regions.CanReach(C(8, 7), new int3(8, 8, 2), PathEnd.OnTile), "stairs should connect the floors");

        foreach (var sim in (Simulation[])[below, above, stairwell])
            sim.TickMany(SimClock.HoursToTicks(3));
        var risen = RoomTemp(below, new int3(8, 7, 2));
        var sunk = RoomTemp(above, C(8, 7));
        var throughStairs = RoomTemp(stairwell, new int3(8, 7, 2));
        Assert.True(risen > sunk + 2f, $"heat rising through the floor {risen:F1}°C vs sinking {sunk:F1}°C");
        Assert.True(throughStairs > risen + 0.5f, $"stairwell {throughStairs:F1}°C vs sealed floor {risen:F1}°C");
    }

    [Fact]
    public void FireClimbsOntoAFloorAbove()
    {
        var sim = TestWorld.Create(20, 20, depth: 5);
        var dirt = sim.Defs.Block("Dirt");
        Array.Fill(sim.Map.Block, dirt, 0, sim.Map.LayerSize);
        sim.SpawnStructure(sim.Thing("WoodWall"), C(10, 10));
        var floor = sim.SpawnStructure(sim.Thing("WoodFloor"), new int3(10, 10, 2));
        sim.RebuildDerivedIfNeeded();

        Assert.True(sim.Fire.Ignite(sim, C(10, 10), 1f));
        sim.TickMany(SimClock.HoursToTicks(12));
        Assert.False(sim.World.IsAlive(floor), "the floor over the burning wall should have burned too");
    }

    [Fact]
    public void SmokeRisesOutdoorsAndIsTrappedIndoors()
    {
        // Fire is chancy (how far it takes varies a lot): seeds where it catches.
        var outdoors = TestWorld.Create(20, 20, seed: 5, depth: 8);
        outdoors.SpawnStructure(outdoors.Thing("Oak"), C(10, 10));
        outdoors.RebuildDerivedIfNeeded();
        outdoors.Fire.Ignite(outdoors, C(10, 10), 1f);
        outdoors.TickMany(SimClock.HoursToTicks(1));
        Assert.True(outdoors.Map.Smoke[outdoors.Map.Idx(new int3(10, 10, 4))] > 0.05f, "a smoke column should rise above an outdoor fire");

        // A walled room under a rock ceiling (a dug-out cave room). Under a wooden floor the fire
        // would climb into the floor and burn its way out, which is fine, but not this test.
        var indoors = TestWorld.Create(20, 20, seed: 7, depth: 5);
        indoors.BuildWallRing(C(4, 4), C(10, 10), "StoneWall");
        indoors.FillBlocks(new int3(4, 4, 2), new int3(10, 10, 2));
        indoors.SpawnItem(indoors.Thing("Wood"), 40, C(7, 8));
        indoors.Fire.Ignite(indoors, C(7, 8), 1f);
        indoors.TickMany(SimClock.HoursToTicks(1));
        var inside = indoors.Map.Smoke[indoors.Map.Idx(C(7, 7))];
        var outside = indoors.Map.Smoke[indoors.Map.Idx(C(15, 15))];
        Assert.True(inside > 0.3f, $"smoke should fill the closed room, is {inside:F2}");
        Assert.True(outside < inside * 0.1f, $"the walls should keep it in ({outside:F2} outside)");
    }
}
