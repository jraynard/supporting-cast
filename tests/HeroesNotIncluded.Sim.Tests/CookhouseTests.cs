using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// A town that keeps its kitchen apart from the inn (a <c>Cookhouse</c> and a <c>LandingInn</c>):
/// the cook works the cookhouse and the innkeeper the inn, each building's sign shows only its own
/// services, and the carter's and the round's stock go where each belongs.
/// </summary>
public class CookhouseTests
{
    private static (Simulation Sim, Entity Inn, Entity Cookhouse) Town()
    {
        var sim = Create(64, 40, depth: 6);
        var inn = Buildings.SpawnFinished(sim, sim.Defs.Building("LandingInn"), C(14, 18), 0);
        var cookhouse = Buildings.SpawnFinished(sim, sim.Defs.Building("Cookhouse"), C(44, 18), 0);
        sim.RebuildDerivedIfNeeded();
        return (sim, inn, cookhouse);
    }

    private static Entity Hire(Simulation sim, string trade, int3 at)
    {
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), trade, at, ageDays: 24 * 365f);
        sim.World.Add(e, new Trade { Def = sim.Defs.Trade(trade) });
        return e;
    }

    [Fact]
    public void TheCookWorksTheCookhouseAndTheInnkeeperTheInn()
    {
        var (sim, inn, cookhouse) = Town();
        Assert.Equal(inn, ShopSystem.BuildingFor(sim, TownRole.Tavern));
        Assert.Equal(cookhouse, ShopSystem.BuildingFor(sim, TownRole.Kitchen));
        var cook = Hire(sim, "Cook", C(30, 34));
        var keeper = Hire(sim, "Innkeeper", C(31, 34));
        Assert.Equal(cookhouse, ShopSystem.WorkplaceOf(sim, cook));
        Assert.Equal(inn, ShopSystem.WorkplaceOf(sim, keeper));
        Assert.True(WorkGivers.CooksAt(sim, cook, cookhouse));
        Assert.False(WorkGivers.CooksAt(sim, cook, inn));
        Assert.True(ShopPrices.Keeps(sim, cook, cookhouse));
        Assert.False(ShopPrices.Keeps(sim, cook, inn));
        Assert.False(ShopPrices.Keeps(sim, keeper, cookhouse));
    }

    [Fact]
    public void EachSignShowsItsOwnServices()
    {
        var (sim, inn, cookhouse) = Town();
        Assert.Equal([ServiceKind.Meal, ServiceKind.Provisions], ShopPrices.Board(sim, cookhouse).Select(p => p.Kind)); // and the inn's hampers
        Assert.Equal([ServiceKind.Drink, ServiceKind.MixedDrink, ServiceKind.Quest, ServiceKind.Lodging], ShopPrices.Board(sim, inn).Select(p => p.Kind));
        Assert.False(Hatch.Of(sim, cookhouse).IsNull, "meals go over the cookhouse's hatch");
        Assert.True(Hatch.Of(sim, inn).IsNull, "the inn has no kitchen to serve from");
        Assert.False(Cooking.PotIn(sim, cookhouse).IsNull);
        Assert.True(Cooking.PotIn(sim, inn).IsNull);
        Assert.NotEmpty(Shops.ShopSystem.LineCells(sim, cookhouse, hatch: true));
        Assert.NotEmpty(Shops.ShopSystem.BedsOf(sim, inn));
    }

    [Fact]
    public void StockGoesWhereItBelongs()
    {
        var (sim, inn, cookhouse) = Town();
        VillageRound.Restock(sim, inn);
        VillageRound.Restock(sim, cookhouse);
        var w = sim.World;
        int Held(Entity building, string def) => Buildings.Parts(sim, building).SelectMany(p => sim.StoredIn(p))
            .Where(e => w.Get<Thing>(e).Def.Name == def).Sum(e => w.Get<Stack>(e).Count);
        Assert.Equal(VillageRound.Tankards, Held(inn, "Tankard"));
        Assert.Equal(VillageRound.StoreroomKegs, Held(inn, "Keg"));
        Assert.Equal(0, Held(inn, "Bowl"));
        Assert.Equal(VillageRound.Bowls, Held(cookhouse, "Bowl"));
        Assert.True(Held(cookhouse, "Cabbage") > 0, "the larder's stocked");
        Assert.Equal(0, Held(cookhouse, "Keg"));

        // The carter calls at both, kegs to the one and food to the other.
        var kegs = Held(inn, "Keg");
        var meat = Held(cookhouse, "Meat");
        InnSupplySystem.Deliver(sim, inn, "Chest", InnSupplySystem.Kegs);
        InnSupplySystem.Deliver(sim, cookhouse, "Larder", InnSupplySystem.Food);
        Assert.True(Held(inn, "Keg") > kegs);
        Assert.True(Held(cookhouse, "Meat") > meat);
    }
}
