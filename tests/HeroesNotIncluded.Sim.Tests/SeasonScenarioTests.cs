using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using Xunit.Abstractions;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// M3's exit: an Honest round played start to finish. Bots take every trade with a game, the
/// Director sends the season's crises, the town gathers for the festival, and the round ends with
/// a reveal. Prints how it went (run with <c>--logger "console;verbosity=detailed"</c>).
/// </summary>
public class SeasonScenarioTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(VillageRound.DefaultMap)]
    [InlineData(ReedmereMapTests.Map)] // the inn supplied from the cookhouse: the bots order, pack and carry its hampers
    public void AnHonestRoundIsPlayableStartToFinish(string map)
    {
        var played = SeasonSweep.Play(TestWorld.LoadDefs(), 1234, botSeed: 100, map: map);
        var (sim, festival, deaths, doing, crises) = (played.Sim, played.Festival, played.Deaths, played.Doing, played.Crises);

        var summary = sim.Round.Summary;
        var log = string.Join("\n", sim.Messages.Messages.TakeLast(25).Select(m => m.Text));
        Assert.True(summary != null, $"the round should end; it's {sim.Round.Phase} at {sim.Clock.TimeString()}\n{log}");
        output.WriteLine($"{summary.Headline} Renown {summary.Renown:0}/{summary.RenownTarget:0}, Dread {summary.Dread:0}/{summary.DreadLimit:0}");
        output.WriteLine($"Crises: {string.Join(", ", crises)}; met {sim.Director.Met}, unmet {sim.Director.Unmet}");
        output.WriteLine($"Parties {summary.Parties}, served {summary.AdventurersServed}, quests cleared {summary.QuestsCleared}, failed {summary.QuestsFailed}, lost {summary.AdventurersLost}");
        foreach (var p in summary.People)
            output.WriteLine($"  {p.Name,-10} {p.Trade,-11} {p.Earned,5:+0;-0;0} silver{(p.Alive ? "" : " (dead)")}");
        foreach (var d in deaths)
            output.WriteLine("  " + d);
        output.WriteLine($"Hampers {sim.Shops.Served(ServiceKind.Provisions)}");
        output.WriteLine("Bots doing (10 s samples): " + string.Join(", ", doing.OrderByDescending(kv => kv.Value).Take(14).Select(kv => $"{kv.Key}={kv.Value}")));

        Assert.NotEqual(RoundOutcome.None, summary.Outcome);
        if (summary.Outcome is RoundOutcome.Won or RoundOutcome.FellShort)
        {
            Assert.True(festival, "the season reached its festival");
            Assert.Contains(sim.Speech.Kept, u => u.Text.StartsWith("Friends! The season's over."));
            Assert.Contains(CrisisKind.Dragon, crises);
        }
        Assert.NotEmpty(crises);
        // Each crisis was met or not, or was still under way when Dread ended the round.
        Assert.Equal(crises.Count, sim.Director.Met + sim.Director.Unmet + sim.Director.Active.Count);
        Assert.True(summary.Parties > 0);
        if (map == ReedmereMapTests.Map)
            Assert.True(sim.Shops.Served(ServiceKind.Provisions) > 0, "the inn had a hamper from the cookhouse");
        Assert.Contains(summary.People, p => p.Player);
    }
}
