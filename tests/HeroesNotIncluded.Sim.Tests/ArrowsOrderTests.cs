using HeroesNotIncluded.Sim.Carpentry;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// Archers ask for arrows aloud (<see cref="ServiceKind.Arrows"/>): one with no quiver, seeing none
/// on the woodshop's shelf, posts an order the carpenter answers by handing them one.
/// </summary>
public class ArrowsOrderTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    private static (Simulation Sim, Entity Bowman) TownWithABowman(string playerTrade)
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
            Hospitality.DirectionsSystem.KnowsTheTown(sim, e);
        var bowman = party.First(e => Fletching.Shoots(sim, e));
        Assert.Empty(Fletching.Carried(sim, bowman));
        return (sim, bowman);
    }

    private static Order? ArrowsOf(Simulation sim, Entity who) =>
        sim.Shops.All.FirstOrDefault(e => e.Poster == who && e.Kind.Kind == ServiceKind.Arrows);

    private static Order WaitForTheAsk(Simulation sim, Entity bowman)
    {
        for (var t = 0; t < 24 && ArrowsOf(sim, bowman) == null; t++)
            sim.TickMany(SimClock.TicksPerHour / 6);
        var order = ArrowsOf(sim, bowman);
        Assert.NotNull(order);
        return order;
    }

    private static void Beside(Simulation sim, Entity me, Entity who)
    {
        var at = sim.World.Get<Position>(who).Cell;
        var cell = sim.SpiralFrom(at, 3).First(c => c != at && sim.Map.Passable(c) && c.Z == at.Z);
        sim.World.Get<Position>(me).Cell = cell;
        sim.World.Get<Position>(me).Pos = Position.CenterOf(cell);
    }

    [Fact]
    public void AnArcherWithNoQuiverAsksTheWoodshopAndIsHandedOne()
    {
        var (sim, bowman) = TownWithABowman("Carpenter");
        var me = Players.CharacterOf(sim, Ana);
        var order = WaitForTheAsk(sim, bowman);
        Assert.Equal(TownRole.Woodshop, order.Kind.AtRole);
        Assert.Contains(Perception.For(sim, me).Orders(), e => e.Id == order.Id); // word reaches the carpenter

        // A quiver of the carpenter's own, handed over: theirs, and paid for.
        var quiver = Fletching.Make(sim, me, 0.8f, warped: false);
        Inventory.Stow(sim, me, quiver);
        Beside(sim, me, bowman);
        var purse = Coin.Count(sim, me);
        sim.ApplyNow(new GiveCommand(me, bowman, quiver, 1));
        Assert.Equal(OrderStatus.Done, order.Status);
        Assert.Equal(purse + order.Price, Coin.Count(sim, me));
        Assert.Contains(quiver, Fletching.Carried(sim, bowman));
        Assert.False(sim.World.Has<ForSale>(quiver));

        // With one, they don't ask again.
        sim.TickMany(SimClock.TicksPerHour * 2);
        Assert.Single(sim.Shops.All, e => e.Poster == bowman && e.Kind.Kind == ServiceKind.Arrows);
    }

    [Fact]
    public void WithAQuiverOnShowTheyBuyItRatherThanAsk()
    {
        var (sim, bowman) = TownWithABowman("Carpenter");
        var me = Players.CharacterOf(sim, Ana);
        var shelf = Goods.MyDisplay(sim, me);
        Assert.False(shelf.IsNull);
        var quiver = Fletching.Make(sim, me, 0.8f, warped: false);
        Assert.True(sim.Store(shelf, quiver).IsNull);
        Assert.True(Goods.Set(sim, me, quiver, 3, true));
        // Silver to spare past the quest's and the healer's: gear's a luxury (ShopSystem.KeptFromGear).
        Inventory.Stow(sim, bowman, sim.CreateItem(sim.Defs.Thing(Coin.Silver), 30));

        for (var t = 0; t < 24 && !sim.Shops.All.Any(e => e.Poster == bowman && e.Kind.Kind == ServiceKind.Goods); t++)
            sim.TickMany(SimClock.TicksPerHour / 6);
        Assert.True(sim.Shops.All.Any(e => e.Poster == bowman && e.Kind.Kind == ServiceKind.Goods && e.Subject == quiver),
            $"silver {Coin.Count(sim, bowman)}: " + string.Join(" | ", sim.Speech.Kept.Where(u => u.Speaker == bowman).Select(u => u.Text)) + " ORDERS " + string.Join(", ", sim.Shops.All.Where(e => e.Poster == bowman).Select(e => $"{e.Kind.Kind} {e.Status}")));
        Assert.Null(ArrowsOf(sim, bowman));
    }

    [Fact]
    public void AnAICarpenterFletchesForAnArchersAsk()
    {
        var (sim, bowman) = TownWithABowman("Empath");
        var carpenter = sim.Creatures().Single(e => sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == "Carpenter");
        var shop = ShopSystem.WorkplaceOf(sim, sim.Defs.Trade("Carpenter"));
        var chest = Carpenters.ChestOf(sim, shop);
        // A plank and feathers to hand.
        sim.Store(chest, sim.CreateItem(sim.Defs.Thing(Carpenters.PlanksDef), 2));
        sim.Store(chest, sim.CreateItem(sim.Defs.Thing(Carpenters.FeathersDef), 2));

        for (var h = 0; h < 10 && Fletching.Carried(sim, bowman).Count == 0; h++)
            sim.TickMany(SimClock.TicksPerHour);

        Assert.True(Fletching.Carried(sim, bowman).Count > 0, $"carpenter: {sim.Ai.LastFailure.GetValueOrDefault(carpenter)} / {sim.World.Get<Mind>(carpenter).Job?.Report}; "
            + string.Join(", ", sim.Shops.All.Where(e => e.Poster == bowman).Select(e => $"{e.Kind.Kind} {e.Status}")));
        // From the shelf or asked for, it came from the woodshop, and the ask (if any) is closed.
        Assert.DoesNotContain(sim.Shops.All, e => e.Poster == bowman && e.Kind.Kind == ServiceKind.Arrows && e.IsOpen);
        if (ArrowsOf(sim, bowman) is { } asked)
            Assert.True(asked.Status is OrderStatus.Done or OrderStatus.Cancelled);
    }
}
