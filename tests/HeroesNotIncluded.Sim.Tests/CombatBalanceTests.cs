using HeroesNotIncluded.Sim.Scenarios;
using Xunit.Abstractions;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// The combat sweep (<see cref="CombatSweep"/>): the yardstick balance is tuned by. It has to give
/// the same answer for the same seed, and its fights have to be fought.
/// </summary>
public class CombatBalanceTests(ITestOutputHelper output)
{
    [Fact]
    public void ARaidTrialIsTheSameEachTimeForItsSeed()
    {
        var defs = TestWorld.LoadDefs();
        var a = CombatSweep.RaidTrial(defs, 3, party: true);
        var b = CombatSweep.RaidTrial(defs, 3, party: true);
        Assert.Equal(a with { RaiderBlows = null!, TownBlows = null!, Log = null! }, b with { RaiderBlows = null!, TownBlows = null!, Log = null! });
        Assert.Equal(a.Log, b.Log);
        Assert.Equal(a.RaiderBlows.ByOutcome, b.RaiderBlows.ByOutcome);
        Assert.Equal(a.TownBlows.ByOutcome, b.TownBlows.ByOutcome);
    }

    [Fact]
    public void ARaidOnTheTownIsFoughtOnBothSides()
    {
        var r = CombatSweep.RaidTrial(TestWorld.LoadDefs(), 1, party: true);
        output.WriteLine($"{r.Met} {r.Raiders} raiders [{r.Band}]: town {r.TownDead} dead; raiders {r.RaiderBlows}; town {r.TownBlows}");
        Assert.True(r.Raiders > 0);
        Assert.True(r.Met != null || r.RoundLost, "the raid ended within its hours");
        Assert.True(r.RaiderBlows.Total > 0 && r.TownBlows.Total > 0, "both sides swung");
    }

    [Fact]
    public void AWellArmedVillagerBeatsAScavengerMostTimes()
    {
        var defs = TestWorld.LoadDefs();
        var wins = Enumerable.Range(1, 6).Count(s => CombatSweep.Duel(defs, (ulong)s, "Scavenger", "IronSpear", 6, "ChainShirt", "IronHelm").VillagerWon == true);
        Assert.True(wins >= 4, $"won {wins} of 6");
    }

    [Fact]
    public void ADuelEndsOneWayOrTheOther()
    {
        var d = CombatSweep.Duel(TestWorld.LoadDefs(), 2, "Swordsman", "ShortSword");
        output.WriteLine($"{d.VillagerWon} after {d.Seconds:F1}s");
        Assert.NotNull(d.VillagerWon);
        Assert.InRange(d.Seconds, 1f, 60f);
    }
}
