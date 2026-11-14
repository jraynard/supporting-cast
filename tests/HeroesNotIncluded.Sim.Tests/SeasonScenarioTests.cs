using HeroesNotIncluded.Sim.Bots;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Director;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Systems;
using Xunit.Abstractions;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// M3's exit: an Honest round played start to finish. Bots take every trade with a game, the
/// Director sends the season's crises, the town gathers for the festival, and the round ends with
/// a reveal. Prints how it went (run with <c>--logger "console;verbosity=detailed"</c>).
/// </summary>
public class SeasonScenarioTests(ITestOutputHelper output)
{
    [Fact]
    public void AnHonestRoundIsPlayableStartToFinish()
    {
        var sim = VillageRound.NewSimulation(TestWorld.LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Director.Enable(sim.Defs.Difficulty("Normal"));
        sim.Round.OpenLobby();
        var trades = new[] { "Innkeeper", "Cook", "Blacksmith", "Healer", "Tinker" };
        var bots = new List<BotPlayer>();
        for (var i = 0; i < trades.Length; i++)
        {
            var id = new Guid(i + 1, 0, 0, new byte[8]);
            sim.ApplyNow(new JoinLobbyCommand(id, $"Bot {i + 1}", default, []));
            sim.ApplyNow(new SetTradePreferencesCommand(id, [sim.Defs.Trade(trades[i])], []));
            bots.Add(new BotPlayer(() => sim, id, c => sim.ApplyNow(c), 100 + (ulong)i));
        }
        sim.ApplyNow(new StartRoundCommand());

        var festival = false;
        var deaths = new List<string>();
        var doing = new Dictionary<string, int>();
        var dead = new HashSet<Entity>();
        var crises = new List<CrisisKind>();
        var limit = sim.Round.EndTick(sim) + SimClock.TicksPerHour;
        while (sim.Round.Phase != RoundPhase.Over && sim.Clock.Tick < limit)
        {
            sim.Tick();
            foreach (var bot in bots)
                bot.Tick();
            festival |= sim.Round.Phase == RoundPhase.Festival;
            if (sim.Clock.Tick % 600 == 0)
                foreach (var b in bots)
                    doing[b.Doing.Split(' ')[0] + " " + (b.Doing.Split(' ').ElementAtOrDefault(1) ?? "")] = doing.GetValueOrDefault(b.Doing.Split(' ')[0] + " " + (b.Doing.Split(' ').ElementAtOrDefault(1) ?? "")) + 1;
            if (sim.Clock.Tick % 60 == 0)
                foreach (var e in sim.World.Query<Avatar>())
                    if (sim.World.Get<Creature>(e).Dead && dead.Add(e))
                        deaths.Add($"[{sim.Clock.TimeString()}] {sim.World.Get<Creature>(e).Name}: "
                            + string.Join(" | ", sim.Messages.Messages.TakeLast(6).Select(m => m.Text)));
            foreach (var c in sim.Director.Active)
                if (c.Started == sim.Clock.Tick)
                    crises.Add(c.Kind);
        }

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
        Assert.Contains(summary.People, p => p.Player);
    }
}
