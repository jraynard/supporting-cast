using Brutal.Numerics;
using SupportingCast.Sim.Defs;

namespace SupportingCast.Tests;

/// <summary>Cutaway walls (<see cref="WorldMesher.CutAt"/>): which walls drop to stubs for a camera heading.</summary>
public class CutawayWallTests
{
    /// <summary>A room 3x3 inside (walls on x and y 5..9), a door on its south side, and a lone wall out in the open.</summary>
    private static SupportingCast.Sim.Simulation Room()
    {
        var defs = DefDatabase.LoadDirectory(DevPaths.Defs!);
        var sim = SupportingCast.Sim.Simulation.CreateStandard(defs, 20, 20, 1);
        for (var y = 5; y <= 9; y++)
        for (var x = 5; x <= 9; x++)
            if (x == 5 || x == 9 || y == 5 || y == 9)
                sim.SpawnStructure(defs.Thing(x == 7 && y == 9 ? "Door" : "WoodWall"), new int3(x, y, 1));
        sim.SpawnStructure(defs.Thing("WoodWall"), new int3(15, 15, 1));
        sim.RebuildDerivedIfNeeded();
        sim.Rooms.UpdateIfNeeded();
        return sim;
    }

    [Fact]
    public void TheWallsBetweenTheCameraAndTheRoomComeDown()
    {
        var sim = Room();
        // Looking north (-y): the room lies beyond the south wall, so it and its door are cut.
        var north = new float2(0, -1);
        Assert.True(WorldMesher.CutAt(sim, 6, 9, 1, north));
        Assert.True(WorldMesher.CutAt(sim, 7, 9, 1, north), "the door goes with its wall");
        Assert.False(WorldMesher.CutAt(sim, 6, 5, 1, north), "the far wall stays up");
        Assert.False(WorldMesher.CutAt(sim, 5, 7, 1, north), "side walls stay up");
        Assert.False(WorldMesher.CutAt(sim, 15, 15, 1, north), "no room behind it");

        // Looking north-west along a diagonal: the south and east walls and their corner come down.
        var diagonal = WorldMesher.SnapHeading(new float3(-0.7f, -0.72f, 0));
        Assert.True(WorldMesher.CutAt(sim, 9, 9, 1, diagonal), "the near corner");
        Assert.True(WorldMesher.CutAt(sim, 9, 7, 1, diagonal));
        Assert.True(WorldMesher.CutAt(sim, 7, 9, 1, diagonal));
        Assert.False(WorldMesher.CutAt(sim, 5, 5, 1, diagonal), "the far corner");
    }

    [Fact]
    public void HeadingsSnapToTheEightCompassDirections()
    {
        var h = WorldMesher.SnapHeading(new float3(0.9f, 0.2f, -0.5f));
        Assert.Equal(1f, h.X, 3);
        Assert.Equal(0f, h.Y, 3);
    }
}
