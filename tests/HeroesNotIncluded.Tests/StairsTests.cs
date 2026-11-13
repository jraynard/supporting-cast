using Brutal.Numerics;
using HeroesNotIncluded.Rendering;
using HeroesNotIncluded.Sim.Defs;

namespace HeroesNotIncluded.Tests;

/// <summary>Stairs as drawn and walked (<see cref="Stairs"/>): the flight's layout, and feet on its treads.</summary>
public class StairsTests
{
    /// <summary>A landing a level up (walls under a floor) east of a stair at (5, 5, 1) that climbs +X.</summary>
    private static HeroesNotIncluded.Sim.Simulation Flight(bool clearFoot = true)
    {
        var defs = DefDatabase.LoadDirectory(DevPaths.Defs!);
        var sim = HeroesNotIncluded.Sim.Simulation.CreateStandard(defs, 20, 20, 1, depth: 4);
        for (var y = 4; y <= 6; y++)
        for (var x = 6; x <= 7; x++)
        {
            sim.SpawnStructure(defs.Thing("WoodWall"), new int3(x, y, 1));
            sim.SpawnStructure(defs.Thing("WoodFloor"), new int3(x, y, 2));
        }
        sim.SpawnStructure(defs.Thing("WoodStairs"), new int3(5, 5, 1));
        if (!clearFoot)
            sim.SpawnStructure(defs.Thing("Chest"), new int3(4, 5, 1));
        sim.RebuildDerivedIfNeeded();
        return sim;
    }

    [Fact]
    public void AFlightLeadsOutIntoAClearFootAndClimbsTowardsTheLanding()
    {
        var sim = Flight();
        var def = sim.Defs.Thing("WoodStairs");
        var (climb, leads, steps) = Stairs.Flight(sim, 5, 5, 1, def);
        Assert.Equal(new int2(1, 0), climb);
        Assert.True(leads);
        Assert.Equal(6, steps);

        var blocked = Flight(clearFoot: false);
        var (_, leadsBlocked, stepsBlocked) = Stairs.Flight(blocked, 5, 5, 1, def);
        Assert.False(leadsBlocked, "something stands at its foot");
        Assert.Equal(def.Steps, stepsBlocked);
    }

    [Fact]
    public void AWalkerIsDrawnOnTheTreadsAllTheWayUp()
    {
        var sim = Flight();
        // The sim's walk: level across the foot and the stair's first half, then lifted to the landing.
        var last = 1f;
        for (var x = 4.5f; x <= 6.5f; x += 0.05f)
        {
            var simZ = x < 5.5f ? 1f : 1f + (x - 5.5f);
            var drawn = Stairs.FloorAt(sim, new float3(x, 5.5f, simZ)) ?? simZ;
            Assert.True(drawn >= last - 0.001f, $"at x {x:F2} drawn {drawn:F2} after {last:F2}: never down on the way up");
            last = drawn;
            if (x is > 5.05f and < 5.95f)
                Assert.True(drawn > 1.1f && drawn <= 2.001f, $"on the stair at x {x:F2}, drawn at {drawn:F2}");
        }
        Assert.Equal(2f, last, 3);
        Assert.Equal(1f, Stairs.FloorAt(sim, new float3(4.2f, 5.5f, 1f)) ?? 1f, 3); // short of the flight: on the floor
    }
}
