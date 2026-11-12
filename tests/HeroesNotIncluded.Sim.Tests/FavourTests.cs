using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Favours;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tests;

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

    private static void Beside(Simulation sim, Entity who, Entity other)
    {
        var at = sim.World.Get<Position>(other).Cell;
        var cell = sim.SpiralFrom(at, 3).First(c => c != at && sim.Map.Passable(c) && c.Z == at.Z);
        sim.World.Get<Position>(who).Cell = cell;
        sim.World.Get<Position>(who).Pos = Position.CenterOf(cell);
    }

    private static int AllCoin(Simulation sim) => sim.Creatures().Sum(e => Coin.Count(sim, e));

    [Fact]
    public void APlayersFavourIsTakenOnAndPaidWhenDone()
    {
        var sim = Round(Ana, Bea);
        var ana = Players.CharacterOf(sim, Ana);
        var bea = Players.CharacterOf(sim, Bea);
        Beside(sim, bea, ana);
        Inventory.Stow(sim, ana, sim.CreateItem(sim.Defs.Thing(Coin.Silver), 10));
        var coinBefore = AllCoin(sim);

        sim.ApplyNow(new PostFavourCommand(ana, "Bring me six birch logs", 7, 4));
        var favour = sim.Favours.All.Single(f => f.Poster == ana);
        Assert.Contains(Perception.For(sim, bea).Favours(), f => f.Id == favour.Id);

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
    public void AFavourNobodyFinishesExpires()
    {
        var sim = Round(Ana);
        var ana = Players.CharacterOf(sim, Ana);
        sim.ApplyNow(new PostFavourCommand(ana, "Watch the gate tonight", 3, 1));
        var favour = sim.Favours.All.Single();
        sim.TickMany(SimClock.HoursToTicks(1) + SimClock.TicksPerHour / 6);
        Assert.Equal(FavourStatus.Expired, favour.Status);
    }
}
