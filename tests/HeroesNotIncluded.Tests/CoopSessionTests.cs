using Brutal.Numerics;
using Narsil.Net;
using HeroesNotIncluded.Net;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Tests;

/// <summary>
/// Co-op end to end, in-process: a real village hosted over a loopback listener, players joining
/// with their own characters, leaving and coming back, all while peers stay in step.
/// </summary>
public class CoopSessionTests
{
    internal static readonly Lazy<DefDatabase> Defs = new(() => DefDatabase.LoadDirectory(DevPaths.Defs!));

    /// <summary>Millbrook, the map rounds are played on, with <paramref name="villagers"/> AI villagers and no round open.</summary>
    internal static (Simulation Sim, int3 Center) Millbrook(ulong seed, int villagers)
    {
        var map = Defs.Value.Map(VillageRound.DefaultMap);
        var sim = MapBuilder.CreateSimulation(Defs.Value, map, seed);
        return (sim, MapBuilder.Build(sim, map, new MapGenerator.Options { Villagers = villagers }).Center);
    }

    internal sealed class Table
    {
        public readonly LoopbackListener Listener = new();
        public readonly CoopSession Host;
        public readonly List<CoopSession> Clients = [];
        public float Speed = SimClock.TicksPerSecond;

        /// <param name="lobbyVillagers">Set: open a round's lobby over this many villagers, instead of a hero walking in.</param>
        public Table(int? lobbyVillagers = null)
        {
            var (sim, center) = Millbrook(77, lobbyVillagers ?? 1);
            if (lobbyVillagers == null)
                VillageRound.SpawnHero(sim, new Hero("Hosta", null, null), center);
            else
                sim.Round.OpenLobby();
            Host = CoopSession.StartHost(sim, new PlayerIdentity(Guid.NewGuid(), "host"), "test", Listener);
        }

        /// <summary>The joiner's current link, so a test can cut it.</summary>
        public INetConnection? Link;

        public CoopSession Join(PlayerIdentity who, bool canReconnect = false)
        {
            Link = Listener.Connect();
            var c = CoopSession.Join(Defs.Value, who, Link, canReconnect ? _ => Task.FromResult(Link = Listener.Connect()) : null);
            Clients.Add(c);
            return c;
        }

        public void Run(float seconds, Action? each = null)
        {
            const float dt = 1 / 60f;
            for (var i = 0; i < (int)(seconds / dt); i++)
            {
                each?.Invoke();
                Host.Update(dt, Speed);
                foreach (var c in Clients)
                    c.Update(dt, 0);
            }
        }

        /// <summary>Pause and let every queued turn play out.</summary>
        public void Settle()
        {
            var speed = Speed;
            Speed = 0;
            Run(1f);
            Speed = speed;
        }

        public void AssertInStep()
        {
            Settle();
            foreach (var c in Clients)
            {
                Assert.True(c.Ready, c.Ended ?? "not ready");
                Assert.Equal(Host.Sim!.Clock.Tick, c.Sim!.Clock.Tick);
                Assert.Equal(Host.Sim.StateHash(), c.Sim.StateHash());
            }
            Assert.Equal(0, Host.Host!.Desyncs);
        }
    }

    [Fact]
    public void ADesyncResyncsAndKeepsBothStatesForDiagnosis()
    {
        var dumps = Path.Combine(Path.GetTempPath(), "dw-desync-" + Guid.NewGuid().ToString("N"));
        var before = CoopSession.DumpDirectory;
        CoopSession.DumpDirectory = dumps;
        try
        {
            var t = new Table();
            t.Run(0.5f);
            var client = t.Join(new PlayerIdentity(Guid.NewGuid(), "Bea"));
            t.Run(1f);
            Assert.True(client.Ready);
            client.Sim!.Rand.NextUInt(); // knock the client off the host's state
            t.Run(4f);

            Assert.True(t.Host.Host!.Desyncs >= 1);
            var files = Directory.GetFiles(dumps).Select(Path.GetFileName).ToArray();
            Assert.Contains(files, f => f!.EndsWith("-host" + SaveSlots.Extension));
            Assert.Contains(files, f => f!.EndsWith("-mine" + SaveSlots.Extension));
            t.Settle(); // and the resync put them back in step
            Assert.Equal(t.Host.Sim!.StateHash(), client.Sim!.StateHash());
        }
        finally
        {
            CoopSession.DumpDirectory = before;
            if (Directory.Exists(dumps))
                Directory.Delete(dumps, recursive: true);
        }
    }

    [Fact]
    public void APredictedWalkStopsWhereThePlayerSawItStop()
    {
        var t = new Table();
        t.Run(0.5f);
        var sim = t.Host.Sim!;
        var me = Players.CharacterOf(sim, t.Host.Me.Id);
        var predictor = new AvatarPredictor();
        void Frame() => predictor.Update(sim, me, 1 / 60f, 1);
        var east = new int2(1, 0);
        var start = sim.World.Get<Position>(me).Cell;
        t.Host.Send(new AvatarMoveCommand(me, east, predictor.DirectionChanged(sim, me, east)));
        Assert.True(predictor.Active);
        t.Run(1.2f, Frame);
        // Let go: the picture stops at once, and the command names the cell it stopped on.
        var stop = predictor.DirectionChanged(sim, me, int2.Zero);
        Assert.NotNull(stop);
        Assert.True(stop!.Value.X > start.X + 1, $"walked east from {start} to {stop}");
        t.Host.Send(new AvatarMoveCommand(me, int2.Zero, stop));
        t.Run(2f, Frame);
        Assert.Equal(stop, sim.World.Get<Position>(me).Cell);
        Assert.False(predictor.Active); // handed back to the sim once it arrived
        t.AssertInStep();
    }

    [Fact]
    public void HostAdoptsTheirAvatar()
    {
        var t = new Table();
        t.Run(0.2f);
        var me = Players.CharacterOf(t.Host.Sim!, t.Host.Me.Id);
        Assert.False(me.IsNull);
        Assert.Equal("Hosta", t.Host.Sim!.World.Get<Creature>(me).Name);
    }

    [Fact]
    public void ChatReachesEveryoneWithoutTouchingTheSimulation()
    {
        var t = new Table();
        t.Run(1f);
        var client = t.Join(new PlayerIdentity(Guid.NewGuid(), "Bea"));
        t.Run(1f);
        var heard = new List<string>();
        client.ChatReceived += (from, text) => heard.Add($"{from}: {text}");

        client.SendChat("is the forge lit?");
        t.Run(0.5f);
        t.Host.SendChat("by the fire");
        t.Run(0.5f);

        Assert.Equal(["Bea: is the forge lit?", "host: by the fire"], heard);
        Assert.Equal(heard, t.Host.ChatLog.Select(l => $"{l.From}: {l.Text}"));
        t.AssertInStep();
    }

    [Fact]
    public void AFreeWalkPredictedOnAClientStopsWhereThePlayerSawItStop()
    {
        var t = new Table();
        t.Host.Sim!.FreeMove.Enable(t.Host.Sim);
        t.Run(1f);
        var bea = new PlayerIdentity(Guid.NewGuid(), "Bea");
        var client = t.Join(bea);
        t.Run(1f);
        client.Send(new CreateCharacterCommand(bea.Id, "Bea", new Appearance(), ["Kind"]));
        t.Run(1.5f);
        var sim = client.Sim!;
        var mine = Players.CharacterOf(sim, bea.Id);
        Assert.True(sim.World.Has<Sim.Combat.FreeMover>(mine), "the joiner moves freely too");
        var start = sim.World.Get<Position>(mine).Pos;

        // Walk east, predicted, sending where we are now and then as the game does.
        var predictor = new FreeMovePredictor();
        var wish = new float2(1, 0);
        var since = 0f;
        client.Send(new FreeMoveCommand(mine, wish.X, wish.Y, 0f));
        t.Run(1f, () =>
        {
            predictor.Update(sim, mine, wish, 1 / 60f, 1);
            if ((since += 1 / 60f) > 0.2f && predictor.Active)
            {
                since = 0;
                client.Send(new FreeMoveCommand(mine, wish.X, wish.Y, 0f, predictor.Pos.X, predictor.Pos.Y, predictor.Level));
            }
        });
        Assert.True(predictor.Active);
        // Let go: the picture stops at once, and the stop says where.
        predictor.Update(sim, mine, float2.Zero, 1 / 60f, 1);
        var seen = predictor.Pos;
        Assert.True(seen.X > start.X + 1f, $"walked east from {start} to {seen}");
        client.Send(new FreeMoveCommand(mine, 0f, 0f, 0f, seen.X, seen.Y, predictor.Level));
        t.Run(1.5f, () => predictor.Update(sim, mine, float2.Zero, 1 / 60f, 1));
        var hostAt = t.Host.Sim.World.Get<Position>(Players.CharacterOf(t.Host.Sim, bea.Id)).Pos;
        Assert.True(MathF.Abs(hostAt.X - seen.X) < 0.4f && MathF.Abs(hostAt.Y - seen.Y) < 0.1f, $"the host has them at {hostAt}, they saw {seen}");
        Assert.False(predictor.Active, "handed back to the sim once it arrived");
        t.AssertInStep();
    }

    [Fact]
    public void ADroppedPlayerStopsWhereTheyAreAndPicksUpOnReconnecting()
    {
        var t = new Table();
        t.Run(1f);
        var bea = new PlayerIdentity(Guid.NewGuid(), "Bea");
        var client = t.Join(bea, canReconnect: true);
        t.Run(1f);
        client.Send(new CreateCharacterCommand(bea.Id, "Bea", new Appearance(), ["Kind"]));
        t.Run(1f);
        var mine = Players.CharacterOf(t.Host.Sim!, bea.Id);
        client.Send(new AvatarMoveCommand(Players.CharacterOf(client.Sim!, bea.Id), new int2(1, 0)));
        t.Run(0.5f);

        t.Link!.Close("wifi dropped");
        t.Run(0.2f);
        Assert.Equal(int2.Zero, t.Host.Sim!.World.Get<Avatar>(mine).MoveDir); // not walking off unattended
        Assert.True(t.Host.Sim.World.Has<Avatar>(mine), "the seat is held, not handed to the AI");
        var stoppedAt = t.Host.Sim.World.Get<Position>(mine).Cell;
        t.Run(3f);

        Assert.Equal(1, client.Client!.Reconnects);
        Assert.Equal(stoppedAt, t.Host.Sim.World.Get<Position>(mine).Cell);
        t.AssertInStep();
    }

    [Fact]
    public void JoinerCreatesACharacterWithAnEggAndEveryoneAgrees()
    {
        var t = new Table();
        t.Run(1f);
        var bea = new PlayerIdentity(Guid.NewGuid(), "Bea");
        var client = t.Join(bea);
        t.Run(1f);
        Assert.True(client.Ready);
        Assert.True(Players.CharacterOf(client.Sim!, bea.Id).IsNull, "a newcomer has no character yet");

        client.Send(new CreateCharacterCommand(bea.Id, "Bea", new Appearance { Hair = HairStyle.Long }, ["Kind"]));
        t.Run(1f);
        var mine = Players.CharacterOf(client.Sim!, bea.Id);
        Assert.False(mine.IsNull);

        // Walk about and give village orders from both sides.
        client.Send(new AvatarMoveCommand(mine, new int2(1, 0)));
        var host = Players.CharacterOf(t.Host.Sim!, t.Host.Me.Id);
        t.Host.Send(new AvatarMoveCommand(host, new int2(0, 1)));
        t.Run(3f);
        client.Send(new AvatarMoveCommand(mine, int2.Zero));
        t.Host.Send(new AvatarMoveCommand(host, int2.Zero));
        t.AssertInStep();
    }

    [Fact]
    public void HostingKeepsTheStoryteller()
    {
        var (sim, center) = Millbrook(5, 1);
        VillageRound.SpawnHero(sim, new Hero("Hosta", null, null), center);
        sim.Storyteller.SetDifficulty(sim, Defs.Value.Difficulty("Normal"));
        using var host = CoopSession.StartHost(sim, new PlayerIdentity(Guid.NewGuid(), "host"), "raids", new LoopbackListener());
        Assert.NotNull(host.Sim!.Storyteller.Difficulty); // raids come to co-op villages too
    }

    [Fact]
    public void ReturningPlayerGetsTheirCharacterBack()
    {
        var t = new Table();
        var bea = new PlayerIdentity(Guid.NewGuid(), "Bea");
        var first = t.Join(bea);
        t.Run(1f);
        first.Send(new CreateCharacterCommand(bea.Id, "Bea", default, []));
        t.Run(1f);
        var mine = Players.CharacterOf(t.Host.Sim!, bea.Id);

        first.Dispose();
        t.Clients.Clear();
        t.Run(1f);
        Assert.False(t.Host.Sim!.World.Has<Avatar>(mine), "away: a villager now");

        var again = t.Join(bea);
        t.Run(1.5f);
        Assert.True(again.Ready);
        Assert.Equal(mine, Players.CharacterOf(again.Sim!, bea.Id));
        Assert.True(again.Sim!.World.Has<Avatar>(mine));
        t.AssertInStep();
    }

    [Fact]
    public void LateJoinerStaysInStepWithAnEarlierOne()
    {
        var t = new Table() { Speed = SimClock.TicksPerSecond * 6 };
        var a = new PlayerIdentity(Guid.NewGuid(), "A");
        var ca = t.Join(a);
        t.Run(1f);
        ca.Send(new CreateCharacterCommand(a.Id, "A", default, []));
        t.Run(4f); // a few game minutes of village life at 6x
        var b = new PlayerIdentity(Guid.NewGuid(), "B");
        t.Join(b);
        t.Run(4f);
        t.AssertInStep();
        Assert.Equal(3, t.Host.Players.Count);
    }

    [Fact]
    public async Task WorksOverRealSockets()
    {
        var (sim, center) = Millbrook(5, 1);
        VillageRound.SpawnHero(sim, new Hero("Hosta", null, null), center);
        var listener = new TcpNetListener(0, System.Net.IPAddress.Loopback);
        using var host = CoopSession.StartHost(sim, new PlayerIdentity(Guid.NewGuid(), "host"), "tcp", listener, listener.Port);
        var bea = new PlayerIdentity(Guid.NewGuid(), "Bea");
        using var client = CoopSession.Join(Defs.Value, bea, await TcpNetConnection.ConnectAsync("127.0.0.1", listener.Port));

        void Pump(float seconds, float speed)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var last = 0.0;
            while (clock.Elapsed.TotalSeconds < seconds)
            {
                var now = clock.Elapsed.TotalSeconds;
                var dt = (float)(now - last);
                last = now;
                host.Update(dt, speed);
                client.Update(dt, 0);
                Thread.Sleep(2);
            }
        }

        Pump(1.5f, SimClock.TicksPerSecond * 3);
        Assert.True(client.Ready, client.Ended ?? "never got the village");
        client.Send(new CreateCharacterCommand(bea.Id, "Bea", default, []));
        Pump(1f, SimClock.TicksPerSecond * 3);
        Pump(1.5f, 0); // pause and let everything arrive
        Assert.False(Players.CharacterOf(client.Sim!, bea.Id).IsNull);
        Assert.Equal(host.Sim!.Clock.Tick, client.Sim!.Clock.Tick);
        Assert.Equal(host.Sim.StateHash(), client.Sim.StateHash());
    }

    [Fact]
    public async Task HostsOnlineThroughARelayAndFriendsJoinByCode()
    {
        using var relayListener = new TcpNetListener(0, System.Net.IPAddress.Loopback);
        using var relay = new RelayServer(relayListener);
        relay.Start();
        CoopRelay.Override = $"127.0.0.1:{relayListener.Port}";
        try
        {
            var (sim, center) = Millbrook(5, 1);
            VillageRound.SpawnHero(sim, new Hero("Hosta", null, null), center);
            using var host = CoopSession.StartHost(sim, new PlayerIdentity(Guid.NewGuid(), "host"), "online", port: 0, online: true);
            CoopSession? client = null;

            void Pump(float seconds, float speed, Func<bool>? until = null)
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var last = 0.0;
                while (clock.Elapsed.TotalSeconds < seconds && until?.Invoke() != true)
                {
                    var now = clock.Elapsed.TotalSeconds;
                    var dt = (float)(now - last);
                    last = now;
                    host.Update(dt, speed);
                    client?.Update(dt, 0);
                    Thread.Sleep(2);
                }
            }

            Pump(5f, SimClock.TicksPerSecond, () => host.JoinCode != null);
            Assert.NotNull(host.JoinCode);
            Assert.True(CoopRelay.TryGet(out var relayHost, out var relayPort));
            var bea = new PlayerIdentity(Guid.NewGuid(), "Bea");
            client = CoopSession.Join(Defs.Value, bea,
                await RelayClient.JoinAsync(relayHost, relayPort, CoopSession.GameId, host.JoinCode!.ToLowerInvariant()));

            Pump(5f, SimClock.TicksPerSecond * 3, () => client.Ready);
            Assert.True(client.Ready, client.Ended ?? "never got the village");
            client.Send(new CreateCharacterCommand(bea.Id, "Bea", default, []));
            Pump(1f, SimClock.TicksPerSecond * 3);
            Pump(1.5f, 0);
            Assert.False(Players.CharacterOf(client.Sim!, bea.Id).IsNull);
            Assert.Equal(host.Sim!.StateHash(), client.Sim!.StateHash());
            Assert.Equal(1, relay.JoinerCount);
            client.Dispose();
        }
        finally
        {
            CoopRelay.Override = null;
        }
    }

    [Fact]
    public async Task WhenTheOnlineHostVanishesAPlayerTakesOverAndTheOtherRejoins()
    {
        using var relayListener = new TcpNetListener(0, System.Net.IPAddress.Loopback);
        using var relay = new RelayServer(relayListener);
        relay.Start();
        CoopRelay.Override = $"127.0.0.1:{relayListener.Port}";
        try
        {
            var (sim, center) = Millbrook(5, 1);
            VillageRound.SpawnHero(sim, new Hero("Hosta", null, null), center);
            var host = CoopSession.StartHost(sim, new PlayerIdentity(Guid.NewGuid(), "host"), "online", port: 0, online: true);
            var peers = new List<CoopSession>();
            var hostAlive = true;
            var speed = SimClock.TicksPerSecond * 3f;

            void Pump(float seconds, Func<bool>? until = null)
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var last = 0.0;
                while (clock.Elapsed.TotalSeconds < seconds && until?.Invoke() != true)
                {
                    var now = clock.Elapsed.TotalSeconds;
                    var dt = (float)(now - last);
                    last = now;
                    if (hostAlive)
                        host.Update(dt, speed);
                    foreach (var p in peers)
                        p.Update(dt, p.IsHost ? speed : 0);
                    Thread.Sleep(2);
                }
            }

            Pump(5f, () => host.JoinCode != null);
            var code = host.JoinCode!;
            Assert.True(CoopRelay.TryGet(out var relayHost, out var relayPort));
            foreach (var name in new[] { "Bea", "Cal" })
            {
                var me = new PlayerIdentity(Guid.NewGuid(), name);
                peers.Add(CoopSession.Join(Defs.Value, me,
                    await RelayClient.JoinAsync(relayHost, relayPort, CoopSession.GameId, code),
                    ct => RelayClient.JoinAsync(relayHost, relayPort, CoopSession.GameId, code, ct)));
                Pump(5f, () => peers[^1].Ready);
                peers[^1].Send(new CreateCharacterCommand(me.Id, name, default, []));
            }
            Pump(1.5f);
            Assert.All(peers, p => Assert.NotNull(p.Client!.HandoverData));

            var tookOver = 0;
            foreach (var p in peers)
                p.BecameHost += () => tookOver++;
            hostAlive = false;
            host.VanishForTests();
            Pump(15f, () => peers.Count(p => p.IsHost) == 1 && peers.Single(p => !p.IsHost).Client!.State == LockstepClient.Phase.Playing);

            Assert.Equal(1, tookOver);
            var newHost = peers.Single(p => p.IsHost);
            var other = peers.Single(p => !p.IsHost);
            Assert.Equal("Bea", newHost.Me.Name); // first in line
            Assert.Equal(code, newHost.JoinCode);
            Pump(1.5f); // play on a little under the new host
            speed = 0;
            Pump(1.5f); // then pause and let every turn arrive
            Assert.Equal(newHost.Sim!.Clock.Tick, other.Sim!.Clock.Tick);
            Assert.Equal(newHost.Sim.StateHash(), other.Sim.StateHash());
            Assert.Equal(0, newHost.Host!.Desyncs);
            // Bea's character is hers to steer as host; the old host's went back to village life.
            Assert.False(Players.CharacterOf(newHost.Sim, newHost.Me.Id).IsNull);
            foreach (var p in peers)
                p.Dispose();
        }
        finally
        {
            CoopRelay.Override = null;
        }
    }
}
