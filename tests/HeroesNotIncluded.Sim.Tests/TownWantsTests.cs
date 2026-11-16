using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Favours;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// The labourer's living when no player posts (<see cref="Wants"/>): AI tradespeople running short
/// pin errands, and an errand for what isn't lying about gets the trees, bushes or rocks for it marked.
/// </summary>
public class TownWantsTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    private static Simulation Round()
    {
        var sim = VillageRound.NewSimulation(TestWorld.LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default, []));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade("Blacksmith")], []));
        sim.ApplyNow(new StartRoundCommand());
        return sim;
    }

    private static Entity Holder(Simulation sim, string trade) =>
        sim.World.Query<Trade>().First(e => sim.World.Get<Trade>(e).Def.Name == trade && !Players.Steered(sim, e));

    /// <summary>Take every one of a thing out of a building (its chests and its floor).</summary>
    private static void Empty(Simulation sim, Entity building, string def)
    {
        var w = sim.World;
        foreach (var part in Buildings.Parts(sim, building))
            foreach (var item in sim.StoredIn(part).ToList())
                if (w.Get<Thing>(item).Def.Name == def)
                    sim.DestroyThing(item);
        foreach (var item in sim.AvailableItems().ToList())
            if (w.Get<Thing>(item).Def.Name == def && w.Get<Thing>(item).Spawned && Buildings.At(sim, w.Get<Thing>(item).Cell) == building)
                sim.DestroyThing(item);
    }

    [Fact]
    public void AnAiCookShortOfLogsPinsAnErrandForThemOnceAndPaysForIt()
    {
        var sim = Round();
        var cook = Holder(sim, "Cook");
        var kitchen = ShopSystem.WorkplaceOf(sim, sim.World.Get<Trade>(cook).Def);
        Empty(sim, kitchen, "Wood");
        Inventory.Stow(sim, cook, sim.CreateItem(sim.Thing(Coin.Silver), 20));
        Wants.Post(sim);
        Wants.Post(sim);
        var errands = sim.Favours.All.Where(f => f.IsOpen && f.Poster == cook && f.Wants?.Name == "Wood").ToList();
        Assert.Single(errands);
        Assert.True(errands[0].Price > 0);
        // A player's own trade posts nothing for them.
        var me = Players.CharacterOf(sim, Ana);
        Assert.DoesNotContain(sim.Favours.All, f => f.Poster == me);
    }

    [Fact]
    public void AnErrandForWhatIsntLyingAboutGetsTreesMarkedForTheLabourer()
    {
        var sim = Round();
        var w = sim.World;
        foreach (var item in sim.AvailableItems().ToList())
            if (w.Get<Thing>(item).Def.Name == "Wood")
                sim.DestroyThing(item);
        var cook = Holder(sim, "Cook");
        Inventory.Stow(sim, cook, sim.CreateItem(sim.Thing(Coin.Silver), 20));
        Assert.NotNull(sim.Favours.SendErrand(sim, cook, sim.Thing("Wood"), 10, 5, 8));
        var labourer = w.Query<Trade>().First(e => w.Get<Trade>(e).Def.Unlimited && !Players.Steered(sim, e));
        Assert.Empty(sim.Map.Designated(Designation.Chop));
        Assert.Null(ErrandJob.For(sim, labourer));
        var marked = sim.Map.Designated(Designation.Chop).ToList();
        Assert.NotEmpty(marked);
        Assert.True(marked.Count <= ErrandJob.MostMarked);
        // Asked again, nothing more's marked: enough is.
        ErrandJob.For(sim, labourer);
        Assert.Equal(marked.Count, sim.Map.Designated(Designation.Chop).Count);
    }

    [Fact]
    public void LeftToItselfTheTownsLabourerFetchesTheCookItsLogs()
    {
        var sim = Round();
        var w = sim.World;
        var cook = Holder(sim, "Cook");
        var kitchen = ShopSystem.WorkplaceOf(sim, w.Get<Trade>(cook).Def);
        Empty(sim, kitchen, "Wood");
        Inventory.Stow(sim, cook, sim.CreateItem(sim.Thing(Coin.Silver), 20));
        Wants.Post(sim);
        var errand = sim.Favours.All.Single(f => f.IsOpen && f.Poster == cook && f.Wants?.Name == "Wood");
        for (var i = 0; i < 48 && errand.IsOpen; i++)
            sim.TickMany(SimClock.TicksPerHour / 8);
        Assert.Equal(FavourStatus.Done, errand.Status);
        Assert.True(w.TryGet<Trade>(errand.Assignee, out var t) && t.Def.Unlimited);
    }
}
