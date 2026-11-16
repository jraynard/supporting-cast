using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using Narsil.Sim.Grid;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// Reedmere, the town on stilts (tools/lake_map.py): it stands up as a round's town, the cook keeps
/// the cookhouse on the shore and the innkeeper the inn at the far landing, everything out on the
/// water can be walked to, and the causeway is what a raid brings down.
/// </summary>
public class ReedmereMapTests
{
    public const string Map = "Reedmere";

    private static (Simulation Sim, VillageRound.Town Town) Town()
    {
        var sim = VillageRound.NewSimulation(LoadDefs(), 1234, Map);
        var town = VillageRound.Setup(sim, Map);
        return (sim, town);
    }

    [Fact]
    public void EveryTradeHasItsBuildingAndTheKitchenIsApart()
    {
        var (sim, town) = Town();
        foreach (var trade in sim.Defs.AllTrades.Where(t => t.HasWorkplace))
            Assert.False(ShopSystem.WorkplaceOf(sim, trade).IsNull, $"{trade.Name}: no {trade.Workplace} in Reedmere");
        Assert.Equal("LandingInn", sim.World.Get<PresetBuilding>(town.Tavern).Def.Name);
        Assert.Equal("Cookhouse", sim.World.Get<PresetBuilding>(town.Kitchen).Def.Name);
        Assert.NotEqual(town.Tavern, town.Kitchen);
        Assert.All(sim.World.Query<PresetBuilding>(), e => Assert.Equal(BuildPhase.Done, sim.World.Get<PresetBuilding>(e).Phase));
    }

    [Fact]
    public void EveryBuildingCanBeWalkedTo()
    {
        var (sim, town) = Town();
        sim.RebuildDerivedIfNeeded();
        var w = sim.World;
        foreach (var b in w.Query<PresetBuilding>())
        {
            var def = w.Get<PresetBuilding>(b).Def;
            var inside = sim.SpiralFrom(w.Get<PresetBuilding>(b).Anchor, 8)
                .FirstOrDefault(c => sim.Map.InBounds(c) && sim.Map.Passable(c) && Buildings.At(sim, c) == b, new int3(-1, -1, -1));
            Assert.True(inside.X >= 0, $"{def.Name}: nowhere to stand inside");
            Assert.True(Navigation.CanReach(sim, Entity.Null, town.Center, inside, PathEnd.OnTile), $"{def.Name} can't be walked to from the landing");
        }
    }

    [Fact]
    public void ARaidCutsTheCausewayAndTheTinkerCanMendIt()
    {
        var (sim, town) = Town();
        var breach = sim.Repairs.BreakBridge(sim, sim.Rand);
        Assert.NotNull(breach);
        Assert.Equal("Causeway", breach!.Def.Name);
        sim.RebuildDerivedIfNeeded();
        var inn = sim.SpiralFrom(sim.World.Get<PresetBuilding>(town.Tavern).Anchor, 8).First(c => sim.Map.InBounds(c) && sim.Map.Passable(c));
        Assert.False(Navigation.CanReach(sim, Entity.Null, town.Center, inn, PathEnd.OnTile), "the town out on the water is cut off");
    }

    [Fact]
    public void BucketsHangAtTheDippingPosts()
    {
        var (sim, _) = Town();
        var posts = Environment.Buckets.Wells(sim).Where(e => sim.World.Get<Thing>(e).Def.Name == "DippingPost").ToList();
        Assert.True(posts.Count >= 4);
        var buckets = sim.AvailableItems().Count(e => Environment.Buckets.IsBucket(sim, e));
        Assert.True(buckets >= Environment.Buckets.PerWell * posts.Count);
    }
}
