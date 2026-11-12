using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>The tavern: pouring at the cask, and serving drink and meal orders (Stage 4 of M2).</summary>
public class InnkeeperTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    private static (Simulation Sim, Entity Me, Entity Cask) AtTheCask()
    {
        var sim = Create(40, 40);
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Inna", C(10, 10), ageDays: 25 * 365);
        sim.Round.OpenLobby();
        sim.Round.Sit(Ana, "Inna", null, null, null);
        sim.Round.Start(sim);
        sim.GiveStartingPack(me);
        var cask = sim.SpawnStructure(sim.Thing("AleCask"), C(11, 10));
        sim.RebuildDerivedIfNeeded();
        sim.World.Get<Carrying>(me).Item = sim.CreateItem(sim.Thing("Tankard"), 3);
        return (sim, me, cask);
    }

    /// <summary>Pour as a player would: tilted or not, and stopping early enough for the head to settle (or not stopping).</summary>
    private static void Pour(Simulation sim, Entity me, bool tilted, bool stop = true, int maxTicks = 60 * 60)
    {
        var predictor = new WorkPredictor();
        var stopped = false;
        for (var t = 0; t < maxTicks && sim.World.Has<WorkSession>(me); t++)
        {
            sim.Tick();
            var hold = false;
            if (predictor.Session is PourSession p)
            {
                // Some of the head settles into drink and the rest goes: aim for where it'll end up.
                stopped |= stop && p.Level + p.Foam * 0.73f >= p.Line;
                hold = !stopped;
            }
            predictor.Update(sim, me, 1, false, hold, tilted ? 1 : 0, c => sim.ApplyNow(c));
        }
        sim.Tick();
    }

    [Fact]
    public void ACarefulPourMakesAGoodAleAndDrawsOneServing()
    {
        var (sim, me, cask) = AtTheCask();
        var full = PourSession.ServingsLeft(sim, cask);
        PourSession.TryStart(sim, me, cask);
        Assert.IsType<PourSession>(sim.World.Get<WorkSession>(me));
        Pour(sim, me, tilted: true);

        var ale = sim.World.Get<Carrying>(me).Item;
        Assert.False(ale.IsNull, "the ale is in hand");
        Assert.True(sim.World.Get<Thing>(ale).Def.Drink);
        Assert.True(sim.World.Get<Crafted>(ale).Quality >= Quality.Fine, $"{sim.World.Get<Crafted>(ale).Quality}");
        Assert.Equal(full - 1, PourSession.ServingsLeft(sim, cask));
    }

    [Fact]
    public void PouringStraightAndNotStoppingSpillsIt()
    {
        var (sim, me, cask) = AtTheCask();
        var full = PourSession.ServingsLeft(sim, cask);
        PourSession.TryStart(sim, me, cask);
        Pour(sim, me, tilted: false, stop: false);

        var held = sim.World.Get<Carrying>(me).Item;
        Assert.Equal("Tankard", sim.World.Get<Thing>(held).Def.Name); // nothing to show for it but the tankard, still clean
        Assert.Equal(3, sim.World.Get<Stack>(held).Count);
        Assert.Equal(full - 1, PourSession.ServingsLeft(sim, cask));
    }

    [Fact]
    public void APourInProgressSurvivesASave()
    {
        var (sim, me, cask) = AtTheCask();
        PourSession.TryStart(sim, me, cask);
        var session = (PourSession)sim.World.Get<WorkSession>(me);
        session.Advance([new WorkInput(0, WorkInputKind.Hold, 1), new WorkInput(0, WorkInputKind.Axis, 1)], 40);

        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        var copy = (PourSession)loaded.World.Get<WorkSession>(me);
        Assert.Equal((session.Level, session.Foam, session.Tilt, session.Line, session.StepsDone), (copy.Level, copy.Foam, copy.Tilt, copy.Line, copy.StepsDone));
    }

    // ---------------- Orders ----------------

    private static (Simulation Sim, List<Entity> Party) Tavern(string playerTrade)
    {
        var sim = VillageRound.NewSimulation(LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default, []));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade(playerTrade)], []));
        sim.ApplyNow(new StartRoundCommand());
        TestWorld.AleOnly(sim); // drink orders here are for ale
        var party = sim.Parties.Send(sim, sim.Defs.Party("GildedSpurs"));
        Assert.NotEmpty(party);
        Tinkering.Wagons.Leave(sim, sim.World.Get<Adventurer>(party[0]).Party); // the inn's the thing here
        return (sim, party);
    }

    private static void Thirsty(Simulation sim, Entity who) => sim.World.Get<Adventurer>(who).NextDrink = sim.Clock.Tick;

    [Fact]
    public void ServedTheyTakeASeatAndSitOverItTalking()
    {
        var (sim, party) = Tavern("Innkeeper");
        var me = Players.CharacterOf(sim, Ana);
        foreach (var e in party)
        {
            sim.World.Get<Health>(e).Injuries.Clear(); // nothing to take them off to the shrine
            Thirsty(sim, e);
        }
        sim.TickMany(SimClock.TicksPerHour / 6);
        var served = new List<Entity>();
        foreach (var e in party)
        {
            if (!sim.Shops.All.Any(o => o.IsOpen && o.Poster == e && o.Kind.Kind is ServiceKind.Drink or ServiceKind.MixedDrink))
                continue;
            var at = sim.World.Get<Position>(e).Cell;
            var cell = sim.SpiralFrom(at, 3).First(c => c != at && sim.Map.Passable(c) && c.Z == at.Z);
            sim.World.Get<Position>(me).Cell = cell;
            sim.World.Get<Position>(me).Pos = Position.CenterOf(cell);
            var ale = sim.CreateItem(sim.Thing("Ale"), 1);
            sim.World.Add(ale, new Crafted { Quality = Quality.Plain, Maker = me });
            Inventory.Stow(sim, me, ale);
            sim.ApplyNow(new GiveCommand(me, e, ale, 1));
            Assert.True(sim.World.Has<Hospitality.Savouring>(e));
            served.Add(e);
        }
        Assert.True(served.Count >= 2, $"{served.Count} served");
        var servedAt = sim.Clock.Tick;

        // A while later they're sat on the stools with their drinks.
        sim.TickMany(SimClock.HoursToTicks(0.25f));
        bool Seated(Entity e) => sim.World.Get<Mind>(e).Job is Hospitality.SavourJob { Seated: true } j
            && sim.World.Get<Thing>(j.Seat).Cell == sim.World.Get<Position>(e).Cell;
        Assert.All(served, e => Assert.True(Seated(e), $"{sim.World.Get<Creature>(e).Name} is {sim.World.Get<Mind>(e).Job?.Report} savouring {sim.World.Has<Hospitality.Savouring>(e)} at {sim.World.Get<Position>(e).Cell}"));
        Assert.Equal(served.Count, served.Select(e => sim.World.Get<Position>(e).Cell).Distinct().Count()); // a stool each

        // It survives a save, sat with it.
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.Equal(sim.World.Get<Hospitality.Savouring>(served[0]).Until, loaded.World.Get<Hospitality.Savouring>(served[0]).Until);

        // Over their drinks, someone talked; then they're done, and leave their tankards.
        foreach (var e in served)
            TillFinished(sim, e);
        Assert.Contains(sim.Speech.Kept, u => u.Tick > servedAt && served.Contains(u.Speaker));
        Assert.All(served, e => Assert.True(sim.World.Get<Carrying>(e).Item.IsNull || sim.World.Get<Thing>(sim.World.Get<Carrying>(e).Item).Def.Name != "Ale"));
    }

    [Fact]
    public void AnAleHandedToWhoeverOrderedOneIsPaidForAndTasted()
    {
        var (sim, party) = Tavern("Innkeeper");
        var me = Players.CharacterOf(sim, Ana);
        var drinker = party[0];
        Thirsty(sim, drinker);
        sim.TickMany(SimClock.TicksPerHour / 6);
        var order = sim.Shops.All.Single(e => e.IsOpen && e.Poster == drinker && e.Kind.Kind == ServiceKind.Drink);
        Assert.Contains(Perception.For(sim, me).Orders(), e => e.Id == order.Id); // word reaches the innkeeper

        var at = sim.World.Get<Position>(drinker).Cell;
        var cell = sim.SpiralFrom(at, 3).First(c => c != at && sim.Map.Passable(c) && c.Z == at.Z);
        sim.World.Get<Position>(me).Cell = cell;
        sim.World.Get<Position>(me).Pos = Position.CenterOf(cell);
        var ale = sim.CreateItem(sim.Thing("Ale"), 1);
        sim.World.Add(ale, new Crafted { Quality = Quality.Crude, Maker = me });
        Inventory.Stow(sim, me, ale);
        var purse = Coin.Count(sim, me);
        var mood = sim.World.Get<Adventurer>(drinker).Satisfaction;

        sim.ApplyNow(new GiveCommand(me, drinker, ale, 1));

        Assert.Equal(OrderStatus.Done, order.Status);
        Assert.Equal(purse + order.Price, Coin.Count(sim, me));
        Assert.Equal(ale, sim.World.Get<Carrying>(drinker).Item); // in hand, to sit with
        TillFinished(sim, drinker);
        Assert.False(sim.World.IsAlive(ale), "they drank it");
        // Quick service pleases by 1.5 (and a bargain a little more); a crude pour takes 0.75 off it.
        Assert.True(sim.World.Get<Adventurer>(drinker).Satisfaction - mood < 1.5f, "a crude pour pleases less");
    }

    [Fact]
    public void AnAIInnkeeperPoursForOrders()
    {
        var (sim, party) = Tavern("Healer");
        var keeper = sim.Creatures().Single(e => sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == "Innkeeper");
        foreach (var e in party)
            Thirsty(sim, e);

        for (var i = 0; i < 12 && !sim.Shops.All.Any(e => e.Kind.Kind == ServiceKind.Drink && e.Status == OrderStatus.Done); i++)
            sim.TickMany(SimClock.TicksPerHour / 4);

        Assert.Contains(sim.Shops.All, e => e.Kind.Kind == ServiceKind.Drink && e.Status == OrderStatus.Done && e.Assignee == keeper);
    }

    [Fact]
    public void AnAICookServesAMealFromTheStores()
    {
        var (sim, party) = Tavern("Healer");
        var keeper = sim.Creatures().Single(e => sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == "Cook");
        var tavern = PartyHaunt(sim);
        sim.PlaceItemNear(sim.CreateItem(sim.Thing("Stew"), 10), tavern);
        var diner = party[0];
        sim.World.Get<Needs>(diner).Values[sim.Defs.Need("Food").Index] = 0.5f;

        for (var i = 0; i < 12 && !sim.Shops.All.Any(e => e.Kind.Kind == ServiceKind.Meal && e.Status == OrderStatus.Done); i++)
            sim.TickMany(SimClock.TicksPerHour / 4);

        var meal = sim.Shops.All.First(e => e.Kind.Kind == ServiceKind.Meal && e.Poster == diner);
        Assert.Equal(OrderStatus.Done, meal.Status);
        Assert.Equal(keeper, meal.Assignee);
    }

    /// <summary>Tick until evening, the party staying on past tomorrow.</summary>
    private static void StayTillEvening(Simulation sim, List<Entity> party)
    {
        foreach (var a in party)
            sim.World.Get<Adventurer>(a).LeaveTick = sim.Clock.Tick + SimClock.TicksPerDay * 2;
        while (sim.Clock.HourOfDay < ShopSystem.LodgingFromHour)
            sim.TickMany(SimClock.TicksPerHour / 4);
    }

    [Fact]
    public void AnAIInnkeeperLetsARoomUpstairsForTheNight()
    {
        var (sim, party) = Tavern("Healer");
        var keeper = sim.Creatures().Single(e => sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == "Innkeeper");
        // Nothing else on the books to keep the keeper busy: whole, and not thirsty.
        foreach (var a in party)
        {
            sim.World.Get<Health>(a).Injuries.Clear();
            sim.World.Get<Adventurer>(a).NextDrink = sim.Clock.Tick + SimClock.TicksPerDay * 3;
            sim.World.Get<Adventurer>(a).Arrived = sim.Clock.Tick + SimClock.TicksPerDay; // and not after work today: they're staying the night
        }
        StayTillEvening(sim, party);
        var lodger = party[0];
        Inventory.Stow(sim, lodger, sim.CreateItem(sim.Thing(Coin.Silver), 20)); // whatever else they've spent on
        // The bed they're given, as soon as they have it (a party sent on a quest the same evening checks out again).
        var bed = Entity.Null;
        for (var t = 0; t < SimClock.TicksPerHour * 6 && bed.IsNull; t += 30)
        {
            sim.TickMany(30);
            if (sim.World.TryGet<BedOwner>(lodger, out var own))
                bed = own.Bed;
        }

        var room = sim.Shops.All.First(e => e.Kind.Kind == ServiceKind.Lodging && e.Poster == lodger);
        Assert.Equal(OrderStatus.Done, room.Status);
        Assert.Equal(keeper, room.Assignee);
        Assert.False(bed.IsNull);
        var inn = Buildings.Of(sim, bed);
        Assert.Equal("Inn", sim.World.Get<PresetBuilding>(inn).Def.Name);
        Assert.Equal(sim.World.Get<PresetBuilding>(inn).Anchor.Z + 1, sim.World.Get<Thing>(bed).Cell.Z); // upstairs
    }

    [Fact]
    public void NobodySleepsInALetBedUnlessItsTheirs()
    {
        var (sim, _) = Tavern("Healer");
        var keeper = sim.Creatures().Single(e => sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == "Innkeeper");
        var bed = Ai.AiQueries.FindFreeBed(sim, keeper);
        Assert.False(!bed.IsNull && ShopSystem.IsLet(sim, bed), "the innkeeper took a guest's bed");
    }

    private static int3 PartyHaunt(Simulation sim) =>
        Adventurers.PartySystem.HauntOf(sim, sim.Defs.Party("GildedSpurs"))!.Anchor;
}
