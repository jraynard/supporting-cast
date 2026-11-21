using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// Supplies to order (<see cref="CarterOrders"/>): a player orders and pays, it's there next morning; a shop
/// the AI runs keeps itself in by its trade's standing orders; nobody's shop is stocked behind their back.
/// </summary>
public class CarterOrderTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    private static (Simulation Sim, Entity Me) Round(string trade)
    {
        var sim = VillageRound.NewSimulation(TestWorld.LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade(trade)], []));
        sim.ApplyNow(new StartRoundCommand());
        sim.Round.Endless = true;
        sim.TickMany(60);
        return (sim, Players.CharacterOf(sim, Ana));
    }

    /// <summary>On to just past the carter's next call (through the evening's standing orders, if before them).</summary>
    internal static void NextMorning(Simulation sim)
    {
        var hour = sim.Clock.HourOfDay;
        if (hour < CarterOrders.StandingHour)
        {
            sim.Clock.Tick += SimClock.HoursToTicks(CarterOrders.StandingHour - hour);
            sim.TickMany(SimClock.TicksPerHour / 2);
        }
        hour = sim.Clock.HourOfDay;
        sim.Clock.Tick += SimClock.HoursToTicks((24 - hour + CarterOrders.DeliveryHour) % 24) - 1;
        sim.TickMany(SimClock.TicksPerHour / 2);
    }

    private static void Silver(Simulation sim, Entity who, int n) => Inventory.Stow(sim, who, sim.CreateItem(sim.Defs.Thing(Coin.Silver), n));

    private static int Ore(Simulation sim, Entity who) => CarterOrders.InStock(sim, ShopSystem.WorkplaceOf(sim, who), sim.Defs.Supply("IronOre"));

    [Fact]
    public void AnOrderIsPaidAtOnceAndInTheChestNextMorning()
    {
        var (sim, me) = Round("Blacksmith");
        Silver(sim, me, 20);
        var purse = Coin.Count(sim, me);
        var iron = sim.Defs.Supply("IronOre");
        var before = Ore(sim, me);
        sim.ApplyNow(new OrderSuppliesCommand(me, "IronOre", 2));
        Assert.Equal(purse - 2 * iron.Price, Coin.Count(sim, me));
        Assert.Equal(before, Ore(sim, me)); // not till the morning
        NextMorning(sim);
        Assert.Equal(before + 2 * iron.Per, Ore(sim, me));
        Assert.DoesNotContain(CarterOrders.Of(sim).All, o => o.By == me);
    }

    [Fact]
    public void OnlyTheWorkplacesOwnSuppliesAndNoMoreThanTheCartBrings()
    {
        var (sim, me) = Round("Blacksmith");
        Silver(sim, me, 200);
        var orders = CarterOrders.Of(sim);
        Assert.NotNull(orders.Order(sim, me, sim.Defs.Supply("Keg"), 1)); // the inn's
        Assert.NotNull(orders.Order(sim, me, sim.Defs.Supply("IronOre"), 0));
        var iron = sim.Defs.Supply("IronOre");
        Assert.Null(orders.Order(sim, me, iron, iron.Max));
        Assert.NotNull(orders.Order(sim, me, iron, 1)); // the cart's full of it
        Assert.DoesNotContain(CarterOrders.Catalogue(sim, me), s => s.Name == "Keg");
        Assert.Contains(CarterOrders.Catalogue(sim, me), s => s.Name == "IronOre");
    }

    [Fact]
    public void ShortOfSilverItsRefusedAndTakenBackItsRefunded()
    {
        var (sim, me) = Round("Blacksmith");
        Coin.Pay(sim, me, Entity.Null, 0);
        foreach (var s in Coin.Stacks(sim, me))
            sim.World.Destroy(Inventory.TakeFromPerson(sim, me, s, sim.World.Get<Stack>(s).Count));
        var iron = sim.Defs.Supply("IronOre");
        Assert.NotNull(CarterOrders.Of(sim).Order(sim, me, iron, 1));
        Silver(sim, me, 10);
        Assert.Null(CarterOrders.Of(sim).Order(sim, me, iron, 2));
        Assert.Equal(10 - 2 * iron.Price, Coin.Count(sim, me));
        var id = CarterOrders.Of(sim).All.Single(o => o.By == me).Id;
        sim.ApplyNow(new CancelSuppliesCommand(me, id));
        Assert.Equal(10, Coin.Count(sim, me));
        Assert.Empty(CarterOrders.Of(sim).All);
    }

    [Fact]
    public void AnAiSmithyIsKeptInButAPlayersIsLeftToThem()
    {
        var (sim, _) = Round("Empath"); // every other trade the AI's
        var smithy = ShopSystem.BuildingFor(sim, Defs.TownRole.Forge);
        var keep = sim.Defs.Trade("Blacksmith").StandingOrders.Single(o => o.Supply == "IronOre").Keep;
        var box = CarterOrders.Into(sim, smithy, sim.Defs.Supply("IronOre"));
        Stock.TakeFrom(sim, box, "IronOre", Stock.CountIn(sim, box, "IronOre"));
        NextMorning(sim);
        Assert.True(Stock.CountIn(sim, box, "IronOre") >= Math.Min(keep, sim.Defs.Supply("IronOre").Max * sim.Defs.Supply("IronOre").Per),
            $"{Stock.CountIn(sim, box, "IronOre")} of {keep}");

        var (mine, me) = Round("Blacksmith");
        var myBox = CarterOrders.Into(mine, ShopSystem.WorkplaceOf(mine, me), mine.Defs.Supply("IronOre"));
        Stock.TakeFrom(mine, myBox, "IronOre", Stock.CountIn(mine, myBox, "IronOre"));
        NextMorning(mine);
        Assert.Equal(0, Stock.CountIn(mine, myBox, "IronOre"));
    }

    [Fact]
    public void ABotSmithSendsForOreOfAnEveningAndPaysForIt()
    {
        var (sim, me) = Round("Blacksmith");
        var bot = new Bots.BotPlayer(() => sim, Ana, c => sim.ApplyNow(c), 7) { Temper = Bots.BotTemperament.Steady };
        Silver(sim, me, 40);
        var box = CarterOrders.Into(sim, ShopSystem.WorkplaceOf(sim, me), sim.Defs.Supply("IronOre"));
        Stock.TakeFrom(sim, box, "IronOre", Stock.CountIn(sim, box, "IronOre"));
        sim.Clock.Tick += SimClock.HoursToTicks(CarterOrders.StandingHour - sim.Clock.HourOfDay);
        var purse = Coin.Count(sim, me);
        for (var t = 0; t < SimClock.TicksPerHour / 4; t++)
        {
            sim.Tick();
            bot.Tick();
        }
        Assert.Contains(CarterOrders.Of(sim).All, o => o.By == me && o.Supply == "IronOre");
        Assert.True(Coin.Count(sim, me) < purse, "paid from its own purse");
    }

    [Fact]
    public void AnOrderWaitingSurvivesASave()
    {
        var (sim, me) = Round("Blacksmith");
        Silver(sim, me, 20);
        Assert.Null(CarterOrders.Of(sim).Order(sim, me, sim.Defs.Supply("TinOre"), 1));
        var loaded = SaveLoadTests.RoundTrip(sim);
        var order = Assert.Single(CarterOrders.Of(loaded).All, o => o.By.Index == me.Index);
        Assert.Equal("TinOre", order.Supply);
        Assert.Equal(sim.Defs.Supply("TinOre").Price, order.Paid);
    }
}
