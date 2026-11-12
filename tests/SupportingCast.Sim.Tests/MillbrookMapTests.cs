using SupportingCast.Sim.Combat;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Scenarios;

namespace SupportingCast.Sim.Tests;

/// <summary>Millbrook (data/defs/maps/millbrook.json), the village a round is played in, stands up and holds together.</summary>
public class MillbrookMapTests
{
    private static (Simulation Sim, VillageRound.Town Town) Round(ulong seed = 1234)
    {
        var sim = VillageRound.NewSimulation(TestWorld.LoadDefs(), seed);
        sim.Presets.PresetMode = true;
        return (sim, VillageRound.Setup(sim));
    }

    [Fact]
    public void EveryTradeHasItsBuilding()
    {
        var (sim, town) = Round();
        Assert.Equal("Inn", sim.World.Get<PresetBuilding>(town.Tavern).Def.Name);
        Assert.Equal("Smithy", sim.World.Get<PresetBuilding>(town.Forge).Def.Name);
        Assert.Equal("Shrine", sim.World.Get<PresetBuilding>(town.Shrine).Def.Name);
        Assert.Equal("Woodshop", sim.World.Get<PresetBuilding>(town.Woodshop).Def.Name);
        foreach (var trade in sim.Defs.AllTrades)
            if (trade.WorkplaceDef is { } workplace)
                Assert.Contains(sim.World.Query<PresetBuilding>(), e => sim.World.Get<PresetBuilding>(e).Def == workplace);
    }

    [Fact]
    public void EveryLaneLeadsBackToTheSquare()
    {
        var (sim, town) = Round();
        var map = sim.Map;
        var road = sim.Defs.Block("Road");
        var cut = new List<int3>();
        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            var z = map.SurfaceZ(x, y);
            if (z < 1 || map.Block[map.Idx(x, y, z - 1)] != road || !map.Passable(new int3(x, y, z)))
                continue;
            if (!sim.Regions.CanReach(town.Center, new int3(x, y, z), PathEnd.OnTile))
                cut.Add(new int3(x, y, z));
        }
        Assert.True(cut.Count == 0, $"{cut.Count} lane cells cut off, e.g. {string.Join(" ", cut.Take(5))}");
    }

    [Fact]
    public void PartiesCanWalkInFromThreeSidesAtLeast()
    {
        var (sim, town) = Round();
        var sides = Enumerable.Range(0, 4).Count(side => Raids.EdgeEntry(sim, side, town.Center) != null);
        Assert.True(sides >= 3, $"only {sides} sides reach the square");
    }

    [Fact]
    public void ThePigsStayInTheirPen()
    {
        var (sim, _) = Round();
        var pigs = sim.Creatures().Where(e => sim.World.Get<Creature>(e).Def.Name == "Pig").ToList();
        Assert.Equal(3, pigs.Count);
        sim.TickMany(SimClock.TicksPerDay);
        Assert.All(pigs, p =>
        {
            var at = sim.World.Get<Position>(p).Cell;
            Assert.True(at.X is >= 27 and <= 35 && at.Y is >= 21 and <= 30, $"a pig got out to {at}");
        });
    }

    [Fact]
    public void TheHeroWalksInOnTheSquare()
    {
        var (sim, town) = Round();
        var me = VillageRound.SpawnHero(sim, new Hero("Arin", null, null), town.Center);
        var at = sim.World.Get<Position>(me).Cell;
        Assert.True(sim.Map.Passable(at));
        Assert.Equal("Road", sim.Map.GroundUnder(sim.Map.Idx(at))?.Name);
    }

    [Fact]
    public void TheSameSeedLaysTheSameVillage()
    {
        var (a, _) = Round(99);
        var (b, _) = Round(99);
        Assert.Equal(a.Map.Block.Select(d => d.Name), b.Map.Block.Select(d => d.Name));
        Assert.Equal(a.World.Count<Thing>(), b.World.Count<Thing>());
    }
}
