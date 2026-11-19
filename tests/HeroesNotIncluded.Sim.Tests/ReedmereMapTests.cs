using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using Narsil.Sim.Grid;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// Reedmere, the town on stilts (tools/lake_map.py): it stands up as a round's town, the cook keeps
/// the cookhouse apart from the inn, everything out on the water can be walked to, the long bridge
/// to the shore is what a raid brings down, the harbour's open water with a way out to the lake, the
/// forge stands on stone, and the festival's held out on the boats.
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
        // The game's trades all have theirs; one only players take may not (there's no mine on the lake), and then
        // it's neither dealt nor offered there.
        foreach (var trade in sim.Defs.AllTrades.Where(t => t.HasWorkplace && !t.PlayerOnly))
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
    public void ARaidCutsTheLongBridgeAndTheTownIsCutOffFromTheShore()
    {
        var (sim, town) = Town();
        var w = sim.World;
        var shore = w.Query<PresetBuilding>().First(e => w.Get<PresetBuilding>(e).Def.Name == "Woodshop");
        var there = sim.SpiralFrom(w.Get<PresetBuilding>(shore).Anchor, 8).First(c => sim.Map.InBounds(c) && sim.Map.Passable(c));
        Assert.True(Navigation.CanReach(sim, Entity.Null, town.Center, there, PathEnd.OnTile), "the bridge carries the road to the shore");
        var breach = sim.Repairs.BreakBridge(sim, sim.Rand);
        Assert.NotNull(breach);
        Assert.Equal("Causeway", breach!.Def.Name);
        sim.RebuildDerivedIfNeeded();
        Assert.False(Navigation.CanReach(sim, Entity.Null, town.Center, there, PathEnd.OnTile), "the town out on the water is cut off from the shore");
    }

    [Fact]
    public void TheCausewayIsTheOneBridge()
    {
        var (sim, _) = Town();
        var w = sim.World;
        var spans = w.Query<Thing>().Where(e => w.Get<Thing>(e).Def.Building is { IsFloor: true, Breakable: true }).ToList();
        Assert.NotEmpty(spans);
        Assert.All(spans, e => Assert.Equal("Causeway", w.Get<Thing>(e).Def.Name));
        // One straight run: every cell of it in the same three columns.
        Assert.Equal(3, spans.Select(e => w.Get<Thing>(e).Cell.X).Distinct().Count());
    }

    [Fact]
    public void TheHarbourIsOpenWaterWithAWayOutToTheLake()
    {
        var map = LoadDefs().Map(Map);
        bool Open(int x, int y) => map.Inside(x, y) && map.Ground[y * map.Width + x].IsWater
            && map.Features[y * map.Width + x] is not { Def.IsFloor: true };
        Assert.True(Open(52, 30), "the harbour's middle is water, not deck");
        // The water-gate, east of the boats: a canal from the harbour out to the lake, open but for a footbridge over each end.
        var row = Enumerable.Range(66, map.Width - 66).Where(x => !Open(x, 32)).ToList();
        Assert.True(row.Count is > 0 and <= 8, $"the canal's row is decked at {string.Join(",", row)}: only its two footbridges should cross it");
        Assert.True(Open(map.Width - 1, 32), "and it opens on the lake");
        Assert.False(Open(map.Center[0], map.Center[1]), "the landing's on deck");
    }

    [Fact]
    public void TheForgeStandsOnStoneAndTheMootHallOnTheFarQuay()
    {
        var (sim, town) = Town();
        var w = sim.World;
        var forge = w.Query<PresetBuilding>().Single(e => w.Get<PresetBuilding>(e).Def.Roles.Contains(TownRole.Forge));
        var plot = sim.SpiralFrom(w.Get<PresetBuilding>(forge).Anchor, 6).Where(c => sim.Map.InBounds(c) && Buildings.At(sim, c) == forge).ToList();
        Assert.NotEmpty(plot);
        foreach (var c in plot)
            Assert.Equal("StonePier", w.Get<Thing>(sim.Map.Floor[sim.Map.Idx(c)]).Def.Name);
        var hall = w.Query<PresetBuilding>().Single(e => w.Get<PresetBuilding>(e).Def.Name == "MootHall");
        Assert.True(w.Get<PresetBuilding>(hall).Anchor.Y < w.Get<PresetBuilding>(town.Tavern).Anchor.Y, "the hall's on the far quay from the gate");
    }

    [Fact]
    public void TheFestivalIsOnTheBoatsRoundTheMast()
    {
        var (sim, _) = Town();
        var w = sim.World;
        var map = sim.Defs.Map(Map);
        Assert.NotEmpty(map.Festival);
        foreach (var p in map.Festival)
        {
            var floor = map.Features[p.At[1] * map.Width + p.At[0]]?.Def.Name;
            Assert.True(floor is "BoatDeck" or "FireBarge", $"the festival's {p.Def} at {p.At[0]},{p.At[1]} stands on {floor}");
        }
        var bonfire = map.Festival.Single(p => p.Def == FestivalSystem.BonfireDef);
        Assert.Equal("FireBarge", map.Features[bonfire.At[1] * map.Width + bonfire.At[0]]?.Def.Name);
        var square = RoundSystem.FindSquare(sim);
        Assert.NotNull(square);
        Assert.Equal("FestivalMast", w.Get<Thing>(sim.Map.Structure[sim.Map.Idx(square!.Value)]).Def.Name);
        Assert.Equal("the boats in the harbour", RoundSystem.GatheringPlace(sim));
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
