using System.Reflection;
using System.Security.Cryptography;
using Narsil;
using Narsil.Net;
using DragonWorld.Sim;
using DragonWorld.Sim.Ai;
using DragonWorld.Sim.Core;
using DragonWorld.Sim.Defs;
using DragonWorld.Sim.Saving;
using DragonWorld.Sim.Systems;
using VillagePlayers = DragonWorld.Sim.Systems.Players;

namespace DragonWorld.Net;

/// <summary>
/// Drop-in co-op over Narsil's lockstep: the village's <see cref="Simulation"/> as an
/// <see cref="ILockstepGame"/>. Player input arrives as encoded <see cref="ISimCommand"/>s and is
/// checked by <see cref="CommandRules"/>; joins and leaves give players their characters back or
/// hand them to the AI (<see cref="Players"/>); a join's barrier is <see cref="AiSystem.InterruptAll"/>,
/// which is exactly what loading the snapshot does on the newcomer's side.
/// </summary>
public sealed class CoopSession : ILockstepGame, IDisposable
{
    public const string GameId = "DragonWorld";
    public const int DefaultPort = 47040;
    public const int DiscoveryPort = 47041;

    private readonly LanAdvertiser? _advertiser;

    /// <summary>The shared village. Replaced when a snapshot loads (joining, resync); null until the first.</summary>
    public Simulation? Sim { get; private set; }

    public DefDatabase Defs { get; }

    public PlayerIdentity Me { get; }

    /// <summary>Set when hosting, or once this client took over from a host that left (<see cref="BecameHost"/>).</summary>
    public LockstepHost? Host { get; private set; }

    /// <summary>Set while joined to someone else's session; null once this peer hosts.</summary>
    public LockstepClient? Client { get; private set; }

    /// <summary>The host left and this player took the session over: host-only duties (autosave, speed) are ours now.</summary>
    public event Action? BecameHost;

    public bool IsHost => Host != null;

    public string SessionName { get; }

    /// <summary>The port clients connect to (host only).</summary>
    public int Port { get; }

    /// <summary>The sim was replaced by a snapshot: anything holding entities or the old sim must refresh.</summary>
    public event Action? SimReplaced;

    /// <summary>Joins, leaves, desyncs, refusals: things to show as a toast.</summary>
    public event Action<string>? Notice;

    /// <summary>Playing turns (host: always; client: once the first snapshot is in).</summary>
    public bool Ready => Sim != null && (Host != null || Client!.State is LockstepClient.Phase.Playing or LockstepClient.Phase.Reconnecting);

    /// <summary>Why a client's session ended, once it has.</summary>
    public string? Ended => Client?.State == LockstepClient.Phase.Closed ? Client.CloseReason ?? "disconnected" : null;

    public IReadOnlyList<NetPlayer> Players => Host?.Players ?? Client!.Players;

    /// <summary>Game speed a client sees, as a multiple of normal (the host decides it).</summary>
    public float Speed => Host != null ? Host.StepsPerSecond / SimClock.TicksPerSecond : Client!.StepsPerSecond / SimClock.TicksPerSecond;

    public bool Stalled => Client?.Stalled == true;

    /// <summary>The client lost its link and is trying to take its place back.</summary>
    public bool Reconnecting => Client?.State == LockstepClient.Phase.Reconnecting;

    /// <summary>A client's view of the link: turns queued, the target it keeps, arrival jitter (ms), times it ran dry.</summary>
    public (int Buffered, int Target, float JitterMs, int Starvations)? ClientStats =>
        Client is { } c ? (c.Buffered, c.BufferTarget, c.ArrivalJitter * 1000f, c.Starvations) : null;

    /// <summary>Called on the host between simulation steps (autosave hooks in here).</summary>
    public Action? AfterStep { get; set; }

    /// <summary>Called on every peer just before a simulation step (render interpolation captures positions here).</summary>
    public Action? BeforeStep { get; set; }

    /// <summary>How far real time is past the last step played, 0..1, for drawing between steps.</summary>
    public float StepFraction => Host?.StepFraction ?? Client!.StepFraction;

    private CoopSession(DefDatabase defs, PlayerIdentity me, string sessionName)
    {
        Defs = defs;
        Me = me;
        SessionName = sessionName;
    }

    private CoopSession(Simulation sim, PlayerIdentity me, string sessionName, INetListener listener, int port, bool advertise) : this(sim.Defs, me, sessionName)
    {
        Sim = sim;
        Port = port;
        Host = new LockstepHost(this, Options(Defs), me.Id, me.Name, listener);
        HookHost();
        if (!advertise)
            return;
        try
        {
            _advertiser = new LanAdvertiser(DiscoveryPort);
        }
        catch (System.Net.Sockets.SocketException e)
        {
            Log.Engine.Warning($"LAN discovery off: {e.Message}");
        }
    }

    private CoopSession(DefDatabase defs, PlayerIdentity me, INetConnection connection, Func<CancellationToken, Task<INetConnection>>? reconnect)
        : this(defs, me, connection.Remote)
    {
        Client = new LockstepClient(this, Options(defs), me.Id, me.Name, connection, reconnect);
        Client.Notice += m => Notice?.Invoke(m);
        Client.Chat += OnChat;
        Client.Resyncing += (turn, state) => DumpDesync(turn, ("host", state), ("mine", SaveGame.Snapshot(Sim!)));
        // Joined through a relay: if the host vanishes, we may be the one to carry the session on.
        if (CoopRelay.TryGet(out var relay, out var relayPort))
        {
            Client.ClaimHost = async (handover, ct) => await RelayHost.ClaimAsync(relay, relayPort, GameId, handover, ct);
            Client.Promoted += TakeOver;
        }
    }

    private void HookHost()
    {
        Host!.Notice += m => Notice?.Invoke(m);
        Host.Chat += OnChat;
        Host.Resynced += (turn, state) => DumpDesync(turn, ("host", state));
    }

    /// <summary>Host migration: the host left and our claim on its join code won.</summary>
    private void TakeOver(INetListener listener)
    {
        Host = LockstepHost.Promote(Client!, listener);
        Client = null;
        HookHost();
        if (listener is RelayHost relay)
        {
            _relay = relay;
            Host.HandoverData = relay.Handover;
        }
        Host.StartPump();
        Log.Engine.Info($"net: took over hosting {SessionName}");
        BecameHost?.Invoke();
    }

    /// <summary>
    /// Open <paramref name="sim"/> to others on <paramref name="port"/> (0 picks a free one), and
    /// through the relay too when one is configured and <paramref name="online"/> (see <see cref="JoinCode"/>).
    /// </summary>
    public static CoopSession StartHost(Simulation sim, PlayerIdentity me, string sessionName, int port = DefaultPort, bool online = false)
    {
        var tcp = new TcpNetListener(port);
        var listener = new CompositeListener(tcp);
        var session = StartHost(sim, me, sessionName, listener, tcp.Port, advertise: true);
        if (online && CoopRelay.TryGet(out var relay, out var relayPort))
            session.OpenRelay(relay, relayPort, listener);
        // A real session keeps its clock off the host's frames: covering or minimising the
        // host's window (which throttles its frame rate) mustn't make every client stutter.
        session.Host!.StartPump();
        return session;
    }

    /// <summary>Host over any listener (tests use <see cref="LoopbackListener"/>).</summary>
    public static CoopSession StartHost(Simulation sim, PlayerIdentity me, string sessionName, INetListener listener, int port = 0, bool advertise = false)
    {
        sim.StrictJobs = false;
        return new CoopSession(sim, me, sessionName, listener, port, advertise);
    }

    /// <summary>Join over an open connection; the village arrives as a snapshot.</summary>
    /// <param name="reconnect">Opens a new connection to the same host, so a dropped link can resume (see <see cref="LockstepClient"/>).</param>
    public static CoopSession Join(DefDatabase defs, PlayerIdentity me, INetConnection connection,
        Func<CancellationToken, Task<INetConnection>>? reconnect = null) => new(defs, me, connection, reconnect);

    /// <summary>
    /// Session settings. Turns are sealed 30 times a second (two ticks each at normal speed):
    /// half of a turn is the average wait for a client's command, and a turn is a few bytes.
    /// </summary>
    public static LockstepOptions Options(DefDatabase defs) => new() { GameId = GameId, BuildTag = BuildTag(defs), TurnInterval = 1f / 30f };

    private static string? _buildTag;

    /// <summary>
    /// Code and content fingerprint: the game and sim assemblies, Narsil's sim half (the PRNG and
    /// pathing live there) (deterministic builds give the same id for the same source) plus every def. Anything else would desync.
    /// </summary>
    public static string BuildTag(DefDatabase defs)
    {
        if (_buildTag != null)
            return _buildTag;
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var asm in new[] { typeof(Simulation).Assembly, typeof(SimRandom).Assembly, typeof(CoopSession).Assembly })
            sha.AppendData(asm.ManifestModule.ModuleVersionId.ToByteArray());
        var dir = DevPaths.Defs ?? Path.Combine(AppContext.BaseDirectory, "data", "defs");
        if (Directory.Exists(dir))
            foreach (var file in Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
                sha.AppendData(File.ReadAllBytes(file));
        var version = typeof(CoopSession).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";
        return _buildTag = $"{version.Split('+')[0]}-{Convert.ToHexString(sha.GetHashAndReset())[..10].ToLowerInvariant()}";
    }

    /// <summary>Where desync dumps go (the log folder); tests point it elsewhere.</summary>
    public static string DumpDirectory { get; set; } = BuildInfo.LogDirectory;

    /// <summary>How many desyncs' dumps to keep: they're whole saves.</summary>
    private const int KeepDumps = 6;

    /// <summary>
    /// Keep the states around a resync as saves (<c>desync-&lt;turn&gt;-host</c>, and on a client
    /// <c>-mine</c>, its own state just before loading the host's), so a tester can send them and
    /// <c>--check-save</c> or a diff of the two finds what diverged. Never fails the session.
    /// </summary>
    private static void DumpDesync(long turn, params (string Who, byte[] State)[] states)
    {
        try
        {
            Directory.CreateDirectory(DumpDirectory);
            foreach (var (who, state) in states)
            {
                var path = Path.Combine(DumpDirectory, $"desync-{turn:D8}-{who}{SaveSlots.Extension}");
                File.WriteAllBytes(path, state);
                Log.Engine.Warning($"net: resync at turn {turn}: wrote {path}");
            }
            var all = Directory.GetFiles(DumpDirectory, "desync-*").Order(StringComparer.Ordinal).ToArray();
            foreach (var old in all.Take(Math.Max(0, all.Length - KeepDumps * 2)))
                File.Delete(old);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Engine.Warning($"net: couldn't write desync dump: {ex.Message}");
        }
    }

    /// <summary>Send an action from this player; it applies when its turn comes round.</summary>
    public void Send(ISimCommand command)
    {
        var bytes = CommandCodec.Encode(command, Defs);
        if (Host != null)
            Host.Send(bytes);
        else
            Client!.Send(bytes);
    }

    /// <summary>
    /// Once per frame. The host passes the game speed in steps per second (0 pauses); clients
    /// follow the host's.
    /// </summary>
    public void Update(float dt, float stepsPerSecond)
    {
        PollRelay();
        if (Host != null)
        {
            Host.StepsPerSecond = stepsPerSecond;
            Host.Update(dt);
            _advertiser?.Update(dt, () => LanBeacon.Encode(GameId, Host.Options.BuildTag, SessionName, Host.Players.Count, Host.Options.MaxPlayers, Port));
        }
        else
        {
            Client!.Update(dt);
        }
    }

    /// <summary>Chat lines seen this session, oldest first (at most <see cref="ChatKept"/>).</summary>
    public IReadOnlyList<(string From, string Text)> ChatLog => _chatLog;

    public const int ChatKept = 100;

    private readonly List<(string From, string Text)> _chatLog = [];

    /// <summary>A chat line arrived (ours included, once relayed).</summary>
    public event Action<string, string>? ChatReceived;

    /// <summary>Sends a chat line to everyone; Narsil checks and relays it outside the simulation.</summary>
    public void SendChat(string text)
    {
        if (Host != null)
            Host.SendChat(text);
        else
            Client!.SendChat(text);
    }

    private void OnChat(NetPlayer from, string text)
    {
        _chatLog.Add((from.Name, text));
        if (_chatLog.Count > ChatKept)
            _chatLog.RemoveAt(0);
        ChatReceived?.Invoke(from.Name, text);
    }

    public void Kick(int slot) => Host?.Kick(slot, "removed by the host");

    // ---------------- ILockstepGame ----------------

    void ILockstepGame.Barrier()
    {
        // Movers' paths aren't saved: a move under way in a fight plays out to its end (with its
        // reactions) on every peer, so the snapshot holds the fight whole.
        if (Sim!.Combat.Active)
            Sim.Combat.FinishAnimation(Sim);
        AiSystem.InterruptAll(Sim);
    }

    void ILockstepGame.PlayerJoined(NetPlayer player)
    {
        if (player.IsHost)
            // Hosting a save: anyone who was playing when it was saved is away until they rejoin.
            VillagePlayers.ReleaseAllBut(Sim!, player.Id);
        VillagePlayers.Join(Sim!, player.Id, adoptLooseAvatar: player.IsHost);
    }

    void ILockstepGame.PlayerAway(NetPlayer player) => VillagePlayers.Away(Sim!, player.Id);

    void ILockstepGame.PlayerLeft(NetPlayer player) => VillagePlayers.Leave(Sim!, player.Id);

    void ILockstepGame.ApplyCommand(NetPlayer issuer, ReadOnlySpan<byte> bytes)
    {
        ISimCommand command;
        try
        {
            command = CommandCodec.Decode(bytes, Defs);
        }
        catch (InvalidDataException e)
        {
            Log.Engine.Warning($"net: dropped a malformed command from slot {issuer.Slot}: {e.Message}");
            return;
        }
        if (CommandRules.Refusal(Sim!, command, issuer.Id, issuer.IsHost) is { } why)
        {
            Log.Engine.Info($"net: refused {command.GetType().Name} from slot {issuer.Slot}: {why}");
            return;
        }
        Sim!.ApplyNow(command);
    }

    void ILockstepGame.Step()
    {
        // A fight plays out a step at a time on every peer (orders are commands, its pacing
        // counts steps), while the world's clock waits between rounds (Encounter.EndPhase).
        if (Sim!.Combat.Active)
        {
            Sim.Combat.Advance(Sim);
            return;
        }
        BeforeStep?.Invoke();
        Sim.Tick();
        AfterStep?.Invoke();
    }

    byte[] ILockstepGame.Snapshot() => SaveGame.Snapshot(Sim!);

    void ILockstepGame.LoadSnapshot(byte[] state)
    {
        var (sim, _) = SaveGame.Read(state, Defs);
        sim.StrictJobs = false;
        Sim = sim;
        SimReplaced?.Invoke();
    }

    ulong ILockstepGame.Checksum() => Sim!.StateHash(withMap: false);

    // ---------------- online hosting through a relay ----------------

    private Task<RelayHost>? _relayOpening;
    private CompositeListener? _relayInto;
    private RelayHost? _relay;
    private readonly CancellationTokenSource _relayCancel = new();

    /// <summary>The code friends type to join through the relay; null until it's open (or when it isn't).</summary>
    public string? JoinCode => _relay is { Connected: true } r ? r.Code : null;

    /// <summary>What online hosting is doing, for the Players window: null when it wasn't asked for.</summary>
    public string? RelayStatus { get; private set; }

    private void OpenRelay(string relay, int port, CompositeListener into)
    {
        _relayInto = into;
        RelayStatus = $"Opening online play through {relay}...";
        _relayOpening = RelayHost.OpenAsync(relay, port, GameId, _relayCancel.Token);
    }

    private void PollRelay()
    {
        if (_relayOpening is { IsCompleted: true } task)
        {
            _relayOpening = null;
            if (task.IsCompletedSuccessfully)
            {
                _relay = task.Result;
                _relayInto!.Add(_relay);
                Host!.HandoverData = _relay.Handover; // so a player can carry on if we vanish
                RelayStatus = null;
                Notice?.Invoke($"Online: friends can join with the code {_relay.Code}.");
                Log.Engine.Info($"Hosting online through the relay, code {_relay.Code}");
            }
            else if (!task.IsCanceled)
            {
                RelayStatus = $"Online play unavailable: {task.Exception?.GetBaseException().Message}";
                Log.Engine.Warning(RelayStatus);
            }
        }
        if (_relay is { Connected: false } lost && RelayStatus == null)
        {
            RelayStatus = $"Lost the relay ({lost.CloseReason}); players on your network can still join.";
            Log.Engine.Warning(RelayStatus);
        }
    }

    /// <summary>Tests: the host's machine vanishes. Its relay link drops with no goodbye to anyone.</summary>
    internal void VanishForTests()
    {
        _relay?.Dispose();
        Host?.Dispose(); // its goodbyes go down the dead link
    }

    public void Dispose()
    {
        _relayCancel.Cancel();
        _relayOpening?.ContinueWith(t => t.Result.Dispose(), TaskContinuationOptions.OnlyOnRanToCompletion);
        Host?.Dispose();
        Client?.Dispose();
        _advertiser?.Dispose();
    }
}
