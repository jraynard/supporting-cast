using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// Who may take an order on (<see cref="ShopSystem.MayTake"/>): the trade that serves it where it
/// works. Overhearing a drink ordered at the inn doesn't make the empath its barkeep (playtest).
/// </summary>
public class OrderTakingTests
{
    private static (Simulation Sim, Entity Drinker, Entity Empath, Entity Keeper, Order Ale) AtTheBar()
    {
        var sim = Create(30, 30);
        Entity Person(string name, int x, string? trade)
        {
            var e = sim.SpawnCreature(sim.Defs.Creature("Human"), name, C(x, 10), ageDays: 25 * 365);
            if (trade != null)
                sim.World.Add(e, new Trade { Def = sim.Defs.Trade(trade) });
            return e;
        }
        var drinker = Person("Drinker", 10, null);
        var empath = Person("Empath", 11, "Empath");
        var keeper = Person("Keeper", 9, "Innkeeper");
        var ale = sim.Shops.Post(sim, drinker, sim.Defs.Service(ServiceKind.Drink), "An ale.", 2, sim.Clock.Tick + SimClock.HoursToTicks(4));
        ale.KnownBy.Add(empath); // both heard it asked
        ale.KnownBy.Add(keeper);
        return (sim, drinker, empath, keeper, ale);
    }

    [Fact]
    public void AnEmpathWhoOverhearsADrinkOrderCantTakeIt()
    {
        var (sim, _, empath, _, ale) = AtTheBar();
        Assert.False(ShopSystem.MayTake(sim, empath, ale));
        Assert.False(sim.Shops.Accept(sim, empath, ale.Id));
        Assert.True(ale.Assignee.IsNull);
    }

    [Fact]
    public void TheInnkeeperTakesTheDrinkOrder()
    {
        var (sim, _, _, keeper, ale) = AtTheBar();
        Assert.True(ShopSystem.MayTake(sim, keeper, ale));
        Assert.True(sim.Shops.Accept(sim, keeper, ale.Id));
        Assert.Equal(keeper, ale.Assignee);
    }

    [Fact]
    public void AnEmpathHoldingADrinkCantServeAnotherShopsOrder()
    {
        var (sim, drinker, empath, keeper, ale) = AtTheBar();
        var mug = sim.CreateItem(sim.Defs.AllThings.First(t => t.Drink), 1);
        sim.World.Get<Carrying>(empath).Item = mug;
        var at = sim.World.Get<Position>(drinker).Cell;
        Assert.Null(BarService.At(sim, empath, Entity.Null, at));
        sim.World.Get<Carrying>(empath).Item = Entity.Null;
        sim.World.Get<Carrying>(keeper).Item = mug;
        Assert.Equal(ale, BarService.At(sim, keeper, Entity.Null, at)?.Order);
    }
}
