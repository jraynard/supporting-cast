using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Round;
using SupportingCast.Sim.Scenarios;
using SupportingCast.Sim.Shops;
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
        var price = sim.Defs.Service(ServiceKind.Drink).DefaultPrice + 1; // still under what any of them thinks it's worth
        sim.ApplyNow(new SetPriceCommand(keeper, inn, ServiceKind.Drink, price, true));
        Thirsty(sim, party[0]);
        sim.TickMany(SimClock.TicksPerHour / 6);
        Assert.Equal(price, OpenOf(sim, party[0], ServiceKind.Drink).Price);
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

    // ---------------- Worth and budget ----------------

    [Fact]
    public void TooDearTheySaySoGoWithoutAndThinkLessOfTheTown()
    {
        var (sim, keeper, inn, party) = Inn();
        var drinker = party[0];
        sim.ApplyNow(new SetPriceCommand(keeper, inn, ServiceKind.Drink, 50, true));
        var mood = sim.World.Get<Adventurer>(drinker).Satisfaction;
        Thirsty(sim, drinker);
        sim.TickMany(SimClock.TicksPerHour / 6);

        Assert.DoesNotContain(sim.Shops.All, o => o.Poster == drinker && o.Kind.Kind == ServiceKind.Drink);
        Assert.Contains(sim.Speech.Kept, u => u.Speaker == drinker && u.Text.Contains("50"));
        Assert.Equal(mood - ShopSystem.TooDearMood, sim.World.Get<Adventurer>(drinker).Satisfaction, 3);

        // Cheaper, and once they've stopped sulking, they'll have one.
        sim.ApplyNow(new SetPriceCommand(keeper, inn, ServiceKind.Drink, 2, true));
        Thirsty(sim, drinker);
        sim.TickMany(SimClock.HoursToTicks(ShopSystem.CooldownHours) + SimClock.TicksPerHour / 6);
        Thirsty(sim, drinker);
        sim.TickMany(SimClock.TicksPerHour / 6);
        Assert.Contains(sim.Shops.All, o => o.Poster == drinker && o.Kind.Kind == ServiceKind.Drink && o.Price == 2);
    }

    [Fact]
    public void WithAnEmptyPurseTheyOrderNothingButDontBlameTheTown()
    {
        var (sim, keeper, _, party) = Inn();
        var drinker = party[0];
        Coin.Pay(sim, drinker, keeper, Coin.Count(sim, drinker));
        var mood = sim.World.Get<Adventurer>(drinker).Satisfaction;
        Thirsty(sim, drinker);
        sim.TickMany(SimClock.TicksPerHour / 6);
        Assert.DoesNotContain(sim.Shops.All, o => o.Poster == drinker && o.Kind.Kind == ServiceKind.Drink);
        Assert.Equal(mood, sim.World.Get<Adventurer>(drinker).Satisfaction, 3);
    }

    [Fact]
    public void ABargainPleasesMoreThanAFullPrice()
    {
        float Gain(int price)
        {
            var (sim, keeper, inn, party) = Inn();
            var drinker = party[0];
            sim.ApplyNow(new SetPriceCommand(keeper, inn, ServiceKind.Drink, price, true));
            Thirsty(sim, drinker);
            sim.TickMany(SimClock.TicksPerHour / 6);
            var order = OpenOf(sim, drinker, ServiceKind.Drink);
            Assert.True(order.Worth >= price);
            var mood = sim.World.Get<Adventurer>(drinker).Satisfaction;
            sim.Shops.Complete(sim, order, keeper);
            return sim.World.Get<Adventurer>(drinker).Satisfaction - mood;
        }
        var full = TestWorld.LoadDefs().Service(ServiceKind.Drink).DefaultPrice + 1;
        Assert.True(Gain(0) > Gain(full));
    }

    [Fact]
    public void APartyTurnedAwayFromAQuestAsksAgainLater()
    {
        var (sim, keeper, inn, party) = Inn();
        sim.ApplyNow(new SetPriceCommand(keeper, inn, ServiceKind.Quest, ShopPrices.MaxPrice, true));
        sim.TickMany(SimClock.HoursToTicks(ShopSystem.QuestAfterHours) + SimClock.TicksPerHour / 6);
        Assert.DoesNotContain(sim.Shops.All, o => o.Kind.Kind == ServiceKind.Quest);

        sim.ApplyNow(new SetPriceCommand(keeper, inn, ServiceKind.Quest, sim.Defs.Service(ServiceKind.Quest).DefaultPrice, true));
        sim.TickMany(SimClock.HoursToTicks(ShopSystem.CooldownHours) + SimClock.TicksPerHour / 6);
        Assert.Single(sim.Shops.All, o => o.Kind.Kind == ServiceKind.Quest && o.IsOpen && party.Contains(o.Poster));
    }

    [Fact]
    public void WhoWentWithoutAndWhatOrdersWereWorthSurviveASave()
    {
        var (sim, keeper, inn, party) = Inn();
        sim.ApplyNow(new SetPriceCommand(keeper, inn, ServiceKind.Drink, 50, true));
        Thirsty(sim, party[0]);
        Hunger(sim, party[1], 0.5f);
        sim.TickMany(SimClock.TicksPerHour / 6);
        var meal = OpenOf(sim, party[1], ServiceKind.Meal);

        var loaded = SaveLoadTests.RoundTrip(sim);
        Assert.Equal(meal.Worth, loaded.Shops.Find(meal.Id)!.Worth);
        SaveLoadTests.AssertSameFuture(sim, loaded, 3);
    }

    // ---------------- Bots at the counter ----------------

    private static int DrinkPrice(Simulation sim, Entity inn) => ShopPrices.Posted(sim, inn, ServiceKind.Drink)!.Value.Price;

    [Fact]
    public void ABotKeeperDropsAPriceItHearsCalledTooDear()
    {
        var (sim, keeper, inn, party) = Inn();
        var bot = new Bots.BotPlayer(() => sim, Ana, c => sim.ApplyNow(c), 11);
        sim.ApplyNow(new SetPriceCommand(keeper, inn, ServiceKind.Drink, 50, true));
        for (var t = 0; t < SimClock.HoursToTicks(6) && DrinkPrice(sim, inn) == 50; t++)
        {
            if (t % SimClock.TicksPerHour == 0)
                foreach (var a in party)
                    Thirsty(sim, a);
            sim.Tick();
            bot.Tick();
        }
        Assert.True(DrinkPrice(sim, inn) < 50, $"bot: {bot.Doing}\n" + string.Join("\n", sim.Speech.Kept.TakeLast(12).Select(u => $"{u.SpeakerName}: {u.Text}")));
    }

    [Fact]
    public void ABotKeeperChargesMoreWhileItsLineIsLong()
    {
        var (sim, keeper, inn, _) = Inn();
        var bot = new Bots.BotPlayer(() => sim, Ana, c => sim.ApplyNow(c), 11);
        var start = DrinkPrice(sim, inn);
        var more = sim.Parties.Send(sim, sim.Defs.Party("IronLantern")); // a crowd
        Assert.NotEmpty(more);
        for (var t = 0; t < SimClock.HoursToTicks(6) && DrinkPrice(sim, inn) <= start; t++)
        {
            if (t % (SimClock.TicksPerHour / 2) == 0)
                foreach (var a in sim.Creatures().Where(e => sim.World.Has<Adventurer>(e)))
                    Thirsty(sim, a);
            sim.Tick();
            bot.Tick();
        }
        Assert.True(DrinkPrice(sim, inn) > start, $"line {sim.Shops.LineAt(sim, inn).Count}, bot: {bot.Doing}");
        Assert.True(DrinkPrice(sim, inn) <= start * Bots.BotPlayer.MaxMarkup);
    }

    // ---------------- The shop's name ----------------

    [Fact]
    public void TheInnIsTheWildBearUntilItsKeeperRenamesIt()
    {
        var (sim, keeper, inn, party) = Inn();
        Assert.Equal("The Wild Bear", ShopName.Of(sim, inn));
        Assert.Equal("The Wild Bear", Buildings.Called(sim, inn));

        sim.ApplyNow(new SetShopNameCommand(keeper, inn, "  The   Drowned\nRat!  "));
        Assert.Equal("The Drowned Rat!", ShopName.Of(sim, inn));
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("The Drowned Rat!"));

        // Anyone else can't, and an empty name puts the old one back.
        sim.ApplyNow(new SetShopNameCommand(party[0], inn, "Nobody's Inn"));
        Assert.Equal("The Drowned Rat!", ShopName.Of(sim, inn));
        sim.ApplyNow(new SetShopNameCommand(keeper, inn, "   "));
        Assert.Equal("The Wild Bear", ShopName.Of(sim, inn));
    }

    [Fact]
    public void ANameIsKeptShortAndSurvivesASave()
    {
        var (sim, keeper, inn, _) = Inn();
        sim.ApplyNow(new SetShopNameCommand(keeper, inn, new string('x', 80)));
        Assert.Equal(ShopName.MaxLength, ShopName.Of(sim, inn)!.Length);
        sim.ApplyNow(new SetShopNameCommand(keeper, inn, "The Gilded Goose"));
        var (loaded, _) = Saving.SaveGame.Read(Saving.SaveGame.Write(sim), sim.Defs);
        Assert.Equal("The Gilded Goose", ShopName.Of(loaded, inn));
    }

    [Fact]
    public void ShopsWithoutANameGoByWhatTheyAre()
    {
        var (sim, _, _, _) = Inn();
        var smithy = ShopSystem.BuildingOf(sim, sim.Defs.Building("Smithy"));
        Assert.Null(ShopName.Of(sim, smithy));
        Assert.StartsWith("the ", Buildings.Called(sim, smithy));
        Assert.EndsWith("smithy", Buildings.Called(sim, smithy));
    }

    [Fact]
    public void CustomersLineUpAtTheInnByName()
    {
        var (sim, _, _, party) = Inn();
        foreach (var a in party)
            Thirsty(sim, a);
        for (var i = 0; i < 24 && !party.Any(a => sim.World.Get<Mind>(a).Job?.Report?.Contains("The Wild Bear") == true); i++)
            sim.TickMany(SimClock.TicksPerHour / 12);
        Assert.Contains(party, a => sim.World.Get<Mind>(a).Job?.Report?.Contains("at The Wild Bear") == true);
    }
}
