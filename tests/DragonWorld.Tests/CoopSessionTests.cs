using Brutal.Numerics;
using Narsil.Net;
using DragonWorld.Net;
using DragonWorld.Sim;
using DragonWorld.Sim.Combat;
using DragonWorld.Sim.Components;
using DragonWorld.Sim.Core;
using DragonWorld.Sim.Defs;
using DragonWorld.Sim.Maps;
using DragonWorld.Sim.Scenarios;
using DragonWorld.Sim.Systems;

namespace DragonWorld.Tests;

/// <summary>
/// Co-op end to end, in-process: a real village hosted over a loopback listener, players joining
/// with their own characters and eggs, leaving and coming back, all while peers stay in step.
/// </summary>
public class CoopSessionTests
{
    private static readonly Lazy<DefDatabase> Defs = new(() => DefDatabase.LoadDirectory(DevPaths.Defs!));

    private sealed class Table
    {
        public readonly LoopbackListener Listener = new();
        public readonly CoopSession Host;
        public readonly List<CoopSession> Clients = [];
        public float Speed = SimClock.TicksPerSecond;

        public Table()
        {
            var sim = Simulation.CreateStandard(Defs.Value, 64, 64, 77, MapGenerator.RecommendedDepth);
            var center = MapGenerator.Generate(sim, new MapGenerator.Options { Villagers = 1 });
            sim.RebuildDerivedIfNeeded();
            SliceDemo.ApplyVillageDefaults(sim, center);
            EggBearerStart.Setup(sim, "Hosta");
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

        client.SendChat("is the egg warm?");
        t.Run(0.5f);
        t.Host.SendChat("by the fire");
        t.Run(0.5f);

        Assert.Equal(["Bea: is the egg warm?", "host: by the fire"], heard);
        Assert.Equal(heard, t.Host.ChatLog.Select(l => $"{l.From}: {l.Text}"));
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
        Assert.True(client.Sim!.World.Has<Egg>(client.Sim.World.Get<Carrying>(mine).Item));

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
    public void AFightPlaysOutAlikeOnEveryPeerAndWaitsForEveryPlayersTurn()
    {
        var t = new Table();
        t.Run(1f);
        var bea = new PlayerIdentity(Guid.NewGuid(), "Bea");
        var client = t.Join(bea);
        t.Run(1f);
        client.Send(new CreateCharacterCommand(bea.Id, "Bea", default, []));
        t.Run(1f);
        var sim = t.Host.Sim!;
        var host = Players.CharacterOf(sim, t.Host.Me.Id);
        var mine = Players.CharacterOf(client.Sim!, bea.Id);
        Assert.False(mine.IsNull);

        // Raiders turn up beside the village (dev command: the storyteller is off in co-op).
        client.Send(new DevRaidCommand(Defs.Value.AllRaiderKinds[0], 2, sim.World.Get<Position>(host).Cell + new int3(8, 0, 0)));
        t.Run(1f);
        Assert.False(sim.Combat.Active, "only the host may send developer commands");
        t.Host.Send(new DevRaidCommand(Defs.Value.AllRaiderKinds[0], 2, sim.World.Get<Position>(host).Cell + new int3(8, 0, 0)));
        t.Run(2f);
        Assert.True(sim.Combat.Active, "the raiders were seen");
        Assert.True(client.Sim!.Combat.Active);
        Assert.Equal(Side.Village, sim.Combat.Phase);
        Assert.NotNull(sim.Combat.Unit(mine));

        // Bea may not order the host's character about.
        var hostTu = sim.Combat.Unit(host)!.TimeUnits;
        client.Send(new CombatMoveCommand(host, sim.World.Get<Position>(host).Cell + new int3(-2, 0, 0)));
        // Bea is done; the round waits for the host.
        client.Send(new CombatEndTurnCommand(bea.Id));
        t.Run(2f);
        Assert.Equal(hostTu, sim.Combat.Unit(host)!.TimeUnits);
        Assert.True(sim.Combat.Unit(mine)!.Acted);
        Assert.Equal(Side.Village, sim.Combat.Phase);
        Assert.Equal(1, sim.Combat.Round);

        t.Host.Send(new CombatEndTurnCommand(t.Host.Me.Id));
        for (var i = 0; i < 40 && sim.Combat.Active && sim.Combat.Round == 1; i++)
            t.Run(1f);
        Assert.True(sim.Combat.Round > 1 || !sim.Combat.Active, "the raiders took their turn and the round came round");

        // Every peer saw the same fight.
        t.Run(1f);
        Assert.Equal(sim.Combat.Log.Select(l => l.Text), client.Sim!.Combat.Log.Select(l => l.Text));
        Assert.Equal(sim.Combat.Round, client.Sim.Combat.Round);
        Assert.Equal(sim.Combat.Phase, client.Sim.Combat.Phase);
        Assert.Equal(sim.Combat.Units.Select(u => (u.Entity, u.TimeUnits, u.Acted)), client.Sim.Combat.Units.Select(u => (u.Entity, u.TimeUnits, u.Acted)));
        Assert.Equal(sim.StateHash(), client.Sim.StateHash());
        Assert.Equal(0, t.Host.Host!.Desyncs);
    }

    /// <summary>Host and Bea (a client with a character) with raiders beside the host: the fight is on, the village's turn.</summary>
    private static (Table T, CoopSession Client, PlayerIdentity Bea) Fight()
    {
        var t = new Table();
        t.Run(1f);
        var bea = new PlayerIdentity(Guid.NewGuid(), "Bea");
        var client = t.Join(bea);
        t.Run(1f);
        client.Send(new CreateCharacterCommand(bea.Id, "Bea", default, []));
        t.Run(1f);
        var host = Players.CharacterOf(t.Host.Sim!, t.Host.Me.Id);
        t.Host.Send(new DevRaidCommand(Defs.Value.AllRaiderKinds[0], 2, t.Host.Sim!.World.Get<Position>(host).Cell + new int3(8, 0, 0)));
        t.Run(2f);
        Assert.True(t.Host.Sim!.Combat.Active);
        return (t, client, bea);
    }

    [Fact]
    public void APlayerCanJoinMidFight()
    {
        var (t, client, bea) = Fight();
        var sim = t.Host.Sim!;
        // Something under way when they come in: Bea walking, the host's turn over.
        var mine = Players.CharacterOf(sim, bea.Id);
        client.Send(new CombatMoveCommand(mine, sim.World.Get<Position>(mine).Cell + new int3(1, 1, 0)));
        t.Host.Send(new CombatEndTurnCommand(t.Host.Me.Id));
        var cal = new PlayerIdentity(Guid.NewGuid(), "Cal");
        var late = t.Join(cal);
        t.Run(2f);
        Assert.True(late.Ready, late.Ended ?? "the snapshot never came");
        Assert.True(late.Sim!.Combat.Active);
        Assert.Equal(sim.Combat.Log.Select(l => l.Text), late.Sim.Combat.Log.Select(l => l.Text));

        // The fight goes on in step on all three.
        client.Send(new CombatEndTurnCommand(bea.Id));
        for (var i = 0; i < 40 && sim.Combat.Active && sim.Combat.Round == 1; i++)
            t.Run(1f);
        t.Run(1f);
        foreach (var peer in new[] { client, late })
        {
            Assert.Equal(sim.Combat.Log.Select(l => l.Text), peer.Sim!.Combat.Log.Select(l => l.Text));
            Assert.Equal(sim.StateHash(), peer.Sim.StateHash());
        }
        Assert.Equal(0, t.Host.Host!.Desyncs);
    }

    [Fact]
    public void ADesyncMidFightResyncs()
    {
        var dumps = Path.Combine(Path.GetTempPath(), "dw-desync-" + Guid.NewGuid().ToString("N"));
        var before = CoopSession.DumpDirectory;
        CoopSession.DumpDirectory = dumps;
        try
        {
            var (t, client, bea) = Fight();
            client.Sim!.Combat.Units[0].TimeUnits -= 5; // knock the client's fight off the host's
            t.Run(4f);
            Assert.True(t.Host.Host!.Desyncs >= 1, "the fight is in the checksum");
            Assert.True(client.Sim!.Combat.Active);
            t.Run(1f);
            Assert.Equal(t.Host.Sim!.StateHash(), client.Sim.StateHash());
            Assert.Equal(t.Host.Sim.Combat.Units.Select(u => u.TimeUnits), client.Sim.Combat.Units.Select(u => u.TimeUnits));
        }
        finally
        {
            CoopSession.DumpDirectory = before;
            if (Directory.Exists(dumps))
                Directory.Delete(dumps, recursive: true);
        }
    }

    [Fact]
    public void HostingKeepsTheStoryteller()
    {
        var sim = Simulation.CreateStandard(Defs.Value, 48, 48, 5, MapGenerator.RecommendedDepth);
        var center = MapGenerator.Generate(sim, new MapGenerator.Options { Villagers = 1 });
        sim.RebuildDerivedIfNeeded();
        SliceDemo.ApplyVillageDefaults(sim, center);
        EggBearerStart.Setup(sim, "Hosta");
        sim.Storyteller.SetDifficulty(sim, Defs.Value.Difficulty("Normal"));
        using var host = CoopSession.StartHost(sim, new PlayerIdentity(Guid.NewGuid(), "host"), "raids", new LoopbackListener());
        Assert.NotNull(host.Sim!.Storyteller.Difficulty); // raids come to co-op villages too
    }

    [Fact]
    public void PlayersCantSteerEachOther()
    {
        var t = new Table();
        var bea = new PlayerIdentity(Guid.NewGuid(), "Bea");
        var client = t.Join(bea);
        t.Run(1f);
        var host = Players.CharacterOf(t.Host.Sim!, t.Host.Me.Id);
        var at = t.Host.Sim!.World.Get<Position>(host).Cell;
        client.Send(new AvatarMoveCommand(host, new int2(1, 0)));
        client.Send(new DevHatchCommand());
        t.Run(2f);
        Assert.Equal(at, t.Host.Sim.World.Get<Position>(host).Cell);
        t.AssertInStep();
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
        var sim = Simulation.CreateStandard(Defs.Value, 48, 48, 5, MapGenerator.RecommendedDepth);
        var center = MapGenerator.Generate(sim, new MapGenerator.Options { Villagers = 1 });
        sim.RebuildDerivedIfNeeded();
        SliceDemo.ApplyVillageDefaults(sim, center);
        EggBearerStart.Setup(sim, "Hosta");
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
            var sim = Simulation.CreateStandard(Defs.Value, 48, 48, 5, MapGenerator.RecommendedDepth);
            var center = MapGenerator.Generate(sim, new MapGenerator.Options { Villagers = 1 });
            sim.RebuildDerivedIfNeeded();
            SliceDemo.ApplyVillageDefaults(sim, center);
            EggBearerStart.Setup(sim, "Hosta");
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
            var sim = Simulation.CreateStandard(Defs.Value, 48, 48, 5, MapGenerator.RecommendedDepth);
            var center = MapGenerator.Generate(sim, new MapGenerator.Options { Villagers = 1 });
            sim.RebuildDerivedIfNeeded();
            SliceDemo.ApplyVillageDefaults(sim, center);
            EggBearerStart.Setup(sim, "Hosta");
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
