using HeroesNotIncluded.Sim.Bots;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>The inn's kitchen: a stew pot cooked by taste over the fire, ladled out by the bowl.</summary>
public class InnKitchenTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    /// <summary>How a test cook goes about it.</summary>
    public enum CookStyle
    {
        Careful,
        BoilAndWalkAway,
    }

    /// <summary>A lit hearth at (11, 10) with the pot beside it at (11, 11), and a player at (10, 11).</summary>
    public static (Simulation Sim, Entity Me, Entity Pot) Kitchen()
    {
        var sim = Create(40, 40);
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Inna", C(10, 11), ageDays: 25 * 365);
        sim.Round.OpenLobby();
        sim.Round.Sit(Ana, "Inna", null, null, null);
        sim.Round.Start(sim);
        sim.GiveStartingPack(me);
        var hearth = sim.SpawnStructure(sim.Thing("Hearth"), C(11, 10));
        var pot = sim.SpawnStructure(sim.Thing("StewPot"), C(11, 11));
        sim.RebuildDerivedIfNeeded();
        sim.World.Get<Fueled>(hearth).Fuel = 10;
        Inventory.Stow(sim, me, sim.CreateItem(sim.Thing("Bowl"), 4));
        return (sim, me, pot);
    }

    /// <summary>Put food from the hand in the pot with the work key.</summary>
    internal static void Fill(Simulation sim, Entity me, Entity pot, params (string Def, int Count)[] food)
    {
        foreach (var (def, count) in food)
        {
            var stack = sim.CreateItem(sim.Thing(def), count);
            sim.World.Get<Carrying>(me).Item = stack;
            Assert.Equal(AvatarActionKind.Cook, AvatarSystem.ActionAt(sim, me, sim.World.Get<Thing>(pot).Cell).Kind);
            sim.ApplyNow(new PotCommand(me, pot, PotStep.Next));
            Assert.True(sim.World.Get<Carrying>(me).Item.IsNull, "it went in");
        }
    }

    /// <summary>Tend the pot until it's cooked (or <paramref name="maxTicks"/>), driving the session as a player would.</summary>
    internal static void Tend(Simulation sim, Entity me, Entity pot, CookStyle style = CookStyle.Careful, int maxTicks = 60 * 60 * 3)
    {
        sim.ApplyNow(new PotCommand(me, pot, PotStep.Tend));
        Assert.IsType<StewSession>(sim.World.Get<WorkSession>(me));
        var predictor = new WorkPredictor();
        var stir = 0;
        for (var t = 0; t < maxTicks && sim.World.Has<WorkSession>(me); t++)
        {
            sim.Tick();
            bool held = false;
            var axis = 0;
            if (predictor.Session is StewSession s)
            {
                if (style == CookStyle.Careful)
                {
                    // Keep it at a simmer, and give it a stir every couple of seconds.
                    axis = s.Flame < 0.6f ? 1 : s.Flame > 0.64f ? -1 : 0;
                    if (s.SinceStir > 120)
                        stir = 20;
                    held = stir-- > 0;
                }
                else
                {
                    axis = 1;
                    if (s.Flame >= 1f)
                    {
                        sim.ApplyNow(new StopWorkCommand(me));
                        break;
                    }
                }
            }
            predictor.Update(sim, me, 1, false, held, axis, c => sim.ApplyNow(c));
        }
        sim.Tick();
    }

    /// <summary>Season it by taste: a pinch, a taste, until the taste says it's right (or it's gone too far).</summary>
    internal static void Season(Simulation sim, Entity me, Entity pot)
    {
        sim.ApplyNow(new PotCommand(me, pot, PotStep.Season));
        Assert.IsType<SeasonSession>(sim.World.Get<WorkSession>(me));
        var predictor = new WorkPredictor();
        var phase = 0;
        for (var t = 0; t < 60 * 60 && sim.World.Has<WorkSession>(me); t++)
        {
            sim.Tick();
            bool press = false;
            var axis = 0;
            if (predictor.Session is SeasonSession s)
            {
                // A pinch, then a taste, then (taste come up) decide.
                phase = (phase + 1) % 30;
                if (s.TastedAt == s.Pinches && s.Taste is "about right" or "a touch salty" or "far too salty")
                    axis = -1;
                else if (phase == 0)
                    press = s.TastedAt == s.Pinches || s.Pinches == 0;
                else if (phase == 15 && s.TastedAt != s.Pinches)
                    axis = 1;
            }
            predictor.Update(sim, me, 1, press, false, axis, c => sim.ApplyNow(c));
        }
        sim.Tick();
    }

    [Fact]
    public void ACarefulPotMakesAFineStew()
    {
        var (sim, me, pot) = Kitchen();
        Fill(sim, me, pot, ("Cabbage", 6), ("Meat", 4), ("Berries", 5));
        Tend(sim, me, pot);
        var p = sim.World.Get<StewPot>(pot);
        Assert.True(p.Cooked, $"cooked {p.Doneness:0.00}");
        Assert.True(p.Scorch < 0.1f, $"scorch {p.Scorch:0.00}");
        Season(sim, me, pot);

        sim.ApplyNow(new PotCommand(me, pot, PotStep.Ladle));
        var bowl = sim.World.Get<Carrying>(me).Item;
        Assert.False(bowl.IsNull, "a bowl in hand");
        Assert.Equal("Stew", sim.World.Get<Thing>(bowl).Def.Name);
        Assert.True(sim.World.Get<Crafted>(bowl).Quality >= Quality.Fine, $"{sim.World.Get<Crafted>(bowl).Quality} (q {Cooking.QualityOf(p):0.00})");
        Assert.True(Cooking.ServingsLeft(sim, pot) > 3, "a pot goes a long way");
    }

    [Fact]
    public void LeftAtARollingBoilItCatchesOnTheBottom()
    {
        var (sim, me, pot) = Kitchen();
        Fill(sim, me, pot, ("Cabbage", 6), ("Meat", 4));
        Tend(sim, me, pot, CookStyle.BoilAndWalkAway);
        sim.TickMany(SimClock.TicksPerSecond * 60);
        var p = sim.World.Get<StewPot>(pot);
        Assert.True(p.Scorch > 0.6f, $"scorch {p.Scorch:0.00}");
        Assert.Equal("it's caught on the bottom", Perception.For(sim, me).Pot(pot)!.Value.Caught);
        sim.World.Get<StewPot>(pot).Doneness = 1f;
        var bowl = Cooking.Ladle(sim, me, pot);
        Assert.True(sim.World.Get<Crafted>(bowl).Quality <= Quality.Plain, $"{sim.World.Get<Crafted>(bowl).Quality}");
    }

    [Fact]
    public void WithTheFireOutItNeverCooks()
    {
        var (sim, me, pot) = Kitchen();
        Fill(sim, me, pot, ("Cabbage", 6));
        foreach (var e in sim.World.Query<Fueled>())
            sim.World.Get<Fueled>(e).Fuel = 0;
        sim.World.Get<StewPot>(pot).Flame = 1f;
        sim.TickMany(SimClock.TicksPerSecond * 120);
        Assert.Equal(Surface.Still, Perception.For(sim, me).Pot(pot)!.Value.Surface);
        Assert.Equal(0f, sim.World.Get<StewPot>(pot).Doneness);
    }

    [Fact]
    public void ATurnedIngredientSpoilsThePotUnseen()
    {
        var (sim, me, pot) = Kitchen();
        var meat = sim.CreateItem(sim.Thing("Meat"), 4);
        sim.World.Get<Rot>(meat).Progress = 0.95f;
        sim.World.Get<Carrying>(me).Item = meat;
        sim.ApplyNow(new PotCommand(me, pot, PotStep.Add));
        Fill(sim, me, pot, ("Cabbage", 2));
        sim.World.Get<StewPot>(pot).Doneness = 1f;

        var bowl = Cooking.Ladle(sim, me, pot);
        Assert.True(FoodPoisoning.IsSpoiled(sim, bowl), $"rot {FoodPoisoning.RotOf(sim, bowl):0.00}");
        // Nothing the cook can see says so: the pot looks like any other.
        var seen = Perception.For(sim, me).Pot(pot)!.Value;
        Assert.DoesNotContain("rot", seen.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SaltCantBeTakenBackOut()
    {
        var (sim, me, pot) = Kitchen();
        Fill(sim, me, pot, ("Cabbage", 6));
        sim.ApplyNow(new PotCommand(me, pot, PotStep.Season));
        var s = (SeasonSession)sim.World.Get<WorkSession>(me);
        var inputs = Enumerable.Range(0, 20).Select(i => new WorkInput(i * SeasonSession.PinchSteps, WorkInputKind.Press)).ToList();
        inputs.Add(new WorkInput(20 * SeasonSession.PinchSteps + 1, WorkInputKind.Axis, 1));
        inputs.Add(new WorkInput(20 * SeasonSession.PinchSteps + 2, WorkInputKind.Axis, -1));
        s.Advance(inputs, 20 * SeasonSession.PinchSteps + 5);
        Assert.Contains("salty", s.Taste);
        sim.Tick();
        Assert.True(sim.World.Get<StewPot>(pot).Seasoning > 1f);
    }

    [Fact]
    public void ThePotAndAStewInHandSurviveASave()
    {
        var (sim, me, pot) = Kitchen();
        Fill(sim, me, pot, ("Cabbage", 6), ("Berries", 4));
        sim.ApplyNow(new PotCommand(me, pot, PotStep.Tend));
        var session = (StewSession)sim.World.Get<WorkSession>(me);
        session.Advance([new WorkInput(0, WorkInputKind.Axis, 1), new WorkInput(10, WorkInputKind.Hold, 1)], 200);

        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        var a = sim.World.Get<StewPot>(pot);
        var b = loaded.World.Get<StewPot>(pot);
        Assert.Equal((a.Nutrition, a.SeasonTarget, a.In.Count), (b.Nutrition, b.SeasonTarget, b.In.Count));
        var copy = (StewSession)loaded.World.Get<WorkSession>(me);
        Assert.Equal((session.Heat, session.Flame, session.Doneness, session.StepsDone), (copy.Heat, copy.Flame, copy.Doneness, copy.StepsDone));
    }

    [Fact]
    public void TheWorkKeyOffersWhatThePotWants()
    {
        var (sim, me, pot) = Kitchen();
        var at = sim.World.Get<Thing>(pot).Cell;
        Assert.NotEqual(AvatarActionKind.Cook, AvatarSystem.ActionAt(sim, me, at).Kind); // empty, nothing in hand
        sim.World.Get<Carrying>(me).Item = sim.CreateItem(sim.Thing("Cabbage"), 3);
        Assert.Equal("Put the cabbage in the pot", AvatarSystem.ActionAt(sim, me, at).Label);
        sim.ApplyNow(new PotCommand(me, pot, PotStep.Next));
        Assert.Equal("Tend the stew", AvatarSystem.ActionAt(sim, me, at).Label);
        sim.World.Get<StewPot>(pot).Doneness = 1f;
        Assert.Equal("Ladle a bowl of stew", AvatarSystem.ActionAt(sim, me, at).Label);
    }

    // ---------------- In a round ----------------

    private static (Simulation Sim, List<Entity> Party) Tavern(string playerTrade)
    {
        var sim = VillageRound.NewSimulation(LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default, []));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade(playerTrade)], []));
        sim.ApplyNow(new StartRoundCommand());
        var party = sim.Parties.Send(sim, sim.Defs.Party("GildedSpurs"));
        return (sim, party);
    }

    [Fact]
    public void TheInnHasAKitchenWithAStockedLarderAndAPotByItsFire()
    {
        var (sim, _) = Tavern("Healer");
        var inn = ShopSystem.BuildingOf(sim, sim.Defs.Building("Inn"));
        var pot = Cooking.PotIn(sim, inn);
        Assert.False(pot.IsNull);
        Assert.True(Cooking.Lit(sim, pot), "the kitchen fire is lit");
        var larder = Buildings.Parts(sim, inn).Single(e => sim.World.Get<Thing>(e).Def.Name == "Larder");
        Assert.Contains(sim.StoredIn(larder), e => Cooking.IsIngredient(sim.World.Get<Thing>(e).Def));
    }

    [Fact]
    public void AnAICookCooksAPotAndServesItByTheBowl()
    {
        var (sim, party) = Tavern("Healer");
        var keeper = sim.Creatures().Single(e => sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == "Cook");
        var pot = Cooking.PotIn(sim, ShopSystem.BuildingOf(sim, sim.Defs.Building("Inn")));
        for (var i = 0; i < 24 && !Cooking.PotOf(sim, pot).Cooked; i++)
            sim.TickMany(SimClock.TicksPerHour / 4);
        Assert.True(Cooking.PotOf(sim, pot).Cooked, $"cooked {Cooking.PotOf(sim, pot).Doneness:0.00}");

        // Nothing else on the books: whole, and not thirsty yet.
        foreach (var e in party)
        {
            sim.World.Get<Health>(e).Injuries.Clear();
            sim.World.Get<Adventurer>(e).NextDrink = sim.Clock.Tick + SimClock.TicksPerDay;
        }
        var diner = party[0];
        sim.World.Get<Needs>(diner).Values[sim.Defs.Need("Food").Index] = 0.5f;
        var bowls = Cooking.ServingsLeft(sim, pot);
        for (var i = 0; i < 12 && !sim.Shops.All.Any(e => e.Kind.Kind == ServiceKind.Meal && e.Poster == diner && e.Status == OrderStatus.Done); i++)
            sim.TickMany(SimClock.TicksPerHour / 4);

        var meal = sim.Shops.All.First(e => e.Kind.Kind == ServiceKind.Meal && e.Poster == diner);
        Assert.Equal(OrderStatus.Done, meal.Status);
        Assert.Equal(keeper, meal.Assignee);
        Assert.True(Cooking.ServingsLeft(sim, pot) < bowls, "it came out of the pot");
        Assert.Contains(sim.Speech.Kept, u => u.Speaker == keeper && u.Text.EndsWith("'s up!")); // through the hatch
    }

    [Fact]
    public void ABotCookCooksTheStewAndServesItHot()
    {
        var (sim, party) = Tavern("Cook");
        var bot = new BotPlayer(() => sim, Ana, c => sim.ApplyNow(c), 7);
        var diner = party[0];
        sim.World.Get<Needs>(diner).Values[sim.Defs.Need("Food").Index] = 0.5f;
        var pot = Cooking.PotIn(sim, ShopSystem.BuildingOf(sim, sim.Defs.Building("Inn")));
        bool Served() => sim.Shops.All.Any(e => e.Kind.Kind == ServiceKind.Meal && e.Poster == diner && e.Status == OrderStatus.Done);
        var putUp = false;
        for (var t = 0; t < SimClock.TicksPerHour * 6 && !Served(); t++)
        {
            sim.Tick();
            bot.Tick();
            putUp |= bot.Doing == "putting a dish up on the hatch";
        }
        Assert.True(Served(), $"bot {bot.Doing}, pot {Cooking.PotOf(sim, pot).Doneness:0.00}\n" + string.Join("\n", sim.Messages.Messages.TakeLast(10).Select(m => m.Text)));
        Assert.Equal(Players.CharacterOf(sim, Ana), sim.Shops.All.First(e => e.Kind.Kind == ServiceKind.Meal && e.Poster == diner).Assignee);
        Assert.True(Cooking.PotOf(sim, pot).Seasoning > 0f, "it seasoned the pot");
        Assert.True(putUp, "it put the bowl up on the hatch");
    }

    // ---------------- The serving hatch ----------------

    private static Entity Inn(Simulation sim) => ShopSystem.BuildingOf(sim, sim.Defs.Building("Inn"));

    private static int3 InnCell(Simulation sim, int col, int row)
    {
        var b = sim.World.Get<PresetBuilding>(Inn(sim));
        return Buildings.CellOf(sim.Map, b.Def, b.Anchor, b.Facing, col, row);
    }

    private static Entity Stew(Simulation sim, Entity maker)
    {
        var stew = sim.CreateItem(sim.Thing("Stew"), 1);
        sim.World.Add(stew, new Crafted { Quality = Quality.Fine, Maker = maker });
        return stew;
    }

    [Fact]
    public void MealsAreOrderedAtTheHatchAndTakenOffItWhenTheCookPutsThemUp()
    {
        var (sim, party) = Tavern("Cook");
        var me = Players.CharacterOf(sim, Ana);
        var inn = Inn(sim);
        var hatch = Hatch.Of(sim, inn);
        Assert.False(hatch.IsNull);
        foreach (var e in party)
        {
            sim.World.Get<Health>(e).Injuries.Clear();
            sim.World.Get<Adventurer>(e).NextDrink = sim.Clock.Tick + SimClock.TicksPerDay;
        }
        var diner = party[0];
        sim.World.Get<Needs>(diner).Values[sim.Defs.Need("Food").Index] = 0.5f;
        sim.TickMany(SimClock.TicksPerHour / 6);
        var order = sim.Shops.All.Single(e => e.IsOpen && e.Poster == diner && e.Kind.Kind == ServiceKind.Meal);
        Assert.True(Hatch.Takes(sim, order));
        Assert.Contains(diner, sim.Shops.LineAt(sim, inn, hatch: true)); // the hatch's line, not the bar's
        Assert.DoesNotContain(diner, sim.Shops.LineAt(sim, inn));
        var front = ShopSystem.LineCells(sim, inn, hatch: true)[0];
        for (var i = 0; i < 40 && sim.World.Get<Position>(diner).Cell != front; i++)
            sim.TickMany(60);
        Assert.Equal(front, sim.World.Get<Position>(diner).Cell);

        // The cook, in the kitchen, puts a bowl up: the work key at the hatch with it in hand.
        var kitchenSide = InnCell(sim, 7, 3);
        sim.World.Get<Position>(me) = Position.At(kitchenSide);
        sim.World.Get<Carrying>(me).Item = Stew(sim, me);
        var purse = Coin.Count(sim, me);
        var action = AvatarSystem.ActionAt(sim, me, sim.World.Get<Thing>(hatch).Cell);
        Assert.Equal(AvatarActionKind.Store, action.Kind);
        Assert.Contains("up on the serving hatch", action.Label);
        AvatarSystem.Perform(sim, me, action);
        Assert.Single(sim.StoredIn(hatch));

        // The diner at the front takes it, pays the cook, and goes to sit with it.
        for (var i = 0; i < 10 && order.IsOpen; i++)
            sim.TickMany(60);
        Assert.Equal(OrderStatus.Done, order.Status);
        Assert.Equal(purse + order.Price, Coin.Count(sim, me));
        Assert.Empty(sim.StoredIn(hatch));
        Assert.True(sim.World.TryGet<Hospitality.Savouring>(diner, out var savouring) && savouring.Meal);
        Assert.Equal(Quality.Fine, sim.World.Get<Crafted>(savouring.Item).Quality);
    }

    [Fact]
    public void WhatsUpOnTheHatchIsntAnyoneElsesToTake()
    {
        var (sim, _) = Tavern("Cook");
        var hatch = Hatch.Of(sim, Inn(sim));
        sim.Store(hatch, Stew(sim, Entity.Null));
        var stew = sim.StoredIn(hatch).Single();
        Assert.False(sim.IsAvailable(stew));
        Assert.DoesNotContain(stew, sim.AvailableItems());
    }

    [Fact]
    public void TheKitchenIsARoomOfItsOwnButHeardThroughTheHatch()
    {
        var (sim, _) = Tavern("Cook");
        int3 kitchen = InnCell(sim, 7, 3), counter = InnCell(sim, 7, 5);
        Assert.NotEqual(sim.Rooms.RoomAt(kitchen), sim.Rooms.RoomAt(counter));
        Assert.Equal(Speech.Clarity.Clear, Speech.SpeechSystem.Hear(sim, counter, kitchen, Speech.Volume.Say));
        // Across the common room, away from the hatch, it's only heard through the wall.
        Assert.Equal(Speech.Clarity.Muffled, Speech.SpeechSystem.Hear(sim, InnCell(sim, 1, 6), InnCell(sim, 1, 2), Speech.Volume.Say));
    }
}
