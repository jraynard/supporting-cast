using HeroesNotIncluded.Sim.Ai;

namespace HeroesNotIncluded.Sim.Tests;

public class PathfindingTests
{
    [Fact]
    public void ADoorUnlockedMidTickIsOpenToTheNextPathAskedFor()
    {
        // A query between a structure change and the tick's own refresh used to rebuild the
        // regions from the old path costs, and they kept the locked door shut for good.
        var sim = TestWorld.Create(20, 20);
        sim.BuildWallRing(C(5, 5), C(11, 11));
        sim.DestroyThing(sim.Map.Structure[sim.Map.Idx(C(8, 11))]);
        var gate = sim.SpawnStructure(sim.Thing("CageGate"), C(8, 11)); // in the south wall
        sim.SetLocked(gate, true);
        sim.RebuildDerivedIfNeeded();
        Assert.False(sim.Regions.CanReach(C(8, 14), C(8, 8), PathEnd.OnTile));

        sim.SetLocked(gate, false);
        Assert.True(sim.Regions.CanReach(C(8, 14), C(8, 8), PathEnd.OnTile));
        Assert.NotNull(sim.Pathfinder.FindPath(C(8, 14), C(8, 8), PathEnd.OnTile));
        sim.RebuildDerivedIfNeeded();
        Assert.True(sim.Regions.CanReach(C(8, 14), C(8, 8), PathEnd.OnTile));
    }

    [Fact]
    public void FindsPathAroundWall()
    {
        var sim = TestWorld.Create(20, 20);
        for (var y = 0; y < 15; y++)
            sim.SpawnStructure(sim.Thing("StoneWall"), C(10, y));
        sim.RebuildDerivedIfNeeded();

        var path = sim.Pathfinder.FindPath(C(5, 5), C(15, 5), PathEnd.OnTile);
        Assert.NotNull(path);
        Assert.Equal(C(15, 5), path[^1]);
        Assert.DoesNotContain(path, t => t.X == 10 && t.Y < 15);
        Assert.Contains(path, t => t.Y >= 15);
    }

    [Fact]
    public void UnreachableTargetFailsFast()
    {
        var sim = TestWorld.Create(20, 20);
        sim.BuildWallRing(C(2, 2), C(8, 8), "StoneWall");
        Assert.False(sim.Regions.CanReach(C(5, 5), C(15, 15), PathEnd.OnTile));
        Assert.Null(sim.Pathfinder.FindPath(C(5, 5), C(15, 15), PathEnd.OnTile));
    }

    [Fact]
    public void TouchModeReachesImpassableTarget()
    {
        var sim = TestWorld.Create(20, 20);
        sim.SpawnStructure(sim.Thing("Boulder"), C(10, 10));
        sim.RebuildDerivedIfNeeded();
        var path = sim.Pathfinder.FindPath(C(2, 2), C(10, 10), PathEnd.Touch);
        Assert.NotNull(path);
        var end = path[^1];
        Assert.True(Math.Abs(end.X - 10) <= 1 && Math.Abs(end.Y - 10) <= 1);
        Assert.NotEqual(C(10, 10), end);
    }

    [Fact]
    public void DoesNotCutCorners()
    {
        var sim = TestWorld.Create(10, 10);
        sim.SpawnStructure(sim.Thing("StoneWall"), C(5, 4));
        sim.SpawnStructure(sim.Thing("StoneWall"), C(4, 5));
        sim.RebuildDerivedIfNeeded();
        var path = sim.Pathfinder.FindPath(C(4, 4), C(5, 5), PathEnd.OnTile);
        // Diagonal squeeze between the two walls is not allowed; the room is sealed on that side.
        Assert.True(path == null || path.Count > 1);
    }

    /// <summary>A 20x10 map whose east half (x ≥ 10) is a plateau one level up, standing at z = 2.</summary>
    private static Simulation Plateau()
    {
        var sim = TestWorld.Create(20, 10, depth: 5);
        sim.FillBlocks(new int3(10, 0, 1), new int3(19, 9, 1));
        return sim;
    }

    [Fact]
    public void FloorAboveSeesWhatIsDrawnOverACellUpToTheCut()
    {
        var sim = TestWorld.Create(20, 10, depth: 6);
        var under = C(5, 5);
        Assert.False(sim.Map.FloorAbove(under, 5, countRoofs: true));
        sim.FillBlocks(new int3(5, 5, 3), new int3(5, 5, 3)); // an overhang two levels up
        Assert.True(sim.Map.FloorAbove(under, 5, countRoofs: true));
        Assert.False(sim.Map.FloorAbove(under, 2, countRoofs: true)); // cut below it: not drawn
    }

    [Fact]
    public void CliffIsUnreachableWithoutAWayUp()
    {
        var sim = Plateau();
        Assert.False(sim.Regions.CanReach(C(5, 5), new int3(15, 5, 2), PathEnd.OnTile));
        Assert.Null(sim.Pathfinder.FindPath(C(5, 5), new int3(15, 5, 2), PathEnd.OnTile));
    }

    [Theory]
    [InlineData("Slope")]
    [InlineData("WoodStairs")]
    public void WalksUpARampOntoHigherGround(string ramp)
    {
        var sim = Plateau();
        sim.SpawnStructure(sim.Thing(ramp), C(9, 5));
        sim.RebuildDerivedIfNeeded();

        var path = sim.Pathfinder.FindPath(C(5, 5), new int3(15, 5, 2), PathEnd.OnTile);
        Assert.NotNull(path);
        var climb = path.IndexOf(new int3(10, 5, 2));
        Assert.True(climb > 0 && path[climb - 1] == C(9, 5), "should step up from the ramp onto the plateau edge");
        Assert.All(path, c => Assert.True(sim.Map.Standable(sim.Map.Idx(c))));

        // And back down again.
        Assert.NotNull(sim.Pathfinder.FindPath(new int3(15, 5, 2), C(5, 5), PathEnd.OnTile));
    }

    [Fact]
    public void RampNeedsHeadroom()
    {
        var sim = Plateau();
        sim.SpawnStructure(sim.Thing("Slope"), C(9, 5));
        sim.FillBlocks(new int3(9, 5, 2), new int3(9, 5, 2)); // rock overhang above the slope
        Assert.False(sim.Regions.CanReach(C(5, 5), new int3(15, 5, 2), PathEnd.OnTile));
    }

    [Fact]
    public void VillagerWalksUpStairs()
    {
        var sim = Plateau();
        sim.SpawnStructure(sim.Thing("WoodStairs"), C(9, 2));
        sim.RebuildDerivedIfNeeded();
        var pawn = sim.SpawnCreature(sim.Defs.Creature("Human"), "Climber", C(3, 3));
        var mover = sim.World.Get<Components.Mover>(pawn);
        mover.Path = sim.Pathfinder.FindPath(C(3, 3), new int3(14, 7, 2), PathEnd.OnTile);
        mover.StepFrom = C(3, 3);
        sim.World.Get<Components.Mind>(pawn).Drafted = true; // no brain, just walk

        var maxZ = 0f;
        for (var t = 0; t < 600 && mover.Moving; t++)
        {
            sim.Tick();
            var p = sim.World.Get<Components.Position>(pawn);
            maxZ = Math.Max(maxZ, p.Pos.Z);
            Assert.False(sim.Map.IsSolid(sim.Map.Idx(p.Cell)), $"walked into rock at {p.Cell}");
        }
        Assert.Equal(new int3(14, 7, 2), sim.World.Get<Components.Position>(pawn).Cell);
        Assert.Equal(2f, maxZ);
    }

    [Fact]
    public void VillagerWalledInByAFinishedWallWalksOut()
    {
        var sim = TestWorld.Create(20, 20);
        sim.Climate.BaseTemperature = 20;
        Array.Fill(sim.Map.Temperature, 20f);
        var pawn = sim.SpawnCreature(sim.Defs.Creature("Human"), "Stuck", C(10, 10));
        Array.Fill(sim.World.Get<Components.Needs>(pawn).Values, 1f);
        sim.SpawnStructure(sim.Thing("WoodWall"), C(10, 10)); // finished around them
        sim.RebuildDerivedIfNeeded();

        sim.TickMany(SimClock.TicksPerSecond * 20);
        var at = sim.World.Get<Components.Position>(pawn).Cell;
        Assert.True(sim.Map.Passable(at), $"still inside the wall at {at}");
    }
}
