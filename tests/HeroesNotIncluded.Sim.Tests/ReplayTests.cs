using HeroesNotIncluded.Sim.Bots;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Replay;
using HeroesNotIncluded.Sim.Scenarios;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// Replays (<see cref="ReplayRecorder"/>, <see cref="ReplayPlayer"/>): a game recorded as it's
/// played comes out the same when played back, state hash for state hash.
/// </summary>
public class ReplayTests
{
    private static readonly string[] Trades = ["Innkeeper", "Cook", "Blacksmith", "Healer", "Tinker"];

    /// <summary>A bot round on <paramref name="map"/>, recorded from before its first tick, for <paramref name="hours"/> game hours.</summary>
    private static (Simulation Sim, ReplayRecorder Recorder) Record(string map, float hours, Action<Simulation, long>? between = null)
    {
        var sim = VillageRound.NewSimulation(LoadDefs(), 1234, map);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim, map);
        sim.Director.Enable(sim.Defs.Difficulty("Normal"));
        sim.Round.OpenLobby();
        var recorder = ReplayRecorder.Begin(sim, out var why);
        Assert.True(recorder != null, why);
        var bots = new List<BotPlayer>();
        for (var i = 0; i < Trades.Length; i++)
        {
            var id = new Guid(i + 1, 0, 0, new byte[8]);
            sim.ApplyNow(new JoinLobbyCommand(id, $"Bot {i + 1}", default));
            sim.ApplyNow(new SetTradePreferencesCommand(id, [sim.Defs.Trade(Trades[i])], []));
            bots.Add(new BotPlayer(() => sim, id, c => sim.ApplyNow(c), 100 + (ulong)i));
        }
        sim.ApplyNow(new StartRoundCommand());
        var end = sim.Clock.Tick + (long)(hours * SimClock.TicksPerHour);
        while (sim.Clock.Tick < end)
        {
            sim.Tick();
            foreach (var bot in bots)
                bot.Tick();
            between?.Invoke(sim, sim.Clock.Tick);
        }
        return (sim, recorder);
    }

    private static ReplayFile FileOf(ReplayRecorder recorder) =>
        recorder.ToFile("test", new Dictionary<string, string> { ["map"] = "test" });

    [Theory]
    [InlineData(VillageRound.DefaultMap)]
    [InlineData(ReedmereMapTests.Map)]
    public void ABotRoundPlaysBackTheSame(string map)
    {
        var (live, recorder) = Record(map, 3f);
        Assert.Null(recorder.Spoiled);
        Assert.NotEmpty(recorder.Checks);
        Assert.Contains(recorder.Events, e => e.Kind == ReplayEventKind.Command);

        var player = new ReplayPlayer(FileOf(recorder), live.Defs);
        player.Seek(long.MaxValue);
        Assert.True(player.Ended);
        Assert.Null(player.DriftedAt);
        Assert.Equal(live.Clock.Tick, player.Sim.Clock.Tick);
        Assert.Equal(live.StateHash(), player.Sim.StateHash());
        Assert.Equal(live.FullHash(), player.Sim.FullHash());
    }

    [Fact]
    public void SeekingBackStartsOverAndLandsTheSame()
    {
        var (_, recorder) = Record(VillageRound.DefaultMap, 1f);
        var player = new ReplayPlayer(FileOf(recorder), LoadDefs());
        var mid = recorder.StartTick + (recorder.EndTick - recorder.StartTick) / 2;
        player.Seek(mid);
        var there = player.Sim.StateHash();
        player.Seek(recorder.EndTick);
        var before = player.Sim;
        player.Seek(mid);
        Assert.NotSame(before, player.Sim); // back means from the start
        Assert.Equal(mid, player.Tick);
        Assert.Equal(there, player.Sim.StateHash());
        Assert.Null(player.DriftedAt);
    }

    [Fact]
    public void CoopEventsPlayBackTheSame()
    {
        var late = new Guid(99, 0, 0, new byte[8]);
        var (live, recorder) = Record(VillageRound.DefaultMap, 1f, (sim, tick) =>
        {
            // A joiner (and the barrier before their snapshot), someone stepping away and leaving.
            if (tick == 600)
            {
                PeerEvents.Barrier(sim);
                PeerEvents.Joined(sim, late, isHost: false);
            }
            if (tick == 1200)
                PeerEvents.Away(sim, new Guid(2, 0, 0, new byte[8]));
            if (tick == 1800)
                PeerEvents.Left(sim, new Guid(3, 0, 0, new byte[8]));
        });
        Assert.Contains(recorder.Events, e => e.Kind == ReplayEventKind.Barrier);
        Assert.Contains(recorder.Events, e => e.Kind == ReplayEventKind.Joined && e.Player == late);
        Assert.Contains(recorder.Events, e => e.Kind == ReplayEventKind.Left);

        var player = new ReplayPlayer(FileOf(recorder), live.Defs);
        player.Seek(long.MaxValue);
        Assert.Null(player.DriftedAt);
        Assert.Equal(live.StateHash(), player.Sim.StateHash());
        Assert.Equal(live.FullHash(), player.Sim.FullHash());
    }

    [Fact]
    public void AReplayFileRoundTrips()
    {
        var (_, recorder) = Record(VillageRound.DefaultMap, 0.25f);
        recorder.Mark(recorder.StartTick + 60, "A fire at the inn");
        var file = recorder.ToFile("build-1", new Dictionary<string, string> { ["map"] = "Millbrook", ["players"] = "Bot 1, Bot 2" });
        var bytes = file.ToBytes();

        var back = ReplayFile.Read(bytes);
        Assert.Equal("build-1", back.BuildTag);
        Assert.Equal("Millbrook", back.Meta["map"]);
        Assert.Equal(file.StartTick, back.StartTick);
        Assert.Equal(file.EndTick, back.EndTick);
        Assert.Equal(file.Start, back.Start);
        Assert.Equal(file.Events.Length, back.Events.Length);
        for (var i = 0; i < file.Events.Length; i++)
        {
            Assert.Equal(file.Events[i].Tick, back.Events[i].Tick);
            Assert.Equal(file.Events[i].Kind, back.Events[i].Kind);
            Assert.Equal(file.Events[i].Command, back.Events[i].Command);
        }
        Assert.Equal(file.Checks, back.Checks);
        Assert.Equal(file.Markers, back.Markers);

        using var head = new MemoryStream(bytes);
        Assert.Equal(file.EndTick, ReplayFile.ReadHeader(head).EndTick);
        Assert.Throws<InvalidDataException>(() => ReplayFile.Read(bytes[..(bytes.Length / 2)]));
        Assert.Throws<InvalidDataException>(() => ReplayFile.Read("not a replay at all"u8.ToArray()));
    }

    [Fact]
    public void RecordingChangesNothing()
    {
        // The same round with and without a recorder: the state's the same.
        var (recorded, _) = Record(VillageRound.DefaultMap, 0.5f);
        var sim = VillageRound.NewSimulation(LoadDefs(), 1234, VillageRound.DefaultMap);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim, VillageRound.DefaultMap);
        sim.Director.Enable(sim.Defs.Difficulty("Normal"));
        sim.Round.OpenLobby();
        var bots = new List<BotPlayer>();
        for (var i = 0; i < Trades.Length; i++)
        {
            var id = new Guid(i + 1, 0, 0, new byte[8]);
            sim.ApplyNow(new JoinLobbyCommand(id, $"Bot {i + 1}", default));
            sim.ApplyNow(new SetTradePreferencesCommand(id, [sim.Defs.Trade(Trades[i])], []));
            bots.Add(new BotPlayer(() => sim, id, c => sim.ApplyNow(c), 100 + (ulong)i));
        }
        sim.ApplyNow(new StartRoundCommand());
        while (sim.Clock.Tick < recorded.Clock.Tick)
        {
            sim.Tick();
            foreach (var bot in bots)
                bot.Tick();
        }
        Assert.Equal(sim.FullHash(), recorded.FullHash());
    }

    [Fact]
    public void QueuedCommandsSpoilIt()
    {
        var (sim, _) = Millbrook();
        var recorder = ReplayRecorder.Begin(sim, out _)!;
        sim.Enqueue(new StartRoundCommand());
        sim.Tick();
        Assert.NotNull(recorder.Spoiled);
    }
}
