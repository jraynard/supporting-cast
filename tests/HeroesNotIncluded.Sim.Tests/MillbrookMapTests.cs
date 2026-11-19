using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Scenarios;

namespace HeroesNotIncluded.Sim.Tests;

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
        Assert.Equal("TinkerShop", sim.World.Get<PresetBuilding>(town.TinkerShop).Def.Name);
        foreach (var trade in sim.Defs.AllTrades)
            if (trade.HasWorkplace)
                Assert.False(Shops.ShopSystem.WorkplaceOf(sim, trade).IsNull, $"{trade.Name}: no {trade.Workplace} in town");
        Assert.All(sim.World.Query<PresetBuilding>(), e => Assert.Equal(BuildPhase.Done, sim.World.Get<PresetBuilding>(e).Phase));
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
        var me = VillageRound.SpawnHero(sim, new Hero("Arin", null), town.Center);
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

    /// <summary>The mine's tunnel cells as built: air at the town's level, and the map's def for them.</summary>
    private static (MapTunnel Tunnel, List<int3> Cells) Mine(Simulation sim)
    {
        var def = sim.Defs.Map("Millbrook");
        var tunnel = Assert.Single(def.Tunnels);
        return (tunnel, [.. tunnel.Cells.Select(c => new int3(c[0], c[1], def.BaseZ + tunnel.Level))]);
    }

    [Fact]
    public void TheMineIsDugIntoTheHillWithRockOverIt()
    {
        var (sim, town) = Round();
        var (_, cells) = Mine(sim);
        Assert.All(cells, c =>
        {
            var i = sim.Map.Idx(c);
            Assert.True(sim.Map.Block[i + sim.Map.LayerSize].Solid, $"the mine at {c} is open to the sky");
            if (!sim.Map.Structure[i].IsNull)
                return; // the lantern in its niche
            Assert.True(sim.Map.Passable(c), $"the mine at {c} isn't walkable");
            Assert.True(sim.Regions.CanReach(town.Center, c, PathEnd.OnTile), $"the mine at {c} can't be reached from the square");
        });
    }

    [Theory]
    [InlineData(1234UL)]
    [InlineData(99UL)]
    [InlineData(7UL)]
    public void TheMineHasIronAndCoalWhateverTheSeed(ulong seed)
    {
        var (sim, _) = Round(seed);
        var (_, cells) = Mine(sim);
        var map = sim.Map;
        // Within a few cells' digging of the tunnel's walls, at its level or the one above.
        var near = new HashSet<string>();
        var shown = new HashSet<string>();
        foreach (var c in cells)
        for (var dy = -4; dy <= 4; dy++)
        for (var dx = -4; dx <= 4; dx++)
        for (var dz = 0; dz <= 1; dz++)
        {
            var n = c + new int3(dx, dy, dz);
            if (!map.InBounds(n) || !map.Block[map.Idx(n)].Ore)
                continue;
            near.Add(map.Block[map.Idx(n)].Name);
            // On the tunnel's own walls (a cliff outside may show a vein, as any hillside can).
            if (Math.Abs(dx) + Math.Abs(dy) + dz == 1)
                shown.Add(map.Block[map.Idx(n)].Name);
        }
        Assert.Contains("IronOre", near);
        Assert.Contains("CoalSeam", near);
        // Struck by digging, not lying open on the tunnel's walls from the start (MapBuilder works them out).
        Assert.Empty(shown);
        // Nothing struck here would be minted into coin.
        Assert.DoesNotContain("SilverOre", near);
        Assert.DoesNotContain("GoldOre", near);
    }

    [Fact]
    public void ATunnelWithNoRockOverItIsAMistakeInTheMap()
    {
        var def = new MapDef
        {
            Name = "Cutting", Width = 3, Height = 1, Depth = 8, GroundLegend = new() { ["k"] = "Stone" },
            GroundRows = ["kkk"], HeightRows = ["231"], FeatureRows = ["..."], Center = [0, 0],
            Tunnels = [new MapTunnel { Level = 1, Cells = [[0, 0], [1, 0]] }],
        };
        var e = Assert.ThrowsAny<Exception>(() => def.Resolve(TestWorld.LoadDefs()));
        Assert.Contains("no rock above it", e.Message);
    }
}
