using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Shops;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Round;
using SupportingCast.Sim.Scenarios;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Tests;

public class ShopTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Bea = new("00000000-0000-0000-0000-00000000000b");

    /// <summary>A round with players in it and a party in town.</summary>
    private static (Simulation Sim, List<Entity> Party) Round(params Guid[] players)
    {
        var sim = VillageRound.NewSimulation(TestWorld.LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        foreach (var p in players)
            sim.ApplyNow(new JoinLobbyCommand(p, p.ToString()[^1..], default, []));
        sim.ApplyNow(new StartRoundCommand());
        var party = sim.Parties.Send(sim, sim.Defs.Party("GildedSpurs"));
        Assert.NotEmpty(party);
        sim.TickMany(SimClock.TicksPerHour / 2);
        return (sim, party);
    }

    private static void Hunger(Simulation sim, Entity who, float food) =>
        sim.World.Get<Needs>(who).Values[sim.Defs.Need("Food").Index] = food;

    private static void Beside(Simulation sim, Entity who, Entity other)
    {
        var at = sim.World.Get<Position>(other).Cell;
        var cell = sim.SpiralFrom(at, 3).First(c => c != at && sim.Map.Passable(c) && c.Z == at.Z);
        sim.World.Get<Position>(who).Cell = cell;
        sim.World.Get<Position>(who).Pos = Position.CenterOf(cell);
    }

    private static int AllCoin(Simulation sim) => sim.Creatures().Sum(e => Coin.Count(sim, e));

    private static Order OpenOf(Simulation sim, Entity poster, ServiceKind kind) =>
        sim.Shops.All.Single(e => e.IsOpen && e.Poster == poster && e.Kind.Kind == kind);

    [Fact]
    public void AHungryAdventurerAsksAloudAndOnlyThoseWhoHeardKnow()
    {
        var (sim, party) = Round(Ana);
        var me = Players.CharacterOf(sim, Ana);
        var diner = party[0];
        Beside(sim, me, diner);
        Hunger(sim, diner, 0.5f);
        sim.TickMany(SimClock.TicksPerHour / 6);

        var meal = OpenOf(sim, diner, ServiceKind.Meal);
        Assert.True(meal.Price > 0);
        Assert.Contains(Perception.For(sim, me).Orders(), e => e.Id == meal.Id && e.Kind == ServiceKind.Meal);
        // Someone far off didn't hear it.
        var far = sim.Creatures().First(e => e != me && !party.Contains(e)
            && Map.Distance(sim.World.Get<Position>(e).Cell, sim.World.Get<Position>(diner).Cell) > 40);
        Assert.DoesNotContain(meal.KnownBy, k => k == far);
    }

    [Fact]
    public void EachPartyAsksForAQuestOnce()
    {
        var (sim, party) = Round(Ana);
        sim.TickMany(SimClock.TicksPerHour);
        Assert.DoesNotContain(sim.Shops.All, e => e.Kind.Kind == ServiceKind.Quest); // they want serving first
        sim.TickMany(SimClock.HoursToTicks(ShopSystem.QuestAfterHours));
        Assert.Single(sim.Shops.All, e => e.Kind.Kind == ServiceKind.Quest && party.Contains(e.Poster));
    }

    [Fact]
    public void HandingFoodToAnAdventurerWhoOrderedItServesItAndPays()
    {
        var (sim, party) = Round(Ana);
        var me = Players.CharacterOf(sim, Ana);
        var diner = party[0];
        Hunger(sim, diner, 0.5f);
        sim.TickMany(SimClock.TicksPerHour / 6);
        var meal = OpenOf(sim, diner, ServiceKind.Meal);

        Beside(sim, me, diner);
        var stew = sim.CreateItem(sim.Defs.Thing("Stew"), 3);
        Inventory.Stow(sim, me, stew);
        var coinBefore = AllCoin(sim);
        var mine = Coin.Count(sim, me);
        var held = sim.World.Get<Carrying>(me).Item;
        var stack = held.IsNull ? sim.StoredIn(sim.PackOf(me)).First(i => sim.World.Get<Thing>(i).Def.Name == "Stew") : held;

        sim.ApplyNow(new GiveCommand(me, diner, stack, 3));

        Assert.Equal(OrderStatus.Done, meal.Status);
        Assert.Equal(mine + meal.Price, Coin.Count(sim, me));
        Assert.Equal(coinBefore, AllCoin(sim));
        Assert.True(sim.World.Get<Needs>(diner).Get(sim.Defs.Need("Food")) > 0.5f, "they ate it");
        Assert.True(sim.World.Get<Adventurer>(diner).Satisfaction > 0);
    }

    [Fact]
    public void AnOrderNobodyServesExpiresAndTheyThinkLessOfTheTown()
    {
        var (sim, party) = Round(Ana);
        var diner = party[0];
        Hunger(sim, diner, 0.5f);
        sim.TickMany(SimClock.TicksPerHour / 6);
        var meal = OpenOf(sim, diner, ServiceKind.Meal);
        Hunger(sim, diner, 0.5f);

        sim.TickMany(SimClock.HoursToTicks(meal.Kind.PatienceHours + 0.5f));

        Assert.Equal(OrderStatus.Expired, meal.Status);
        // Others in town may have served them meanwhile; the meal they waited for counts against it.
        Assert.Contains(sim.Speech.Kept, u => u.Speaker == diner && u.Text.Contains("So much for a meal"));
    }

    [Fact]
    public void PayingIsHandingOverCoin()
    {
        var (sim, party) = Round(Ana);
        var me = Players.CharacterOf(sim, Ana);
        var them = party[0];
        Beside(sim, me, them);
        Inventory.Stow(sim, me, sim.CreateItem(sim.Defs.Thing(Coin.Silver), 5));
        var coins = sim.StoredIn(sim.PackOf(me)).First(i => Coin.IsSilver(sim, i));
        var theirs = Coin.Count(sim, them);

        sim.ApplyNow(new GiveCommand(me, them, coins, 3));
        Assert.Equal(2, Coin.Count(sim, me));
        Assert.Equal(theirs + 3, Coin.Count(sim, them));

        // Out of reach, nothing moves.
        sim.World.Get<Position>(them).Cell += new int3(10, 0, 0);
        sim.ApplyNow(new GiveCommand(me, them, coins, 2));
        Assert.Equal(2, Coin.Count(sim, me));
    }

    // ---------------- The price sign ----------------

    /// <summary>A round with Ana as the innkeeper (and a party in town), and the inn.</summary>
    private static (Simulation Sim, Entity Keeper, Entity Inn, List<Entity> Party) Inn()
    {
        var sim = VillageRound.NewSimulation(TestWorld.LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default, []));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade("Innkeeper")], []));
        sim.ApplyNow(new StartRoundCommand());
        var party = sim.Parties.Send(sim, sim.Defs.Party("GildedSpurs"));
        Assert.NotEmpty(party);
        var inn = ShopSystem.BuildingOf(sim, sim.Defs.Building("Inn"));
        Assert.False(inn.IsNull);
        return (sim, Players.CharacterOf(sim, Ana), inn, party);
    }

    private static void Thirsty(Simulation sim, Entity who) => sim.World.Get<Adventurer>(who).NextDrink = sim.Clock.Tick;

    [Fact]
    public void AShopChargesTheDefaultsUntilItsKeeperSaysOtherwise()
    {
        var (sim, keeper, inn, _) = Inn();
        var sign = Perception.For(sim, keeper).Prices(inn);
        Assert.Equal(sim.Defs.AllServices.Where(d => d.At == "Inn").Select(d => d.Kind), sign.Select(p => p.Kind));
        Assert.All(sign, p => Assert.True(p.Offered && p.Price == sim.Defs.Service(p.Kind).DefaultPrice));

        sim.ApplyNow(new SetPriceCommand(keeper, inn, ServiceKind.Drink, 7, true));
        Assert.Equal(7, ShopPrices.Posted(sim, inn, ServiceKind.Drink)!.Value.Price);
        Assert.Equal(sim.Defs.Service(ServiceKind.Meal).DefaultPrice, ShopPrices.Posted(sim, inn, ServiceKind.Meal)!.Value.Price);
    }

    [Fact]
    public void OnlyTheKeeperChangesTheSignAndOnlyForWhatTheShopServes()
    {
        var (sim, keeper, inn, party) = Inn();
        var bea = sim.Creatures().First(e => e != keeper && sim.World.Has<Trade>(e) && sim.World.Get<Trade>(e).Def.Name != "Innkeeper");
        sim.World.Add(bea, new Avatar());

        sim.ApplyNow(new SetPriceCommand(bea, inn, ServiceKind.Drink, 50, true)); // not their shop
        sim.ApplyNow(new SetPriceCommand(keeper, inn, ServiceKind.Repair, 50, true)); // not served here
        sim.ApplyNow(new SetPriceCommand(keeper, party[0], ServiceKind.Drink, 50, true)); // not a shop
        sim.ApplyNow(new SetPriceCommand(keeper, inn, (ServiceKind)200, 50, true));
        Assert.False(sim.World.Has<ShopPrices>(inn));
        Assert.Null(ShopPrices.Posted(sim, inn, ServiceKind.Repair));

        sim.ApplyNow(new SetPriceCommand(keeper, inn, ServiceKind.Drink, int.MaxValue, true));
        Assert.Equal(ShopPrices.MaxPrice, ShopPrices.Posted(sim, inn, ServiceKind.Drink)!.Value.Price);
        sim.ApplyNow(new SetPriceCommand(keeper, inn, ServiceKind.Drink, -5, true));
        Assert.Equal(0, ShopPrices.Posted(sim, inn, ServiceKind.Drink)!.Value.Price);
    }

    [Fact]
    public void AdventurersPayWhatTheSignSays()
    {
        var (sim, keeper, inn, party) = Inn();
        sim.ApplyNow(new SetPriceCommand(keeper, inn, ServiceKind.Drink, 4, true));
        Thirsty(sim, party[0]);
        sim.TickMany(SimClock.TicksPerHour / 6);
        Assert.Equal(4, OpenOf(sim, party[0], ServiceKind.Drink).Price);
    }

    [Fact]
    public void NobodyOrdersWhatTheShopIsntOffering()
    {
        var (sim, keeper, inn, party) = Inn();
        sim.ApplyNow(new SetPriceCommand(keeper, inn, ServiceKind.Drink, 2, false));
        Assert.False(ShopPrices.Posted(sim, inn, ServiceKind.Drink)!.Value.Offered);
        foreach (var a in party)
            Thirsty(sim, a);
        sim.TickMany(SimClock.TicksPerHour / 6);
        Assert.DoesNotContain(sim.Shops.All, o => o.Kind.Kind == ServiceKind.Drink);
    }

    [Fact]
    public void TheSignSurvivesASave()
    {
        var (sim, keeper, inn, _) = Inn();
        sim.ApplyNow(new SetPriceCommand(keeper, inn, ServiceKind.Drink, 9, true));
        sim.ApplyNow(new SetPriceCommand(keeper, inn, ServiceKind.Lodging, 5, false));
        var loaded = SaveLoadTests.RoundTrip(sim);
        Assert.Equal(Perception.For(sim, keeper).Prices(inn), Perception.For(loaded, keeper).Prices(inn));
    }
}
