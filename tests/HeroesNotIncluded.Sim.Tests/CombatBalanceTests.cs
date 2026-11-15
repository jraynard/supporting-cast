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

    /// <summary>
    /// The balance the season is tuned to (docs/HISTORY.md, "Combat balance"): an unattended town
    /// with heroes in it meets about half its day-two raids, without them about a quarter. Loose
    /// bounds over sixteen seeds: it fails when balance has moved a long way, not on a seed's luck.
    /// </summary>
    [Fact]
    public void RaidsAreMetAboutHalfTheTimeWithHeroesAndLessWithout()
    {
        var defs = TestWorld.LoadDefs();
        var seeds = Enumerable.Range(1, 16).Select(i => (ulong)i).ToArray();
        int Met(bool party) => seeds.AsParallel().Count(s => CombatSweep.RaidTrial(defs, s, party).Met == true);
        var with = Met(true);
        var without = Met(false);
        output.WriteLine($"met {with}/16 with a party, {without}/16 without");
        Assert.InRange(with, 4, 13);
        Assert.InRange(without, 1, 9);
        Assert.True(with >= without, "heroes in town help");
    }

    [Fact]
    public void AFighterSlipsMoreBlowsThanAFrightenedCook()
    {
        var defs = TestWorld.LoadDefs();
        var fighter = Dodges(defs, "ShortSword", 12);
        var cook = Dodges(defs, null, 6);
        output.WriteLine($"fighter dodged {fighter}, cook {cook}");
        Assert.True(fighter >= 2 * Math.Max(1, cook), $"a skilled fighter dodges far more than the unarmed: {fighter} against {cook}");
    }

    /// <summary>Of a Swordsman's blows at someone standing still, how many they dodged.</summary>
    private static int Dodges(Defs.DefDatabase defs, string? weapon, int skill)
    {
        var sim = Arena.NewSimulation(defs, 5);
        var center = Arena.Setup(sim);
        var w = sim.World;
        var target = sim.SpawnCreature(defs.Creature("Human"), "Corin", center, Components.Faction.Village, ageDays: 25 * 365);
        Array.Fill(w.Get<Components.Needs>(target).Values, 1f);
        Array.Fill(w.Get<Components.Skills>(target).Level, skill);
        if (weapon != null)
            sim.Equip(target, defs.Thing(weapon));
        w.Get<Components.Mind>(target).Drafted = true;
        var foe = Combat.Raids.SpawnRaider(sim, defs.AllRaiderKinds.First(k => k.Name == "Swordsman"), "Mott", center + new int3(1, 0, 0), sim.Rand);
        var dodged = 0;
        var seen = long.MinValue;
        for (var t = 0; t < SimClock.TicksPerSecond * 60; t++)
        {
            Arena.Freshen(sim, target); // never hurt enough to go down: just count
            sim.Tick();
            foreach (var b in sim.RecentBlows)
                if (b.Tick > seen && b.Attacker == foe && b.Outcome == Combat.BlowOutcome.Dodged)
                    dodged++;
            if (sim.RecentBlows.Count > 0)
                seen = sim.RecentBlows[^1].Tick;
        }
        return dodged;
    }
}
