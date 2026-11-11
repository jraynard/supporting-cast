using SupportingCast.Sim.Bots;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Errands;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Round;
using SupportingCast.Sim.Scenarios;
using SupportingCast.Sim.Systems;
using Xunit.Abstractions;

namespace SupportingCast.Sim.Tests;

/// <summary>
/// M2's exit, played from the players' side of the line: three bots take the three trades, parties
/// arrive, are served (drink, mending, healing), are sent on a quest, and come back changed. Bots
/// play only through commands and Perception, so this proves the loop is playable by people.
/// </summary>
public class PartyLoopScenarioTests(ITestOutputHelper output)
{
    private static readonly string[] Trades = ["Blacksmith", "Innkeeper", "Healer"];

    [Theory]
    [InlineData(1234UL)]
    [InlineData(99UL)]
    public void APartyIsServedSentOutAndComesBackChanged(ulong seed)
    {
        var sim = Simulation.CreateStandard(TestWorld.LoadDefs(), 160, 160, seed, MapGenerator.RecommendedDepth);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        var bots = new List<BotPlayer>();
        for (var i = 0; i < Trades.Length; i++)
        {
            var id = new Guid(i + 1, 0, 0, new byte[8]);
            sim.ApplyNow(new JoinLobbyCommand(id, $"Bot {Trades[i]}", default, []));
            sim.ApplyNow(new SetTradePreferencesCommand(id, [sim.Defs.Trade(Trades[i])], []));
            bots.Add(new BotPlayer(() => sim, id, c => sim.ApplyNow(c), seed + (ulong)i));
        }
        sim.ApplyNow(new StartRoundCommand());
        var players = bots.Select(b => Players.CharacterOf(sim, b.Player)).ToHashSet();
        for (var i = 0; i < Trades.Length; i++)
            Assert.Equal(Trades[i], sim.World.Get<Trade>(Players.CharacterOf(sim, bots[i].Player)).Def.Name);

        // Served by a bot, per kind; and how the round went.
        var served = new HashSet<ErrandKind>();
        var log = new List<string>();
        var end = SimClock.TicksPerDay * 3 / 2;
        for (var t = 0; t < end; t++)
        {
            sim.Tick();
            foreach (var bot in bots)
                bot.Tick();
            if (t % 60 != 0)
                continue;
            foreach (var e in sim.Errands.All)
                if (e.Status == ErrandStatus.Done && players.Contains(e.Assignee) && served.Add(e.Kind.Kind))
                    log.Add($"{SimClock.TicksToDays(t) * 24:F1}h: {e.AssigneeName} did {e.Kind.Label} for {e.PosterName}");
            if (served.Count >= 4 && sim.Expeditions.Succeeded + sim.Expeditions.Failed > 0)
                break;
        }

        var errands = sim.Errands.All.GroupBy(e => (e.Kind.Kind, e.Status)).Select(g => $"{g.Key.Kind}/{g.Key.Status}: {g.Count()}");
        var summary = string.Join("\n", log) + $"\nerrands: {string.Join(", ", errands)}\nparties {sim.Parties.PartiesSent}, out {sim.Expeditions.Out.Count}, "
            + $"succeeded {sim.Expeditions.Succeeded}, failed {sim.Expeditions.Failed}, dead {sim.Expeditions.Dead}\n"
            + string.Join("\n", sim.Messages.Messages.TakeLast(25).Select(m => m.Text));
        output.WriteLine(summary);

        Assert.True(sim.Parties.PartiesSent >= 1, summary);
        foreach (var kind in new[] { ErrandKind.Drink, ErrandKind.Repair, ErrandKind.Quest })
            Assert.True(served.Contains(kind), $"no bot served {kind}\n{summary}");
        Assert.True(sim.Expeditions.Succeeded + sim.Expeditions.Failed >= 1, $"no party came back from a quest\n{summary}");
        Assert.True(sim.Messages.Messages.Any(m => m.Text.Contains("return from") || m.Text.StartsWith("No one comes back")), summary);
    }
}
