using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Director;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>The Director: a round's crises on the season's beats, met or not.</summary>
public class DirectorTests
{
    private static Simulation Round(bool director = true, bool start = true)
    {
        var sim = VillageRound.NewSimulation(TestWorld.LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        if (director)
            sim.Director.Enable(sim.Defs.Difficulty("Normal"));
        if (start)
        {
            sim.Round.OpenLobby();
            sim.ApplyNow(new JoinLobbyCommand(Guid.Empty, "Arin", default, []));
            sim.ApplyNow(new StartRoundCommand());
        }
        return sim;
    }

    private static List<Entity> Raiders(Simulation sim) =>
        sim.Creatures().Where(e => sim.World.Get<Creature>(e) is { Faction: Faction.Raider, Dead: false }).ToList();

    [Fact]
    public void OffUnlessTheGameTurnsItOn()
    {
        var sim = Round(director: false);
        sim.Clock.Tick = sim.Round.TickAt(sim, 2, 9);
        sim.TickMany(SimClock.TicksPerHour * 9);
        Assert.Empty(Raiders(sim));
        Assert.Empty(sim.Director.Active);
    }

    [Fact]
    public void NothingComesOutsideARound()
    {
        var sim = Round(start: false);
        sim.TickMany(SimClock.TicksPerHour * 2);
        Assert.Empty(sim.Director.Active);
        Assert.Empty(Raiders(sim));
    }

    [Fact]
    public void TheFirstNightBringsARaidInItsWindow()
    {
        var sim = Round();
        var beat = sim.Defs.Season.Beats[0];
        sim.TickMany(SimClock.TicksPerHour); // the beats are planned
        sim.Clock.Tick = sim.Round.TickAt(sim, beat.Day, beat.FromHour) - 1;
        for (var t = 0; t < SimClock.HoursToTicks(beat.ToHour - beat.FromHour) + 200 && sim.Director.Active.Count == 0; t++)
            sim.Tick();
        var raid = Assert.IsType<RaidCrisis>(Assert.Single(sim.Director.Active));
        Assert.NotEmpty(raid.Raiders);
        Assert.Contains(sim.Speech.Kept, u => u.Text.StartsWith("Raiders!"));
    }

    [Fact]
    public void RaidersDrivenOffIsACrisisMet()
    {
        var sim = Round();
        var raid = (RaidCrisis)sim.Director.Send(sim, CrisisKind.Raid)!;
        var renown = sim.Reckoning.Renown;
        foreach (var e in raid.Raiders)
            NeedsSystem.Kill(sim, e, ref sim.World.Get<Creature>(e));
        sim.TickMany(SimClock.TicksPerHour / 6 + 1);
        Assert.Empty(sim.Director.Active);
        Assert.Equal(1, sim.Director.Met);
        Assert.Equal(renown + sim.Defs.Season.CrisisMetRenown, sim.Reckoning.Renown);
    }

    [Fact]
    public void RaidersStillAboutWhenTimeRunsOutIsACrisisUnmet()
    {
        var sim = Round();
        var raid = (RaidCrisis)sim.Director.Send(sim, CrisisKind.Raid)!;
        var dread = sim.Reckoning.Dread;
        // Keep them at the edge: they've come, and nobody's gone out to them.
        foreach (var e in raid.Raiders)
            sim.World.Get<Mind>(e).Drafted = true;
        sim.TickMany(SimClock.HoursToTicks(sim.Defs.Season.CrisisHours) + SimClock.TicksPerHour / 6 + 1);
        Assert.Empty(sim.Director.Active);
        Assert.Equal(1, sim.Director.Unmet);
        Assert.True(sim.Reckoning.Dread >= dread + sim.Defs.Season.CrisisUnmetDread - 0.01f);
        Assert.All(raid.Raiders.Where(sim.World.IsAlive), e => Assert.True(sim.World.Has<Combat.Leaving>(e)));
    }

    [Fact]
    public void ACrisisUnderWaySurvivesASave()
    {
        var sim = Round();
        sim.TickMany(SimClock.TicksPerHour);
        var raid = (RaidCrisis)sim.Director.Send(sim, CrisisKind.Raid)!;
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.True(loaded.Director.Enabled);
        var again = Assert.IsType<RaidCrisis>(Assert.Single(loaded.Director.Active));
        Assert.Equal(raid.Raiders, again.Raiders);
        Assert.Equal(raid.Started, again.Started);
    }
}
