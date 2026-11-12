using Narsil.Net;
using SupportingCast.Net;
using Brutal.Numerics;
using SupportingCast.Sim;
using SupportingCast.Sim.Bots;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Core;
using SupportingCast.Sim.Ecs;
using SupportingCast.Sim.Speech;
using SupportingCast.Sim.Round;
using SupportingCast.Sim.Systems;
using Table = SupportingCast.Tests.CoopSessionTests.Table;

namespace SupportingCast.Tests;

/// <summary>A round end to end over loopback: real sessions in a real lobby, dealt on every peer alike.</summary>
public class CoopRoundTests
{
    private static PlayerIdentity Person(string name) => new(Guid.NewGuid(), name);

    private static void Seat(CoopSession session, string trade)
    {
        session.Send(new JoinLobbyCommand(session.Me.Id, session.Me.Name, null, []));
        session.Send(new SetTradePreferencesCommand(session.Me.Id, [session.Defs.Trade(trade)], []));
    }

    /// <summary>A host and three players, seated and dealt. The session stands still until the deal, as the game runs it.</summary>
    private static (Table Table, CoopSession[] Players) Dealt()
    {
        var t = new Table(lobbyVillagers: 6) { Speed = 0 };
        t.Run(0.2f);
        var clients = new[] { t.Join(Person("Bea")), t.Join(Person("Cal")), t.Join(Person("Dee")) };
        t.Run(1f);
        Seat(t.Host, "Innkeeper");
        Seat(clients[0], "Blacksmith");
        Seat(clients[1], "Healer");
        Seat(clients[2], "Carpenter");
        t.Run(0.5f);
        t.Host.Send(new StartRoundCommand());
        t.Run(0.5f);
        return (t, [t.Host, .. clients]);
    }

    [Fact]
    public void FourPlayersTakeTheirTradesAndStayInStep()
    {
        var (t, players) = Dealt();
        t.Speed = SimClock.TicksPerSecond;
        t.Run(3f);
        t.AssertInStep();

        var expected = new[] { "Innkeeper", "Blacksmith", "Healer", "Carpenter" };
        for (var i = 0; i < players.Length; i++)
        {
            var sim = players[i].Sim!;
            Assert.Equal(RoundPhase.Playing, sim.Round.Phase);
            var me = Players.CharacterOf(sim, players[i].Me.Id);
            Assert.False(me.IsNull, $"{players[i].Me.Name} has no character");
            Assert.Equal(expected[i], sim.World.Get<Trade>(me).Def.Name);
        }
    }

    private static void PutAt(Simulation sim, Entity e, int3 cell)
    {
        sim.World.Get<Position>(e).Cell = cell;
        sim.World.Get<Position>(e).Pos = Position.CenterOf(cell);
    }

    /// <summary>The milestone's exit: four bots, each its own peer, playing a round with limited senses.</summary>
    [Fact]
    public void FourPlayersOnlySeeAndHearWhatTheirCharactersCan()
    {
        var (t, players) = Dealt();
        var bots = players.Select((p, i) => new BotPlayer(() => p.Sim!, p.Me.Id, p.Send, (ulong)i + 1)).ToList();
        t.Speed = SimClock.TicksPerSecond;
        t.Run(8f, () => bots.ForEach(b => b.Tick()));
        t.AssertInStep();
        Assert.NotEmpty(t.Host.Sim!.Speech.Kept); // the bots really played: they met and spoke

        // Say something on one peer and check who caught it, on another: every peer must agree.
        var host = t.Host.Sim!;
        var client = players[1].Sim!;
        var ids = players.Select(p => Players.CharacterOf(host, p.Me.Id)).ToArray();
        var ground = host.World.Get<Position>(ids[0]).Cell;
        var whisperer = ground;
        foreach (var sim in new[] { host, client, players[2].Sim!, players[3].Sim! })
        {
            PutAt(sim, ids[0], whisperer);
            PutAt(sim, ids[1], sim.SpiralFrom(whisperer, 1).First(c => c != whisperer && sim.Map.Passable(c) && SupportingCast.Sim.Ai.Pathfinder.Touches(c, whisperer)));
            PutAt(sim, ids[2], sim.SpiralFrom(whisperer + new int3(7, 0, 0), 5).First(c => sim.Map.Passable(c)));
            PutAt(sim, ids[3], sim.SpiralFrom(whisperer + new int3(40, 0, 0), 12).First(c => sim.Map.Passable(c)));
        }
        t.Speed = 0;
        t.Settle();
        players[0].Send(new SayCommand(ids[0], "it was the healer", Volume.Whisper));
        t.Run(0.5f);

        for (var i = 0; i < players.Length; i++)
        {
            var heard = Perception.For(players[i].Sim!, ids[i]).Heard().Where(l => l.Text == "it was the healer").ToList();
            Assert.True((i <= 1) == (heard.Count == 1), $"{players[i].Me.Name} heard {heard.Count} whispers");
        }

        var view = Perception.For(client, ids[1]);
        Assert.True(view.CanSee(ids[0]), "the one beside you is in plain sight");
        Assert.False(view.CanSee(ids[3]), "the one far across the valley is not");
    }

    [Fact]
    public void AClientCantStartTheRound()
    {
        var t = new Table(lobbyVillagers: 2) { Speed = 0 };
        t.Run(0.2f);
        var client = t.Join(Person("Bea"));
        t.Run(1f);
        Seat(client, "Healer");
        t.Run(0.5f);

        client.Send(new StartRoundCommand());
        t.Run(0.5f);

        Assert.Equal(RoundPhase.Lobby, t.Host.Sim!.Round.Phase);
    }

    [Fact]
    public void LeavingTheLobbyGivesUpTheSeat()
    {
        var t = new Table(lobbyVillagers: 2) { Speed = 0 };
        t.Run(0.2f);
        var client = t.Join(Person("Bea"));
        t.Run(1f);
        Seat(client, "Healer");
        t.Run(0.5f);
        Assert.Single(t.Host.Sim!.Round.Seats);

        client.Dispose();
        t.Clients.Remove(client);
        t.Run(1f);

        Assert.Empty(t.Host.Sim!.Round.Seats);
    }

    [Fact]
    public void ALateJoinerTakesOverAVillagerOnEveryPeer()
    {
        var (t, _) = Dealt();
        t.Speed = SimClock.TicksPerSecond;
        t.Run(1f);

        var late = t.Join(Person("Eve"));
        t.Run(1f);
        late.Send(new TakeTradeCommand(late.Me.Id, [], []));
        t.Run(1f);
        t.AssertInStep();

        var me = Players.CharacterOf(late.Sim!, late.Me.Id);
        Assert.False(me.IsNull);
        Assert.Equal(Players.CharacterOf(t.Host.Sim!, late.Me.Id), me);
    }
}
