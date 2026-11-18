using HeroesNotIncluded.Siege;
using HeroesNotIncluded.Sim.Modding;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>Ways to play a round (<see cref="IGameMode"/>): the game's own, and a mod's (mods/siege).</summary>
[Collection(nameof(SimContentTests))]
public class GameModeTests
{
    private static readonly Guid Arin = new("00000000-0000-0000-0000-00000000000a");

    private static Simulation Round(string? mode)
    {
        var sim = VillageRound.NewSimulation(TestWorld.LoadFreshDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Director.Enable(sim.Defs.Difficulty("Normal"));
        sim.Round.OpenLobby();
        if (mode != null)
            sim.Round.SetMode(mode);
        sim.ApplyNow(new JoinLobbyCommand(Arin, "Arin", default));
        sim.ApplyNow(new SetTradePreferencesCommand(Arin, [sim.Defs.Trade("Blacksmith")], []));
        sim.ApplyNow(new StartRoundCommand());
        return sim;
    }

    [Fact]
    public void ARoundIsASeasonUnlessToldOtherwise()
    {
        var sim = Round(null);
        Assert.Equal(SeasonMode.Id, sim.Round.Mode);
        Assert.True(sim.Round.ModeOf(sim).Seasonal);
        sim.Round.SetMode(PracticeMode.Id); // not once it's begun
        Assert.Equal(SeasonMode.Id, sim.Round.Mode);
    }

    [Fact]
    public void AModeThatIsntThereIsRefusedAndASaveKeepsTheMode()
    {
        var sim = Round("no-such-mode");
        Assert.Equal(SeasonMode.Id, sim.Round.Mode);
        var practice = Round(PracticeMode.Id);
        Assert.True(practice.Round.Practice);
        var (loaded, _) = Saving.SaveGame.Read(Saving.SaveGame.Write(practice), practice.Defs);
        Assert.Equal(PracticeMode.Id, loaded.Round.Mode);
    }

    [Fact]
    public void SiegeNightStartsAtDuskSendsItsWavesAndEndsAtDawn()
    {
        try
        {
            SimContent.Use([.. TestContent.Standard, ("siege", typeof(SiegeMod).Assembly)]);
            var sim = Round(SiegeMode.Id);
            Assert.Equal(SiegeMode.Id, sim.Round.Mode);
            Assert.True(sim.Clock.HourOfDay >= SiegeMode.Dusk, $"it began at {sim.Clock.HourOfDay:0.0}");
            var siege = sim.System<SiegeSystem>();
            while (sim.Round.Phase == RoundPhase.Playing && sim.Clock.Tick < SiegeMode.DawnTick(sim) + SimClock.TicksPerHour)
                sim.TickMany(SimClock.TicksPerHour / 4);
            Assert.Equal(SiegeSystem.Waves.Length, siege.Sent);
            Assert.Equal(RoundPhase.Over, sim.Round.Phase);
            var summary = sim.Round.Summary!;
            // Held till dawn, or the town fell to it: either way the night decided it, and no festival came.
            Assert.Contains(summary.Outcome, (RoundOutcome[])[RoundOutcome.Won, RoundOutcome.TownFell]);
            if (summary.Outcome == RoundOutcome.Won)
                Assert.Equal("Dawn, and the town still stands.", summary.Headline);
            Assert.True(sim.Festival.StartTick < 0, "no festival in a siege");
            var (loaded, _) = Saving.SaveGame.Read(Saving.SaveGame.Write(sim), sim.Defs);
            Assert.Equal(summary.Headline, loaded.Round.Summary!.Headline);
        }
        finally
        {
            SimContent.Use(TestContent.Standard);
        }
    }
}
