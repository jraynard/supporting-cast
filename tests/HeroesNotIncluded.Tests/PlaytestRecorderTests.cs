using System.Text.Json;
using HeroesNotIncluded.Playtest;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Tests;

/// <summary>The playtest recorder: a round's messages, crises, notes and summary land in its folder for the tuning pass.</summary>
public class PlaytestRecorderTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    [Fact]
    public void ARoundLeavesItsMessagesCrisesNotesAndSummary()
    {
        var root = Path.Combine(Path.GetTempPath(), "hni-playtest-" + Guid.NewGuid().ToString("N"));
        try
        {
            var sim = VillageRound.NewSimulation(DefDatabase.LoadDirectory(DevPaths.Defs!), 1234);
            sim.Presets.PresetMode = true;
            VillageRound.Setup(sim);
            sim.Round.OpenLobby();
            sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default));
            sim.ApplyNow(new StartRoundCommand());
            var recorder = new PlaytestRecorder("round", root);
            Assert.Null(recorder.Folder); // nothing written yet: no folder

            RaidDrill.Stage(sim, Players.CharacterOf(sim, Ana));
            for (var i = 0; i < 120; i++)
            {
                sim.Tick();
                recorder.Update(sim, null);
            }
            recorder.Note(sim, "the bell was hard to hear", recorder.NextShot());
            sim.Round.End(sim, RoundOutcome.Dread);
            recorder.Update(sim, null);
            recorder.WriteSummary(sim, sim.Round.Summary!, new RoundSetup("solo", sim.Seed, "millbrook", "Normal", false, 1));

            var folder = recorder.Folder!;
            Assert.StartsWith(root, folder);
            Assert.Contains("began: Raid", File.ReadAllText(Path.Combine(folder, "crises.log")));
            Assert.Contains("Day 1", File.ReadAllText(Path.Combine(folder, "messages.log")));
            Assert.Contains("the bell was hard to hear (shot-001.png)", File.ReadAllText(Path.Combine(folder, "notes.md")));
            Assert.False(File.Exists(Path.Combine(folder, "net.log"))); // solo: no link to log

            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "summary.json")));
            Assert.Equal("Dread", json.RootElement.GetProperty("Summary"u8).GetProperty("Outcome"u8).GetString());
            Assert.Equal(1234ul, json.RootElement.GetProperty("Setup"u8).GetProperty("Seed"u8).GetUInt64());
            Assert.Contains(sim.Round.Summary!.Headline, File.ReadAllText(Path.Combine(folder, "summary.md")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void OnlyTheNewestSessionsAreKept()
    {
        var root = Path.Combine(Path.GetTempPath(), "hni-playtest-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            for (var i = 0; i < PlaytestRecorder.Keep + 5; i++)
                Directory.CreateDirectory(Path.Combine(root, $"20260101-{i:D6}-round"));
            var recorder = new PlaytestRecorder("practice", root);
            Assert.NotNull(recorder.NextShot());
            Assert.Equal(PlaytestRecorder.Keep, Directory.GetDirectories(root).Length);
            Assert.False(Directory.Exists(Path.Combine(root, "20260101-000000-round")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
