using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Hospitality;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>The kitchen and the town: feasts, broths for the healer, keeping the larder, word of the food.</summary>
public class KitchenTownTests
{
    // ---------------- Feasts ----------------

    private static (Simulation Sim, Entity Me, List<Entity> Party, Entity Inn) PartyInTheInn()
    {
        var (sim, me, party) = ExpeditionTests.Round("Cook", tillTheyAsk: false);
        var inn = ShopSystem.BuildingOf(sim, sim.Defs.Building("Inn"));
        if (!sim.World.Has<ShopPrices>(inn))
            sim.World.Add(inn, ShopPrices.Defaults(sim.Defs));
        sim.World.Get<ShopPrices>(inn).Offered[(int)ServiceKind.Quest] = false; // they stay in town
        var b = sim.World.Get<PresetBuilding>(inn);
        var inside = Buildings.CellOf(sim.Map, b.Def, b.Anchor, b.Facing, 3, 7);
        Assert.Equal(inn, Buildings.At(sim, inside));
        foreach (var e in party)
            sim.World.Get<Adventurer>(e).LeaveTick = sim.Clock.Tick + SimClock.TicksPerDay * 2;
        var leader = party[0];
        sim.World.Get<Position>(leader).Cell = inside;
        sim.World.Get<Position>(leader).Pos = Position.CenterOf(inside);
        return (sim, me, party, inn);
    }

    [Fact]
    public void APartyBackInTriumphCallsForAFeastAndADishEach()
    {
        var (sim, _, party, _) = PartyInTheInn();
        var before = sim.Shops.All.Select(o => o.Id).ToHashSet();
        sim.World.Add(party[0], new FeastDue { Party = sim.World.Get<Adventurer>(party[0]).Party });
        sim.TickMany(sim.Feasts.Interval + 1);
        Assert.False(sim.World.Has<FeastDue>(party[0]), $"in {Buildings.At(sim, sim.World.Get<Position>(party[0]).Cell)}, can act {Speech.SpeechSystem.CanAct(sim, party[0])}");
        var feast = Assert.Single(sim.Feasts.Active);
        Assert.Equal(party.Count, feast.Orders.Count);
        Assert.All(sim.Shops.All.Where(o => !before.Contains(o.Id)), o => Assert.Equal(ServiceKind.Meal, o.Kind.Kind));
    }

    [Fact]
    public void AFeastLaidOnCheersTheRoomAndTheTown()
    {
        var (sim, me, party, inn) = PartyInTheInn();
        var feast = sim.Feasts.Call(sim, party[0], sim.World.Get<Adventurer>(party[0]).Party, inn)!;
        var renown = sim.Reckoning.Renown;
        var pleased = sim.World.Get<Adventurer>(party[0]).Satisfaction;
        foreach (var o in sim.Shops.All.Where(o => feast.Orders.Contains(o.Id)).ToList())
            sim.Shops.Complete(sim, o, me);
        sim.TickMany(sim.Feasts.Interval + 1);
        Assert.Empty(sim.Feasts.Active);
        Assert.True(sim.Reckoning.Renown > renown);
        Assert.True(sim.World.Get<Adventurer>(party[0]).Satisfaction > pleased);
        Assert.Contains(sim.World.Get<Mood>(party[0]).Memories, m => m.Def.Name == "Feasted");
    }

    [Fact]
    public void AFeastLeftBareDisappoints()
    {
        var (sim, _, party, inn) = PartyInTheInn();
        sim.Feasts.Call(sim, party[0], sim.World.Get<Adventurer>(party[0]).Party, inn);
        // Just before the clock runs out, then past it: the long wait has its own ups and downs.
        sim.Clock.Tick = sim.Feasts.Active[0].Deadline - sim.Feasts.Interval * 2;
        sim.TickMany(sim.Feasts.Interval);
        Assert.NotEmpty(sim.Feasts.Active);
        var pleased = sim.World.Get<Adventurer>(party[0]).Satisfaction;
        sim.TickMany(sim.Feasts.Interval * 4);
        Assert.Empty(sim.Feasts.Active);
        Assert.True(sim.World.Get<Adventurer>(party[0]).Satisfaction < pleased);
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("feast fell flat"));
    }

    [Fact]
    public void AFeastSurvivesASave()
    {
        var (sim, _, party, inn) = PartyInTheInn();
        sim.Feasts.Call(sim, party[0], sim.World.Get<Adventurer>(party[0]).Party, inn);
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        var (a, b) = (sim.Feasts.Active[0], Assert.Single(loaded.Feasts.Active));
        Assert.Equal((a.Party, a.Inn, a.Deadline), (b.Party, b.Inn, b.Deadline));
        Assert.Equal(a.Orders, b.Orders);
    }

    // ---------------- Broths ----------------

    [Fact]
    public void AFreshPotIsABrothThatMendsAndGivesBackMana()
    {
        var (sim, me, pot) = InnKitchenTests.Kitchen();
        Cooking.Add(sim, pot, sim.CreateItem(sim.Thing("Meat"), 3));
        Cooking.Add(sim, pot, sim.CreateItem(sim.Thing("Cabbage"), 6));
        Cooking.Add(sim, pot, sim.CreateItem(sim.Thing("Herbs"), 2));
        var p = Cooking.PotOf(sim, pot);
        p.Doneness = 1f;
        Assert.True(Perception.For(sim, me).Pot(pot)!.Value.Mends);
        var bowl = Cooking.Ladle(sim, me, pot);
        Assert.True(Broths.IsBroth(sim, bowl));
        sim.World.Add(me, new Mana { Current = 10, Max = 100 });
        Broths.Eaten(sim, me, bowl);
        Assert.Equal(Broths.HealFactor, Broths.HealFactorOf(sim, me));
        Assert.Equal(10 + Broths.Mana, sim.World.Get<Mana>(me).Current);
    }

    [Fact]
    public void APotOfMeatIsNoBroth()
    {
        var (sim, me, pot) = InnKitchenTests.Kitchen();
        Cooking.Add(sim, pot, sim.CreateItem(sim.Thing("Meat"), 6));
        Cooking.PotOf(sim, pot).Doneness = 1f;
        var bowl = Cooking.Ladle(sim, me, pot);
        Assert.False(Broths.IsBroth(sim, bowl));
        Broths.Eaten(sim, me, bowl);
        Assert.Equal(1f, Broths.HealFactorOf(sim, me));
    }

    // ---------------- Keeping the larder ----------------

    [Fact]
    public void TheLarderKeepsCoolerThanTheKitchen()
    {
        var (sim, _, _) = InnKitchenTests.Kitchen();
        var larder = sim.SpawnStructure(sim.Thing("Larder"), C(8, 8));
        sim.RebuildDerivedIfNeeded();
        var meat = sim.CreateItem(sim.Thing("Meat"), 4);
        sim.Store(larder, meat);
        var at = sim.World.Get<Thing>(larder).Cell;
        Assert.True(RotSystem.KeptAt(sim, meat, at) <= sim.Thing("Larder").Building!.KeepsAt!.Value);
        Assert.True(RotSystem.RateAt(RotSystem.KeptAt(sim, meat, at)) < 1f);
    }

    [Fact]
    public void SaltedMeatKeepsAndSeasonsThePot()
    {
        var (sim, me, pot) = InnKitchenTests.Kitchen();
        var table = sim.SpawnStructure(sim.Thing("PrepTable"), C(10, 12));
        sim.RebuildDerivedIfNeeded();
        Inventory.Stow(sim, me, sim.CreateItem(sim.Thing("Meat"), 5));
        Inventory.Stow(sim, me, sim.CreateItem(sim.Thing(Larder.Salt), 3));
        sim.ApplyNow(new PrepCommand(me, table, PrepStep.Salt));
        var on = sim.StoredIn(sim.PackOf(me)).ToList();
        var salted = on.Single(e => sim.World.Get<Thing>(e).Def.Name == "SaltMeat");
        Assert.Equal(5, sim.World.Get<Stack>(salted).Count);
        Assert.Equal(1, sim.World.Get<Stack>(on.Single(e => sim.World.Get<Thing>(e).Def.Name == Larder.Salt)).Count); // two scoops for five
        Assert.True(sim.Thing("SaltMeat").RotDays > sim.Thing("Meat").RotDays * 5);
        Cooking.Add(sim, pot, salted);
        Assert.True(Cooking.PotOf(sim, pot).Seasoning > 0.3f, $"{Cooking.PotOf(sim, pot).Seasoning}");
    }

    [Fact]
    public void MeatOnTheSmokingRackByAFireCures()
    {
        var (sim, _, _) = InnKitchenTests.Kitchen();
        var rack = sim.SpawnStructure(sim.Thing("SmokingRack"), C(12, 11));
        sim.RebuildDerivedIfNeeded();
        Assert.True(Larder.Smoky(sim, rack)); // the kitchen test's hearth is two cells off
        sim.Store(rack, sim.CreateItem(sim.Thing("Meat"), 3));
        sim.TickMany(SimClock.HoursToTicks(Larder.SmokeHours + 0.5f));
        var cured = Assert.Single(sim.StoredIn(rack));
        Assert.Equal("SmokedMeat", sim.World.Get<Thing>(cured).Def.Name);
        Assert.Equal(3, sim.World.Get<Stack>(cured).Count);
    }

    // ---------------- Word of the food ----------------

    [Fact]
    public void FineMealsGetTheTableTalkedOfAndBringPartiesSooner()
    {
        var sim = Create(20, 20);
        Assert.Equal(0f, sim.Shops.FareFactor);
        for (var i = 0; i < 12; i++)
            sim.Shops.HeardOfTheFood(1f);
        Assert.True(sim.Shops.FareFactor > 0.1f);
        Assert.NotEqual(ShopSystem.FareWord(0f), ShopSystem.FareWord(sim.Shops.Fare));
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.Equal(sim.Shops.Fare, loaded.Shops.Fare);
        sim.Shops.TalkFades();
        Assert.True(sim.Shops.Fare < loaded.Shops.Fare);
    }
}
