using DragonWorld.Sim.Ai;
using DragonWorld.Sim.Components;
using DragonWorld.Sim.Systems;

namespace DragonWorld.Sim.Tests;

public class FlightTests
{
    /// <summary>30x12, with a deep-water channel at x = 12..17 that nobody can walk across.</summary>
    private static Simulation RiverWorld()
    {
        var sim = TestWorld.Create(30, 12, depth: 8);
        sim.Climate.BaseTemperature = 30;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 30f);
        var water = sim.Defs.Block("Water");
        for (var y = 0; y < 12; y++)
        for (var x = 12; x <= 17; x++)
            sim.Map.Block[sim.Map.Idx(x, y, 0)] = water;
        sim.Map.MarkStructureChanged();
        sim.RebuildDerivedIfNeeded();
        return sim;
    }

    private static Entity Dragon(Simulation sim, int3 at, float ageDays)
    {
        var e = sim.SpawnCreature(sim.Defs.Creature("Dragon"), "Ember", at, ageDays: ageDays);
        Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
        return e;
    }

    [Fact]
    public void AFlyerAlreadyAloftCanReplanFromTheCeiling()
    {
        // A chase replans mid-flight: up at the map's top level there's no climbing to a cruise,
        // which used to make every replan fail (and every hunt give up).
        var sim = RiverWorld();
        var dragon = Dragon(sim, C(4, 6), ageDays: 14);
        var aloft = new int3(4, 6, sim.Map.Depth - 1);
        sim.World.Get<Position>(dragon) = Position.At(aloft);
        var route = Navigation.PlanRoute(sim, dragon, aloft, C(25, 6), PathEnd.OnTile);
        Assert.NotNull(route);
        Assert.Equal(C(25, 6), route[^1]);
    }

    [Fact]
    public void AStoopEndsBesideThePreyCloseEnoughToStrike()
    {
        var sim = RiverWorld();
        var dragon = Dragon(sim, C(4, 6), ageDays: 14);
        var aloft = new int3(2, 2, 5);
        var prey = C(9, 7);
        var dive = Navigation.StoopRoute(sim, dragon, aloft, prey);
        Assert.NotNull(dive);
        Assert.True(Navigation.CanStrikeFromAir(sim.Map, dive[^1], prey), $"ends at {dive[^1]}");
        Assert.All(dive.Zip(dive.Skip(1)), p => Assert.True(Maps.Map.Distance(p.First, p.Second) <= 1 && Math.Abs(p.First.Z - p.Second.Z) <= 1));
        Assert.Null(Navigation.StoopRoute(sim, dragon, C(4, 6), prey)); // on the ground: hop up first
    }

    [Fact]
    public void JuvenileFliesOverWaterItCantWalkAcross()
    {
        var sim = RiverWorld();
        var dragon = Dragon(sim, C(4, 6), ageDays: 5);
        Assert.Null(sim.Pathfinder.FindPath(C(4, 6), C(25, 6), PathEnd.OnTile));

        var route = Navigation.PlanRoute(sim, dragon, C(4, 6), C(25, 6), PathEnd.OnTile);
        Assert.NotNull(route);
        Assert.Contains(route, c => !sim.Map.Passable(c));
        Assert.Equal(C(25, 6), route[^1]);

        var mover = sim.World.Get<Mover>(dragon);
        mover.Path = route;
        mover.StepFrom = C(4, 6);
        sim.World.Get<Mind>(dragon).Drafted = true;
        var maxZ = 0f;
        for (var t = 0; t < 2000 && mover.Moving; t++)
        {
            sim.Tick();
            maxZ = Math.Max(maxZ, sim.World.Get<Position>(dragon).Pos.Z);
        }
        Assert.Equal(C(25, 6), sim.World.Get<Position>(dragon).Cell);
        Assert.True(maxZ >= 3, $"should have flown up, peaked at {maxZ:F1}");
        Assert.Equal(1, sim.World.Get<Dragon>(dragon).Flights);
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("takes to the air"));
    }

    [Fact]
    public void AdultSoarsALoopOverheadAndLandsWhereItTookOff()
    {
        var sim = TestWorld.Create(40, 40, depth: 8);
        sim.Climate.BaseTemperature = 30; // dragons cut a flight short when it's dangerously cold
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 30f);
        sim.FillBlocks(new int3(28, 18, 1), new int3(30, 22, 3)); // a rock stack on the loop
        var dragon = Dragon(sim, C(10, 20), ageDays: 14);
        var route = Navigation.SoaringRoute(sim, dragon, C(20, 20), 9);
        Assert.NotNull(route);
        Assert.Equal(C(10, 20), route[^1]);
        Assert.All(route, c => Assert.False(sim.Map.IsSolid(sim.Map.Idx(c))));
        var cruise = route.Max(c => c.Z);
        Assert.True(cruise > 4, $"should clear the rock stack, cruised at {cruise}");
        var columns = route.Where(c => c.Z == cruise).Select(c => (c.X, c.Y)).Distinct().Count();
        Assert.True(columns > 30, $"only {columns} columns flown over: not much of a loop");

        AiSystem.Order(sim, dragon, new SoarJob(route));
        for (var t = 0; t < 3000 && sim.World.Get<Mind>(dragon).Job is SoarJob; t++)
            sim.Tick();
        Assert.Equal(C(10, 20), sim.World.Get<Position>(dragon).Cell);
    }

    [Fact]
    public void HatchlingsCantFly()
    {
        var sim = RiverWorld();
        var dragon = Dragon(sim, C(4, 6), ageDays: 1);
        Assert.Null(Navigation.PlanRoute(sim, dragon, C(4, 6), C(25, 6), PathEnd.OnTile));
        Assert.False(Navigation.CanReach(sim, dragon, C(4, 6), C(25, 6), PathEnd.OnTile));
    }

    [Fact]
    public void DragonFliesAcrossTheRiverToItsKeeper()
    {
        var sim = RiverWorld();
        var keeper = sim.SpawnCreature(sim.Defs.Creature("Human"), "Keeper", C(25, 6));
        Array.Fill(sim.World.Get<Needs>(keeper).Values, 1f);
        sim.World.Get<Mind>(keeper).Drafted = true; // stays put
        var dragon = Dragon(sim, C(4, 6), ageDays: 5);
        EggSystem.AssignKeeper(sim, dragon, keeper);
        sim.World.Get<Needs>(dragon).Values[sim.Defs.Need("Bond").Index] = 0.2f; // lonely

        sim.TickMany(SimClock.HoursToTicks(1));
        var at = sim.World.Get<Position>(dragon).Cell;
        Assert.True(at.X > 17, $"dragon should be across the river, is at {at}");
        Assert.True(sim.World.Get<Dragon>(dragon).Flights > 0);
    }

    [Fact]
    public void InterruptedFlightGlidesDown()
    {
        var sim = RiverWorld();
        var dragon = Dragon(sim, C(4, 6), ageDays: 5);
        sim.World.Get<Mind>(dragon).Drafted = true;
        sim.World.Get<Position>(dragon) = Position.At(new int3(14, 6, 5)); // high over the water
        var hp = sim.World.Get<Creature>(dragon).Hp;
        sim.Tick();
        var p = sim.World.Get<Position>(dragon);
        Assert.Equal(1, p.Cell.Z);
        Assert.Equal(hp, sim.World.Get<Creature>(dragon).Hp);
    }
}
