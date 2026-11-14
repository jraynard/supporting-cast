using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>Customers line up at a shop's counter and are served from the front (docs/DESIGN.md, "Shops and the line").</summary>
public class ShopLineTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    /// <summary>A round with Ana in <paramref name="trade"/> (so nobody else serves her shop), and a party in town.</summary>
    private static (Simulation Sim, Entity Me, List<Entity> Party) Town(string trade)
    {
        var sim = VillageRound.NewSimulation(TestWorld.LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default, []));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade(trade)], []));
        sim.ApplyNow(new StartRoundCommand());
        TestWorld.AleOnly(sim); // drink orders here are for ale
        var party = sim.Parties.Send(sim, sim.Defs.Party("GildedSpurs"));
        foreach (var e in party)
        {
            Hospitality.DirectionsSystem.KnowsTheTown(sim, e); // finding the way is InnTalkTests'
            sim.World.Get<Needs>(e).Values[sim.Defs.Need("Food").Index] = 1f; // fed on the road: the hatch's line is InnKitchenTests'
        }
        Assert.NotEmpty(party);
        return (sim, Players.CharacterOf(sim, Ana), party);
    }

    private static Entity Shop(Simulation sim, string building) => ShopSystem.BuildingOf(sim, sim.Defs.Building(building));

    private static int3 At(Simulation sim, Entity who) => sim.World.Get<Position>(who).Cell;

    private static string Log(Simulation sim) => string.Join("\n", sim.Speech.Kept.TakeLast(10).Select(u => $"{u.SpeakerName}: {u.Text}"));

    [Fact]
    public void EveryShopInTheVillageHasRoomForALine()
    {
        var (sim, _, _) = Town("Labourer");
        foreach (var name in sim.Defs.AllServices.Select(d => d.At).Where(a => a.Length > 0).Distinct())
        {
            var shop = Shop(sim, name);
            Assert.False(shop.IsNull, name);
            var cells = ShopSystem.LineCells(sim, shop);
            Assert.True(cells.Count >= 3, $"{name}: {cells.Count} line cells");
            Assert.Equal(cells.Count, cells.Distinct().Count());
        }
    }

    [Fact]
    public void ThirstyAdventurersLineUpAtTheBarAndOnlyTheFrontOneAsks()
    {
        var (sim, _, party) = Town("Innkeeper");
        var inn = Shop(sim, "Inn");
        foreach (var a in party)
            sim.World.Get<Adventurer>(a).NextDrink = sim.Clock.Tick;
        sim.TickMany(SimClock.TicksPerHour);

        var line = sim.Shops.LineAt(sim, inn);
        var cells = ShopSystem.LineCells(sim, inn);
        Assert.True(line.Count >= 2, $"line {line.Count}\n{Log(sim)}");
        for (var i = 0; i < Math.Min(line.Count, cells.Count); i++)
            Assert.True(At(sim, line[i]) == cells[i], $"place {i}: at {At(sim, line[i])}, not {cells[i]}");

        var orders = sim.Shops.All.Where(o => o.IsOpen && o.Kind.Kind == ServiceKind.Drink).ToList();
        Assert.True(orders.Single(o => o.Poster == line[0]).LastAsked > 0, "the front one says what they want");
        Assert.All(orders.Where(o => o.Poster != line[0]), o => Assert.True(o.LastAsked < o.PostedTick, $"{o.PosterName} spoke out of turn"));
    }

    [Fact]
    public void TheLineMovesUpWhenTheFrontIsServed()
    {
        var (sim, me, party) = Town("Innkeeper");
        var inn = Shop(sim, "Inn");
        foreach (var a in party)
            sim.World.Get<Adventurer>(a).NextDrink = sim.Clock.Tick;
        sim.TickMany(SimClock.TicksPerHour);
        var line = sim.Shops.LineAt(sim, inn);
        Assert.True(line.Count >= 2);
        var (first, second) = (line[0], line[1]);

        foreach (var o in sim.Shops.All.Where(o => o.IsOpen && o.Poster == first && o.Where == inn).ToList())
            sim.Shops.Complete(sim, o, me);
        Assert.Equal(second, sim.Shops.LineAt(sim, inn)[0]);
        sim.TickMany(SimClock.TicksPerHour / 4);
        Assert.Equal(ShopSystem.LineCells(sim, inn)[0], At(sim, second));
    }

    [Fact]
    public void KeepersServeFromTheFrontOfTheLine()
    {
        var (sim, _, party) = Town("Innkeeper");
        var inn = Shop(sim, "Inn");
        foreach (var a in party)
            sim.World.Get<Adventurer>(a).NextDrink = sim.Clock.Tick;
        sim.TickMany(SimClock.TicksPerHour);
        var line = sim.Shops.LineAt(sim, inn);
        var waiting = sim.Shops.FrontFirst(sim).Where(o => o.Where == inn).Select(o => o.Poster).Distinct().ToList();
        Assert.Equal(line, waiting.Take(line.Count));
    }

    [Fact]
    public void AnAdventurerStepsOutOfTheLineOnceTheirBladeIsHandedOver()
    {
        var (sim, me, party) = Town("Blacksmith");
        var smithy = Shop(sim, "Smithy");
        var owner = party.First(e => sim.World.TryGet<Equipment>(e, out var eq) && WeaponWear.Edged(sim.World.Get<Thing>(eq.Weapon).Def));
        var sword = sim.World.Get<Equipment>(owner).Weapon;
        sim.World.Get<Thing>(sword).Hp = sim.World.Get<Thing>(sword).MaxHp * 0.5f;
        WeaponWear.SetDullness(sim, sword, 0.7f);
        // They walk over from wherever they are (the edge of the map, the inn) and take their place.
        bool InPlace() => sim.Shops.PlaceOf(sim, owner) is { Cell: { } c } && At(sim, owner) == c;
        for (var t = 0; t < SimClock.HoursToTicks(4) && !InPlace(); t += SimClock.TicksPerHour / 12)
            sim.TickMany(SimClock.TicksPerHour / 12);

        var order = sim.Shops.All.Single(o => o.IsOpen && o.Poster == owner && o.Kind.Kind == ServiceKind.Repair);
        Assert.Equal(smithy, order.Where);
        Assert.Contains(owner, sim.Shops.LineAt(sim, smithy));
        Assert.True(InPlace(), $"{sim.World.Get<Creature>(owner).Name} at {At(sim, owner)}: {sim.World.Get<Mind>(owner).Job?.Report}, "
            + $"line {string.Join(" ", ShopSystem.LineCells(sim, smithy))}, failed: {sim.Ai.LastFailure.GetValueOrDefault(owner)}");

        sim.ApplyNow(new TakeOrderCommand(me, order.Id));
        var cell = sim.SpiralFrom(At(sim, owner), 3).First(c => c != At(sim, owner) && sim.Map.Passable(c) && c.Z == At(sim, owner).Z);
        sim.World.Get<Position>(me).Cell = cell;
        sim.World.Get<Position>(me).Pos = Position.CenterOf(cell);
        Assert.True(sim.Shops.TryHandOver(sim, order));
        Assert.DoesNotContain(owner, sim.Shops.LineAt(sim, smithy));
        Assert.True(order.IsOpen, "still waiting for it back");
    }

    [Fact]
    public void ALineSurvivesASave()
    {
        var (sim, _, party) = Town("Innkeeper");
        var inn = Shop(sim, "Inn");
        foreach (var a in party)
            sim.World.Get<Adventurer>(a).NextDrink = sim.Clock.Tick;
        sim.TickMany(SimClock.TicksPerHour);
        Assert.NotEmpty(sim.Shops.LineAt(sim, inn));

        var loaded = SaveLoadTests.RoundTrip(sim);
        Assert.Equal(sim.Shops.LineAt(sim, inn), loaded.Shops.LineAt(loaded, inn));
        SaveLoadTests.AssertSameFuture(sim, loaded, 2);
    }
}
