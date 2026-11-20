using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// A raider who puts a player down cuts half their purse and runs for it (<see cref="Plunders"/>); caught before it's
/// away, the silver falls where it does. And a round's raid is sized more by its players than by its AI villagers.
/// </summary>
[Collection(nameof(SimContentTests))]
public class PlunderTests
{
    private static readonly Guid Arin = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Bea = new("00000000-0000-0000-0000-00000000000b");

    private static Simulation Round(params Guid[] players)
    {
        var sim = VillageRound.NewSimulation(TestWorld.LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        foreach (var id in players)
            sim.ApplyNow(new JoinLobbyCommand(id, id.ToString()[^1..], default));
        sim.ApplyNow(new StartRoundCommand());
        return sim;
    }

    private static (Simulation Sim, Entity Me, Entity Raider) Downed()
    {
        var sim = Round(Arin);
        var w = sim.World;
        var me = Players.CharacterOf(sim, Arin);
        var silver = sim.CreateItem(sim.Thing(Coin.Silver), 20 - Coin.Count(sim, me));
        Inventory.Stow(sim, me, silver);
        Assert.Equal(20, Coin.Count(sim, me));
        HealthSystem.SetDowned(sim, me, ref w.Get<Creature>(me), true);
        var at = w.Get<Position>(me).Cell;
        var by = sim.Map.NearestStandable(at.X + 2, at.Y, at.Z)!.Value;
        var raider = Raids.SpawnRaider(sim, sim.Defs.AllRaiderKinds[0], "Vask", by, new SimRandom(5));
        return (sim, me, raider);
    }

    private static void TickUntil(Simulation sim, Func<bool> done, int ticks)
    {
        for (var t = 0; t < ticks && !done(); t++)
        {
            sim.Tick();
            ref var c = ref sim.World.Get<Creature>(Players.CharacterOf(sim, Arin));
            if (!c.Dead && !c.Downed)
                HealthSystem.SetDowned(sim, Players.CharacterOf(sim, Arin), ref c, true); // stays down for the test
        }
    }

    [Fact]
    public void ARaiderCutsHalfADownedPlayersPurseAndRunsForIt()
    {
        var (sim, me, raider) = Downed();
        var w = sim.World;
        TickUntil(sim, () => w.Has<Plunder>(raider), SimClock.TicksPerSecond * 30);
        Assert.True(w.Has<Plunder>(raider), "robbed");
        Assert.Equal(10, Coin.Count(sim, me));
        Assert.Equal(10, w.Get<Plunder>(raider).Coins);
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("cuts") && m.Text.Contains("purse"));
        Assert.False(w.Has<Leaving>(raider), "still fair game for the town");
        sim.TickMany(SimClock.TicksPerSecond * 2);
        Assert.Contains("Making off", w.Get<Mind>(raider).Job?.Report ?? "");
    }

    [Fact]
    public void ARobberCaughtDropsTheSilverWhereItFalls()
    {
        var (sim, me, raider) = Downed();
        var w = sim.World;
        TickUntil(sim, () => w.Has<Plunder>(raider), SimClock.TicksPerSecond * 30);
        Assert.True(w.Has<Plunder>(raider));
        HealthSystem.SetDowned(sim, raider, ref w.Get<Creature>(raider), true);
        Assert.False(w.Has<Plunder>(raider));
        var at = w.Get<Position>(raider).Cell;
        var lying = w.Query<Thing>().Where(e => Coin.IsSilver(sim, e) && w.Get<Thing>(e).Spawned && Maps.Map.Distance(w.Get<Thing>(e).Cell, at) <= 2)
            .Sum(e => w.Get<Stack>(e).Count);
        Assert.Equal(10, lying);
        Assert.Contains(sim.Messages.Messages, m => m.Text.StartsWith("Caught!"));
    }

    [Fact]
    public void ARobberSavedAndLoadedStillHasItsPlunder()
    {
        var (sim, _, raider) = Downed();
        TickUntil(sim, () => sim.World.Has<Plunder>(raider), SimClock.TicksPerSecond * 30);
        var (loaded, _) = Saving.SaveGame.Read(Saving.SaveGame.Write(sim), sim.Defs);
        Assert.Equal(10, loaded.World.Get<Plunder>(raider).Coins);
    }

    [Fact]
    public void ARoundsRaidGrowsWithItsPlayersMoreThanItsVillagers()
    {
        var solo = Round(Arin);
        var pair = Round(Arin, Bea);
        var share = solo.Defs.Season.RaidAiShare;
        Assert.True(share < 1f);
        // One more player, one fewer AI villager to their trade: the band's the bigger by the difference.
        var players = solo.Creatures().Count(e => Players.Steered(solo, e));
        Assert.Equal(1, players);
        Assert.True(Raids.Points(pair, share) > Raids.Points(solo, share));
        Assert.True(Raids.Points(solo, share) < Raids.Points(solo), "a solo town's raid is smaller than the old count");
    }
}
