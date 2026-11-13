using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>A round's season: its festival on the last evening, its end, and the town's Renown and Dread.</summary>
public class SeasonTests
{
    private static Simulation Round(ulong seed = 7)
    {
        var (sim, _) = TestWorld.Millbrook(seed, 4);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Guid.Empty, "Arin", default, []));
        sim.ApplyNow(new StartRoundCommand());
        return sim;
    }

    /// <summary>Jump the clock to just before <paramref name="tick"/>, then play an hour past it.</summary>
    private static void PlayTo(Simulation sim, long tick)
    {
        sim.Clock.Tick = Math.Max(sim.Clock.Tick, tick - 1);
        sim.TickMany(SimClock.TicksPerHour);
    }

    [Fact]
    public void TheFestivalFallsOnTheLastEveningAndThenTheRoundIsOver()
    {
        var sim = Round();
        var season = sim.Defs.Season;
        Assert.Equal(RoundPhase.Playing, sim.Round.Phase);
        Assert.Equal((1, season.Days), sim.Round.SeasonDay(sim));

        // The deal was at 08:00 on day one: the festival is the last day's evening.
        var festival = sim.Round.FestivalTick(sim);
        var at = festival + sim.Clock.StartHour * (long)SimClock.TicksPerHour;
        Assert.Equal(season.Days, (int)(at / SimClock.TicksPerDay) + 1);
        Assert.Equal(season.FestivalHour, at % SimClock.TicksPerDay / (float)SimClock.TicksPerHour, 2);

        sim.Clock.Tick = festival - SimClock.TicksPerHour * 2;
        sim.TickMany(SimClock.TicksPerHour);
        Assert.Equal(RoundPhase.Playing, sim.Round.Phase);
        Assert.Equal((season.Days, season.Days), sim.Round.SeasonDay(sim));

        PlayTo(sim, festival);
        Assert.Equal(RoundPhase.Festival, sim.Round.Phase);
        Assert.True(sim.Round.Live);

        PlayTo(sim, sim.Round.EndTick(sim));
        Assert.Equal(RoundPhase.Over, sim.Round.Phase);
        Assert.False(sim.Round.Live);
        Assert.Equal(RoundOutcome.FellShort, sim.Round.Outcome); // nobody's been served
        var summary = sim.Round.Summary!;
        Assert.True(summary.People.Exists(p => p.Player && p.Name == "Arin"), string.Join("; ", summary.People));
        Assert.NotEmpty(summary.RenownByHour); // only the hours played: this test jumps the clock
    }

    [Fact]
    public void RenownAtTheFestivalWinsTheSeason()
    {
        var sim = Round();
        for (var i = 0; i < 7; i++)
            sim.Reckoning.AdventurerLeft(sim, 50f); // counts for no more than the range's top
        Assert.Equal(7 * sim.Defs.Season.SatisfactionRange[1] * sim.Defs.Season.RenownPerSatisfaction, sim.Reckoning.Renown);

        PlayTo(sim, sim.Round.EndTick(sim));
        Assert.Equal(RoundOutcome.Won, sim.Round.Outcome);
        Assert.Equal(7, sim.Round.Summary!.AdventurersServed);
    }

    [Fact]
    public void DreadOverflowingEndsTheRoundEarly()
    {
        var sim = Round();
        sim.Reckoning.Add(sim, 0, sim.Defs.Season.DreadLimit * 2);
        sim.TickMany(SimClock.TicksPerHour);
        Assert.Equal(RoundPhase.Over, sim.Round.Phase);
        Assert.Equal(RoundOutcome.Dread, sim.Round.Outcome);
        Assert.Equal(sim.Defs.Season.DreadLimit, sim.Round.Summary!.Dread);
    }

    [Fact]
    public void QuestsLeftUnansweredFeedDread()
    {
        var sim = Round();
        sim.TickMany(SimClock.TicksPerHour * 6);
        var open = sim.Expeditions.Quests.Count(q => q.Status == Expeditions.QuestStatus.Open);
        Assert.True(open > 0);
        Assert.True(sim.Reckoning.Dread > 0, "open quests weigh on the valley");
        Assert.True(sim.Reckoning.Dread <= open * sim.Defs.Season.DreadPerOpenQuestHour * 6 + 0.01f);

        var view = Perception.For(sim, Players.CharacterOf(sim, Guid.Empty));
        Assert.NotNull(view.Standing());
    }

    [Fact]
    public void AnAdventurerLeavingTownCountsTowardRenown()
    {
        var sim = Round();
        var party = sim.Parties.Send(sim, sim.Defs.AllParties[0]);
        Assert.NotEmpty(party);
        foreach (var e in party)
        {
            ref var a = ref sim.World.Get<Adventurer>(e);
            a.Satisfaction = 3f;
            a.LeaveTick = sim.Clock.Tick;
        }
        sim.TickMany(SimClock.TicksPerHour / 2);
        Assert.Equal(party.Count, sim.Reckoning.AdventurersCounted);
        Assert.Equal(3f * party.Count * sim.Defs.Season.RenownPerSatisfaction, sim.Reckoning.Renown, 3);
    }

    [Fact]
    public void TheSeasonAndItsEndSurviveASave()
    {
        var sim = Round();
        sim.TickMany(SimClock.TicksPerHour * 3);
        sim.Reckoning.AdventurerLeft(sim, 4f);
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.Equal(sim.Round.StartTick, loaded.Round.StartTick);
        Assert.Equal(sim.Round.FestivalTick(sim), loaded.Round.FestivalTick(loaded));
        Assert.Equal(sim.Reckoning.Renown, loaded.Reckoning.Renown);
        Assert.Equal(sim.Reckoning.Dread, loaded.Reckoning.Dread);
        Assert.Equal(sim.Reckoning.RenownByHour, loaded.Reckoning.RenownByHour);

        sim.Reckoning.Add(sim, 0, 1000);
        sim.TickMany(SimClock.TicksPerHour);
        (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.Equal(RoundPhase.Over, loaded.Round.Phase);
        Assert.Equal(RoundOutcome.Dread, loaded.Round.Outcome);
        Assert.Equal(sim.Round.Summary!.People.Count, loaded.Round.Summary!.People.Count);
        Assert.Equal(sim.Round.Summary.DreadByHour, loaded.Round.Summary.DreadByHour);
    }

    [Fact]
    public void NobodyActsOnceTheSeasonIsOver()
    {
        var sim = Round();
        var me = Players.CharacterOf(sim, Guid.Empty);
        var say = new SayCommand(me, "hello", Volume.Say);
        const string over = "the season's over";
        Assert.NotEqual(over, CommandRules.Refusal(sim, say, Guid.Empty, isHost: true));
        sim.Round.End(sim, RoundOutcome.FellShort);
        Assert.Equal(over, CommandRules.Refusal(sim, say, Guid.Empty, isHost: true));
    }

    [Fact]
    public void AnEndlessRoundHasNoFestival()
    {
        var sim = Round();
        sim.Round.Endless = true;
        PlayTo(sim, sim.Round.EndTick(sim));
        Assert.Equal(RoundPhase.Playing, sim.Round.Phase);
    }
}
