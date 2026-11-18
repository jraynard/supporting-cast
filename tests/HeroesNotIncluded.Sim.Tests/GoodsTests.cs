using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>Gear put up for sale on a shop's wares shelf, and the adventurers who'd be better off with it buying it.</summary>
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
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade(trade)], []));
        sim.ApplyNow(new StartRoundCommand());
        var party = sim.Parties.Send(sim, sim.Defs.Party("GildedSpurs"));
        Assert.NotEmpty(party);
        return (sim, Players.CharacterOf(sim, Ana), ShopSystem.BuildingOf(sim, sim.Defs.Building("Smithy")), party);
    }

    /// <summary>The smithy's wares shelf.</summary>
    private static Entity ShelfIn(Simulation sim, Entity smithy) => Buildings.Parts(sim, smithy).Single(p => Goods.IsDisplay(sim, p));

    /// <summary>A piece of gear on the smithy's wares shelf (not yet priced).</summary>
    private static Entity InTheSmithy(Simulation sim, Entity smithy, string def)
    {
        var item = sim.CreateItem(sim.Defs.Thing(def), 1);
        Assert.True(sim.Store(ShelfIn(sim, smithy), item).IsNull);
        return item;
    }

    /// <summary>Stand someone where they can reach a thing.</summary>
    private static void Beside(Simulation sim, Entity who, Entity thing)
    {
        var at = sim.World.Get<Thing>(thing).Cell;
        var cell = sim.SpiralFrom(at, 3).First(c => c != at && sim.Map.Passable(c) && AvatarSystem.InReach(sim, c, thing));
        sim.World.Get<Position>(who).Cell = cell;
        sim.World.Get<Position>(who).Pos = Position.CenterOf(cell);
    }

    [Fact]
    public void GearOnTheFloorIsNotOnShowOnlyOnTheShelf()
    {
        var (sim, me, smithy, _) = Town("Blacksmith");
        var cell = ShopSystem.LineCells(sim, smithy)[0];
        var floor = sim.PlaceItemNear(sim.CreateItem(sim.Defs.Thing("BronzeMace"), 1), cell);
        Assert.Equal(smithy, Buildings.At(sim, sim.World.Get<Thing>(floor).Cell));
        sim.ApplyNow(new SetItemPriceCommand(me, floor, 10, true));
        Assert.False(sim.World.Has<ForSale>(floor), "lying on the floor, it can't be put up");
        Assert.True(Goods.ShownIn(sim, floor).IsNull);
    }

    [Fact]
    public void PuttingGearOnYourShelfPricesItAndOnlyYouTakeItDown()
    {
        var (sim, me, smithy, _) = Town("Blacksmith");
        var w = sim.World;
        var shelf = ShelfIn(sim, smithy);
        Assert.Equal(shelf, Goods.MyDisplay(sim, me));
        var mace = sim.CreateItem(sim.Defs.Thing("BronzeMace"), 1);
        Inventory.Stow(sim, me, mace);
        Beside(sim, me, shelf);
        var label = AvatarSystem.ActionAt(sim, me, w.Get<Thing>(shelf).Cell).Label;
        Assert.Contains("wares shelf", label);
        sim.ApplyNow(new StoreFromPackCommand(me, shelf, mace));
        Assert.True(w.TryGet<ForSale>(mace, out var sale) && sale.Seller == me && sale.Price == Goods.DefaultPrice(sim, mace));
        Assert.Equal(smithy, Goods.ShownIn(sim, mace));
        // Wood isn't sold here: the shelf won't take it.
        Assert.Equal(0, sim.ContainerRoom(shelf, sim.Defs.Thing("Wood"), null));

        // Someone else can't reach it down.
        var other = sim.Creatures().First(e => e != me && w.TryGet<Trade>(e, out var t) && t.Def.Name == "Empath");
        w.Add(other, new Avatar());
        Beside(sim, other, shelf);
        Assert.False(Goods.CanSellAt(sim, other, shelf));
        sim.ApplyNow(new TakeFromStorageCommand(other, mace));
        Assert.Equal(shelf, w.Get<Stored>(mace).In);

        // Its seller can: taken down, it's off sale.
        sim.ApplyNow(new TakeFromStorageCommand(me, mace));
        Assert.True(Inventory.OnPerson(sim, me, mace));
        Assert.False(w.Has<ForSale>(mace));
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
        var smith = sim.Creatures().First(e => e != me && sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == "Empath");
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
    public void AnAdventurerBuysAnUpgradeOffTheShelfThemselvesAndKeepsWhatItReplaces()
    {
        var (sim, me, smithy, party) = Town("Blacksmith");
        var buyer = ClubBearer(sim, party);
        var club = sim.World.Get<Equipment>(buyer).Weapon;
        var mace = InTheSmithy(sim, smithy, "BronzeMace");
        sim.ApplyNow(new SetItemPriceCommand(me, mace, 10, true));
        var purse = Coin.Count(sim, me);

        // Nobody serves them: they walk over, take it down and pay Ana for it.
        TickUntil(sim, () => OrderFor(sim, mace) is { IsOpen: false }, 6);
        var order = OrderFor(sim, mace);
        Assert.True(order is { Status: OrderStatus.Done }, $"{order?.Status}");
        Assert.Equal(buyer, order!.Poster);
        Assert.Equal(smithy, order.Where);
        Assert.Equal(10, order.Price);
        Assert.Equal(mace, sim.World.Get<Equipment>(buyer).Weapon);
        Assert.True(Inventory.OnPerson(sim, buyer, club), "they keep their old club");
        Assert.False(sim.World.Has<ForSale>(mace));
        Assert.Equal(purse + 10, Coin.Count(sim, me));
        // Ana hears of the sale, and sees it in the shop as theirs, by name.
        var known = Perception.For(sim, me).Orders().Single(o => o.Id == order.Id);
        Assert.Contains("bronze mace", known.KindLabel);
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
        var (sim, _, smithy, party) = Town("Empath");
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
    public void TakingAPieceOffSaleBeforeTheyReachItKeepsIt()
    {
        var (sim, me, smithy, party) = Town("Blacksmith");
        var buyer = ClubBearer(sim, party);
        var mace = InTheSmithy(sim, smithy, "BronzeMace");
        sim.ApplyNow(new SetItemPriceCommand(me, mace, 10, true));
        TickUntil(sim, () => sim.Shops.BrowsingFor(buyer) != null, 2);
        Assert.Equal(mace, sim.Shops.BrowsingFor(buyer)?.Item);
        sim.ApplyNow(new SetItemPriceCommand(me, mace, 0, false));
        sim.TickMany(SimClock.TicksPerHour);
        Assert.Null(sim.Shops.BrowsingFor(buyer));
        Assert.NotEqual(mace, sim.World.Get<Equipment>(buyer).Weapon);
        Assert.Equal(smithy, Buildings.Of(sim, sim.World.Get<Stored>(mace).In));
    }

    [Fact]
    public void GearForSaleSurvivesASave()
    {
        var (sim, me, smithy, party) = Town("Blacksmith");
        var buyer = ClubBearer(sim, party);
        var mace = InTheSmithy(sim, smithy, "BronzeMace");
        sim.ApplyNow(new SetItemPriceCommand(me, mace, 10, true));
        TickUntil(sim, () => sim.Shops.BrowsingFor(buyer) != null, 2);

        var loaded = SaveLoadTests.RoundTrip(sim);
        Assert.Equal(sim.World.Get<ForSale>(mace), loaded.World.Get<ForSale>(mace));
        Assert.Equal(smithy, Goods.ShownIn(loaded, mace));
        Assert.Equal(sim.Shops.BrowsingFor(buyer), loaded.Shops.BrowsingFor(buyer));
        SaveLoadTests.AssertSameFuture(sim, loaded, 2);
    }

    /// <summary>The first open ask for a piece by name, by anyone.</summary>
    private static Order? Asked(Simulation sim) => sim.Shops.All.FirstOrDefault(o => o.IsOpen && o.Kind.Kind == ServiceKind.Goods && o.Wants != null);

    [Fact]
    public void WithNothingOnShowTheyAskForAPieceByName()
    {
        var (sim, me, _, _) = Town("Blacksmith");
        TickUntil(sim, () => Asked(sim) != null, 12);
        var order = Asked(sim);
        Assert.NotNull(order);
        var label = Goods.WishLabel(order!.Wants!, order.WantsMending);
        Assert.Contains(label, order.Text);
        Assert.Equal(ShopSystem.BuildingOf(sim, sim.Defs.Building("Smithy")), order.Where); // Ana's is the only shop a player keeps
        Assert.False(ShopSystem.WaitsInLine(sim, order), "it has to be made: they don't stand in line for it");
        // Ana knows of it, by name, not as "gear".
        var known = Perception.For(sim, me).Orders().Single(o => o.Id == order.Id);
        Assert.Equal(Goods.WithArticle(label), known.KindLabel);
        Assert.Equal(order.Wants!.Sprite, known.Icon);

        // Made and handed over, it's done and paid for.
        var piece = sim.CreateItem(order.Wants!, 1);
        sim.World.Get<Carrying>(me).Item = Inventory.PickUp(sim, me, piece, sim.World.Get<Carrying>(me).Item);
        var at = sim.World.Get<Position>(order.Poster).Cell;
        var cell = sim.SpiralFrom(at, 3).First(c => c != at && sim.Map.Passable(c) && c.Z == at.Z);
        sim.World.Get<Position>(me).Cell = cell;
        sim.World.Get<Position>(me).Pos = Position.CenterOf(cell);
        Assert.Same(order, ShopSystem.AskedFor(sim, order.Poster, piece));
        var purse = Coin.Count(sim, me);
        sim.ApplyNow(new GiveCommand(me, order.Poster, piece, 1));
        Assert.Equal(OrderStatus.Done, order.Status);
        Assert.Equal(purse + order.Price, Coin.Count(sim, me));
    }

    [Fact]
    public void APieceAskedForAndPutOnShowIsBoughtByWhoAskedForIt()
    {
        var (sim, me, smithy, _) = Town("Blacksmith");
        TickUntil(sim, () => Asked(sim) != null, 12);
        var order = Asked(sim)!;
        Assert.NotNull(order);
        var piece = InTheSmithy(sim, smithy, order.Wants!.Name);
        sim.ApplyNow(new SetItemPriceCommand(me, piece, Math.Min(order.Price, 1), true));
        TickUntil(sim, () => !order.IsOpen, 6);
        Assert.Equal(OrderStatus.Done, order.Status);
        Assert.True(Inventory.OnPerson(sim, order.Poster, piece) || sim.World.Get<Equipment>(order.Poster).Weapon == piece
            || sim.Worn(order.Poster).Contains(piece));
    }

    [Fact]
    public void AnAskForAPieceSurvivesASave()
    {
        var (sim, _, _, _) = Town("Blacksmith");
        TickUntil(sim, () => Asked(sim) != null, 12);
        var order = Asked(sim)!;
        var loaded = SaveLoadTests.RoundTrip(sim);
        var again = loaded.Shops.Find(order.Id)!;
        Assert.Equal(order.Wants, again.Wants);
        Assert.Equal(order.WantsMending, again.WantsMending);
        SaveLoadTests.AssertSameFuture(sim, loaded, 2);
    }
}
