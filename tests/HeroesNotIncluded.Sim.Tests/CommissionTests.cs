using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// Commissions on the notice board (<see cref="Commissions"/>): pinned there, known at once to the
/// trade that makes the thing and to whoever reads the board, taken by the maker, and paid when the
/// whole lot's been handed over.
/// </summary>
public class CommissionTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Bea = new("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid Cal = new("00000000-0000-0000-0000-00000000000c");

    /// <summary>A round with Ana the carpenter, Bea the smith and Cal the tinker.</summary>
    private static Simulation Round()
    {
        var sim = VillageRound.NewSimulation(TestWorld.LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        foreach (var (p, trade) in new[] { (Ana, "Carpenter"), (Bea, "Blacksmith"), (Cal, "Tinker") })
        {
            sim.ApplyNow(new JoinLobbyCommand(p, p.ToString()[^1..], default));
            sim.ApplyNow(new SetTradePreferencesCommand(p, [sim.Defs.Trade(trade)], []));
        }
        sim.ApplyNow(new StartRoundCommand());
        return sim;
    }

    private static void Put(Simulation sim, Entity who, int3 cell)
    {
        sim.World.Get<Position>(who).Cell = cell;
        sim.World.Get<Position>(who).Pos = Position.CenterOf(cell);
    }

    private static Entity Board(Simulation sim) => sim.World.Query<Thing>().Single(e => Commissions.IsBoard(sim, e));

    /// <summary>Stand someone at the notice board, on their own free cell beside it.</summary>
    private static void AtBoard(Simulation sim, Entity who, int nth = 0)
    {
        var at = sim.World.Get<Thing>(Board(sim)).Cell;
        Put(sim, who, sim.SpiralFrom(at, 1).Where(c => c != at && sim.Map.Passable(c) && sim.Map.Standable(sim.Map.Idx(c))).Skip(nth).First());
    }

    private static int Count(Simulation sim, Entity who, ThingDef def) => Inventory.CarriedOf(sim, who, def).Sum(e => sim.World.Get<Stack>(e).Count);

    private static bool Knows(Simulation sim, Entity who, Order e) => Perception.For(sim, who).Orders().Any(o => o.Id == e.Id);

    /// <summary>Bea, at the board, pins a commission for so many planks.</summary>
    private static (Simulation Sim, Entity Ana, Entity Bea, Entity Cal, Order Order) Planks(int count = 10, int price = 12, int hours = 8)
    {
        var sim = Round();
        var (ana, bea, cal) = (Players.CharacterOf(sim, Ana), Players.CharacterOf(sim, Bea), Players.CharacterOf(sim, Cal));
        AtBoard(sim, bea);
        sim.ApplyNow(new PostCommissionCommand(bea, sim.Defs.Thing("Planks"), count, price, hours));
        return (sim, ana, bea, cal, sim.Shops.All.Single(e => e.Commission));
    }

    /// <summary>Ana beside Bea, with just so many planks on her (her kit's taken off her): the stack they're in.</summary>
    private static Entity Bring(Simulation sim, Entity ana, Entity bea, int planks)
    {
        var at = sim.World.Get<Position>(bea).Cell;
        Put(sim, ana, sim.SpiralFrom(at, 3).First(c => c != at && sim.Map.Passable(c) && c.Z == at.Z));
        var def = sim.Defs.Thing("Planks");
        foreach (var had in Inventory.CarriedOf(sim, ana, def).ToList())
            sim.DestroyThing(had);
        Inventory.Stow(sim, ana, sim.CreateItem(def, planks));
        return Inventory.CarriedOf(sim, ana, def).Single();
    }

    [Fact]
    public void TheSquareHasANoticeBoardAndFIsToReadIt()
    {
        var sim = Round();
        var ana = Players.CharacterOf(sim, Ana);
        AtBoard(sim, ana);
        Assert.Equal(AvatarActionKind.ReadBoard, AvatarSystem.ActionAt(sim, ana, sim.World.Get<Thing>(Board(sim)).Cell).Kind);
    }

    [Fact]
    public void ACommissionIsPinnedAtTheBoardAndKnownToItsMakerAtOnce()
    {
        var sim = Round();
        var (ana, bea, cal) = (Players.CharacterOf(sim, Ana), Players.CharacterOf(sim, Bea), Players.CharacterOf(sim, Cal));
        var planks = sim.Defs.Thing("Planks");
        sim.ApplyNow(new PostCommissionCommand(bea, planks, 10, 12, 8)); // not at the board
        Assert.DoesNotContain(sim.Shops.All, e => e.Commission);

        AtBoard(sim, bea);
        sim.ApplyNow(new PostCommissionCommand(bea, planks, 10, 12, 8));
        var order = sim.Shops.All.Single(e => e.Commission);
        Assert.Equal(planks, order.Wants);
        Assert.Equal(10, order.Count);
        Assert.Equal(sim.Defs.Building("Woodshop"), sim.World.Get<PresetBuilding>(order.Where).Def);
        Assert.True(Knows(sim, bea, order));
        Assert.True(Knows(sim, ana, order)); // the carpenter makes planks: she hears of it at once
        Assert.False(Knows(sim, cal, order));
        Assert.Equal("10 planks", Perception.For(sim, ana).Orders().Single(o => o.Id == order.Id).KindLabel);

        AtBoard(sim, cal, 1);
        sim.ApplyNow(new ReadBoardCommand(cal));
        Assert.True(Knows(sim, cal, order));
    }

    [Fact]
    public void NothingNobodyMakesCanBeCommissioned()
    {
        var sim = Round();
        var bea = Players.CharacterOf(sim, Bea);
        AtBoard(sim, bea);
        Assert.DoesNotContain(sim.Defs.Thing("Berries"), Commissions.Commissionable(sim));
        Assert.Contains(sim.Defs.Thing("Planks"), Commissions.Commissionable(sim));
        sim.ApplyNow(new PostCommissionCommand(bea, sim.Defs.Thing("Berries"), 5, 5, 8));
        sim.ApplyNow(new PostCommissionCommand(bea, sim.Defs.Thing(Commissions.BoardDef), 1, 5, 8)); // not an item at all
        Assert.DoesNotContain(sim.Shops.All, e => e.Commission);
    }

    [Fact]
    public void OnlyTheMakerMayTakeIt()
    {
        var (sim, ana, bea, cal, order) = Planks();
        AtBoard(sim, cal, 1);
        sim.ApplyNow(new ReadBoardCommand(cal));
        sim.ApplyNow(new TakeOrderCommand(cal, order.Id));
        Assert.True(order.Assignee.IsNull);
        sim.ApplyNow(new TakeOrderCommand(bea, order.Id)); // not your own
        Assert.True(order.Assignee.IsNull);
        sim.ApplyNow(new TakeOrderCommand(ana, order.Id));
        Assert.Equal(ana, order.Assignee);
    }

    [Fact]
    public void ALotComesInPartsAndIsPaidForWhenItsAllCome()
    {
        var (sim, ana, bea, _, order) = Planks();
        var planks = sim.Defs.Thing("Planks");
        Inventory.Stow(sim, bea, sim.CreateItem(sim.Thing(Coin.Silver), 20));
        var first = Bring(sim, ana, bea, 4);
        var silver = Coin.Count(sim, ana);
        sim.ApplyNow(new GiveCommand(ana, bea, first, 4));
        Assert.True(order.IsOpen);
        Assert.Equal(4, order.Delivered);
        Assert.Equal(ana, order.Assignee); // bringing some is taking it on
        Assert.Equal(silver, Coin.Count(sim, ana));

        // F while holding the rest at her: a hand-over, toward the lot.
        var rest = Bring(sim, ana, bea, 10);
        sim.ApplyNow(new Core.HoldCommand(ana, rest));
        var action = AvatarSystem.ActionAt(sim, ana, sim.World.Get<Position>(bea).Cell);
        Assert.Equal(AvatarActionKind.HandOver, action.Kind);
        Assert.Contains("6 of 10", action.Label);
        var beaHad = Count(sim, bea, planks);
        sim.ApplyNow(new GiveCommand(ana, bea, rest, 10));
        Assert.Equal(OrderStatus.Done, order.Status);
        Assert.Equal(beaHad + 6, Count(sim, bea, planks));
        Assert.Equal(4, Count(sim, ana, planks)); // more than was wanted comes back
        Assert.Equal(silver + 12, Coin.Count(sim, ana));
    }

    [Fact]
    public void CalledOffPartWayTheMakerIsPaidAShare()
    {
        var (sim, ana, bea, _, order) = Planks();
        Inventory.Stow(sim, bea, sim.CreateItem(sim.Thing(Coin.Silver), 20));
        sim.ApplyNow(new GiveCommand(ana, bea, Bring(sim, ana, bea, 5), 5));
        var silver = Coin.Count(sim, ana);
        sim.ApplyNow(new CancelCommissionCommand(ana, order.Id)); // not hers
        Assert.True(order.IsOpen);
        sim.ApplyNow(new CancelCommissionCommand(bea, order.Id));
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(silver + 6, Coin.Count(sim, ana));
    }

    [Fact]
    public void ACommissionNobodySeesToLapses()
    {
        var (sim, _, _, _, order) = Planks(hours: 1);
        sim.TickMany(SimClock.HoursToTicks(1) + SimClock.TicksPerHour / 6);
        Assert.Equal(OrderStatus.Expired, order.Status);
    }

    [Fact]
    public void ACommissionKeepsThroughASave()
    {
        var (sim, ana, bea, _, order) = Planks();
        sim.ApplyNow(new GiveCommand(ana, bea, Bring(sim, ana, bea, 3), 3));
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        var again = loaded.Shops.Find(order.Id)!;
        Assert.True(again.Commission);
        Assert.Equal(order.Wants, again.Wants);
        Assert.Equal(10, again.Count);
        Assert.Equal(3, again.Delivered);
    }

    [Fact]
    public void HostileValuesAreClamped()
    {
        var sim = Round();
        var bea = Players.CharacterOf(sim, Bea);
        AtBoard(sim, bea);
        sim.ApplyNow(new PostCommissionCommand(bea, sim.Defs.Thing("Planks"), -5, -100, 9999));
        var order = sim.Shops.All.Single(e => e.Commission);
        Assert.Equal(1, order.Count);
        Assert.Equal(0, order.Price);
        Assert.True(order.Deadline - order.PostedTick <= SimClock.HoursToTicks(Commissions.MaxHours));
        for (var i = 0; i < Commissions.MaxOpen + 2; i++)
            sim.ApplyNow(new PostCommissionCommand(bea, sim.Defs.Thing("Planks"), 2, 2, 2));
        Assert.Equal(Commissions.MaxOpen, sim.Shops.All.Count(e => e.IsOpen && e.Commission && e.Poster == bea));
    }
}
