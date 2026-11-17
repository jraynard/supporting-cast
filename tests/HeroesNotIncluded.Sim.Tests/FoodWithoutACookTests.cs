using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// Getting fed with nobody playing the cook (a playtest's two-player round): the AI cook keeps
/// bowls up on the hatch, a player buys one and has it at once, and every cottage has a bread
/// crock the carter tops up, eaten from with F.
/// </summary>
public class FoodWithoutACookTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    private static (Simulation Sim, List<Entity> Party) Round(string playerTrade = "Healer")
    {
        var sim = VillageRound.NewSimulation(LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade(playerTrade)], []));
        sim.ApplyNow(new StartRoundCommand());
        var party = sim.Parties.Send(sim, sim.Defs.Party("GildedSpurs"));
        foreach (var e in party)
        {
            sim.World.Get<Needs>(e).Values[sim.Defs.Need("Food").Index] = 1f; // nobody else after a bowl
            sim.World.Get<Health>(e).Injuries.Clear();
            sim.World.Get<Adventurer>(e).NextDrink = sim.Clock.Tick + SimClock.TicksPerDay;
        }
        return (sim, party);
    }

    private static Entity Inn(Simulation sim) => ShopSystem.BuildingOf(sim, sim.Defs.Building("Inn"));

    private static Entity Stew(Simulation sim, Entity maker)
    {
        var stew = sim.CreateItem(sim.Thing("Stew"), 1);
        sim.World.Add(stew, new Crafted { Quality = Quality.Fine, Maker = maker });
        return stew;
    }

    [Fact]
    public void NobodyEatsTheBarsStockEvenStarving()
    {
        // From a playtest: villagers helped themselves to the berries behind the bar. The bar shelf's
        // stock is for mixing (a "kept" piece): with nothing else in town to eat, it's still left be.
        var (sim, _) = Round();
        var w = sim.World;
        var shelf = Buildings.Parts(sim, Inn(sim)).Single(p => w.Get<Thing>(p).Def.Name == "BarShelf");
        int Berries() => sim.StoredIn(shelf).Where(e => w.Get<Thing>(e).Def.Name == "Berries").Sum(e => w.Get<Stack>(e).Count);
        var before = Berries();
        Assert.True(before > 0);
        foreach (var e in w.Query<Stack>())
            if (w.Get<Thing>(e).Def.Nutrition > 0 && !(w.TryGet<Stored>(e, out var st) && st.In == shelf))
                sim.DestroyThing(e);
        var food = sim.Defs.Need("Food").Index;
        foreach (var e in sim.Creatures().Where(c => w.Has<Trade>(c)))
            w.Get<Needs>(e).Values[food] = 0.02f;
        sim.TickMany(SimClock.TicksPerHour * 3);
        Assert.Equal(before, Berries());
    }

    [Fact]
    public void ABowlAlreadyUpIsPaidForAndInHandAtOnce()
    {
        var (sim, _) = Round();
        var me = Players.CharacterOf(sim, Ana);
        var inn = Inn(sim);
        var hatch = Hatch.Of(sim, inn);
        var cook = sim.Creatures().First(c => sim.World.TryGet<Trade>(c, out var t) && t.Def.Serving(ServiceKind.Meal));
        sim.Store(hatch, Stew(sim, cook));
        sim.World.Get<Position>(me) = Position.At(ShopSystem.LineCells(sim, inn, hatch: true)[0]);
        var purse = Coin.Count(sim, me);

        AvatarSystem.Perform(sim, me, AvatarSystem.ActionAt(sim, me, sim.World.Get<Thing>(hatch).Cell));
        // No waiting on the shops' round: bought as it's ordered.
        Assert.DoesNotContain(sim.Shops.All, e => e.IsOpen && e.Poster == me);
        Assert.True(Coin.Count(sim, me) < purse);
        Assert.True(sim.World.TryGet<Hospitality.Savouring>(me, out var savouring) && savouring.Meal);
    }

    [Fact]
    public void TheAICookPutsBowlsUpForAHungryPlayerWithNoOrders()
    {
        var (sim, _) = Round();
        var inn = Inn(sim);
        var hatch = Hatch.Of(sim, inn);
        var me = Players.CharacterOf(sim, Ana);
        sim.TickMany(SimClock.TicksPerHour * 2);
        Assert.Empty(Hatch.DishesOn(sim, hatch, ServiceKind.Meal)); // nobody's hungry: none kept up to go cold
        sim.World.Get<Needs>(me).Values[sim.Defs.Need("Food").Index] = 0.3f;
        for (var i = 0; i < 24 && Hatch.DishesOn(sim, hatch, ServiceKind.Meal).Count == 0; i++)
            sim.TickMany(SimClock.TicksPerHour / 2);
        var up = Hatch.DishesOn(sim, hatch, ServiceKind.Meal).Sum(d => sim.World.Get<Stack>(d).Count);
        Assert.InRange(up, 1, Hatch.Spare + Hatch.Waiting(sim, inn, ServiceKind.Meal));
    }

    [Fact]
    public void EveryCottageHasABreadCrockStockedAtTheDealAndToppedUpEachMorning()
    {
        var (sim, _) = Round();
        var me = Players.CharacterOf(sim, Ana);
        var home = Homes.HomeOf(sim, me);
        var crock = Buildings.Parts(sim, home).Single(e => Homes.IsCrock(sim, e));
        int Bread() => sim.StoredIn(crock).Where(e => sim.World.Get<Thing>(e).Def.Name == "Bread").Sum(e => sim.World.Get<Stack>(e).Count);
        Assert.Equal(Homes.CrockFood.Single(f => f.Def == "Bread").Count, Bread());

        // Hungry by it: F eats from it.
        sim.World.Get<Position>(me) = Position.At(sim.SpiralFrom(sim.World.Get<Thing>(crock).Cell, 1)
            .First(c => c != sim.World.Get<Thing>(crock).Cell && sim.Map.Passable(c) && Buildings.At(sim, c) == home));
        sim.World.Get<Needs>(me).Values[sim.Defs.Need("Food").Index] = 0.3f;
        var action = AvatarSystem.ActionAt(sim, me, sim.World.Get<Thing>(crock).Cell);
        Assert.Equal(AvatarActionKind.EatFromCrock, action.Kind);
        AvatarSystem.Perform(sim, me, action);
        for (var i = 0; i < 60 && sim.World.Get<Needs>(me).Get(sim.Defs.Need("Food")) < 0.4f; i++)
            sim.TickMany(60);
        Assert.True(sim.World.Get<Needs>(me).Get(sim.Defs.Need("Food")) > 0.4f, "fed from the crock");
        Assert.True(Bread() < Homes.CrockFood.Single(f => f.Def == "Bread").Count);

        // Fed, F there just opens it.
        sim.World.Get<Needs>(me).Values[sim.Defs.Need("Food").Index] = 1f;
        Assert.NotEqual(AvatarActionKind.EatFromCrock, AvatarSystem.ActionAt(sim, me, sim.World.Get<Thing>(crock).Cell).Kind);

        // The carter's round tops it back up.
        var day = sim.Clock.Day;
        while (sim.Clock.Day == day || sim.Clock.HourOfDay < InnSupplySystem.DeliveryHour + 0.5f)
            sim.TickMany(SimClock.TicksPerHour / 4);
        Assert.Equal(Homes.CrockFood.Single(f => f.Def == "Bread").Count, Bread());
    }
}
