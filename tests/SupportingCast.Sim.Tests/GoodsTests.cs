using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Round;
using SupportingCast.Sim.Scenarios;
using SupportingCast.Sim.Shops;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Tests;

/// <summary>Gear put up for sale in a shop, and the adventurers who'd be better off with it buying it.</summary>
public class GoodsTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    /// <summary>A round with Ana in <paramref name="trade"/>, a party in town, and the smithy.</summary>
    private static (Simulation Sim, Entity Me, Entity Smithy, List<Entity> Party) Town(string trade)
    {
        var sim = VillageRound.NewSimulation(TestWorld.LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default, []));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade(trade)], []));
        sim.ApplyNow(new StartRoundCommand());
        var party = sim.Parties.Send(sim, sim.Defs.Party("GildedSpurs"));
        Assert.NotEmpty(party);
        return (sim, Players.CharacterOf(sim, Ana), ShopSystem.BuildingOf(sim, sim.Defs.Building("Smithy")), party);
    }

    /// <summary>A piece of gear lying on the smithy's floor.</summary>
    private static Entity InTheSmithy(Simulation sim, Entity smithy, string def)
    {
        var cell = ShopSystem.LineCells(sim, smithy)[0];
        Assert.Equal(smithy, Buildings.At(sim, cell));
        var item = sim.PlaceItemNear(sim.CreateItem(sim.Defs.Thing(def), 1), cell);
        Assert.Equal(smithy, Buildings.At(sim, sim.World.Get<Thing>(item).Cell));
        return item;
    }

    /// <summary>The one with a club: a bronze mace is a step up for them, and for nobody else in the party.</summary>
    private static Entity ClubBearer(Simulation sim, List<Entity> party) =>
        party.Single(e => sim.World.TryGet<Equipment>(e, out var eq) && sim.World.Get<Thing>(eq.Weapon).Def.Name == "Club");

    private static Order? OrderFor(Simulation sim, Entity item) => sim.Shops.All.FirstOrDefault(o => o.Subject == item && o.Kind.Kind == ServiceKind.Goods);

    private static void TickUntil(Simulation sim, Func<bool> done, float hours)
    {
        for (var t = 0; t < SimClock.HoursToTicks(hours) && !done(); t += SimClock.TicksPerHour / 12)
            sim.TickMany(SimClock.TicksPerHour / 12);
    }

    [Fact]
    public void OnlyTheKeeperPutsGearInTheirShopUpForSale()
    {
        var (sim, me, smithy, party) = Town("Blacksmith");
        var mace = InTheSmithy(sim, smithy, "BronzeMace");
        var wood = sim.PlaceItemNear(sim.CreateItem(sim.Defs.Thing("Wood"), 3), sim.World.Get<Thing>(mace).Cell);
        var smith = sim.Creatures().First(e => e != me && sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == "Healer");
        sim.World.Add(smith, new Avatar());

        sim.ApplyNow(new SetItemPriceCommand(smith, mace, 10, true)); // not their shop
        sim.ApplyNow(new SetItemPriceCommand(me, wood, 10, true)); // not gear
        sim.ApplyNow(new SetItemPriceCommand(me, party[0], 10, true)); // not an item
        Assert.False(sim.World.Has<ForSale>(mace));
        Assert.False(sim.World.Has<ForSale>(wood));

        sim.ApplyNow(new SetItemPriceCommand(me, mace, int.MaxValue, true));
        Assert.Equal(ShopPrices.MaxPrice, sim.World.Get<ForSale>(mace).Price);
        Assert.Equal(smithy, Goods.ShownIn(sim, mace));
        sim.ApplyNow(new SetItemPriceCommand(smith, mace, 0, false)); // not theirs to take off
        Assert.True(sim.World.Has<ForSale>(mace));
        sim.ApplyNow(new SetItemPriceCommand(me, mace, 0, false));
        Assert.False(sim.World.Has<ForSale>(mace));
    }

    [Fact]
    public void AnAdventurerBuysAnUpgradeOnShowAndKeepsWhatItReplaces()
    {
        var (sim, me, smithy, party) = Town("Blacksmith");
        var buyer = ClubBearer(sim, party);
        var club = sim.World.Get<Equipment>(buyer).Weapon;
        var mace = InTheSmithy(sim, smithy, "BronzeMace");
        sim.ApplyNow(new SetItemPriceCommand(me, mace, 10, true));

        TickUntil(sim, () => OrderFor(sim, mace) != null, 2);
        var order = OrderFor(sim, mace);
        Assert.NotNull(order);
        Assert.Equal(buyer, order.Poster);
        Assert.Equal(smithy, order.Where);
        Assert.Equal(10, order.Price);
        Assert.Contains(order.Id, Perception.For(sim, me).Orders().Select(o => o.Id)); // word reaches the smith

        // Ana takes it down and hands it over.
        sim.World.Get<Carrying>(me).Item = Inventory.PickUp(sim, me, mace, sim.World.Get<Carrying>(me).Item);
        var at = sim.World.Get<Position>(buyer).Cell;
        var cell = sim.SpiralFrom(at, 3).First(c => c != at && sim.Map.Passable(c) && c.Z == at.Z);
        sim.World.Get<Position>(me).Cell = cell;
        sim.World.Get<Position>(me).Pos = Position.CenterOf(cell);
        var purse = Coin.Count(sim, me);
        sim.ApplyNow(new GiveCommand(me, buyer, mace, 1));

        Assert.Equal(OrderStatus.Done, order.Status);
        Assert.Equal(mace, sim.World.Get<Equipment>(buyer).Weapon);
        Assert.True(Inventory.OnPerson(sim, buyer, club), "they keep their old club");
        Assert.False(sim.World.Has<ForSale>(mace));
        Assert.Equal(purse + 10, Coin.Count(sim, me));
    }

    [Fact]
    public void TooDearGearIsLeftOnTheShelf()
    {
        var (sim, me, smithy, party) = Town("Blacksmith");
        var buyer = ClubBearer(sim, party);
        var mace = InTheSmithy(sim, smithy, "BronzeMace");
        sim.ApplyNow(new SetItemPriceCommand(me, mace, 500, true));
        sim.TickMany(SimClock.HoursToTicks(2));
        Assert.Null(OrderFor(sim, mace));
        Assert.Contains(sim.Speech.Kept, u => u.Speaker == buyer && u.Text.Contains("500"));
    }

    [Fact]
    public void NobodyBuysWhatWouldntBeAStepUp()
    {
        var (sim, me, smithy, _) = Town("Blacksmith");
        var club = InTheSmithy(sim, smithy, "Club");
        sim.ApplyNow(new SetItemPriceCommand(me, club, 1, true));
        sim.TickMany(SimClock.HoursToTicks(2));
        Assert.Null(OrderFor(sim, club));
    }

    [Fact]
    public void AnAIKeeperSellsWhatsOnShow()
    {
        var (sim, _, smithy, party) = Town("Healer");
        var smith = sim.Creatures().Single(e => sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == "Blacksmith");
        Assert.False(sim.World.Has<Avatar>(smith));
        var buyer = ClubBearer(sim, party);
        var mace = InTheSmithy(sim, smithy, "BronzeMace");
        Assert.True(Goods.Set(sim, smith, mace, 8, true)); // what a player left on show when they went
        var purse = Coin.Count(sim, smith);

        TickUntil(sim, () => OrderFor(sim, mace) is { IsOpen: false }, 8);
        var order = OrderFor(sim, mace);
        Assert.True(order is { Status: OrderStatus.Done }, $"{order?.Status}, smith: {sim.Ai.LastFailure.GetValueOrDefault(smith)} / {sim.World.Get<Mind>(smith).Job?.Report}");
        Assert.Equal(mace, sim.World.Get<Equipment>(buyer).Weapon);
        Assert.True(Coin.Count(sim, smith) >= purse + 8);
    }

    [Fact]
    public void TakingAPieceOffSaleCallsOffItsOrder()
    {
        var (sim, me, smithy, _) = Town("Blacksmith");
        var mace = InTheSmithy(sim, smithy, "BronzeMace");
        sim.ApplyNow(new SetItemPriceCommand(me, mace, 10, true));
        TickUntil(sim, () => OrderFor(sim, mace) != null, 2);
        var order = OrderFor(sim, mace)!;
        sim.ApplyNow(new SetItemPriceCommand(me, mace, 0, false));
        sim.TickMany(SimClock.TicksPerHour / 6);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void GearForSaleSurvivesASave()
    {
        var (sim, me, smithy, _) = Town("Blacksmith");
        var mace = InTheSmithy(sim, smithy, "BronzeMace");
        sim.ApplyNow(new SetItemPriceCommand(me, mace, 10, true));
        TickUntil(sim, () => OrderFor(sim, mace) != null, 2);

        var loaded = SaveLoadTests.RoundTrip(sim);
        Assert.Equal(sim.World.Get<ForSale>(mace), loaded.World.Get<ForSale>(mace));
        Assert.Equal(smithy, Goods.ShownIn(loaded, mace));
        SaveLoadTests.AssertSameFuture(sim, loaded, 2);
    }
}
