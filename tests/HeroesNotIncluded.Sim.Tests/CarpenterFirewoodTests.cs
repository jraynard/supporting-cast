using HeroesNotIncluded.Sim.Carpentry;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>Firewood to order from the carpenter: sent for and paid from the shop window, carried to the workplace's store, paid a bundle at a time.</summary>
public class CarpenterFirewoodTests
{
    private static Entity Rack(Simulation sim) =>
        FuelStores.In(sim, ShopSystem.BuildingOf(sim, sim.Defs.Building("Inn"))).First(e => sim.World.Get<Thing>(e).Def.Name == "LogRack");

    private static int Firewood(Simulation sim, Entity store) =>
        sim.StoredIn(store).Where(e => sim.World.Get<Thing>(e).Def.Name == Stock.FirewoodDef).Sum(e => sim.World.Get<Stack>(e).Count);

    private static Entity Carpenter(Simulation sim) =>
        sim.Creatures().First(e => sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == CarpenterModule.TradeName);

    [Fact]
    public void AKeeperSendsForFirewoodAndPaysAndCallingItOffPaysBack()
    {
        var (sim, me, _) = ExpeditionTests.Round("Innkeeper");
        Inventory.Stow(sim, me, sim.CreateItem(sim.Defs.Thing(Coin.Silver), 10));
        var purse = Coin.Count(sim, me);
        var orders = FirewoodOrders.Of(sim);
        var inn = ShopSystem.BuildingOf(sim, sim.Defs.Building("Inn"));
        sim.ApplyNow(new OrderFirewoodCommand(me, 2));
        Assert.Equal(2, orders.Owed(inn));
        Assert.Equal(purse - 2 * FirewoodOrders.Price, Coin.Count(sim, me));
        sim.ApplyNow(new CancelFirewoodCommand(me, orders.All.Single().Id));
        Assert.Equal(0, orders.Owed(inn));
        Assert.Equal(purse, Coin.Count(sim, me));
    }

    [Fact]
    public void TheCarpenterPutsABundleInTheStoreAndIsPaid()
    {
        var (sim, me, _) = ExpeditionTests.Round("Innkeeper");
        Inventory.Stow(sim, me, sim.CreateItem(sim.Defs.Thing(Coin.Silver), 10));
        sim.ApplyNow(new OrderFirewoodCommand(me, 1));
        var carpenter = Carpenter(sim);
        var rack = Rack(sim);
        var before = Firewood(sim, rack);
        var silver = Coin.Count(sim, carpenter);
        Inventory.Stow(sim, carpenter, sim.CreateItem(sim.Defs.Thing(Stock.FirewoodDef), FirewoodOrders.Bundle, sim.Defs.Material("Pine")));
        Assert.Equal(1, FirewoodOrders.Of(sim).Deliver(sim, carpenter, rack));
        Assert.Equal(before + FirewoodOrders.Bundle, Firewood(sim, rack));
        Assert.Equal(silver + FirewoodOrders.Price, Coin.Count(sim, carpenter));
        Assert.Empty(FirewoodOrders.Of(sim).All);
    }

    [Fact]
    public void TheAiCarpenterSplitsAndBringsWhatWasSentFor()
    {
        var (sim, me, _) = ExpeditionTests.Round("Innkeeper");
        Inventory.Stow(sim, me, sim.CreateItem(sim.Defs.Thing(Coin.Silver), 10));
        var rack = Rack(sim);
        var before = Firewood(sim, rack);
        sim.ApplyNow(new OrderFirewoodCommand(me, 1));
        for (var t = 0; t < SimClock.TicksPerHour * 8 && FirewoodOrders.Of(sim).All.Count > 0; t += 60)
            sim.TickMany(60);
        Assert.Empty(FirewoodOrders.Of(sim).All);
        Assert.True(Firewood(sim, rack) >= before + FirewoodOrders.Bundle - 18, $"brought: {Firewood(sim, rack)} from {before}"); // the innkeeper's own fire may have burned some meanwhile
    }

    [Fact]
    public void WithAPlayerCarpenterTheInnSendsToThemNotTheCarter()
    {
        var (sim, _, _) = ExpeditionTests.Round("Carpenter"); // the inn run by the AI
        var rack = Rack(sim);
        foreach (var log in sim.StoredIn(rack).ToList())
            sim.DestroyThing(log);
        var inn = ShopSystem.BuildingOf(sim, sim.Defs.Building("Inn"));
        // The next evening: the carter's morning cart has been and gone, and both the carpenter's order hour and the carter's have come.
        var day = sim.Clock.Day;
        while (sim.Clock.Day == day)
            sim.Tick();
        while (sim.Clock.HourOfDay < CarterOrders.StandingHour + 0.5f)
            sim.Tick();
        Assert.True(FirewoodOrders.Of(sim).Owed(inn) > 0, "the inn sent to the carpenter");
        Assert.DoesNotContain(CarterOrders.Of(sim).All, o => o.Building == inn && o.Supply == "InnFirewood");
    }
}
