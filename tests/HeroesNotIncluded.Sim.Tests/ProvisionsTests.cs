using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Hospitality;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// The cook supplies the inn: the innkeeper orders a hamper (from the bar, or at the hatch), it's
/// packed and put up, taken or run over and unpacked: rations onto the merchandise shelf for sale,
/// and where the kitchen's apart (Reedmere) dishes onto the warming shelf for the lodgers' board.
/// </summary>
public class ProvisionsTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    private static Simulation Round(string map, string playerTrade = "Healer")
    {
        var sim = VillageRound.NewSimulation(LoadDefs(), 1234, map);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim, map);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade(playerTrade)], []));
        sim.ApplyNow(new StartRoundCommand());
        return sim;
    }

    private static Entity Holder(Simulation sim, string trade) =>
        sim.Creatures().First(e => sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == trade);

    private static void Purse(Simulation sim, Entity who, int silver) =>
        Inventory.Stow(sim, who, sim.CreateItem(sim.Defs.Thing(Coin.Silver), silver));

    private static void Put(Simulation sim, Entity who, int3 cell)
    {
        sim.World.Get<Position>(who).Cell = cell;
        sim.World.Get<Position>(who).Pos = Position.CenterOf(cell);
    }

    /// <summary>Somewhere to stand in reach of a piece of furniture.</summary>
    private static int3 Beside(Simulation sim, Entity thing) =>
        sim.SpiralFrom(sim.World.Get<Thing>(thing).Cell, 3).First(c => c != sim.World.Get<Thing>(thing).Cell && sim.Map.Passable(c)
            && AvatarSystem.InReach(sim, c, thing));

    [Fact]
    public void MillbrooksInnGetsItsHampersFromItsOwnKitchen()
    {
        var sim = Round(VillageRound.DefaultMap);
        Assert.False(Provisions.Split(sim));
        Assert.True(Provisions.Active(sim));
        Assert.True(Provisions.Shelf(sim).IsNull, "no warming shelf: the board is the hatch's meals");
        Assert.False(Provisions.Merchandise(sim).IsNull);
        Assert.Equal(Provisions.Inn(sim), Provisions.Cookhouse(sim));
        Assert.Contains(ShopPrices.Board(sim, Provisions.Inn(sim)), p => p.Kind == ServiceKind.Provisions);
        var keeper = Holder(sim, "Innkeeper");
        Purse(sim, keeper, 50);
        Assert.Null(Provisions.Order(sim, keeper));
        Assert.NotNull(Provisions.Ordered(sim, keeper));
        // A hamper holds rations only here: there's no board to send dishes for.
        Assert.True(Provisions.Packable(sim, sim.Defs.Thing(Rations.Def)));
        Assert.False(Provisions.Packable(sim, sim.Defs.Thing("Stew")));
    }

    [Fact]
    public void TheInnkeeperSendsForAHamperFromTheBar()
    {
        var sim = Round(VillageRound.DefaultMap, playerTrade: "Innkeeper");
        var w = sim.World;
        var me = sim.Creatures().First(e => w.Has<PlayerCharacter>(e));
        Purse(sim, me, 50);
        var bar = Buildings.Parts(sim, Provisions.Inn(sim)).First(p => w.Get<Thing>(p).Def.Building is { Bar: true });
        // Away from the bar, nothing.
        Put(sim, me, Beside(sim, Buildings.Parts(sim, Provisions.Inn(sim)).First(p => Rations.IsPrepTable(sim, p))));
        sim.ApplyNow(new OrderProvisionsCommand(me));
        Assert.Null(Provisions.Ordered(sim, me));
        Put(sim, me, Beside(sim, bar));
        sim.ApplyNow(new OrderProvisionsCommand(me));
        var order = Provisions.Ordered(sim, me);
        Assert.NotNull(order);
        Assert.Contains(Holder(sim, "Cook"), order!.KnownBy); // word goes to the kitchen
        Assert.False(ShopSystem.WaitsInLine(sim, order), "no place held at the hatch till the hamper's up");
        Assert.DoesNotContain(me, sim.Shops.LineAt(sim, Provisions.Cookhouse(sim), hatch: true));
    }

    [Fact]
    public void OnlyTheInnkeeperOrdersAndOnlyWithTheSilver()
    {
        var sim = Round(ReedmereMapTests.Map);
        Assert.True(Provisions.Split(sim));
        Assert.Contains(ShopPrices.Board(sim, Provisions.Cookhouse(sim)), p => p.Kind == ServiceKind.Provisions);
        Assert.DoesNotContain(ShopPrices.Board(sim, Provisions.Inn(sim)), p => p.Kind == ServiceKind.Provisions);
        Assert.Equal("the inn's keeper orders its provisions", Provisions.Order(sim, Holder(sim, "Cook")));
        var keeper = Holder(sim, "Innkeeper");
        foreach (var coin in Inventory.CarriedOf(sim, keeper, sim.Defs.Thing(Coin.Silver)))
            sim.World.Destroy(Inventory.TakeFromPerson(sim, keeper, coin, sim.World.Get<Stack>(coin).Count));
        Assert.StartsWith("a hamper is", Provisions.Order(sim, keeper));
        Purse(sim, keeper, 50);
        Assert.Null(Provisions.Order(sim, keeper));
        Assert.Equal(Provisions.Cookhouse(sim), Provisions.Ordered(sim, keeper)!.Where);
        Assert.Equal("you've a hamper on order already", Provisions.Order(sim, keeper));
    }

    [Fact]
    public void AHamperIsPackedHandedOverAndUnpackedOntoTheShelf()
    {
        var sim = Round(ReedmereMapTests.Map);
        var w = sim.World;
        var keeper = Holder(sim, "Innkeeper");
        var cook = Holder(sim, "Cook");
        Purse(sim, keeper, 50);
        Assert.Null(Provisions.Order(sim, keeper));
        var order = Provisions.Ordered(sim, keeper)!;
        Assert.Contains(cook, order.KnownBy); // word of work at the cookhouse reaches its cook

        // Four bowls of stew and a bundle of rations on the cook: into the hamper, in their arms.
        w.Get<Carrying>(cook).Item = Entity.Null;
        w.Get<Carrying>(cook).Item = sim.CreateItem(sim.Defs.Thing("Stew"), 4);
        Inventory.Stow(sim, cook, sim.CreateItem(sim.Defs.Thing(Rations.Def), 1));
        var hamper = Provisions.MakeHamper(sim, cook, loose: false);
        Assert.False(hamper.IsNull);
        Assert.Equal(hamper, w.Get<Carrying>(cook).Item);
        Assert.Equal(5, Provisions.Servings(sim, hamper));
        Assert.Empty(Provisions.DishesOn(sim, cook));

        // Handed over (as off the hatch): the order's done and paid, the hamper in the keeper's arms.
        var cookSilver = Coin.Count(sim, cook);
        w.Get<Carrying>(cook).Item = Entity.Null;
        sim.Shops.Received(sim, cook, keeper, hamper);
        Assert.Equal(OrderStatus.Done, order.Status);
        Assert.Equal(hamper, w.Get<Carrying>(keeper).Item);
        Assert.Equal(cookSilver + order.Price, Coin.Count(sim, cook));

        // At the warming shelf, F unpacks it: the stew there, the rations up for sale beside it.
        var shelf = Provisions.Shelf(sim);
        Assert.False(shelf.IsNull);
        Put(sim, keeper, Beside(sim, shelf));
        var action = AvatarSystem.ActionAt(sim, keeper, w.Get<Thing>(shelf).Cell);
        Assert.Equal(AvatarActionKind.BarWork, action.Kind);
        Assert.StartsWith("Unpack the hamper", action.Label);
        Assert.True(Provisions.Unpack(sim, keeper, shelf));
        Assert.False(w.IsAlive(hamper));
        Assert.True(w.Get<Carrying>(keeper).Item.IsNull);
        Assert.Equal(4, Provisions.OnShelf(sim));
        Assert.False(Provisions.ShelfBare(sim));
        Assert.Equal(1, Provisions.RationsShelved(sim));
        var bundle = sim.StoredIn(Provisions.Merchandise(sim)).Single();
        Assert.Equal(keeper, w.Get<ForSale>(bundle).Seller);
        Assert.Contains(Goods.OnShow(sim), g => g.Item == bundle && g.Shop == Provisions.Inn(sim));
        // Nobody's job takes from either: the lodgers' board, and the keeper's wares.
        Assert.All(sim.StoredIn(shelf), e => Assert.False(sim.IsAvailable(e)));
        Assert.False(sim.IsAvailable(bundle));
    }

    [Fact]
    public void APlayerCookPacksTheHamperAtThePrepTable()
    {
        var sim = Round(ReedmereMapTests.Map, playerTrade: "Cook");
        var w = sim.World;
        var me = sim.Creatures().First(e => w.Has<PlayerCharacter>(e));
        var keeper = Holder(sim, "Innkeeper");
        Purse(sim, keeper, 50);
        Assert.Null(Provisions.Order(sim, keeper));
        var table = Buildings.Parts(sim, Provisions.Cookhouse(sim)).First(p => Rations.IsPrepTable(sim, p));
        Put(sim, me, Beside(sim, table));
        w.Get<Carrying>(me).Item = Entity.Null;
        w.Get<Carrying>(me).Item = sim.CreateItem(sim.Defs.Thing("Stew"), 3);
        Assert.Equal("Pack a hamper for the inn", AvatarSystem.ActionAt(sim, me, w.Get<Thing>(table).Cell).Label);
        sim.ApplyNow(new PrepCommand(me, table, PrepStep.Hamper));
        Assert.True(w.TryGet<WorkSession>(me, out var s) && s is PackSession { ForHamper: true }, "the hamper's packed with the rations game");
    }

    [Fact]
    public void AHamperTakenInLineIsKeptInHandAndUnpackedNotSetDown()
    {
        // Off the hatch while the keeper's still in line: the line's job ending (ReleasePawn) mustn't
        // set it down where they stand, or nobody ever unpacks it.
        var sim = Round(VillageRound.DefaultMap);
        var w = sim.World;
        var keeper = Holder(sim, "Innkeeper");
        Assert.False(w.Has<Avatar>(keeper));
        w.Get<Carrying>(keeper).Item = Entity.Null;
        Inventory.Stow(sim, keeper, sim.CreateItem(sim.Defs.Thing(Rations.Def), 1));
        var hamper = Provisions.MakeHamper(sim, keeper, loose: false);
        Assert.Equal(hamper, w.Get<Carrying>(keeper).Item);
        AiSystem.ReleasePawn(sim, keeper);
        Assert.Equal(hamper, w.Get<Carrying>(keeper).Item);

        for (var t = 0; t < 12 && w.IsAlive(hamper); t++)
            sim.TickMany(SimClock.TicksPerHour / 12);
        Assert.False(w.IsAlive(hamper), w.IsAlive(hamper) ? $"still {(w.Get<Thing>(hamper).Spawned ? "set down" : "in hand")}; keeper: {w.Get<Mind>(keeper).Job?.Report}" : "");
        Assert.Equal(1, Provisions.RationsShelved(sim));
    }

    [Fact]
    public void ASlackLidLetsTheDishesTurn()
    {
        var sim = Round(ReedmereMapTests.Map);
        var w = sim.World;
        var keeper = Holder(sim, "Innkeeper");
        w.Get<Carrying>(keeper).Item = Entity.Null;
        w.Get<Carrying>(keeper).Item = sim.CreateItem(sim.Defs.Thing("Stew"), 2);
        var hamper = Provisions.MakeHamper(sim, keeper, loose: true);
        var shelf = Provisions.Shelf(sim);
        Put(sim, keeper, Beside(sim, shelf));
        Assert.True(Provisions.Unpack(sim, keeper, shelf));
        var stew = sim.StoredIn(shelf).Single();
        Assert.True(w.Get<Rot>(stew).Progress >= Provisions.LooseRot - 1e-4f);
    }

    [Fact]
    public void APartyBuysRationsOffTheMerchandiseShelfAndTakesNoneFree()
    {
        var sim = Round(ReedmereMapTests.Map);
        var w = sim.World;
        var keeper = Holder(sim, "Innkeeper");
        var shelf = Provisions.Merchandise(sim);
        var bundle = sim.CreateItem(sim.Defs.Thing(Rations.Def), 1);
        w.Add(bundle, new Ration { Quality = 0.5f });
        sim.Store(shelf, bundle);
        Assert.True(Goods.Set(sim, keeper, bundle, 3, true));
        // The cook can't sell from the innkeeper's shelf, though they keep the same town's food.
        Assert.False(Goods.CanSellAt(sim, Holder(sim, "Cook"), shelf));
        var member = sim.Parties.Send(sim, sim.Defs.Party("GildedSpurs"))[0];
        foreach (var r in Rations.Carried(sim, member))
            w.Destroy(Inventory.TakeFromPerson(sim, member, r, 1));
        Assert.True(Goods.IsUpgrade(sim, member, bundle), "a party member with none would buy a bundle");
        Assert.Contains(Goods.OnShow(sim), g => g.Item == bundle);
    }

    [Fact]
    public void ALabourerRunsTheHamperOverAndIsPaidForIt()
    {
        var sim = Round(ReedmereMapTests.Map, playerTrade: "Innkeeper");
        var w = sim.World;
        var me = sim.Creatures().First(e => w.Has<PlayerCharacter>(e));
        var cook = Holder(sim, "Cook");
        var runner = sim.Creatures().FirstOrDefault(e => Provisions.Runs(sim, e) && !Players.Steered(sim, e), Entity.Null);
        Assert.False(runner.IsNull, "Reedmere has a labourer");
        Assert.True(Provisions.RunnerAbout(sim));
        Purse(sim, me, 50);
        Assert.Null(Provisions.Order(sim, me));
        var order = Provisions.Ordered(sim, me)!;
        // A hamper of rations, up on the cookhouse hatch.
        var hatch = Hatch.Of(sim, Provisions.Cookhouse(sim));
        w.Get<Carrying>(cook).Item = Entity.Null;
        for (var n = 0; n < 2; n++)
        {
            var bundle = sim.CreateItem(sim.Defs.Thing(Rations.Def), 1);
            w.Add(bundle, new Ration { Quality = 0.6f, Maker = cook });
            Inventory.Stow(sim, cook, bundle);
        }
        var hamper = Provisions.MakeHamper(sim, cook, loose: false);
        w.Get<Carrying>(cook).Item = Entity.Null;
        Assert.True(sim.Store(hatch, hamper).IsNull);
        Assert.True(Provisions.HamperUp(sim));
        Assert.False(ShopSystem.WaitsInLine(sim, order), "with a labourer about the keeper doesn't wait at the cookhouse");
        Assert.NotNull(Provisions.ToRun(sim));

        // The labourer takes it and carries it over.
        Put(sim, runner, Beside(sim, hatch));
        var runnerSilver = Coin.Count(sim, runner);
        var cookSilver = Coin.Count(sim, cook);
        Assert.True(Provisions.TakeToRun(sim, runner));
        Assert.Equal(runner, order.Assignee);
        Assert.Equal(hamper, w.Get<Carrying>(runner).Item);
        var shelf = Provisions.UnpackAt(sim);
        Put(sim, runner, Beside(sim, shelf));
        Assert.True(Provisions.Unpack(sim, runner, shelf));
        Assert.Equal(OrderStatus.Done, order.Status);
        Assert.Equal(2, Provisions.RationsShelved(sim));
        Assert.All(sim.StoredIn(Provisions.Merchandise(sim)), e => Assert.Equal(me, w.Get<ForSale>(e).Seller));
        Assert.Equal(cookSilver + order.Price, Coin.Count(sim, cook));
        Assert.Equal(runnerSilver + Provisions.RunFee, Coin.Count(sim, runner));
    }

    [Fact]
    public void AnAiLabourerRunsAHamperUnasked()
    {
        var sim = Round(ReedmereMapTests.Map, playerTrade: "Innkeeper");
        var w = sim.World;
        var me = sim.Creatures().First(e => w.Has<PlayerCharacter>(e));
        var cook = Holder(sim, "Cook");
        Purse(sim, me, 50);
        Assert.Null(Provisions.Order(sim, me));
        var order = Provisions.Ordered(sim, me)!;
        var bundle = sim.CreateItem(sim.Defs.Thing(Rations.Def), 1);
        w.Add(bundle, new Ration { Quality = 0.6f, Maker = cook });
        w.Get<Carrying>(cook).Item = Entity.Null;
        Inventory.Stow(sim, cook, bundle);
        var hamper = Provisions.MakeHamper(sim, cook, loose: false);
        w.Get<Carrying>(cook).Item = Entity.Null;
        sim.Store(Hatch.Of(sim, Provisions.Cookhouse(sim)), hamper);
        for (var i = 0; i < 96 && order.IsOpen; i++)
            sim.TickMany(SimClock.TicksPerHour / 12);
        Assert.Equal(OrderStatus.Done, order.Status);
        Assert.True(Provisions.Runs(sim, order.Assignee), "a labourer ran it");
        Assert.Equal(1, Provisions.RationsShelved(sim));
    }

    [Fact]
    public void ALodgerHasTheirBoardOffTheShelfAtSupper()
    {
        var sim = Round(ReedmereMapTests.Map);
        var w = sim.World;
        var shelf = Provisions.Shelf(sim);
        sim.Store(shelf, sim.CreateItem(sim.Defs.Thing("Stew"), 6));
        // The merchandise shelf stocked too, so no hamper's sent for to fill the warming shelf back up.
        for (var n = 0; n < Provisions.RationsLow; n++)
        {
            var bundle = sim.CreateItem(sim.Defs.Thing(Rations.Def), 1);
            w.Add(bundle, new Ration { Quality = 0.5f });
            sim.Store(Provisions.Merchandise(sim), bundle);
        }
        while (sim.Clock.HourOfDay < ShopSystem.SupperFromHour)
            sim.TickMany(SimClock.TicksPerHour / 4);
        var party = sim.Parties.Send(sim, sim.Defs.Party("GildedSpurs")); // just in, for the night
        var lodger = party[0];
        // A room tonight, at the inn out at the landing, and there in time for supper.
        var bed = ShopSystem.FreeLetBed(sim, Provisions.Inn(sim));
        Assert.False(bed.IsNull);
        Lodging.Let(sim, lodger, bed);
        Assert.Equal(Provisions.Inn(sim), Provisions.BoardAt(sim, lodger));
        var lineAt = ShopSystem.LineCells(sim, Provisions.Inn(sim))[^1];
        foreach (var e in party)
        {
            Put(sim, e, lineAt);
            w.Get<Needs>(e).Values[sim.Defs.Need("Food").Index] = e == lodger ? 0.5f : 1f;
            w.Get<Health>(e).Injuries.Clear();
            w.Get<Adventurer>(e).NextDrink = sim.Clock.Tick + SimClock.TicksPerDay;
        }
        Order? board = null;
        var from = sim.Clock.Tick;
        for (var i = 0; i < 24 && board == null; i++)
        {
            sim.TickMany(SimClock.TicksPerHour / 12);
            board = sim.Shops.All.FirstOrDefault(o => o.Poster == lodger && o.Kind.Kind == ServiceKind.Meal && o.PostedTick >= from);
        }
        Assert.True(board != null, string.Join("\n", sim.Shops.All.Where(o => o.Poster == lodger).Select(o => $"{o.Kind.Name} {o.Status} at {o.Where}"))
            + $"\nhunger {w.Get<Needs>(lodger).Get(sim.Defs.Need("Food")):0.00} hour {sim.Clock.HourOfDay:0.0} board at {Provisions.BoardAt(sim, lodger)} savouring {Table.IsSavouring(sim, lodger)}");
        Assert.True(Provisions.IsBoard(sim, board!), "asked at the inn, not the cookhouse");
        Assert.Equal(0, board!.Price);
        // The innkeeper fed already: they'd go over to the cookhouse for their own supper otherwise.
        var keeperX = Holder(sim, "Innkeeper");
        w.Get<Needs>(keeperX).Values[sim.Defs.Need("Food").Index] = 1f;
        foreach (var o in sim.Shops.All.Where(o => o.IsOpen && o.Poster == keeperX))
            o.Status = OrderStatus.Cancelled;
        var seen = new List<string>();
        for (var i = 0; i < 36 && board.IsOpen; i++)
        {
            sim.TickMany(SimClock.TicksPerHour / 12);
            seen.Add($"{w.Get<Mind>(keeperX).Job?.GetType().Name}:{w.Get<Mind>(keeperX).Job?.Report} assignee={board.AssigneeName} knows={board.KnownBy.Contains(keeperX)}");
        }
        Assert.True(board.Status == OrderStatus.Done, string.Join("\n", seen.Distinct()));
        Assert.True(Provisions.OnShelf(sim) < 6, "served off the shelf");
    }

    [Fact]
    public void ACookhouseCookPacksAHamperWhenTheInnHasOneOnOrder()
    {
        var sim = Round(ReedmereMapTests.Map, playerTrade: "Innkeeper");
        var w = sim.World;
        var me = sim.Creatures().First(e => w.Has<PlayerCharacter>(e));
        var cook = Holder(sim, "Cook");
        Purse(sim, me, 50);
        Assert.Null(Provisions.Order(sim, me));
        var order = Provisions.Ordered(sim, me)!;
        // A pot of stew ready to ladle, and the cook left to it.
        var pot = Cooking.PotIn(sim, Provisions.Cookhouse(sim));
        Cooking.Add(sim, pot, sim.CreateItem(sim.Defs.Thing("Meat"), 6));
        w.Get<StewPot>(pot).Doneness = 1f;
        for (var i = 0; i < 48 && !Provisions.HamperUp(sim); i++)
            sim.TickMany(SimClock.TicksPerHour / 12);
        Assert.True(Provisions.HamperUp(sim), "the cook puts a hamper up on the hatch");
        Assert.True(order.IsOpen, "it waits on the hatch for the innkeeper");
    }

    [Fact]
    public void ACutCausewayStopsTheCarterAtTheWater()
    {
        var sim = Round(ReedmereMapTests.Map);
        var w = sim.World;
        Assert.NotNull(sim.Repairs.BreakBridge(sim, sim.Rand));
        var day = sim.Clock.Day;
        var seen = sim.Messages.Total;
        while (sim.Clock.Day == day || sim.Clock.HourOfDay < InnSupplySystem.DeliveryHour + 0.5f)
        {
            sim.TickMany(SimClock.TicksPerHour / 4);
            if (!sim.Repairs.RoadCut) // the tinker's mended it overnight: down again for the morning
                sim.Repairs.BreakBridge(sim, sim.Rand);
        }
        var said = sim.Messages.Messages.Skip(sim.Messages.IndexAfter(seen)).Select(m => m.Text).Where(t => t.Contains("carter")).ToList();
        // Reedmere's inn and cookhouse both stand out on the water: neither is reached, nor the forge's ore.
        Assert.True(said.Any(t => t.Contains("couldn't get through") && t.Contains("the inn and the cookhouse")), string.Join("\n", said));
        Assert.DoesNotContain(said, t => t.Contains("left a load of ore"));
    }
}
