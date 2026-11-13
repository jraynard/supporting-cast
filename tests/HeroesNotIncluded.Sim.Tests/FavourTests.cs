using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Favours;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>Favours on the notice board: pinned there, known by reading it, done for silver; errands done by bringing.</summary>
public class FavourTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Bea = new("00000000-0000-0000-0000-00000000000b");

    private static Simulation Round(params Guid[] players)
    {
        var sim = VillageRound.NewSimulation(TestWorld.LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        foreach (var p in players)
            sim.ApplyNow(new JoinLobbyCommand(p, p.ToString()[^1..], default, []));
        sim.ApplyNow(new StartRoundCommand());
        return sim;
    }

    private static void Put(Simulation sim, Entity who, int3 cell)
    {
        sim.World.Get<Position>(who).Cell = cell;
        sim.World.Get<Position>(who).Pos = Position.CenterOf(cell);
    }

    private static void Beside(Simulation sim, Entity who, Entity other)
    {
        var at = sim.World.Get<Position>(other).Cell;
        Put(sim, who, sim.SpiralFrom(at, 3).First(c => c != at && sim.Map.Passable(c) && c.Z == at.Z));
    }

    private static Entity Board(Simulation sim) => sim.World.Query<Thing>().Single(e => FavourBoard.IsBoard(sim, e));

    /// <summary>Stand someone at the notice board, on their own free cell beside it.</summary>
    private static void AtBoard(Simulation sim, Entity who, int nth = 0)
    {
        var at = sim.World.Get<Thing>(Board(sim)).Cell;
        Put(sim, who, sim.SpiralFrom(at, 1).Where(c => c != at && sim.Map.Passable(c) && sim.Map.Standable(sim.Map.Idx(c))).Skip(nth).First());
    }

    private static int AllCoin(Simulation sim) => sim.Creatures().Sum(e => Coin.Count(sim, e));

    [Fact]
    public void TheSquareHasANoticeBoardAndFIsToReadIt()
    {
        var sim = Round(Ana);
        var ana = Players.CharacterOf(sim, Ana);
        var board = Board(sim);
        AtBoard(sim, ana);
        Assert.Equal(AvatarActionKind.ReadBoard, AvatarSystem.ActionAt(sim, ana, sim.World.Get<Thing>(board).Cell).Kind);
    }

    [Fact]
    public void FavoursArePinnedAtTheBoardAndKnownByReadingIt()
    {
        var sim = Round(Ana, Bea);
        var ana = Players.CharacterOf(sim, Ana);
        var bea = Players.CharacterOf(sim, Bea);

        // Away from the board, nothing's pinned.
        sim.ApplyNow(new PostFavourCommand(ana, "Bring me six birch logs", 7, 4));
        Assert.Empty(sim.Favours.All);

        AtBoard(sim, ana);
        Beside(sim, bea, ana); // standing right by: still doesn't know till she reads it
        sim.ApplyNow(new PostFavourCommand(ana, "Bring me six birch logs", 7, 4));
        var favour = sim.Favours.All.Single(f => f.Poster == ana);
        Assert.DoesNotContain(Perception.For(sim, bea).Favours(), f => f.Id == favour.Id);

        AtBoard(sim, bea, 1);
        sim.ApplyNow(new ReadBoardCommand(bea));
        Assert.Contains(Perception.For(sim, bea).Favours(), f => f.Id == favour.Id);
    }

    [Fact]
    public void AFavourIsTakenOnAndPaidWhenDone()
    {
        var sim = Round(Ana, Bea);
        var ana = Players.CharacterOf(sim, Ana);
        var bea = Players.CharacterOf(sim, Bea);
        AtBoard(sim, ana);
        AtBoard(sim, bea, 1);
        Inventory.Stow(sim, ana, sim.CreateItem(sim.Defs.Thing(Coin.Silver), 10));
        var coinBefore = AllCoin(sim);

        sim.ApplyNow(new PostFavourCommand(ana, "Watch the gate tonight", 7, 4));
        var favour = sim.Favours.All.Single(f => f.Poster == ana);
        sim.ApplyNow(new AcceptFavourCommand(bea, favour.Id)); // she hasn't read it
        Assert.True(favour.Assignee.IsNull);
        sim.ApplyNow(new ReadBoardCommand(bea));

        sim.ApplyNow(new AcceptFavourCommand(ana, favour.Id)); // not your own
        Assert.True(favour.Assignee.IsNull);
        sim.ApplyNow(new AcceptFavourCommand(bea, favour.Id));
        Assert.Equal(bea, favour.Assignee);
        sim.ApplyNow(new CompleteFavourCommand(bea, favour.Id)); // only the asker says it's done
        Assert.True(favour.IsOpen);

        var had = Coin.Count(sim, bea);
        sim.ApplyNow(new CompleteFavourCommand(ana, favour.Id));
        Assert.Equal(FavourStatus.Done, favour.Status);
        Assert.Equal(had + 7, Coin.Count(sim, bea));
        Assert.Equal(coinBefore, AllCoin(sim));
    }

    [Fact]
    public void AnErrandIsDoneByBringingIt()
    {
        var sim = Round(Ana, Bea);
        var ana = Players.CharacterOf(sim, Ana);
        var bea = Players.CharacterOf(sim, Bea);
        AtBoard(sim, ana);
        AtBoard(sim, bea, 1);
        var wood = sim.Defs.Thing("Wood");
        sim.ApplyNow(new PostErrandCommand(ana, wood, 4, 6, 4));
        var errand = sim.Favours.All.Single();
        Assert.Equal(wood, errand.Wants);
        Assert.Equal(4, errand.Count);
        sim.ApplyNow(new ReadBoardCommand(bea));
        sim.ApplyNow(new AcceptFavourCommand(bea, errand.Id));

        sim.ApplyNow(new DeliverFavourCommand(bea, errand.Id)); // nothing to bring yet
        Assert.True(errand.IsOpen);
        Inventory.Stow(sim, bea, sim.CreateItem(wood, 5));
        var anaWood = Inventory.CarriedOf(sim, ana, wood).Sum(e => sim.World.Get<Stack>(e).Count);
        var paid = Coin.Count(sim, bea);
        sim.ApplyNow(new DeliverFavourCommand(bea, errand.Id));
        Assert.Equal(FavourStatus.Done, errand.Status);
        Assert.Equal(anaWood + 4, Inventory.CarriedOf(sim, ana, wood).Sum(e => sim.World.Get<Stack>(e).Count));
        Assert.Equal(1, Inventory.CarriedOf(sim, bea, wood).Sum(e => sim.World.Get<Stack>(e).Count));
        Assert.Equal(paid + 6, Coin.Count(sim, bea));

        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.Equal(wood, loaded.Favours.All.Single().Wants);
    }

    [Fact]
    public void TheLabourerTakesOnAnErrandAndBringsIt()
    {
        var sim = Round(Ana);
        var ana = Players.CharacterOf(sim, Ana);
        var w = sim.World;
        var labourer = sim.Creatures().Single(e => !w.Has<Avatar>(e) && w.TryGet<Trade>(e, out var t) && t.Def.Unlimited);
        AtBoard(sim, ana);
        w.Get<Mind>(ana).Drafted = true;
        var wood = sim.Defs.Thing("Stone"); // not wood: on a cold day that goes on the fires
        // Stone lying in the square for the fetching.
        var at = w.Get<Thing>(Board(sim)).Cell;
        var pile = sim.SpiralFrom(at, 4).First(c => Map.Distance(c, at) >= 3 && sim.Map.Passable(c) && sim.Map.Standable(sim.Map.Idx(c)) && sim.Map.Item[sim.Map.Idx(c)].IsNull);
        sim.SpawnItem(wood, 6, pile);
        sim.ApplyNow(new PostErrandCommand(ana, wood, 3, 5, 8));
        var errand = sim.Favours.All.Single();
        for (var i = 0; i < 32 && errand.IsOpen; i++)
            sim.TickMany(SimClock.TicksPerHour / 8);
        Assert.True(errand.Status == FavourStatus.Done, $"labourer: {w.Get<Mind>(labourer).Job?.Report ?? "idle"}, last failure {sim.Ai.LastFailure.GetValueOrDefault(labourer)}, haul priority {w.Get<Mind>(labourer).WorkPriority[(int)Defs.WorkType.Haul]}, pack {sim.PackOf(labourer)}");
        Assert.Equal(labourer, errand.Assignee);
    }

    [Fact]
    public void AFavourNobodyFinishesExpires()
    {
        var sim = Round(Ana);
        var ana = Players.CharacterOf(sim, Ana);
        AtBoard(sim, ana);
        sim.ApplyNow(new PostFavourCommand(ana, "Watch the gate tonight", 3, 1));
        var favour = sim.Favours.All.Single();
        sim.TickMany(SimClock.HoursToTicks(1) + SimClock.TicksPerHour / 6);
        Assert.Equal(FavourStatus.Expired, favour.Status);
    }
}
