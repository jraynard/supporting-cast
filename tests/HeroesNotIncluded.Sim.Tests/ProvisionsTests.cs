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
/// The cook supplies the inn where they stand apart (Reedmere): the innkeeper orders a hamper at
/// the cookhouse, it's packed, carried over and unpacked onto the warming shelf, and the shelf
/// feeds the lodgers and the road. On Millbrook, where the inn has its own kitchen, none of it applies.
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
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default, []));
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
    public void MillbrooksInnHasItsOwnKitchenAndNoHampers()
    {
        var sim = Round(VillageRound.DefaultMap);
        Assert.False(Provisions.Split(sim));
        Assert.True(Provisions.Shelf(sim).IsNull);
        foreach (var b in sim.World.Query<PresetBuilding>())
            Assert.DoesNotContain(ShopPrices.Board(sim, b), p => p.Kind == ServiceKind.Provisions);
        var keeper = Holder(sim, "Innkeeper");
        Purse(sim, keeper, 50);
        Assert.Equal("the inn has its own kitchen", Provisions.Order(sim, keeper));
        Assert.Null(Provisions.Ordered(sim, keeper));
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

        // At the warming shelf, F unpacks it.
        var shelf = Provisions.Shelf(sim);
        Assert.False(shelf.IsNull);
        Put(sim, keeper, Beside(sim, shelf));
        var action = AvatarSystem.ActionAt(sim, keeper, w.Get<Thing>(shelf).Cell);
        Assert.Equal(AvatarActionKind.BarWork, action.Kind);
        Assert.StartsWith("Unpack the hamper", action.Label);
        Assert.True(Provisions.Unpack(sim, keeper, shelf));
        Assert.False(w.IsAlive(hamper));
        Assert.True(w.Get<Carrying>(keeper).Item.IsNull);
        Assert.Equal(5, Provisions.OnShelf(sim));
        Assert.False(Provisions.ShelfBare(sim));
        // Nobody's job takes from it: it's the lodgers' board.
        Assert.All(sim.StoredIn(shelf), e => Assert.False(sim.IsAvailable(e)));
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
    public void APartySettingOutTakesRationsOffTheShelf()
    {
        var sim = Round(ReedmereMapTests.Map);
        var shelf = Provisions.Shelf(sim);
        var bundle = sim.CreateItem(sim.Defs.Thing(Rations.Def), 1);
        sim.World.Add(bundle, new Ration { Quality = 0.5f });
        sim.Store(shelf, bundle);
        var member = sim.Parties.Send(sim, sim.Defs.Party("GildedSpurs"))[0];
        foreach (var r in Rations.Carried(sim, member))
            sim.World.Destroy(Inventory.TakeFromPerson(sim, member, r, 1));
        Assert.True(Provisions.TakeRations(sim, member));
        Assert.Single(Rations.Carried(sim, member));
        Assert.Equal(0, Provisions.OnShelf(sim));
        Assert.False(Provisions.TakeRations(sim, member)); // one's enough, and the shelf's bare
    }

    [Fact]
    public void ALodgerHasTheirBoardOffTheShelfAtSupper()
    {
        var sim = Round(ReedmereMapTests.Map);
        var w = sim.World;
        var shelf = Provisions.Shelf(sim);
        sim.Store(shelf, sim.CreateItem(sim.Defs.Thing("Stew"), 6));
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
    public void ACutCausewayStopsTheCarterOnlyAtTheInn()
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
        Assert.True(said.Any(t => t.Contains("been by the cookhouse") && t.Contains("couldn't get to the inn")), string.Join("\n", said));
    }
}
