namespace SupportingCast.Sim.Tests;

public class RaycastTests
{
    private static Simulation Mesa()
    {
        var sim = TestWorld.Create(20, 20, depth: 6);
        sim.FillBlocks(new int3(8, 8, 1), new int3(11, 11, 2)); // top face at height 3
        return sim;
    }

    [Fact]
    public void RayFromAboveHitsTheTopFace()
    {
        var map = Mesa().Map;
        Assert.True(map.Raycast(new float3(9.5f, 9.5f, 50f), new float3(0, 0, -1), 100f, map.IsSolid, out var cell, out var normal));
        Assert.Equal(new int3(9, 9, 2), cell);
        Assert.Equal(new int3(0, 0, 1), normal);
    }

    [Fact]
    public void SlantedRayFromOutsideTheMapHitsTheSide()
    {
        var map = Mesa().Map;
        // From far west, aimed slightly down at the mesa's west face at mid height.
        var origin = new float3(-30f, 9.5f, 2.5f);
        var dir = new float3(1f, 0f, -0.01f);
        Assert.True(map.Raycast(origin, dir, 200f, map.IsSolid, out var cell, out var normal));
        Assert.Equal(new int3(8, 9, 2), cell);
        Assert.Equal(new int3(-1, 0, 0), normal);
    }

    [Fact]
    public void RayThatMissesReturnsFalse()
    {
        var map = Mesa().Map;
        // Above the ground layer, pointing up and away.
        Assert.False(map.Raycast(new float3(2.5f, 2.5f, 1.5f), new float3(0.3f, 0.2f, 1f), 100f, map.IsSolid, out _, out _));
    }
}
